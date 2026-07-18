using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// First-run authentication gate. Existing Firebase sessions pass through
    /// automatically; new players can use Google, email, or an anonymous account.
    /// </summary>
    public sealed class AuthController : MonoBehaviour
    {
        public event Action Authenticated;

        private readonly List<Button> _actions = new();

        private AuthService _auth;
        private GoogleCredentialBridge _google;
        private InputField _email;
        private InputField _password;
        private Text _status;
        private bool _googleBusy;
        private bool _completed;

        public void Build(RectTransform parent, AuthService auth, GoogleCredentialBridge googleBridge)
        {
            _auth = auth;
            _google = googleBridge;

            var background = UiKit.Image(parent, "AuthBackground", Theme.Background);
            UiKit.Stretch(background.rectTransform);

            RectTransform safe = BuildSafeArea(parent);
            var title = UiKit.Text(safe, "GameTitle", "PAINT MAZE", 92, Theme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(900f, 130f), new Vector2(0f, -145f));

            var subtitle = UiKit.Text(safe, "Subtitle", "SIGN IN TO SAVE YOUR PROGRESS", 29,
                Theme.InkSoft, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(900f, 58f), new Vector2(0f, -265f));

            var shadow = UiKit.Image(safe, "AuthShadow", new Color(0f, 0f, 0f, 0.3f),
                SpriteFactory.RoundedRect(96, 30));
            shadow.raycastTarget = false;
            UiKit.Anchor(shadow.rectTransform, new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.5f),
                new Vector2(870f, 1120f), new Vector2(0f, -14f));

            var card = UiKit.Image(safe, "AuthCard", Theme.CardBg,
                SpriteFactory.RoundedRect(96, 30));
            RectTransform cardRt = card.rectTransform;
            UiKit.Anchor(cardRt, new Vector2(0.5f, 0.48f), new Vector2(0.5f, 0.5f),
                new Vector2(850f, 1100f), Vector2.zero);

            var welcome = UiKit.Text(cardRt, "Welcome", "WELCOME", 48, Theme.Ink,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(welcome.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(720f, 72f), new Vector2(0f, -42f));

            _status = UiKit.Text(cardRt, "Status", "CONNECTING...", 24, Theme.InkSoft,
                TextAnchor.MiddleCenter);
            UiKit.Anchor(_status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(720f, 58f), new Vector2(0f, -112f));

            var googleButton = BuildButton(cardRt, "Google", Color.white, "CONTINUE WITH GOOGLE",
                Theme.Hex("#2E333D"), 34, -205f);
            var googleMark = UiKit.Text(googleButton.transform, "GoogleMark", "G", 40,
                Theme.Hex("#4285F4"), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(googleMark.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(62f, 62f), new Vector2(62f, 0f));
            googleButton.onClick.AddListener(BeginGoogle);

            BuildDivider(cardRt, -325f);

            _email = UiKit.Input(cardRt, "Email", "Email address",
                InputField.ContentType.EmailAddress, out var emailImage);
            UiKit.Anchor(emailImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 94f), new Vector2(0f, -415f));
            _email.keyboardType = TouchScreenKeyboardType.EmailAddress;

            _password = UiKit.Input(cardRt, "Password", "Password",
                InputField.ContentType.Password, out var passwordImage);
            UiKit.Anchor(passwordImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 94f), new Vector2(0f, -530f));

            var signIn = BuildButton(cardRt, "EmailSignIn", Theme.Accent(Difficulty.Easy),
                "SIGN IN", Color.white, 35, -650f);
            signIn.onClick.AddListener(() => _auth.SignInWithEmail(_email.text, _password.text));

            var create = BuildButton(cardRt, "CreateAccount", Theme.TrackBg,
                "CREATE EMAIL ACCOUNT", Theme.Ink, 30, -765f);
            create.onClick.AddListener(() =>
                _auth.CreateEmailAccount(_email.text, _password.text));

            var reset = BuildTextButton(cardRt, "ResetPassword", "FORGOT PASSWORD?", -850f);
            reset.onClick.AddListener(() => _auth.SendPasswordReset(_email.text));

            var guest = BuildTextButton(cardRt, "Guest", "CONTINUE AS GUEST", -925f);
            guest.onClick.AddListener(_auth.SignInAnonymously);

            var privacy = UiKit.Text(cardRt, "Privacy",
                "Guest progress stays on this device until an account is linked.", 21,
                Theme.InkSoft, TextAnchor.MiddleCenter);
            UiKit.Anchor(privacy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 54f), new Vector2(0f, -1000f));

            _auth.Ready += HandleReady;
            _auth.UserChanged += HandleUserChanged;
            _auth.BusyChanged += HandleBusyChanged;
            _auth.Failed += HandleMessage;
            _auth.Succeeded += HandleSuccess;
            _google.TokenReceived += HandleGoogleToken;
            _google.Failed += HandleGoogleFailure;

            SetInteractable(false);
        }

        public void Begin()
        {
            _completed = false;
            gameObject.SetActive(true);
            if (_auth.IsReady) HandleReady();
        }

        private RectTransform BuildSafeArea(RectTransform parent)
        {
            var go = new GameObject("SafeArea", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Rect safe = Screen.safeArea;
            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            rt.anchorMin = new Vector2(safe.xMin / width, safe.yMin / height);
            rt.anchorMax = new Vector2(safe.xMax / width, safe.yMax / height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        private Button BuildButton(RectTransform parent, string name, Color background,
            string label, Color textColor, int fontSize, float top)
        {
            var button = UiKit.Button(parent, name, background,
                SpriteFactory.RoundedRect(78, 24), out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(700f, 98f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, fontSize, textColor,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform, 24f, 24f);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private Button BuildTextButton(RectTransform parent, string name, string label, float top)
        {
            var button = UiKit.Button(parent, name, new Color(0f, 0f, 0f, 0f),
                null, out var image);
            UiKit.Anchor(image.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(620f, 62f), new Vector2(0f, top));
            var text = UiKit.Text(image.rectTransform, "Label", label, 26, Theme.Gold,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(text.rectTransform);
            AddPressFx(image.rectTransform);
            _actions.Add(button);
            return button;
        }

        private static void BuildDivider(RectTransform parent, float top)
        {
            var left = UiKit.Image(parent, "DividerLeft", Theme.TrackBg);
            left.raycastTarget = false;
            UiKit.Anchor(left.rectTransform, new Vector2(0.5f, 1f), new Vector2(1f, 0.5f),
                new Vector2(280f, 2f), new Vector2(-56f, top));

            var or = UiKit.Text(parent, "Or", "OR", 23, Theme.InkSoft,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Anchor(or.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(80f, 42f), new Vector2(0f, top));

            var right = UiKit.Image(parent, "DividerRight", Theme.TrackBg);
            right.raycastTarget = false;
            UiKit.Anchor(right.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0.5f),
                new Vector2(280f, 2f), new Vector2(56f, top));
        }

        private void BeginGoogle()
        {
            if (_googleBusy || _auth.IsBusy) return;
            _googleBusy = true;
            SetInteractable(false);
            SetStatus("OPENING GOOGLE SIGN-IN...", Theme.InkSoft);
            _google.SignIn();
        }

        private void HandleGoogleToken(string token)
        {
            _googleBusy = false;
            _auth.SignInWithGoogleToken(token);
        }

        private void HandleGoogleFailure(string message)
        {
            _googleBusy = false;
            SetInteractable(_auth.IsReady && !_auth.IsBusy);
            SetStatus(message, Theme.Hex("#E66A5E"));
        }

        private void HandleReady()
        {
            if (_auth.CurrentUser != null)
            {
                HandleUserChanged(_auth.CurrentUser);
                return;
            }

            SetInteractable(true);
            SetStatus("CHOOSE HOW TO CONTINUE", Theme.InkSoft);
        }

        private void HandleUserChanged(Firebase.Auth.FirebaseUser user)
        {
            if (user == null || _completed) return;
            _completed = true;
            SetStatus("SIGNED IN", Theme.Accent(Difficulty.Easy));
            Authenticated?.Invoke();
        }

        private void HandleBusyChanged(bool busy)
        {
            SetInteractable(!busy && !_googleBusy);
            if (busy) SetStatus("PLEASE WAIT...", Theme.InkSoft);
        }

        private void HandleMessage(string message)
        {
            SetStatus(message, Theme.Hex("#E66A5E"));
        }

        private void HandleSuccess(string message)
        {
            SetStatus(message, Theme.Accent(Difficulty.Easy));
        }

        private void SetInteractable(bool enabled)
        {
            foreach (Button action in _actions) action.interactable = enabled;
            if (_email != null) _email.interactable = enabled;
            if (_password != null) _password.interactable = enabled;
        }

        private void SetStatus(string message, Color color)
        {
            _status.text = message;
            _status.color = color;
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
                _auth.Ready -= HandleReady;
                _auth.UserChanged -= HandleUserChanged;
                _auth.BusyChanged -= HandleBusyChanged;
                _auth.Failed -= HandleMessage;
                _auth.Succeeded -= HandleSuccess;
            }

            if (_google != null)
            {
                _google.TokenReceived -= HandleGoogleToken;
                _google.Failed -= HandleGoogleFailure;
            }
        }
    }
}
