using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PaintMaze.Services;

namespace PaintMaze.Menu
{
    /// <summary>
    /// Home-screen modal for buying and equipping ball cosmetics.
    /// </summary>
    public sealed class BallShopPanel : MonoBehaviour
    {
        public event Action SelectionChanged;

        private const float CardWidth = 430f;
        private const float CardHeight = 338f;
        private const float RowGap = 44f;
        private const float RowStep = CardHeight + RowGap;
        private const float ColumnOffset = 235f;
        private const float PreviewTopInset = 150f;
        private const float PreviewHeight = 278f;
        private const float ListTopInset = 446f;
        private const float ListBottomInset = 130f;
        private const float ListSideInset = 30f;
        private const float ContentTopPad = 8f;
        private const float ContentBottomPad = 16f;

        private readonly Dictionary<string, ShopCard> _cards = new();
        private CosmeticService _cosmetics;
        private Text _balance;
        private Image _balanceIcon;
        private Text _message;
        private RectTransform _content;
        private ScrollRect _scroll;
        private BallSkinLivePreview _heroPreview;
        private Text _heroName;
        private Text _heroState;
        private Button _actionButton;
        private Image _actionBackground;
        private Text _actionLabel;
        private string _focusedSkinId;

        public void Build(RectTransform root, CosmeticService cosmetics)
        {
            _cosmetics = cosmetics;
            if (_cosmetics != null)
                _cosmetics.Changed += HandleCosmeticsChanged;

            var scrim = UiKit.Button(
                root,
                "ShopScrim",
                new Color(0f, 0f, 0f, 0.64f),
                null,
                out Image scrimImage);
            UiKit.Stretch(scrimImage.rectTransform);
            scrim.onClick.AddListener(Hide);

            Image card = UiKit.Image(
                root,
                "ShopCard",
                Theme.CardBg,
                SpriteFactory.RoundedRect(96, 28));
            RectTransform cardRt = card.rectTransform;
            UiKit.Anchor(
                cardRt,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(1020f, 1510f),
                new Vector2(0f, -20f));

            Text title = UiKit.Text(
                cardRt,
                "Title",
                "BALL LOCKER",
                48,
                Theme.Ink,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                title.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(0f, 0.5f),
                new Vector2(620f, 74f),
                new Vector2(58f, -100f));

            var balanceGroupObject = new GameObject(
                "BalanceGroup",
                typeof(RectTransform));
            balanceGroupObject.transform.SetParent(cardRt, false);
            RectTransform balanceGroup =
                balanceGroupObject.GetComponent<RectTransform>();
            UiKit.Anchor(
                balanceGroup,
                new Vector2(1f, 1f),
                new Vector2(1f, 0.5f),
                new Vector2(190f, 64f),
                new Vector2(-178f, -100f));

            _balance = UiKit.Text(
                balanceGroup,
                "Balance",
                string.Empty,
                30,
                Theme.Gold,
                TextAnchor.MiddleRight,
                FontStyle.Bold);
            UiKit.Anchor(
                _balance.rectTransform,
                new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f),
                new Vector2(142f, 60f),
                new Vector2(48f, 0f));
            Image balanceIcon = UiKit.Image(
                balanceGroup,
                "BalanceCoinsIcon",
                Color.white,
                SpriteFactory.Coins());
            _balanceIcon = balanceIcon;
            balanceIcon.raycastTarget = false;
            balanceIcon.preserveAspect = true;
            UiKit.Anchor(
                balanceIcon.rectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(38f, 38f),
                new Vector2(-80f, 0f));

