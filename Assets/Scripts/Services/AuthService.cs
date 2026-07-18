using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
using UnityEngine;

namespace PaintMaze.Services
{
    public enum AuthTransitionKind
    {
        None,
        GuestLinked,
        ExistingAccountSignIn
    }

    public enum ReauthenticationProvider
    {
        None,
        Password,
        Google
    }

    /// <summary>
    /// Owns the Firebase Auth session and keeps provider-specific operations out
    /// of the UI layer.
    /// </summary>
    public sealed class AuthService : MonoBehaviour
    {
        public FirebaseUser CurrentUser => _auth?.CurrentUser;
        public bool IsReady { get; private set; }
        public bool IsBusy { get; private set; }
        public bool IsSilentSessionPending => _ensureSessionWhenReady;
        public ReauthenticationProvider CurrentReauthenticationProvider
        {
            get
            {
                FirebaseUser user = CurrentUser;
                if (user == null || user.IsAnonymous) return ReauthenticationProvider.None;
                bool hasPassword = false;
                foreach (IUserInfo provider in user.ProviderData)
                {
                    if (provider.ProviderId == GoogleAuthProvider.ProviderId)
                        return ReauthenticationProvider.Google;
                    if (provider.ProviderId == EmailAuthProvider.ProviderId)
                        hasPassword = true;
                }
                return hasPassword
                    ? ReauthenticationProvider.Password
                    : ReauthenticationProvider.None;
            }
        }

        public event Action Ready;
        public event Action<FirebaseUser> UserChanged;
        public event Action<bool> BusyChanged;
        public event Action<string> Failed;
        public event Action<string> Succeeded;
        public event Action<string> DisplayNameChanged;
        public event Action<AuthTransitionKind, string> TransitionStarted;
        public event Action<AuthTransitionKind, FirebaseUser> TransitionCompleted;

        private FirebaseService _firebase;
        private FirebaseAuth _auth;
        private bool _ensureSessionWhenReady;
        private bool _silentSignInRunning;

        public void Init(FirebaseService firebase)
        {
            _firebase = firebase;
            _firebase.Ready += Bind;
            _firebase.InitializationFailed += HandleInitializationFailed;
            if (_firebase.IsReady) Bind();
        }

        public void SignInAnonymously()
        {
            if (!CanStart()) return;
            RunAuth(_auth.SignInAnonymouslyAsync());
        }

        public void EnsureSessionSilently()
        {
            if (!IsReady)
            {
                _ensureSessionWhenReady = true;
                return;
            }

            _ensureSessionWhenReady = false;
            if (CurrentUser != null)
            {
                UserChanged?.Invoke(CurrentUser);
                return;
            }

            if (_silentSignInRunning) return;
            _silentSignInRunning = true;
            _auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(task =>
            {
                _silentSignInRunning = false;
                if (task.IsCanceled || task.IsFaulted)
                {
                    Debug.LogWarning("[Auth] Silent guest session failed: " +
                                     MessageFor(task.Exception, "Authentication unavailable."));
                    return;
                }

                UserChanged?.Invoke(task.Result.User);
            });
        }

        public void SignInWithEmail(string email, string password)
        {
            if (!CanStart()) return;
            email = NormalizeEmail(email);
            if (!ValidateEmailPassword(email, password)) return;
            BeginTransition(AuthTransitionKind.ExistingAccountSignIn);
            RunAuth(_auth.SignInWithEmailAndPasswordAsync(email, password),
                AuthTransitionKind.ExistingAccountSignIn);
        }

        public void CreateEmailAccount(string email, string password)
        {
            if (!CanStart()) return;
            email = NormalizeEmail(email);
            if (!ValidateEmailPassword(email, password)) return;

            if (CurrentUser != null && CurrentUser.IsAnonymous)
            {
                Credential credential = EmailAuthProvider.GetCredential(email, password);
                BeginTransition(AuthTransitionKind.GuestLinked);
                RunAuth(CurrentUser.LinkWithCredentialAsync(credential),
                    AuthTransitionKind.GuestLinked);
                return;
            }

            BeginTransition(AuthTransitionKind.GuestLinked);
            RunAuth(_auth.CreateUserWithEmailAndPasswordAsync(email, password),
                AuthTransitionKind.GuestLinked);
        }

