using System;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Game
{
    /// <summary>Modal gameplay confirmation for buying one immediate hint.</summary>
    public sealed class HintPurchaseDialog : MonoBehaviour
    {
        private Text _balanceText;
        private Text _confirmText;
        private Action _confirmed;

        public bool IsVisible => gameObject.activeSelf;
        public int DisplayedBalance { get; private set; }
        public int DisplayedCost { get; private set; }

        public void Build(RectTransform parent)
        {
            transform.SetParent(parent, false);
            var root = (RectTransform)transform;
            UiKit.Stretch(root);

            Image scrim = gameObject.AddComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0.58f);
            scrim.raycastTarget = true;

            Image card = UiKit.Image(
                root,
                "HintPurchaseCard",
                Theme.CardBg,
                SpriteFactory.RoundedRect(64, 28));
            UiKit.Anchor(
                card.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(840f, 520f),
                Vector2.zero);

            Text title = UiKit.Text(
                card.rectTransform,
                "Title",
                "USE A HINT?",
                48,
                Theme.Ink,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                title.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(700f, 70f),
                new Vector2(0f, -52f));

            Text detail = UiKit.Text(
                card.rectTransform,
                "Detail",
                "Your 3 free hints are used for today.",
                25,
                Theme.InkSoft,
                TextAnchor.MiddleCenter);
            UiKit.Anchor(
                detail.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(720f, 60f),
                new Vector2(0f, -132f));

            Image coin = UiKit.Image(
                card.rectTransform,
                "BalanceCoin",
                Color.white,
                SpriteFactory.Coins());
            coin.preserveAspect = true;
            coin.raycastTarget = false;
            UiKit.Anchor(
                coin.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(54f, 54f),
                new Vector2(-72f, 28f));

            _balanceText = UiKit.Text(
                card.rectTransform,
                "Balance",
                "0",
                34,
                Theme.Ink,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                _balanceText.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(170f, 58f),
                new Vector2(-35f, 28f));

            Button cancel = UiKit.Button(
                card.rectTransform,
                "Cancel",
                Theme.CircleBg,
                SpriteFactory.RoundedRect(64, 26),
                out Image cancelImage);
            UiKit.Anchor(
                cancelImage.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(300f, 84f),
                new Vector2(-174f, 54f));
            Text cancelText = UiKit.Text(
                cancelImage.rectTransform,
                "Label",
                "CANCEL",
                27,
                Theme.OnCircle,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(cancelText.rectTransform);
            cancel.onClick.AddListener(Hide);

            Button confirm = UiKit.Button(
                card.rectTransform,
                "Confirm",
                Theme.Gold,
                SpriteFactory.RoundedRect(64, 26),
                out Image confirmImage);
            UiKit.Anchor(
                confirmImage.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(390f, 84f),
                new Vector2(174f, 54f));
            _confirmText = UiKit.Text(
                confirmImage.rectTransform,
                "Label",
                "USE 50 COINS",
                27,
                Theme.Hex("#3A3320"),
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_confirmText.rectTransform);
            confirm.onClick.AddListener(Confirm);

            gameObject.SetActive(false);
        }

        public void Show(int currentBalance, int cost, Action confirmed)
        {
            DisplayedBalance = Mathf.Max(0, currentBalance);
            DisplayedCost = Mathf.Max(0, cost);
            _confirmed = confirmed;
            if (_balanceText != null)
                _balanceText.text = DisplayedBalance.ToString();
            if (_confirmText != null)
                _confirmText.text = $"USE {DisplayedCost} COINS";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Hide()
        {
            _confirmed = null;
            gameObject.SetActive(false);
        }

        private void Confirm()
        {
            Action callback = _confirmed;
            Hide();
            callback?.Invoke();
        }
    }
}
