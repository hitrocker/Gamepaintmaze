using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    public static class LegalConfig
    {
        public const string OnlinePrivacyPolicyUrl =
            "https://hitrocker.github.io/paintmaze/privacy.html";

        public static bool HasPublishedPolicyUrl =>
            OnlinePrivacyPolicyUrl.StartsWith("https://");
    }

    /// <summary>Local privacy summary that remains available without a network.</summary>
    public sealed class PrivacyPolicyPanel : MonoBehaviour
    {
        public void Build(RectTransform root)
        {
            var scrim = UiKit.Button(root, "PrivacyScrim",
                new Color(0f, 0f, 0f, 0.72f), null, out var scrimImage);
            UiKit.Stretch(scrimImage.rectTransform);
            scrim.onClick.AddListener(Hide);

            var card = UiKit.Image(root, "PrivacyCard", Theme.CardBg,
                SpriteFactory.RoundedRect(96, 28));
            UiKit.Anchor(card.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(870f, 1320f), Vector2.zero);

            var title = UiKit.Text(card.rectTransform, "Title", "PRIVACY & DATA", 44,
                Theme.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(600f, 76f), new Vector2(56f, -46f));

            var close = UiKit.Button(card.rectTransform, "Close", Theme.CircleBg,
                SpriteFactory.Circle(), out var closeImage);
            UiKit.Anchor(closeImage.rectTransform, new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(76f, 76f), new Vector2(-54f, -52f));
            var closeText = UiKit.Text(closeImage.rectTransform, "X", "\u00D7", 44,
                Theme.OnCircle, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(closeText.rectTransform);
            close.onClick.AddListener(Hide);

            const string summary =
                "Paint Maze uses Firebase Authentication and Cloud Firestore.\n\n" +
                "DATA WE PROCESS\n" +
                "• Firebase user ID and sign-in provider\n" +
                "• Optional email address for email accounts\n" +
                "• Public display name\n" +
                "• Highest completed level per difficulty and timestamps\n\n" +
                "ON THIS DEVICE\n" +
                "Progress and preferences such as theme, sound, haptics, camera shake, " +
                "and paint splashes are stored locally.\n\n" +
                "WHY\n" +
                "Account data restores progress, supports sign-in, and displays the " +
                "global leaderboard. We do not sell personal data and do not show email " +
                "addresses publicly.\n\n" +
                "DELETION\n" +
                "Open Account → Delete Account to permanently remove the Firebase account " +
                "and its leaderboard records. Device preferences are preserved.\n\n" +
                "The full policy, retention details, service providers, and contact " +
                "information are included with the app's legal documents.";

            var body = UiKit.Text(card.rectTransform, "Body", summary, 24, Theme.Ink,
                TextAnchor.UpperLeft);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            UiKit.Anchor(body.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(750f, 1040f), new Vector2(0f, -155f));

            if (LegalConfig.HasPublishedPolicyUrl)
            {
                var online = UiKit.Button(card.rectTransform, "OpenOnlinePolicy",
                    Theme.Gold, SpriteFactory.RoundedRect(72, 22), out var onlineImage);
                UiKit.Anchor(onlineImage.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f), new Vector2(650f, 90f), new Vector2(0f, 46f));
                var label = UiKit.Text(onlineImage.rectTransform, "Label",
                    "OPEN ONLINE POLICY", 28, Color.white,
                    TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(label.rectTransform);
                online.onClick.AddListener(() =>
                    Application.OpenURL(LegalConfig.OnlinePrivacyPolicyUrl));
            }

            gameObject.SetActive(false);
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}
