using System;
using System.Reflection;
using NUnit.Framework;
using PaintMaze.Domain;
using PaintMaze.Game;
using PaintMaze.Services;
using UnityEngine;
using UnityEngine.UI;

namespace PaintMaze.Tests
{
    public class HintServiceTests
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
        public void FreeQuota_AllowsThreeUsesThenResetsOnNextUtcDay()
        {
            DateTime now = new(2026, 7, 25, 23, 59, 0, DateTimeKind.Utc);
            var service = new HintService(utcNow: () => now);

            Assert.AreEqual(3, service.FreeHintsRemaining);
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.IsFalse(service.TryConsumeFreeHint());
            Assert.AreEqual(0, service.FreeHintsRemaining);

            now = now.AddMinutes(2);
            Assert.AreEqual(3, service.FreeHintsRemaining);
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.AreEqual(2, service.FreeHintsRemaining);
        }

        [Test]
        public void FreeQuota_IsIsolatedPerProgressOwner()
        {
            DateTime now = new(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);
            var service = new HintService(utcNow: () => now);

            SaveService.SetProgressOwner("user-a");
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.AreEqual(2, service.FreeHintsRemaining);

            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(3, service.FreeHintsRemaining);
            Assert.IsTrue(service.TryConsumeFreeHint());
            Assert.IsTrue(service.TryConsumeFreeHint());

            SaveService.SetProgressOwner("user-a");
            Assert.AreEqual(2, service.FreeHintsRemaining);
        }

        [Test]
        public void EconomySnapshot_UsesMaxCountOnSameDayAndNewerDayAcrossDevices()
        {
            var local = new EconomySnapshot
            {
                DailyHintDayId = "20260725",
                DailyHintsUsed = 1
            };
            var sameDayRemote = new EconomySnapshot
            {
                DailyHintDayId = "20260725",
                DailyHintsUsed = 3
            };
            var nextDay = new EconomySnapshot
            {
                DailyHintDayId = "20260726",
                DailyHintsUsed = 2
            };

            EconomySnapshot sameDay =
                EconomySnapshot.MergeMax(local, sameDayRemote);
            Assert.AreEqual("20260725", sameDay.DailyHintDayId);
            Assert.AreEqual(3, sameDay.DailyHintsUsed);

            EconomySnapshot newer =
                EconomySnapshot.MergeMax(sameDay, nextDay);
            Assert.AreEqual("20260726", newer.DailyHintDayId);
            Assert.AreEqual(2, newer.DailyHintsUsed);
        }

        [Test]
        public void SaveService_CapturesAppliesAndClearsOwnerHintUsage()
        {
            SaveService.SetProgressOwner("user-a");
            SaveService.SetDailyHintUsage("20260725", 2);
            EconomySnapshot captured = SaveService.CaptureEconomy();
            Assert.AreEqual("20260725", captured.DailyHintDayId);
            Assert.AreEqual(2, captured.DailyHintsUsed);

            SaveService.ApplyEconomy(
                "user-b",
                new EconomySnapshot
                {
                    DailyHintDayId = "20260726",
                    DailyHintsUsed = 3
                },
                mergeWithExisting: false);
            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(3, SaveService.DailyHintsUsed);

            SaveService.ClearProgressForUser("user-b");
            SaveService.SetProgressOwner("user-b");
            Assert.AreEqual(string.Empty, SaveService.DailyHintDayId);
            Assert.AreEqual(0, SaveService.DailyHintsUsed);
        }

        [Test]
        public void PaidHint_SpendsExactlyFiftyOnlyWhenAffordable()
        {
            var service = new HintService();
            SaveService.Coins = 80;

            Assert.IsTrue(service.TryPurchaseHint());
            Assert.AreEqual(30, SaveService.Coins);
            Assert.IsFalse(service.TryPurchaseHint());
            Assert.AreEqual(30, SaveService.Coins);
        }

        [Test]
        public void GameplayValidationAndTutorialPath_DoNotMutateEconomy()
        {
            var gameObject = new GameObject("GameController");
            try
            {
                GameController game = gameObject.AddComponent<GameController>();
                Level level = TestHelpers.Make(
                    "S..",
                    "##.",
                    "...");
                SetPrivate(game, "_level", level);
                SetPrivate(game, "_state", new GameState(level));

                Assert.IsTrue(game.CanShowHint());
                Assert.AreEqual(0, SaveService.DailyHintsUsed);
                Assert.AreEqual(0, SaveService.Coins);

                SetPrivate(game, "_state", null);
                Assert.IsFalse(game.CanShowHint());
                Assert.AreEqual(0, SaveService.DailyHintsUsed);
                Assert.AreEqual(0, SaveService.Coins);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void HudBadge_ShowsFreeCountThenCompactCoinPrice()
        {
            var rootObject = new GameObject("Root", typeof(RectTransform));
            try
            {
                RectTransform root = rootObject.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(1080f, 1920f);
                UIManager hud = rootObject.AddComponent<UIManager>();
                hud.Build(root, Difficulty.Easy);

                hud.UpdateHints(2, false);
                Assert.AreEqual("2", hud.hintCountText.text);
                Assert.IsFalse(
                    hud.hintCountBadge.transform
                        .Find("HintPriceCoin").gameObject.activeSelf);

                hud.UpdateHints(0, false);
                Assert.AreEqual("50", hud.hintCountText.text);
                Assert.IsTrue(
                    hud.hintCountBadge.transform
                        .Find("HintPriceCoin").gameObject.activeSelf);
                Assert.That(
                    hud.hintCountBadge.GetComponent<RectTransform>().sizeDelta.x,
                    Is.GreaterThan(48f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void PurchaseDialog_CancelAndConfirmHaveExplicitBehavior()
        {
            var rootObject = new GameObject("Root", typeof(RectTransform));
            var dialogObject = new GameObject("Dialog", typeof(RectTransform));
            try
            {
                RectTransform root = rootObject.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(1080f, 1920f);
                HintPurchaseDialog dialog =
                    dialogObject.AddComponent<HintPurchaseDialog>();
                dialog.Build(root);
                bool confirmed = false;

                dialog.Show(120, 50, () => confirmed = true);
                Assert.IsTrue(dialog.IsVisible);
                Assert.AreEqual(120, dialog.DisplayedBalance);
                Assert.AreEqual(50, dialog.DisplayedCost);

                Button cancel = dialog.transform
                    .Find("HintPurchaseCard/Cancel")
                    .GetComponent<Button>();
                cancel.onClick.Invoke();
                Assert.IsFalse(dialog.IsVisible);
                Assert.IsFalse(confirmed);

                dialog.Show(120, 50, () => confirmed = true);
                Button confirm = dialog.transform
                    .Find("HintPurchaseCard/Confirm")
                    .GetComponent<Button>();
                confirm.onClick.Invoke();
                Assert.IsFalse(dialog.IsVisible);
                Assert.IsTrue(confirmed);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