        public void SignInWithGoogleToken(string idToken)
        {
            if (!CanStart()) return;
            if (string.IsNullOrWhiteSpace(idToken))
            {
                ReportFailure("Google did not return an identity token.");
                return;
            }

            Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
            if (CurrentUser != null && CurrentUser.IsAnonymous)
            {
                LinkGoogleWithReturningFallback(credential);
                return;
            }

            BeginTransition(AuthTransitionKind.ExistingAccountSignIn);
            RunAuth(_auth.SignInWithCredentialAsync(credential),
                AuthTransitionKind.ExistingAccountSignIn);
        }

        public void SignInExistingWithGoogleToken(string idToken)
        {
            if (!CanStart()) return;
            if (string.IsNullOrWhiteSpace(idToken))
            {
                ReportFailure("Google did not return an identity token.");
                return;
            }

            Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
            BeginTransition(AuthTransitionKind.ExistingAccountSignIn);
            RunAuth(_auth.SignInWithCredentialAsync(credential),
                AuthTransitionKind.ExistingAccountSignIn);
        }

        public void SendPasswordReset(string email)
        {
            if (!CanStart()) return;
            email = NormalizeEmail(email);
            if (string.IsNullOrEmpty(email))
            {
                ReportFailure("Enter your email address first.");
                return;
            }

            SetBusy(true);
            _auth.SendPasswordResetEmailAsync(email).ContinueWithOnMainThread(task =>
            {
                SetBusy(false);
                if (task.IsCanceled || task.IsFaulted)
                {
                    ReportFailure(MessageFor(task.Exception, "Could not send the reset email."));
                    return;
                }

                Succeeded?.Invoke("Password reset email sent.");
            });
        }

        public void UpdateDisplayName(string displayName)
        {
            if (!CanStart()) return;
            if (CurrentUser == null)
            {
                ReportFailure("Sign in before updating your profile.");
                return;
            }

            displayName = string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : displayName.Trim();
            if (!IsValidDisplayName(displayName))
            {
                ReportFailure("Name must be 3–16 letters, numbers, spaces, _ or -.");
                return;
            }

            SetBusy(true);
            var profile = new UserProfile { DisplayName = displayName };
            CurrentUser.UpdateUserProfileAsync(profile).ContinueWithOnMainThread(task =>
            {
                SetBusy(false);
                if (task.IsCanceled || task.IsFaulted)
                {
                    ReportFailure(MessageFor(task.Exception, "Could not update your name."));
                    return;
                }

                UserChanged?.Invoke(CurrentUser);
                DisplayNameChanged?.Invoke(displayName);
                Succeeded?.Invoke("Display name updated.");
            });
        }

        public void SignOut()
        {
            if (!IsReady) return;
            _auth.SignOut();
        }

        public void ReauthenticateWithPassword(string password,
            Action<bool, string> callback)
        {
            if (!CanStartSensitive(callback)) return;
            FirebaseUser user = CurrentUser;
            if (string.IsNullOrEmpty(user.Email) || string.IsNullOrEmpty(password))
            {
                callback?.Invoke(false, "Enter your account password.");
                return;
            }

            Credential credential = EmailAuthProvider.GetCredential(user.Email, password);
            RunSensitive(user.ReauthenticateAsync(credential), callback,
                "Could not verify your password.");
        }

        public void ReauthenticateWithGoogleToken(string idToken,
            Action<bool, string> callback)
        {
            if (!CanStartSensitive(callback)) return;
            if (string.IsNullOrWhiteSpace(idToken))
            {
                callback?.Invoke(false, "Google did not return an identity token.");
                return;
            }

            Credential credential = GoogleAuthProvider.GetCredential(idToken, null);
            RunSensitive(CurrentUser.ReauthenticateAsync(credential), callback,
                "Could not verify your Google account.");
        }

