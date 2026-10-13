using NUnit.Framework;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public class DeviceTestSupportTests
    {
        [TestCase(null, 0, 50000)]
        [TestCase("", 0, 50000)]
        [TestCase("  ", 0, 50000)]
        [TestCase("0", 0, 50000)]
        [TestCase("junk", 0, 50000)]
        [TestCase("-12", 0, 50000)]
        [TestCase("75000", 0, 75000)]
        [TestCase(" 75000 ", 100, 75000)]
        [TestCase("", 80000, 80000)]
        [TestCase("50000", 80000, 80000)]
        public void ResolveDebugCoinGrant_UsesFloorUnlessOverrideOrHigherBalance(
            string rawProperty,
            int currentCoins,
            int expected)
        {
            Assert.AreEqual(
                expected,
                DeviceTestSupport.ResolveDebugCoinGrant(rawProperty, currentCoins));
        }

        [Test]
        public void ResolveDebugCoinGrant_ClampsHugeOverrideToMaxCoins()
        {
            Assert.AreEqual(
                SaveService.MaxCoins,
                DeviceTestSupport.ResolveDebugCoinGrant("2147483647", 0));
        }
    }
}
