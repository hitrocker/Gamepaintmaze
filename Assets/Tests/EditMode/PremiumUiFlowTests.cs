using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PaintMaze.Menu;
using PaintMaze.Services;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class PremiumUiFlowTests
    {
        [TestCase(UiPage.Home)]
        [TestCase(UiPage.Game)]
        [TestCase(UiPage.Complete)]
        [TestCase(UiPage.SignIn)]
        [TestCase(UiPage.Account)]
        [TestCase(UiPage.Leaderboard)]
        public void ExclusiveNavigation_ShowsOnlySelectedPage(UiPage active)
        {
            foreach (UiPage candidate in System.Enum.GetValues(typeof(UiPage)))
                Assert.AreEqual(active == candidate,
                    UiFlowPolicy.IsVisible(active, candidate));
        }

        [Test]
        public void GuestAndMissingSessions_RouteToSignIn()
        {
            Assert.AreEqual(AccountEntryDestination.SignIn,
                UiFlowPolicy.AccountDestination(false, true));
            Assert.AreEqual(AccountEntryDestination.SignIn,
                UiFlowPolicy.AccountDestination(true, true));
            Assert.AreEqual(AccountEntryDestination.Account,
                UiFlowPolicy.AccountDestination(true, false));
        }

        [Test]
        public void SignInMode_PreservesExistingVersusCreateSemantics()
        {
            Assert.IsTrue(UiFlowPolicy.UsesExistingAccount(true));
            Assert.IsFalse(UiFlowPolicy.UsesExistingAccount(false));
        }

        [Test]
        public void PodiumEntries_UsesFirstThreeRankedPlayers()
        {
            var entries = new List<LeaderboardEntry>
            {
                new() { UserId = "1", DisplayName = "One", HighestLevel = 50 },
                new() { UserId = "2", DisplayName = "Two", HighestLevel = 40 },
                new() { UserId = "3", DisplayName = "Three", HighestLevel = 30 },
                new() { UserId = "4", DisplayName = "Four", HighestLevel = 20 }
            };

            IReadOnlyList<LeaderboardEntry> podium =
                LeaderboardController.PodiumEntries(entries);

            Assert.AreEqual(3, podium.Count);
            Assert.AreEqual("1", podium[0].UserId);
            Assert.AreEqual("2", podium[1].UserId);
            Assert.AreEqual("3", podium[2].UserId);
        }

        [TestCase(false, 0f, 100f, true)]
        [TestCase(true, 80f, 100f, true)]
        [TestCase(true, 100f, 100f, false)]
        [TestCase(true, 120f, 100f, false)]
        public void PinnedPlayer_ShowsOnlyWhileLoadedRowIsBelowMergeSlot(
            bool rowInLoadedList,
            float ownRowTop,
            float pinnedTop,
            bool expected)
        {
            Assert.AreEqual(expected, LeaderboardController.ShouldShowPinnedRow(
                rowInLoadedList, ownRowTop, pinnedTop));
        }

        [Test]
        public void PinnedPlayer_MatchesNormalLeaderboardRowDimensions()
        {
            var cardObject = new GameObject("Card", typeof(RectTransform));
            var contentObject = new GameObject("Content", typeof(RectTransform));
            var listRowObject = new GameObject("ListRow", typeof(RectTransform));
            var pinnedRowObject = new GameObject("PinnedRow", typeof(RectTransform));
            try
            {
                RectTransform card = cardObject.GetComponent<RectTransform>();
                card.sizeDelta = new Vector2(1080f, 1920f);

                RectTransform content = contentObject.GetComponent<RectTransform>();
                content.SetParent(card, false);
                content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
                content.sizeDelta = new Vector2(900f, 1000f);

                RectTransform listRow = listRowObject.GetComponent<RectTransform>();
                listRow.SetParent(content, false);
                RectTransform pinnedRow = pinnedRowObject.GetComponent<RectTransform>();
                pinnedRow.SetParent(card, false);

                MethodInfo configureList = typeof(LeaderboardController).GetMethod(
                    "ConfigureListRowRect",
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo configurePinned = typeof(LeaderboardController).GetMethod(
                    "ConfigurePinnedRowRect",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(configureList);
                Assert.IsNotNull(configurePinned);

                configureList.Invoke(null, new object[] { listRow, 0 });
                configurePinned.Invoke(null, new object[] { pinnedRow });

                Assert.That(pinnedRow.rect.width,
                    Is.EqualTo(listRow.rect.width).Within(0.001f));
                Assert.That(pinnedRow.rect.height,
                    Is.EqualTo(listRow.rect.height).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }
    }
}
