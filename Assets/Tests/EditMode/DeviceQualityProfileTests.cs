using NUnit.Framework;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public class DeviceQualityProfileTests
    {
        [TestCase(3072, 8)]
        [TestCase(4096, 8)]
        [TestCase(4608, 4)]
        public void Resolve_FourGbClassHardware_ReturnsLow(int memoryMb, int cores)
        {
            Assert.AreEqual(DeviceQualityTier.Low,
                DeviceQualityProfile.Resolve(memoryMb, cores));
        }

        [TestCase(0, 0)]
        [TestCase(5120, 8)]
        [TestCase(8192, 4)]
        public void Resolve_AmbiguousHardware_ReturnsStandard(int memoryMb, int cores)
        {
            Assert.AreEqual(DeviceQualityTier.Standard,
                DeviceQualityProfile.Resolve(memoryMb, cores));
        }

        [Test]
        public void Resolve_HighMemoryEightCoreHardware_ReturnsHigh()
        {
            Assert.AreEqual(DeviceQualityTier.High,
                DeviceQualityProfile.Resolve(8192, 8));
        }
    }
}
