using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// A non-blocking veil that softens an already-completed page swap. It never
    /// delays loading or intercepts input.
    /// </summary>
    public sealed class ScreenRevealController : MonoBehaviour
    {
        public const float InitialAlpha = 0.34f;

        private Image _veil;
        private Coroutine _running;

        public bool IsRevealing => gameObject.activeSelf;

        public void Build(RectTransform root)
        {
            _veil = UiKit.Image(root, "RevealVeil", Theme.Background);
            _veil.raycastTarget = false;
            UiKit.Stretch(_veil.rectTransform);
            gameObject.SetActive(false);
        }

        public void Reveal()
        {
            if (_running != null) StopCoroutine(_running);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _running = StartCoroutine(Fade());
        }

        private IEnumerator Fade()
        {
            Color baseColor = Theme.Background;
            float elapsed = 0f;
            while (elapsed < GameFeedbackProfile.RevealDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / GameFeedbackProfile.RevealDuration);
                float eased = k * k;
                baseColor.a = Mathf.Lerp(InitialAlpha, 0f, eased);
                _veil.color = baseColor;
                yield return null;
            }

            baseColor.a = 0f;
            _veil.color = baseColor;
            _running = null;
            gameObject.SetActive(false);
        }
    }
}
