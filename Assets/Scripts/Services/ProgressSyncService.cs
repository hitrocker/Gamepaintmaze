using System;
using Firebase.Auth;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Coordinates account transitions with local and Firestore progress. Gameplay
    /// always reads local progress; cloud restoration updates it asynchronously.
    /// </summary>
    public sealed class ProgressSyncService : MonoBehaviour
    {
        public bool IsSuspended { get; private set; }
        public bool IsSyncing { get; private set; }

        public event Action ProgressApplied;
        public event Action<string> StatusChanged;

        private AuthService _auth;
        private LeaderboardService _leaderboard;
        private ProgressSnapshot _carryForward;
        private string _carryOwner;
        private FirebaseUser _pendingUser;
        private bool _transitionInFlight;
        private int _generation;

        public void Init(AuthService auth, LeaderboardService leaderboard)
        {
            _auth = auth;
            _leaderboard = leaderboard;
            _auth.TransitionStarted += HandleTransitionStarted;
            _auth.TransitionCompleted += HandleTransitionCompleted;
            _auth.UserChanged += HandleUserChanged;
            _leaderboard.SessionReady += HandleSessionReady;
        }

        public void Suspend()
        {
            IsSuspended = true;
            IsSyncing = false;
            _pendingUser = null;
            _generation++;
        }

        public void Resume()
        {
            IsSuspended = false;
            FirebaseUser user = _auth?.CurrentUser;
            if (user != null) BeginSync(user, null);
        }

        private void HandleTransitionStarted(AuthTransitionKind kind, string previousUserId)
        {
            if (kind == AuthTransitionKind.None) return;
            _transitionInFlight = true;
            if (!string.IsNullOrWhiteSpace(previousUserId) &&
                (_carryForward == null || _carryOwner != previousUserId))
            {
                _carryOwner = previousUserId;
                _carryForward = SaveService.CaptureProgressForOwner(previousUserId);
            }
        }

        private void HandleTransitionCompleted(AuthTransitionKind kind, FirebaseUser user)
        {
            _transitionInFlight = false;
            if (user == null || IsSuspended) return;
            ProgressSnapshot carry = kind == AuthTransitionKind.ExistingAccountSignIn
                ? _carryForward
                : null;
            _carryForward = null;
            _carryOwner = null;
            BeginSync(user, carry);
        }

        private void HandleUserChanged(FirebaseUser user)
        {
            if (IsSuspended) return;
            if (user == null)
            {
                _pendingUser = null;
                IsSyncing = false;
                _generation++;
                return;
            }

            if (_transitionInFlight) return;
            if (IsSyncing && SaveService.CurrentProgressOwner == user.UserId) return;
            BeginSync(user, null);
        }

        private void HandleSessionReady()
        {
            if (IsSuspended) return;
            FirebaseUser user = _pendingUser ?? _auth.CurrentUser;
            if (user != null) BeginSync(user, null);
        }

        private void BeginSync(FirebaseUser user, ProgressSnapshot carry)
        {
            if (user == null || IsSuspended) return;
            if (!_leaderboard.IsReady)
            {
                _pendingUser = user;
                return;
            }

            _pendingUser = null;
            int generation = ++_generation;
            string uid = user.UserId;
            ProgressSnapshot accountLocal = SaveService.CaptureProgressForOwner(uid);
            ProgressSnapshot baseProgress =
                ProgressSnapshot.MergeMax(accountLocal, carry);

            SaveService.SetProgressOwner(uid);
            SaveService.ApplyProgress(uid, baseProgress);
            ProgressApplied?.Invoke();
            IsSyncing = true;
            StatusChanged?.Invoke("Restoring progress...");

            _leaderboard.LoadOwnProgress(result =>
            {
                if (generation != _generation || IsSuspended ||
                    _auth.CurrentUser?.UserId != uid)
                    return;

                ProgressSnapshot merged = ProgressSnapshot.MergeMax(
                    baseProgress, result.Progress);
                SaveService.SetProgressOwner(uid);
                SaveService.ApplyProgress(uid, merged);
                ProgressApplied?.Invoke();

                if (!result.IsFinal) return;
                IsSyncing = false;
                _leaderboard.BackfillFromLocalSave();
                StatusChanged?.Invoke(string.IsNullOrEmpty(result.Error)
                    ? "Progress restored."
                    : "Using saved progress. Cloud sync will retry later.");
            });
        }

        private void OnDestroy()
        {
            if (_auth != null)
            {
                _auth.TransitionStarted -= HandleTransitionStarted;
                _auth.TransitionCompleted -= HandleTransitionCompleted;
                _auth.UserChanged -= HandleUserChanged;
            }
            if (_leaderboard != null)
                _leaderboard.SessionReady -= HandleSessionReady;
        }
    }
}
