using System;
using System.Collections.Generic;
using System.Text;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Deterministic arena-first search for Extra Hard boards. Search starts from a
    /// solver-certified broad arena, mutates compact wall islands/exterior notches,
    /// and retains near-miss states using never-stuck diagnostics. Only states passing
    /// both never-stuck safety and the scale-aware arena contract are publishable.
    /// </summary>
    public sealed class ExtraHardLevelBuilder
    {
        private const int BeamWidth = 8;
        private const int ChildrenPerState = 6;
        private const int SpawnCandidates = 12;

        private readonly Solver _solver;
        private readonly LevelDifficultyAnalyzer _difficultyAnalyzer;
        private readonly LevelLayoutAnalyzer _layoutAnalyzer;

        private sealed class SearchState
        {
            public Tile[,] Grid;
            public Position Spawn;
            public NeverStuckAnalysis Safety;
            public LevelLayoutMetrics Layout;
            public bool Publishable;
            public int MutationCount;
            public double Rank;
            public string Key;
        }

        public ExtraHardLevelBuilder(
            Solver solver,
            LevelDifficultyAnalyzer difficultyAnalyzer)
        {
            _solver = solver ?? throw new ArgumentNullException(nameof(solver));
            _difficultyAnalyzer = difficultyAnalyzer ??
                                  throw new ArgumentNullException(nameof(difficultyAnalyzer));
            _layoutAnalyzer = new LevelLayoutAnalyzer();
        }

        public static int OperationBudgetFor(int rows, int cols)
        {
            int cells = rows * cols;
            if (cells <= 100) return 8;
            if (cells <= 224) return 9;
            return 10;
        }

        public Level TryGenerate(
            DeterministicRng rng,
            int rows,
            int cols,
            double obstacleFraction,
            Difficulty difficulty,
            int index,
            int analysisSeed,
            out LevelDifficultyMetrics difficultyMetrics)
        {
            difficultyMetrics = null;
            var beam = new List<SearchState>();
            var certified = new List<SearchState>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            int firstVariant = rng.Next(rows == cols ? 8 : 4);
            for (int i = 0; i < 3; i++)
            {
                Tile[,] seedGrid = ArenaExtraHardTemplateBank.Build(
                    rows, cols, firstVariant + i, out Position seedSpawn);
                if (!TryCreateState(
                        seedGrid, seedSpawn, difficulty, index, obstacleFraction,
                        0, rng, out SearchState state))
                    continue;
                if (!seen.Add(state.Key)) continue;
                beam.Add(state);
                if (state.Publishable) certified.Add(state);
            }
            if (beam.Count == 0) return null;

            int operationBudget = OperationBudgetFor(rows, cols);
            for (int operation = 0; operation < operationBudget; operation++)
            {
                var next = new List<SearchState>(beam);
                foreach (SearchState parent in beam)
                {
                    for (int childIndex = 0;
                         childIndex < ChildrenPerState;
                         childIndex++)
                    {
                        Tile[,] childGrid = Clone(parent.Grid);
                        if (!ApplyArenaMutation(
                                childGrid, parent, rng, childIndex))
                            continue;
                        if (!TryCreateState(
                                childGrid, parent.Spawn, difficulty, index,
                                obstacleFraction, parent.MutationCount + 1, rng,
                                out SearchState child))
                            continue;
                        if (!seen.Add(child.Key)) continue;
                        next.Add(child);
                        if (child.Publishable) certified.Add(child);
                    }
                }
                beam = SelectBeam(next);
            }

            if (certified.Count == 0) return null;
            certified.Sort(CompareStates);
            double target = LevelDifficultyAnalyzer.TargetScore(difficulty, index);
            SearchState best = null;
            LevelDifficultyMetrics bestMetrics = null;
            double bestRank = double.NegativeInfinity;
            int analysisCount = Math.Min(8, certified.Count);
            for (int i = 0; i < analysisCount; i++)
            {
                SearchState state = certified[i];
                var level = new Level(
                    Clone(state.Grid), state.Spawn, difficulty, index);
                LevelDifficultyMetrics metrics = _difficultyAnalyzer.Analyze(
                    level,
                    unchecked(analysisSeed + i * 7919),
                    LevelDifficultyAnalysisOptions.Runtime);
                double rank =
                    -Math.Abs(metrics.Score - target) +
                    metrics.Score * 0.08 +
                    state.Rank * 0.015 +
                    state.MutationCount * 1.5;
                if (rank <= bestRank) continue;
                bestRank = rank;
                best = state;
                bestMetrics = metrics;
            }

            if (best == null) return null;
            difficultyMetrics = bestMetrics;
            return new Level(Clone(best.Grid), best.Spawn, difficulty, index);
        }

        private bool TryCreateState(
            Tile[,] grid,
            Position preferredSpawn,
            Difficulty difficulty,
            int index,
            double obstacleFraction,
            int mutationCount,
            DeterministicRng rng,
            out SearchState state)
        {
            state = null;
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            if (!FloorIsConnected(grid) ||
                FloorFraction(grid) < 0.50 ||
                !HasTile(grid, Tile.Void) ||
                !HasTile(grid, Tile.Wall))
                return false;

            var topologyProbe = new Level(
                grid,
                FirstFloor(grid),
                difficulty,
                index);
            if (!LevelTopology.HasOnlyExteriorVoid(topologyProbe))
                return false;

            Position spawn = SelectBestSpawn(
                grid, preferredSpawn, difficulty, index, rng,
                out NeverStuckAnalysis safety);
            var level = new Level(grid, spawn, difficulty, index);
            LevelLayoutMetrics layout = _layoutAnalyzer.Analyze(level);
            if (layout.OpenFloorCoreCount == 0 ||
                layout.OpenFloorCoreCellRatio < 0.12 ||
                layout.InteriorWallIslandCount == 0)
                return false;

            bool publishable = safety.Passes &&
                LevelLayoutAnalyzer.MeetsExtraHardFloor(
                    level, layout, out _);
            int uncovered = Math.Max(
                0, safety.TotalFloorCount - safety.CoveredFloorCount);
            int stranded = safety.StrandedStops?.Count ?? 0;
            double actualObstacles = 1.0 - FloorFraction(grid);
            double rank =
                (publishable ? 2200.0 : 0.0) -
                uncovered * 32.0 -
                stranded * 48.0 +
                layout.OpenFloorCoreCellRatio * 620.0 +
                layout.LargestOpenCoreComponentRatio * 260.0 +
                layout.FloorDegreeFourRatio * 180.0 +
                Math.Min(12, layout.InteriorWallIslandCount) * 5.0 -
                layout.ParallelSeparatorWallRatio * 180.0 -
                Math.Abs(actualObstacles - obstacleFraction) * 140.0 +
                mutationCount * 4.0;

            state = new SearchState
            {
                Grid = grid,
                Spawn = spawn,
                Safety = safety,
                Layout = layout,
                Publishable = publishable,
                MutationCount = mutationCount,
                Rank = rank,
                Key = CanonicalKey(grid, spawn)
            };
            return true;
        }

        private Position SelectBestSpawn(
            Tile[,] grid,
            Position preferred,
            Difficulty difficulty,
            int index,
            DeterministicRng rng,
            out NeverStuckAnalysis bestAnalysis)
        {
            var candidates = new List<Position>();
            AddIfFloor(preferred);
            AddIfFloor(new Position(0, 0));
            AddIfFloor(new Position(0, grid.GetLength(1) - 1));
            AddIfFloor(new Position(grid.GetLength(0) - 1, 0));
            AddIfFloor(new Position(
                grid.GetLength(0) - 1, grid.GetLength(1) - 1));

            var remaining = new List<Position>();
            for (int r = 0; r < grid.GetLength(0); r++)
                for (int c = 0; c < grid.GetLength(1); c++)
                    if (grid[r, c] == Tile.Floor)
                        remaining.Add(new Position(r, c));
            rng.Shuffle(remaining);
            foreach (Position candidate in remaining)
            {
                AddIfFloor(candidate);
                if (candidates.Count >= SpawnCandidates) break;
            }

            Position best = candidates[0];
            bestAnalysis = null;
            double bestRank = double.NegativeInfinity;
            foreach (Position candidate in candidates)
            {
                var level = new Level(grid, candidate, difficulty, index);
                NeverStuckAnalysis analysis = _solver.AnalyzeNeverStuck(level);
                int uncovered = Math.Max(
                    0, analysis.TotalFloorCount - analysis.CoveredFloorCount);
                int stranded = analysis.StrandedStops?.Count ?? 0;
                double rank =
                    (analysis.Passes ? 1000000.0 : 0.0) -
                    uncovered * 100.0 -
                    stranded * 180.0 +
                    analysis.CoveredFloorCount;
                if (rank <= bestRank) continue;
                bestRank = rank;
                best = candidate;
                bestAnalysis = analysis;
                if (analysis.Passes && candidate == preferred)
                    break;
            }
            return best;

            void AddIfFloor(Position candidate)
            {
                if (candidate.Row < 0 || candidate.Row >= grid.GetLength(0) ||
                    candidate.Col < 0 || candidate.Col >= grid.GetLength(1) ||
                    grid[candidate.Row, candidate.Col] != Tile.Floor ||
                    candidates.Contains(candidate))
                    return;
                candidates.Add(candidate);
            }
        }

        private static List<SearchState> SelectBeam(List<SearchState> states)
        {
            states.Sort(CompareStates);
            var selected = new List<SearchState>(BeamWidth);
            var selectedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (SearchState candidate in states)
            {
                if (selected.Count >= BeamWidth - 2) break;
                if (selectedKeys.Add(candidate.Key)) selected.Add(candidate);
            }
            foreach (SearchState candidate in states)
            {
                if (selected.Count >= BeamWidth) break;
                if (candidate.Safety.Passes ||
                    !selectedKeys.Add(candidate.Key))
                    continue;
                selected.Add(candidate);
            }
            foreach (SearchState candidate in states)
            {
                if (selected.Count >= BeamWidth) break;
                if (selectedKeys.Add(candidate.Key)) selected.Add(candidate);
            }
            return selected;
        }

        private static int CompareStates(SearchState left, SearchState right)
        {
            int rank = right.Rank.CompareTo(left.Rank);
            return rank != 0
                ? rank
                : string.CompareOrdinal(left.Key, right.Key);
        }

        private static bool ApplyArenaMutation(
            Tile[,] grid,
            SearchState parent,
            DeterministicRng rng,
            int childIndex)
        {
            if (!parent.Safety.Passes)
            {
                if (parent.Safety.FailureKind ==
                    NeverStuckFailureKind.UncoveredFloor &&
                    childIndex < 2 &&
                    RepairUncoveredFloor(grid, parent, rng))
                    return true;
                if (parent.Safety.FailureKind ==
                    NeverStuckFailureKind.NotStronglyConnected &&
                    childIndex < 2 &&
                    RepairStrandedStop(grid, parent, rng))
                    return true;
            }

            int operation = (rng.Next(7) + childIndex) % 7;
            return operation switch
            {
                0 => AddCompactIsland(grid, parent.Spawn, rng),
                1 => ShiftCompactIsland(grid, rng),
                2 => CarveIslandEdge(grid, rng),
                3 => SwapWallAndFloor(grid, parent.Spawn, rng),
                4 => AddExteriorNotch(grid, parent.Spawn, rng),
                5 => AddCompactIsland(grid, parent.Spawn, rng),
                _ => ShiftCompactIsland(grid, rng)
            };
        }

        private static bool AddCompactIsland(
            Tile[,] grid,
            Position spawn,
            DeterministicRng rng)
        {
            var shapes = new[]
            {
                new[] { new Position(0, 0), new Position(0, 1) },
                new[] { new Position(0, 0), new Position(1, 0) },
                new[]
                {
                    new Position(0, 0), new Position(0, 1),
                    new Position(1, 0)
                },
                new[]
                {
                    new Position(0, 0), new Position(0, 1),
                    new Position(1, 0), new Position(1, 1)
                },
                new[]
                {
                    new Position(0, 0), new Position(0, 1),
                    new Position(0, 2)
                }
            };
            Position[] shape = shapes[rng.Next(shapes.Length)];
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int row = 1 + rng.Next(Math.Max(1, grid.GetLength(0) - 2));
                int col = 1 + rng.Next(Math.Max(1, grid.GetLength(1) - 2));
                bool valid = true;
                foreach (Position offset in shape)
                {
                    int r = row + offset.Row;
                    int c = col + offset.Col;
                    if (r <= 0 || r >= grid.GetLength(0) - 1 ||
                        c <= 0 || c >= grid.GetLength(1) - 1 ||
                        grid[r, c] != Tile.Floor ||
                        (r == spawn.Row && c == spawn.Col))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;
                foreach (Position offset in shape)
                    grid[row + offset.Row, col + offset.Col] = Tile.Wall;
                return true;
            }
            return false;
        }

        private static bool ShiftCompactIsland(
            Tile[,] grid,
            DeterministicRng rng)
        {
            List<List<Position>> components = WallComponents(grid);
            components.RemoveAll(component => component.Count > 18);
            if (components.Count == 0) return false;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                List<Position> component = components[rng.Next(components.Count)];
                Position direction = CardinalDirections[rng.Next(4)];
                var members = new HashSet<Position>(component);
                bool valid = true;
                foreach (Position source in component)
                {
                    var destination = new Position(
                        source.Row + direction.Row,
                        source.Col + direction.Col);
                    if (destination.Row <= 0 ||
                        destination.Row >= grid.GetLength(0) - 1 ||
                        destination.Col <= 0 ||
                        destination.Col >= grid.GetLength(1) - 1 ||
                        (!members.Contains(destination) &&
                         grid[destination.Row, destination.Col] != Tile.Floor))
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;
                foreach (Position source in component)
                    grid[source.Row, source.Col] = Tile.Floor;
                foreach (Position source in component)
                    grid[source.Row + direction.Row, source.Col + direction.Col] =
                        Tile.Wall;
                return true;
            }
            return false;
        }

        private static bool CarveIslandEdge(Tile[,] grid, DeterministicRng rng)
        {
            var candidates = new List<Position>();
            foreach (List<Position> component in WallComponents(grid))
            {
                if (component.Count <= 2) continue;
                foreach (Position wall in component)
                {
                    int neighbours = 0;
                    foreach (Position direction in CardinalDirections)
                    {
                        int row = wall.Row + direction.Row;
                        int col = wall.Col + direction.Col;
                        if (InBounds(grid, row, col) &&
                            grid[row, col] == Tile.Wall)
                            neighbours++;
                    }
                    if (neighbours <= 2) candidates.Add(wall);
                }
            }
            if (candidates.Count == 0) return false;
            Position selected = candidates[rng.Next(candidates.Count)];
            grid[selected.Row, selected.Col] = Tile.Floor;
            return true;
        }

        private static bool SwapWallAndFloor(
            Tile[,] grid,
            Position spawn,
            DeterministicRng rng)
        {
            var walls = new List<Position>();
            for (int r = 1; r < grid.GetLength(0) - 1; r++)
                for (int c = 1; c < grid.GetLength(1) - 1; c++)
                    if (grid[r, c] == Tile.Wall)
                        walls.Add(new Position(r, c));
            if (walls.Count == 0) return false;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                Position wall = walls[rng.Next(walls.Count)];
                int row = wall.Row + rng.Next(5) - 2;
                int col = wall.Col + rng.Next(5) - 2;
                if (row <= 0 || row >= grid.GetLength(0) - 1 ||
                    col <= 0 || col >= grid.GetLength(1) - 1 ||
                    grid[row, col] != Tile.Floor ||
                    (row == spawn.Row && col == spawn.Col))
                    continue;
                grid[wall.Row, wall.Col] = Tile.Floor;
                grid[row, col] = Tile.Wall;
                return true;
            }
            return false;
        }

        private static bool AddExteriorNotch(
            Tile[,] grid,
            Position spawn,
            DeterministicRng rng)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            int side = rng.Next(4);
            int length = 1 + rng.Next(3);
            int depth = 1 + rng.Next(2);
            int span = side < 2 ? cols : rows;
            int start = 1 + rng.Next(Math.Max(1, span - length - 1));
            var cells = new List<Position>();
            for (int d = 0; d < depth; d++)
            {
                for (int i = 0; i < length; i++)
                {
                    Position cell = side switch
                    {
                        0 => new Position(d, start + i),
                        1 => new Position(rows - 1 - d, start + i),
                        2 => new Position(start + i, d),
                        _ => new Position(start + i, cols - 1 - d)
                    };
                    if (!InBounds(grid, cell.Row, cell.Col) ||
                        grid[cell.Row, cell.Col] != Tile.Floor ||
                        cell == spawn)
                        return false;
                    cells.Add(cell);
                }
            }
            foreach (Position cell in cells)
                grid[cell.Row, cell.Col] = Tile.Void;
            return true;
        }

        private static bool RepairUncoveredFloor(
            Tile[,] grid,
            SearchState parent,
            DeterministicRng rng)
        {
            if (parent.Safety.UncoveredFloors.Count == 0) return false;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Position uncovered = parent.Safety.UncoveredFloors[
                    rng.Next(parent.Safety.UncoveredFloors.Count)];
                Position direction = CardinalDirections[rng.Next(4)];
                int row = uncovered.Row + direction.Row;
                int col = uncovered.Col + direction.Col;
                if (row <= 0 || row >= grid.GetLength(0) - 1 ||
                    col <= 0 || col >= grid.GetLength(1) - 1 ||
                    grid[row, col] != Tile.Floor ||
                    (row == parent.Spawn.Row && col == parent.Spawn.Col))
                    continue;
                grid[row, col] = Tile.Wall;
                return true;
            }
            return false;
        }

        private static bool RepairStrandedStop(
            Tile[,] grid,
            SearchState parent,
            DeterministicRng rng)
        {
            if (parent.Safety.StrandedStops.Count == 0) return false;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Position stop = parent.Safety.StrandedStops[
                    rng.Next(parent.Safety.StrandedStops.Count)];
                Position direction = CardinalDirections[rng.Next(4)];
                int row = stop.Row + direction.Row;
                int col = stop.Col + direction.Col;
                if (!InBounds(grid, row, col) ||
                    grid[row, col] != Tile.Wall)
                    continue;
                grid[row, col] = Tile.Floor;
                return true;
            }
            return false;
        }

        private static List<List<Position>> WallComponents(Tile[,] grid)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            var seen = new bool[rows, cols];
            var result = new List<List<Position>>();
            for (int r = 1; r < rows - 1; r++)
            {
                for (int c = 1; c < cols - 1; c++)
                {
                    if (seen[r, c] || grid[r, c] != Tile.Wall) continue;
                    var component = new List<Position>();
                    var queue = new Queue<Position>();
                    seen[r, c] = true;
                    queue.Enqueue(new Position(r, c));
                    while (queue.Count > 0)
                    {
                        Position current = queue.Dequeue();
                        component.Add(current);
                        foreach (Position direction in CardinalDirections)
                        {
                            int row = current.Row + direction.Row;
                            int col = current.Col + direction.Col;
                            if (row <= 0 || row >= rows - 1 ||
                                col <= 0 || col >= cols - 1 ||
                                seen[row, col] ||
                                grid[row, col] != Tile.Wall)
                                continue;
                            seen[row, col] = true;
                            queue.Enqueue(new Position(row, col));
                        }
                    }
                    result.Add(component);
                }
            }
            return result;
        }

        private static bool FloorIsConnected(Tile[,] grid)
        {
            Position start = FirstFloor(grid);
            var seen = new bool[grid.GetLength(0), grid.GetLength(1)];
            var queue = new Queue<Position>();
            seen[start.Row, start.Col] = true;
            queue.Enqueue(start);
            int count = 0;
            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                count++;
                foreach (Position direction in CardinalDirections)
                {
                    int row = current.Row + direction.Row;
                    int col = current.Col + direction.Col;
                    if (!InBounds(grid, row, col) || seen[row, col] ||
                        grid[row, col] != Tile.Floor)
                        continue;
                    seen[row, col] = true;
                    queue.Enqueue(new Position(row, col));
                }
            }
            int total = 0;
            foreach (Tile tile in grid)
                if (tile == Tile.Floor) total++;
            return count == total;
        }

        private static Position FirstFloor(Tile[,] grid)
        {
            for (int r = 0; r < grid.GetLength(0); r++)
                for (int c = 0; c < grid.GetLength(1); c++)
                    if (grid[r, c] == Tile.Floor)
                        return new Position(r, c);
            throw new InvalidOperationException("Arena candidate has no floor.");
        }

        private static bool HasTile(Tile[,] grid, Tile expected)
        {
            foreach (Tile tile in grid)
                if (tile == expected) return true;
            return false;
        }

        private static double FloorFraction(Tile[,] grid)
        {
            int floors = 0;
            foreach (Tile tile in grid)
                if (tile == Tile.Floor) floors++;
            return (double)floors / grid.Length;
        }

        private static Tile[,] Clone(Tile[,] source)
        {
            return (Tile[,])source.Clone();
        }

        private static string CanonicalKey(Tile[,] grid, Position spawn)
        {
            var builder = new StringBuilder(grid.Length + 16);
            builder.Append(spawn.Row).Append(',').Append(spawn.Col).Append('|');
            for (int r = 0; r < grid.GetLength(0); r++)
                for (int c = 0; c < grid.GetLength(1); c++)
                    builder.Append((char)('0' + (int)grid[r, c]));
            return builder.ToString();
        }

        private static bool InBounds(Tile[,] grid, int row, int col)
        {
            return row >= 0 && row < grid.GetLength(0) &&
                   col >= 0 && col < grid.GetLength(1);
        }

        private static readonly Position[] CardinalDirections =
        {
            new Position(-1, 0),
            new Position(1, 0),
            new Position(0, -1),
            new Position(0, 1)
        };
    }
}
