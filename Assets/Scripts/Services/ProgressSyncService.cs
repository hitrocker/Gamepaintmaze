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
        private EconomySyncService _economy;
        private ProgressSnapshot _carryForward;
        private EconomySnapshot _carryEconomy;
        private string _carryOwner;
        private FirebaseUser _pendingUser;
        private ProgressSnapshot _pendingCarry;
        private EconomySnapshot _pendingEconomyCarry;
        private bool _transitionInFlight;
        private int _generation;

        public void Init(
            AuthService auth,
            LeaderboardService leaderboard,
            EconomySyncService economy = null)
        {
            _auth = auth;
            _leaderboard = leaderboard;
            _economy = economy;
            _auth.TransitionStarted += HandleTransitionStarted;
            _auth.TransitionCompleted += HandleTransitionCompleted;
            _auth.Failed += HandleAuthFailed;
            _auth.UserChanged += HandleUserChanged;
            _leaderboard.SessionReady += HandleSessionReady;
        }

        public void Suspend()
        {
            IsSuspended = true;
            IsSyncing = false;
            _pendingUser = null;
            _pendingCarry = null;
            _pendingEconomyCarry = null;
            _generation++;
            _economy?.Suspend();
        }

        public void Resume()
        {
            IsSuspended = false;
            _economy?.Resume();
            FirebaseUser user = _auth?.CurrentUser;
            if (user != null) BeginSync(user, null, null);
        }

        private void HandleTransitionStarted(AuthTransitionKind kind, string previousUserId)
        {
            if (kind == AuthTransitionKind.None) return;
            _transitionInFlight = true;
            _economy?.DeferLoads();
            string captureOwner = string.IsNullOrWhiteSpace(previousUserId)
                ? SaveService.CurrentProgressOwner
                : previousUserId.Trim();
            if (_carryForward != null &&
                string.Equals(
                    _carryOwner ?? string.Empty,
                    captureOwner ?? string.Empty,
                    StringComparison.Ordinal))
                return;

            _carryOwner = captureOwner;
            _carryForward = SaveService.CaptureProgressForOwner(captureOwner);
            _carryEconomy = SaveService.CaptureEconomyForOwner(captureOwner);
        }

        private void HandleTransitionCompleted(AuthTransitionKind _, FirebaseUser user)
        {
            _transitionInFlight = false;
            ProgressSnapshot carry = _carryForward;
            EconomySnapshot economyCarry = _carryEconomy;
            ClearCarry();
            if (user == null || IsSuspended)
            {
                FinishDeferredEconomy();
                return;
            }

            BeginSync(user, carry, economyCarry);
        }

        private void HandleAuthFailed(string _)
        {
            if (!_transitionInFlight) return;
            _transitionInFlight = false;
            ClearCarry();
            FinishDeferredEconomy();
        }

        private void HandleUserChanged(FirebaseUser user)
        {
            if (IsSuspended || _transitionInFlight) return;
            if (user == null)
            {
                _pendingUser = null;
                IsSyncing = false;
                _generation++;
                return;
            }

            if (IsSyncing && SaveService.CurrentProgressOwner == user.UserId) return;
            BeginSync(user, _pendingCarry, _pendingEconomyCarry);
        }

        private void HandleSessionReady()
        {
            if (IsSuspended) return;
            FirebaseUser user = _pendingUser ?? _auth.CurrentUser;
            if (user != null) BeginSync(user, _pendingCarry, _pendingEconomyCarry);
        }

        private void BeginSync(
            FirebaseUser user,
            ProgressSnapshot carry,
            EconomySnapshot economyCarry)
        {
            if (user == null || IsSuspended) return;
            if (carry == null) carry = _pendingCarry;
            if (economyCarry == null) economyCarry = _pendingEconomyCarry;
            if (!_leaderboard.IsReady)
            {
                _pendingUser = user;
                _pendingCarry = carry;
                _pendingEconomyCarry = economyCarry;
                return;
            }

            _pendingUser = null;
            _pendingCarry = null;
            _pendingEconomyCarry = null;
            int generation = ++_generation;
            string uid = user.UserId;
            SaveService.AdoptOwner(uid, carry, economyCarry);
            ProgressSnapshot baseProgress = SaveService.CaptureProgressForOwner(uid);
            _economy?.AllowLoads();
            ProgressApplied?.Invoke();
            _economy?.LoadAndMerge(uid);
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

        private void FinishDeferredEconomy()
        {
            _economy?.AllowLoads();
            FirebaseUser user = _auth?.CurrentUser;
            if (!IsSuspended && user != null)
                _economy?.LoadAndMerge(user.UserId);
        }

        private void ClearCarry()
        {
            _carryForward = null;
            _carryEconomy = null;
            _carryOwner = null;
        }

        private void OnDestroy()
        {
            if (_auth != null)
            {
                _auth.TransitionStarted -= HandleTransitionStarted;
                _auth.TransitionCompleted -= HandleTransitionCompleted;
                _auth.Failed -= HandleAuthFailed;
                _auth.UserChanged -= HandleUserChanged;
            }
            if (_leaderboard != null)
                _leaderboard.SessionReady -= HandleSessionReady;
        }
    }
}
