using NUnit.Framework;
using PaintMaze.Game;
using UnityEngine;

namespace PaintMaze.Tests
{
    public class BallSlideMotionTests
    {
        private const float LongDistanceThreshold = 4f;
        private const float LongDistanceMultiplier = 2f;

        [TestCase(1f, 1f / 32f)]
        [TestCase(2f, 2f / 32f)]
        [TestCase(4f, 4f / 32f)]
        public void ShortSlideDuration_UsesBaseWorldSpeed(float worldDistance, float expected)
        {
            float duration = BallSlideMotion.DurationForDistance(
                worldDistance, LongDistanceThreshold, LongDistanceMultiplier);
            Assert.That(duration, Is.EqualTo(expected).Within(0.00001f));
        }

        [Test]
        public void LongDistanceBonus_IncreasesSmoothlyPastThreshold()
        {
            float atThreshold = BallSlideMotion.LongDistanceBonus(
                4f, LongDistanceThreshold, LongDistanceMultiplier);
            float justPast = BallSlideMotion.LongDistanceBonus(
                4.01f, LongDistanceThreshold, LongDistanceMultiplier);
            float farther = BallSlideMotion.LongDistanceBonus(
                6f, LongDistanceThreshold, LongDistanceMultiplier);

            Assert.That(atThreshold, Is.EqualTo(0f));
            Assert.That(justPast, Is.GreaterThan(0f).And.LessThan(0.001f));
            Assert.That(farther, Is.GreaterThan(justPast));
        }

        [Test]
        public void CompletedTravel_SamplesExactDestination()
        {
            BallSlideMotion motion = CreateMotion(4);

            BallSlideStep step = motion.Advance(motion.TravelDuration);

            Assert.IsTrue(step.Arrived);
            Assert.That(motion.TravelCells, Is.EqualTo(4f));
            BallSlideMotion.PathSample(
                motion.TravelCells, motion.CellCount, out int segmentIndex, out float segmentT);
            float sampledPosition = Mathf.Lerp(segmentIndex, segmentIndex + 1f, segmentT);
            Assert.That(sampledPosition, Is.EqualTo(4f));
        }

        [Test]
        public void SkippedFrames_StillEnterEveryCellExactlyOnce()
        {
            const int cells = 10;
            BallSlideMotion motion = CreateMotion(cells);
            var paintCounts = new int[cells];
            int painted = 0;

            while (motion.Phase == BallSlidePhase.Travel)
            {
                motion.Advance(1f / 15f);
                while (painted < motion.EnteredCellCount)
                    paintCounts[painted++]++;
            }

            Assert.That(painted, Is.EqualTo(cells));
            for (int i = 0; i < paintCounts.Length; i++)
                Assert.That(paintCounts[i], Is.EqualTo(1), $"Path cell {i} was not painted exactly once.");
        }

        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(120f)]
        public void SimulatedFrameRates_ProduceTheConfiguredTravelDuration(float fps)
        {
            BallSlideMotion motion = CreateMotion(7);
            float simulatedTime = AdvanceThroughTravel(ref motion, fps);

            float expected = BallSlideMotion.DurationForDistance(
                7f, LongDistanceThreshold, LongDistanceMultiplier);
            Assert.That(simulatedTime, Is.EqualTo(expected).Within(0.00001f));
            Assert.That(motion.TravelElapsed, Is.EqualTo(motion.TravelDuration).Within(0.00001f));
            Assert.That(motion.TravelCells, Is.EqualTo(7f));
        }

        [Test]
        public void AnalyticSettle_IsFrameRateIndependentAndNeverExceedsOvershootCap()
        {
            float duration30 = SimulateWholeMotion(10, 30f, out float overshoot30);
            float duration60 = SimulateWholeMotion(10, 60f, out float overshoot60);
            float duration120 = SimulateWholeMotion(10, 120f, out float overshoot120);

            Assert.That(duration30, Is.EqualTo(duration60).Within(0.00001f));
            Assert.That(duration60, Is.EqualTo(duration120).Within(0.00001f));
            Assert.That(overshoot30, Is.LessThanOrEqualTo(BallSlideMotion.MaximumOvershoot + 0.00001f));
            Assert.That(overshoot60, Is.LessThanOrEqualTo(BallSlideMotion.MaximumOvershoot + 0.00001f));
            Assert.That(overshoot120, Is.LessThanOrEqualTo(BallSlideMotion.MaximumOvershoot + 0.00001f));
        }

