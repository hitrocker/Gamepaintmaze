using System.Linq;
using NUnit.Framework;
using PaintMaze.Menu;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Tests
{
    public class BallShopPanelTests
    {
        private GameObject _root;
        private GameObject _panelObject;
        private BallShopPanel _panel;
        private CosmeticService _cosmetics;
        private ThemeMode _originalTheme;

        [SetUp]
        public void SetUp()
        {
            _originalTheme = Theme.Mode;
            Theme.Mode = ThemeMode.Light;
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();

            _root = new GameObject("ShopTestRoot", typeof(RectTransform));
            _panelObject = new GameObject("BallShopPanel", typeof(RectTransform));
            _panelObject.transform.SetParent(_root.transform, false);
            _cosmetics = new CosmeticService();
            _panel = _panelObject.AddComponent<BallShopPanel>();
            _panel.Build(
                (RectTransform)_panelObject.transform,
                _cosmetics);
        }

        [TearDown]
        public void TearDown()
        {
            Theme.Mode = _originalTheme;
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void SpriteFactory_CreatesNonEmptyCoinAndCartIcons()
        {
            AssertSpriteHasVisiblePixels(SpriteFactory.Coins(64));
            AssertSpriteHasVisiblePixels(SpriteFactory.Cart(64));
        }

        [Test]
        public void CatalogIcons_UseBakedHighResolutionSprites()
        {
            foreach (BallSkinDefinition skin in CosmeticCatalog.All)
            {
                Sprite icon = SportsBallArt.IconFor(skin.Id);
                Assert.IsNotNull(icon, skin.Id);
                Assert.GreaterOrEqual(icon.texture.width, 512, skin.Id);
                Assert.GreaterOrEqual(icon.texture.height, 512, skin.Id);
            }
        }

        [Test]
        public void Build_CreatesHeroActionAndUnifiedCatalog()
        {
            Transform hero = Find("ShopCard/SelectedSkinPreview/HeroBall");
            BallSkinLivePreview preview =
                hero.GetComponent<BallSkinLivePreview>();
            Assert.IsNotNull(preview);
            Assert.AreEqual(512, preview.TargetTexture.width);
            Assert.IsFalse(preview.IsRendering);
            Assert.IsNotNull(Find("ShopCard/SelectedSkinPreview/SkinAction"));

            Transform shopCard = Find("ShopCard");
            Assert.IsNull(shopCard.Find("Tab_Sports"));
            Assert.IsNull(shopCard.Find("Tab_SolarSystem"));
            foreach (BallSkinDefinition skin in CosmeticCatalog.All)
                Assert.IsNotNull(
                    Find("ShopCard/ShopViewport/Content/Skin_" + skin.Id));
        }

        [Test]
        public void Show_FocusesEquippedSkinInUnifiedCatalog()
        {
            SaveService.GrantSkin("uranus");
            Assert.IsTrue(_cosmetics.TryEquip("uranus"));

            _panel.Show();

            Assert.AreEqual(
                "URANUS",
                TextAt("ShopCard/SelectedSkinPreview/HeroName").text);
            Assert.IsTrue(
                Find("ShopCard/ShopViewport/Content/Skin_soccer").gameObject.activeSelf);
            Assert.IsTrue(
                Find("ShopCard/ShopViewport/Content/Skin_uranus").gameObject.activeSelf);
            Assert.AreEqual(
                "uranus",
                Find("ShopCard/SelectedSkinPreview/HeroBall")
                    .GetComponent<BallSkinLivePreview>()
                    .CurrentSkinId);
            Button action = Find(
                "ShopCard/SelectedSkinPreview/SkinAction").GetComponent<Button>();
            Assert.IsFalse(action.interactable);
            Assert.AreEqual(
                "EQUIPPED",
                TextAt("ShopCard/SelectedSkinPreview/SkinAction/Label").text);
        }

        [Test]
        public void SelectingLockedCard_DoesNotSpendUntilBuyAction()
        {
            SaveService.Coins = 700;
            int changes = 0;
            _panel.SelectionChanged += () => changes++;
            _panel.Show();

            Click("ShopCard/ShopViewport/Content/Skin_venus");

            Assert.AreEqual(700, SaveService.Coins);
            Assert.IsFalse(SaveService.OwnsSkin("venus"));
            Assert.AreEqual(
                "VENUS",
                TextAt("ShopCard/SelectedSkinPreview/HeroName").text);
            Assert.AreEqual(
                "venus",
                Find("ShopCard/SelectedSkinPreview/HeroBall")
                    .GetComponent<BallSkinLivePreview>()
                    .CurrentSkinId);
            Assert.AreEqual(
                "BUY  500",
                TextAt("ShopCard/SelectedSkinPreview/SkinAction/Label").text);

            Click("ShopCard/SelectedSkinPreview/SkinAction");

            Assert.AreEqual(200, SaveService.Coins);
            Assert.IsTrue(SaveService.OwnsSkin("venus"));
            Assert.AreEqual("venus", SaveService.EquippedSkinId);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void OwnedSkin_EquipsOnlyFromHeroAction()
        {
            SaveService.GrantSkin("soccer");
            _panel.Show();

            Click("ShopCard/ShopViewport/Content/Skin_soccer");

            Assert.AreEqual("classic", SaveService.EquippedSkinId);
            Assert.AreEqual(
                "EQUIP",
                TextAt("ShopCard/SelectedSkinPreview/SkinAction/Label").text);

            Click("ShopCard/SelectedSkinPreview/SkinAction");

            Assert.AreEqual("soccer", SaveService.EquippedSkinId);
        }

        [Test]
        public void SaturnPreview_KeepsRingsOutsideRotatingSphere()
        {
            _panel.Show();
            Click("ShopCard/ShopViewport/Content/Skin_saturn");

            BallSkinLivePreview preview =
                Find("ShopCard/SelectedSkinPreview/HeroBall")
                    .GetComponent<BallSkinLivePreview>();
            Assert.AreEqual("saturn", preview.CurrentSkinId);
            Assert.IsTrue(preview.RingsVisible);
            Assert.AreEqual(
                preview.SpinTransform.parent,
                preview.RingTransform.parent);
            Assert.AreNotEqual(
                preview.SpinTransform,
                preview.RingTransform.parent);
        }

        [Test]
        public void ShopVisibility_ControlsPreviewRendering()
        {
            BallSkinLivePreview preview =
                Find("ShopCard/SelectedSkinPreview/HeroBall")
                    .GetComponent<BallSkinLivePreview>();
            Assert.IsFalse(preview.IsRendering);

            _panel.Show();
            Assert.IsTrue(preview.IsRendering);

            _panel.Hide();
            Assert.IsFalse(preview.IsRendering);
        }

        [Test]
        public void Dispose_ReleasesPreviewCameraMeshAndRenderTexture()
        {
            BallSkinLivePreview preview =
                Find("ShopCard/SelectedSkinPreview/HeroBall")
                    .GetComponent<BallSkinLivePreview>();
            Camera camera = preview.PreviewCamera;
            RenderTexture target = preview.TargetTexture;
            Mesh ringMesh = preview.RingTransform
                .GetComponent<MeshFilter>()
                .sharedMesh;

            preview.Dispose();

            Assert.IsTrue(camera == null);
            Assert.IsTrue(target == null);
            Assert.IsTrue(ringMesh == null);
        }

        [Test]
        public void Show_ResetsUnifiedCatalogScrollToTop()
        {
            _panel.Show();
            RectTransform content = (RectTransform)Find(
                "ShopCard/ShopViewport/Content");
            content.anchoredPosition = new Vector2(0f, 500f);

            _panel.Hide();
            _panel.Show();

            Assert.IsTrue(
                Find("ShopCard/ShopViewport/Content/Skin_classic").gameObject.activeSelf);
            Assert.IsTrue(
                Find("ShopCard/ShopViewport/Content/Skin_sun").gameObject.activeSelf);
            Assert.AreEqual(Vector2.zero, content.anchoredPosition);
        }

        private Transform Find(string path)
        {
            Transform found = _panelObject.transform.Find(path);
            Assert.IsNotNull(found, "Missing shop transform: " + path);
            return found;
        }

        private Text TextAt(string path) => Find(path).GetComponent<Text>();

        private void Click(string path)
        {
            Button button = Find(path).GetComponent<Button>();
            Assert.IsNotNull(button, "Missing Button component: " + path);
            button.onClick.Invoke();
        }

        private static void AssertSpriteHasVisiblePixels(Sprite sprite)
        {
            Assert.IsNotNull(sprite);
            Assert.IsTrue(
                sprite.texture.GetPixels32().Any(pixel => pixel.a > 0),
                sprite.name + " has no visible pixels");
        }
    }
}