        public void DeleteCurrentUser(Action<bool, string> callback)
        {
            if (!CanStartSensitive(callback)) return;
            RunSensitive(CurrentUser.DeleteAsync(), callback,
                "Could not delete the account.");
        }

        private void Bind()
        {
            if (IsReady) return;
            _auth = _firebase.Auth;
            _auth.StateChanged += HandleAuthStateChanged;
            IsReady = true;
            Ready?.Invoke();
            UserChanged?.Invoke(CurrentUser);
            if (_ensureSessionWhenReady) EnsureSessionSilently();
        }

        private bool CanStart()
        {
            if (!IsReady)
            {
                ReportFailure("Firebase is still connecting.");
                return false;
            }

            if (IsBusy) return false;
            return true;
        }

        private bool CanStartSensitive(Action<bool, string> callback)
        {
            if (!IsReady || CurrentUser == null)
            {
                callback?.Invoke(false, "No account is signed in.");
                return false;
            }
            if (IsBusy)
            {
                callback?.Invoke(false, "Please wait for the current account action.");
                return false;
            }
            return true;
        }

        private void RunSensitive(Task operation, Action<bool, string> callback,
            string fallback)
        {
            SetBusy(true);
            operation.ContinueWithOnMainThread(task =>
            {
                SetBusy(false);
                if (task.IsCanceled || task.IsFaulted)
                {
                    string message = MessageFor(task.Exception, fallback);
                    ReportFailure(message);
                    callback?.Invoke(false, message);
                    return;
                }
                callback?.Invoke(true, null);
            });
        }

        private bool ValidateEmailPassword(string email, string password)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            {
                ReportFailure("Enter a valid email address.");
                return false;
            }

            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                ReportFailure("Password must contain at least 6 characters.");
                return false;
            }