            var close = UiKit.Button(
                cardRt,
                "Close",
                Theme.CircleBg,
                SpriteFactory.Circle(),
                out Image closeImage);
            UiKit.Anchor(
                closeImage.rectTransform,
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(64f, 64f),
                new Vector2(-96f, -100f));
            Image crossA = UiKit.Image(
                closeImage.rectTransform,
                "CrossA",
                Theme.OnCircle,
                SpriteFactory.RoundedRect(24, 7));
            crossA.raycastTarget = false;
            UiKit.Anchor(
                crossA.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(27f, 5f),
                Vector2.zero);
            crossA.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image crossB = UiKit.Image(
                closeImage.rectTransform,
                "CrossB",
                Theme.OnCircle,
                SpriteFactory.RoundedRect(24, 7));
            crossB.raycastTarget = false;
            UiKit.Anchor(
                crossB.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(27f, 5f),
                Vector2.zero);
            crossB.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            close.onClick.AddListener(Hide);

            BuildPreview(cardRt);
            BuildScrollList(cardRt);

            _message = UiKit.Text(
                cardRt,
                "Message",
                "SELECT A BALL",
                25,
                Theme.InkSoft,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Anchor(
                _message.rectTransform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(860f, 62f),
                new Vector2(0f, 48f));

            gameObject.SetActive(false);
        }

        private void BuildPreview(RectTransform parent)
        {
            Image panel = UiKit.Image(
                parent,
                "SelectedSkinPreview",
                Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.42f),
                SpriteFactory.RoundedRect(88, 24));
            UiKit.Anchor(
                panel.rectTransform,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(960f, PreviewHeight),
                new Vector2(0f, -PreviewTopInset));

            var heroObject = new GameObject(
                "HeroBall",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            heroObject.transform.SetParent(panel.rectTransform, false);
            RawImage heroImage = heroObject.GetComponent<RawImage>();
            heroImage.color = Color.white;
            heroImage.raycastTarget = false;
            UiKit.Anchor(
                heroImage.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(224f, 224f),
                new Vector2(-330f, 0f));
            _heroPreview = heroObject.AddComponent<BallSkinLivePreview>();
            _heroPreview.Build(heroImage);

            _heroName = UiKit.Text(
                panel.rectTransform,
                "HeroName",
                string.Empty,
                38,
                Theme.Ink,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                _heroName.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(520f, 56f),
                new Vector2(132f, 80f));

            _heroState = UiKit.Text(
                panel.rectTransform,
                "HeroState",
                string.Empty,
                25,
                Theme.InkSoft,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.Anchor(
                _heroState.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(520f, 44f),
                new Vector2(132f, 30f));

            _actionButton = UiKit.Button(
                panel.rectTransform,
                "SkinAction",
                Theme.Gold,
                SpriteFactory.RoundedRect(72, 22),
                out _actionBackground);
            UiKit.Anchor(
                _actionBackground.rectTransform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(430f, 82f),
                new Vector2(85f, -72f));
            _actionLabel = UiKit.Text(
                _actionBackground.rectTransform,
                "Label",
                string.Empty,
                28,
                Theme.CircleBg,
                TextAnchor.MiddleCenter,
                FontStyle.Bold);
            UiKit.Stretch(_actionLabel.rectTransform);
            _actionButton.onClick.AddListener(HandleActionPressed);
        }

        public void Show()
        {
            BallSkinDefinition equipped =
                CosmeticCatalog.GetOrClassic(SaveService.EquippedSkinId);
            _focusedSkinId = equipped.Id;
            if (_scroll != null)
            {
                _scroll.StopMovement();
                _scroll.verticalNormalizedPosition = 1f;
            }
            if (_content != null)
                _content.anchoredPosition = Vector2.zero;
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _heroPreview?.SetRendering(true);
        }

        public void Hide()
        {
            _heroPreview?.SetRendering(false);
            gameObject.SetActive(false);
        }

        // The selected-skin preview stays fixed while the unified catalog
        // moves underneath in a clipped scroll view.
        private void BuildScrollList(RectTransform parent)
        {
            Image viewport = UiKit.Image(
                parent,
                "ShopViewport",
                Color.Lerp(Theme.CardBg, Theme.Background, 0.22f),
                SpriteFactory.RoundedRect(72, 20));
            UiKit.Stretch(
                viewport.rectTransform,
                ListSideInset,
                ListSideInset,
                ListTopInset,
                ListBottomInset);
            viewport.gameObject.AddComponent<RectMask2D>();

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewport.rectTransform, false);
            _content = (RectTransform)contentObject.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport.rectTransform;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;
            _scroll.scrollSensitivity = 32f;

            BuildCatalog(_content);
        }

