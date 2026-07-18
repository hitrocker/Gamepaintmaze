using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    /// <summary>
    /// In-game HUD / UI manager built to the project's UI spec, adapted to this
    /// project: code-driven uGUI (no prefabs, no Inspector wiring) using legacy
    /// <see cref="Text"/> (the project has no TextMeshPro). It subclasses
    /// <see cref="Hud"/> so the existing GameController wiring (Back/Hint/Restart
    /// events, SetLevel/SetProgress) keeps working untouched.
    ///
    /// Layout: a transparent header (back button left, difficulty + "Level N"
    /// centered) and a bottom-right gold hint button with a hint-count badge and an
    /// optional video-hint badge. Colors come from the approved <see cref="Theme"/>.
    /// </summary>
    public sealed class UIManager : Hud
    {
        // Public references (assigned in code here rather than via the Inspector).
        public Text difficultyLabel;
        public Text levelTitle;
        public Button backButton;

        public Button hintButton;
        public Text hintCountText;
        public GameObject hintCountBadge;
        public GameObject videoHintBadge;

        private Image _backImage;
        private Image _backShadow;
        private Text _backGlyph;
        private Image _difficultyPillImage;
        private Image _hintImage;
        private Image _hintShadow;
        private Image _bulbBaseImage;
        private Image _hintCountImage;
        private Image _videoImage;
        private Text _videoGlyph;
        private RectTransform _safeArea;

        public override void Build(RectTransform parent, Difficulty difficulty)
        {
            _safeArea = BuildSafeArea(parent);
            BuildHeader(_safeArea);
            BuildHintButton(_safeArea);
            SetLevel(difficulty, 1);
        }

        private static RectTransform BuildSafeArea(RectTransform parent)
        {
            var go = new GameObject("GameSafeArea", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Rect safe = Screen.safeArea;
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            rt.anchorMin = new Vector2(safe.xMin / screenWidth, safe.yMin / screenHeight);
            rt.anchorMax = new Vector2(safe.xMax / screenWidth, safe.yMax / screenHeight);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private void BuildHeader(RectTransform parent)
        {
            // Floating circular back control: shadow first so it remains behind the
            // interactive image without intercepting input.
            _backShadow = UiKit.Image(parent, "BackButtonShadow",
                new Color(0f, 0f, 0f, Theme.IsDark ? 0.24f : 0.17f), SpriteFactory.Circle());
            _backShadow.raycastTarget = false;
            UiKit.Anchor(_backShadow.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f),
                new Vector2(108, 108), new Vector2(95, -137));

            backButton = UiKit.Button(parent, "BackButton", TranslucentCircleBg(),
                SpriteFactory.Circle(), out var backImg);
            _backImage = backImg;
            UiKit.Anchor(backImg.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(100, 100), new Vector2(95, -130));
            var chev = UiKit.Text(backImg.rectTransform, "Chevron", "\u2039", 56, Theme.OnCircle, TextAnchor.MiddleCenter, FontStyle.Bold);
            _backGlyph = chev;
            UiKit.Anchor(chev.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(100, 100), Vector2.zero);
            backButton.onClick.AddListener(OnBackPressed);
            AddPressFx(backImg, 0.92f, false);

            // Compact difficulty badge above the rounded, high-contrast level title.
            _difficultyPillImage = UiKit.Image(parent, "DifficultyPill", MutedPillBg(),
                SpriteFactory.RoundedRect(64, 28));
            UiKit.Anchor(_difficultyPillImage.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(250, 52), new Vector2(0, -78));
            difficultyLabel = UiKit.Text(_difficultyPillImage.rectTransform, "DifficultyLabel", "EASY",
                24, Theme.OnCircle, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(difficultyLabel.rectTransform);

            levelTitle = UiKit.Text(parent, "LevelTitle", "Level 1", 74, Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(levelTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(900, 100), new Vector2(0, -148));
        }

        /// <summary>Bottom edge of the grouped header in physical screen pixels.</summary>
        public float HeaderBottomScreenY
        {
            get
            {
                if (levelTitle == null) return Screen.height * 0.84f;
                var corners = new Vector3[4];
                levelTitle.rectTransform.GetWorldCorners(corners);
                return RectTransformUtility.WorldToScreenPoint(null, corners[0]).y;
            }
        }

        private void BuildHintButton(RectTransform parent)
        {
            _hintShadow = UiKit.Image(parent, "HintButtonShadow",
                new Color(0f, 0f, 0f, Theme.IsDark ? 0.25f : 0.18f), SpriteFactory.Circle());
            _hintShadow.raycastTarget = false;
            UiKit.Anchor(_hintShadow.rectTransform, new Vector2(1, 0), new Vector2(0.5f, 0.5f),
                new Vector2(148, 148), new Vector2(-110, 152));

            hintButton = UiKit.Button(parent, "HintButton", Theme.Gold, SpriteFactory.Circle(), out var btnImg);
            _hintImage = btnImg;
            UiKit.Anchor(btnImg.rectTransform, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(135, 135), new Vector2(-110, 160));
            hintButton.onClick.AddListener(OnHintPressed);
            AddPressFx(btnImg, 0.94f, true);

            // Lightbulb glyph (bulb + base) drawn from primitives.
            var bulb = UiKit.Image(btnImg.rectTransform, "Bulb", Color.white, SpriteFactory.Circle());
            bulb.raycastTarget = false;
            UiKit.Anchor(bulb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(58, 58), new Vector2(0, 10));
            var bulbBase = UiKit.Image(btnImg.rectTransform, "BulbBase", Theme.Hex("#3A3320"), SpriteFactory.RoundedRect(24, 8));
            _bulbBaseImage = bulbBase;
            bulbBase.raycastTarget = false;
            UiKit.Anchor(bulbBase.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(30, 16), new Vector2(0, -30));

            // Hint count badge (white circle, dark number) top-right.
            var badgeImg = UiKit.Image(btnImg.rectTransform, "HintCountBadge", Color.white, SpriteFactory.Circle());
            _hintCountImage = badgeImg;
            badgeImg.raycastTarget = false;
            UiKit.Anchor(badgeImg.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(48, 48), new Vector2(-2, -2));
            hintCountBadge = badgeImg.gameObject;
            hintCountText = UiKit.Text(badgeImg.rectTransform, "HintCount", "+", 28, Theme.Background, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(hintCountText.rectTransform);

            // Optional video-hint badge (dark circle) bottom-right.
            var videoImg = UiKit.Image(btnImg.rectTransform, "VideoHintBadge", Theme.CircleBg, SpriteFactory.Circle());
            _videoImage = videoImg;
            videoImg.raycastTarget = false;
            UiKit.Anchor(videoImg.rectTransform, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(40, 40), new Vector2(-2, 2));
            var play = UiKit.Text(videoImg.rectTransform, "Play", "\u25B6", 18, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _videoGlyph = play;
            UiKit.Stretch(play.rectTransform);
            videoHintBadge = videoImg.gameObject;
        }

        private void AddPressFx(Image img, float pressedScale, bool overshoot)
        {
            var fx = img.gameObject.AddComponent<UiPressFx>();
            fx.target = img.rectTransform;
            fx.pressedScale = pressedScale;
            fx.overshoot = overshoot;
        }

        private static Color TranslucentCircleBg()
        {
            Color color = Theme.CircleBg;
            color.a = Theme.IsDark ? 0.78f : 0.90f;
            return color;
        }

        private static Color MutedPillBg()
        {
            Color color = Theme.CircleBg;
            color.a = Theme.IsDark ? 0.72f : 0.88f;
            return color;
        }

        // ----- Spec API -----

        public void SetupLevel(string difficulty, int levelNumber, int hintsRemaining, bool hasVideoHint)
        {
            if (difficultyLabel != null) difficultyLabel.text = (difficulty ?? "").ToUpper();
            if (levelTitle != null) levelTitle.text = LevelLabel.HudTitle(levelNumber);
            UpdateHints(hintsRemaining, hasVideoHint);
        }

        public void UpdateHints(int hintsRemaining, bool hasVideoHint)
        {
            // hintsRemaining < 0 means "unlimited" (this project's hint is free): show "+".
            if (hintCountText != null) hintCountText.text = hintsRemaining < 0 ? "+" : hintsRemaining.ToString();
            if (hintCountBadge != null) hintCountBadge.SetActive(hintsRemaining != 0);
            if (videoHintBadge != null) videoHintBadge.SetActive(hasVideoHint);
        }

        public void OnBackPressed() => RaiseBack();
        public void OnHintPressed() => RaiseHint();

        /// <summary>Refreshes the persistent HUD after the home screen changes theme.</summary>
        public void ApplyTheme()
        {
            if (levelTitle != null) levelTitle.color = Theme.Ink;
            if (_backImage != null) _backImage.color = TranslucentCircleBg();
            if (_backShadow != null)
                _backShadow.color = new Color(0f, 0f, 0f, Theme.IsDark ? 0.24f : 0.17f);
            if (_backGlyph != null) _backGlyph.color = Theme.OnCircle;
            if (_difficultyPillImage != null) _difficultyPillImage.color = MutedPillBg();
            if (difficultyLabel != null) difficultyLabel.color = Theme.OnCircle;
            if (_hintImage != null) _hintImage.color = Theme.Gold;
            if (_hintShadow != null)
                _hintShadow.color = new Color(0f, 0f, 0f, Theme.IsDark ? 0.25f : 0.18f);
            if (_bulbBaseImage != null) _bulbBaseImage.color = Theme.Hex("#3A3320");
            if (_hintCountImage != null) _hintCountImage.color = Color.white;
            if (hintCountText != null) hintCountText.color = Theme.Background;
            if (_videoImage != null) _videoImage.color = Theme.CircleBg;
            if (_videoGlyph != null) _videoGlyph.color = Color.white;
        }

        // Bridges the GameController-driven Hud API onto the spec widgets.
        public override void SetLevel(Difficulty difficulty, int index)
        {
            SetupLevel(DifficultyConfig.For(difficulty).DisplayName, index, -1, false);
        }
    }
}
