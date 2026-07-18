using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Auth;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Home-screen account modal for public display name, guest upgrades,
    /// password recovery, and sign-out.
    /// </summary>
    public sealed class AccountController : MonoBehaviour
    {
        public event Action Closed;
        public event Action SignedOut;
        public event Action SignInRequested;

        private readonly List<Button> _actions = new();

        private AuthService _auth;
        private GoogleCredentialBridge _google;
        private ProgressSyncService _progressSync;
        private AccountDeletionService _deletion;
        private CanvasGroup _canvasGroup;
        private RectTransform _card;
        private InputField _displayName;
        private InputField _email;
        private InputField _password;
        private Text _identity;
        private Text _status;
        private Text _avatarText;
        private GameObject _guestActions;
        private GameObject _registeredActions;
        private Text _guestHeading;
        private Text _googleActionLabel;
        private Text _emailActionLabel;
        private Text _guestModeLabel;
        private GameObject _deleteConfirmRoot;
        private Text _deleteBody;
        private Text _deleteConfirmLabel;
        private InputField _deletePassword;
        private PrivacyPolicyPanel _privacy;
        private Coroutine _transition;
        private bool _googleBusy;
        private bool _returningMode;
        private bool _googleForDeletion;
        private int _deleteConfirmationStep;

        public void Build(RectTransform root, AuthService auth, GoogleCredentialBridge google,
            ProgressSyncService progressSync, AccountDeletionService deletion)
        {
            _auth = auth;
            _google = google;
            _progressSync = progressSync;
            _deletion = deletion;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            var background = UiKit.Image(root, "AccountBackground", Theme.Background);
            UiKit.Stretch(background.rectTransform);
            RectTransform safe = UiKit.SafeArea(root);
            _card = safe;
            var back = UiKit.FullScreenHeader(safe, "ACCOUNT", Hide, out _);
            AddPressFx((RectTransform)back.transform);
            _actions.Add(back);

            var profile = UiKit.Image(safe, "ProfileCard",
                Color.Lerp(Theme.CardBg, Theme.TrackBg, 0.18f),
                SpriteFactory.RoundedRect(84, 26));
            UiKit.Anchor(profile.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(920f, 300f), new Vector2(0f, -180f));

            var avatar = UiKit.Image(profile.rectTransform, "Avatar",
                Theme.Accent(Difficulty.Medium), SpriteFactory.Circle());
            UiKit.Anchor(avatar.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(126f, 126f), new Vector2(98f, 18f));
            _avatarText = UiKit.Text(avatar.rectTransform, "Initial", "P", 54, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_avatarText.rectTransform);

            _identity = UiKit.Text(profile.rectTransform, "Identity", "PLAYER", 29, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(_identity.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(610f, 52f), new Vector2(185f, 37f));

            _status = UiKit.Text(profile.rectTransform, "Status", string.Empty, 23, Theme.InkSoft,
                TextAnchor.MiddleLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_status.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(650f, 82f), new Vector2(185f, -42f));

            var nameLabel = UiKit.Text(safe, "NameLabel", "DISPLAY NAME", 24, Theme.InkSoft,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(nameLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 40f), new Vector2(0f, -525f));

            _displayName = UiKit.Input(safe, "DisplayName", "3–16 characters",
                InputField.ContentType.Standard, out var nameImage);
            UiKit.Anchor(nameImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 106f), new Vector2(0f, -595f));
            _displayName.characterLimit = 16;

            var saveName = BuildButton(safe, "SaveName", Theme.Accent(Difficulty.Easy),
                "SAVE DISPLAY NAME", Color.white, 31, -720f);
            saveName.onClick.AddListener(() => _auth.UpdateDisplayName(_displayName.text));

            _guestActions = BuildGuestActions(safe);
            _registeredActions = BuildRegisteredActions(safe);

            var privacy = BuildButton(safe, "Privacy", Theme.CardBg,
                "PRIVACY POLICY & DATA", Theme.Ink, 29, -1040f);
            privacy.onClick.AddListener(() => _privacy.Show());

            var signOut = BuildButton(safe, "SignOut", Theme.CardBg,
                "SIGN OUT", Theme.Ink, 29, -1170f);
            signOut.onClick.AddListener(() =>
            {
                _auth.SignOut();
                SignedOut?.Invoke();
            });

            var dangerLabel = UiKit.Text(safe, "DangerLabel", "DANGER ZONE", 23,
                Theme.Hex("#E66A5E"), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(dangerLabel.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(920f, 42f), new Vector2(0f, -1290f));

            var delete = BuildButton(safe, "DeleteAccount",
                Color.Lerp(Theme.Hex("#E66A5E"), Theme.CardBg, 0.72f),
                "DELETE ACCOUNT", Theme.Hex("#E66A5E"), 29, -1370f);
            delete.onClick.AddListener(ShowDeleteConfirmation);

            BuildDeleteConfirmation(root);
            var privacyRoot = new GameObject("AccountPrivacyPanel", typeof(RectTransform));
            privacyRoot.transform.SetParent(root, false);
            var privacyRt = (RectTransform)privacyRoot.transform;
            UiKit.Stretch(privacyRt);
            _privacy = privacyRoot.AddComponent<PrivacyPolicyPanel>();
            _privacy.Build(privacyRt);

            _auth.UserChanged += HandleUserChanged;
            _auth.BusyChanged += HandleBusyChanged;
            _auth.Failed += HandleFailure;
            _auth.Succeeded += HandleSuccess;
            _google.TokenReceived += HandleGoogleToken;
            _google.Failed += HandleGoogleFailure;
            _progressSync.StatusChanged += HandleSyncStatus;

            gameObject.SetActive(false);
        }

        public void Show()
        {
            Refresh();
            gameObject.SetActive(true);
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(true));
        }

        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Animate(false));
        }

        private GameObject BuildGuestActions(RectTransform parent)
        {
            var root = new GameObject("GuestUpgrade", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            UiKit.Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 200f), new Vector2(0f, -825f));

            _guestHeading = UiKit.Text(rt, "Heading",
                "Connect an account to protect and restore this progress.", 24, Theme.InkSoft,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(_guestHeading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(900f, 48f), new Vector2(0f, -8f));

            var signIn = BuildButton(rt, "OpenSignIn", Theme.Accent(Difficulty.ExtraHard),
                "SIGN IN OR CREATE ACCOUNT", Color.white, 30, -82f);
            signIn.onClick.AddListener(() => SignInRequested?.Invoke());
            return root;
        }

        private GameObject BuildRegisteredActions(RectTransform parent)
        {
            var root = new GameObject("RegisteredActions", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = (RectTransform)root.transform;
            UiKit.Anchor(rt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 190f), new Vector2(0f, -825f));

            var secured = UiKit.Text(rt, "Secured", "ACCOUNT SECURED", 28,
                Theme.Accent(Difficulty.Easy), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(secured.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 54f), new Vector2(0f, -10f));

            var reset = BuildTextButton(rt, "ResetPassword", "SEND PASSWORD RESET", -86f,
                Theme.Gold);
            reset.onClick.AddListener(() =>
            {
                string email = _auth.CurrentUser?.Email;
                if (string.IsNullOrEmpty(email))
                    HandleFailure("Password reset is only available for email accounts.");
                else
                    _auth.SendPasswordReset(email);
            });
            return root;
        }

        private void BuildDeleteConfirmation(RectTransform root)
        {
            _deleteConfirmRoot = new GameObject(
                "DeleteAccountConfirmation", typeof(RectTransform));
            _deleteConfirmRoot.transform.SetParent(root, false);
            UiKit.Stretch((RectTransform)_deleteConfirmRoot.transform);

            var scrim = UiKit.Image((RectTransform)_deleteConfirmRoot.transform,
                "DeleteScrim", new Color(0f, 0f, 0f, 0.76f));
            UiKit.Stretch(scrim.rectTransform);

            var card = UiKit.Image((RectTransform)_deleteConfirmRoot.transform,
                "DeleteCard", Theme.CardBg, SpriteFactory.RoundedRect(92, 28));
            UiKit.Anchor(card.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(790f, 690f), Vector2.zero);

            var title = UiKit.Text(card.rectTransform, "Title", "DELETE ACCOUNT?", 42,
                Theme.Hex("#E66A5E"), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(680f, 70f), new Vector2(0f, -50f));

            _deleteBody = UiKit.Text(card.rectTransform, "Body", string.Empty, 25,
                Theme.Ink, TextAnchor.MiddleCenter);
            _deleteBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            _deleteBody.verticalOverflow = VerticalWrapMode.Overflow;
            UiKit.Anchor(_deleteBody.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(660f, 180f), new Vector2(0f, -165f));

            _deletePassword = UiKit.Input(card.rectTransform, "DeletePassword",
                "Current password", InputField.ContentType.Password, out var passwordImage);
            UiKit.Anchor(passwordImage.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(650f, 90f), new Vector2(0f, -350f));

            var cancel = UiKit.Button(card.rectTransform, "CancelDelete", Theme.CircleBg,
                SpriteFactory.RoundedRect(72, 22), out var cancelImage);
            UiKit.Anchor(cancelImage.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(300f, 96f), new Vector2(-170f, 52f));
            var cancelLabel = UiKit.Text(cancelImage.rectTransform, "Label", "CANCEL", 28,
                Theme.OnCircle, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(cancelLabel.rectTransform);
            cancel.onClick.AddListener(HideDeleteConfirmation);
            AddPressFx(cancelImage.rectTransform);
            _actions.Add(cancel);

            var confirm = UiKit.Button(card.rectTransform, "ConfirmDelete",
                Theme.Hex("#E66A5E"), SpriteFactory.RoundedRect(72, 22),
                out var confirmImage);
            UiKit.Anchor(confirmImage.rectTransform, new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(300f, 96f), new Vector2(170f, 52f));
            _deleteConfirmLabel = UiKit.Text(confirmImage.rectTransform, "Label",
                "CONTINUE", 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_deleteConfirmLabel.rectTransform, 12f, 12f);
            confirm.onClick.AddListener(AdvanceDeleteConfirmation);
            AddPressFx(confirmImage.rectTransform);
            _actions.Add(confirm);

            _deleteConfirmRoot.SetActive(false);
        }

        private void ShowDeleteConfirmation()
        {
            _deleteConfirmationStep = 1;
            _deletePassword.text = string.Empty;
            _deletePassword.gameObject.SetActive(false);
            _deleteBody.text =
                "This permanently removes your Firebase account and all four cloud leaderboard records. This cannot be undone.";
            _deleteConfirmLabel.text = "CONTINUE";
            _deleteConfirmRoot.SetActive(true);
        }

        private void HideDeleteConfirmation()
        {
            if (_deletion.IsDeleting) return;
            _googleForDeletion = false;
            _deleteConfirmRoot.SetActive(false);
        }

        private void AdvanceDeleteConfirmation()
        {
            if (_deletion.IsDeleting) return;
            if (_deleteConfirmationStep == 1)
            {
                _deleteConfirmationStep = 2;
                ReauthenticationProvider provider =
                    _auth.CurrentReauthenticationProvider;
                _deletePassword.gameObject.SetActive(
                    provider == ReauthenticationProvider.Password);
                _deleteBody.text = provider switch
                {
                    ReauthenticationProvider.Password =>
                        "Final confirmation: enter your current password, then delete forever.",
                    ReauthenticationProvider.Google =>
                        "Final confirmation: Google will ask you to verify this account.",
                    _ =>
                        "Final confirmation: delete this guest account and all of its progress?"
                };
                _deleteConfirmLabel.text = "DELETE FOREVER";
                return;
            }

            BeginAccountDeletion();
        }

        private void BeginAccountDeletion()
        {
            if (_auth.CurrentReauthenticationProvider == ReauthenticationProvider.Google)
            {
                _googleForDeletion = true;
                _googleBusy = true;
                SetInteractable(false);
                _deleteBody.text = "Verify with Google to continue...";
                _google.SignIn();
                return;
            }

            RunAccountDeletion(_deletePassword.text, null);
        }

        private void RunAccountDeletion(string password, string googleToken)
        {
            _deleteBody.text = "Deleting cloud progress and account...";
            SetInteractable(false);
            _deletion.DeleteCurrentAccount(password, googleToken, (success, message) =>
            {
                SetInteractable(true);
                if (!success)
                {
                    _deleteBody.text = message;
                    return;
                }

                _deleteConfirmRoot.SetActive(false);
                SetStatus(message, Theme.Accent(Difficulty.Easy));
                SignedOut?.Invoke();
            });
        }

        private Button BuildButton(RectTransform parent, string name, Color background,
            string label, Color textColor, int fontSize, float top)
        {
            var button = UiKit.Button(parent, name, background,
                SpriteFactory.RoundedRect(76, 24), out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 92f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, fontSize, textColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform, 22f, 22f);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private Button BuildTextButton(RectTransform parent, string name, string label,
            float top, Color color)
        {
            var button = UiKit.Button(parent, name, new Color(0f, 0f, 0f, 0f),
                null, out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(620f, 58f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, 25, color,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private void BeginGoogle()
        {
            if (_googleBusy || _auth.IsBusy) return;
            _googleBusy = true;
            SetInteractable(false);
            SetStatus(_returningMode
                ? "OPENING GOOGLE SIGN-IN..."
                : "CONNECTING GOOGLE...", Theme.InkSoft);
            _google.SignIn();
        }

        private void HandleGoogleToken(string token)
        {
            if (!_googleForDeletion) return;
            _googleBusy = false;
            _googleForDeletion = false;
            RunAccountDeletion(null, token);
        }

        private void HandleGoogleFailure(string message)
        {
            if (!_googleForDeletion) return;
            _googleBusy = false;
            SetInteractable(!_auth.IsBusy);
            _googleForDeletion = false;
            _deleteBody.text = message;
        }

        private void HandleUserChanged(FirebaseUser user)
        {
            if (user == null) return;
            Refresh();
        }

        private void Refresh()
        {
            FirebaseUser user = _auth.CurrentUser;
            if (user == null) return;

            string name = string.IsNullOrWhiteSpace(user.DisplayName)
                ? (user.IsAnonymous ? "Guest Player" : "Player")
                : user.DisplayName.Trim();
            _displayName.text = string.IsNullOrWhiteSpace(user.DisplayName)
                ? string.Empty
                : user.DisplayName;
            _avatarText.text = name.Substring(0, 1).ToUpperInvariant();
            _identity.text = user.IsAnonymous
                ? "GUEST ACCOUNT"
                : ProviderLabel(user);
            _guestActions.SetActive(user.IsAnonymous);
            _registeredActions.SetActive(!user.IsAnonymous);
            SetStatus(user.IsAnonymous
                    ? "Guest progress is stored locally and synced to this guest account."
                    : name,
                Theme.InkSoft);
            SetInteractable(!_auth.IsBusy && !_googleBusy);
        }

        private void ToggleGuestMode()
        {
            _returningMode = !_returningMode;
            RefreshGuestMode();
            SetStatus(_returningMode
                    ? "Sign in to restore and merge your saved progress."
                    : "Connect a new account to protect this guest progress.",
                Theme.InkSoft);
        }

        private void RefreshGuestMode()
        {
            if (_guestHeading == null) return;
            _guestHeading.text = _returningMode
                ? "SIGN IN TO EXISTING ACCOUNT"
                : "SECURE YOUR GUEST ACCOUNT";
            _googleActionLabel.text = _returningMode
                ? "SIGN IN WITH GOOGLE"
                : "CONNECT GOOGLE";
            _emailActionLabel.text = _returningMode
                ? "SIGN IN WITH EMAIL"
                : "CONNECT EMAIL";
            _guestModeLabel.text = _returningMode
                ? "CREATE OR CONNECT A NEW ACCOUNT"
                : "ALREADY HAVE AN ACCOUNT? SIGN IN";
        }

        private static string ProviderLabel(FirebaseUser user)
        {
            bool google = false;
            bool email = false;
            foreach (IUserInfo provider in user.ProviderData)
            {
                if (provider.ProviderId == GoogleAuthProvider.ProviderId) google = true;
                if (provider.ProviderId == EmailAuthProvider.ProviderId) email = true;
            }

            if (google && email) return "GOOGLE + EMAIL";
            if (google) return "GOOGLE ACCOUNT";
            if (email) return "EMAIL ACCOUNT";
            return "REGISTERED ACCOUNT";
        }

        private void HandleBusyChanged(bool busy)
        {
            SetInteractable(!busy && !_googleBusy);
            if (busy) SetStatus("PLEASE WAIT...", Theme.InkSoft);
        }

        private void HandleFailure(string message)
        {
            SetStatus(message, Theme.Hex("#E66A5E"));
        }

        private void HandleSuccess(string message)
        {
            SetStatus(message, Theme.Accent(Difficulty.Easy));
        }

        private void HandleSyncStatus(string message)
        {
            SetStatus(message, Theme.InkSoft);
        }

        private void SetStatus(string message, Color color)
        {
            _status.text = message;
            _status.color = color;
        }

        private void SetInteractable(bool enabled)
        {
            foreach (Button action in _actions) action.interactable = enabled;
            if (_displayName != null) _displayName.interactable = enabled;
            if (_email != null) _email.interactable = enabled;
            if (_password != null) _password.interactable = enabled;
        }

        private IEnumerator Animate(bool opening)
        {
            float duration = opening ? 0.2f : 0.14f;
            float fromAlpha = opening ? 0f : _canvasGroup.alpha;
            float toAlpha = opening ? 1f : 0f;
            Vector2 fromPosition = opening
                ? new Vector2(42f, 0f)
                : _card.anchoredPosition;
            Vector2 toPosition = opening ? Vector2.zero : new Vector2(24f, 0f);
            float elapsed = 0f;

            if (opening)
            {
                _canvasGroup.alpha = 0f;
                _card.anchoredPosition = fromPosition;
            }

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - k) * (1f - k);
                _canvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, eased);
                _card.anchoredPosition =
                    Vector2.LerpUnclamped(fromPosition, toPosition, eased);
                yield return null;
            }

            _canvasGroup.alpha = toAlpha;
            _card.anchoredPosition = toPosition;
            _transition = null;
            if (!opening)
            {
                gameObject.SetActive(false);
                Closed?.Invoke();
            }
        }

        private static void AddPressFx(RectTransform target)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = 0.96f;
        }

        private void OnDestroy()
        {
            if (_auth != null)
            {
                _auth.UserChanged -= HandleUserChanged;
                _auth.BusyChanged -= HandleBusyChanged;
                _auth.Failed -= HandleFailure;
                _auth.Succeeded -= HandleSuccess;
            }

            if (_google != null)
            {
                _google.TokenReceived -= HandleGoogleToken;
                _google.Failed -= HandleGoogleFailure;
            }
            if (_progressSync != null)
                _progressSync.StatusChanged -= HandleSyncStatus;
        }
    }
}