        private void BuildCatalog(RectTransform parent)
        {
            IReadOnlyList<BallSkinDefinition> skins = CosmeticCatalog.All;
            if (skins.Count == 0) return;

            int rows = (skins.Count + 1) / 2;
            float height =
                ContentTopPad +
                (rows - 1) * RowStep +
                CardHeight +
                ContentBottomPad;
            parent.sizeDelta = new Vector2(0f, height);
            BuildSectionCards(parent, skins, -ContentTopPad);
        }

        private void BuildSectionCards(
            RectTransform parent,
            IReadOnlyList<BallSkinDefinition> skins,
            float sectionTop)
        {
            for (int index = 0; index < skins.Count; index++)
            {
                BallSkinDefinition skin = skins[index];
                int row = index / 2;
                int col = index % 2;
                float x = col == 0 ? -ColumnOffset : ColumnOffset;
                float y = sectionTop - row * RowStep;

                Button button = UiKit.Button(
                    parent,
                    "Skin_" + skin.Id,
                    Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.35f),
                    SpriteFactory.RoundedRect(80, 24),
                    out Image cardImage);
                UiKit.Anchor(
                    cardImage.rectTransform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(CardWidth, CardHeight),
                    new Vector2(x, y));

                Image preview = UiKit.Image(
                    cardImage.rectTransform,
                    "Preview",
                    Color.white,
                    SportsBallArt.IconFor(skin.Id));
                preview.type = Image.Type.Simple;
                preview.preserveAspect = true;
                preview.raycastTarget = false;
                UiKit.Anchor(
                    preview.rectTransform,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(174f, 174f),
                    new Vector2(0f, -24f));

                Text name = UiKit.Text(
                    cardImage.rectTransform,
                    "Name",
                    skin.DisplayName.ToUpperInvariant(),
                    29,
                    Theme.Ink,
                    TextAnchor.MiddleCenter,
                    FontStyle.Bold);
                UiKit.Anchor(
                    name.rectTransform,
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(390f, 48f),
                    new Vector2(0f, 91f));

                Text state = UiKit.Text(
                    cardImage.rectTransform,
                    "State",
                    string.Empty,
                    23,
                    Theme.Gold,
                    TextAnchor.MiddleCenter,
                    FontStyle.Bold);
                UiKit.Anchor(
                    state.rectTransform,
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(390f, 46f),
                    new Vector2(0f, 40f));

                string capturedId = skin.Id;
                button.onClick.AddListener(() => HandleSkinPressed(capturedId));
                _cards[skin.Id] = new ShopCard
                {
                    Background = cardImage,
                    State = state,
                    Button = button
                };
            }
        }

        private void HandleSkinPressed(string skinId)
        {
            if (!CosmeticCatalog.TryGet(skinId, out BallSkinDefinition skin))
                return;

            _focusedSkinId = skin.Id;
            _message.text = "SELECTED " + skin.DisplayName.ToUpperInvariant();
            Refresh();
        }

        private void HandleActionPressed()
        {
            if (_cosmetics == null ||
                !CosmeticCatalog.TryGet(
                    _focusedSkinId,
                    out BallSkinDefinition skin))
                return;

            if (SaveService.OwnsSkin(skin.Id))
            {
                if (_cosmetics.TryEquip(skin.Id))
                {
                    _message.text = "EQUIPPED " + skin.DisplayName.ToUpperInvariant();
                    Refresh();
                    SelectionChanged?.Invoke();
                }
                return;
            }

            CosmeticPurchaseResult result = _cosmetics.TryBuy(skin.Id);
            switch (result)
            {
                case CosmeticPurchaseResult.Purchased:
                    _message.text = "UNLOCKED & EQUIPPED";
                    Refresh();
                    SelectionChanged?.Invoke();
                    break;
                case CosmeticPurchaseResult.InsufficientCoins:
                    int needed = Mathf.Max(
                        0,
                        skin.Price - SaveService.Coins);
                    _message.text = $"NEED {needed} MORE COINS";
                    UpdatePreview();
                    break;
                case CosmeticPurchaseResult.AlreadyOwned:
                    _cosmetics.TryEquip(skin.Id);
                    Refresh();
                    SelectionChanged?.Invoke();
                    break;
                default:
                    _message.text = "BALL UNAVAILABLE";
                    break;
            }
        }

