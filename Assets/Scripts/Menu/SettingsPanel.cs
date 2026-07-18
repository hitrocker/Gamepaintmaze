using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Firebase.Auth;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Modal home-screen settings card backed directly by SaveService.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public event Action SignInRequested;
        public event Action AccountRequested;

        private static readonly Color SwitchOn = new(0.216f, 0.722f, 0.431f);

        private CanvasGroup _canvasGroup;
        private RectTransform _card;
        private Action _closed;
        private Coroutine _transition;
        private PrivacyPolicyPanel _privacy;
        private AuthService _auth;
        private Text _accountName;
        private Text _accountDetail;
        private Text _accountAction;

        public void Build(RectTransform root, AuthService auth, Action closed)
        {
            _auth = auth;
            _closed = closed;
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            var scrim = UiKit.Button(root, "SettingsScrim", new Color(0f, 0f, 0f, 0.58f),
                null, out var scrimImg);
            UiKit.Stretch(scrimImg.rectTransform);
            scrim.onClick.AddListener(Hide);

            var card = UiKit.Image(root, "SettingsCard", Theme.CardBg, SpriteFactory.RoundedRect(96, 28));
            _card = card.rectTransform;
            UiKit.Anchor(_card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(1040f, 1500f), Vector2.zero);

            var handle = UiKit.Image(_card, "DragHandle", Theme.InkSoft,
                SpriteFactory.RoundedRect(40, 12));
            handle.raycastTarget = false;
            UiKit.Anchor(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(92f, 9f), new Vector2(0f, -34f));

            var title = UiKit.Text(_card, "Title", "SETTINGS", 46, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(680f, 74f), new Vector2(60f, -92f));

            var close = UiKit.Button(_card, "Close", Theme.CircleBg, SpriteFactory.Circle(), out var closeImg);
            UiKit.Anchor(closeImg.rectTransform, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(76f, 76f), new Vector2(-58f, -94f));
            var x = UiKit.Text(closeImg.rectTransform, "X", "\u00D7", 44, Theme.OnCircle,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(x.rectTransform);
            close.onClick.AddListener(Hide);
            AddPressFx(closeImg.rectTransform, 0.9f);

            BuildAccountStrip(_card);

            BuildSwitch(_card, "SOUND", "Music and game sounds", 470f,
                () => SaveService.SoundEnabled, value => SaveService.SoundEnabled = value);
            BuildSwitch(_card, "HAPTICS", "Vibration and impact feedback", 670f,
                () => SaveService.HapticsEnabled, value => SaveService.HapticsEnabled = value);
            BuildSwitch(_card, "CAMERA SHAKE", "Screen movement on wall impacts", 870f,
                () => SaveService.CameraShakeEnabled, value => SaveService.CameraShakeEnabled = value);
            BuildSwitch(_card, "PAINT SPLASHES", "Paint droplets while rolling", 1070f,
                () => SaveService.PaintSplashesEnabled,
                value => SaveService.PaintSplashesEnabled = value);

            var privacy = UiKit.Button(_card, "PrivacyPolicy", new Color(0f, 0f, 0f, 0f),
                null, out var privacyImage);
            UiKit.Anchor(privacyImage.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(760f, 64f), new Vector2(0f, -1265f));
            var privacyLabel = UiKit.Text(privacyImage.rectTransform, "Label",
                "PRIVACY POLICY & DATA", 25, Theme.Gold,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(privacyLabel.rectTransform);
            privacy.onClick.AddListener(() => _privacy.Show());
            AddPressFx(privacyImage.rectTransform, 0.96f);

            var accountData = UiKit.Button(_card, "AccountAndData",
                new Color(0f, 0f, 0f, 0f), null, out var accountDataImage);
            UiKit.Anchor(accountDataImage.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(760f, 64f), new Vector2(0f, -1360f));
            var accountDataLabel = UiKit.Text(accountDataImage.rectTransform, "Label",
                "ACCOUNT & DATA", 25, Theme.InkSoft,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(accountDataLabel.rectTransform);
            accountData.onClick.AddListener(() =>
            {
                DismissForNavigation();
                AccountRequested?.Invoke();
            });
            AddPressFx(accountDataImage.rectTransform, 0.96f);

            var privacyRoot = new GameObject("SettingsPrivacyPanel", typeof(RectTransform));
            privacyRoot.transform.SetParent(root, false);
            var privacyRt = (RectTransform)privacyRoot.transform;
            UiKit.Stretch(privacyRt);
            _privacy = privacyRoot.AddComponent<PrivacyPolicyPanel>();
            _privacy.Build(privacyRt);

            _auth.UserChanged += HandleUserChanged;

            gameObject.SetActive(false);
        }

        public void Show()
        {
            RefreshAccount();
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

        private void DismissForNavigation()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        private void BuildAccountStrip(RectTransform parent)
        {
            var button = UiKit.Button(parent, "AccountStrip",
                Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.28f),
                SpriteFactory.RoundedRect(72, 24), out var card);
            UiKit.Anchor(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 190f), new Vector2(0f, -235f));

            _accountDetail = UiKit.Text(card.rectTransform, "Detail", "PLAYING AS GUEST",
                23, Theme.InkSoft, TextAnchor.MiddleLeft);
            UiKit.Anchor(_accountDetail.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(520f, 42f), new Vector2(36f, 31f));

            _accountName = UiKit.Text(card.rectTransform, "Name", "Guest Player",
                34, Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(_accountName.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(560f, 58f), new Vector2(36f, -30f));

            _accountAction = UiKit.Text(card.rectTransform, "Action", "SIGN IN",
                27, Theme.Accent(PaintMaze.Domain.Difficulty.ExtraHard),
                TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Anchor(_accountAction.rectTransform, new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(230f, 58f), new Vector2(-34f, 0f));

            button.onClick.AddListener(() =>
            {
                DismissForNavigation();
                FirebaseUser user = _auth.CurrentUser;
                if (UiFlowPolicy.AccountDestination(user != null, user?.IsAnonymous ?? true) ==
                    AccountEntryDestination.SignIn)
                    SignInRequested?.Invoke();
                else
                    AccountRequested?.Invoke();
            });
            AddPressFx(card.rectTransform, 0.98f);
        }

        private void HandleUserChanged(FirebaseUser _)
        {
            RefreshAccount();
        }

        private void RefreshAccount()
        {
            if (_accountName == null) return;
            FirebaseUser user = _auth.CurrentUser;
            bool guest = user == null || user.IsAnonymous;
            string name = guest || string.IsNullOrWhiteSpace(user.DisplayName)
                ? "Guest Player"
                : user.DisplayName.Trim();
            _accountName.text = name;
            _accountDetail.text = guest ? "PLAYING AS GUEST" : "ACCOUNT CONNECTED";
            _accountAction.text = guest ? "SIGN IN" : "MANAGE";
        }

        private void BuildSwitch(RectTransform parent, string label, string detail, float top,
            Func<bool> get, Action<bool> set)
        {
            var row = UiKit.Image(parent, "Setting_" + label,
                Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.28f),
                SpriteFactory.RoundedRect(72, 24));
            var rowRt = row.rectTransform;
            UiKit.Anchor(rowRt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(920f, 170f), new Vector2(0f, -top));

            var heading = UiKit.Text(rowRt, "Label", label, 31, Theme.Ink,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(heading.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(540f, 48f), new Vector2(36f, 26f));

            var sub = UiKit.Text(rowRt, "Detail", detail, 24, Theme.InkSoft, TextAnchor.MiddleLeft);
            UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(600f, 40f), new Vector2(36f, -27f));

            var button = UiKit.Button(rowRt, "Switch", Theme.TrackBg,
                SpriteFactory.RoundedRect(56, 28), out var track);
            UiKit.Anchor(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(132f, 64f), new Vector2(-34f, 0f));

            var knob = UiKit.Image(track.rectTransform, "Knob", Color.white, SpriteFactory.Circle());
            knob.raycastTarget = false;
            var knobRt = knob.rectTransform;
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.sizeDelta = Vector2.one * 50f;

            void Refresh(bool on)
            {
                track.color = on ? SwitchOn : Theme.TrackBg;
                knobRt.anchoredPosition = new Vector2(on ? 31f : -31f, 0f);
            }

            Refresh(get());
            button.onClick.AddListener(() =>
            {
                bool value = !get();
                set(value);
                Refresh(value);
                AudioService.Instance?.HapticMedium();
            });
            AddPressFx(track.rectTransform, 0.96f);
        }

        private IEnumerator Animate(bool opening)
        {
            float duration = opening ? 0.26f : 0.18f;
            float fromAlpha = opening ? 0f : _canvasGroup.alpha;
            float toAlpha = opening ? 1f : 0f;
            Vector2 shown = Vector2.zero;
            Vector2 hidden = new Vector2(0f, -_card.rect.height - 40f);
            Vector2 fromPosition = opening ? hidden : _card.anchoredPosition;
            Vector2 toPosition = opening ? shown : hidden;
            float elapsed = 0f;

            if (opening)
            {
                _canvasGroup.alpha = 0f;
                _card.anchoredPosition = hidden;
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
                _closed?.Invoke();
            }
        }

        private static void AddPressFx(RectTransform target, float scale)
        {
            var fx = target.gameObject.AddComponent<UiPressFx>();
            fx.target = target;
            fx.pressedScale = scale;
        }

        private void OnDestroy()
        {
            if (_auth != null) _auth.UserChanged -= HandleUserChanged;
        }
    }
}
