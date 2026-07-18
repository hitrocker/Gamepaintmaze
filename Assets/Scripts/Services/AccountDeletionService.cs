using System;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Performs account deletion in a fixed, retry-safe order while progress sync
    /// is paused so deleted leaderboard documents cannot be recreated mid-flow.
    /// </summary>
    public sealed class AccountDeletionService : MonoBehaviour
    {
        public bool IsDeleting { get; private set; }

        private AuthService _auth;
        private LeaderboardService _leaderboard;
        private ProgressSyncService _progressSync;

        public void Init(AuthService auth, LeaderboardService leaderboard,
            ProgressSyncService progressSync)
        {
            _auth = auth;
            _leaderboard = leaderboard;
            _progressSync = progressSync;
        }

        public void DeleteCurrentAccount(string password, string googleToken,
            Action<bool, string> callback)
        {
            if (IsDeleting)
            {
                callback?.Invoke(false, "Account deletion is already in progress.");
                return;
            }

            if (_auth.CurrentUser == null)
            {
                callback?.Invoke(false, "No account is signed in.");
                return;
            }

            string uid = _auth.CurrentUser.UserId;
            IsDeleting = true;
            _progressSync.Suspend();
            _leaderboard.SuspendSync();

            void AfterReauthentication(bool success, string error)
            {
                if (!success)
                {
                    FinishFailure(error, callback);
                    return;
                }
                DeleteCloudData(uid, callback);
            }

            switch (_auth.CurrentReauthenticationProvider)
            {
                case ReauthenticationProvider.Password:
                    _auth.ReauthenticateWithPassword(password, AfterReauthentication);
                    break;
                case ReauthenticationProvider.Google:
                    _auth.ReauthenticateWithGoogleToken(googleToken, AfterReauthentication);
                    break;
                default:
                    AfterReauthentication(true, null);
                    break;
            }
        }

        private void DeleteCloudData(string uid, Action<bool, string> callback)
        {
            _leaderboard.DeleteOwnEntries((cloudDeleted, cloudError) =>
            {
                if (!cloudDeleted)
                {
                    FinishFailure(cloudError, callback);
                    return;
                }

                _auth.DeleteCurrentUser((authDeleted, authError) =>
                {
                    if (!authDeleted)
                    {
                        FinishFailure(authError, callback);
                        return;
                    }

                    SaveService.ClearProgressForUser(uid);
                    SaveService.SetProgressOwner(null);
                    IsDeleting = false;
                    _leaderboard.ResumeSync();
                    _progressSync.Resume();
                    _auth.EnsureSessionSilently();
                    callback?.Invoke(true, "Account and cloud progress deleted.");
                });
            });
        }

        private void FinishFailure(string error, Action<bool, string> callback)
        {
            IsDeleting = false;
            _leaderboard.ResumeSync();
            _progressSync.Resume();
            callback?.Invoke(false, string.IsNullOrEmpty(error)
                ? "Could not delete the account."
                : error);
        }
    }
}
