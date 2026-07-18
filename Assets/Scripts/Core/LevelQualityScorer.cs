using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Immutable quality metrics for comparing generated candidates.</summary>
    public sealed class LevelQualityMetrics
    {
        public bool NeverStuck { get; }
        public int? MinMoves { get; }
        public int WallCount { get; }
        public int FloorCount { get; }
        public int VoidCount { get; }
        public int StopPositionCount { get; }
        public double AverageBranching { get; }
        public double IsolatedWallRatio { get; }
        public double SilhouettePerimeter { get; }
        public double FloorFraction { get; }
        public LevelLayoutMetrics Layout { get; }
        public bool PassesQualityFloors { get; }
        public double Score { get; }

        public LevelQualityMetrics(
            bool neverStuck,
            int? minMoves,
            int wallCount,
            int floorCount,
            int voidCount,
            int stopPositionCount,
            double averageBranching,
            double isolatedWallRatio,
            double silhouettePerimeter,
            double floorFraction,
            LevelLayoutMetrics layout,
            bool passesQualityFloors,
            double score)
        {
            NeverStuck = neverStuck;
            MinMoves = minMoves;
            WallCount = wallCount;
            FloorCount = floorCount;
            VoidCount = voidCount;
            StopPositionCount = stopPositionCount;
            AverageBranching = averageBranching;
            IsolatedWallRatio = isolatedWallRatio;
            SilhouettePerimeter = silhouettePerimeter;
            FloorFraction = floorFraction;
            Layout = layout;
            PassesQualityFloors = passesQualityFloors;
            Score = score;
        }
    }

    /// <summary>Scores generated boards using solver-backed movement analysis.</summary>
    public sealed class LevelQualityScorer
    {
        public const double MinFloorFraction = 0.45;

        private readonly Solver _solver;
        private readonly MovementSystem _movement;
        private readonly LevelLayoutAnalyzer _layoutAnalyzer;

        public LevelQualityScorer(Solver solver = null, MovementSystem movement = null)
        {
            _movement = movement ?? new MovementSystem();
            _solver = solver ?? new Solver(_movement);
            _layoutAnalyzer = new LevelLayoutAnalyzer(_movement);
        }

        public LevelQualityMetrics Score(Level level, Difficulty difficulty, bool evaluateMinMoves = false,
            int minMovesMaxStates = 200000)
        {
            CountTiles(level, out int walls, out int floors, out int voids);
            bool neverStuck = _solver.IsAlwaysSolvable(level);
            int? minMoves = evaluateMinMoves ? _solver.SolveMinMoves(level, minMovesMaxStates) : null;

            var stopGraph = BuildStopGraph(level);
            int stopCount = stopGraph.Count;
            double avgBranching = AverageBranching(stopGraph);
            double isolatedRatio = IsolatedWallRatio(level.Grid, level.Rows, level.Cols);
            double perimeter = SilhouettePerimeter(level.Grid, level.Rows, level.Cols);
            LevelLayoutMetrics layout = _layoutAnalyzer.Analyze(level);
            int cellCount = level.Rows * level.Cols;
            double floorFraction = cellCount > 0 ? (double)floors / cellCount : 0;
            bool passesOpenLayout = difficulty < Difficulty.ExtraHard ||
                LevelLayoutAnalyzer.MeetsExtraHardFloor(level, layout, out _);
            bool passesQualityFloors = neverStuck
                && floorFraction >= MinFloorFraction
                && (difficulty >= Difficulty.ExtraHard ||
                    !LooksLikeSerpentine(level.Grid, level.Rows, level.Cols, 0.65))
                && passesOpenLayout;
            double score = passesQualityFloors
                ? ComputeScore(difficulty, minMoves, walls, floors, voids, stopCount, avgBranching,
                    isolatedRatio, perimeter, layout)
                : double.NegativeInfinity;

            return new LevelQualityMetrics(
                neverStuck,
                minMoves,
                walls,
                floors,
                voids,
                stopCount,
                avgBranching,
                isolatedRatio,
                perimeter,
                floorFraction,
                layout,
                passesQualityFloors,
                score);
        }

        internal static bool LooksLikeSerpentine(Tile[,] grid, int rows, int cols)
            => LooksLikeSerpentine(grid, rows, cols, 0.65);

        private static bool LooksLikeSerpentine(
            Tile[,] grid, int rows, int cols, double matchingThreshold)
        {
            int matchingRows = 0;
            int structuredRows = 0;
            for (int r = 0; r < rows; r++)
            {
                int active = 0;
                int floorCount = 0;
                int wallCount = 0;
                for (int c = 0; c < cols; c++)
                {
                    Tile tile = grid[r, c];
                    if (tile == Tile.Void) continue;
                    active++;
                    if (tile == Tile.Floor) floorCount++;
                    else wallCount++;
                }

                if (active < 2) continue;
                structuredRows++;

                bool corridorRow = floorCount >= (int)Math.Ceiling(active * 0.88);
                bool snakeRow = floorCount == 1 && wallCount == active - 1;
                if (corridorRow || snakeRow)
                    matchingRows++;
            }

            if (structuredRows < 3) return false;
            return (double)matchingRows / structuredRows >= matchingThreshold;
        }

        private static void CountTiles(Level level, out int walls, out int floors, out int voids)
        {
            walls = 0;
            floors = 0;
            voids = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    switch (level.Grid[r, c])
                    {
                        case Tile.Wall: walls++; break;
                        case Tile.Floor: floors++; break;
                        case Tile.Void: voids++; break;
                    }
                }
            }
        }

        private Dictionary<Position, List<Position>> BuildStopGraph(Level level)
        {
            var forward = new Dictionary<Position, List<Position>>();
            var visited = new HashSet<Position> { level.Spawn };
            var queue = new Queue<Position>();
            queue.Enqueue(level.Spawn);

            while (queue.Count > 0)
            {
                Position cur = queue.Dequeue();
                foreach (SwipeDirection dir in SwipeDirectionExtensions.All)
                {
                    var slide = _movement.Slide(level, cur, dir);
                    if (slide.stop == cur) continue;

                    if (!forward.TryGetValue(cur, out List<Position> edges))
                    {
                        edges = new List<Position>();
                        forward[cur] = edges;
                    }

                    edges.Add(slide.stop);
                    if (visited.Add(slide.stop))
                        queue.Enqueue(slide.stop);
                }
            }

            return forward;
        }

        private static double AverageBranching(Dictionary<Position, List<Position>> stopGraph)
        {
            if (stopGraph.Count == 0) return 0;

            int total = 0;
            foreach (var kv in stopGraph)
                total += kv.Value.Count;

            return (double)total / stopGraph.Count;
        }

        private static double IsolatedWallRatio(Tile[,] grid, int rows, int cols)
        {
            int walls = 0;
            int isolated = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] != Tile.Wall) continue;
                    walls++;
                    bool joined =
                        (r > 0 && grid[r - 1, c] == Tile.Wall) ||
                        (r + 1 < rows && grid[r + 1, c] == Tile.Wall) ||
                        (c > 0 && grid[r, c - 1] == Tile.Wall) ||
                        (c + 1 < cols && grid[r, c + 1] == Tile.Wall);
                    if (!joined) isolated++;
                }
            }

            if (walls == 0) return 0;
            return (double)isolated / walls;
        }

        private static double SilhouettePerimeter(Tile[,] grid, int rows, int cols)
        {
            double perimeter = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    Tile tile = grid[r, c];
                    if (tile == Tile.Void) continue;

                    if (r == 0 || grid[r - 1, c] == Tile.Void) perimeter++;
                    if (r + 1 == rows || grid[r + 1, c] == Tile.Void) perimeter++;
                    if (c == 0 || grid[r, c - 1] == Tile.Void) perimeter++;
                    if (c + 1 == cols || grid[r, c + 1] == Tile.Void) perimeter++;
                }
            }

            return perimeter;
        }

        private static double ComputeScore(
            Difficulty difficulty,
            int? minMoves,
            int walls,
            int floors,
            int voids,
            int stopCount,
            double avgBranching,
            double isolatedRatio,
            double perimeter,
            LevelLayoutMetrics layout)
        {
            double wallWeight = difficulty switch
            {
                Difficulty.Easy => 1.0,
                Difficulty.Medium => 1.2,
                Difficulty.Hard => 1.4,
                Difficulty.ExtraHard => 1.6,
                Difficulty.UltraHard => 1.8,
                _ => 1.0
            };

            double moveWeight = difficulty switch
            {
                Difficulty.Easy => 0.8,
                Difficulty.Medium => 1.0,
                Difficulty.Hard => 1.2,
                Difficulty.ExtraHard => 1.4,
                Difficulty.UltraHard => 1.6,
                _ => 1.0
            };

            double score = walls * wallWeight;
            score += floors * 0.15;
            score += voids * 0.35;
            score += stopCount * 0.5;
            score += avgBranching * 2.0;
            score += perimeter * 0.25;
            score -= isolatedRatio * 12.0;
            if (difficulty >= Difficulty.ExtraHard)
            {
                score += layout.OpenFloorCellCount * 0.2;
                score += layout.FloorJunctionCount;
                score += layout.FloorGraphCycleRank * 0.5;
                score += layout.ChoiceStopCount * 1.5;
                score -= layout.FloorDegreeTwoRatio * 8.0;
            }

            if (minMoves.HasValue)
                score += minMoves.Value * moveWeight;

            return score;
        }
    }
}
