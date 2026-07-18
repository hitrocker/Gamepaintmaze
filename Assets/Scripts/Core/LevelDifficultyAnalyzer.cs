using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    public sealed class LevelDifficultyAnalysisOptions
    {
        public int MaxExactStates { get; set; } = 25000;
        public int ExactFloorLimit { get; set; } = 100;
        public int RandomRolloutCount { get; set; } = 16;
        public int RolloutMaxMoves { get; set; } = 192;

        public static LevelDifficultyAnalysisOptions Runtime => new LevelDifficultyAnalysisOptions
        {
            MaxExactStates = 12000,
            ExactFloorLimit = 90,
            RandomRolloutCount = 8,
            RolloutMaxMoves = 160
        };

        public static LevelDifficultyAnalysisOptions Bake => new LevelDifficultyAnalysisOptions
        {
            MaxExactStates = 30000,
            ExactFloorLimit = 100,
            RandomRolloutCount = 12,
            RolloutMaxMoves = 280
        };
    }

    public sealed class LevelDifficultyMetrics
    {
        public int? MinMoves { get; }
        public int RepresentativeMoves { get; }
        public int Turns { get; }
        public int Reversals { get; }
        public int RevisitedStops { get; }
        public double AveragePaintPerMove { get; }
        public double ForcedMoveRatio { get; }
        public int RandomSolutionBest { get; }
        public double RandomSolutionAverage { get; }
        public int RandomSolutionWorst { get; }
        public int RandomSolutionFailed { get; }
        public int RandomRolloutCount { get; }
        public double RandomSolutionSpread { get; }
        public double MeaningfulChoiceRatio { get; }
        public int FloorGraphCycleRank { get; }
        public double RandomFailureRate =>
            RandomRolloutCount > 0 ? (double)RandomSolutionFailed / RandomRolloutCount : 1.0;
        public double Score { get; }

        public LevelDifficultyMetrics(
            int? minMoves,
            int representativeMoves,
            int turns,
            int reversals,
            int revisitedStops,
            double averagePaintPerMove,
            double forcedMoveRatio,
            int randomSolutionBest,
            double randomSolutionAverage,
            int randomSolutionWorst,
            int randomSolutionFailed,
            int randomRolloutCount,
            double randomSolutionSpread,
            double meaningfulChoiceRatio,
            int floorGraphCycleRank,
            double score)
        {
            MinMoves = minMoves;
            RepresentativeMoves = representativeMoves;
            Turns = turns;
            Reversals = reversals;
            RevisitedStops = revisitedStops;
            AveragePaintPerMove = averagePaintPerMove;
            ForcedMoveRatio = forcedMoveRatio;
            RandomSolutionBest = randomSolutionBest;
            RandomSolutionAverage = randomSolutionAverage;
            RandomSolutionWorst = randomSolutionWorst;
            RandomSolutionFailed = randomSolutionFailed;
            RandomRolloutCount = randomRolloutCount;
            RandomSolutionSpread = randomSolutionSpread;
            MeaningfulChoiceRatio = meaningfulChoiceRatio;
            FloorGraphCycleRank = floorGraphCycleRank;
            Score = score;
        }
    }

    /// <summary>
    /// Deterministic, bounded difficulty analysis used to rank large Extra Hard boards.
    /// Exact search is reserved for small boards; seeded rollouts provide stable signals
    /// when the painted-state search space is too large.
    /// </summary>
    public sealed class LevelDifficultyAnalyzer
    {
        private readonly Solver _solver;
        private readonly MovementSystem _movement;
        private readonly LevelLayoutAnalyzer _layoutAnalyzer;

        public LevelDifficultyAnalyzer(Solver solver = null, MovementSystem movement = null)
        {
            _movement = movement ?? new MovementSystem();
            _solver = solver ?? new Solver(_movement);
            _layoutAnalyzer = new LevelLayoutAnalyzer(_movement);
        }

        public LevelDifficultyMetrics Analyze(
            Level level,
            int seed,
            LevelDifficultyAnalysisOptions options = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            options = options ?? LevelDifficultyAnalysisOptions.Runtime;

            List<SwipeDirection> exact = null;
            int? minMoves = null;
            if (level.TotalPaintable <= options.ExactFloorLimit && options.MaxExactStates > 0)
            {
                exact = _solver.SolutionFrom(
                    level,
                    level.Spawn,
                    new HashSet<Position> { level.Spawn },
                    options.MaxExactStates);
                minMoves = exact?.Count;
            }

            var rng = new DeterministicRng(seed);
            int completed = 0;
            int failed = 0;
            int best = int.MaxValue;
            int worst = 0;
            long completedMoves = 0;
            List<SwipeDirection> representative = exact;

            int rolloutCount = Math.Max(0, options.RandomRolloutCount);
            for (int i = 0; i < rolloutCount; i++)
            {
                List<SwipeDirection> moves = RunRollout(level, rng, options.RolloutMaxMoves);
                if (moves == null)
                {
                    failed++;
                    continue;
                }

                completed++;
                completedMoves += moves.Count;
                if (moves.Count < best)
                {
                    best = moves.Count;
                    if (representative == null) representative = moves;
                }
                if (moves.Count > worst) worst = moves.Count;
            }

            if (completed == 0)
            {
                best = 0;
                worst = 0;
            }

            AnalyzeMoves(level, representative,
                out int turns,
                out int reversals,
                out int revisits,
                out double paintPerMove);
            double forcedRatio = CalculateForcedMoveRatio(level);
            int representativeMoves = representative?.Count ?? 0;
            double randomAverage = completed > 0 ? (double)completedMoves / completed : 0;
            double rolloutSpread = completed > 1 ? worst - best : 0;
            LevelLayoutMetrics layout = _layoutAnalyzer.Analyze(level);

            double moveCap = Math.Max(12.0, Math.Sqrt(level.TotalPaintable) * 4.0);
            double score = Math.Min(representativeMoves, moveCap) * 0.25;
            score += turns * 0.50;
            score += reversals * 0.70;
            score += revisits * 0.40;
            score += Math.Min(30.0, rolloutSpread) * 0.25;
            score += layout.MeaningfulChoiceRatio * 20.0;
            score += Math.Min(20, layout.ChoiceStopCount) * 0.40;
            score += Math.Min(24, layout.FloorGraphCycleRank) * 0.55;
            score += (1.0 - forcedRatio) * 10.0;
            score -= forcedRatio * 8.0;
            if (rolloutCount > 0 && layout.ChoiceStopCount > 0)
                score += (double)failed / rolloutCount * 2.0;
            if (paintPerMove > 0)
                score += Math.Min(8.0, level.TotalPaintable / paintPerMove * 0.25);

            return new LevelDifficultyMetrics(
                minMoves,
                representativeMoves,
                turns,
                reversals,
                revisits,
                paintPerMove,
                forcedRatio,
                best,
                randomAverage,
                worst,
                failed,
                rolloutCount,
                rolloutSpread,
                layout.MeaningfulChoiceRatio,
                layout.FloorGraphCycleRank,
                score);
        }

        public static double TargetScore(Difficulty difficulty, int sourceIndex)
        {
            int index = Math.Max(1, sourceIndex);
            if (difficulty == Difficulty.ExtraHard)
                return 18.0 + Math.Min(18.0, (index - 1) / 28.0);
            if (difficulty == Difficulty.UltraHard)
                return 28.0 + Math.Min(24.0, (index - 1) / 24.0);
            return 0;
        }

        public static bool MeetsExtraHardFloor(
            Level level,
            Difficulty difficulty,
            int sourceIndex,
            LevelDifficultyMetrics metrics,
            out string failureReason)
        {
            if (difficulty < Difficulty.ExtraHard)
            {
                failureReason = null;
                return true;
            }

            int minimumStops = Math.Max(12, level.TotalPaintable / 6);
            if (metrics == null)
            {
                failureReason = "missing difficulty analysis";
                return false;
            }

            if (metrics.AveragePaintPerMove > 0 &&
                metrics.AveragePaintPerMove > Math.Max(12.0, level.TotalPaintable * 0.55))
            {
                failureReason = $"paint-per-move too high ({metrics.AveragePaintPerMove:F2})";
                return false;
            }

            double minimumScore = TargetScore(difficulty, sourceIndex) * 0.35;
            if (metrics.Score < minimumScore)
            {
                failureReason = $"difficulty score {metrics.Score:F2} below {minimumScore:F2}";
                return false;
            }

            // A resistant board can legitimately exceed every bounded search. For boards
            // solved by the analyzer, reject only clearly trivial solutions.
            if (metrics.MinMoves.HasValue &&
                metrics.MinMoves.Value < Math.Max(4, minimumStops / 4))
            {
                failureReason = $"minimum moves too low ({metrics.MinMoves.Value})";
                return false;
            }

            failureReason = null;
            return true;
        }

        private List<SwipeDirection> RunRollout(Level level, DeterministicRng rng, int maxMoves)
        {
            var painted = new HashSet<Position> { level.Spawn };
            var moves = new List<SwipeDirection>();
            Position current = level.Spawn;
            SwipeDirection? previous = null;

            for (int step = 0; step < Math.Max(1, maxMoves); step++)
            {
                if (painted.Count >= level.TotalPaintable)
                    return moves;

                var legal = new List<(SwipeDirection dir, Position stop, List<Position> path, int gain)>();
                foreach (SwipeDirection direction in SwipeDirectionExtensions.All)
                {
                    var slide = _movement.Slide(level, current, direction);
                    if (slide.stop == current) continue;
                    int gain = 0;
                    foreach (Position position in slide.path)
                        if (!painted.Contains(position)) gain++;
                    legal.Add((direction, slide.stop, slide.path, gain));
                }

                if (legal.Count == 0) return null;

                int bestGain = 0;
                foreach (var candidate in legal)
                    if (candidate.gain > bestGain) bestGain = candidate.gain;

                var preferred = new List<int>();
                for (int i = 0; i < legal.Count; i++)
                {
                    bool avoidsImmediateReverse = !previous.HasValue ||
                        !IsReverse(previous.Value, legal[i].dir);
                    if (legal[i].gain == bestGain && (avoidsImmediateReverse || preferred.Count == 0))
                        preferred.Add(i);
                }

                int chosenIndex;
                if (bestGain > 0 && rng.NextDouble() < 0.72)
                    chosenIndex = preferred[rng.Next(preferred.Count)];
                else
                    chosenIndex = rng.Next(legal.Count);

                var chosen = legal[chosenIndex];
                foreach (Position position in chosen.path) painted.Add(position);
                moves.Add(chosen.dir);
                current = chosen.stop;
                previous = chosen.dir;
            }

            return painted.Count >= level.TotalPaintable ? moves : null;
        }

        private void AnalyzeMoves(
            Level level,
            List<SwipeDirection> moves,
            out int turns,
            out int reversals,
            out int revisits,
            out double averagePaintPerMove)
        {
            turns = 0;
            reversals = 0;
            revisits = 0;
            averagePaintPerMove = 0;
            if (moves == null || moves.Count == 0) return;

            Position current = level.Spawn;
            var painted = new HashSet<Position> { current };
            var stops = new HashSet<Position> { current };
            int newlyPainted = 0;
            SwipeDirection? previous = null;

            foreach (SwipeDirection direction in moves)
            {
                if (previous.HasValue && previous.Value != direction)
                {
                    turns++;
                    if (IsReverse(previous.Value, direction)) reversals++;
                }

                var slide = _movement.Slide(level, current, direction);
                foreach (Position position in slide.path)
                    if (painted.Add(position)) newlyPainted++;
                current = slide.stop;
                if (!stops.Add(current)) revisits++;
                previous = direction;
            }

            averagePaintPerMove = (double)newlyPainted / moves.Count;
        }

        private double CalculateForcedMoveRatio(Level level)
        {
            var visited = new HashSet<Position> { level.Spawn };
            var queue = new Queue<Position>();
            queue.Enqueue(level.Spawn);
            int stops = 0;
            int forced = 0;

            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                int legal = 0;
                foreach (SwipeDirection direction in SwipeDirectionExtensions.All)
                {
                    var slide = _movement.Slide(level, current, direction);
                    if (slide.stop == current) continue;
                    legal++;
                    if (visited.Add(slide.stop)) queue.Enqueue(slide.stop);
                }

                stops++;
                // With two legal directions, one is normally the immediate reverse,
                // leaving only one meaningful forward choice.
                if (legal <= 2) forced++;
            }

            return stops > 0 ? (double)forced / stops : 1.0;
        }

        private static bool IsReverse(SwipeDirection a, SwipeDirection b) =>
            a.DRow() == -b.DRow() && a.DCol() == -b.DCol();
    }
}
