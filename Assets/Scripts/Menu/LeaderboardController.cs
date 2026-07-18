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
    /// Four-board, top-50 Firestore leaderboard modal.
    /// </summary>
    public sealed class LeaderboardController : MonoBehaviour
    {
        public event Action Closed;
        public event Action SignInRequested;

        private static readonly Difficulty[] Boards =
        {
            Difficulty.Easy,
            Difficulty.Medium,
            Difficulty.Hard,
            Difficulty.ExtraHard
        };

        private const float ListSideInset = 80f;
        private const float ContentSideInset = 10f;
        private const float RowSideInset = 8f;
        private const float RowHeight = 84f;
        private const float RowGap = 8f;
        private const float PinnedBottomInset = 38f;
        private const float MergeEpsilon = 0.5f;

        private readonly List<Image> _tabImages = new();
        private readonly List<Text> _tabTexts = new();

        private LeaderboardService _service;
        private CanvasGroup _canvasGroup;
        private RectTransform _card;
        private RectTransform _content;
        private RectTransform _listViewport;
        private ScrollRect _scroll;
        private Text _status;
        private RectTransform _podium;
        private GameObject _pinnedRow;
        private RectTransform _pinnedRowRect;
        private Image _pinnedRowImage;
        private Text _pinnedRank;
        private Text _pinnedName;
        private Text _pinnedLevel;
        private RectTransform _currentUserRow;
        private LeaderboardEntry _ownEntry;
        private Text _identityName;
        private Text _identityAction;
        private AuthService _auth;
        private Coroutine _transition;
        private Difficulty _selected;
        private int _requestVersion;

        public void Build(RectTransform root, LeaderboardService service, AuthService auth)
        {
            _service = service;
            _auth = auth;
            _service.SessionReady += HandleSessionReady;
            _auth.UserChanged += HandleUserChanged;
            _selected = SaveService.SelectedDifficulty;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            var background = UiKit.Image(root, "LeaderboardBackground", Theme.Background);
            UiKit.Stretch(background.rectTransform);
            _card = UiKit.SafeArea(root);
            var back = UiKit.FullScreenHeader(_card, "LEADERBOARD", Hide, out _);
            AddPressFx((RectTransform)back.transform, 0.94f);

            BuildTabs();
            BuildIdentityStrip();

            _status = UiKit.Text(_card, "Status", "LOADING...", 23, Theme.InkSoft,
                TextAnchor.MiddleCenter);
            UiKit.Anchor(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 46f), new Vector2(0f, -455f));

            BuildPodiumHost();
            BuildList();
            BuildPinnedRow();

            gameObject.SetActive(false);
        }

        public void Show(Difficulty difficulty)
        {
            _selected = DifficultyCatalog.NormalizePlayable(difficulty);
            gameObject.SetActive(true);
            RefreshIdentity();
            RefreshTabs();
            Load();
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(true));
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            _requestVersion++;
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(false));
        }

        private void BuildTabs()
        {
            var tabs = new GameObject("DifficultyTabs", typeof(RectTransform));
            tabs.transform.SetParent(_card, false);
            var tabsRt = (RectTransform)tabs.transform;
            UiKit.Anchor(tabsRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 88f), new Vector2(0f, -155f));

            string[] labels = { "EASY", "MED", "HARD", "X-HARD" };
            for (int i = 0; i < Boards.Length; i++)
            {
                int index = i;
                var button = UiKit.Button(tabsRt, "Tab_" + Boards[i], Theme.TrackBg,
                    SpriteFactory.RoundedRect(56, 18), out var image);
                UiKit.Anchor(image.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(210f, 76f), new Vector2(105f + i * 236f, 0f));
                var text = UiKit.Text(image.rectTransform, "Label", labels[i], 23, Theme.Ink,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(text.rectTransform, 8f, 8f);
                button.onClick.AddListener(() => SelectBoard(Boards[index]));
                AddPressFx(image.rectTransform, 0.95f);
                _tabImages.Add(image);
                _tabTexts.Add(text);
            }
        }

        private void BuildIdentityStrip()
        {
            var button = UiKit.Button(_card, "IdentityStrip",
                Color.Lerp(Theme.CardBg, Theme.TrackBg, 0.18f),
                SpriteFactory.RoundedRect(72, 24), out var strip);
            UiKit.Anchor(strip.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(920f, 150f), new Vector2(0f, -275f));

            var label = UiKit.Text(strip.rectTransform, "Label", "PLAYING AS", 22,
                Theme.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(label.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(520f, 38f), new Vector2(34f, 27f));

            _identityName = UiKit.Text(strip.rectTransform, "Name", "Guest Player", 32,
                Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(_identityName.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(600f, 52f), new Vector2(34f, -27f));

            _identityAction = UiKit.Text(strip.rectTransform, "Action", "SIGN IN", 27,
                Theme.Accent(Difficulty.ExtraHard), TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Anchor(_identityAction.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(220f, 54f), new Vector2(-34f, 0f));
            button.onClick.AddListener(() =>
            {
                if (_auth.CurrentUser == null || _auth.CurrentUser.IsAnonymous)
                    SignInRequested?.Invoke();
            });
            AddPressFx(strip.rectTransform, 0.98f);
            RefreshIdentity();
        }

        private void BuildPodiumHost()
        {
            var go = new GameObject("Podium", typeof(RectTransform));
            go.transform.SetParent(_card, false);
            _podium = (RectTransform)go.transform;
            UiKit.Anchor(_podium, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 310f), new Vector2(0f, -490f));
        }

        private void BuildList()
        {
            var viewport = UiKit.Image(_card, "ListViewport",
                Color.Lerp(Theme.CardBg, Theme.Background, 0.22f),
                SpriteFactory.RoundedRect(72, 20));
            _listViewport = viewport.rectTransform;
            UiKit.Stretch(
                _listViewport,
                ListSideInset,
                ListSideInset,
                830f,
                PinnedBottomInset);
            viewport.gameObject.AddComponent<RectMask2D>();

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewport.rectTransform, false);
            _content = (RectTransform)contentObject.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, -12f);
            _content.sizeDelta = new Vector2(-ContentSideInset * 2f, 0f);

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport.rectTransform;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
            _scroll.scrollSensitivity = 32f;
            _scroll.onValueChanged.AddListener(HandleListScrolled);
        }

        private void BuildPinnedRow()
        {
            var row = UiKit.Image(_card, "PinnedPlayer",
                Color.Lerp(Theme.Accent(_selected), Theme.CardBg, 0.35f),
                SpriteFactory.RoundedRect(54, 16));
            _pinnedRowImage = row;
            _pinnedRow = row.gameObject;
            _pinnedRowRect = row.rectTransform;
            ConfigurePinnedRowRect(_pinnedRowRect);
            (_pinnedRank, _pinnedName, _pinnedLevel) = BuildRankRowText(
                _pinnedRowRect, true);
            _pinnedRow.SetActive(false);
        }

        private void SelectBoard(Difficulty difficulty)
        {
            if (_selected == difficulty) return;
            _selected = difficulty;
            RefreshTabs();
            Load();
        }

        private void RefreshTabs()
        {
            for (int i = 0; i < Boards.Length; i++)
            {
                bool selected = Boards[i] == _selected;
                _tabImages[i].color = selected ? Theme.Accent(Boards[i]) : Theme.TrackBg;
                _tabTexts[i].color = selected ? Color.white : Theme.Ink;
            }
        }

        private void Load()
        {
            int request = ++_requestVersion;
            _status.text = "LOADING...";
            _status.color = Theme.InkSoft;
            UpdatePinnedRow(null, null);
            _service.LoadTop(_selected, result =>
            {
                if (!gameObject.activeSelf || request != _requestVersion) return;
                ApplyResult(result);
            });
        }

        private void HandleSessionReady()
        {
            if (gameObject.activeSelf) Load();
        }

        private void ApplyResult(LeaderboardLoadResult result)
        {
            if (!string.IsNullOrEmpty(result.Error))
            {
                ClearRows();
                ClearPodium();
                UpdatePinnedRow(null, null);
                _status.text = result.Error.ToUpperInvariant();
                _status.color = Theme.Hex("#E66A5E");
                return;
            }

            BuildPodium(result.Entries);
            BuildRows(result.Entries);
            UpdatePinnedRow(result.OwnEntry, result.OwnRank);
            Canvas.ForceUpdateCanvases();
            RefreshPinnedVisibility();
            _status.text = result.IsFromCache
                ? "OFFLINE — SHOWING SAVED RANKINGS"
                : result.Entries.Count == 0 ? "NO RANKINGS YET" : "TOP PLAYERS";
            _status.color = result.IsFromCache ? Theme.Gold : Theme.InkSoft;
        }

        public static string FormatPinnedRank(int? rank) =>
            rank.HasValue && rank.Value > 0 ? rank.Value.ToString() : "?";

        private void UpdatePinnedRow(LeaderboardEntry entry, int? rank)
        {
            if (_pinnedRow == null) return;
            _ownEntry = entry;
            if (entry == null)
            {
                _pinnedRow.SetActive(false);
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? "Player"
                : entry.DisplayName.Trim();
            _pinnedRowImage.color =
                Color.Lerp(Theme.Accent(_selected), Theme.CardBg, 0.35f);
            _pinnedRank.color = RankColor(rank);
            _pinnedRank.text = FormatPinnedRank(rank);
            _pinnedName.text = displayName + "  (YOU)";
            _pinnedLevel.text = "LEVEL " + entry.HighestLevel;
            RefreshPinnedVisibility();
        }

        private void HandleListScrolled(Vector2 _)
        {
            RefreshPinnedVisibility();
        }

        private void RefreshPinnedVisibility()
        {
            if (_pinnedRow == null) return;
            if (_ownEntry == null)
            {
                _pinnedRow.SetActive(false);
                return;
            }

            bool ownRowInLoadedList = _currentUserRow != null;
            float ownRowTop = 0f;
            float pinnedTop = 0f;
            if (ownRowInLoadedList)
            {
                var ownCorners = new Vector3[4];
                var pinnedCorners = new Vector3[4];
                _currentUserRow.GetWorldCorners(ownCorners);
                _pinnedRowRect.GetWorldCorners(pinnedCorners);
                ownRowTop = ownCorners[1].y;
                pinnedTop = pinnedCorners[1].y;
            }

            _pinnedRow.SetActive(ShouldShowPinnedRow(
                ownRowInLoadedList, ownRowTop, pinnedTop));
        }

        public static bool ShouldShowPinnedRow(
            bool ownRowInLoadedList,
            float ownRowTop,
            float pinnedTop)
        {
            return !ownRowInLoadedList ||
                   ownRowTop < pinnedTop - MergeEpsilon;
        }

        public static IReadOnlyList<LeaderboardEntry> PodiumEntries(
            IReadOnlyList<LeaderboardEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return Array.Empty<LeaderboardEntry>();
            int count = Mathf.Min(3, entries.Count);
            var podium = new List<LeaderboardEntry>(count);
            for (int i = 0; i < count; i++) podium.Add(entries[i]);
            return podium;
        }

        private void BuildPodium(IReadOnlyList<LeaderboardEntry> entries)
        {
            ClearPodium();
            IReadOnlyList<LeaderboardEntry> top = PodiumEntries(entries);
            int[] displayOrder = { 1, 0, 2 };
            float[] x = { -300f, 0f, 300f };
            float[] heights = { 126f, 190f, 104f };
            Color[] colors =
            {
                Color.Lerp(Theme.InkSoft, Theme.CardBg, 0.28f),
                Theme.Gold,
                Color.Lerp(Theme.Accent(Difficulty.Hard), Theme.CardBg, 0.28f)
            };

            for (int visual = 0; visual < displayOrder.Length; visual++)
            {
                int rankIndex = displayOrder[visual];
                if (rankIndex >= top.Count) continue;
                LeaderboardEntry entry = top[rankIndex];

                var block = UiKit.Image(_podium, "Place" + (rankIndex + 1), colors[visual],
                    SpriteFactory.RoundedRect(70, 20));
                UiKit.Anchor(block.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(270f, heights[visual]),
                    new Vector2(x[visual], 0f));

                var rank = UiKit.Text(block.rectTransform, "Rank", (rankIndex + 1).ToString(),
                    48, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(rank.rectTransform);

                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? "Player"
                    : entry.DisplayName;
                string shortName = displayName.Length > 15
                    ? displayName.Substring(0, 15)
                    : displayName;
                var name = UiKit.Text(_podium, "Name" + rankIndex, shortName, 23,
                    Theme.Ink, TextAnchor.MiddleCenter,
                    entry.IsCurrentUser ? FontStyle.Bold : FontStyle.Normal);
                UiKit.Anchor(name.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(280f, 50f),
                    new Vector2(x[visual], heights[visual] + 54f));

                var level = UiKit.Text(_podium, "Level" + rankIndex,
                    "LEVEL " + entry.HighestLevel, 22, Theme.InkSoft,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Anchor(level.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(260f, 42f),
                    new Vector2(x[visual], heights[visual] + 15f));
            }
        }

        private void ClearPodium()
        {
            if (_podium == null) return;
            for (int i = _podium.childCount - 1; i >= 0; i--)
                Destroy(_podium.GetChild(i).gameObject);
        }

        private void HandleUserChanged(Firebase.Auth.FirebaseUser _)
        {
            RefreshIdentity();
        }

        private void RefreshIdentity()
        {
            if (_identityName == null) return;
            Firebase.Auth.FirebaseUser user = _auth.CurrentUser;
            bool guest = user == null || user.IsAnonymous;
            _identityName.text = guest || string.IsNullOrWhiteSpace(user.DisplayName)
                ? "Guest Player"
                : user.DisplayName.Trim();
            _identityAction.text = guest ? "SIGN IN" : "CONNECTED";
        }

        private static void ConfigureListRowRect(RectTransform row, int index)
        {
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-RowSideInset * 2f, RowHeight);
            row.anchoredPosition =
                new Vector2(0f, -index * (RowHeight + RowGap));
        }

        private static void ConfigurePinnedRowRect(RectTransform row)
        {
            float horizontalInset =
                ListSideInset + ContentSideInset + RowSideInset;
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(1f, 0f);
            row.pivot = new Vector2(0.5f, 0f);
            row.offsetMin = new Vector2(horizontalInset, PinnedBottomInset);
            row.offsetMax = new Vector2(
                -horizontalInset,
                PinnedBottomInset + RowHeight);
        }

        private static (Text rank, Text name, Text level) BuildRankRowText(
            RectTransform row,
            bool isCurrentUser)
        {
            Text rank = UiKit.Text(
                row,
                "Rank",
                string.Empty,
                29,
                Theme.InkSoft,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                rank.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(76f, 60f),
                new Vector2(48f, 0f));

            Text name = UiKit.Text(
                row,
                "Name",
                string.Empty,
                27,
                Theme.Ink,
                TextAnchor.MiddleLeft,
                isCurrentUser ? FontStyle.Bold : FontStyle.Normal);
            UiKit.Anchor(
                name.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(412f, 60f),
                new Vector2(118f, 0f));

            Text level = UiKit.Text(
                row,
                "Level",
                string.Empty,
                26,
                isCurrentUser ? Color.white : Theme.InkSoft,
                TextAnchor.MiddleRight,
                FontStyle.Bold);
            UiKit.Anchor(
                level.rectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(190f, 60f),
                new Vector2(-26f, 0f));

            return (rank, name, level);
        }

        private static Color RankColor(int? rank) =>
            rank.HasValue && rank.Value > 0 && rank.Value <= 3
                ? Theme.Gold
                : Theme.InkSoft;

        private void BuildRows(IReadOnlyList<LeaderboardEntry> entries)
        {
            ClearRows();
            for (int i = 0; i < entries.Count; i++)
            {
                LeaderboardEntry entry = entries[i];
                Color rowColor = entry.IsCurrentUser
                    ? Color.Lerp(Theme.Accent(_selected), Theme.CardBg, 0.35f)
                    : (i % 2 == 0 ? Theme.TrackBg : Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.35f));
                var row = UiKit.Image(_content, "Rank_" + (i + 1), rowColor,
                    SpriteFactory.RoundedRect(54, 16));
                RectTransform rt = row.rectTransform;
                ConfigureListRowRect(rt, i);
                if (entry.IsCurrentUser) _currentUserRow = rt;

                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                    ? "Player"
                    : entry.DisplayName.Trim();
                string playerName = entry.IsCurrentUser
                    ? displayName + "  (YOU)"
                    : displayName;
                (Text rank, Text name, Text level) =
                    BuildRankRowText(rt, entry.IsCurrentUser);
                rank.text = (i + 1).ToString();
                rank.color = RankColor(i + 1);
                name.text = playerName;
                level.text = "LEVEL " + entry.HighestLevel;
            }

            float height = entries.Count == 0
                ? 0f
                : entries.Count * (RowHeight + RowGap) - RowGap + 24f;
            _content.sizeDelta = new Vector2(-ContentSideInset * 2f, height);
            _content.anchoredPosition = new Vector2(0f, -12f);
        }

        private void ClearRows()
        {
            _currentUserRow = null;
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                GameObject child = _content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _content.sizeDelta = new Vector2(-ContentSideInset * 2f, 0f);
        }

        private IEnumerator Animate(bool opening)
        {
            float duration = opening ? 0.2f : 0.14f;
            float fromAlpha = opening ? 0f : _canvasGroup.alpha;
            float toAlpha = opening ? 1f : 0f;
            Vector3 fromScale = opening ? Vector3.one * 0.94f : _card.localScale;
            Vector3 toScale = opening ? Vector3.one : Vector3.one * 0.96f;
            float elapsed = 0f;

            if (opening)
            {
                _canvasGroup.alpha = 0f;
                _card.localScale = fromScale;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - k) * (1f - k);
                _canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                _card.localScale = Vector3.LerpUnclamped(fromScale, toScale, eased);
                yield return null;
            }

            _canvasGroup.alpha = toAlpha;
            _card.localScale = toScale;
            _transition = null;
            if (!opening)
            {
                gameObject.SetActive(false);
                Closed?.Invoke();
            }
        }

        private static void AddPressFx(RectTransform target, float scale)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = scale;
        }

        private void OnDestroy()
        {
            if (_service != null) _service.SessionReady -= HandleSessionReady;
            if (_auth != null) _auth.UserChanged -= HandleUserChanged;
        }
    }
}
