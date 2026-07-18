using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Structural signals that distinguish open slide puzzles from visually dense
    /// one-cell paths. These metrics intentionally ignore orientation, so rotating or
    /// backbite-randomizing a corridor cannot evade the Extra Hard layout contract.
    /// </summary>
    public sealed class LevelLayoutMetrics
    {
        public int OpenFloorQuadCount { get; }
        public int OpenFloorCellCount { get; }
        public double OpenFloorCellRatio { get; }
        public int FloorJunctionCount { get; }
        public double FloorDegreeTwoRatio { get; }
        public int FloorGraphCycleRank { get; }
        public int LongestStraightFloorRun { get; }
        public double LongLaneFloorRatio { get; }
        public int OpenFloorCoreCount { get; }
        public int OpenFloorCoreCellCount { get; }
        public double OpenFloorCoreCellRatio { get; }
        public int MaximumFloorClearance { get; }
        public double FloorDegreeFourRatio { get; }
        public double LargestOpenCoreComponentRatio { get; }
        public int InteriorWallIslandCount { get; }
        public double WallIslandCellRatio { get; }
        public double ParallelSeparatorWallRatio { get; }
        public int ExteriorCutDepth { get; }
        public int SilhouetteConcaveCornerCount { get; }
        public int ReachableStopCount { get; }
        public int ChoiceStopCount { get; }
        public double MeaningfulChoiceRatio { get; }
        public bool IsSingleCorridorLike { get; }

        public LevelLayoutMetrics(
            int openFloorQuadCount,
            int openFloorCellCount,
            double openFloorCellRatio,
            int floorJunctionCount,
            double floorDegreeTwoRatio,
            int floorGraphCycleRank,
            int longestStraightFloorRun,
            double longLaneFloorRatio,
            int openFloorCoreCount,
            int openFloorCoreCellCount,
            double openFloorCoreCellRatio,
            int maximumFloorClearance,
            double floorDegreeFourRatio,
            double largestOpenCoreComponentRatio,
            int interiorWallIslandCount,
            double wallIslandCellRatio,
            double parallelSeparatorWallRatio,
            int exteriorCutDepth,
            int silhouetteConcaveCornerCount,
            int reachableStopCount,
            int choiceStopCount,
            double meaningfulChoiceRatio,
            bool isSingleCorridorLike)
        {
            OpenFloorQuadCount = openFloorQuadCount;
            OpenFloorCellCount = openFloorCellCount;
            OpenFloorCellRatio = openFloorCellRatio;
            FloorJunctionCount = floorJunctionCount;
            FloorDegreeTwoRatio = floorDegreeTwoRatio;
            FloorGraphCycleRank = floorGraphCycleRank;
            LongestStraightFloorRun = longestStraightFloorRun;
            LongLaneFloorRatio = longLaneFloorRatio;
            OpenFloorCoreCount = openFloorCoreCount;
            OpenFloorCoreCellCount = openFloorCoreCellCount;
            OpenFloorCoreCellRatio = openFloorCoreCellRatio;
            MaximumFloorClearance = maximumFloorClearance;
            FloorDegreeFourRatio = floorDegreeFourRatio;
            LargestOpenCoreComponentRatio = largestOpenCoreComponentRatio;
            InteriorWallIslandCount = interiorWallIslandCount;
            WallIslandCellRatio = wallIslandCellRatio;
            ParallelSeparatorWallRatio = parallelSeparatorWallRatio;
            ExteriorCutDepth = exteriorCutDepth;
            SilhouetteConcaveCornerCount = silhouetteConcaveCornerCount;
            ReachableStopCount = reachableStopCount;
            ChoiceStopCount = choiceStopCount;
            MeaningfulChoiceRatio = meaningfulChoiceRatio;
            IsSingleCorridorLike = isSingleCorridorLike;
        }
    }

    public sealed class LevelLayoutAnalyzer
    {
        private readonly MovementSystem _movement;

        public LevelLayoutAnalyzer(MovementSystem movement = null)
        {
            _movement = movement ?? new MovementSystem();
        }

        public LevelLayoutMetrics Analyze(Level level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            var openCells = new HashSet<Position>();
            int quads = 0;
            for (int r = 0; r + 1 < level.Rows; r++)
            {
                for (int c = 0; c + 1 < level.Cols; c++)
                {
                    if (!IsFloor(level, r, c) ||
                        !IsFloor(level, r + 1, c) ||
                        !IsFloor(level, r, c + 1) ||
                        !IsFloor(level, r + 1, c + 1))
                        continue;

                    quads++;
                    openCells.Add(new Position(r, c));
                    openCells.Add(new Position(r + 1, c));
                    openCells.Add(new Position(r, c + 1));
                    openCells.Add(new Position(r + 1, c + 1));
                }
            }

            int floors = 0;
            int degreeTwo = 0;
            int degreeFour = 0;
            int junctions = 0;
            int edges = 0;
            int components = 0;
            var visitedFloors = new HashSet<Position>();
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (!IsFloor(level, r, c)) continue;
                    floors++;
                    int degree = FloorDegree(level, r, c);
                    if (degree == 2) degreeTwo++;
                    if (degree == 4) degreeFour++;
                    if (degree >= 3) junctions++;
                    if (IsFloor(level, r + 1, c)) edges++;
                    if (IsFloor(level, r, c + 1)) edges++;

                    var position = new Position(r, c);
                    if (visitedFloors.Contains(position)) continue;
                    components++;
                    VisitFloorComponent(level, position, visitedFloors);
                }
            }

            AnalyzeStopChoices(level, out int stops, out int choiceStops);
            int cycleRank = Math.Max(0, edges - floors + components);
            AnalyzeLongLanes(
                level, out int longestStraightRun, out int longLaneFloorCount);
            AnalyzeOpenFloorCore(
                level,
                out int openCoreCount,
                out HashSet<Position> openCoreCells,
                out int maximumFloorClearance,
                out int largestOpenCoreComponent);
            AnalyzeWallMorphology(
                level,
                out int wallCount,
                out int interiorWallIslandCount,
                out int interiorWallCellCount,
                out int parallelSeparatorWallCount);
            AnalyzeSilhouette(
                level,
                out int exteriorCutDepth,
                out int silhouetteConcaveCorners);
            double openRatio = floors > 0 ? (double)openCells.Count / floors : 0;
            double degreeTwoRatio = floors > 0 ? (double)degreeTwo / floors : 1;
            double degreeFourRatio = floors > 0 ? (double)degreeFour / floors : 0;
            double longLaneRatio = floors > 0
                ? (double)longLaneFloorCount / floors
                : 1;
            double openCoreCellRatio = floors > 0
                ? (double)openCoreCells.Count / floors
                : 0;
            double largestOpenCoreRatio = floors > 0
                ? (double)largestOpenCoreComponent / floors
                : 0;
            double wallIslandRatio = wallCount > 0
                ? (double)interiorWallCellCount / wallCount
                : 0;
            double parallelSeparatorRatio = wallCount > 0
                ? (double)parallelSeparatorWallCount / wallCount
                : 0;
            double choiceRatio = stops > 0 ? (double)choiceStops / stops : 0;
            bool singleCorridorLike =
                quads == 0 && junctions == 0 && cycleRank == 0;

            return new LevelLayoutMetrics(
                quads,
                openCells.Count,
                openRatio,
                junctions,
                degreeTwoRatio,
                cycleRank,
                longestStraightRun,
                longLaneRatio,
                openCoreCount,
                openCoreCells.Count,
                openCoreCellRatio,
                maximumFloorClearance,
                degreeFourRatio,
                largestOpenCoreRatio,
                interiorWallIslandCount,
                wallIslandRatio,
                parallelSeparatorRatio,
                exteriorCutDepth,
                silhouetteConcaveCorners,
                stops,
                choiceStops,
                choiceRatio,
                singleCorridorLike);
        }

        public static bool MeetsExtraHardFloor(
            Level level,
            LevelLayoutMetrics metrics,
            out string failureReason)
        {
            if (level == null || metrics == null)
            {
                failureReason = "missing open-layout analysis";
                return false;
            }

            bool large = level.Rows * level.Cols > 100;
            int minimumOpenCoreCount = large
                ? Math.Max(4, level.TotalPaintable / 50)
                : 1;
            const double minimumOpenCoreCellRatio = 0.18;
            const double minimumDegreeFourRatio = 0.07;
            const double minimumLargestOpenCoreRatio = 0.12;
            const double minimumWallIslandRatio = 0.35;
            const double maximumParallelSeparatorRatio = 0.90;

            if (metrics.IsSingleCorridorLike)
            {
                failureReason = "generalized one-cell corridor";
                return false;
            }
            if (metrics.OpenFloorCoreCount < minimumOpenCoreCount)
            {
                failureReason =
                    $"3x3 open cores {metrics.OpenFloorCoreCount} below " +
                    $"{minimumOpenCoreCount}";
                return false;
            }
            if (metrics.OpenFloorCoreCellRatio < minimumOpenCoreCellRatio)
            {
                failureReason =
                    $"open-core floor ratio {metrics.OpenFloorCoreCellRatio:F3} below " +
                    $"{minimumOpenCoreCellRatio:F3}";
                return false;
            }
            if (metrics.MaximumFloorClearance < 1)
            {
                failureReason = "floor clearance never reaches a 3x3 room";
                return false;
            }
            if (metrics.FloorDegreeFourRatio < minimumDegreeFourRatio)
            {
                failureReason =
                    $"degree-four floor ratio {metrics.FloorDegreeFourRatio:F3} below " +
                    $"{minimumDegreeFourRatio:F3}";
                return false;
            }
            if (metrics.LargestOpenCoreComponentRatio < minimumLargestOpenCoreRatio)
            {
                failureReason =
                    $"largest open-core region {metrics.LargestOpenCoreComponentRatio:F3} below " +
                    $"{minimumLargestOpenCoreRatio:F3}";
                return false;
            }
            if (metrics.InteriorWallIslandCount < 1)
            {
                failureReason = "no compact interior wall island";
                return false;
            }
            if (metrics.WallIslandCellRatio < minimumWallIslandRatio)
            {
                failureReason =
                    $"wall-island ratio {metrics.WallIslandCellRatio:F3} below " +
                    $"{minimumWallIslandRatio:F3}";
                return false;
            }
            if (metrics.ParallelSeparatorWallRatio > maximumParallelSeparatorRatio)
            {
                failureReason =
                    $"parallel-separator wall ratio " +
                    $"{metrics.ParallelSeparatorWallRatio:F3} above " +
                    $"{maximumParallelSeparatorRatio:F3}";
                return false;
            }
            if (large && metrics.ChoiceStopCount < 1)
            {
                failureReason = "no stop with multiple non-reverse choices";
                return false;
            }

            failureReason = null;
            return true;
        }

        private static void AnalyzeOpenFloorCore(
            Level level,
            out int coreCount,
            out HashSet<Position> coreCells,
            out int maximumClearance,
            out int largestCoreComponent)
        {
            coreCount = 0;
            var cells = new HashSet<Position>();
            for (int r = 0; r + 2 < level.Rows; r++)
            {
                for (int c = 0; c + 2 < level.Cols; c++)
                {
                    bool open = true;
                    for (int dr = 0; dr < 3 && open; dr++)
                        for (int dc = 0; dc < 3; dc++)
                            if (!IsFloor(level, r + dr, c + dc))
                            {
                                open = false;
                                break;
                            }
                    if (!open) continue;
                    coreCount++;
                    for (int dr = 0; dr < 3; dr++)
                        for (int dc = 0; dc < 3; dc++)
                            cells.Add(new Position(r + dr, c + dc));
                }
            }

            maximumClearance = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (!IsFloor(level, r, c)) continue;
                    int clearance = 0;
                    while (IsOpenSquare(level, r, c, clearance + 1))
                        clearance++;
                    if (clearance > maximumClearance)
                        maximumClearance = clearance;
                }
            }

            largestCoreComponent = 0;
            var visited = new HashSet<Position>();
            foreach (Position start in cells)
            {
                if (!visited.Add(start)) continue;
                int count = 0;
                var queue = new Queue<Position>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Position current = queue.Dequeue();
                    count++;
                    Visit(current.Row - 1, current.Col);
                    Visit(current.Row + 1, current.Col);
                    Visit(current.Row, current.Col - 1);
                    Visit(current.Row, current.Col + 1);
                }
                if (count > largestCoreComponent)
                    largestCoreComponent = count;

                void Visit(int row, int col)
                {
                    var next = new Position(row, col);
                    if (cells.Contains(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            coreCells = cells;
        }

        private static bool IsOpenSquare(
            Level level,
            int centerRow,
            int centerCol,
            int radius)
        {
            for (int r = centerRow - radius; r <= centerRow + radius; r++)
                for (int c = centerCol - radius; c <= centerCol + radius; c++)
                    if (!IsFloor(level, r, c))
                        return false;
            return true;
        }

        private static void AnalyzeWallMorphology(
            Level level,
            out int wallCount,
            out int interiorIslandCount,
            out int interiorIslandCellCount,
            out int parallelSeparatorWallCount)
        {
            wallCount = 0;
            interiorIslandCount = 0;
            interiorIslandCellCount = 0;
            var walls = new HashSet<Position>();
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] != Tile.Wall) continue;
                    walls.Add(new Position(r, c));
                    wallCount++;
                }
            }

            var visited = new HashSet<Position>();
            int separatorCellCount = 0;
            foreach (Position start in walls)
            {
                if (!visited.Add(start)) continue;
                int count = 0;
                bool touchesBoundary = false;
                int minRow = start.Row;
                int maxRow = start.Row;
                int minCol = start.Col;
                int maxCol = start.Col;
                var queue = new Queue<Position>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Position current = queue.Dequeue();
                    count++;
                    minRow = Math.Min(minRow, current.Row);
                    maxRow = Math.Max(maxRow, current.Row);
                    minCol = Math.Min(minCol, current.Col);
                    maxCol = Math.Max(maxCol, current.Col);
                    if (current.Row == 0 || current.Row == level.Rows - 1 ||
                        current.Col == 0 || current.Col == level.Cols - 1)
                        touchesBoundary = true;
                    Visit(current.Row - 1, current.Col);
                    Visit(current.Row + 1, current.Col);
                    Visit(current.Row, current.Col - 1);
                    Visit(current.Row, current.Col + 1);
                }
                if (!touchesBoundary)
                {
                    interiorIslandCount++;
                    interiorIslandCellCount += count;
                }
                int componentHeight = maxRow - minRow + 1;
                int componentWidth = maxCol - minCol + 1;
                if ((componentWidth >= Math.Max(4, level.Cols / 3) &&
                     componentHeight <= 2) ||
                    (componentHeight >= Math.Max(4, level.Rows / 3) &&
                     componentWidth <= 2))
                    separatorCellCount += count;

                void Visit(int row, int col)
                {
                    var next = new Position(row, col);
                    if (walls.Contains(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            parallelSeparatorWallCount = separatorCellCount;
        }

        private static void AnalyzeSilhouette(
            Level level,
            out int exteriorCutDepth,
            out int concaveCornerCount)
        {
            exteriorCutDepth = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] != Tile.Void) continue;
                    int depth = Math.Min(
                        Math.Min(r, level.Rows - 1 - r),
                        Math.Min(c, level.Cols - 1 - c));
                    if (depth > exteriorCutDepth)
                        exteriorCutDepth = depth;
                }
            }

            concaveCornerCount = 0;
            for (int r = 0; r + 1 < level.Rows; r++)
            {
                for (int c = 0; c + 1 < level.Cols; c++)
                {
                    int boardCells = 0;
                    if (level.Grid[r, c] != Tile.Void) boardCells++;
                    if (level.Grid[r + 1, c] != Tile.Void) boardCells++;
                    if (level.Grid[r, c + 1] != Tile.Void) boardCells++;
                    if (level.Grid[r + 1, c + 1] != Tile.Void) boardCells++;
                    if (boardCells == 3) concaveCornerCount++;
                }
            }
        }

        private static void AnalyzeLongLanes(
            Level level,
            out int longest,
            out int longLaneFloorCount)
        {
            const int longLaneThreshold = 6;
            longest = 0;
            var longLaneCells = new HashSet<Position>();
            for (int r = 0; r < level.Rows; r++)
            {
                int start = 0;
                while (start < level.Cols)
                {
                    while (start < level.Cols && !IsFloor(level, r, start)) start++;
                    int end = start;
                    while (end < level.Cols && IsFloor(level, r, end)) end++;
                    int length = end - start;
                    if (length > longest) longest = length;
                    if (length >= longLaneThreshold)
                        for (int c = start; c < end; c++)
                            longLaneCells.Add(new Position(r, c));
                    start = Math.Max(end, start + 1);
                }
            }
            for (int c = 0; c < level.Cols; c++)
            {
                int start = 0;
                while (start < level.Rows)
                {
                    while (start < level.Rows && !IsFloor(level, start, c)) start++;
                    int end = start;
                    while (end < level.Rows && IsFloor(level, end, c)) end++;
                    int length = end - start;
                    if (length > longest) longest = length;
                    if (length >= longLaneThreshold)
                        for (int r = start; r < end; r++)
                            longLaneCells.Add(new Position(r, c));
                    start = Math.Max(end, start + 1);
                }
            }
            longLaneFloorCount = longLaneCells.Count;
        }

        private void AnalyzeStopChoices(
            Level level,
            out int reachableStops,
            out int choiceStops)
        {
            var visited = new HashSet<Position> { level.Spawn };
            var queue = new Queue<Position>();
            queue.Enqueue(level.Spawn);
            choiceStops = 0;

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

                // One legal edge may be the route back to the previous stop. Three or
                // more guarantees at least two meaningful onward options.
                if (legal >= 3) choiceStops++;
            }

            reachableStops = visited.Count;
        }

        private static void VisitFloorComponent(
            Level level,
            Position start,
            HashSet<Position> visited)
        {
            var queue = new Queue<Position>();
            visited.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                TryVisit(current.Row - 1, current.Col);
                TryVisit(current.Row + 1, current.Col);
                TryVisit(current.Row, current.Col - 1);
                TryVisit(current.Row, current.Col + 1);
            }

            void TryVisit(int row, int col)
            {
                if (!IsFloor(level, row, col)) return;
                var next = new Position(row, col);
                if (visited.Add(next)) queue.Enqueue(next);
            }
        }

        private static int FloorDegree(Level level, int row, int col)
        {
            int degree = 0;
            if (IsFloor(level, row - 1, col)) degree++;
            if (IsFloor(level, row + 1, col)) degree++;
            if (IsFloor(level, row, col - 1)) degree++;
            if (IsFloor(level, row, col + 1)) degree++;
            return degree;
        }

        private static bool IsFloor(Level level, int row, int col) =>
            row >= 0 && row < level.Rows &&
            col >= 0 && col < level.Cols &&
            level.Grid[row, col] == Tile.Floor;
    }
}
