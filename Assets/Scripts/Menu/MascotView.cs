using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// An original, friendly "paint blob" mascot built from primitive shapes,
    /// tinted to the selected mode. A little idle bob keeps it lively.
    /// </summary>
    public sealed class MascotView : MonoBehaviour
    {
        private Image _body;
        private RectTransform _root;
        private float _baseY;

        public void Build(RectTransform parent, Difficulty difficulty)
        {
            var holder = UiKit.Image(parent, "Mascot", new Color(0, 0, 0, 0));
            _root = holder.rectTransform;
            _root.sizeDelta = new Vector2(260, 260);

            var circle = SpriteFactory.Circle();

            _body = UiKit.Image(_root, "Body", Theme.Accent(difficulty), circle);
            SetCircle(_body, 220, Vector2.zero);

            var face = UiKit.Image(_root, "Face", Theme.FloorTop, circle);
            SetCircle(face, 150, new Vector2(0, -6));

            EyeAt(face.rectTransform, new Vector2(-32, 18));
            EyeAt(face.rectTransform, new Vector2(32, 18));

            var smile = UiKit.Image(face.rectTransform, "Smile", Theme.ButtonDark, SpriteFactory.RoundedRect(48, 24));
            var srt = smile.rectTransform;
            srt.sizeDelta = new Vector2(70, 26);
            srt.anchoredPosition = new Vector2(0, -34);

            var cheekL = UiKit.Image(face.rectTransform, "CheekL", new Color(1f, 0.5f, 0.5f, 0.5f), circle);
            SetCircle(cheekL, 26, new Vector2(-50, -10));
            var cheekR = UiKit.Image(face.rectTransform, "CheekR", new Color(1f, 0.5f, 0.5f, 0.5f), circle);
            SetCircle(cheekR, 26, new Vector2(50, -10));
        }

        public void SetDifficulty(Difficulty difficulty)
        {
            if (_body != null) _body.color = Theme.Accent(difficulty);
        }

        private void EyeAt(RectTransform parent, Vector2 pos)
        {
            var white = UiKit.Image(parent, "Eye", Color.white, SpriteFactory.Circle());
            SetCircle(white, 34, pos);
            var pupil = UiKit.Image(white.rectTransform, "Pupil", Theme.Ink, SpriteFactory.Circle());
            SetCircle(pupil, 16, new Vector2(0, -2));
        }

        private static void SetCircle(Image img, float d, Vector2 pos)
        {
            var rt = img.rectTransform;
            rt.sizeDelta = new Vector2(d, d);
            rt.anchoredPosition = pos;
        }

        private void Start()
        {
            _baseY = _root.anchoredPosition.y;
        }

        private void Update()
        {
            if (_root == null) return;
            var p = _root.anchoredPosition;
            p.y = _baseY + Mathf.Sin(Time.time * 2f) * 8f;
            _root.anchoredPosition = p;
        }
    }
}
