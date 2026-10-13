using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class CoinServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.SetProgressOwner(null);
            EditModeTestSupport.ClearPlayerPrefs();
        }

        [TestCase(Difficulty.Easy, 5)]
        [TestCase(Difficulty.Medium, 10)]
        [TestCase(Difficulty.Hard, 15)]
        [TestCase(Difficulty.ExtraHard, 20)]
        public void Campaign_FirstClear_GrantsDifficultyAmount(
            Difficulty difficulty,
            int expected)
        {
            Assert.AreEqual(1, SaveService.HighestUnlocked(difficulty));
            int granted = CoinService.TryGrantCampaignFirstClear(difficulty, 1);
            Assert.AreEqual(expected, granted);
            Assert.AreEqual(expected, SaveService.Coins);

            SaveService.MarkCompleted(difficulty, 1);
            Assert.AreEqual(0, CoinService.TryGrantCampaignFirstClear(difficulty, 1));
            Assert.AreEqual(expected, SaveService.Coins);
        }

        [Test]
        public void Campaign_Replay_DoesNotGrant()
        {
            SaveService.MarkCompleted(Difficulty.Easy, 1);
            Assert.AreEqual(2, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(0, CoinService.TryGrantCampaignFirstClear(Difficulty.Easy, 1));
            Assert.AreEqual(0, SaveService.Coins);
        }

        [Test]
        public void DailyBase_GrantsOncePerDay()
        {
            Assert.AreEqual(10, CoinService.TryGrantDailyBase("20260724", true));
            Assert.AreEqual(10, SaveService.Coins);
            Assert.AreEqual(0, CoinService.TryGrantDailyBase("20260724", true));
            Assert.AreEqual(10, SaveService.Coins);
            Assert.AreEqual(10, CoinService.TryGrantDailyBase("20260725", true));
            Assert.AreEqual(20, SaveService.Coins);
        }

        [Test]
        public void DailyBase_IgnoresNonNewCompletion()
        {
            Assert.AreEqual(0, CoinService.TryGrantDailyBase("20260724", false));
            Assert.AreEqual(0, SaveService.Coins);
        }

        [TestCase(1, 50)]
        [TestCase(2, 40)]
        [TestCase(3, 30)]
        [TestCase(4, 0)]
        public void Podium_GrantsOncePerDay(int rank, int expected)
        {
            Assert.AreEqual(expected, CoinService.TryClaimPodium("20260723", rank));
            Assert.AreEqual(expected, SaveService.Coins);
            Assert.AreEqual(0, CoinService.TryClaimPodium("20260723", 1));
            Assert.AreEqual(expected, SaveService.Coins);
        }

        [Test]
        public void EconomySnapshot_MergeTakesMaxCoinsAndUnionSkins()
        {
            var left = new EconomySnapshot
            {
                Coins = 40,
                OwnedSkinIds = new[] { "classic", "gold" },
                EquippedSkinId = "gold"
            };
            var right = new EconomySnapshot
            {
                Coins = 90,
                OwnedSkinIds = new[] { "classic", "mint" },
                EquippedSkinId = "mint"
            };

            EconomySnapshot merged = EconomySnapshot.MergeMax(left, right);
            Assert.AreEqual(90, merged.Coins);
            CollectionAssert.AreEquivalent(
                new[] { "classic", "gold", "mint" },
                merged.OwnedSkinIds);
            Assert.AreEqual("mint", merged.EquippedSkinId);
        }

        [Test]
        public void SaveService_CoinsPersistPerOwner()
        {
            SaveService.SetProgressOwner("user-a");
            SaveService.AddCoins(25);
            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(0, SaveService.Coins);
            SaveService.AddCoins(7);
            SaveService.SetProgressOwner("user-a");
            Assert.AreEqual(25, SaveService.Coins);
        }
    }
}
