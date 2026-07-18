using UnityEngine;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    /// <summary>
    /// Screen-space camera shake with a decaying "trauma" model (offset scales with
    /// trauma squared for a snappy punch that settles smoothly). Offsets are applied
    /// in the camera's own right/up axes so the shake reads as screen jitter, and are
    /// undone each frame so it never fights the framing set by AppRoot.FrameCamera.
    /// Attach to the board camera and call <see cref="Shake"/> on impacts.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        public static CameraShake Instance { get; private set; }

        private const float MaxOffset = 0.24f; // world units at full trauma
        private const float Decay = 2.4f;       // trauma lost per second
        private const float Frequency = 26f;    // jitter speed

        private float _trauma;
        private Vector3 _offset;
        private float _seedX, _seedY;

        private void Awake()
        {
            Instance = this;
            _seedX = Random.value * 100f;
            _seedY = Random.value * 100f + 50f;
        }

        /// <summary>Adds trauma (0..1). Bigger impacts should pass a larger amount.</summary>
        public void Shake(float amount)
        {
            if (!SaveService.CameraShakeEnabled)
            {
                _trauma = 0f;
                return;
            }
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        private void LateUpdate()
        {
            // Undo last frame's shake so we always offset from the intended framing.
            transform.position -= _offset;
            _offset = Vector3.zero;

            if (!SaveService.CameraShakeEnabled)
            {
                _trauma = 0f;
                return;
            }
            if (_trauma <= 0f) return;

            _trauma = Mathf.Max(0f, _trauma - Decay * Time.deltaTime);
            float s = _trauma * _trauma;
            float t = Time.time * Frequency;
            float ox = Mathf.PerlinNoise(_seedX, t) * 2f - 1f;
            float oy = Mathf.PerlinNoise(_seedY, t) * 2f - 1f;

            _offset = (transform.right * ox + transform.up * oy) * (MaxOffset * s);
            transform.position += _offset;
        }
    }
}
