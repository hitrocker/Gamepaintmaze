using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Services
{
    public sealed class LeaderboardEntry
    {
        public string UserId { get; set; }
        public string DisplayName { get; set; }
        public int HighestLevel { get; set; }
        public DateTime? ReachedAtUtc { get; set; }
        public bool IsCurrentUser { get; set; }
    }

    public sealed class LeaderboardLoadResult
    {
        public IReadOnlyList<LeaderboardEntry> Entries { get; set; }
        public LeaderboardEntry OwnEntry { get; set; }
        public int? OwnRank { get; set; }
        public bool IsFromCache { get; set; }
        public string Error { get; set; }
    }

    public sealed class RemoteProgressLoadResult
    {
        public ProgressSnapshot Progress { get; set; }
        public bool IsFromCache { get; set; }
        public bool IsFinal { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Direct-Firestore progress leaderboard. Writes are monotonic transactions;
    /// local progression remains authoritative for gameplay and provides retries.
    /// </summary>
    public sealed class LeaderboardService : MonoBehaviour
    {
        private const int ResultLimit = 50;

        private readonly Dictionary<Difficulty, int> _pending = new();

        private FirebaseService _firebase;
        private AuthService _auth;
        private FirebaseFirestore _db;
        private string _activeUserId;
        private bool _suspended;

        public bool IsReady => !_suspended && _db != null && _auth?.CurrentUser != null;
        public event Action SessionReady;

        public void Init(FirebaseService firebase, AuthService auth)
        {
            _firebase = firebase;
            _auth = auth;
            _firebase.Ready += BindFirestore;
            _auth.UserChanged += HandleUserChanged;
            _auth.DisplayNameChanged += HandleDisplayNameChanged;
            if (_firebase.IsReady) BindFirestore();
        }

        public void TryRaiseCompletedLevel(Difficulty difficulty, int completedLevel)
        {
            difficulty = DifficultyCatalog.NormalizePlayable(difficulty);
            completedLevel = Mathf.Clamp(
                completedLevel, 1, DifficultyConfig.PracticalMaxLevel);
            QueuePending(difficulty, completedLevel);
            FlushPending(difficulty);
        }

        public void BackfillFromLocalSave()
        {
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                int completed = CompletedLevelForHighestUnlocked(
                    SaveService.HighestUnlocked(difficulty));
                if (completed > 0) QueuePending(difficulty, completed);
            }

            FlushAllPending();
        }

        public void SuspendSync()
        {
            _suspended = true;
            _pending.Clear();
        }

        public void ResumeSync()
        {
            _suspended = false;
            FirebaseUser user = _auth?.CurrentUser;
            if (_db != null && user != null)
            {
                _activeUserId = user.UserId;
                SessionReady?.Invoke();
            }
        }

        public void DeleteOwnEntries(Action<bool, string> callback)
        {
            FirebaseUser user = _auth?.CurrentUser;
            if (_db == null || user == null)
            {
                callback?.Invoke(false, "Cloud data is not available.");
                return;
            }

            string uid = user.UserId;
            WriteBatch batch = _db.StartBatch();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
                batch.Delete(EntryReference(difficulty, uid));
            batch.CommitAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    callback?.Invoke(false,
                        "Could not remove cloud progress. Check your connection and try again.");
                    return;
                }
                callback?.Invoke(true, null);
            });
        }

        public void LoadTop(Difficulty difficulty, Action<LeaderboardLoadResult> callback)
        {
            difficulty = DifficultyCatalog.NormalizePlayable(difficulty);
            if (!IsReady)
            {
                callback?.Invoke(new LeaderboardLoadResult
                {
                    Entries = Array.Empty<LeaderboardEntry>(),
                    Error = "Leaderboard is connecting."
                });
                return;
            }

            bool deliveredCache = false;
            LoadSourceAsync(difficulty, Source.Cache).ContinueWithOnMainThread(cacheTask =>
            {
                if (cacheTask.Status == TaskStatus.RanToCompletion &&
                    cacheTask.Result.Entries.Count > 0)
                {
                    deliveredCache = true;
                    callback?.Invoke(cacheTask.Result);
                }

                LoadSourceAsync(difficulty, Source.Server).ContinueWithOnMainThread(serverTask =>
                {
                    if (serverTask.Status == TaskStatus.RanToCompletion)
                    {
                        callback?.Invoke(serverTask.Result);
                        return;
                    }

                    if (!deliveredCache)
                    {
                        callback?.Invoke(new LeaderboardLoadResult
                        {
                            Entries = Array.Empty<LeaderboardEntry>(),
                            Error = LoadErrorFor(serverTask.Exception)
                        });
                    }
                });
            });
        }

        public void LoadOwnProgress(Action<RemoteProgressLoadResult> callback)
        {
            if (!IsReady)
            {
                callback?.Invoke(new RemoteProgressLoadResult
                {
                    Progress = new ProgressSnapshot(),
                    IsFinal = true,
                    Error = "Progress sync is connecting."
                });
                return;
            }

            string uid = _auth.CurrentUser.UserId;
            LoadOwnProgressSourceAsync(uid, Source.Cache)
                .ContinueWithOnMainThread(cacheTask =>
                {
                    if (cacheTask.Status == TaskStatus.RanToCompletion)
                    {
                        RemoteProgressLoadResult cached = cacheTask.Result;
                        cached.IsFromCache = true;
                        callback?.Invoke(cached);
                    }

                    LoadOwnProgressSourceAsync(uid, Source.Server)
                        .ContinueWithOnMainThread(serverTask =>
                        {
                            if (serverTask.Status == TaskStatus.RanToCompletion)
                            {
                                RemoteProgressLoadResult server = serverTask.Result;
                                server.IsFinal = true;
                                callback?.Invoke(server);
                                return;
                            }

                            callback?.Invoke(new RemoteProgressLoadResult
                            {
                                Progress = new ProgressSnapshot(),
                                IsFinal = true,
                                Error = LoadErrorFor(serverTask.Exception)
                            });
                        });
                });
        }

        public static string BoardIdFor(Difficulty difficulty) =>
            DifficultyCatalog.NormalizePlayable(difficulty) switch
            {
                Difficulty.Easy => "easy",
                Difficulty.Medium => "medium",
                Difficulty.Hard => "hard",
                Difficulty.ExtraHard => "extraHard",
                _ => "easy"
            };

        public static string DisplayNameFor(FirebaseUser user)
        {
            return DisplayNameFor(user?.UserId, user?.DisplayName);
        }

        public static string DisplayNameFor(string uid, string displayName)
        {
            if (AuthService.IsValidDisplayName(displayName))
                return displayName.Trim();

            uid ??= string.Empty;
            string suffix = uid.Length <= 4
                ? uid.ToUpperInvariant()
                : uid.Substring(uid.Length - 4).ToUpperInvariant();
            if (string.IsNullOrEmpty(suffix)) suffix = "0000";
            return "Guest " + suffix;
        }

        public static bool ShouldRaise(int remoteLevel, int completedLevel) =>
            completedLevel > remoteLevel;

        public static int CompletedLevelForHighestUnlocked(int highestUnlocked) =>
            Mathf.Max(0, highestUnlocked - 1);

        public static int HighestUnlockedForCompletedLevel(int completedLevel) =>
            Mathf.Clamp(completedLevel + 1, 1, DifficultyConfig.PracticalMaxLevel);

        public static int ComputeGlobalRank(
            long higherLevelCount, long earlierSameLevelCount)
        {
            long higher = Math.Min(
                Math.Max(0L, higherLevelCount), int.MaxValue - 1L);
            long earlier = Math.Min(
                Math.Max(0L, earlierSameLevelCount), int.MaxValue - 1L);
            long ahead = Math.Min(int.MaxValue - 1L, higher + earlier);
            return (int)(ahead + 1L);
        }

        private void BindFirestore()
        {
            if (_db != null) return;
            _db = _firebase.Firestore;
            if (_auth.CurrentUser != null) HandleUserChanged(_auth.CurrentUser);
        }

        private void HandleUserChanged(FirebaseUser user)
        {
            if (user == null)
            {
                _activeUserId = null;
                return;
            }
            if (_db == null || user.UserId == _activeUserId) return;
            _activeUserId = user.UserId;
            SessionReady?.Invoke();
        }

        private void HandleDisplayNameChanged(string _)
        {
            SyncDisplayName();
        }

        private void QueuePending(Difficulty difficulty, int completedLevel)
        {
            if (_pending.TryGetValue(difficulty, out int current) && current >= completedLevel)
                return;
            _pending[difficulty] = completedLevel;
        }

        private void FlushAllPending()
        {
            foreach (Difficulty difficulty in _pending.Keys.ToArray())
                FlushPending(difficulty);
        }

        private void FlushPending(Difficulty difficulty)
        {
            if (!IsReady || !_pending.TryGetValue(difficulty, out int completedLevel)) return;

            FirebaseUser user = _auth.CurrentUser;
            DocumentReference entry = EntryReference(difficulty, user.UserId);
            string displayName = DisplayNameFor(user);

            _db.RunTransactionAsync(async transaction =>
            {
                DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(entry);
                int remoteLevel = snapshot.Exists &&
                                  snapshot.TryGetValue("highestLevel", out long stored)
                    ? (int)Math.Min(stored, int.MaxValue)
                    : 0;
                if (!ShouldRaise(remoteLevel, completedLevel)) return false;

                var data = new Dictionary<string, object>
                {
                    ["highestLevel"] = completedLevel,
                    ["displayName"] = displayName,
                    ["reachedAt"] = FieldValue.ServerTimestamp,
                    ["updatedAt"] = FieldValue.ServerTimestamp
                };
                transaction.Set(entry, data, SetOptions.MergeAll);
                return true;
            }).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogWarning("[Leaderboard] Progress sync deferred: " +
                                     task.Exception?.GetBaseException().Message);
                    return;
                }

                if (_pending.TryGetValue(difficulty, out int queued) &&
                    queued <= completedLevel)
                    _pending.Remove(difficulty);
            });
        }

        private void SyncDisplayName()
        {
            if (!IsReady) return;
            FirebaseUser user = _auth.CurrentUser;
            string name = DisplayNameFor(user);
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                DocumentReference entry = EntryReference(difficulty, user.UserId);
                entry.GetSnapshotAsync(Source.Default).ContinueWithOnMainThread(task =>
                {
                    if (task.IsCanceled || task.IsFaulted || !task.Result.Exists) return;
                    entry.UpdateAsync(new Dictionary<string, object>
                    {
                        ["displayName"] = name,
                        ["updatedAt"] = FieldValue.ServerTimestamp
                    });
                });
            }
        }

        private async Task<LeaderboardLoadResult> LoadSourceAsync(
            Difficulty difficulty, Source source)
        {
            string uid = _auth.CurrentUser.UserId;
            Query query = Entries(difficulty)
                .OrderByDescending("highestLevel")
                .OrderBy("reachedAt")
                .Limit(ResultLimit);
            QuerySnapshot snapshot = await query.GetSnapshotAsync(source);
            var entries = snapshot.Documents
                .Select(document => ToEntry(document, uid))
                .Where(entry => entry != null)
                .ToList();

            LeaderboardEntry ownEntry = entries.FirstOrDefault(entry => entry.IsCurrentUser);
            if (ownEntry == null)
            {
                DocumentSnapshot ownSnapshot =
                    await EntryReference(difficulty, uid).GetSnapshotAsync(source);
                if (ownSnapshot.Exists) ownEntry = ToEntry(ownSnapshot, uid);
            }
            int? ownRank = await ResolveOwnRankAsync(
                difficulty, source, entries, ownEntry);

            return new LeaderboardLoadResult
            {
                Entries = entries,
                OwnEntry = ownEntry,
                OwnRank = ownRank,
                IsFromCache = source == Source.Cache
            };
        }

        private async Task<int?> ResolveOwnRankAsync(
            Difficulty difficulty,
            Source source,
            IReadOnlyList<LeaderboardEntry> entries,
            LeaderboardEntry ownEntry)
        {
            if (ownEntry == null) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].IsCurrentUser) return i + 1;

            if (source != Source.Server || !ownEntry.ReachedAtUtc.HasValue)
                return null;

            CollectionReference collection = Entries(difficulty);
            Task<AggregateQuerySnapshot> higherTask = collection
                .WhereGreaterThan("highestLevel", ownEntry.HighestLevel)
                .Count
                .GetSnapshotAsync(AggregateSource.Server);

            Timestamp reachedAt = Timestamp.FromDateTime(
                ownEntry.ReachedAtUtc.Value.ToUniversalTime());
            Task<AggregateQuerySnapshot> earlierTieTask = collection
                .WhereEqualTo("highestLevel", ownEntry.HighestLevel)
                .WhereLessThan("reachedAt", reachedAt)
                .Count
                .GetSnapshotAsync(AggregateSource.Server);

            await Task.WhenAll(higherTask, earlierTieTask);
            return ComputeGlobalRank(
                higherTask.Result.Count, earlierTieTask.Result.Count);
        }

        private async Task<RemoteProgressLoadResult> LoadOwnProgressSourceAsync(
            string uid, Source source)
        {
            var progress = new ProgressSnapshot();
            foreach (Difficulty difficulty in DifficultyCatalog.Playable)
            {
                DocumentSnapshot snapshot =
                    await EntryReference(difficulty, uid).GetSnapshotAsync(source);
                if (!snapshot.Exists ||
                    !snapshot.TryGetValue("highestLevel", out long completed))
                    continue;

                int safeCompleted = (int)Math.Min(completed, int.MaxValue);
                progress.SetHighestUnlocked(difficulty,
                    HighestUnlockedForCompletedLevel(safeCompleted));
            }

            return new RemoteProgressLoadResult
            {
                Progress = progress,
                IsFromCache = source == Source.Cache
            };
        }

        private CollectionReference Entries(Difficulty difficulty) =>
            _db.Collection("leaderboards")
                .Document(BoardIdFor(difficulty))
                .Collection("entries");

        private DocumentReference EntryReference(Difficulty difficulty, string uid) =>
            Entries(difficulty).Document(uid);

        private static LeaderboardEntry ToEntry(DocumentSnapshot document, string currentUid)
        {
            if (!document.TryGetValue("highestLevel", out long level)) return null;
            document.TryGetValue("displayName", out string displayName);
            DateTime? reachedAt = null;
            if (document.TryGetValue("reachedAt", out Timestamp timestamp))
                reachedAt = timestamp.ToDateTime();

            return new LeaderboardEntry
            {
                UserId = document.Id,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                HighestLevel = Mathf.Clamp((int)Math.Min(level, int.MaxValue),
                    1, DifficultyConfig.PracticalMaxLevel),
                ReachedAtUtc = reachedAt,
                IsCurrentUser = document.Id == currentUid
            };
        }

        private static string LoadErrorFor(Exception exception)
        {
            string message = exception?.GetBaseException().Message?.ToLowerInvariant() ??
                             string.Empty;
            if (message.Contains("network") || message.Contains("offline") ||
                message.Contains("unavailable") || message.Contains("deadline"))
                return "Connect to the internet to load rankings.";
            if (message.Contains("index") || message.Contains("failed precondition"))
                return "Leaderboard index is being prepared.";
            return "Leaderboard is unavailable right now.";
        }

        private void OnDestroy()
        {
            if (_firebase != null) _firebase.Ready -= BindFirestore;
            if (_auth != null)
            {
                _auth.UserChanged -= HandleUserChanged;
                _auth.DisplayNameChanged -= HandleDisplayNameChanged;
            }
        }
    }
}
