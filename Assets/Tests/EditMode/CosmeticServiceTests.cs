using System.Linq;
using NUnit.Framework;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class CosmeticServiceTests
    {
        private CosmeticService _service;

        [SetUp]
        public void SetUp()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
            _service = new CosmeticService();
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
        }

        [Test]
        public void Catalog_HasFreeClassicAndEqualPricePaidSkins()
        {
            string[] ids = CosmeticCatalog.All.Select(skin => skin.Id).ToArray();
            Assert.AreEqual("classic", ids.First());
            Assert.AreEqual(ids.Length, ids.Distinct().Count());
            Assert.AreEqual(0, CosmeticCatalog.GetOrClassic("classic").Price);
            foreach (BallSkinDefinition skin in CosmeticCatalog.All.Skip(1))
                Assert.AreEqual(CosmeticCatalog.SportsBallPrice, skin.Price);
        }

        [Test]
        public void Catalog_EveryCategoryHasSkinsAndIsListed()
        {
            foreach (SkinCategory category in CosmeticCatalog.Categories)
            {
                Assert.IsNotEmpty(
                    CosmeticCatalog.All.Where(skin => skin.Category == category).ToArray(),
                    $"Category {category} has no skins");
                Assert.IsNotEmpty(CosmeticCatalog.CategoryLabel(category));
            }

            foreach (BallSkinDefinition skin in CosmeticCatalog.All)
                CollectionAssert.Contains(CosmeticCatalog.Categories, skin.Category);
        }

        [Test]
        public void SportsBallArt_GeneratesDistinctIconsAndMaterials()
        {
            Sprite[] icons = CosmeticCatalog.All
                .Select(skin => SportsBallArt.IconFor(skin.Id))
                .ToArray();
            Material[] materials = CosmeticCatalog.All
                .Select(skin => SportsBallArt.MaterialFor(skin.Id))
                .ToArray();

            Assert.IsTrue(icons.All(icon => icon != null));
            Assert.IsTrue(materials.All(material => material != null));
            Assert.AreEqual(icons.Length, icons.Distinct().Count());
            Assert.AreEqual(materials.Length, materials.Distinct().Count());
        }

        [TestCase("soccer", 0.20f)]
        [TestCase("baseball", 0.16f)]
        [TestCase("uranus", 0.04f)]
        [TestCase("saturn", 0.04f)]
        [TestCase("cricket", 0.50f)]
        [TestCase("basketball", 0.28f)]
        [TestCase("tennis", 0.22f)]
        [TestCase("golf", 0.18f)]
        [TestCase("bowling", 0.55f)]
        [TestCase("eightball", 0.62f)]
        public void SportsBallArt_UsesApprovedGlossiness(
            string skinId,
            float expected)
        {
            Material material = SportsBallArt.MaterialFor(skinId);
            Assert.AreEqual(
                expected,
                material.GetFloat("_Glossiness"),
                0.001f);
        }

        [Test]
        public void Classic_IsAlwaysOwnedAndEquippable()
        {
            Assert.IsTrue(SaveService.OwnsSkin("classic"));
            Assert.IsTrue(_service.TryEquip("classic"));
            Assert.AreEqual("classic", SaveService.EquippedSkinId);
        }

        [Test]
        public void Buy_SpendsCoinsGrantsAndEquips()
        {
            SaveService.Coins = 700;

            CosmeticPurchaseResult result = _service.TryBuy("soccer");

            Assert.AreEqual(CosmeticPurchaseResult.Purchased, result);
            Assert.AreEqual(200, SaveService.Coins);
            Assert.IsTrue(SaveService.OwnsSkin("soccer"));
            Assert.AreEqual("soccer", SaveService.EquippedSkinId);
        }

        [Test]
        public void Buy_WithInsufficientCoins_DoesNotMutateEconomy()
        {
            SaveService.Coins = 499;

            CosmeticPurchaseResult result = _service.TryBuy("basketball");

            Assert.AreEqual(CosmeticPurchaseResult.InsufficientCoins, result);
            Assert.AreEqual(499, SaveService.Coins);
            Assert.IsFalse(SaveService.OwnsSkin("basketball"));
            Assert.AreEqual("classic", SaveService.EquippedSkinId);
        }

        [Test]
        public void Buy_AlreadyOwned_DoesNotSpendTwice()
        {
            SaveService.Coins = 1000;
            Assert.AreEqual(
                CosmeticPurchaseResult.Purchased,
                _service.TryBuy("cricket"));
            Assert.AreEqual(500, SaveService.Coins);

            Assert.AreEqual(
                CosmeticPurchaseResult.AlreadyOwned,
                _service.TryBuy("cricket"));
            Assert.AreEqual(500, SaveService.Coins);
        }

        [Test]
        public void Equip_RequiresOwnershipAndKnownCatalogId()
        {
            Assert.IsFalse(_service.TryEquip("baseball"));
            Assert.IsFalse(_service.TryEquip("not-a-skin"));
            Assert.AreEqual("classic", SaveService.EquippedSkinId);

            SaveService.GrantSkin("baseball");
            Assert.IsTrue(_service.TryEquip("baseball"));
            Assert.AreEqual("baseball", SaveService.EquippedSkinId);
        }

        [Test]
        public void Buy_RejectsUnknownSkinWithoutSpending()
        {
            SaveService.Coins = 1000;
            Assert.AreEqual(
                CosmeticPurchaseResult.UnknownSkin,
                _service.TryBuy("developer-only"));
            Assert.AreEqual(1000, SaveService.Coins);
        }
    }
}
