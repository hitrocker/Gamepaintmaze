using UnityEngine;
using PaintMaze.Services;

namespace PaintMaze.Game
{
    public enum FeedbackHaptic
    {
        None,
        Light,
        Medium,
        Heavy
    }

    public readonly struct PaintFeedback
    {
        public readonly FeedbackHaptic Haptic;
        public readonly int SplashCount;

        public PaintFeedback(FeedbackHaptic haptic, int splashCount)
        {
            Haptic = haptic;
            SplashCount = splashCount;
        }
    }

    public readonly struct ImpactFeedback
    {
        public readonly float Volume;
        public readonly float Pitch;
        public readonly float Shake;
        public readonly int SparkCount;
        public readonly FeedbackHaptic Haptic;

        public ImpactFeedback(float volume, float pitch, float shake,
            int sparkCount, FeedbackHaptic haptic)
        {
            Volume = volume;
            Pitch = pitch;
            Shake = shake;
            SparkCount = sparkCount;
            Haptic = haptic;
        }
    }

    public readonly struct MotionVfxProfile
    {
        public readonly int SparkMaxParticles;
        public readonly int SplashMaxParticles;
        public readonly bool EagerBuild;
        public readonly int CompletionSparkCount;

        public MotionVfxProfile(int sparkMaxParticles, int splashMaxParticles,
            bool eagerBuild, int completionSparkCount)
        {
            SparkMaxParticles = sparkMaxParticles;
            SplashMaxParticles = splashMaxParticles;
            EagerBuild = eagerBuild;
            CompletionSparkCount = completionSparkCount;
        }

        public static MotionVfxProfile For(DeviceQualityTier tier)
        {
            return tier switch
            {
                DeviceQualityTier.Low => new MotionVfxProfile(160, 96, false, 18),
                DeviceQualityTier.Standard => new MotionVfxProfile(600, 256, true, 24),
                _ => new MotionVfxProfile(600, 256, true, 30)
            };
        }
    }

    /// <summary>
    /// Pure, deterministic feel tuning. Gameplay code consumes these values but never
    /// derives movement, progression, or state changes from them.
    /// </summary>
    public static class GameFeedbackProfile
    {
        public const float CompletionPulseDuration = 0.34f;
        public const float CompletionPulseSpread = 0.14f;
        public const float CompletionOverlayDelay = 0.06f;
        public const float RevealDuration = 0.22f;

        public static PaintFeedback Paint(
            int sequenceIndex, bool newlyPainted, DeviceQualityTier tier)
        {
            int sequence = Mathf.Max(0, sequenceIndex);
            FeedbackHaptic haptic = newlyPainted || sequence % 2 == 0
                ? FeedbackHaptic.Light
                : FeedbackHaptic.None;
            int splashCount = newlyPainted
                ? (tier == DeviceQualityTier.Low ? 6 : BoardView3D.NewTileSplashCount)
                : (tier == DeviceQualityTier.Low ? 2 : BoardView3D.RevisitedTileSplashCount);
            return new PaintFeedback(haptic, splashCount);
        }

        public static ImpactFeedback Impact(int travelledCells, DeviceQualityTier tier)
        {
            int cells = Mathf.Max(1, travelledCells);
            float strength = Mathf.InverseLerp(1f, 10f, cells);
            float qualityScale = tier switch
            {
                DeviceQualityTier.Low => 0.72f,
                DeviceQualityTier.Standard => 0.88f,
                _ => 1f
            };
            int sparks = Mathf.RoundToInt(Mathf.Lerp(12f, 26f, strength) * qualityScale);
            return new ImpactFeedback(
                Mathf.Lerp(0.42f, 0.64f, strength),
                Mathf.Lerp(0.99f, 0.88f, strength),
                Mathf.Lerp(0.16f, 0.44f, strength),
                Mathf.Max(8, sparks),
                cells >= 7 ? FeedbackHaptic.Medium : FeedbackHaptic.Light);
        }
    }
}
