using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Shared bake-time acceptance rules for offline and runtime tooling.</summary>
    public static class LevelBakeValidator
    {
        public const double MaxIsolatedWallRatio = 0.5;

        public static bool MeetsRequirements(Level level, Solver solver, LevelQualityMetrics metrics,
            out string failureReason)
        {
            return MeetsRequirements(level, solver, metrics, null, level?.Difficulty ?? Difficulty.Easy,
                level?.Index ?? 1, out failureReason);
        }

        public static bool MeetsRequirements(
            Level level,
            Solver solver,
            LevelQualityMetrics metrics,
            LevelDifficultyMetrics difficultyMetrics,
            Difficulty sourceDifficulty,
            int sourceIndex,
            out string failureReason)
        {
            if (level == null)
            {
                failureReason = "level is null";
                return false;
            }

            NeverStuckAnalysis safety = solver.AnalyzeNeverStuck(level);
            if (!safety.Passes)
            {
                failureReason = LevelSafetyValidator.DescribeNeverStuckFailure(safety);
                return false;
            }

            if (sourceDifficulty >= Difficulty.ExtraHard &&
                !LevelLayoutAnalyzer.MeetsExtraHardFloor(
                    level, metrics.Layout, out failureReason))
            {
                return false;
            }

            if (!metrics.PassesQualityFloors)
            {
                failureReason = $"quality floor failed (floorFraction={metrics.FloorFraction:F3})";
                return false;
            }

            if (metrics.VoidCount < 1)
            {
                failureReason = "no void tiles";
                return false;
            }

            if (!LevelTopology.HasOnlyExteriorVoid(level))
            {
                failureReason = "contains enclosed void/background";
                return false;
            }

            if (metrics.IsolatedWallRatio > MaxIsolatedWallRatio)
            {
                failureReason = $"isolated wall ratio {metrics.IsolatedWallRatio:F3}";
                return false;
            }

            if (sourceDifficulty >= Difficulty.ExtraHard &&
                difficultyMetrics != null &&
                !LevelDifficultyAnalyzer.MeetsExtraHardFloor(
                    level, sourceDifficulty, sourceIndex, difficultyMetrics, out failureReason))
            {
                return false;
            }

            failureReason = null;
            return true;
        }
    }
}
