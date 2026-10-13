using System.Linq;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class SaveServiceAdoptOwnerTests
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

        [Test]
        public void AdoptOwner_FromNullOwner_KeepsLegacyCoinsProgressAndSkin()
        {
            SaveService.SetProgressOwner(null);
            SaveService.MarkCompleted(Difficulty.Easy, 14);
            SaveService.AddCoins(80);
            SaveService.GrantSkin("soccer");
            SaveService.EquippedSkinId = "soccer";

            SaveService.AdoptOwner("new-uid");

            Assert.AreEqual("new-uid", SaveService.CurrentProgressOwner);
            Assert.AreEqual(15, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(80, SaveService.Coins);
            Assert.IsTrue(SaveService.OwnsSkin("soccer"));
            Assert.AreEqual("soccer", SaveService.EquippedSkinId);
        }

        [Test]
        public void AdoptOwner_CarriesGuestScopedEconomyToNewUid()
        {
            SaveService.SetProgressOwner("guest-uid");
            SaveService.MarkCompleted(Difficulty.Easy, 14);
            SaveService.AddCoins(80);
            SaveService.GrantSkin("soccer");
            SaveService.EquippedSkinId = "soccer";
            ProgressSnapshot progress = SaveService.CaptureProgress();
            EconomySnapshot economy = SaveService.CaptureEconomy();

            SaveService.AdoptOwner("new-uid", progress, economy);

            Assert.AreEqual("new-uid", SaveService.CurrentProgressOwner);
            Assert.AreEqual(15, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(80, SaveService.Coins);
            Assert.IsTrue(SaveService.OwnsSkin("soccer"));
            Assert.AreEqual("soccer", SaveService.EquippedSkinId);
        }

        [Test]
        public void AdoptOwner_SameUid_LeavesExistingBalanceUnchanged()
        {
            SaveService.SetProgressOwner("guest-uid");
            SaveService.AddCoins(50);

            SaveService.AdoptOwner("guest-uid");

            Assert.AreEqual(50, SaveService.Coins);
        }

        [Test]
        public void AdoptOwner_ReturningAccount_MergesMaxCoinsAndUnionsSkins()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);
            PlayerPrefs.SetString("pm_progress_claimed_by", "legacy-owner");

            SaveService.SetProgressOwner("guest-uid");
            SaveService.AddCoins(80);
            SaveService.GrantSkin("soccer");
            SaveService.MarkCompleted(Difficulty.Easy, 9);
            ProgressSnapshot guestProgress = SaveService.CaptureProgress();
            EconomySnapshot guestEconomy = SaveService.CaptureEconomy();

            SaveService.SetProgressOwner("account-uid");
            SaveService.AddCoins(40);
            SaveService.GrantSkin("baseball");

            SaveService.AdoptOwner("account-uid", guestProgress, guestEconomy);

            Assert.AreEqual(80, SaveService.Coins);
            Assert.AreEqual(10, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.IsTrue(SaveService.OwnsSkin("soccer"));
            Assert.IsTrue(SaveService.OwnsSkin("baseball"));
        }

        [Test]
        public void AdoptOwner_DoesNotStealLegacyWhenAlreadyClaimed()
        {
            PlayerPrefs.SetInt("pm_save_schema", SaveService.CurrentSchemaVersion);
            PlayerPrefs.SetInt("pm_unlocked_0", 18);
            PlayerPrefs.SetInt("pm_coins", 80);
            PlayerPrefs.SetString("pm_progress_claimed_by", "first-user");

            SaveService.AdoptOwner("second-user");

            Assert.AreEqual(1, SaveService.HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(0, SaveService.Coins);
            Assert.AreEqual(18, SaveService.CaptureProgressForOwner(null)
                .HighestUnlocked(Difficulty.Easy));
            Assert.AreEqual(80, SaveService.CaptureEconomyForOwner(null).Coins);
        }

        [Test]
        public void AdoptOwner_KeepsGuestOwnedSkinWithoutCoinBalance()
        {
            SaveService.SetProgressOwner("guest-uid");
            SaveService.GrantSkin("soccer");
            SaveService.EquippedSkinId = "soccer";
            EconomySnapshot economy = SaveService.CaptureEconomy();

            SaveService.AdoptOwner("new-uid", economyCarry: economy);

            Assert.IsTrue(SaveService.OwnsSkin("soccer"));
            Assert.AreEqual("soccer", SaveService.EquippedSkinId);
            CollectionAssert.Contains(SaveService.OwnedSkinIds.ToArray(), "soccer");
        }
    }
}
