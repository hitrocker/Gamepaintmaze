using System;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Services
{
    /// <summary>
    /// Tiny helpers to build uGUI elements in code (so screens need no prefabs).
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                // Lexend (SIL OFL) keeps interface text clean and highly readable.
                // Fredoka remains a safe bundled fallback.
                _font = Resources.Load<Font>("Fonts/Lexend");
                if (_font == null) _font = Resources.Load<Font>("Fonts/Fredoka");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
                return _font;
            }
        }

        public static RectTransform Rect(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            return rt;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 anchoredPos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
        }

        public static RectTransform SafeArea(Transform parent, string name = "SafeArea")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Rect safe = Screen.safeArea;
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            rt.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            rt.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Button FullScreenHeader(RectTransform safe, string title,
            Action back, out Text titleText)
        {
            var button = Button(safe, "Back", new Color(0f, 0f, 0f, 0f),
                null, out var image);
            Anchor(image.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(108f, 108f), new Vector2(68f, -72f));
            var arrow = Text(image.rectTransform, "Arrow", "‹", 76, Theme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(arrow.rectTransform);
            button.onClick.AddListener(() => back?.Invoke());

            titleText = Text(safe, "PageTitle", title, 54, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            Anchor(titleText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(760f, 92f), new Vector2(130f, -72f));
            return button;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return img;
        }

        public static Text Text(Transform parent, string name, string value, int fontSize, Color color,
            TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.text = value;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string name, Color bg, Sprite sprite, out Image image)
        {
            image = Image(parent, name, bg, sprite);
            var btn = image.gameObject.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
            colors.fadeDuration = 0.06f;
            btn.colors = colors;
            return btn;
        }

        public static InputField Input(Transform parent, string name, string placeholder,
            InputField.ContentType contentType, out Image image)
        {
            image = Image(parent, name, Theme.CircleBg, SpriteFactory.RoundedRect(72, 22));
            image.gameObject.AddComponent<RectMask2D>();
            var field = image.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;
            field.contentType = contentType;
            field.lineType = InputField.LineType.SingleLine;
            field.caretColor = Theme.Ink;
            field.selectionColor = new Color(0.24f, 0.56f, 0.9f, 0.45f);

            var value = Text(image.rectTransform, "Value", string.Empty, 30, Theme.Ink,
                TextAnchor.MiddleLeft);
            value.supportRichText = false;
            value.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(value.rectTransform, 30f, 30f, 10f, 10f);

            var hintColor = Theme.InkSoft;
            hintColor.a = 0.72f;
            var hint = Text(image.rectTransform, "Placeholder", placeholder, 30, hintColor,
                TextAnchor.MiddleLeft, FontStyle.Italic);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(hint.rectTransform, 30f, 30f, 10f, 10f);

            field.textComponent = value;
            field.placeholder = hint;
            return field;
        }
    }
}
