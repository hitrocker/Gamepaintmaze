using System;
using UnityEngine;
using UnityEngine.EventSystems;
using PaintMaze.Domain;

namespace PaintMaze.Game
{
    /// <summary>
    /// Full-screen pointer surface that emits a cardinal <see cref="SwipeDirection"/>
    /// whenever a held pointer crosses the drag threshold. The gesture re-anchors after
    /// each swipe so players can enter multiple directions without lifting their finger.
    /// </summary>
    public sealed class SwipeInput : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public event Action<SwipeDirection> Swiped;
        public event Action<SwipeDirection> SwipedWhileDisabled;
        public bool Enabled { get; set; } = true;

        private Vector2 _down;
        private bool _active;
        private float _minPixels;

        private void Awake()
        {
            _minPixels = Mathf.Max(35f, Screen.height * 0.035f);
        }

        private void OnDisable()
        {
            // A pointer-up is not guaranteed when the gameplay screen is hidden.
            // Never carry a partial gesture into the next level activation.
            _active = false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            _down = e.position;
            _active = true;
        }

        public void OnDrag(PointerEventData e)
        {
            TryEmitSwipe(e.position);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_active) return;
            TryEmitSwipe(e.position);
            _active = false;
        }

        private bool TryEmitSwipe(Vector2 position)
        {
            if (!_active) return false;

            Vector2 d = position - _down;
            if (d.magnitude < _minPixels) return false;

            _down = position;
            SwipeDirection dir;
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
                dir = d.x > 0 ? SwipeDirection.Right : SwipeDirection.Left;
            else
                dir = d.y > 0 ? SwipeDirection.Up : SwipeDirection.Down;

            if (Enabled) Swiped?.Invoke(dir);
            else SwipedWhileDisabled?.Invoke(dir);
            return true;
        }
    }
}
