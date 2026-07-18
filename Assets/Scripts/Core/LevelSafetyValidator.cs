using System;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Minimum runtime acceptance rules. Unlike LevelBakeValidator, these rules never
    /// relax for emergency fallbacks: a published level must be structurally valid and
    /// must preserve the game's never-stuck guarantee.
    /// </summary>
    public static class LevelSafetyValidator
    {
        public static bool IsSafe(Level level, Solver solver, out string failureReason)
        {
            if (level == null)
            {
                failureReason = "level is null";
                return false;
            }

            if (level.Rows <= 0 || level.Cols <= 0)
            {
                failureReason = "grid is empty";
                return false;
            }

            if (!level.IsFloor(level.Spawn))
            {
                failureReason = $"spawn {level.Spawn} is not a floor cell";
                return false;
            }

            if (level.TotalPaintable <= 0)
            {
                failureReason = "level has no paintable cells";
                return false;
            }

            if (!LevelTopology.HasOnlyExteriorVoid(level))
            {
                failureReason = "contains enclosed void/background";
                return false;
            }

            if (solver == null)
            {
                failureReason = "solver is null";
                return false;
            }

            NeverStuckAnalysis analysis = solver.AnalyzeNeverStuck(level);
            if (!analysis.Passes)
            {
                failureReason = DescribeNeverStuckFailure(analysis);
                return false;
            }

            failureReason = null;
            return true;
        }

        public static string DescribeNeverStuckFailure(NeverStuckAnalysis analysis)
        {
            if (analysis == null) return "not always solvable";
            if (analysis.FailureKind == NeverStuckFailureKind.UncoveredFloor)
            {
                string first = analysis.UncoveredFloors.Count > 0
                    ? $", first={analysis.UncoveredFloors[0]}"
                    : "";
                return "not always solvable: uncovered floor " +
                       $"({analysis.CoveredFloorCount}/{analysis.TotalFloorCount}{first})";
            }
            if (analysis.FailureKind == NeverStuckFailureKind.NotStronglyConnected)
            {
                string first = analysis.StrandedStops.Count > 0
                    ? $", first={analysis.StrandedStops[0]}"
                    : "";
                return "not always solvable: stop graph not strongly connected " +
                       $"({analysis.StrandedStops.Count} stranded{first})";
            }
            return "not always solvable";
        }

        public static Level RequireSafe(Level level, Solver solver, string context)
        {
            if (IsSafe(level, solver, out string failureReason))
                return level;

            throw new InvalidOperationException(
                $"{context ?? "Level"} failed runtime safety validation: {failureReason}");
        }
    }
}
