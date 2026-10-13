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
    /// <summary>
    /// Command-line demo harness that drives real UI/gameplay and dumps a
    /// timed JPEG frame sequence for Play Console video encoding.
    /// </summary>
    public sealed class StoreDemoVideoCapture : MonoBehaviour
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const float CaptureFps = 15f;

        private string _output;
        private string _framesDir;
        private int _width;
        private int _height;
        private int _frameIndex;
        private bool _recording;
        private Coroutine _captureLoop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (!ScreenshotPlayerMode.IsActive ||
                !ScreenshotPlayerMode.HasCommandLineArgument(
                    arguments, "-demoVideoOutput"))
                return;
            Debug.Log("[DemoVideo] Starting demo video automation.");
            var go = new GameObject("StoreDemoVideoCapture");
            DontDestroyOnLoad(go);
            var capture = go.AddComponent<StoreDemoVideoCapture>();
            capture.StartCoroutine(capture.Run());
        }

        private IEnumerator Run()
        {
            Debug.Log("[DemoVideo] Capture coroutine started.");
            Application.runInBackground = true;
            _output = Argument("-demoVideoOutput");
            _width = IntegerArgument("-captureWidth", 1080);
            _height = IntegerArgument("-captureHeight", 1920);
            _framesDir = Path.Combine(_output, "frames");
            if (Directory.Exists(_framesDir))
                Directory.Delete(_framesDir, true);
            Directory.CreateDirectory(_framesDir);

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
                Debug.LogError("[DemoVideo] AppRoot did not initialize.");
                Application.Quit(2);
                yield break;
            }

            _recording = true;
            _captureLoop = StartCoroutine(CaptureLoop());

            yield return new WaitForSecondsRealtime(4.5f);

            HowToPlayController howTo = FindSceneObject<HowToPlayController>();
            if (howTo != null)
            {
                howTo.Show();
                yield return new WaitForSecondsRealtime(4f);
                howTo.Hide();
                yield return new WaitForSecondsRealtime(0.8f);
            }

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
                Debug.LogError("[DemoVideo] Gameplay did not initialize.");
                StopRecording();
                Application.Quit(3);
                yield break;
            }

            yield return new WaitForSecondsRealtime(1.8f);
            yield return PlaySolution(game);
            yield return WaitUntil(() => game.IsComplete, 20f);
            yield return new WaitForSecondsRealtime(3f);

            Invoke(app, "ShowHome");
            yield return new WaitForSecondsRealtime(4f);

            Invoke(app, "ShowLeaderboard");
            LeaderboardController leaderboard =
                FindSceneObject<LeaderboardController>();
            yield return null;
            PopulateLeaderboard(leaderboard);
            yield return new WaitForSecondsRealtime(4f);

            Invoke(app, "ShowHome");
            yield return new WaitForSecondsRealtime(0.8f);

            BallShopPanel shop = FindSceneObject<BallShopPanel>();
            if (shop != null)
            {
                shop.Show();
                yield return new WaitForSecondsRealtime(4f);
                shop.Hide();
                yield return new WaitForSecondsRealtime(0.6f);
            }

            SettingsPanel settings = FindSceneObject<SettingsPanel>();
            if (settings != null)
            {
                settings.Show();
                yield return new WaitForSecondsRealtime(3.5f);
                settings.Hide();
                yield return new WaitForSecondsRealtime(0.6f);
            }

            Invoke(app, "ShowAuth");
            yield return new WaitForSecondsRealtime(4f);

            StopRecording();
            yield return new WaitForSecondsRealtime(0.4f);
            Debug.Log(
                $"[DemoVideo] Finished {_frameIndex} frames in {_framesDir}");
            Application.Quit(0);
        }

        private IEnumerator PlaySolution(GameController game)
        {
            Level level = game.Level;
            var painted = new HashSet<Position> { level.Spawn };
            var solver = new Solver(new MovementSystem());
            List<SwipeDirection> solution =
                solver.SolutionFrom(level, level.Spawn, painted, 200000);
            if (solution == null || solution.Count == 0)
            {
                Debug.LogError("[DemoVideo] No solution for Easy #1.");
                yield break;
            }

            MethodInfo swipe = typeof(GameController).GetMethod(
                "HandleSwipe", PrivateInstance);
            FieldInfo animating = typeof(GameController).GetField(
                "_animating", PrivateInstance);
            if (swipe == null || animating == null)
            {
                Debug.LogError("[DemoVideo] Could not reflect GameController.");
                yield break;
            }

            foreach (SwipeDirection direction in solution)
            {
                yield return WaitUntil(
                    () => !(bool)animating.GetValue(game), 10f);
                if (game.IsComplete) yield break;
                swipe.Invoke(game, new object[] { direction });
                yield return null;
                yield return WaitUntil(
                    () => !(bool)animating.GetValue(game), 10f);
                yield return new WaitForSecondsRealtime(0.28f);
            }
        }

        private IEnumerator CaptureLoop()
        {
            float interval = 1f / CaptureFps;
            while (_recording)
            {
                yield return new WaitForEndOfFrame();
                if (!_recording) yield break;
                CaptureFrame();
                float next = Time.realtimeSinceStartup + interval;
                while (_recording && Time.realtimeSinceStartup < next)
                    yield return null;
            }
        }

        private void StopRecording()
        {
            _recording = false;
            if (_captureLoop != null)
            {
                StopCoroutine(_captureLoop);
                _captureLoop = null;
            }
        }

        private void CaptureFrame()
        {
            var source = new Texture2D(
                Screen.width, Screen.height, TextureFormat.RGB24, false);
            source.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
            source.Apply();
            string path = Path.Combine(
                _framesDir, $"frame_{_frameIndex:D06}.bmp");
            WriteBitmap(source, path);
            Destroy(source);
            _frameIndex++;
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
                    UserId = "demo-capture-" + i,
                    DisplayName = names[i],
                    HighestLevel = levels[i],
                    ReachedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                    IsCurrentUser = false
                });
            }

            var ownEntry = new LeaderboardEntry
            {
                UserId = "demo-capture-current",
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
