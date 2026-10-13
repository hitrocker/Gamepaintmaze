using System;
using System.Collections;
using System.Collections.Generic;
using PaintMaze.Game;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Daily ranks start as a light top-three summary and expand only on request.
    /// </summary>
    public sealed class DailyLeaderboardPanel : MonoBehaviour
    {
        public event Action Home;
        public event Action Replay;

        private readonly List<Text> _rows = new();
        private readonly List<Image> _rowImages = new();
        private GameObject _root;
        private Image _background;
        private RectTransform _card;
        private CanvasGroup _cardGroup;
        private Image _handle;
        private Text _title;
        private Text _status;
        private Text _columns;
        private Text _own;
        private GameObject _ownCard;
        private Button _expand;
        private Text _expandLabel;
        private GameObject _actions;
        private Button _replay;
        private Image _replayImage;
        private Text _replayLabel;
        private bool _locked;
        private bool _expanded;
        private bool _showOwnCard;
        private Coroutine _sheetTransition;
        private DailyLeaderboardResult _result;
        private const float ExpandedScrimAlpha = 0.36f;

        public void Build(RectTransform parent)
        {
            _background = UiKit.Image(
                parent,
                "DailyLeaderboardPanel",
                new Color(0f, 0f, 0f, 0f));
            _background.raycastTarget = false;
            _root = _background.gameObject;
            UiKit.Stretch(_background.rectTransform);
            var scrimButton = _root.AddComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.targetGraphic = _background;
            scrimButton.onClick.AddListener(() =>
            {
                if (_expanded) ToggleExpanded();
            });

            var card = UiKit.Image(
                _background.rectTransform,
                "RanksCard",
                new Color(0.075f, 0.085f, 0.11f, 0.96f),
                SpriteFactory.RoundedRect(96, 28));
            _card = card.rectTransform;
            _cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            _handle = UiKit.Image(
                _card,
                "DragHandle",
                new Color(1f, 1f, 1f, 0.38f),
                SpriteFactory.RoundedRect(40, 12));
            _handle.raycastTarget = false;
            UiKit.Anchor(
                _handle.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(92f, 9f),
                new Vector2(0f, -22f));

            BuildHeading(_card);
            _status.gameObject.SetActive(false);
            for (int i = 0; i < DailyLeaderboardService.ResultLimit; i++)
                _rows.Add(BuildRow(_card, i));
            BuildOwnRank(_card);
            BuildExpandButton(_card);
            BuildActions(_card);

            ApplyLayout();
            _root.SetActive(false);
        }

        public void Prepare(bool locked)
        {
            _locked = locked;
            _expanded = false;
            _result = null;
            _showOwnCard = false;
            _status.gameObject.SetActive(false);
            _own.text = "YOUR RANK  —";
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].text = $"{i + 1,2}        —";
            ApplyReplayState();
            ApplyLayout();
        }

        public void SetResult(DailyLeaderboardResult result)
        {
            _result = result;
            if (_root.activeSelf) Refresh();
        }

        public void Show()
        {
            StopAllCoroutines();
            _expanded = false;
            _background.color = new Color(0f, 0f, 0f, 0f);
            ApplyLayout();
            _root.SetActive(true);
            Refresh();
            StartCoroutine(AnimateIn());
        }

        public void Hide()
        {
            StopAllCoroutines();
            _sheetTransition = null;
            if (_root != null) _root.SetActive(false);
        }

        private void BuildHeading(RectTransform parent)
        {
            _title = UiKit.Text(
                parent,
                "Title",
                "DAILY RANKS",
                42,
                Color.white,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                _title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(820f, 62f),
                new Vector2(0f, -48f));

            _status = UiKit.Text(
                parent,
                "Status",
                string.Empty,
                19,
                Theme.Gold,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                _status.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(820f, 34f),
                new Vector2(0f, -98f));

            _columns = UiKit.Text(
                parent,
                "Columns",
                "RANK     PLAYER                         TIME        MOVES",
                18,
                new Color(1f, 1f, 1f, 0.52f),
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                _columns.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(820f, 34f),
                new Vector2(0f, -122f));
        }

        private Text BuildRow(RectTransform parent, int index)
        {
            var row = UiKit.Image(
                parent,
                "Rank" + (index + 1),
                index % 2 == 0
                    ? new Color(1f, 1f, 1f, 0.075f)
                    : new Color(1f, 1f, 1f, 0.035f),
                SpriteFactory.RoundedRect(56, 14));
            _rowImages.Add(row);
            UiKit.Anchor(
                row.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(840f, 64f),
                new Vector2(0f, -202f - index * 76f));
            var text = UiKit.Text(
                row.rectTransform,
                "Text",
                $"{index + 1,2}        —",
                21,
                Color.white,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                text.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(790f, 54f),
                Vector2.zero);
            return text;
        }

        private void BuildOwnRank(RectTransform parent)
        {
            var ownCard = UiKit.Image(
                parent,
                "OwnRankCard",
                new Color(0.98f, 0.74f, 0.22f, 0.18f),
                SpriteFactory.RoundedRect(72, 20));
            _ownCard = ownCard.gameObject;
            _own = UiKit.Text(
                ownCard.rectTransform,
                "OwnRank",
                "YOUR RANK  —",
                23,
                Theme.Gold,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_own.rectTransform);
        }

        private void BuildExpandButton(RectTransform parent)
        {
            _expand = UiKit.Button(
                parent,
                "ExpandRanks",
                new Color(1f, 1f, 1f, 0.10f),
                SpriteFactory.RoundedRect(64, 18),
                out var image);
            _expandLabel = UiKit.Text(
                image.rectTransform,
                "Label",
                "VIEW TOP 10",
                21,
                Color.white,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_expandLabel.rectTransform);
            _expand.onClick.AddListener(ToggleExpanded);
            AddPressFx(image.rectTransform);
        }

        private void BuildActions(RectTransform parent)
        {
            _actions = new GameObject(
                "ExpandedActions",
                typeof(RectTransform));
            _actions.transform.SetParent(parent, false);
            var actions = (RectTransform)_actions.transform;
            UiKit.Anchor(
                actions,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(780f, 104f),
                new Vector2(0f, 60f));

            var home = UiKit.Button(
                actions,
                "LeaderboardHome",
                Theme.CircleBg,
                SpriteFactory.RoundedRect(72, 22),
                out var homeImage);
            UiKit.Anchor(
                homeImage.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(350f, 92f),
                new Vector2(-190f, 0f));
            var homeLabel = UiKit.Text(
                homeImage.rectTransform,
                "Label",
                "HOME",
                26,
                Theme.OnCircle,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(homeLabel.rectTransform);
            home.onClick.AddListener(() => Home?.Invoke());
            AddPressFx(homeImage.rectTransform);

            _replay = UiKit.Button(
                actions,
                "LeaderboardReplay",
                Theme.Gold,
                SpriteFactory.RoundedRect(72, 22),
                out _replayImage);
            UiKit.Anchor(
                _replayImage.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(350f, 92f),
                new Vector2(190f, 0f));
            _replayLabel = UiKit.Text(
                _replayImage.rectTransform,
                "Label",
                "REPLAY",
                26,
                Theme.Hex("#3A3320"),
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_replayLabel.rectTransform);
            _replay.onClick.AddListener(() => Replay?.Invoke());
            AddPressFx(_replayImage.rectTransform);
        }

        private void ToggleExpanded()
        {
            if (_sheetTransition != null) StopCoroutine(_sheetTransition);
            if (_expanded)
            {
                _sheetTransition = StartCoroutine(AnimateSheet(false));
                return;
            }

            _expanded = true;
            Refresh();
            _sheetTransition = StartCoroutine(AnimateSheet(true));
        }

        private void ApplyLayout()
        {
            bool minimal = !_expanded &&
                           (_result == null ||
                            (!string.IsNullOrEmpty(_result.Error) &&
                             (_result.Entries?.Count ?? 0) == 0));
            UiKit.Anchor(
                _card,
                new Vector2(0.5f, _expanded ? 0f : 1f),
                new Vector2(0.5f, _expanded ? 0f : 1f),
                _expanded
                    ? new Vector2(
                        1040f,
                        _showOwnCard ? 1390f : 1260f)
                    : minimal
                        ? new Vector2(920f, 265f)
                        : new Vector2(
                            920f,
                            _showOwnCard ? 560f : 460f),
                _expanded
                    ? Vector2.zero
                    : minimal
                        ? new Vector2(0f, -152f)
                        : new Vector2(
                            0f,
                            _showOwnCard ? -300f : -250f));

            int visibleRows = _expanded
                ? DailyLeaderboardService.ResultLimit
                : minimal ? 0 : 3;
            _columns.gameObject.SetActive(visibleRows > 0);
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i < visibleRows)
                {
                    UiKit.Anchor(
                        (RectTransform)_rows[i].transform.parent,
                        new Vector2(0.5f, 1f),
                        new Vector2(0.5f, 1f),
                        _expanded
                            ? new Vector2(840f, 64f)
                            : new Vector2(840f, 54f),
                        new Vector2(
                            0f,
                            _expanded
                                ? -202f - i * 76f
                                : -168f - i * 60f));
                }
                _rows[i].transform.parent.gameObject.SetActive(i < visibleRows);
            }

            RectTransform ownRect =
                (RectTransform)_ownCard.transform;
            UiKit.Anchor(
                ownRect,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(840f, minimal ? 62f : 72f),
                new Vector2(
                    0f,
                    _expanded
                        ? -1032f
                        : minimal ? -188f : -365f));
            _ownCard.SetActive(_showOwnCard);

            RectTransform expandRect =
                (RectTransform)_expand.transform;
            UiKit.Anchor(
                expandRect,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(320f, _expanded ? 58f : 50f),
                new Vector2(
                    0f,
                    _expanded
                        ? _showOwnCard ? -1130f : -990f
                        : _showOwnCard ? -465f : -365f));
            _expandLabel.text = _expanded ? "SHOW LESS" : "VIEW TOP 10";
            _actions.SetActive(_expanded);
            _handle.gameObject.SetActive(_expanded);
            _background.raycastTarget = _expanded;
        }

        private IEnumerator AnimateSheet(bool opening)
        {
            float duration = opening ? 0.26f : 0.18f;
            Vector2 shown = Vector2.zero;
            Vector2 hidden = new Vector2(0f, -_card.rect.height - 40f);
            Vector2 fromPosition =
                opening ? hidden : _card.anchoredPosition;
            Vector2 toPosition = opening ? shown : hidden;
            float fromScrim = opening
                ? 0f
                : _background.color.a;
            float toScrim = opening ? ExpandedScrimAlpha : 0f;
            float elapsed = 0f;

            if (opening)
            {
                _card.anchoredPosition = hidden;
                _background.color = new Color(0f, 0f, 0f, 0f);
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);
                _card.anchoredPosition =
                    Vector2.LerpUnclamped(fromPosition, toPosition, eased);
                _background.color = new Color(
                    0f,
                    0f,
                    0f,
                    Mathf.Lerp(fromScrim, toScrim, eased));
                yield return null;
            }

            _card.anchoredPosition = toPosition;
            _background.color = new Color(0f, 0f, 0f, toScrim);
            _sheetTransition = null;
            if (opening) yield break;

            _expanded = false;
            Refresh();
        }

        private void Refresh()
        {
            ApplyReplayState();
            if (_result == null)
            {
                _showOwnCard = false;
                _status.gameObject.SetActive(false);
                _expand.gameObject.SetActive(false);
                ApplyLayout();
                return;
            }

            IReadOnlyList<DailyLeaderboardEntry> entries =
                _result.Entries ?? Array.Empty<DailyLeaderboardEntry>();
            bool unavailable =
                !string.IsNullOrEmpty(_result.Error) && entries.Count == 0;
            _status.gameObject.SetActive(unavailable);
            if (unavailable) _status.text = "RANKINGS UNAVAILABLE";
            for (int i = 0; i < _rows.Count; i++)
            {
                DailyLeaderboardEntry entry =
                    i < entries.Count ? entries[i] : null;
                bool isOwn = IsOwnRow(entry, _result.OwnEntry);
                _rows[i].text = entry != null
                    ? FormatRow(i + 1, entry)
                    : $"{i + 1,2}        —";
                _rows[i].color = isOwn ? Theme.Gold : Color.white;
                _rowImages[i].color = isOwn
                    ? new Color(
                        Theme.Gold.r,
                        Theme.Gold.g,
                        Theme.Gold.b,
                        0.26f)
                    : RowColor(i);
            }

            int visibleLimit = _expanded
                ? DailyLeaderboardService.ResultLimit
                : 3;
            _showOwnCard = unavailable ||
                           ShouldShowPinnedOwn(
                               entries,
                               _result.OwnEntry,
                               visibleLimit);
            _expand.gameObject.SetActive(entries.Count > 3);
            ApplyLayout();
            if (_result.OwnEntry == null)
            {
                _own.text = "YOUR RANK  —";
                return;
            }

            string rank = _result.OwnRank.HasValue
                ? "#" + _result.OwnRank.Value
                : "#—";
            _own.text =
                $"{rank}  YOU    " +
                $"{DailyPuzzleService.FormatElapsed(_result.OwnEntry.ElapsedSeconds)}" +
                $"  ·  {_result.OwnEntry.MoveCount} MOVES";
        }

        public static bool IsOwnRow(
            DailyLeaderboardEntry entry,
            DailyLeaderboardEntry ownEntry) =>
            entry != null &&
            (entry.IsCurrentUser ||
             (ownEntry != null &&
              !string.IsNullOrEmpty(entry.UserId) &&
              entry.UserId == ownEntry.UserId));

        public static bool ShouldShowPinnedOwn(
            IReadOnlyList<DailyLeaderboardEntry> entries,
            DailyLeaderboardEntry ownEntry,
            int visibleLimit)
        {
            if (ownEntry == null) return false;
            int count = Mathf.Min(
                Mathf.Max(0, visibleLimit),
                entries?.Count ?? 0);
            for (int i = 0; i < count; i++)
                if (IsOwnRow(entries[i], ownEntry)) return false;
            return true;
        }

        private IEnumerator AnimateIn()
        {
            _cardGroup.alpha = 0f;
            _card.localScale = Vector3.one * 0.96f;
            float elapsed = 0f;
            const float duration = 0.22f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - t) * (1f - t);
                _cardGroup.alpha = eased;
                _card.localScale =
                    Vector3.one * Mathf.Lerp(0.96f, 1f, eased);
                yield return null;
            }
            _cardGroup.alpha = 1f;
            _card.localScale = Vector3.one;
        }

        private static string FormatRow(
            int rank,
            DailyLeaderboardEntry entry)
        {
            string name = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? "Player"
                : entry.DisplayName;
            if (name.Length > 16) name = name.Substring(0, 16);
            return
                $"{rank,2}     {name,-16}     " +
                $"{DailyPuzzleService.FormatElapsed(entry.ElapsedSeconds),9}" +
                $"     {entry.MoveCount,4}";
        }

        private static Color RowColor(int index) =>
            index % 2 == 0
                ? new Color(1f, 1f, 1f, 0.075f)
                : new Color(1f, 1f, 1f, 0.035f);

        private void ApplyReplayState()
        {
            _replay.interactable = !_locked;
            _replayLabel.text = _locked ? "LOCKED" : "REPLAY";
            _replayImage.color = _locked
                ? new Color(
                    Theme.Gold.r,
                    Theme.Gold.g,
                    Theme.Gold.b,
                    0.35f)
                : Theme.Gold;
        }

        private static void AddPressFx(RectTransform target)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = 0.96f;
        }
    }
}
