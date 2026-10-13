using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Gameplay-first home screen: compact title, animated miniature maze, and a
    /// lightweight launch dock with difficulty, level position, and one primary action.
    /// Secondary sound/haptic preferences live in a modal settings panel.
    /// </summary>
    public sealed class HomeController : MonoBehaviour
    {
        private const float HomeCardWidth = 900f;
        private const float HomeCardShadowWidth = 908f;

        public event Action<Difficulty, int> Play;
        public event Action<Difficulty, int> SelectionChanged;
        public event Action ThemeChanged;
        public event Action AccountRequested;
        public event Action SignInRequested;
        public event Action LeaderboardRequested;
        public event Action DailyPlay;
        public event Action BallSkinChanged;

        private RectTransform _root;
        private RectTransform _safe;
        private Image _bg;
        private Image _playBg;
        private Text _playLabel;
        private Text _levelLabel;
        private Text _endlessLabel;
        private HomeMazePreview _preview;
        private SettingsPanel _settings;
        private BallShopPanel _shop;
        private HowToPlayController _howTo;
        private AuthService _auth;
        private CosmeticService _cosmetics;
        private DailyPuzzleService _dailyService;
        private Text _dailyStatusLabel;
        private Text _dailyActionLabel;
        private Button _dailyButton;
        private Text _coinsLabel;
        private Text _toastLabel;
        private Coroutine _toastRoutine;
        private Difficulty _selected;

        public Difficulty SelectedDifficulty => _selected;
        public bool HasDismissibleOverlay =>
            (_howTo != null && _howTo.gameObject.activeSelf) ||
            (_shop != null && _shop.gameObject.activeSelf) ||
            (_settings != null && _settings.gameObject.activeSelf);

        public bool DismissActiveOverlay()
        {
            if (_howTo != null && _howTo.gameObject.activeSelf)
            {
                _howTo.Hide();
                return true;
            }
            if (_shop != null && _shop.gameObject.activeSelf)
            {
                _shop.Hide();
                return true;
            }
            if (_settings != null && _settings.gameObject.activeSelf)
            {
                _settings.Hide();
                return true;
            }
            return false;
        }

        public void Build(
            RectTransform parent,
            AuthService auth,
            DailyPuzzleService dailyService = null,
            CosmeticService cosmetics = null)
        {
            _auth = auth;
            _dailyService = dailyService;
            _cosmetics = cosmetics;
            _selected = SaveService.SelectedDifficulty;
            Theme.Mode = SaveService.ThemeMode;

            _bg = UiKit.Image(parent, "HomeBg", Color.white);
            _root = _bg.rectTransform;
            UiKit.Stretch(_root);

            RebuildContent();
        }

        private void RebuildContent()
        {
            StopAllCoroutines();
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            ApplyBackground();

            _safe = BuildSafeArea();
            BuildTopActions();

            var title = UiKit.Text(_safe, "Title", "PAINT MAZE", 76, Theme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(760f, 102f), new Vector2(0f, -160f));

            var subtitle = UiKit.Text(_safe, "Subtitle", "PAINT EVERY TILE", 28, Theme.InkSoft,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 44f), new Vector2(0f, -250f));

            var previewHost = new GameObject("MazePreview", typeof(RectTransform));
            previewHost.transform.SetParent(_safe, false);
            var previewRt = (RectTransform)previewHost.transform;
            UiKit.Anchor(previewRt, new Vector2(0.5f, 0.60f), new Vector2(0.5f, 0.5f),
                new Vector2(740f, 440f), Vector2.zero);
            _preview = previewHost.AddComponent<HomeMazePreview>();
            _preview.Build(previewRt, CurrentLevel);

            RectTransform dailyCard = _dailyService != null
                ? BuildDailyCard()
                : null;
            RectTransform card = BuildGameCard();
            RectTransform actions = BuildBottomActionBar();
            BuildSettingsPanel();
            BuildShopPanel();
            BuildHowToPanel();
            RefreshData();

            StartCoroutine(AnimateIn(title.rectTransform, 0f, new Vector2(0f, 18f)));
            StartCoroutine(AnimateIn(previewRt, 0.05f, new Vector2(0f, -20f)));
            if (dailyCard != null)
                StartCoroutine(AnimateIn(dailyCard, 0.1f, new Vector2(0f, -24f)));
            StartCoroutine(AnimateIn(card, 0.14f, new Vector2(0f, -28f)));
            StartCoroutine(AnimateIn(actions, 0.18f, new Vector2(0f, -24f)));
        }

        private void ApplyBackground()
        {
            Color bg = Theme.Background;
            Color top = Color.Lerp(bg, Color.white, 0.10f);
            Color bottom = Color.Lerp(bg, Color.black, 0.10f);
            _bg.sprite = SpriteFactory.VerticalGradient(bottom, top);
            _bg.type = Image.Type.Simple;
            _bg.color = Color.white;
        }

        private RectTransform BuildSafeArea()
        {
            var go = new GameObject("SafeArea", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            Rect safe = Screen.safeArea;
            float sw = Mathf.Max(1f, Screen.width);
            float sh = Mathf.Max(1f, Screen.height);
            rt.anchorMin = new Vector2(safe.xMin / sw, safe.yMin / sh);
            rt.anchorMax = new Vector2(safe.xMax / sw, safe.yMax / sh);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void BuildTopActions()
        {
            Image coinsImage = UiKit.Image(
                _safe,
                "CoinsChip",
                Theme.CircleBg,
                SpriteFactory.RoundedRect(72, 24));
            coinsImage.raycastTarget = false;
            UiKit.Anchor(
                coinsImage.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(188f, 58f),
                new Vector2(48f, -33f));
            Image coinIcon = UiKit.Image(
                coinsImage.rectTransform,
                "CoinsIcon",
                Color.white,
                SpriteFactory.Coins());
            coinIcon.raycastTarget = false;
            UiKit.Anchor(
                coinIcon.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(40f, 40f),
                new Vector2(55f, 0f));
            _coinsLabel = UiKit.Text(
                coinsImage.rectTransform,
                "Label",
                "0",
                30,
                Theme.Gold,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                _coinsLabel.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(110f, 52f),
                new Vector2(24f, 0f));
            _coinsLabel.raycastTarget = false;
            RefreshCoins();

            _toastLabel = UiKit.Text(
                _safe,
                "HomeToast",
                string.Empty,
                28,
                Theme.Ink,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                _toastLabel.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(720f, 48f),
                new Vector2(0f, -150f));
            _toastLabel.raycastTarget = false;
            _toastLabel.gameObject.SetActive(false);

            var cartBtn = UiKit.Button(
                _safe,
                "CartButton",
                Theme.CircleBg,
                SpriteFactory.Circle(),
                out Image cartImage);
            UiKit.Anchor(
                cartImage.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(84f, 84f),
                new Vector2(-266f, -62f));
            Image cartIcon = UiKit.Image(
                cartImage.rectTransform,
                "CartIcon",
                Theme.OnCircle,
                SpriteFactory.Cart());
            cartIcon.raycastTarget = false;
            cartIcon.preserveAspect = true;
            UiKit.Anchor(
                cartIcon.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(60f, 60f),
                Vector2.zero);
            cartBtn.onClick.AddListener(() => _shop?.Show());
            AddPressFx(cartImage.rectTransform, 0.9f);

            var themeBtn = UiKit.Button(_safe, "ThemeToggle", Theme.CircleBg,
                SpriteFactory.Circle(), out var themeImg);
            UiKit.Anchor(themeImg.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(84f, 84f), new Vector2(-164f, -62f));
            BuildThemeIcon(themeImg.rectTransform);
            themeBtn.onClick.AddListener(ToggleTheme);
            AddPressFx(themeImg.rectTransform, 0.9f);

            var helpBtn = UiKit.Button(_safe, "HowToPlayButton", Theme.CircleBg,
                SpriteFactory.Circle(), out var accountImg);
            UiKit.Anchor(accountImg.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(84f, 84f), new Vector2(-62f, -62f));
            BuildHelpIcon(accountImg.rectTransform);
            helpBtn.onClick.AddListener(() => _howTo?.Show());
            AddPressFx(accountImg.rectTransform, 0.9f);
        }

        private RectTransform BuildGameCard()
        {
            // The selector and level summary stay above the thumb-zone action row.
            var dockGo = new GameObject("LaunchDock", typeof(RectTransform));
            dockGo.transform.SetParent(_safe, false);
            var dockRt = (RectTransform)dockGo.transform;
            UiKit.Anchor(dockRt, new Vector2(0.5f, 0.27f), new Vector2(0.5f, 0.5f),
                new Vector2(920f, 220f), Vector2.zero);

            var sliderHost = new GameObject("SliderHost", typeof(RectTransform));
            sliderHost.transform.SetParent(dockRt, false);
            var sliderRt = (RectTransform)sliderHost.transform;
            UiKit.Anchor(sliderRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(HomeCardWidth, 88f), new Vector2(0f, 0f));
            var sliderShadow = UiKit.Image(
                sliderRt,
                "DifficultyShadow",
                new Color(0f, 0f, 0f, 0.14f),
                SpriteFactory.RoundedRect(64, 32));
            sliderShadow.raycastTarget = false;
            UiKit.Anchor(
                sliderShadow.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(HomeCardShadowWidth, 94f),
                new Vector2(6f, -8f));
            var slider = sliderHost.AddComponent<DifficultySlider>();
            slider.Build(sliderRt, _selected);
            slider.Changed += OnDifficultyChanged;

            var shadow = UiKit.Image(dockRt, "LaunchCardShadow", new Color(0f, 0f, 0f, 0.14f),
                SpriteFactory.RoundedRect(80, 24));
            shadow.raycastTarget = false;
            UiKit.Anchor(shadow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(HomeCardShadowWidth, 122f), new Vector2(6f, -8f));

            var card = UiKit.Image(dockRt, "LaunchCard", Theme.CardBg, SpriteFactory.RoundedRect(80, 24));
            var cardRt = card.rectTransform;
            UiKit.Anchor(cardRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(HomeCardWidth, 116f), Vector2.zero);

            _levelLabel = UiKit.Text(cardRt, "LevelPosition", "EASY  ·  LEVEL 1", 32, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(_levelLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(610f, 54f), new Vector2(42f, 0f));

            _endlessLabel = UiKit.Text(cardRt, "EndlessLabel", "ENDLESS", 24, Theme.InkSoft,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Anchor(_endlessLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(180f, 54f), new Vector2(-42f, 0f));
            return dockRt;
        }

        private RectTransform BuildDailyCard()
        {
            var shadow = UiKit.Image(
                _safe,
                "DailyPuzzleShadow",
                new Color(0f, 0f, 0f, 0.14f),
                SpriteFactory.RoundedRect(76, 24));
            shadow.raycastTarget = false;
            UiKit.Anchor(
                shadow.rectTransform,
                new Vector2(0.5f, 0.405f),
                new Vector2(0.5f, 0.5f),
                new Vector2(HomeCardShadowWidth, 132f),
                new Vector2(6f, -8f));

            _dailyButton = UiKit.Button(
                _safe,
                "DailyPuzzle",
                Theme.CardBg,
                SpriteFactory.RoundedRect(76, 24),
                out var card);
            UiKit.Anchor(
                card.rectTransform,
                new Vector2(0.5f, 0.405f),
                new Vector2(0.5f, 0.5f),
                new Vector2(HomeCardWidth, 126f),
                Vector2.zero);

            var title = UiKit.Text(
                card.rectTransform,
                "DailyTitle",
                "DAILY PUZZLE",
                29,
                Theme.Ink,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                title.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(500f, 42f),
                new Vector2(42f, 24f));

            _dailyStatusLabel = UiKit.Text(
                card.rectTransform,
                "DailyStatus",
                string.Empty,
                22,
                Theme.InkSoft,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                _dailyStatusLabel.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(610f, 38f),
                new Vector2(42f, -24f));

            _dailyActionLabel = UiKit.Text(
                card.rectTransform,
                "DailyAction",
                "PLAY  ▶",
                26,
                Theme.Gold,
                TextAnchor.MiddleRight,
                FontStyle.Bold);
            UiKit.Anchor(
                _dailyActionLabel.rectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(230f, 54f),
                new Vector2(-42f, 0f));

            _dailyButton.onClick.AddListener(() => DailyPlay?.Invoke());
            AddPressFx(card.rectTransform, 0.97f, true);
            RefreshDailyData();
            return card.rectTransform;
        }

        private RectTransform BuildBottomActionBar()
        {
            var go = new GameObject("BottomActionBar", typeof(RectTransform));
            go.transform.SetParent(_safe, false);
            var bar = (RectTransform)go.transform;
            UiKit.Anchor(bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(960f, 164f), new Vector2(0f, 34f));

            var settings = UiKit.Button(bar, "SettingsButton", Theme.CircleBg,
                SpriteFactory.RoundedRect(76, 24), out var settingsImage);
            UiKit.Anchor(settingsImage.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(124f, 124f), new Vector2(64f, 0f));
            BuildSettingsIcon(settingsImage.rectTransform);
            settings.onClick.AddListener(() => _settings?.Show());
            AddPressFx(settingsImage.rectTransform, 0.9f);

            var play = UiKit.Button(bar, "Play", Theme.Accent(_selected),
                SpriteFactory.RoundedRect(84, 30), out _playBg);
            UiKit.Anchor(_playBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(620f, 132f), Vector2.zero);
            _playLabel = UiKit.Text(_playBg.rectTransform, "PlayLabel", "PLAY  ▶", 40,
                Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_playLabel.rectTransform);
            play.onClick.AddListener(() => Play?.Invoke(_selected, CurrentLevel));
            AddPressFx(_playBg.rectTransform, 0.96f, true);

            var leaderboard = UiKit.Button(bar, "LeaderboardButton", Theme.CircleBg,
                SpriteFactory.RoundedRect(76, 24), out var leaderboardImage);
            UiKit.Anchor(leaderboardImage.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(124f, 124f), new Vector2(-64f, 0f));
            BuildTrophyIcon(leaderboardImage.rectTransform);
            leaderboard.onClick.AddListener(() => LeaderboardRequested?.Invoke());
            AddPressFx(leaderboardImage.rectTransform, 0.9f);
            return bar;
        }

        private void BuildSettingsPanel()
        {
            var go = new GameObject("SettingsPanel", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            UiKit.Stretch(rt);
            _settings = go.AddComponent<SettingsPanel>();
            _settings.Build(rt, _auth, null);
            _settings.SignInRequested += () => SignInRequested?.Invoke();
            _settings.AccountRequested += () => AccountRequested?.Invoke();
        }

        private void BuildShopPanel()
        {
            var go = new GameObject("BallShopPanel", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            UiKit.Stretch(rt);
            _shop = go.AddComponent<BallShopPanel>();
            _shop.Build(rt, _cosmetics);
            _shop.SelectionChanged += () =>
            {
                RefreshCosmetics();
                BallSkinChanged?.Invoke();
            };
        }

        private void BuildHowToPanel()
        {
            var go = new GameObject("HowToPlayPanel", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            UiKit.Stretch(rt);
            _howTo = go.AddComponent<HowToPlayController>();
            _howTo.Build(rt);
        }

        private void BuildThemeIcon(RectTransform parent)
        {
            // Dark mode -> show a moon (crescent); Light mode -> show a sun (disc).
            var disc = UiKit.Image(parent, "Disc", Theme.OnCircle, SpriteFactory.Circle());
            disc.raycastTarget = false;
            UiKit.Anchor(disc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(46, 46), Vector2.zero);
            if (Theme.IsDark)
            {
                var bite = UiKit.Image(parent, "Bite", Theme.CircleBg, SpriteFactory.Circle());
                bite.raycastTarget = false;
                UiKit.Anchor(bite.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, 40), new Vector2(11, 6));
            }
        }

        private void BuildSettingsIcon(RectTransform parent)
        {
            float[] ys = { 15f, 0f, -15f };
            float[] knobXs = { -10f, 12f, -2f };
            for (int i = 0; i < 3; i++)
            {
                var line = UiKit.Image(parent, "Line" + i, Theme.OnCircle,
                    SpriteFactory.RoundedRect(24, 8));
                line.raycastTarget = false;
                UiKit.Anchor(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(42f, 5f), new Vector2(0f, ys[i]));

                var knob = UiKit.Image(parent, "Knob" + i, Theme.OnCircle, SpriteFactory.Circle());
                knob.raycastTarget = false;
                UiKit.Anchor(knob.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(13f, 13f), new Vector2(knobXs[i], ys[i]));
            }
        }

        private void BuildTrophyIcon(RectTransform parent)
        {
            var cup = UiKit.Image(parent, "Cup", Theme.Gold,
                SpriteFactory.RoundedRect(42, 14));
            cup.raycastTarget = false;
            UiKit.Anchor(cup.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(43f, 32f), new Vector2(0f, 10f));

            var leftHandle = UiKit.Image(parent, "LeftHandle", Theme.Gold,
                SpriteFactory.RoundedRect(24, 8));
            leftHandle.raycastTarget = false;
            UiKit.Anchor(leftHandle.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(20f, 9f), new Vector2(-27f, 13f));

            var rightHandle = UiKit.Image(parent, "RightHandle", Theme.Gold,
                SpriteFactory.RoundedRect(24, 8));
            rightHandle.raycastTarget = false;
            UiKit.Anchor(rightHandle.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(20f, 9f), new Vector2(27f, 13f));

            var stem = UiKit.Image(parent, "Stem", Theme.Gold,
                SpriteFactory.RoundedRect(16, 6));
            stem.raycastTarget = false;
            UiKit.Anchor(stem.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(10f, 20f), new Vector2(0f, -14f));

            var baseImage = UiKit.Image(parent, "Base", Theme.Gold,
                SpriteFactory.RoundedRect(34, 8));
            baseImage.raycastTarget = false;
            UiKit.Anchor(baseImage.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(40f, 9f), new Vector2(0f, -29f));
        }

        private void BuildAccountIcon(RectTransform parent)
        {
            var head = UiKit.Image(parent, "Head", Theme.OnCircle, SpriteFactory.Circle());
            head.raycastTarget = false;
            UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(25f, 25f), new Vector2(0f, 12f));

            var body = UiKit.Image(parent, "Body", Theme.OnCircle,
                SpriteFactory.RoundedRect(48, 20));
            body.raycastTarget = false;
            UiKit.Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(48f, 25f), new Vector2(0f, -16f));
        }

        private void BuildHelpIcon(RectTransform parent)
        {
            var ring = UiKit.Text(parent, "Help", "?", 49, Theme.OnCircle,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(ring.rectTransform);
        }

        private void ToggleTheme()
        {
            Theme.Mode = Theme.IsDark ? ThemeMode.Light : ThemeMode.Dark;
            SaveService.ThemeMode = Theme.Mode;
            ThemeChanged?.Invoke();
            RebuildContent();
        }

        private void OnDifficultyChanged(Difficulty d)
        {
            _selected = d;
            SaveService.SelectedDifficulty = d;
            RefreshData();
            _preview?.Refresh(CurrentLevel);
            SelectionChanged?.Invoke(_selected, CurrentLevel);
        }

        private int CurrentLevel => SaveService.HighestUnlocked(_selected);

        private void RefreshData()
        {
            if (_playBg != null) _playBg.color = Theme.Accent(_selected);
            if (_playLabel != null) _playLabel.text = CurrentLevel > 1 ? "CONTINUE  ▶" : "PLAY  ▶";

            if (_levelLabel != null)
                _levelLabel.text = LevelLabel.DockPosition(_selected, CurrentLevel);
            if (_endlessLabel != null) _endlessLabel.text = LevelLabel.Endless;
            RefreshCoins();
            RefreshDailyData();
        }

        public void RefreshCoins()
        {
            if (_coinsLabel == null) return;
            _coinsLabel.text = SaveService.Coins.ToString("N0");
            _coinsLabel.color = Theme.Gold;
        }

        public void RefreshCosmetics()
        {
            RefreshCoins();
            _preview?.RefreshBallSkin();
        }

        public void ShowToast(string message)
        {
            if (_toastLabel == null || string.IsNullOrWhiteSpace(message)) return;
            if (_toastRoutine != null) StopCoroutine(_toastRoutine);
            _toastRoutine = StartCoroutine(ShowToastRoutine(message));
        }

        private IEnumerator ShowToastRoutine(string message)
        {
            _toastLabel.text = message;
            _toastLabel.color = Theme.Ink;
            _toastLabel.gameObject.SetActive(true);
            var group = _toastLabel.GetComponent<CanvasGroup>();
            if (group == null) group = _toastLabel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            float t = 0f;
            while (t < 0.2f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Clamp01(t / 0.2f);
                yield return null;
            }

            group.alpha = 1f;
            yield return new WaitForSecondsRealtime(2.2f);

            t = 0f;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / 0.25f);
                yield return null;
            }

            _toastLabel.gameObject.SetActive(false);
            _toastRoutine = null;
        }

        private void RefreshDailyData()
        {
            if (_dailyService == null || _dailyStatusLabel == null) return;
            DailyPuzzleStatus status = _dailyService.Today;
            if (_dailyButton != null)
                _dailyButton.interactable = !status.IsLocked;
            if (status.IsLocked)
            {
                _dailyStatusLabel.text = status.IsCompleted
                    ? $"COMPLETED  ·  {DailyPuzzleService.MaxAttempts}/" +
                      $"{DailyPuzzleService.MaxAttempts} TRIES  ·  " +
                      $"STREAK {status.CurrentStreak}"
                    : $"{DailyPuzzleService.MaxAttempts} TRIES USED  ·  " +
                      "NEW PUZZLE TOMORROW";
                if (_dailyActionLabel != null)
                    _dailyActionLabel.text = "LOCKED";
                return;
            }
            if (status.IsAttemptInProgress)
            {
                _dailyStatusLabel.text =
                    $"TRY {status.AttemptsUsed}/" +
                    $"{DailyPuzzleService.MaxAttempts} IN PROGRESS";
                if (_dailyActionLabel != null)
                    _dailyActionLabel.text = "RESUME  ▶";
                return;
            }
            if (status.IsCompleted)
            {
                _dailyStatusLabel.text =
                    $"COMPLETED  ·  {status.AttemptsUsed}/" +
                    $"{DailyPuzzleService.MaxAttempts} TRIES  ·  " +
                    $"STREAK {status.CurrentStreak}";
                if (_dailyActionLabel != null) _dailyActionLabel.text = "REPLAY  ▶";
                return;
            }

            _dailyStatusLabel.text = status.CurrentStreak > 0
                ? $"MEDIUM  ·  {DailyPuzzleService.MaxAttempts} TRIES  ·  " +
                  $"STREAK {status.CurrentStreak}"
                : $"MEDIUM  ·  {DailyPuzzleService.MaxAttempts} TRIES TODAY";
            if (_dailyActionLabel != null) _dailyActionLabel.text = "PLAY  ▶";
        }

        private static void AddPressFx(RectTransform target, float scale, bool overshoot = false)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = scale;
            fx.overshoot = overshoot;
        }

        private IEnumerator AnimateIn(RectTransform target, float delay, Vector2 offset)
        {
            var group = target.gameObject.AddComponent<CanvasGroup>();
            Vector2 destination = target.anchoredPosition;
            target.anchoredPosition = destination + offset;
            target.localScale = Vector3.one * 0.97f;
            group.alpha = 0f;

            float waited = 0f;
            while (waited < delay)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            const float duration = 0.26f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - k) * (1f - k);
                target.anchoredPosition = Vector2.LerpUnclamped(destination + offset, destination, eased);
                target.localScale = Vector3.LerpUnclamped(Vector3.one * 0.97f, Vector3.one, eased);
                group.alpha = eased;
                yield return null;
            }

            target.anchoredPosition = destination;
            target.localScale = Vector3.one;
            group.alpha = 1f;
        }

        public void OnShown()
        {
            RefreshData();
            _preview?.Refresh(CurrentLevel);
            SelectionChanged?.Invoke(_selected, CurrentLevel);
        }
    }
}
