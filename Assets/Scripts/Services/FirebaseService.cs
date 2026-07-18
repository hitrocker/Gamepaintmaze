using System;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using Firebase.Functions;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Initializes Firebase once and exposes the product clients used by
    /// authentication and leaderboard services.
    /// </summary>
    public sealed class FirebaseService : MonoBehaviour
    {
        public static FirebaseService Instance { get; private set; }

        public bool IsReady { get; private set; }
        public FirebaseAuth Auth { get; private set; }
        public FirebaseFirestore Firestore { get; private set; }
        public FirebaseFunctions Functions { get; private set; }

        public event Action Ready;
        public event Action<string> InitializationFailed;

        private bool _initializing;

        public void Init()
        {
            if (_initializing || IsReady) return;
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Firebase] Duplicate service ignored.");
                Destroy(this);
                return;
            }

            Instance = this;
            _initializing = true;
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                _initializing = false;
                if (task.IsCanceled)
                {
                    Fail("Dependency check was canceled.");
                    return;
                }

                if (task.IsFaulted)
                {
                    Fail(task.Exception?.GetBaseException().Message ?? "Dependency check failed.");
                    return;
                }

                if (task.Result != DependencyStatus.Available)
                {
                    Fail("Dependencies are unavailable: " + task.Result);
                    return;
                }

                try
                {
                    _ = FirebaseApp.DefaultInstance;
                    Auth = FirebaseAuth.DefaultInstance;
                    Firestore = FirebaseFirestore.DefaultInstance;
                    Functions = FirebaseFunctions.DefaultInstance;
                    IsReady = true;
                    Debug.Log("[Firebase] Initialized successfully.");
                    Ready?.Invoke();
                }
                catch (Exception ex)
                {
                    Fail(ex.Message);
                }
            });
        }

        private void Fail(string message)
        {
            Debug.LogError("[Firebase] Initialization failed: " + message);
            InitializationFailed?.Invoke(message);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
