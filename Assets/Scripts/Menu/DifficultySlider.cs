using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// A segmented "pill" difficulty selector: one tappable segment per
    /// <see cref="Difficulty"/> value inside a recessed track, with a rounded
    /// highlight that glides to the active segment and tints to that mode's
    /// accent colour. Fires <see cref="Changed"/> on selection. (Name kept for
    /// existing call sites.)
    /// </summary>
    public sealed class DifficultySlider : MonoBehaviour
    {
        public event Action<Difficulty> Changed;

        private static readonly IReadOnlyList<Difficulty> Difficulties =
            DifficultyCatalog.Playable;

        private const float Pad = 5f;
        private const float HighlightInset = 5f;
        private const int LabelFontSize = 22;
        private const float LabelInset = 6f;

        private RectTransform _track;
        private RectTransform _highlight;
        private Image _highlightImg;
        private Text[] _labels;
        private float _segW;
        private float _height;
        private int _index;
        private int _count;
        private Coroutine _slide;

        public void Build(RectTransform parent, Difficulty initial)
        {
            _count = Difficulties.Count;
            initial = DifficultyCatalog.NormalizePlayable(initial);
            _index = IndexOf(initial);
            if (_index < 0) _index = 0;

            // Fill the host so sizing follows whatever HomeController lays out.
            float w = parent.rect.width > 1f ? parent.rect.width : 900f;
            _height = parent.rect.height > 1f ? parent.rect.height : 92f;

            var track = UiKit.Image(parent, "SegTrack", Theme.CircleBg, SpriteFactory.RoundedRect(64, 32));
            _track = track.rectTransform;
            UiKit.Stretch(_track);

            float innerW = w - Pad * 2f;
            _segW = innerW / _count;
            float segH = _height - Pad * 2f;

            // Gliding highlight behind the labels.
            _highlightImg = UiKit.Image(_track, "SegHighlight", Theme.Accent(initial), SpriteFactory.RoundedRect(56, 28));
            _highlight = _highlightImg.rectTransform;
            _highlight.anchorMin = _highlight.anchorMax = new Vector2(0f, 0.5f);
            _highlight.pivot = new Vector2(0.5f, 0.5f);
            _highlight.sizeDelta = new Vector2(_segW - HighlightInset, segH);
            _highlight.anchoredPosition = new Vector2(SegCenterX(_index), 0f);

            _labels = new Text[_count];
            for (int i = 0; i < _count; i++)
            {
                int idx = i;
                var d = Difficulties[i];

                // Transparent tap target across the whole segment.
                var hit = UiKit.Button(_track, "Seg" + i, new Color(0, 0, 0, 0), null, out _);
                var hrt = hit.image.rectTransform;
                hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 0.5f);
                hrt.pivot = new Vector2(0.5f, 0.5f);
                hrt.sizeDelta = new Vector2(_segW, _height);
                hrt.anchoredPosition = new Vector2(SegCenterX(i), 0f);
                hit.onClick.AddListener(() => Select(idx, true));

                var lbl = UiKit.Text(hrt, "Lbl" + i, DifficultyConfig.For(d).DisplayName, LabelFontSize, Theme.InkSoft,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                // Keep all four segments legible on a narrow portrait track: wrap
                // "Extra Hard" onto two lines instead of
                // letting them overflow into the neighbouring segment, and inset the text
                // slightly so it never touches the segment edges.
                lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
                lbl.verticalOverflow = VerticalWrapMode.Overflow;
                lbl.resizeTextForBestFit = true;
                lbl.resizeTextMinSize = 14;
                lbl.resizeTextMaxSize = LabelFontSize;
                UiKit.Stretch(lbl.rectTransform, LabelInset, LabelInset);
                _labels[i] = lbl;
            }

            ApplyColors();
        }

        private float SegCenterX(int i) => Pad + _segW * (i + 0.5f);

        private static int IndexOf(Difficulty difficulty)
        {
            for (int i = 0; i < Difficulties.Count; i++)
                if (Difficulties[i] == difficulty) return i;
            return -1;
        }

        private void Select(int i, bool notify)
        {
            if (i < 0 || i >= _count) return;
            _index = i;
            var d = Difficulties[i];
            if (_slide != null) StopCoroutine(_slide);
            if (isActiveAndEnabled) _slide = StartCoroutine(SlideTo(SegCenterX(i), Theme.Accent(d)));
            else { _highlight.anchoredPosition = new Vector2(SegCenterX(i), 0f); _highlightImg.color = Theme.Accent(d); }
            ApplyColors();
            if (notify)
            {
                AudioService.Instance?.HapticMedium();
                Changed?.Invoke(d);
            }
        }

        // Ease the highlight to the target segment and cross-fade its tint.
        private IEnumerator SlideTo(float targetX, Color targetColor)
        {
            Vector2 from = _highlight.anchoredPosition;
            Color fromC = _highlightImg.color;
            const float dur = 0.18f;
            float e = 0f;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / dur);
                float s = 1f - (1f - k) * (1f - k); // ease-out
                _highlight.anchoredPosition = new Vector2(Mathf.Lerp(from.x, targetX, s), 0f);
                _highlightImg.color = Color.Lerp(fromC, targetColor, s);
                yield return null;
            }
            _highlight.anchoredPosition = new Vector2(targetX, 0f);
            _highlightImg.color = targetColor;
            _slide = null;
        }

        private void ApplyColors()
        {
            for (int i = 0; i < _labels.Length; i++)
                _labels[i].color = i == _index ? Color.white : Theme.InkSoft;
        }
    }
}
