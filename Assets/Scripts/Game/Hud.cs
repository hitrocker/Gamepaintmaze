using System;
using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Game
{
    /// <summary>
    /// Base HUD contract shared by the in-game UI: the Back/Hint/Restart events the
    /// <see cref="GameController"/> subscribes to, plus the virtual build/update hooks.
    /// The concrete UI is built by <see cref="UIManager"/>, which overrides these.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public event Action Back;
        public event Action Hint;
        public event Action Restart; // available for callers; not shown by default

        protected void RaiseBack() => Back?.Invoke();
        protected void RaiseHint() => Hint?.Invoke();
        protected void RaiseRestart() => Restart?.Invoke();

        public virtual void Build(RectTransform parent, Difficulty difficulty) { }

        public virtual void SetLevel(Difficulty difficulty, int index) { }

        // No progress bar in the reference; kept as a no-op so callers don't branch.
        public virtual void SetProgress(float fraction) { }
    }
}
