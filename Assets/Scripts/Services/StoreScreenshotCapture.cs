#if UNITY_STANDALONE_OSX || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Menu;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>Command-line capture harness for real store-listing screenshots.</summary>
    public sealed class StoreScreenshotCapture : MonoBehaviour
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private string _output;
        private int _width;
        private int _height;
        private int _scale;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.productName.Contains("Screenshot")) return;
            Debug.Log("[StoreCapture] Starting screenshot automation.");
            var go = new GameObject("StoreScreenshotCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<StoreScreenshotCapture>();
            capture.StartCoroutine(capture.Run());
        }

        private IEnumerator Run()
        {
            Debug.Log("[StoreCapture] Capture coroutine started.");
            _output = Argument("-captureOutput");
            _width = IntegerArgument("-captureWidth", 540);
            _height = IntegerArgument("-captureHeight", 960);
            _scale = IntegerArgument("-captureScale", 2);
            Directory.CreateDirectory(_output);

            Screen.SetResolution(_width, _height, FullScreenMode.Windowed);
            for (int i = 0; i < 20; i++) yield return null;

            AppRoot app = null;
            yield return WaitUntil(
                () =>
                {
                    app = FindSceneObject<AppRoot>();
                    return app != null;
                },
                15f);
            if (app == null)
            {
                Debug.LogError("[StoreCapture] AppRoot did not initialize.");
                Application.Quit(2);
                yield break;
            }

            yield return new WaitForSecondsRealtime(4f);
            yield return Capture("01_home.bmp");

            SettingsPanel settings = FindSceneObject<SettingsPanel>();
            if (settings != null)
            {
                settings.Show();
                yield return new WaitForSecondsRealtime(0.55f);
                yield return Capture("06_settings.bmp");
                settings.Hide();
                yield return new WaitForSecondsRealtime(0.55f);
            }

            Invoke(app, "ShowLeaderboard");
            LeaderboardController leaderboard = FindSceneObject<LeaderboardController>();
            yield return null;
            PopulateLeaderboard(leaderboard);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Capture("05_leaderboard.bmp");

            Invoke(app, "ShowHome");
            yield return new WaitForSecondsRealtime(0.5f);
            Invoke(app, "StartLevel", Difficulty.Easy, 1);

            GameController game = null;
            yield return WaitUntil(
                () =>
                {
                    game = FindSceneObject<GameController>();
                    return game != null && game.Level != null &&
                           game.gameObject.activeInHierarchy;
                },
                25f);
            if (game == null || game.Level == null)
            {
                Debug.LogError("[StoreCapture] Gameplay did not initialize.");
                Application.Quit(3);
                yield break;
            }

            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture("02_gameplay.bmp");

            BoardView3D board = FindSceneObject<BoardView3D>();
            ApplySolutionPreview(game, board, 2);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("03_painted_route.bmp");

            PaintAll(game.Level, board);
            CompleteController complete = FindSceneObject<CompleteController>();
            if (complete != null)
            {
                complete.Show(game.Level.Difficulty, game.Level.Index);
                yield return new WaitForSecondsRealtime(0.38f);
                yield return Capture("04_level_complete.bmp");
            }

            Debug.Log($"[StoreCapture] Finished {_output}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        private IEnumerator Capture(string filename)
        {
            string path = Path.Combine(_output, filename);
            if (File.Exists(path)) File.Delete(path);
            yield return new WaitForEndOfFrame();
            var source = new Texture2D(
                Screen.width, Screen.height, TextureFormat.RGB24, false);
            source.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
            source.Apply();

            Texture2D output = source;
            RenderTexture scaled = null;
            RenderTexture previous = RenderTexture.active;
            if (_scale > 1)
            {
                scaled = RenderTexture.GetTemporary(
                    Screen.width * _scale, Screen.height * _scale, 0,
                    RenderTextureFormat.ARGB32);
                Graphics.Blit(source, scaled);
                RenderTexture.active = scaled;
                output = new Texture2D(
                    scaled.width, scaled.height, TextureFormat.RGB24, false);
                output.ReadPixels(
                    new Rect(0f, 0f, scaled.width, scaled.height), 0, 0);
                output.Apply();
            }

            WriteBitmap(output, path);
            RenderTexture.active = previous;
            if (scaled != null) RenderTexture.ReleaseTemporary(scaled);
            if (output != source) Destroy(output);
            Destroy(source);
            Debug.Log("[StoreCapture] Wrote " + path);
        }

        private static void WriteBitmap(Texture2D texture, string path)
        {
            Color32[] pixels = texture.GetPixels32();
            int width = texture.width;
            int height = texture.height;
            int rowSize = (width * 3 + 3) & ~3;
            int pixelBytes = rowSize * height;

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)'B');
            writer.Write((byte)'M');
            writer.Write(54 + pixelBytes);
            writer.Write(0);
            writer.Write(54);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write((short)24);
            writer.Write(0);
            writer.Write(pixelBytes);
            writer.Write(2835);
            writer.Write(2835);
            writer.Write(0);
            writer.Write(0);

            int padding = rowSize - width * 3;
            for (int row = 0; row < height; row++)
            {
                int offset = row * width;
                for (int col = 0; col < width; col++)
                {
                    Color32 pixel = pixels[offset + col];
                    writer.Write(pixel.b);
                    writer.Write(pixel.g);
                    writer.Write(pixel.r);
                }
                for (int pad = 0; pad < padding; pad++) writer.Write((byte)0);
            }
        }

        private static void ApplySolutionPreview(
            GameController game, BoardView3D board, int moveCount)
        {
            if (game?.Level == null || board == null) return;
            Level level = game.Level;
            var painted = new HashSet<Position> { level.Spawn };
            Position ball = level.Spawn;
            var movement = new MovementSystem();
            var solver = new Solver(movement);
            List<SwipeDirection> solution =
                solver.SolutionFrom(level, ball, painted, 200000);
            if (solution == null) return;

            int moves = Mathf.Min(moveCount, solution.Count);
            for (int i = 0; i < moves; i++)
            {
                (Position stop, List<Position> path) =
                    movement.Slide(level, ball, solution[i]);
                foreach (Position position in path)
                {
                    painted.Add(position);
                    board.SetPainted(position, false);
                }
                ball = stop;
            }

            board.SetBallCell(ball);
            UIManager hud = FindSceneObject<UIManager>();
            hud?.SetProgress(painted.Count / (float)level.TotalPaintable);
        }

        private static void PaintAll(Level level, BoardView3D board)
        {
            if (level == null || board == null) return;
            Position last = level.Spawn;
            for (int row = 0; row < level.Rows; row++)
            {
                for (int col = 0; col < level.Cols; col++)
                {
                    if (level.Grid[row, col] != Tile.Floor) continue;
                    last = new Position(row, col);
                    board.SetPainted(last, false);
                }
            }
            board.SetBallCell(last);
            FindSceneObject<UIManager>()?.SetProgress(1f);
        }

        private static void PopulateLeaderboard(LeaderboardController controller)
        {
            if (controller == null) return;
            string[] names =
            {
                "MazeMia", "TileTamer", "ColorDash", "RollingNova", "PaintPilot",
                "SwiftSphere", "PuzzlePanda", "RouteMaster", "NeonTrail", "CleverCube",
                "LunaRoll", "SplashFox", "MazeAce", "TileWizard", "InkRunner",
                "GlideGuru", "PixelPath", "BrightBall", "QuietQuest", "PathFinder",
                "RollLogic", "MazeBloom", "ColorComet", "TileScout", "PaintPulse"
            };
            int[] levels =
            {
                195, 187, 179, 171, 163, 155, 147, 139, 131, 123,
                115, 107, 99, 91, 83, 75, 67, 59, 51, 43, 37, 32, 28, 24, 20
            };
            var entries = new List<LeaderboardEntry>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                entries.Add(new LeaderboardEntry
                {
                    UserId = "store-capture-" + i,
                    DisplayName = names[i],
                    HighestLevel = levels[i],
                    ReachedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                    IsCurrentUser = false
                });
            }

            var ownEntry = new LeaderboardEntry
            {
                UserId = "store-capture-current",
                DisplayName = "MazeRocker",
                HighestLevel = 18,
                ReachedAtUtc = DateTime.UtcNow,
                IsCurrentUser = true
            };
            Invoke(controller, "ApplyResult", new LeaderboardLoadResult
            {
                Entries = entries,
                OwnEntry = ownEntry,
                OwnRank = 446,
                IsFromCache = false
            });
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private static void Invoke(object target, string method, params object[] args)
        {
            target.GetType().GetMethod(method, PrivateInstance)?.Invoke(target, args);
        }

        private static T FindSceneObject<T>() where T : Component
        {
            return Resources.FindObjectsOfTypeAll<T>()
                .FirstOrDefault(item => item != null && item.gameObject.scene.IsValid());
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Missing command-line argument " + name);
            return args[index + 1];
        }

        private static int IntegerArgument(string name, int fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length &&
                   int.TryParse(args[index + 1], out int value)
                ? value
                : fallback;
        }
    }
}
#endif
