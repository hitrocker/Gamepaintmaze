using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PaintMaze.Game
{
    /// <summary>
    /// Adds a tactile press animation to a uGUI button: scale down on pointer-down
    /// and spring back (with optional overshoot) on pointer-up. Coroutine-based, so
    /// it needs no DOTween. Attach to the button's graphic GameObject.
    /// </summary>
    public sealed class UiPressFx : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public RectTransform target;
        public float pressedScale = 0.92f;
        public bool overshoot;            // bounce slightly past 1.0 on release
        public float overshootScale = 1.03f;

        private Coroutine _co;

        public void OnPointerDown(PointerEventData _) => Run(ScaleTo(pressedScale, 0.08f));

        public void OnPointerUp(PointerEventData _) =>
            Run(overshoot ? Spring() : ScaleTo(1f, 0.08f));

        private void Run(IEnumerator routine)
        {
            if (!isActiveAndEnabled) return;
            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(routine);
        }

        private RectTransform T => target != null ? target : (RectTransform)transform;

        private IEnumerator ScaleTo(float to, float dur)
        {
            var rt = T;
            Vector3 from = rt.localScale;
            Vector3 dst = Vector3.one * to;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / Mathf.Max(0.0001f, dur);
                float k = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t)); // ease-out
                rt.localScale = Vector3.LerpUnclamped(from, dst, k);
                yield return null;
            }
            rt.localScale = dst;
        }

        private IEnumerator Spring()
        {
            yield return ScaleTo(overshootScale, 0.06f);
            yield return ScaleTo(1f, 0.06f);
        }
    }
}
