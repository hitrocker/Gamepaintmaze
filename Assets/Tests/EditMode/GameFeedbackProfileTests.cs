using NUnit.Framework;
using PaintMaze.Game;
using PaintMaze.Services;

namespace PaintMaze.Tests
{
    public class GameFeedbackProfileTests
    {
        [Test]
        public void NewPaint_HasMoreSplashFeedbackThanRevisit()
        {
            PaintFeedback fresh =
                GameFeedbackProfile.Paint(1, true, DeviceQualityTier.High);
            PaintFeedback revisit =
                GameFeedbackProfile.Paint(1, false, DeviceQualityTier.High);

            Assert.Greater(fresh.SplashCount, revisit.SplashCount);
        }

        [Test]
        public void RevisitHaptic_IsCadencedInsteadOfPlayingEveryCell()
        {
            Assert.AreEqual(FeedbackHaptic.Light, GameFeedbackProfile.Paint(
                0, false, DeviceQualityTier.High).Haptic);
            Assert.AreEqual(FeedbackHaptic.None, GameFeedbackProfile.Paint(
                1, false, DeviceQualityTier.High).Haptic);
            Assert.AreEqual(FeedbackHaptic.Light, GameFeedbackProfile.Paint(
                2, false, DeviceQualityTier.High).Haptic);
            Assert.AreEqual(FeedbackHaptic.None, GameFeedbackProfile.Paint(
                3, false, DeviceQualityTier.High).Haptic);
        }

        [Test]
        public void LowTier_ReducesPaintAndImpactParticles()
        {
            PaintFeedback lowPaint =
                GameFeedbackProfile.Paint(0, true, DeviceQualityTier.Low);
            PaintFeedback highPaint =
                GameFeedbackProfile.Paint(0, true, DeviceQualityTier.High);
            ImpactFeedback lowImpact =
                GameFeedbackProfile.Impact(10, DeviceQualityTier.Low);
            ImpactFeedback highImpact =
                GameFeedbackProfile.Impact(10, DeviceQualityTier.High);

            Assert.Less(lowPaint.SplashCount, highPaint.SplashCount);
            Assert.Less(lowImpact.SparkCount, highImpact.SparkCount);
        }

        [Test]
        public void LongImpact_IsDeeperAndStrongerButClamped()
        {
            ImpactFeedback shortImpact =
                GameFeedbackProfile.Impact(1, DeviceQualityTier.High);
            ImpactFeedback longImpact =
                GameFeedbackProfile.Impact(20, DeviceQualityTier.High);

            Assert.Greater(longImpact.Volume, shortImpact.Volume);
            Assert.Less(longImpact.Pitch, shortImpact.Pitch);
            Assert.Greater(longImpact.Shake, shortImpact.Shake);
            Assert.That(longImpact.Shake, Is.LessThanOrEqualTo(0.44f));
            Assert.AreEqual(FeedbackHaptic.Medium, longImpact.Haptic);
        }

        [TestCase(DeviceQualityTier.Low, 160, 96, false)]
        [TestCase(DeviceQualityTier.Standard, 600, 256, true)]
        [TestCase(DeviceQualityTier.High, 600, 256, true)]
        public void MotionVfxProfile_HasStableTierCaps(
            DeviceQualityTier tier, int sparks, int splashes, bool eager)
        {
            MotionVfxProfile profile = MotionVfxProfile.For(tier);
            Assert.AreEqual(sparks, profile.SparkMaxParticles);
            Assert.AreEqual(splashes, profile.SplashMaxParticles);
            Assert.AreEqual(eager, profile.EagerBuild);
        }

        [Test]
        public void CompletionAndRevealTimings_AreShortAndPositive()
        {
            Assert.That(GameFeedbackProfile.CompletionPulseDuration,
                Is.InRange(0.2f, 0.5f));
            Assert.That(GameFeedbackProfile.CompletionPulseSpread,
                Is.GreaterThan(0f));
            Assert.That(GameFeedbackProfile.CompletionOverlayDelay,
                Is.GreaterThanOrEqualTo(0f));
            Assert.That(GameFeedbackProfile.RevealDuration,
                Is.InRange(0.1f, 0.35f));
        }
    }
}
