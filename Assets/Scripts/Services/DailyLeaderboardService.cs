using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;

namespace PaintMaze.Services
{
    public sealed class DailyLeaderboardEntry
    {
        public string UserId { get; set; }
        public string DayId { get; set; }
        public string DisplayName { get; set; }
        public int ElapsedMilliseconds { get; set; }
        public int MoveCount { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public bool IsCurrentUser { get; set; }

        public float ElapsedSeconds => ElapsedMilliseconds / 1000f;
    }

    public sealed class DailyLeaderboardResult
    {
        public IReadOnlyList<DailyLeaderboardEntry> Entries { get; set; } =
            Array.Empty<DailyLeaderboardEntry>();
        public DailyLeaderboardEntry OwnEntry { get; set; }
        public int? OwnRank { get; set; }
        public bool IsFromCache { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Best-of-three UTC daily board. New personal bests refresh the server top
    /// ten once so the displayed row and rank share one snapshot; non-improving
    /// attempts reuse the cached result.
    /// </summary>
    public sealed class DailyLeaderboardService : MonoBehaviour
    {
        public const int ResultLimit = 10;
        private const string CollectionName = "dailyScores";
        private const string KeyServerLoadedDay =
            "pm_daily_leaderboard_server_loaded_day";

        private readonly Dictionary<string, DailyLeaderboardResult> _results =
            new();
        private readonly Dictionary<string, PendingScore> _pending = new();
        private readonly Dictionary<string, Action<DailyLeaderboardResult>>
            _pendingCallbacks = new();

        private FirebaseService _firebase;
        private AuthService _auth;
        private FirebaseFirestore _db;
        private string _activeUserId;

        public bool IsReady => _db != null && _auth?.CurrentUser != null;
        public event Action SessionReady;

        public void Init(FirebaseService firebase, AuthService auth)
        {
            _firebase = firebase;
            _auth = auth;
            _firebase.Ready += BindFirestore;
            _auth.UserChanged += HandleUserChanged;
            if (_firebase.IsReady) BindFirestore();
        }

        public void LoadForCompletion(
            DailyPuzzleCompletion completion,
            Action<DailyLeaderboardResult> callback)
        {
            if (completion?.Status == null)
            {
                callback?.Invoke(Unavailable("Daily score is unavailable."));
                return;
            }

            string dayId = completion.Status.DayId;
            int elapsedMilliseconds = SecondsToMilliseconds(
                completion.BestTimeSeconds);
            int moves = completion.BestMoveCount;
            if (ShouldSubmit(completion))
            {
                QueueBest(dayId, elapsedMilliseconds, moves);
                Flush(dayId, callback);
                return;
            }

            if (_results.TryGetValue(dayId, out DailyLeaderboardResult cached))
            {
                callback?.Invoke(CloneResult(cached, true));
                return;
            }

            if (!IsReady)
            {
                callback?.Invoke(Unavailable(
                    "Connect to the internet to load rankings."));
                return;
            }

            LoadBoard(dayId, null, callback);
        }

        public void ForceRefresh(
            string dayId,
            Action<DailyLeaderboardResult> callback)
        {
            if (string.IsNullOrEmpty(dayId) || !IsReady)
            {
                callback?.Invoke(Unavailable(
                    "Connect to the internet to refresh rankings."));
                return;
            }

            PlayerPrefs.DeleteKey(KeyServerLoadedDay);
            LoadBoard(dayId, null, callback, true);
        }

        public void DeleteOwnEntries(
            string uid,
            Action<bool, string> callback)
        {
            if (_db == null || string.IsNullOrEmpty(uid))
            {
                callback?.Invoke(false, "Daily cloud scores are unavailable.");
                return;
            }

            _db.Collection(CollectionName)
                .WhereEqualTo("userId", uid)
                .GetSnapshotAsync(Source.Server)
                .ContinueWithOnMainThread(task =>
                {
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        callback?.Invoke(false, LoadErrorFor(task.Exception));
                        return;
                    }

                    WriteBatch batch = _db.StartBatch();
                    foreach (DocumentSnapshot document in task.Result.Documents)
                        batch.Delete(document.Reference);
                    batch.CommitAsync().ContinueWithOnMainThread(deleteTask =>
                    {
                        if (deleteTask.IsCanceled || deleteTask.IsFaulted)
                        {
                            callback?.Invoke(
                                false,
                                LoadErrorFor(deleteTask.Exception));
                            return;
                        }

                        callback?.Invoke(true, null);
                    });
                });
        }

        public static bool ShouldReplace(
            int remoteMilliseconds,
            int remoteMoves,
            int candidateMilliseconds,
            int candidateMoves) =>
            DailyPuzzleService.IsBetterScore(
                candidateMilliseconds,
                candidateMoves,
                remoteMilliseconds,
                remoteMoves);

        public static bool ShouldSubmit(DailyPuzzleCompletion completion) =>
            completion?.Status != null &&
            completion.IsNewBest &&
            completion.BestTimeSeconds > 0f &&
            completion.BestMoveCount > 0;

        public static bool ShouldFetchServer(
            string dayId,
            string loadedDayId,
            bool forceServer = false) =>
            forceServer ||
            !string.Equals(
                dayId,
                loadedDayId,
                StringComparison.Ordinal);

        public static int ComputeRank(long fasterCount, long equalTimeFewerMoves)
        {
            long faster = Math.Min(
                Math.Max(0L, fasterCount),
                int.MaxValue - 1L);
            long tieBreakers = Math.Min(
                Math.Max(0L, equalTimeFewerMoves),
                int.MaxValue - 1L);
            return (int)(Math.Min(
                int.MaxValue - 1L,
                faster + tieBreakers) + 1L);
        }

        public static IReadOnlyList<DailyLeaderboardEntry> MergeTopTen(
            IEnumerable<DailyLeaderboardEntry> entries,
            DailyLeaderboardEntry ownEntry)
        {
            var byUser = new Dictionary<string, DailyLeaderboardEntry>();
            if (entries != null)
            {
                foreach (DailyLeaderboardEntry entry in entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.UserId))
                        continue;
                    byUser[entry.UserId] = entry;
                }
            }

