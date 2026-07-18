using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Lightweight decorative gameplay preview for the home screen. It builds a tiny
    /// uGUI maze, rolls a white ball through a fixed route, and paints each visited
    /// tile using the upcoming level's real paint colour. It never touches game state.
    /// </summary>
    public sealed class HomeMazePreview : MonoBehaviour
    {
        private const int Cols = 6;
        private const int Rows = 4;
        private const float Tile = 88f;
        private const float Gap = 8f;
        private const float StepDuration = 0.16f;

        private static readonly Vector2Int[] Route =
        {
            new(0, 0), new(1, 0), new(2, 0),
            new(2, 1), new(2, 2), new(1, 2),
            new(0, 2), new(0, 3),
        };

        private readonly Dictionary<int, Image> _tiles = new();
        private RectTransform _board;
        private RectTransform _ball;
        private Coroutine _loop;
        private Color _paint;
        private bool _built;

        public void Build(RectTransform parent, int levelNumber)
        {
            _board = parent;
            _paint = Theme.Paint(levelNumber);
            BuildTiles();
            BuildBall();
            _built = true;
            ResetPreview();
            RestartLoop();
        }

        public void Refresh(int levelNumber)
        {
            _paint = Theme.Paint(levelNumber);
            if (!_built) return;
            ResetPreview();
            RestartLoop();
        }

        private void BuildTiles()
        {
            bool[,] floor =
            {
                { true,  true,  true,  false, false, false },
                { false, false, true,  false, true,  true  },
                { true,  true,  true,  true,  true,  false },
                { true,  false, false, false, true,  true  },
            };

            float width = Cols * Tile + (Cols - 1) * Gap;
            float height = Rows * Tile + (Rows - 1) * Gap;
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    if (!floor[row, col]) continue;
                    var tile = UiKit.Image(_board, $"Tile_{row}_{col}", Theme.TileTop,
                        SpriteFactory.RoundedRect(40, 8));
                    tile.raycastTarget = false;
                    var rt = tile.rectTransform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = Vector2.one * Tile;
                    rt.anchoredPosition = new Vector2(
                        col * (Tile + Gap) - width * 0.5f + Tile * 0.5f,
                        height * 0.5f - Tile * 0.5f - row * (Tile + Gap));
                    _tiles[Key(row, col)] = tile;
                }
            }
        }

        private void BuildBall()
        {
            var shadow = UiKit.Image(_board, "BallShadow", new Color(0f, 0f, 0f, 0.22f),
                SpriteFactory.Circle());
            shadow.raycastTarget = false;
            var shadowRt = shadow.rectTransform;
            shadowRt.anchorMin = shadowRt.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRt.pivot = new Vector2(0.5f, 0.5f);
            shadowRt.sizeDelta = new Vector2(62f, 42f);

            var ball = UiKit.Image(_board, "Ball", Theme.Ball, SpriteFactory.Circle());
            ball.raycastTarget = false;
            _ball = ball.rectTransform;
            _ball.anchorMin = _ball.anchorMax = new Vector2(0.5f, 0.5f);
            _ball.pivot = new Vector2(0.5f, 0.5f);
            _ball.sizeDelta = Vector2.one * 58f;

            // Keep the shadow parented under the board but drive it through the ball's
            // position in the animation loop.
            shadowRt.SetAsLastSibling();
            _ball.SetAsLastSibling();
            _shadow = shadowRt;
        }

        private RectTransform _shadow;

        private void ResetPreview()
        {
            foreach (var tile in _tiles.Values) tile.color = Theme.TileTop;
            Vector2 start = CellPosition(Route[0]);
            _ball.anchoredPosition = start;
            _shadow.anchoredPosition = start + new Vector2(5f, -8f);
            if (_tiles.TryGetValue(Key(Route[0].y, Route[0].x), out var first))
                first.color = _paint;
        }

        private void RestartLoop()
        {
            if (!isActiveAndEnabled) return;
            if (_loop != null) StopCoroutine(_loop);
            _loop = StartCoroutine(Loop());
        }

        private IEnumerator Loop()
        {
            yield return WaitUnscaled(0.55f);
            while (true)
            {
                for (int i = 1; i < Route.Length; i++)
                {
                    Vector2 from = _ball.anchoredPosition;
                    Vector2 to = CellPosition(Route[i]);
                    float elapsed = 0f;
                    while (elapsed < StepDuration)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        float k = Mathf.Clamp01(elapsed / StepDuration);
                        float eased = 1f - (1f - k) * (1f - k);
                        Vector2 p = Vector2.LerpUnclamped(from, to, eased);
                        _ball.anchoredPosition = p;
                        _shadow.anchoredPosition = p + new Vector2(5f, -8f);
                        yield return null;
                    }

                    if (_tiles.TryGetValue(Key(Route[i].y, Route[i].x), out var tile))
                        tile.color = _paint;
                    yield return WaitUnscaled(0.07f);
                }

                yield return WaitUnscaled(0.85f);
                ResetPreview();
                yield return WaitUnscaled(0.45f);
            }
        }

        private static IEnumerator WaitUnscaled(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private Vector2 CellPosition(Vector2Int cell)
        {
            float width = Cols * Tile + (Cols - 1) * Gap;
            float height = Rows * Tile + (Rows - 1) * Gap;
            return new Vector2(
                cell.x * (Tile + Gap) - width * 0.5f + Tile * 0.5f,
                height * 0.5f - Tile * 0.5f - cell.y * (Tile + Gap));
        }

        private static int Key(int row, int col) => row * Cols + col;

        private void OnEnable()
        {
            if (_built) RestartLoop();
        }

        private void OnDisable()
        {
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
        }
    }
}