        [Test]
        public void LongSlides_AreFasterPerCellThanMediumSlides()
        {
            float fourCellTimePerCell = BallSlideMotion.DurationForDistance(
                4f, LongDistanceThreshold, LongDistanceMultiplier) / 4f;
            float tenCellTimePerCell = BallSlideMotion.DurationForDistance(
                10f, LongDistanceThreshold, LongDistanceMultiplier) / 10f;

            Assert.That(tenCellTimePerCell, Is.LessThan(fourCellTimePerCell));
        }

        [Test]
        public void InputRemainsLockedThroughTravelAndSettle()
        {
            BallSlideMotion motion = CreateMotion(4);
            Assert.IsTrue(motion.InputLocked);

            motion.Advance(motion.TravelDuration * 0.5f);
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Travel));
            Assert.IsTrue(motion.InputLocked);

            BallSlideStep arrival = motion.Advance(motion.TravelDuration);
            Assert.IsTrue(arrival.Arrived);
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Settle));
            Assert.IsTrue(motion.InputLocked);

            motion.Advance(motion.SettleDuration * 0.5f);
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Settle));
            Assert.IsTrue(motion.InputLocked);

            BallSlideStep settled = motion.Advance(motion.SettleDuration);
            Assert.IsTrue(settled.Settled);
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Complete));
            Assert.IsFalse(motion.InputLocked);
            Assert.That(motion.SettleOffsetCells, Is.EqualTo(0f));
            Assert.That(motion.Squash, Is.EqualTo(0f));
        }

        [Test]
        public void SkipSettle_IsRejectedDuringTravel()
        {
            BallSlideMotion motion = CreateMotion(4);

            Assert.IsFalse(motion.TrySkipSettle());
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Travel));
            Assert.IsTrue(motion.InputLocked);
        }

        [Test]
        public void SkipSettle_CompletesMotionAndClearsCosmeticOffsets()
        {
            BallSlideMotion motion = CreateMotion(4);
            motion.Advance(motion.TravelDuration);
            motion.Advance(motion.SettleDuration * 0.05f);

            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Settle));
            Assert.That(Mathf.Abs(motion.SettleOffsetCells), Is.GreaterThan(0f));
            Assert.IsTrue(motion.TrySkipSettle());
            Assert.That(motion.Phase, Is.EqualTo(BallSlidePhase.Complete));
            Assert.IsFalse(motion.InputLocked);
            Assert.That(motion.SettleElapsed, Is.EqualTo(motion.SettleDuration));
            Assert.That(motion.SettleOffsetCells, Is.EqualTo(0f));
            Assert.That(motion.Squash, Is.EqualTo(0f));
        }

        [Test]
        public void EnteredCells_ChangeAtCellBoundaries()
        {
            Assert.That(BallSlideMotion.EnteredCellsForDistance(0.49f, 4), Is.EqualTo(0));
            Assert.That(BallSlideMotion.EnteredCellsForDistance(0.50f, 4), Is.EqualTo(1));
            Assert.That(BallSlideMotion.EnteredCellsForDistance(1.49f, 4), Is.EqualTo(1));
            Assert.That(BallSlideMotion.EnteredCellsForDistance(1.50f, 4), Is.EqualTo(2));
            Assert.That(BallSlideMotion.EnteredCellsForDistance(4f, 4), Is.EqualTo(4));
        }

        private static float AdvanceThroughTravel(ref BallSlideMotion motion, float fps)
        {
            float elapsed = 0f;
            float frameTime = 1f / fps;
            while (motion.Phase == BallSlidePhase.Travel)
            {
                float step = Mathf.Min(frameTime, motion.TravelDuration - motion.TravelElapsed);
                elapsed += step;
                motion.Advance(step);
            }
            return elapsed;
        }

        private static float SimulateWholeMotion(int cells, float fps, out float maximumOvershoot)
        {
            BallSlideMotion motion = CreateMotion(cells);
            float elapsed = 0f;
            float frameTime = 1f / fps;
            maximumOvershoot = 0f;

            while (motion.InputLocked)
            {
                float phaseRemaining = motion.Phase == BallSlidePhase.Travel
                    ? motion.TravelDuration - motion.TravelElapsed
                    : motion.SettleDuration - motion.SettleElapsed;
                float step = Mathf.Min(frameTime, phaseRemaining);
                elapsed += step;
                motion.Advance(step);
                maximumOvershoot = Mathf.Max(maximumOvershoot, motion.SettleOffsetCells);
            }

            return elapsed;
        }

        private static BallSlideMotion CreateMotion(int cells)
        {
            float worldDistance = cells * BoardView3D.Size;
            return new BallSlideMotion(
                cells, worldDistance, LongDistanceThreshold, LongDistanceMultiplier);
        }
    }
}