            if (ownEntry != null && !string.IsNullOrEmpty(ownEntry.UserId))
                byUser[ownEntry.UserId] = ownEntry;

            return byUser.Values
                .OrderBy(entry => entry.ElapsedMilliseconds)
                .ThenBy(entry => entry.MoveCount)
                .ThenBy(entry => entry.SubmittedAtUtc ?? DateTime.MaxValue)
                .Take(ResultLimit)
                .ToArray();
        }

        private void QueueBest(
            string dayId,
            int elapsedMilliseconds,
            int moveCount)
        {
            if (_pending.TryGetValue(dayId, out PendingScore queued) &&
                !ShouldReplace(
                    queued.ElapsedMilliseconds,
                    queued.MoveCount,
                    elapsedMilliseconds,
                    moveCount))
                return;

            _pending[dayId] = new PendingScore
            {
                DayId = dayId,
                ElapsedMilliseconds = elapsedMilliseconds,
                MoveCount = moveCount
            };
        }

        private void Flush(
            string dayId,
            Action<DailyLeaderboardResult> callback)
        {
            if (!IsReady || !_pending.TryGetValue(dayId, out PendingScore pending))
            {
                if (callback != null && _pending.ContainsKey(dayId))
                    _pendingCallbacks[dayId] = callback;
                callback?.Invoke(CachedOrUnavailable(dayId));
                return;
            }

            FirebaseUser user = _auth.CurrentUser;
            string displayName = LeaderboardService.DisplayNameFor(user);
            DocumentReference score = ScoreReference(dayId, user.UserId);
            int effectiveMilliseconds = pending.ElapsedMilliseconds;
            int effectiveMoves = pending.MoveCount;

            _db.RunTransactionAsync(async transaction =>
            {
                DocumentSnapshot snapshot =
                    await transaction.GetSnapshotAsync(score);
                int remoteMilliseconds =
                    ReadInt(snapshot, "elapsedMs");
                int remoteMoves = ReadInt(snapshot, "moveCount");
                if (!ShouldReplace(
                        remoteMilliseconds,
                        remoteMoves,
                        pending.ElapsedMilliseconds,
                        pending.MoveCount))
                {
                    effectiveMilliseconds = remoteMilliseconds;
                    effectiveMoves = remoteMoves;
                    return false;
                }

                var data = new Dictionary<string, object>
                {
                    ["dayId"] = dayId,
                    ["userId"] = user.UserId,
                    ["elapsedMs"] = pending.ElapsedMilliseconds,
                    ["moveCount"] = pending.MoveCount,
                    ["displayName"] = displayName,
                    ["submittedAt"] = FieldValue.ServerTimestamp,
                    ["updatedAt"] = FieldValue.ServerTimestamp
                };
                transaction.Set(score, data);
                return true;
            }).ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogWarning(
                        "[DailyLeaderboard] Score sync deferred: " +
                        task.Exception?.GetBaseException().Message);
                    callback?.Invoke(CachedOrUnavailable(dayId));
                    return;
                }

