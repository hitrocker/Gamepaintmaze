using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    public sealed class HowToPlayController : MonoBehaviour
    {
        private CanvasGroup _group;
        private RectTransform _card;
        private Coroutine _transition;

        public void Build(RectTransform root)
        {
            _group = gameObject.AddComponent<CanvasGroup>();
            var scrim = UiKit.Button(root, "HowToScrim", new Color(0f, 0f, 0f, 0.62f),
                null, out var scrimImage);
            UiKit.Stretch(scrimImage.rectTransform);
            scrim.onClick.AddListener(Hide);

            var card = UiKit.Image(root, "HowToCard", Theme.CardBg,
                SpriteFactory.RoundedRect(96, 28));
            _card = card.rectTransform;
            UiKit.Anchor(_card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(900f, 1120f), Vector2.zero);

            var title = UiKit.Text(_card, "Title", "HOW TO PLAY", 52, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(680f, 82f), new Vector2(54f, -54f));

            BuildMiniMaze(_card);

            BuildInstruction(_card, 1, "SWIPE TO ROLL",
                "Swipe up, down, left, or right. The ball keeps moving until a wall stops it.",
                -500f);
            BuildInstruction(_card, 2, "PAINT EVERY TILE",
                "Every floor tile the ball crosses is painted. Fill the whole maze to win.",
                -675f);
            BuildInstruction(_card, 3, "PLAN AHEAD",
                "Use walls to change direction and reach corners without leaving gaps.",
                -850f);

            var gotIt = UiKit.Button(_card, "GotIt", Theme.Accent(Difficulty.ExtraHard),
                SpriteFactory.RoundedRect(76, 24), out var buttonImage);
            UiKit.Anchor(buttonImage.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(760f, 112f), new Vector2(0f, 54f));
            var label = UiKit.Text(buttonImage.rectTransform, "Label", "GOT IT", 34,
                Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(label.rectTransform);
            gotIt.onClick.AddListener(Hide);
            var fx = buttonImage.gameObject.AddComponent<UiPressFx>();
            fx.target = buttonImage.rectTransform;
            fx.pressedScale = 0.96f;

            gameObject.SetActive(false);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(true));
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(false));
        }

        private static void BuildMiniMaze(RectTransform parent)
        {
            var host = new GameObject("MiniMaze", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            var rt = (RectTransform)host.transform;
            UiKit.Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(560f, 280f), new Vector2(0f, -165f));

            Vector2[] tiles =
            {
                new(-180f, 70f), new(-90f, 70f), new(0f, 70f),
                new(0f, -20f), new(90f, -20f), new(180f, -20f),
                new(180f, -110f), new(90f, -110f), new(0f, -110f)
            };
            for (int i = 0; i < tiles.Length; i++)
            {
                Color color = i < 4
                    ? Theme.Accent(Difficulty.ExtraHard)
                    : Theme.TrackBg;
                var tile = UiKit.Image(rt, "Tile" + i, color,
                    SpriteFactory.RoundedRect(52, 12));
                tile.raycastTarget = false;
                UiKit.Anchor(tile.rectTransform, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(74f, 74f), tiles[i]);
            }

            var ball = UiKit.Image(rt, "Ball", Theme.Ball, SpriteFactory.Circle());
            ball.raycastTarget = false;
            UiKit.Anchor(ball.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(48f, 48f), tiles[3]);
        }

        private static void BuildInstruction(RectTransform parent, int number,
            string title, string detail, float top)
        {
            var badge = UiKit.Image(parent, "Step" + number, Theme.CircleBg,
                SpriteFactory.Circle());
            UiKit.Anchor(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(62f, 62f), new Vector2(90f, top));
            var numberText = UiKit.Text(badge.rectTransform, "Number", number.ToString(), 27,
                Theme.OnCircle, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(numberText.rectTransform);

            var heading = UiKit.Text(parent, "StepTitle" + number, title, 30,
                Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(heading.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(650f, 48f), new Vector2(145f, top + 22f));

            var body = UiKit.Text(parent, "StepBody" + number, detail, 24,
                Theme.InkSoft, TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(body.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(650f, 90f), new Vector2(145f, top - 35f));
        }

        private IEnumerator Animate(bool opening)
        {
            float duration = opening ? 0.2f : 0.14f;
            float start = opening ? 0f : _group.alpha;
            float finish = opening ? 1f : 0f;
            Vector3 from = opening ? Vector3.one * 0.94f : _card.localScale;
            Vector3 to = opening ? Vector3.one : Vector3.one * 0.96f;
            if (opening)
            {
                _group.alpha = 0f;
                _card.localScale = from;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - k) * (1f - k);
                _group.alpha = Mathf.Lerp(start, finish, eased);
                _card.localScale = Vector3.LerpUnclamped(from, to, eased);
                yield return null;
            }
            _group.alpha = finish;
            _card.localScale = to;
            _transition = null;
            if (!opening) gameObject.SetActive(false);
        }
    }
}