            return true;
        }

        private void RunAuth(Task<AuthResult> operation,
            AuthTransitionKind transition = AuthTransitionKind.None)
        {
            SetBusy(true);
            operation.ContinueWithOnMainThread(task =>
            {
                SetBusy(false);
                if (task.IsCanceled || task.IsFaulted)
                {
                    ReportFailure(MessageFor(task.Exception, "Authentication failed."));
                    return;
                }

                FirebaseUser user = task.Result.User;
                UserChanged?.Invoke(user);
                if (transition != AuthTransitionKind.None)
                {
                    TransitionCompleted?.Invoke(transition, user);
                    Succeeded?.Invoke(transition == AuthTransitionKind.GuestLinked
                        ? "Account connected."
                        : "Signed in. Restoring progress...");
                }
            });
        }

        private void LinkGoogleWithReturningFallback(Credential credential)
        {
            BeginTransition(AuthTransitionKind.GuestLinked);
            SetBusy(true);
            CurrentUser.LinkWithCredentialAsync(credential).ContinueWithOnMainThread(linkTask =>
            {
                if (linkTask.Status == TaskStatus.RanToCompletion)
                {
                    SetBusy(false);
                    FirebaseUser linked = linkTask.Result.User;
                    UserChanged?.Invoke(linked);
                    TransitionCompleted?.Invoke(AuthTransitionKind.GuestLinked, linked);
                    Succeeded?.Invoke("Google account connected.");
                    return;
                }

                if (!IsAuthError(linkTask.Exception, AuthError.CredentialAlreadyInUse))
                {
                    SetBusy(false);
                    ReportFailure(MessageFor(linkTask.Exception, "Could not connect Google."));
                    return;
                }

                BeginTransition(AuthTransitionKind.ExistingAccountSignIn);
                _auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(signInTask =>
                {
                    SetBusy(false);
                    if (signInTask.IsCanceled || signInTask.IsFaulted)
                    {
                        ReportFailure(MessageFor(signInTask.Exception,
                            "Could not sign in to the existing Google account."));
                        return;
                    }

                    FirebaseUser signedIn = signInTask.Result;
                    UserChanged?.Invoke(signedIn);
                    TransitionCompleted?.Invoke(
                        AuthTransitionKind.ExistingAccountSignIn, signedIn);
                    Succeeded?.Invoke("Signed in. Restoring progress...");
                });
            });
        }

        private void BeginTransition(AuthTransitionKind transition)
        {
            string previousUserId = CurrentUser?.UserId;
            TransitionStarted?.Invoke(transition, previousUserId);
        }

        private void RunAuth(Task<FirebaseUser> operation,
            AuthTransitionKind transition = AuthTransitionKind.None)
        {
            SetBusy(true);
            operation.ContinueWithOnMainThread(task =>
            {
                SetBusy(false);
                if (task.IsCanceled || task.IsFaulted)
                {
                    ReportFailure(MessageFor(task.Exception, "Authentication failed."));
                    return;
                }

                FirebaseUser user = task.Result;
                UserChanged?.Invoke(user);
                if (transition != AuthTransitionKind.None)
                {
                    TransitionCompleted?.Invoke(transition, user);
                    Succeeded?.Invoke(transition == AuthTransitionKind.GuestLinked
                        ? "Account connected."
                        : "Signed in. Restoring progress...");
                }
            });
        }

        private void HandleAuthStateChanged(object sender, EventArgs args)
        {
            UserChanged?.Invoke(CurrentUser);
        }

        private void HandleInitializationFailed(string message)
        {
            ReportFailure("Firebase could not start: " + message);
        }

        private void SetBusy(bool busy)
        {
            if (IsBusy == busy) return;
            IsBusy = busy;
            BusyChanged?.Invoke(busy);
        }

        private void ReportFailure(string message)
        {
            Debug.LogWarning("[Auth] " + message);
            Failed?.Invoke(message);
        }

        private static string NormalizeEmail(string email) =>
            string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim();

        public static bool IsValidDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            if (value.Length < 3 || value.Length > 16) return false;
            foreach (char c in value)
                if (c > 127 ||
                    (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-'))
                    return false;
            return true;
        }

        private static string MessageFor(AggregateException exception, string fallback)
        {
            Exception cause = exception?.GetBaseException();
            if (cause is FirebaseException firebase)
            {
                AuthError error = (AuthError)firebase.ErrorCode;
                return error switch
                {
                    AuthError.InvalidEmail => "Enter a valid email address.",
                    AuthError.EmailAlreadyInUse => "An account already uses this email.",
                    AuthError.WeakPassword => "Choose a stronger password.",
                    AuthError.WrongPassword => "The email or password is incorrect.",
                    AuthError.UserNotFound => "The email or password is incorrect.",
                    AuthError.CredentialAlreadyInUse => "That account is already connected to another player.",
                    AuthError.RequiresRecentLogin => "For security, sign in again before deleting your account.",
                    AuthError.NetworkRequestFailed => "Check your internet connection and try again.",
                    AuthError.TooManyRequests => "Too many attempts. Please wait and try again.",
                    _ => string.IsNullOrEmpty(cause.Message) ? fallback : cause.Message
                };
            }

            return string.IsNullOrEmpty(cause?.Message) ? fallback : cause.Message;
        }

        private static bool IsAuthError(AggregateException exception, AuthError expected)
        {
            return exception?.GetBaseException() is FirebaseException firebase &&
                   (AuthError)firebase.ErrorCode == expected;
        }

        private void OnDestroy()
        {
            if (_firebase != null)
            {
                _firebase.Ready -= Bind;
                _firebase.InitializationFailed -= HandleInitializationFailed;
            }

            if (_auth != null) _auth.StateChanged -= HandleAuthStateChanged;
        }
    }
}