                if (_pending.TryGetValue(dayId, out PendingScore queued) &&
                    queued.ElapsedMilliseconds == pending.ElapsedMilliseconds &&
                    queued.MoveCount == pending.MoveCount)
                    _pending.Remove(dayId);
                _pendingCallbacks.Remove(dayId);

                var own = new DailyLeaderboardEntry
                {
                    UserId = user.UserId,
                    DayId = dayId,
                    DisplayName = displayName,
                    ElapsedMilliseconds = effectiveMilliseconds,
                    MoveCount = effectiveMoves,
                    IsCurrentUser = true,
                    SubmittedAtUtc = DateTime.UtcNow
                };
                // A committed best can change both the top ten and exact rank.
                // Refresh once now; later non-improving attempts remain cache-only.
                LoadBoard(dayId, own, callback, true);
            });
        }

        private void LoadBoard(
            string dayId,
            DailyLeaderboardEntry knownOwn,
            Action<DailyLeaderboardResult> callback,
            bool forceServer = false)
        {
            bool fetchServer = ShouldFetchServer(
                dayId,
                PlayerPrefs.GetString(KeyServerLoadedDay, string.Empty),
                forceServer);
            Source source = fetchServer
                ? Source.Server
                : Source.Cache;

            LoadBoardAsync(dayId, source, knownOwn, knownOwn != null)
                .ContinueWithOnMainThread(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    DailyLeaderboardResult result = task.Result;
                    _results[dayId] = result;
                    if (source == Source.Server)
                    {
                        PlayerPrefs.SetString(KeyServerLoadedDay, dayId);
                        PlayerPrefs.Save();
                    }
                    callback?.Invoke(result);
                    return;
                }

                if (source == Source.Cache)
                {
                    LoadBoard(dayId, knownOwn, callback, true);
                    return;
                }

                callback?.Invoke(new DailyLeaderboardResult
                {
                    Entries = _results.TryGetValue(
                        dayId,
                        out DailyLeaderboardResult cached)
                        ? cached.Entries
                        : Array.Empty<DailyLeaderboardEntry>(),
                    OwnEntry = cached?.OwnEntry ?? knownOwn,
                    OwnRank = cached?.OwnRank,
                    IsFromCache = cached != null,
                    Error = LoadErrorFor(task.Exception)
                });
            });
        }

        private async Task<DailyLeaderboardResult> LoadBoardAsync(
            string dayId,
            Source source,
            DailyLeaderboardEntry knownOwn,
            bool refreshRank)
        {
            string uid = _auth.CurrentUser.UserId;
            Query query = Scores
                .WhereEqualTo("dayId", dayId)
                .OrderBy("elapsedMs")
                .OrderBy("moveCount")
                .OrderBy("submittedAt")
                .Limit(ResultLimit);
            QuerySnapshot snapshot = await query.GetSnapshotAsync(source);
            List<DailyLeaderboardEntry> entries = snapshot.Documents
                .Select(document => ToEntry(document, uid))
                .Where(entry => entry != null)
                .ToList();

            DailyLeaderboardEntry own = entries.FirstOrDefault(
                entry => entry.IsCurrentUser) ?? knownOwn;
            if (own == null)
            {
                DocumentSnapshot ownSnapshot =
                    await ScoreReference(dayId, uid).GetSnapshotAsync(source);
                if (ownSnapshot.Exists) own = ToEntry(ownSnapshot, uid);
            }

            IReadOnlyList<DailyLeaderboardEntry> merged =
                MergeTopTen(entries, own);
            int? rank = await ResolveRankAsync(
                dayId,
                source,
                merged,
                own,
                refreshRank);
            return new DailyLeaderboardResult
            {
                Entries = merged,
                OwnEntry = own,
                OwnRank = rank,
                IsFromCache = source == Source.Cache
            };
        }

        private async Task<int?> ResolveRankAsync(
            string dayId,
            Source source,
            IReadOnlyList<DailyLeaderboardEntry> entries,
            DailyLeaderboardEntry own,
            bool refreshRank)
        {
            if (own == null) return null;
            if (!refreshRank || source == Source.Server)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].IsCurrentUser ||
                        entries[i].UserId == own.UserId)
                        return i + 1;
                }
            }

            if (!refreshRank &&
                source != Source.Server &&
                _results.TryGetValue(dayId, out DailyLeaderboardResult cached) &&
                cached.OwnRank.HasValue)
                return cached.OwnRank;

            Task<AggregateQuerySnapshot> fasterTask = Scores
                .WhereEqualTo("dayId", dayId)
                .WhereLessThan("elapsedMs", own.ElapsedMilliseconds)
                .Count
                .GetSnapshotAsync(AggregateSource.Server);
            Task<AggregateQuerySnapshot> fewerMovesTask = Scores
                .WhereEqualTo("dayId", dayId)
                .WhereEqualTo("elapsedMs", own.ElapsedMilliseconds)
                .WhereLessThan("moveCount", own.MoveCount)
                .Count
                .GetSnapshotAsync(AggregateSource.Server);
            await Task.WhenAll(fasterTask, fewerMovesTask);
            return ComputeRank(
                fasterTask.Result.Count,
                fewerMovesTask.Result.Count);
        }

        private void BindFirestore()
        {
            if (_db != null) return;
            _db = _firebase.Firestore;
            if (_auth.CurrentUser != null)
                HandleUserChanged(_auth.CurrentUser);
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
            foreach (string dayId in _pending.Keys.ToArray())
            {
                _pendingCallbacks.TryGetValue(
                    dayId,
                    out Action<DailyLeaderboardResult> callback);
                Flush(dayId, callback);
            }
        }

        private CollectionReference Scores =>
            _db.Collection(CollectionName);

        private DocumentReference ScoreReference(string dayId, string uid) =>
            Scores.Document(DocumentId(dayId, uid));

        public static string DocumentId(string dayId, string uid) =>
            (dayId ?? string.Empty) + "_" + (uid ?? string.Empty);

        private static DailyLeaderboardEntry ToEntry(
            DocumentSnapshot document,
            string currentUid)
        {
            int elapsed = ReadInt(document, "elapsedMs");
            int moves = ReadInt(document, "moveCount");
            if (elapsed <= 0 || moves <= 0) return null;

            document.TryGetValue("dayId", out string dayId);
            document.TryGetValue("userId", out string userId);
            document.TryGetValue("displayName", out string displayName);
            DateTime? submittedAt = null;
            if (document.TryGetValue("submittedAt", out Timestamp timestamp))
                submittedAt = timestamp.ToDateTime();

            return new DailyLeaderboardEntry
            {
                UserId = userId,
                DayId = dayId,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? "Player"
                    : displayName,
                ElapsedMilliseconds = elapsed,
                MoveCount = moves,
                SubmittedAtUtc = submittedAt,
                IsCurrentUser = userId == currentUid
            };
        }

        private static int ReadInt(
            DocumentSnapshot snapshot,
            string field)
        {
            if (snapshot == null ||
                !snapshot.Exists ||
                !snapshot.TryGetValue(field, out long value))
                return 0;
            return (int)Math.Min(Math.Max(0L, value), int.MaxValue);
        }

        private DailyLeaderboardResult CachedOrUnavailable(string dayId)
        {
            if (_results.TryGetValue(dayId, out DailyLeaderboardResult result))
                return CloneResult(result, true);
            return Unavailable("Connect to the internet to load rankings.");
        }

        private static DailyLeaderboardResult CloneResult(
            DailyLeaderboardResult source,
            bool fromCache) =>
            new()
            {
                Entries = source.Entries,
                OwnEntry = source.OwnEntry,
                OwnRank = source.OwnRank,
                IsFromCache = fromCache,
                Error = source.Error
            };

        private static DailyLeaderboardResult Unavailable(string error) =>
            new()
            {
                Error = error,
                IsFromCache = true
            };

        private static int SecondsToMilliseconds(float seconds) =>
            (int)Math.Min(
                int.MaxValue,
                Math.Round(Math.Max(0d, seconds) * 1000d));

        private static string LoadErrorFor(Exception exception)
        {
            string message =
                exception?.GetBaseException().Message?.ToLowerInvariant() ??
                string.Empty;
            if (message.Contains("network") ||
                message.Contains("offline") ||
                message.Contains("unavailable") ||
                message.Contains("deadline"))
                return "Connect to the internet to load rankings.";
            if (message.Contains("index") ||
                message.Contains("failed precondition"))
                return "Daily leaderboard index is being prepared.";
            return "Daily leaderboard is unavailable right now.";
        }

        private void OnDestroy()
        {
            if (_firebase != null) _firebase.Ready -= BindFirestore;
            if (_auth != null) _auth.UserChanged -= HandleUserChanged;
        }

        private sealed class PendingScore
        {
            public string DayId;
            public int ElapsedMilliseconds;
            public int MoveCount;
        }
    }
}
