using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Reference-style level-complete celebration: a violet dim wash, a big
    /// animated praise word ("FANTASTIC!") that pops in with an overshoot, and a
    /// swirling ring of star particles. It auto-advances to the next level when
    /// the flourish finishes (no buttons), matching the reference flow.
    /// </summary>
    public sealed class CompleteController : MonoBehaviour
    {
        public event Action Next;
        public event Action Home;
        public event Action Replay;

        private static readonly string[] Praise =
            { "FANTASTIC!", "AWESOME!", "GREAT!", "PERFECT!", "BRILLIANT!", "NICE!" };
        private static readonly string[] DailyPraise =
            { "CRUSHED!", "NAILED!", "CLUTCH!", "FLAWLESS!", "EPIC!", "BOOM!" };
        private static int _lastDailyPraiseIndex = -1;

        private const int SparkPoolSize = 36;
        private static readonly Color[] Sparks =
        {
            new Color(0.96f, 0.78f, 0.29f), // gold
            new Color(0.90f, 0.42f, 0.84f), // magenta
            new Color(0.98f, 0.45f, 0.72f), // pink
            Color.white,
        };

        private RectTransform _root;
        private RectTransform _particleLayer;
        private Image _dim;
        private Text _word;
        private Text _detail;
        private Text _coinsLabel;
        private GameObject _dailyActions;
        private Button _dailyReplayButton;
        private Image _dailyReplayImage;
        private Text _dailyReplayLabel;
        private DailyLeaderboardPanel _dailyLeaderboardPanel;
        private Coroutine _running;
        private Color _dimColor;
        private readonly Stack<Image> _availableSparks = new();
        private readonly List<Image> _sparkPool = new();

        public int SparkPoolCount => _sparkPool.Count;
        public int ActiveSparkCount => SparkPoolSize - _availableSparks.Count;

        public void Build(RectTransform parent)
        {
            _dimColor = CompletionDim();
            _dim = UiKit.Image(parent, "CompleteDim", _dimColor);
            _root = _dim.rectTransform;
            UiKit.Stretch(_root);

            // Particle layer sits under the word so stars render behind the text.
            var layer = UiKit.Image(_root, "Sparks", new Color(0, 0, 0, 0));
            _particleLayer = layer.rectTransform;
            UiKit.Stretch(_particleLayer);
            layer.raycastTarget = false;

            _word = UiKit.Text(_root, "Praise", "FANTASTIC!", 124, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(_word.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(920, 220), new Vector2(0f, 22f));
            _word.raycastTarget = false;

            _detail = UiKit.Text(_root, "Detail", "LEVEL COMPLETE", 29,
                Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(_detail.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(760f, 58f), new Vector2(0f, -108f));
            _detail.raycastTarget = false;

            _coinsLabel = UiKit.Text(_root, "CoinsReward", string.Empty, 34,
                Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(_coinsLabel.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(480f, 48f), new Vector2(0f, -168f));
            _coinsLabel.raycastTarget = false;
            _coinsLabel.gameObject.SetActive(false);

            BuildDailyActions();
            PrewarmSparkPool();
            BuildDailyLeaderboardPanel();
        }

        public void Show(Difficulty difficulty, int levelIndex, int coinsGranted = 0)
        {
            StopAllCoroutines();
            ResetSparkPool();
            ShowCelebrationView();
            _dailyLeaderboardPanel.Hide();
            gameObject.SetActive(true);
            _dimColor = CompletionDim();
            _word.color = Color.white;
            _detail.color = Theme.Gold;
            _word.fontSize = 124;
            _word.text = Praise[UnityEngine.Random.Range(0, Praise.Length)];
            _detail.text = $"{Theme.Name(difficulty).ToUpperInvariant()}  ·  LEVEL {levelIndex} COMPLETE";
            _detail.fontSize = 29;
            UiKit.Anchor(
                _detail.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(760f, 58f),
                new Vector2(0f, -108f));
            SetCoinsReward(coinsGranted, -168f);
            _dailyActions.SetActive(false);
            _running = StartCoroutine(Celebrate(true));
        }

        public void ShowDaily(DailyPuzzleCompletion completion, int coinsGranted = 0)
        {
            if (completion == null) return;
            StopAllCoroutines();
            ResetSparkPool();
            ShowCelebrationView();
            _dailyLeaderboardPanel.Hide();
            gameObject.SetActive(true);
            _dimColor = CompletionDim();
            _word.color = Color.white;
            _detail.color = Theme.Gold;
            _word.fontSize = 94;
            _word.text = NextDailyPraise();
            _detail.fontSize = 26;
            _detail.text = BuildDailyDetail(completion);
            UiKit.Anchor(
                _detail.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(880f, 150f),
                new Vector2(0f, -142f));
            SetCoinsReward(coinsGranted, -230f);
            _dailyActions.SetActive(true);
            bool canReplay = !completion.Status.IsLocked;
            _dailyReplayButton.interactable = canReplay;
            _dailyReplayImage.color = canReplay
                ? Theme.Gold
                : new Color(
                    Theme.Gold.r,
                    Theme.Gold.g,
                    Theme.Gold.b,
                    0.35f);
            _dailyReplayLabel.text = canReplay ? "REPLAY" : "LOCKED";
            _dailyLeaderboardPanel.Prepare(completion.Status.IsLocked);
            _running = StartCoroutine(Celebrate(false));
        }

        private void SetCoinsReward(int coinsGranted, float anchoredY)
        {
            if (_coinsLabel == null) return;
            if (coinsGranted <= 0)
            {
                _coinsLabel.gameObject.SetActive(false);
                return;
            }

            _coinsLabel.gameObject.SetActive(true);
            _coinsLabel.text = $"+{coinsGranted} COINS";
            _coinsLabel.color = Theme.Gold;
            UiKit.Anchor(
                _coinsLabel.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(480f, 48f),
                new Vector2(0f, anchoredY));
        }

        public void SetDailyLeaderboardResult(DailyLeaderboardResult result) =>
            _dailyLeaderboardPanel?.SetResult(result);

        private static string NextDailyPraise()
        {
            int index = _lastDailyPraiseIndex < 0
                ? UnityEngine.Random.Range(0, DailyPraise.Length)
                : UnityEngine.Random.Range(0, DailyPraise.Length - 1);
            if (_lastDailyPraiseIndex >= 0 && index >= _lastDailyPraiseIndex)
                index++;
            _lastDailyPraiseIndex = index;
            return DailyPraise[index];
        }

        private IEnumerator Celebrate(bool autoAdvance)
        {
            var wordRt = _word.rectTransform;
            wordRt.localScale = Vector3.one * 0.3f;
            SetAlpha(_word, 0f);
            SetAlpha(_detail, 0f);
            if (_coinsLabel != null && _coinsLabel.gameObject.activeSelf)
                SetAlpha(_coinsLabel, 0f);
            _dim.color = new Color(_dimColor.r, _dimColor.g, _dimColor.b, 0f);

            // Spark ring waves around the word.
            StartCoroutine(SparkWave(20, 120f, 0f));
            StartCoroutine(SparkWave(16, 210f, 0.12f));

            // Pop-in: fade the dim while the word scales up with an overshoot.
            float t = 0f;
            const float popDur = 0.30f;
            while (t < popDur)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(t / popDur);
                wordRt.localScale = Vector3.one * EaseOutBack(x);
                SetAlpha(_word, Mathf.Clamp01(x * 1.6f));
                SetAlpha(_detail, Mathf.Clamp01((x - 0.25f) * 1.5f));
                if (_coinsLabel != null && _coinsLabel.gameObject.activeSelf)
                    SetAlpha(_coinsLabel, Mathf.Clamp01((x - 0.35f) * 1.6f));
                _dim.color = new Color(
                    _dimColor.r, _dimColor.g, _dimColor.b, _dimColor.a * x);
                yield return null;
            }
            wordRt.localScale = Vector3.one;
            SetAlpha(_word, 1f);
            SetAlpha(_detail, 1f);

            // A gentle breathing pulse during the hold.
            float hold = 0f;
            while (hold < 0.90f)
            {
                hold += Time.unscaledDeltaTime;
                wordRt.localScale = Vector3.one * (1f + 0.018f * Mathf.Sin(hold * 8f));
                yield return null;
            }

            if (!autoAdvance)
            {
                wordRt.localScale = Vector3.one;
                _running = null;
                ShowDailyLeaderboardPanel();
                yield break;
            }

            // Fade everything out, then advance.
            float f = 0f;
            const float fadeDur = 0.22f;
            while (f < fadeDur)
            {
                f += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(f / fadeDur);
                SetAlpha(_word, a);
                SetAlpha(_detail, a);
                _dim.color = new Color(
                    _dimColor.r, _dimColor.g, _dimColor.b, _dimColor.a * a);
                yield return null;
            }

            _running = null;
            Next?.Invoke();
        }

        private void BuildDailyLeaderboardPanel()
        {
            var panelObject = new GameObject(
                "DailyLeaderboard",
                typeof(RectTransform));
            panelObject.transform.SetParent(_root, false);
            var panel = (RectTransform)panelObject.transform;
            UiKit.Stretch(panel);
            _dailyLeaderboardPanel =
                panelObject.AddComponent<DailyLeaderboardPanel>();
            _dailyLeaderboardPanel.Build(panel);
            _dailyLeaderboardPanel.Home += () => Home?.Invoke();
            _dailyLeaderboardPanel.Replay += () => Replay?.Invoke();
        }

        private void ShowCelebrationView()
        {
            if (_particleLayer != null)
                _particleLayer.gameObject.SetActive(true);
            if (_word != null) _word.gameObject.SetActive(true);
            if (_detail != null) _detail.gameObject.SetActive(true);
        }

        private void ShowDailyLeaderboardPanel()
        {
            if (_particleLayer != null)
                _particleLayer.gameObject.SetActive(false);
            _dailyLeaderboardPanel?.Show();
        }

        private void BuildDailyActions()
        {
            _dailyActions = new GameObject("DailyActions", typeof(RectTransform));
            _dailyActions.transform.SetParent(_root, false);
            var actions = (RectTransform)_dailyActions.transform;
            UiKit.Anchor(
                actions,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(780f, 110f),
                new Vector2(0f, -300f));

            var home = UiKit.Button(
                actions,
                "DailyHome",
                Theme.CircleBg,
                SpriteFactory.RoundedRect(72, 22),
                out var homeImage);
            UiKit.Anchor(
                homeImage.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(350f, 96f),
                new Vector2(-190f, 0f));
            var homeLabel = UiKit.Text(
                homeImage.rectTransform,
                "Label",
                "HOME",
                27,
                Theme.OnCircle,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(homeLabel.rectTransform);
            home.onClick.AddListener(() => Home?.Invoke());
            AddPressFx(homeImage.rectTransform);

            _dailyReplayButton = UiKit.Button(
                actions,
                "DailyReplay",
                Theme.Gold,
                SpriteFactory.RoundedRect(72, 22),
                out _dailyReplayImage);
            UiKit.Anchor(
                _dailyReplayImage.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(350f, 96f),
                new Vector2(190f, 0f));
            _dailyReplayLabel = UiKit.Text(
                _dailyReplayImage.rectTransform,
                "Label",
                "REPLAY",
                27,
                Theme.Hex("#3A3320"),
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_dailyReplayLabel.rectTransform);
            _dailyReplayButton.onClick.AddListener(() => Replay?.Invoke());
            AddPressFx(_dailyReplayImage.rectTransform);
            _dailyActions.SetActive(false);
        }

        private static void AddPressFx(RectTransform target)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = 0.96f;
        }

        private static string BuildDailyDetail(
            DailyPuzzleCompletion completion)
        {
            string best = completion.BestMoveCount > 0
                ? $"{completion.BestMoveCount} MOVES / " +
                  DailyPuzzleService.FormatElapsed(
                      completion.BestTimeSeconds)
                : "—";
            string marker = completion.IsNewBest ? "  ·  NEW BEST" : string.Empty;
            return
                $"TIME {DailyPuzzleService.FormatElapsed(completion.ElapsedSeconds)}  ·  " +
                $"{completion.MoveCount} MOVES{marker}\n" +
                $"BEST {best}\n" +
                $"TRY {completion.Status.AttemptsUsed}/" +
                $"{DailyPuzzleService.MaxAttempts}  ·  " +
                $"STREAK {completion.Status.CurrentStreak}  ·  " +
                $"BEST STREAK {completion.Status.BestStreak}";
        }

        private IEnumerator SparkWave(int count, float radius, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            float baseAngle = UnityEngine.Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float ang = baseAngle + (360f / count) * i + UnityEngine.Random.Range(-8f, 8f);
                StartCoroutine(SparkParticle(ang, radius));
            }
        }

        private IEnumerator SparkParticle(float angleDeg, float radius)
        {
            Image img = RentSpark();
            if (img == null) yield break;
            img.color = Sparks[UnityEngine.Random.Range(0, Sparks.Length)];
            img.gameObject.SetActive(true);
            var rt = img.rectTransform;
            UiKit.Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.one * UnityEngine.Random.Range(34f, 62f), Vector2.zero);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            float rad = angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float r0 = radius * 0.45f;
            float r1 = radius + UnityEngine.Random.Range(90f, 210f);
            float spin = UnityEngine.Random.Range(-220f, 220f);
            float life = UnityEngine.Random.Range(0.95f, 1.30f);

            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float x = Mathf.Clamp01(t / life);
                float outp = 1f - (1f - x) * (1f - x); // ease-out distance
                rt.anchoredPosition = dir * Mathf.Lerp(r0, r1, outp);
                // pop up quickly then shrink away
                float s = x < 0.25f ? Mathf.Lerp(0.2f, 1f, x / 0.25f) : Mathf.Lerp(1f, 0.1f, (x - 0.25f) / 0.75f);
                rt.localScale = Vector3.one * s;
                rt.Rotate(0, 0, spin * Time.unscaledDeltaTime);
                SetAlpha(img, 1f - Mathf.Clamp01((x - 0.58f) / 0.42f));
                yield return null;
            }
            ReturnSpark(img);
        }

        private void PrewarmSparkPool()
        {
            Sprite star = SpriteFactory.Star(64);
            for (int i = 0; i < SparkPoolSize; i++)
            {
                Image image = UiKit.Image(_particleLayer, "Spark_" + i,
                    Theme.Gold, star);
                image.raycastTarget = false;
                image.gameObject.SetActive(false);
                _sparkPool.Add(image);
                _availableSparks.Push(image);
            }
        }

        private Image RentSpark()
        {
            return _availableSparks.Count > 0 ? _availableSparks.Pop() : null;
        }

        private void ReturnSpark(Image image)
        {
            if (image == null || !image.gameObject.activeSelf) return;
            image.gameObject.SetActive(false);
            image.rectTransform.localRotation = Quaternion.identity;
            image.rectTransform.localScale = Vector3.one;
            _availableSparks.Push(image);
        }

        private void ResetSparkPool()
        {
            _availableSparks.Clear();
            for (int i = 0; i < _sparkPool.Count; i++)
            {
                Image image = _sparkPool[i];
                if (image == null) continue;
                image.gameObject.SetActive(false);
                image.rectTransform.localRotation = Quaternion.identity;
                image.rectTransform.localScale = Vector3.one;
                _availableSparks.Push(image);
            }
        }

        private static Color CompletionDim()
        {
            Color color = Color.Lerp(Theme.Background, Color.black,
                Theme.IsDark ? 0.24f : 0.38f);
            color.a = 0.88f;
            return color;
        }

        private static void SetAlpha(Graphic g, float a)
        {
            var c = g.color; c.a = a; g.color = c;
        }

        // Overshoot ease (back) for a lively pop-in.
        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float xm = x - 1f;
            return 1f + c3 * xm * xm * xm + c1 * xm * xm;
        }
    }
}
