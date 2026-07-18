using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PaintMaze.Core;
using PaintMaze.Domain;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    /// <summary>
    /// Drives one level on the 2.5D board: routes swipes through MovementSystem,
    /// animates the roll in world space while painting the trail, surfaces solver
    /// hints as progress-scoped next-move guidance, supports restart, and reports
    /// completion.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public event Action Completed;
        public event Action StateCommitted;
        public event Action Restarted;

        private readonly MovementSystem _movement = new MovementSystem();
        private readonly Solver _solver = new Solver();

        private BoardView3D _board;
        private SwipeInput _swipe;
        private Hud _hud;
        private Color _paint;

        private Level _level;
        private GameState _state;
        private bool _animating;
        private bool _hasBufferedSwipe;
        private SwipeDirection _bufferedSwipe;
        private bool _firstSwipeLogged;
        private float _levelReadyTime;
        private int _paintFeedbackSequence;
        private Coroutine _tutorialHintRoutine;
        private readonly ProgressHintSession _hintGuidance = new();

        public Level Level => _level;
        public bool IsComplete => _state?.IsComplete ?? false;
        public bool HintGuidanceActive => _hintGuidance.Active;
        public float LongDistanceThreshold
        {
            get => longDistanceThreshold;
            set => longDistanceThreshold = Mathf.Max(0.0001f, value);
        }
        public float BallSpeedIncWhenLongDist
        {
            get => ballSpeedIncWhenLongDist;
            set => ballSpeedIncWhenLongDist = Mathf.Max(0f, value);
        }

        public void Setup(BoardView3D board, SwipeInput swipe, Hud hud)
        {
            _board = board;
            _swipe = swipe;
            _hud = hud;
            _swipe.Swiped += HandleSwipe;
            _swipe.SwipedWhileDisabled += BufferSwipe;
            _hud.Hint += ShowHint;
            _hud.Restart += Restart;
        }

        public void Load(Level level, GameState restoredState = null)
        {
            CancelTutorialHint();
            EndHintGuidance();
            _level = level;
            _paint = Theme.Paint(level.Index);
            _state = restoredState != null &&
                     ReferenceEquals(restoredState.Level, level)
                ? restoredState
                : new GameState(level);

            _board.Build(level, _paint);
            foreach (Position position in _state.Painted)
                _board.SetPainted(position, false);
            _board.SetBallCell(_state.BallPos);
            _hud.SetLevel(level.Difficulty, level.Index);
            _hud.SetProgress(_state.ProgressFraction);
            _spin = Quaternion.identity;
            _board.ResetBallSpin();
            _animating = false;
            _hasBufferedSwipe = false;
            _firstSwipeLogged = false;
            _paintFeedbackSequence = 0;
            _levelReadyTime = Time.realtimeSinceStartup;
            _swipe.Enabled = true;

            if (level.Index == 1 && _state.MoveCount == 0)
                _tutorialHintRoutine = StartCoroutine(TutorialHint());
        }

        public ActiveLevelSnapshot CaptureSnapshot(string progressOwner) =>
            ActiveLevelSnapshot.From(_state, progressOwner);

        private void HandleSwipe(SwipeDirection dir)
        {
            if (_animating)
            {
                BufferSwipe(dir);
                return;
            }
            if (_state == null || _state.IsComplete) return;
            _board.EnsureMotionVfx();
            var (stop, path) = _movement.Slide(_level, _state.BallPos, dir);
            if (stop == _state.BallPos || path.Count == 0) return;
            _board.ClearRouteHint();
            if (!_firstSwipeLogged)
            {
                _firstSwipeLogged = true;
                Debug.Log($"[Perf] firstSwipeAccepted after=" +
                          $"{(Time.realtimeSinceStartup - _levelReadyTime) * 1000f:F0}ms");
            }
            StartCoroutine(AnimateMove(_state.BallPos, path));
        }

        private void BufferSwipe(SwipeDirection dir)
        {
            if (!_animating || _state == null || _state.IsComplete) return;
            _bufferedSwipe = dir;
            _hasBufferedSwipe = true;
        }

        private bool CanChainBufferedSwipe()
        {
            if (!_hasBufferedSwipe || _state == null || _state.IsComplete) return false;
            var (_, path) = _movement.Slide(_level, _state.BallPos, _bufferedSwipe);
            return path.Count > 0;
        }

        [SerializeField, Min(0.0001f)]
        [Tooltip("World-space slide distance before the long-distance speed bonus begins.")]
        private float longDistanceThreshold = 4f;

        [SerializeField, Min(0f)]
        [Tooltip("Extra world units/second per world unit beyond the threshold.")]
        private float ballSpeedIncWhenLongDist = 2f;

        private const float SpinGain = 0.5f;   // visible roll spin scale
        private const bool DEBUG_BALL = false; // adb logcat instrumentation (dev only)

        private Quaternion _spin = Quaternion.identity;

        private IEnumerator AnimateMove(Position from, List<Position> path)
        {
            _animating = true;
            _swipe.Enabled = false;

            Vector3 start = _board.BallLocalFor(from);
            var stop = path[path.Count - 1];
            Vector3 stopCenter = _board.BallLocalFor(stop);
            int cells = path.Count;
            float worldDistance = Vector3.Distance(start, stopCenter);
            var motion = new BallSlideMotion(
                cells, worldDistance, longDistanceThreshold, ballSpeedIncWhenLongDist);

            Vector3 dir = (stopCenter - start).sqrMagnitude > 1e-6f
                ? (stopCenter - start).normalized : Vector3.zero;
            Vector3 spinAxis = Vector3.Cross(Vector3.up, dir); // rolling axis
            float radius = Mathf.Max(0.01f, _board.BallBaseScale.x * 0.5f);
            Vector3 prevPos = start;
            int painted = 0;
            bool impacted = false;
            float maxOvershoot = 0f;

            while (motion.InputLocked)
            {
                BallSlideStep step = motion.Advance(Time.deltaTime);

                if (!impacted)
                {
                    BallSlideMotion.PathSample(
                        motion.TravelCells, cells, out int segmentIndex, out float segmentT);
                    Vector3 segFrom = segmentIndex == 0
                        ? start
                        : _board.BallLocalFor(path[segmentIndex - 1]);
                    Vector3 segTo = _board.BallLocalFor(path[segmentIndex]);
                    Vector3 pos = Vector3.Lerp(segFrom, segTo, segmentT);
                    _board.SetBallLocal(pos);
                    SpinBy(pos, ref prevPos, dir, spinAxis, radius);

                    // EnteredCellCount is cumulative, so this catches every crossed tile
                    // even when one rendered frame advances across multiple boundaries.
                    while (painted < motion.EnteredCellCount)
                        PaintTile(path[painted++]);
                }

                if (step.Arrived)
                {
                    _board.SetBallLocal(stopCenter);
                    while (painted < cells) PaintTile(path[painted++]);

                    // Commit gameplay state at contact; all remaining motion is cosmetic.
                    _state.Apply(stop, path);
                    _hud.SetProgress(_state.ProgressFraction);
                    StateCommitted?.Invoke();
                    ImpactFeedback impact =
                        GameFeedbackProfile.Impact(cells, _board.QualityTier);
                    if (AudioService.Instance != null)
                        AudioService.Instance.PlayWall(impact);
                    _board.BurstSparks(impact.SparkCount);
                    if (CameraShake.Instance != null)
                        CameraShake.Instance.Shake(impact.Shake);
                    impacted = true;
                }

                if (impacted && motion.Phase == BallSlidePhase.Settle)
                {
                    if (CanChainBufferedSwipe())
                    {
                        motion.TrySkipSettle();
                    }
                    else
                    {
                        float offset = motion.SettleOffsetCells * BoardView3D.Size;
                        maxOvershoot = Mathf.Max(maxOvershoot, offset);
                        Vector3 pos = stopCenter + dir * offset;
                        _board.SetBallLocal(pos);
                        ApplySquash(dir, motion.Squash);
                        SpinBy(pos, ref prevPos, dir, spinAxis, radius);
                    }
                }

                if (motion.InputLocked) yield return null;
            }

            // Force the exact resting state so nothing drifts off-grid.
            _board.SetBallLocal(stopCenter);
            _board.ResetBallScale();

            if (DEBUG_BALL)
                Debug.Log($"[Ball] cells={cells} vImpact={motion.ArrivalVelocity:F2} " +
                          $"maxOvershoot={maxOvershoot:F3} settle={motion.SettleElapsed:F2}s");

            _animating = false;
            _swipe.Enabled = !_state.IsComplete;
            if (_hintGuidance.ShouldEndAtRest(_state.IsComplete))
                EndHintGuidance();

            if (_state.IsComplete)
            {
                _hasBufferedSwipe = false;
                yield return new WaitForSecondsRealtime(
                    GameFeedbackProfile.CompletionOverlayDelay);
                if (AudioService.Instance != null)
                {
                    AudioService.Instance.PlayComplete();
                    AudioService.Instance.HapticComplete();
                }
                yield return _board.PlayCompletionPulse();
                Completed?.Invoke();
            }
            else if (_hasBufferedSwipe)
            {
                SwipeDirection next = _bufferedSwipe;
                _hasBufferedSwipe = false;
                HandleSwipe(next);
                if (!_animating && _hintGuidance.ShouldRefreshAtRest(_state.IsComplete))
                    ShowNextProgressHint();
            }
            else if (_hintGuidance.ShouldRefreshAtRest(_state.IsComplete))
                ShowNextProgressHint();
        }

        // Paints one tile the ball rolls over, with a light haptic tick per tile so the
        // roll feels like a rapid rattle synced to motion.
        private void PaintTile(Position p)
        {
            bool newlyPainted = !_state.Painted.Contains(p);
            _hintGuidance.ObserveTile(newlyPainted);
            PaintFeedback feedback = GameFeedbackProfile.Paint(
                _paintFeedbackSequence++, newlyPainted, _board.QualityTier);
            _board.SetPainted(p);
            _board.EmitPaintSplash(p, feedback);
            if (AudioService.Instance != null)
                AudioService.Instance.PlayPaint(feedback);
        }

        // Accumulates a rolling spin from how far the ball moved along its travel axis.
        private void SpinBy(Vector3 pos, ref Vector3 prevPos, Vector3 dir, Vector3 axis, float radius)
        {
            float d = Vector3.Dot(pos - prevPos, dir);
            prevPos = pos;
            if (Mathf.Abs(d) < 1e-6f || axis.sqrMagnitude < 1e-6f) return;
            float deg = (d / radius) * Mathf.Rad2Deg * SpinGain;
            _spin = Quaternion.AngleAxis(deg, axis) * _spin;
            _board.SetBallSpin(_spin);
        }

        // Squash along the travel axis, stretch on the other two (volume-ish preserved look).
        private void ApplySquash(Vector3 dir, float sq)
        {
            Vector3 b = _board.BallBaseScale;
            float along = 1f - sq;
            float perp = 1f + sq * 0.5f;
            Vector3 absd = new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));
            float sx = Mathf.Lerp(perp, along, absd.x);
            float sz = Mathf.Lerp(perp, along, absd.z);
            _board.SetBallScale(new Vector3(b.x * sx, b.y * perp, b.z * sz));
        }

        private void Restart()
        {
            if (_level == null) return;
            Restarted?.Invoke();
            Load(_level);
        }

        private void ShowHint()
        {
            if (_animating || _state == null || _state.IsComplete) return;

            _hintGuidance.Begin();
            ShowNextProgressHint();
        }

        private void ShowNextProgressHint()
        {
            if (!_hintGuidance.Active || _animating ||
                _state == null || _state.IsComplete)
                return;

            SwipeDirection? direction = _solver.SuggestProgressMove(
                _level, _state.BallPos, _state.Painted);
            if (!direction.HasValue)
            {
                EndHintGuidance();
                return;
            }

            List<HintRouteSegment> route = HintRouteBuilder.Build(
                _level, _state.BallPos, new[] { direction.Value });
            if (route.Count == 0)
            {
                EndHintGuidance();
                return;
            }

            _board.ShowRouteHint(route);
        }

        private void EndHintGuidance()
        {
            _hintGuidance.End();
            _board?.ClearRouteHint();
        }

        private IEnumerator TutorialHint()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            _tutorialHintRoutine = null;
            if (_animating || _state == null || _state.IsComplete) yield break;
            ShowHint();
        }

        private void CancelTutorialHint()
        {
            if (_tutorialHintRoutine != null)
            {
                StopCoroutine(_tutorialHintRoutine);
                _tutorialHintRoutine = null;
            }
        }

        private void OnDisable()
        {
            CancelTutorialHint();
            EndHintGuidance();
        }
    }
}
