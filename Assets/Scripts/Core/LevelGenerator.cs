using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    public sealed class LevelGenerationOptions
    {
        public int AttemptCount { get; set; } = LevelGenerator.DefaultAttemptCount;
        public int AttemptOffset { get; set; }
        public bool EvaluateMinMoves { get; set; }
        public int MinMovesMaxStates { get; set; } = 200000;
        public int GeneratorVersion { get; set; } = LevelGenerator.GeneratorVersion;
    }

    /// <summary>
    /// Pure deterministic level generator shared by runtime and offline tooling.
    /// </summary>
    public sealed class LevelGenerator
    {
        public const int LegacyGeneratorVersion = 3;
        public const int GeneratorVersion = 6;
        public const int DefaultAttemptCount = 12;

        private const int InnerCandidatesPerFraction = 32;
        private const int MaxSpawnCandidates = 6;
        private const double MinMaskOccupancy = 0.58;

        private static readonly double[] FractionScheduleOffsets =
        {
            0.00, 0.02, -0.02, 0.04, -0.04, 0.06, -0.06, 0.08
        };

        private readonly Solver _solver;
        private readonly LevelQualityScorer _scorer;
        private readonly LevelDifficultyAnalyzer _difficultyAnalyzer;
        private readonly ExtraHardLevelBuilder _extraHardBuilder;

        public LevelGenerator(Solver solver = null, LevelQualityScorer scorer = null)
        {
            _solver = solver ?? new Solver(new MovementSystem());
            _scorer = scorer ?? new LevelQualityScorer(_solver);
            _difficultyAnalyzer = new LevelDifficultyAnalyzer(_solver);
            _extraHardBuilder = new ExtraHardLevelBuilder(_solver, _difficultyAnalyzer);
        }

        public static int VersionFor(Difficulty difficulty) =>
            difficulty >= Difficulty.ExtraHard ? GeneratorVersion : LegacyGeneratorVersion;

        public static int ExtraHardOperationBudgetFor(int rows, int cols) =>
            ExtraHardLevelBuilder.OperationBudgetFor(rows, cols);

        public Level Generate(Difficulty difficulty, int levelNumber, LevelGenerationOptions options = null)
        {
            options = options ?? new LevelGenerationOptions();
            if (levelNumber < 1) levelNumber = 1;

            var cfg = DifficultyConfig.For(difficulty);
            int rows = cfg.BoardRowsFor(levelNumber);
            int cols = cfg.BoardColsFor(levelNumber);
            if (difficulty >= Difficulty.ExtraHard)
                return GenerateExtraHard(difficulty, levelNumber, rows, cols, cfg.ObstacleFraction, options);

            Level best = null;
            double bestScore = double.NegativeInfinity;
            for (int attempt = 0; attempt < options.AttemptCount; attempt++)
            {
                int seed = LevelSeed.For(difficulty, levelNumber, SeedVersionFor(difficulty, options),
                    options.AttemptOffset + attempt);
                var rng = new DeterministicRng(seed);
                Level candidate = SearchAttempt(rng, rows, cols, cfg.ObstacleFraction, difficulty, levelNumber,
                    options);
                if (candidate == null) continue;

                LevelQualityMetrics metrics = _scorer.Score(candidate, difficulty, options.EvaluateMinMoves,
                    options.MinMovesMaxStates);
                if (metrics.PassesQualityFloors && metrics.Score > bestScore)
                {
                    best = candidate;
                    bestScore = metrics.Score;
                }
            }

            return best ?? GenerateGuaranteedFallback(rows, cols, difficulty, levelNumber);
        }

        private Level GenerateExtraHard(
            Difficulty difficulty,
            int levelNumber,
            int rows,
            int cols,
            double obstacleFraction,
            LevelGenerationOptions options)
        {
            Level best = null;
            double bestRank = double.NegativeInfinity;
            int attemptCount = Math.Max(
                1,
                Math.Min(options.AttemptCount, options.EvaluateMinMoves ? 2 : 1));
            int version = SeedVersionFor(difficulty, options);
            LevelDifficultyAnalysisOptions analysisOptions = options.EvaluateMinMoves
                ? LevelDifficultyAnalysisOptions.Bake
                : LevelDifficultyAnalysisOptions.Runtime;

            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                int attemptNumber = options.AttemptOffset + attempt;
                int seed = LevelSeed.For(difficulty, levelNumber, version, attemptNumber);
                var rng = new DeterministicRng(seed);
                Level candidate = _extraHardBuilder.TryGenerate(
                    rng, rows, cols, obstacleFraction, difficulty, levelNumber, seed,
                    out LevelDifficultyMetrics spawnMetrics);
                if (candidate == null) continue;

                LevelQualityMetrics quality = _scorer.Score(candidate, difficulty);
                if (!quality.PassesQualityFloors) continue;

                LevelDifficultyMetrics metrics = options.EvaluateMinMoves
                    ? _difficultyAnalyzer.Analyze(candidate, seed, analysisOptions)
                    : spawnMetrics ?? _difficultyAnalyzer.Analyze(candidate, seed, analysisOptions);
                double target = LevelDifficultyAnalyzer.TargetScore(difficulty, levelNumber);
                double targetDistance = Math.Abs(metrics.Score - target);
                double rank = -targetDistance + metrics.Score * 0.08 + quality.Score * 0.03;
                if (rank <= bestRank) continue;
                best = candidate;
                bestRank = rank;
            }

            if (best != null) return best;

            int openFallbackSeed = LevelSeed.For(
                difficulty, levelNumber, version, options.AttemptOffset + 10000);
            Level openFallback = _extraHardBuilder.TryGenerate(
                new DeterministicRng(openFallbackSeed),
                rows, cols, obstacleFraction, difficulty, levelNumber,
                openFallbackSeed, out _);
            if (openFallback != null)
                return openFallback;

            throw new InvalidOperationException(
                $"Unable to certify open Extra Hard layout ({rows}x{cols}, " +
                $"{difficulty} #{levelNumber}). Corridor recovery is diagnostic-only.");
        }

        private static int SeedVersionFor(Difficulty difficulty, LevelGenerationOptions options)
        {
            // The v6 change is intentionally scoped to Extra Hard. Existing lower-mode
            // defaults continue using their v3 seed stream and therefore preserve layouts.
            return options.GeneratorVersion == GeneratorVersion
                ? VersionFor(difficulty)
                : options.GeneratorVersion;
        }

        private Level SearchAttempt(DeterministicRng rng, int rows, int cols, double targetFraction,
            Difficulty difficulty, int index, LevelGenerationOptions options,
            bool useExtraHardStructures = false)
        {
            Level best = null;
            double bestScore = double.NegativeInfinity;

            foreach (double fraction in BuildFractionSchedule(targetFraction))
            {
                for (int inner = 0; inner < InnerCandidatesPerFraction; inner++)
                {
                    Level candidate = TryGenerateBoard(
                        rng, rows, cols, fraction, difficulty, index, useExtraHardStructures);
                    if (candidate == null) continue;

                    LevelQualityMetrics metrics = _scorer.Score(candidate, difficulty, options.EvaluateMinMoves,
                        options.MinMovesMaxStates);
                    if (!metrics.PassesQualityFloors) continue;
                    if (metrics.Score > bestScore)
                    {
                        best = candidate;
                        bestScore = metrics.Score;
                    }
                }
            }

            return best;
        }

        private Level TryGenerateBoard(DeterministicRng rng, int rows, int cols, double obstacleFraction,
            Difficulty difficulty, int index, bool useExtraHardStructures)
        {
            bool[,] mask = TryBuildIrregularMask(rows, cols, rng);
            if (mask == null) return null;

            var grid = MaskToGrid(mask, rows, cols);
            if (!TryScatterWalls(
                grid, mask, rows, cols, rng, obstacleFraction,
                useExtraHardStructures)) return null;
            LevelTopology.SealEnclosedVoid(grid);
            if (!LevelTopology.HasExteriorVoid(grid)) return null;

            List<Position> floors = CollectFloors(grid, rows, cols);
            if (floors.Count == 0) return null;

            rng.Shuffle(floors);
            int spawnTries = difficulty >= Difficulty.ExtraHard
                ? Math.Min(24, floors.Count)
                : Math.Min(SpawnTriesFor(obstacleFraction), floors.Count);
            for (int i = 0; i < spawnTries; i++)
            {
                Position spawn = floors[i];
                var candidate = new Level(grid, spawn, difficulty, index);
                if (_solver.IsAlwaysSolvable(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool[,] TryBuildIrregularMask(int rows, int cols, DeterministicRng rng)
        {
            var mask = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                    mask[r, c] = true;
            }

            int cutCount = NextInt(rng, 2, Math.Max(3, (rows + cols) / 3));
            for (int cut = 0; cut < cutCount; cut++)
            {
                int edge = rng.Next(4);
                if (edge < 2)
                {
                    int span = NextInt(rng, 1, Math.Max(1, cols / 3));
                    int depth = NextInt(rng, 1, Math.Max(1, Math.Min(3, rows / 3)));
                    int start = rng.Next(Math.Max(1, cols - span + 1));
                    if (edge == 0)
                    {
                        for (int r = 0; r < depth; r++)
                        {
                            for (int c = start; c < start + span; c++)
                                mask[r, c] = false;
                        }
                    }
                    else
                    {
                        for (int r = rows - depth; r < rows; r++)
                        {
                            for (int c = start; c < start + span; c++)
                                mask[r, c] = false;
                        }
                    }
                }
                else
                {
                    int span = NextInt(rng, 1, Math.Max(1, rows / 3));
                    int depth = NextInt(rng, 1, Math.Max(1, Math.Min(3, cols / 3)));
                    int start = rng.Next(Math.Max(1, rows - span + 1));
                    if (edge == 2)
                    {
                        for (int r = start; r < start + span; r++)
                        {
                            for (int c = 0; c < depth; c++)
                                mask[r, c] = false;
                        }
                    }
                    else
                    {
                        for (int r = start; r < start + span; r++)
                        {
                            for (int c = cols - depth; c < cols; c++)
                                mask[r, c] = false;
                        }
                    }
                }
            }

            int active = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (mask[r, c]) active++;
                }
            }

            return active >= (int)(rows * cols * MinMaskOccupancy) ? mask : null;
        }

        private static Tile[,] MaskToGrid(bool[,] mask, int rows, int cols)
        {
            var grid = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                    grid[r, c] = mask[r, c] ? Tile.Floor : Tile.Void;
            }

            return grid;
        }

        private static bool TryScatterWalls(
            Tile[,] grid,
            bool[,] mask,
            int rows,
            int cols,
            DeterministicRng rng,
            double obstacleFraction,
            bool useExtraHardStructures)
        {
            var cells = new List<Position>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (mask[r, c])
                        cells.Add(new Position(r, c));
                }
            }

            if (cells.Count == 0) return false;

            int target = (int)Math.Round(cells.Count * obstacleFraction);
            var walls = new HashSet<Position>();
            int attempts = 0;
            int maxAttempts = Math.Max(40, target * 16);

            while (walls.Count < target && attempts < maxAttempts)
            {
                attempts++;
                Position cell = cells[rng.Next(cells.Count)];
                bool horizontal = rng.NextDouble() < 0.5;
                int length = ChooseBarLength(rng, useExtraHardStructures);
                int dr = horizontal ? 0 : 1;
                int dc = horizontal ? 1 : 0;
                if (rng.NextDouble() < 0.5)
                {
                    dr = -dr;
                    dc = -dc;
                }

                var bar = new List<Position>(length);
                for (int i = 0; i < length; i++)
                {
                    int r = cell.Row + dr * i;
                    int c = cell.Col + dc * i;
                    if (r < 0 || r >= rows || c < 0 || c >= cols || !mask[r, c]) continue;
                    bar.Add(new Position(r, c));
                }

                if (bar.Count == 0) continue;
                foreach (Position p in bar)
                {
                    if (walls.Count >= target) break;
                    walls.Add(p);
                }
            }

            var isolated = new List<Position>();
            foreach (Position p in walls)
            {
                bool joined = false;
                foreach (Position n in OrthogonalNeighbours(p, rows, cols))
                {
                    if (walls.Contains(n))
                    {
                        joined = true;
                        break;
                    }
                }

                if (!joined) isolated.Add(p);
            }

            rng.Shuffle(isolated);
            int keepIsolated = Math.Max(1, (int)Math.Round(walls.Count * 0.15));
            for (int i = keepIsolated; i < isolated.Count; i++)
                walls.Remove(isolated[i]);

            foreach (Position p in walls)
                grid[p.Row, p.Col] = Tile.Wall;

            return IsFloorConnected(grid, rows, cols);
        }

        private static int ChooseBarLength(DeterministicRng rng, bool useExtraHardStructures)
        {
            if (useExtraHardStructures && rng.NextDouble() < 0.45)
                return 4 + rng.Next(5);
            if (rng.NextDouble() < 0.12) return 1;
            return rng.Next(3) < 2 ? 2 : 3;
        }

        private static IEnumerable<Position> OrthogonalNeighbours(Position p, int rows, int cols)
        {
            if (p.Row > 0) yield return new Position(p.Row - 1, p.Col);
            if (p.Row + 1 < rows) yield return new Position(p.Row + 1, p.Col);
            if (p.Col > 0) yield return new Position(p.Row, p.Col - 1);
            if (p.Col + 1 < cols) yield return new Position(p.Row, p.Col + 1);
        }

        private static bool IsFloorConnected(Tile[,] grid, int rows, int cols)
        {
            Position? start = null;
            for (int r = 0; r < rows && !start.HasValue; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] == Tile.Floor)
                    {
                        start = new Position(r, c);
                        break;
                    }
                }
            }

            if (!start.HasValue) return false;

            var seen = new HashSet<Position> { start.Value };
            var queue = new Queue<Position>();
            queue.Enqueue(start.Value);
            while (queue.Count > 0)
            {
                Position cur = queue.Dequeue();
                foreach (Position n in OrthogonalNeighbours(cur, rows, cols))
                {
                    if (grid[n.Row, n.Col] != Tile.Floor || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] == Tile.Floor && !seen.Contains(new Position(r, c)))
                        return false;
                }
            }

            return true;
        }

        private static List<Position> CollectFloors(Tile[,] grid, int rows, int cols)
        {
            var floors = new List<Position>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] == Tile.Floor)
                        floors.Add(new Position(r, c));
                }
            }

            return floors;
        }

        private static double[] BuildFractionSchedule(double target)
        {
            var schedule = new double[FractionScheduleOffsets.Length];
            for (int i = 0; i < schedule.Length; i++)
            {
                double value = target + FractionScheduleOffsets[i];
                if (value < 0.05) value = 0.05;
                if (value > 0.45) value = 0.45;
                schedule[i] = value;
            }

            return schedule;
        }

        private static int NextInt(DeterministicRng rng, int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive) maxInclusive = minInclusive;
            return minInclusive + rng.Next(maxInclusive - minInclusive + 1);
        }

        /// <summary>
        /// Guaranteed-valid fallback used when irregular generation fails. Varies structurally
        /// by difficulty and index so canonical fingerprints stay distinct.
        /// </summary>
        public Level GenerateGuaranteedFallback(
            int rows,
            int cols,
            Difficulty difficulty,
            int index,
            int variant = 0)
        {
            int seed = LevelSeed.For(
                difficulty, index, VersionFor(difficulty), attempt: unchecked(991 + variant));
            var rng = new DeterministicRng(seed);
            if (difficulty >= Difficulty.ExtraHard)
            {
                Tile[,] corridorGrid = BuildHamiltonianCorridorGrid(
                    rows, cols, rng, out Position corridorSpawn);
                var corridor = new Level(
                    corridorGrid, corridorSpawn, difficulty, index);
                if (LevelSafetyValidator.IsSafe(corridor, _solver, out _))
                    return corridor;
            }

            Tile[,] grid = BuildSerpentineGrid(rows, cols);
            ApplyMinimalSerpentineVoids(grid, rows, cols, difficulty, index, variant);
            ApplySafeFallbackFeatures(grid, rows, cols, rng, difficulty, index);
            LevelTopology.SealEnclosedVoid(grid);

            Level selected = TrySelectSolvableSpawn(grid, rows, cols, rng, difficulty, index);
            if (selected != null && LevelSafetyValidator.IsSafe(selected, _solver, out _))
                return selected;

            Level repaired = RepairFallbackUntilSolvable(grid, rows, cols, difficulty, index);
            if (repaired != null && LevelSafetyValidator.IsSafe(repaired, _solver, out _))
                return repaired;

            return BuildPlainSerpentineFallback(rows, cols, difficulty, index);
        }

        public Level GenerateCertifiedOpenFallback(
            int rows,
            int cols,
            Difficulty difficulty,
            int index,
            int variant = 0)
        {
            if (difficulty < Difficulty.ExtraHard)
                return GenerateGuaranteedFallback(rows, cols, difficulty, index, variant);

            for (int retry = 0; retry < 4; retry++)
            {
                int seed = LevelSeed.For(
                    difficulty, index, VersionFor(difficulty),
                    attempt: 30000 + variant * 4 + retry);
                Level candidate = _extraHardBuilder.TryGenerate(
                    new DeterministicRng(seed),
                    rows,
                    cols,
                    DifficultyConfig.For(difficulty).ObstacleFraction,
                    difficulty,
                    index,
                    seed,
                    out _);
                if (candidate == null) continue;
                LevelQualityMetrics quality = _scorer.Score(candidate, difficulty);
                if (quality.PassesQualityFloors &&
                    LevelLayoutAnalyzer.MeetsExtraHardFloor(
                        candidate, quality.Layout, out _) &&
                    LevelSafetyValidator.IsSafe(candidate, _solver, out _))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Open fallback bank exhausted ({rows}x{cols}, {difficulty} #{index}).");
        }

        /// <summary>Legacy square serpentine retained for solver tests and diagnostics.</summary>
        public Level GenerateSerpentine(int size, Difficulty difficulty, int index)
        {
            Tile[,] grid = BuildSerpentineGrid(size, size);
            int marked = 0;
            for (int r = 1; r < size && marked < Math.Max(2, size / 2); r += 2)
            {
                int connector = ((r / 2) % 2 == 0) ? size - 1 : 0;
                int col = connector == 0 ? size - 1 : 0;
                if (grid[r, col] != Tile.Wall) continue;
                grid[r, col] = Tile.Void;
                marked++;
            }
            return new Level(grid, new Position(0, 0), difficulty, index);
        }

        private static int SpawnTriesFor(double obstacleFraction)
        {
            int tries = MaxSpawnCandidates;
            if (obstacleFraction >= 0.26) tries += 6;
            else if (obstacleFraction >= 0.22) tries += 3;
            return tries;
        }

        private static void ApplyMinimalSerpentineVoids(
            Tile[,] grid,
            int rows,
            int cols,
            Difficulty difficulty,
            int index,
            int variant)
        {
            int marked = 0;
            int voidTarget = Math.Max(2, (rows + cols) / 4);
            int oddRowCount = Math.Max(1, rows / 2);
            int startOdd = (index + (int)difficulty) % oddRowCount;
            for (int step = 0; step < oddRowCount && marked < voidTarget; step++)
            {
                int r = 1 + 2 * ((startOdd + step) % oddRowCount);
                if (r >= rows) continue;
                int connector = ((r / 2) % 2 == 0) ? cols - 1 : 0;
                int col = connector == 0 ? cols - 1 : 0;
                if (grid[r, col] == Tile.Wall)
                {
                    int maxDepth = variant == 0 ? 1 : Math.Max(1, Math.Min(5, cols / 3));
                    int depth = variant == 0
                        ? 1
                        : 1 + (int)(MixNotch(index, variant, r, difficulty) % (uint)maxDepth);
                    int direction = col == 0 ? 1 : -1;
                    for (int d = 0; d < depth; d++)
                    {
                        int notchCol = col + direction * d;
                        if (notchCol < 0 || notchCol >= cols ||
                            grid[r, notchCol] != Tile.Wall)
                            break;
                        grid[r, notchCol] = Tile.Void;
                    }
                    marked++;
                }
            }
        }

        private static uint MixNotch(int index, int variant, int row, Difficulty difficulty)
        {
            unchecked
            {
                uint value = (uint)index * 0x9E3779B9u;
                value ^= (uint)variant * 0x85EBCA6Bu;
                value ^= (uint)row * 0xC2B2AE35u;
                value ^= (uint)difficulty * 0x27D4EB2Fu;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }

        private static Tile[,] BuildHamiltonianCorridorGrid(
            int rows,
            int cols,
            DeterministicRng rng,
            out Position spawn)
        {
            int nodeRows = Math.Max(1, (rows + 1) / 2);
            int nodeCols = Math.Max(1, (cols + 1) / 2);
            var path = new List<Position>(nodeRows * nodeCols);
            for (int r = 0; r < nodeRows; r++)
            {
                if (r % 2 == 0)
                {
                    for (int c = 0; c < nodeCols; c++)
                        path.Add(new Position(r, c));
                }
                else
                {
                    for (int c = nodeCols - 1; c >= 0; c--)
                        path.Add(new Position(r, c));
                }
            }

            // Backbite moves preserve one Hamiltonian path while replacing the obvious
            // row snake with deterministic bends distributed across the whole board.
            int mixes = Math.Max(32, path.Count * 8);
            for (int step = 0; step < mixes; step++)
                TryBackbite(path, rng);

            var grid = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    grid[r, c] = Tile.Wall;

            for (int i = 0; i < path.Count; i++)
            {
                int row = path[i].Row * 2;
                int col = path[i].Col * 2;
                grid[row, col] = Tile.Floor;
                if (i == 0) continue;
                int previousRow = path[i - 1].Row * 2;
                int previousCol = path[i - 1].Col * 2;
                grid[(row + previousRow) / 2, (col + previousCol) / 2] = Tile.Floor;
            }

            // Wall cells on the physical edge are not part of the corridor. Removing
            // them creates deep exterior cuts without altering the certified floor path.
            for (int c = 0; c < cols; c++)
            {
                if (grid[0, c] == Tile.Wall) grid[0, c] = Tile.Void;
                if (grid[rows - 1, c] == Tile.Wall) grid[rows - 1, c] = Tile.Void;
            }
            for (int r = 0; r < rows; r++)
            {
                if (grid[r, 0] == Tile.Wall) grid[r, 0] = Tile.Void;
                if (grid[r, cols - 1] == Tile.Wall) grid[r, cols - 1] = Tile.Void;
            }

            spawn = new Position(path[0].Row * 2, path[0].Col * 2);
            return grid;
        }

        private static void TryBackbite(List<Position> path, DeterministicRng rng)
        {
            if (path.Count < 4) return;
            bool fromStart = rng.NextDouble() < 0.5;
            Position endpoint = fromStart ? path[0] : path[path.Count - 1];
            var choices = new List<int>(4);
            for (int i = 0; i < path.Count; i++)
            {
                if (fromStart && i < 2) continue;
                if (!fromStart && i > path.Count - 3) continue;
                Position candidate = path[i];
                int distance = Math.Abs(candidate.Row - endpoint.Row) +
                               Math.Abs(candidate.Col - endpoint.Col);
                if (distance == 1) choices.Add(i);
            }

            if (choices.Count == 0) return;
            int pivot = choices[rng.Next(choices.Count)];
            var rearranged = new List<Position>(path.Count);
            if (fromStart)
            {
                for (int i = pivot - 1; i >= 0; i--) rearranged.Add(path[i]);
                for (int i = pivot; i < path.Count; i++) rearranged.Add(path[i]);
            }
            else
            {
                for (int i = 0; i <= pivot; i++) rearranged.Add(path[i]);
                for (int i = path.Count - 1; i > pivot; i--) rearranged.Add(path[i]);
            }

            path.Clear();
            path.AddRange(rearranged);
        }

        private void ApplySafeFallbackFeatures(Tile[,] grid, int rows, int cols, DeterministicRng rng,
            Difficulty difficulty, int index)
        {
            unchecked
            {
                int phaseRow = 1 + ((index + (int)difficulty * 3) % Math.Max(1, rows - 2));
                int phaseCol = ((phaseRow / 2) % 2 == 0) ? cols - 1 : 0;
                TryMutateIfSolvable(grid, rows, cols, difficulty, index, g =>
                {
                    if (g[phaseRow, phaseCol] == Tile.Wall)
                        g[phaseRow, phaseCol] = Tile.Void;
                });

                int pocketR = 1 + ((index * 5 + (int)difficulty * 7) % Math.Max(1, rows - 2));
                int pocketC = 1 + ((index * 11 + (int)difficulty * 13) % Math.Max(1, cols - 2));
                TryMutateIfSolvable(grid, rows, cols, difficulty, index,
                    g => TrySetExteriorVoidOrWall(g, rows, cols, pocketR, pocketC));

                int notchR = (index + (int)difficulty * 17) % rows;
                int notchC = (index * 3 + (int)difficulty * 19) % cols;
                TryMutateIfSolvable(grid, rows, cols, difficulty, index,
                    g => TrySetExteriorVoidOrWall(g, rows, cols, notchR, notchC));
            }

            if (difficulty >= Difficulty.ExtraHard)
            {
                int targetRows = Math.Max(2, (int)Math.Ceiling(rows * 0.22));
                int openedRows = 0;
                for (int r = 1; r < rows && openedRows < targetRows; r += 2)
                {
                    int row = r;
                    int width = Math.Min(4, Math.Max(2, cols / 4));
                    int start = 1 + ((index + r * 3) % Math.Max(1, cols - width - 1));
                    bool accepted = TryMutateIfSolvable(
                        grid, rows, cols, difficulty, index, g =>
                        {
                            for (int c = start; c < start + width; c++)
                                if (g[row, c] == Tile.Wall)
                                    g[row, c] = Tile.Floor;
                        });
                    if (accepted) openedRows++;
                }
            }

            int wallBudget = difficulty >= Difficulty.ExtraHard
                ? Math.Max(4, rows / 2)
                : 1 + ((int)difficulty + index) % 4;
            List<Position> floors = CollectFloors(grid, rows, cols);
            rng.Shuffle(floors);
            int placed = 0;
            foreach (Position p in floors)
            {
                if (placed >= wallBudget) break;
                Position wallAt = p;
                if (!TryMutateIfSolvable(grid, rows, cols, difficulty, index, g =>
                    {
                        if (g[wallAt.Row, wallAt.Col] != Tile.Floor) return;
                        g[wallAt.Row, wallAt.Col] = Tile.Wall;
                    }))
                    continue;
                placed++;
            }
        }

        private bool TryMutateIfSolvable(Tile[,] grid, int rows, int cols, Difficulty difficulty, int index,
            Action<Tile[,]> mutate)
        {
            Tile[,] backup = CloneGrid(grid);
            mutate(grid);
            if (HasAnySolvableSpawn(grid, rows, cols, difficulty, index))
                return true;

            RestoreGrid(backup, grid);
            return false;
        }

        private Level TrySelectSolvableSpawn(Tile[,] grid, int rows, int cols, DeterministicRng rng,
            Difficulty difficulty, int index)
        {
            List<Position> floors = CollectFloors(grid, rows, cols);
            if (floors.Count == 0) return null;

            rng.Shuffle(floors);
            int maxTries = Math.Min(floors.Count, 8 + (int)difficulty * 2);
            for (int i = 0; i < maxTries; i++)
            {
                var candidate = new Level(grid, floors[i], difficulty, index);
                if (_solver.IsAlwaysSolvable(candidate))
                    return candidate;
            }

            return null;
        }

        private bool HasAnySolvableSpawn(Tile[,] grid, int rows, int cols, Difficulty difficulty, int index)
        {
            List<Position> floors = CollectFloors(grid, rows, cols);
            foreach (Position spawn in floors)
            {
                if (_solver.IsAlwaysSolvable(new Level(grid, spawn, difficulty, index)))
                    return true;
            }

            return false;
        }

        private Level RepairFallbackUntilSolvable(Tile[,] grid, int rows, int cols, Difficulty difficulty, int index)
        {
            Level selected = TrySelectSolvableSpawn(grid, rows, cols,
                new DeterministicRng(LevelSeed.For(difficulty, index, VersionFor(difficulty), attempt: 992)),
                difficulty, index);
            if (selected != null)
                return selected;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] != Tile.Wall) continue;
                    grid[r, c] = Tile.Floor;
                    selected = TrySelectSolvableSpawn(grid, rows, cols,
                        new DeterministicRng(LevelSeed.For(
                            difficulty, index, VersionFor(difficulty), attempt: 993 + r + c)),
                        difficulty, index);
                    if (selected != null)
                        return selected;
                }
            }

            return null;
        }

        private Level BuildPlainSerpentineFallback(int rows, int cols, Difficulty difficulty, int index)
        {
            Tile[,] grid = BuildSerpentineGrid(rows, cols);
            List<Position> floors = CollectFloors(grid, rows, cols);
            if (floors.Count == 0)
                throw new InvalidOperationException("Certified serpentine fallback produced no floor cells.");

            // The canonical endpoint is preferred so the emergency board is stable and
            // easy to reason about. The exhaustive loop is a defensive certification.
            var endpoint = new Position(0, 0);
            if (grid[endpoint.Row, endpoint.Col] == Tile.Floor)
            {
                var endpointCandidate = new Level(grid, endpoint, difficulty, index);
                if (LevelSafetyValidator.IsSafe(endpointCandidate, _solver, out _))
                    return endpointCandidate;
            }

            int start = (index + (int)difficulty) % floors.Count;
            for (int i = 0; i < floors.Count; i++)
            {
                Position spawn = floors[(start + i) % floors.Count];
                var candidate = new Level(grid, spawn, difficulty, index);
                if (LevelSafetyValidator.IsSafe(candidate, _solver, out _))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Unable to certify serpentine fallback ({rows}x{cols}, {difficulty} #{index}).");
        }

        private static Tile[,] CloneGrid(Tile[,] grid)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            var copy = new Tile[rows, cols];
            Array.Copy(grid, copy, grid.Length);
            return copy;
        }

        private static void RestoreGrid(Tile[,] source, Tile[,] target)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                    target[r, c] = source[r, c];
            }
        }

        private static void TrySetExteriorVoidOrWall(Tile[,] grid, int rows, int cols, int row, int col)
        {
            if (row < 0 || row >= rows || col < 0 || col >= cols) return;
            if (grid[row, col] == Tile.Void) return;

            bool touchesExterior = row == 0 || row == rows - 1 || col == 0 || col == cols - 1 ||
                                   (row > 0 && grid[row - 1, col] == Tile.Void) ||
                                   (row + 1 < rows && grid[row + 1, col] == Tile.Void) ||
                                   (col > 0 && grid[row, col - 1] == Tile.Void) ||
                                   (col + 1 < cols && grid[row, col + 1] == Tile.Void);
            grid[row, col] = touchesExterior ? Tile.Void : Tile.Wall;
        }

        private static Tile[,] BuildSerpentineGrid(int rows, int cols)
        {
            var grid = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (r % 2 == 0)
                    {
                        grid[r, c] = Tile.Floor;
                    }
                    else
                    {
                        int connectorCol = ((r / 2) % 2 == 0) ? cols - 1 : 0;
                        grid[r, c] = (c == connectorCol) ? Tile.Floor : Tile.Wall;
                    }
                }
            }

            return grid;
        }
    }
}
