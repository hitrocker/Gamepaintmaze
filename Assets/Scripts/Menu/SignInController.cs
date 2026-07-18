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
    /// Account entry page used after Home has already opened with a silent guest.
    /// Returning sign-in and guest linking deliberately remain separate operations.
    /// </summary>
    public sealed class SignInController : MonoBehaviour
    {
        public event Action BackRequested;
        public event Action Completed;

        private readonly List<Button> _actions = new();
        private AuthService _auth;
        private GoogleCredentialBridge _google;
        private ProgressSyncService _progress;
        private InputField _email;
        private InputField _password;
        private Text _status;
        private Text _heading;
        private Text _primaryLabel;
        private Text _googleLabel;
        private Text _modeLabel;
        private bool _returning = true;
        private bool _googlePending;
        private bool _completing;

        public void Build(RectTransform root, AuthService auth,
            GoogleCredentialBridge google, ProgressSyncService progress)
        {
            _auth = auth;
            _google = google;
            _progress = progress;

            var background = UiKit.Image(root, "SignInBackground", Theme.Background);
            UiKit.Stretch(background.rectTransform);
            RectTransform safe = UiKit.SafeArea(root);
            UiKit.FullScreenHeader(safe, "SIGN IN", () => BackRequested?.Invoke(), out _);

            var badge = UiKit.Image(safe, "MazeBadge", Theme.Accent(Difficulty.ExtraHard),
                SpriteFactory.RoundedRect(72, 24));
            UiKit.Anchor(badge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(150f, 150f), new Vector2(0f, -205f));
            var badgeText = UiKit.Text(badge.rectTransform, "Mark", "PM", 47, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(badgeText.rectTransform);

            _heading = UiKit.Text(safe, "Heading", "SAVE & RESTORE YOUR PROGRESS", 34,
                Theme.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(_heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(900f, 64f), new Vector2(0f, -390f));

            var detail = UiKit.Text(safe, "Detail",
                "Your guest progress will be merged with the best progress on this account.",
                25, Theme.InkSoft, TextAnchor.MiddleCenter);
            detail.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(detail.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(820f, 92f), new Vector2(0f, -455f));

            _status = UiKit.Text(safe, "Status", string.Empty, 23, Theme.InkSoft,
                TextAnchor.MiddleCenter);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Anchor(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(850f, 58f), new Vector2(0f, -545f));

            _email = UiKit.Input(safe, "Email", "Email address",
                InputField.ContentType.EmailAddress, out var emailImage);
            UiKit.Anchor(emailImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 108f), new Vector2(0f, -635f));
            _email.keyboardType = TouchScreenKeyboardType.EmailAddress;

            _password = UiKit.Input(safe, "Password", "Password",
                InputField.ContentType.Password, out var passwordImage);
            UiKit.Anchor(passwordImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 108f), new Vector2(0f, -765f));

            var primary = BuildButton(safe, "Primary", Theme.Accent(Difficulty.ExtraHard),
                "SIGN IN", Color.white, -915f);
            _primaryLabel = primary.GetComponentInChildren<Text>();
            primary.onClick.AddListener(SubmitEmail);

            var reset = BuildTextButton(safe, "Reset", "FORGOT PASSWORD?", -1020f, Theme.Gold);
            reset.onClick.AddListener(() => _auth.SendPasswordReset(_email.text));

            BuildDivider(safe, -1085f);

            var googleButton = BuildButton(safe, "Google", Theme.CardBg,
                "CONTINUE WITH GOOGLE", Theme.Ink, -1175f);
            _googleLabel = googleButton.GetComponentInChildren<Text>();
            googleButton.onClick.AddListener(BeginGoogle);

            var mode = BuildTextButton(safe, "Mode", "NEW HERE?  CREATE ACCOUNT",
                -1285f, Theme.Gold);
            _modeLabel = mode.GetComponentInChildren<Text>();
            mode.onClick.AddListener(ToggleMode);

            var guestNote = UiKit.Text(safe, "GuestNote",
                "You can return to the game without signing in.", 22,
                Theme.InkSoft, TextAnchor.MiddleCenter);
            UiKit.Anchor(guestNote.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(850f, 50f), new Vector2(0f, -1360f));

            _auth.BusyChanged += HandleBusyChanged;
            _auth.Failed += HandleFailure;
            _auth.Succeeded += HandleSuccess;
            _auth.TransitionCompleted += HandleTransitionCompleted;
            _google.TokenReceived += HandleGoogleToken;
            _google.Failed += HandleGoogleFailure;
            _progress.StatusChanged += HandleProgressStatus;
            gameObject.SetActive(false);
        }

        public void Show(bool returning = true)
        {
            _returning = returning;
            _completing = false;
            _googlePending = false;
            RefreshMode();
            SetStatus("Choose how you want to continue.", Theme.InkSoft);
            SetInteractable(!_auth.IsBusy);
            gameObject.SetActive(true);
        }

        private void SubmitEmail()
        {
            if (UiFlowPolicy.UsesExistingAccount(_returning))
                _auth.SignInWithEmail(_email.text, _password.text);
            else
                _auth.CreateEmailAccount(_email.text, _password.text);
        }

        private void ToggleMode()
        {
            _returning = !_returning;
            RefreshMode();
        }

        private void RefreshMode()
        {
            _heading.text = _returning
                ? "SAVE & RESTORE YOUR PROGRESS"
                : "CREATE YOUR PAINT MAZE ACCOUNT";
            _primaryLabel.text = _returning ? "SIGN IN" : "CREATE ACCOUNT";
            _googleLabel.text = _returning
                ? "SIGN IN WITH GOOGLE"
                : "CONNECT WITH GOOGLE";
            _modeLabel.text = _returning
                ? "NEW HERE?  CREATE ACCOUNT"
                : "ALREADY HAVE AN ACCOUNT?  SIGN IN";
        }

        private void BeginGoogle()
        {
            if (_googlePending || _auth.IsBusy) return;
            _googlePending = true;
            SetInteractable(false);
            SetStatus("Opening Google sign-in...", Theme.InkSoft);
            _google.SignIn();
        }

        private void HandleGoogleToken(string token)
        {
            if (!_googlePending || !gameObject.activeSelf) return;
            _googlePending = false;
            if (UiFlowPolicy.UsesExistingAccount(_returning))
                _auth.SignInExistingWithGoogleToken(token);
            else
                _auth.SignInWithGoogleToken(token);
        }

        private void HandleGoogleFailure(string message)
        {
            if (!_googlePending) return;
            _googlePending = false;
            SetInteractable(!_auth.IsBusy);
            HandleFailure(message);
        }

        private void HandleTransitionCompleted(AuthTransitionKind _, FirebaseUser __)
        {
            if (!gameObject.activeSelf || _completing) return;
            _completing = true;
            SetStatus("Restoring progress...", Theme.Accent(Difficulty.Easy));
            StartCoroutine(CompleteAfterDelay());
        }

        private IEnumerator CompleteAfterDelay()
        {
            float elapsed = 0f;
            while (elapsed < 0.65f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            Completed?.Invoke();
        }

        private void HandleProgressStatus(string message)
        {
            if (gameObject.activeSelf) SetStatus(message, Theme.InkSoft);
        }

        private void HandleBusyChanged(bool busy)
        {
            SetInteractable(!busy && !_googlePending);
            if (busy) SetStatus("Please wait...", Theme.InkSoft);
        }

        private void HandleFailure(string message) =>
            SetStatus(message, Theme.Hex("#E66A5E"));

        private void HandleSuccess(string message) =>
            SetStatus(message, Theme.Accent(Difficulty.Easy));

        private void SetStatus(string message, Color color)
        {
            _status.text = message;
            _status.color = color;
        }

        private void SetInteractable(bool enabled)
        {
            foreach (Button action in _actions) action.interactable = enabled;
            _email.interactable = enabled;
            _password.interactable = enabled;
        }

        private Button BuildButton(RectTransform parent, string name, Color background,
            string label, Color textColor, float top)
        {
            var button = UiKit.Button(parent, name, background,
                SpriteFactory.RoundedRect(78, 24), out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 112f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, 32, textColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform, 24f, 24f);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private Button BuildTextButton(RectTransform parent, string name, string label,
            float top, Color color)
        {
            var button = UiKit.Button(parent, name, Color.clear, null, out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(820f, 64f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, 25, color,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private static void BuildDivider(RectTransform parent, float top)
        {
            var line = UiKit.Image(parent, "Divider", Theme.TrackBg);
            line.raycastTarget = false;
            UiKit.Anchor(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(920f, 2f), new Vector2(0f, top));
            var or = UiKit.Text(parent, "Or", "OR", 22, Theme.InkSoft,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(or.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(90f, 42f), new Vector2(0f, top));
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
                _auth.BusyChanged -= HandleBusyChanged;
                _auth.Failed -= HandleFailure;
                _auth.Succeeded -= HandleSuccess;
                _auth.TransitionCompleted -= HandleTransitionCompleted;
            }
            if (_google != null)
            {
                _google.TokenReceived -= HandleGoogleToken;
                _google.Failed -= HandleGoogleFailure;
            }
            if (_progress != null) _progress.StatusChanged -= HandleProgressStatus;
        }
    }
}
