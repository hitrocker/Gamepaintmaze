using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Syncs cosmetic economy state to Firestore <c>users/{uid}</c>.
    /// </summary>
    public sealed class EconomySyncService : MonoBehaviour
    {
        private const string CollectionName = "users";

        public event Action EconomyApplied;

        private FirebaseService _firebase;
        private AuthService _auth;
        private FirebaseFirestore _db;
        private int _generation;
        private bool _pushQueued;
        private bool _loadsDeferred;
        private string _pendingUserId;

        public bool IsReady => _db != null && _auth?.CurrentUser != null;
        public bool IsSuspended { get; private set; }

        public void Init(FirebaseService firebase, AuthService auth)
        {
            _firebase = firebase;
            _auth = auth;
            _firebase.Ready += BindFirestore;
            _auth.UserChanged += HandleUserChanged;
            if (_firebase.IsReady) BindFirestore();
        }

        public void Suspend()
        {
            IsSuspended = true;
            _generation++;
            _pushQueued = false;
            _pendingUserId = null;
        }

        public void Resume()
        {
            IsSuspended = false;
            FirebaseUser user = _auth?.CurrentUser;
            if (user != null) LoadAndMerge(user.UserId);
        }

        public void DeferLoads()
        {
            _loadsDeferred = true;
            _generation++;
        }

        public void AllowLoads()
        {
            _loadsDeferred = false;
        }

        public void LoadAndMerge(string userId = null)
        {
            if (IsSuspended || _loadsDeferred) return;
            string uid = string.IsNullOrWhiteSpace(userId)
                ? _auth?.CurrentUser?.UserId
                : userId.Trim();
            if (string.IsNullOrEmpty(uid)) return;
            if (_db == null)
            {
                _pendingUserId = uid;
                return;
            }

            _pendingUserId = null;
            int generation = ++_generation;
            DocumentReference reference = UserReference(uid);
            reference.GetSnapshotAsync(Source.Server)
                .ContinueWithOnMainThread(task =>
                {
                    if (generation != _generation || IsSuspended || _loadsDeferred) return;
                    if (_auth.CurrentUser?.UserId != uid) return;

                    if (task.IsCanceled || task.IsFaulted)
                    {
                        Debug.LogWarning(
                            "[Economy] Load deferred: " +
                            task.Exception?.GetBaseException().Message);
                        return;
                    }

                    EconomySnapshot remote = FromDocument(task.Result);
                    SaveService.SetProgressOwner(uid);
                    EconomySnapshot local = SaveService.CaptureEconomyForOwner(uid);
                    EconomySnapshot merged = EconomySnapshot.MergeMax(local, remote);
                    SaveService.ApplyEconomy(uid, merged, mergeWithExisting: false);
                    EconomyApplied?.Invoke();
                    PushLocal(uid);
                });
        }

        public void PushLocal(string userId = null)
        {
            if (IsSuspended || _loadsDeferred) return;
            string uid = string.IsNullOrWhiteSpace(userId)
                ? _auth?.CurrentUser?.UserId
                : userId.Trim();
            if (string.IsNullOrEmpty(uid)) return;
            if (_db == null)
            {
                _pushQueued = true;
                _pendingUserId = uid;
                return;
            }

            EconomySnapshot economy = SaveService.CaptureEconomyForOwner(uid);
            string hintDayId = SaveService.NormalizeDayId(economy.DailyHintDayId);
            if (string.IsNullOrEmpty(hintDayId))
                hintDayId = DailyPuzzleService.DayId(DateTime.UtcNow);
            var data = new Dictionary<string, object>
            {
                ["coins"] = economy.Coins,
                ["ownedSkinIds"] = economy.OwnedSkinIds.ToList(),
                ["equippedSkinId"] = economy.EquippedSkinId ?? SaveService.DefaultSkinId,
                ["dailyHintDayId"] = hintDayId,
                ["dailyHintsUsed"] = economy.DailyHintsUsed,
                ["updatedAt"] = FieldValue.ServerTimestamp
            };

            UserReference(uid).SetAsync(data)
                .ContinueWithOnMainThread(task =>
                {
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        Debug.LogWarning(
                            "[Economy] Push deferred: " +
                            task.Exception?.GetBaseException().Message);
                        _pushQueued = true;
                    }
                });
        }

        public void DeleteOwn(string uid, Action<bool, string> callback)
        {
            if (_db == null || string.IsNullOrEmpty(uid))
            {
                callback?.Invoke(false, "Cloud economy is unavailable.");
                return;
            }

            UserReference(uid).DeleteAsync()
                .ContinueWithOnMainThread(task =>
                {
                    if (task.IsCanceled || task.IsFaulted)
                    {
                        callback?.Invoke(
                            false,
                            "Could not remove cloud economy. Check your connection and try again.");
                        return;
                    }

                    callback?.Invoke(true, null);
                });
        }

        private void HandleUserChanged(FirebaseUser user)
        {
            if (IsSuspended || _loadsDeferred) return;
            if (user == null)
            {
                _pendingUserId = null;
                _pushQueued = false;
                _generation++;
                return;
            }

            LoadAndMerge(user.UserId);
        }

        private void BindFirestore()
        {
            if (_firebase?.Firestore == null) return;
            _db = _firebase.Firestore;
            if (_loadsDeferred) return;
            if (!string.IsNullOrEmpty(_pendingUserId))
            {
                string uid = _pendingUserId;
                bool push = _pushQueued;
                _pushQueued = false;
                LoadAndMerge(uid);
                if (push) PushLocal(uid);
            }
            else if (_auth?.CurrentUser != null)
            {
                LoadAndMerge(_auth.CurrentUser.UserId);
            }
        }

        private DocumentReference UserReference(string uid) =>
            _db.Collection(CollectionName).Document(uid);

        private static EconomySnapshot FromDocument(DocumentSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Exists)
                return null;

            int coins = 0;
            if (snapshot.TryGetValue("coins", out long coinsLong))
                coins = (int)Mathf.Clamp(coinsLong, 0, SaveService.MaxCoins);
            else if (snapshot.TryGetValue("coins", out int coinsInt))
                coins = Mathf.Clamp(coinsInt, 0, SaveService.MaxCoins);

            var owned = new List<string> { SaveService.DefaultSkinId };
            if (snapshot.TryGetValue("ownedSkinIds", out object rawOwned) &&
                rawOwned is IEnumerable rawList)
            {
                foreach (object item in rawList)
                {
                    string skinId = item as string;
                    if (!string.IsNullOrWhiteSpace(skinId))
                        owned.Add(skinId.Trim());
                }
            }

            string equipped = SaveService.DefaultSkinId;
            if (snapshot.TryGetValue("equippedSkinId", out string equippedRaw) &&
                !string.IsNullOrWhiteSpace(equippedRaw))
                equipped = equippedRaw.Trim();

            string hintDayId = string.Empty;
            if (snapshot.TryGetValue("dailyHintDayId", out string hintDayRaw))
                hintDayId = SaveService.NormalizeDayId(hintDayRaw);

            int hintsUsed = 0;
            if (snapshot.TryGetValue("dailyHintsUsed", out long hintsUsedLong))
                hintsUsed = (int)Mathf.Clamp(hintsUsedLong, 0, 3);
            else if (snapshot.TryGetValue("dailyHintsUsed", out int hintsUsedInt))
                hintsUsed = Mathf.Clamp(hintsUsedInt, 0, 3);

            return new EconomySnapshot
            {
                Coins = coins,
                OwnedSkinIds = owned,
                EquippedSkinId = equipped,
                DailyHintDayId = hintDayId,
                DailyHintsUsed = hintsUsed
            };
        }

        private void OnDestroy()
        {
            if (_firebase != null) _firebase.Ready -= BindFirestore;
            if (_auth != null) _auth.UserChanged -= HandleUserChanged;
        }
    }
}