        private void HandleCosmeticsChanged()
        {
            if (gameObject.activeSelf)
                Refresh();
        }

        private void Refresh()
        {
            if (_balance != null)
            {
                _balance.text = SaveService.Coins.ToString("N0");
                if (_balanceIcon != null)
                {
                    float balanceWidth = Mathf.Min(
                        _balance.rectTransform.rect.width,
                        _balance.preferredWidth);
                    UiKit.Anchor(
                        _balanceIcon.rectTransform,
                        new Vector2(1f, 0.5f),
                        new Vector2(1f, 0.5f),
                        new Vector2(38f, 38f),
                        new Vector2(-balanceWidth - 10f, 0f));
                }
            }
            string equipped = SaveService.EquippedSkinId;
            foreach (BallSkinDefinition skin in CosmeticCatalog.All)
            {
                if (!_cards.TryGetValue(skin.Id, out ShopCard card))
                    continue;

                bool owned = SaveService.OwnsSkin(skin.Id);
                bool isEquipped = equipped == skin.Id;
                bool isSelected = _focusedSkinId == skin.Id;
                card.State.text = isEquipped
                    ? "EQUIPPED"
                    : owned
                        ? "OWNED"
                        : skin.Price.ToString("N0") + " COINS";
                card.State.color = isEquipped ? Theme.Gold : Theme.InkSoft;
                card.Background.color = isSelected
                    ? Color.Lerp(Theme.Gold, Theme.CardBg, 0.60f)
                    : isEquipped
                    ? Color.Lerp(Theme.Gold, Theme.CardBg, 0.78f)
                    : Color.Lerp(Theme.TrackBg, Theme.CardBg, 0.35f);
                card.Button.interactable = true;
            }
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_heroPreview == null ||
                !CosmeticCatalog.TryGet(
                    _focusedSkinId,
                    out BallSkinDefinition skin))
                return;

            _heroPreview.SetSkin(skin.Id);
            _heroName.text = skin.DisplayName.ToUpperInvariant();
            bool owned = SaveService.OwnsSkin(skin.Id);
            bool equipped = SaveService.EquippedSkinId == skin.Id;
            if (equipped)
            {
                _heroState.text = "CURRENT BALL";
                _heroState.color = Theme.Gold;
                _actionLabel.text = "EQUIPPED";
                _actionButton.interactable = false;
                _actionBackground.color = Theme.TrackBg;
                _actionLabel.color = Theme.InkSoft;
            }
            else if (owned)
            {
                _heroState.text = "OWNED";
                _heroState.color = Theme.InkSoft;
                _actionLabel.text = "EQUIP";
                _actionButton.interactable = true;
                _actionBackground.color = Theme.Gold;
                _actionLabel.color = Theme.CircleBg;
            }
            else
            {
                _heroState.text = skin.Price.ToString("N0") + " COINS";
                _heroState.color = Theme.InkSoft;
                _actionLabel.text = "BUY  " + skin.Price.ToString("N0");
                _actionButton.interactable = true;
                _actionBackground.color = Theme.Gold;
                _actionLabel.color = Theme.CircleBg;
            }
        }

        private void OnDestroy()
        {
            _heroPreview?.Dispose();
            if (_cosmetics != null)
                _cosmetics.Changed -= HandleCosmeticsChanged;
        }

        private sealed class ShopCard
        {
            public Image Background;
            public Text State;
            public Button Button;
        }
    }
}
