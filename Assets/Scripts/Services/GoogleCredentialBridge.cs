using System;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Requests a Google ID token from Android Credential Manager. Firebase Auth
    /// exchanges the returned token for the game's Firebase session.
    /// </summary>
    public sealed class GoogleCredentialBridge : MonoBehaviour
    {
        // OAuth client_type 3 from google-services.json. Google ID tokens must
        // target the web client ID, not the Android client ID.
        private const string WebClientId =
            "560778352521-5eujrgf66non6jbmr82v0p7aqqnhboc6.apps.googleusercontent.com";

        public event Action<string> TokenReceived;
        public event Action<string> Failed;

        private bool _requesting;

        public void SignIn()
        {
            if (_requesting) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            _requesting = true;
            try
            {
                using var bridge = new AndroidJavaClass(
                    "com.hitrocker.paintmaze.auth.GoogleCredentialBridge");
                bridge.CallStatic(
                    "signIn", gameObject.name, nameof(OnGoogleToken),
                    nameof(OnGoogleError), WebClientId);
            }
            catch (Exception ex)
            {
                _requesting = false;
                Failed?.Invoke("Could not open Google Sign-In: " + ex.Message);
            }
#else
            Failed?.Invoke("Google Sign-In is available in the Android build.");
#endif
        }

        public void OnGoogleToken(string token)
        {
            _requesting = false;
            TokenReceived?.Invoke(token);
        }

        public void OnGoogleError(string message)
        {
            _requesting = false;
            Failed?.Invoke(string.IsNullOrEmpty(message)
                ? "Google Sign-In was canceled."
                : message);
        }
    }
}
