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

        /// <summary>
        /// When set, candidate acceptance compares against these boards instead of
        /// the deterministic sketches of the previous indices.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<LevelShapeProfile> RecentShapes { get; set; }
    }

    /// <summary>
    /// Pure deterministic level generator shared by runtime and offline tooling.
    /// </summary>
    public sealed class LevelGenerator
    {
        public const int LegacyGeneratorVersion = 3;
        public const int StandardGeneratorVersion = 5;
        public const int GeneratorVersion = 7;
        public const int DefaultAttemptCount = 12;

        private const int InnerCandidatesPerFraction = 32;
        private const int MaxSpawnCandidates = 6;
        private const double MinMaskOccupancy = 0.58;

        private static readonly double[] FractionScheduleOffsets =
        {
            0.00, 0.02, -0.02, 0.04, -0.04, 0.06, -0.06, 0.08
        };

        private static readonly LevelDifficultyAnalysisOptions StandardShapeOptions =
            new LevelDifficultyAnalysisOptions
            {
                MaxExactStates = 2500,
                ExactFloorLimit = 36,
                RandomRolloutCount = 4,
                RolloutMaxMoves = 64
            };

        private readonly Solver _solver;
        private readonly LevelQualityScorer _scorer;
        private readonly LevelDifficultyAnalyzer _difficultyAnalyzer;
        private readonly ExtraHardLevelBuilder _extraHardBuilder;
        private readonly MovementSystem _movement = new MovementSystem();
        private readonly object _sketchGate = new object();
        private readonly Dictionary<long, LevelShapeProfile> _sketchMemory = new Dictionary<long, LevelShapeProfile>();

        public LevelGenerator(Solver solver = null, LevelQualityScorer scorer = null)
        {
            _solver = solver ?? new Solver(new MovementSystem());
            _scorer = scorer ?? new LevelQualityScorer(_solver);
            _difficultyAnalyzer = new LevelDifficultyAnalyzer(_solver);
            _extraHardBuilder = new ExtraHardLevelBuilder(_solver, _difficultyAnalyzer);
        }

        public static int VersionFor(Difficulty difficulty) =>
            difficulty >= Difficulty.ExtraHard ? GeneratorVersion : StandardGeneratorVersion;

        public static int ExtraHardOperationBudgetFor(int rows, int cols) =>
            ExtraHardLevelBuilder.OperationBudgetFor(rows, cols);

        public Level Generate(Difficulty difficulty, int levelNumber, LevelGenerationOptions options = null)
        {
            options = options ?? new LevelGenerationOptions();
            if (levelNumber < 1) levelNumber = 1;

            var cfg = DifficultyConfig.For(difficulty);
            int rows = cfg.BoardRowsFor(levelNumber);
            int cols = cfg.BoardColsFor(levelNumber);
            double obstacleFraction = cfg.ObstacleFractionFor(levelNumber);
            if (difficulty >= Difficulty.ExtraHard)
                return GenerateExtraHard(difficulty, levelNumber, rows, cols, obstacleFraction, options);

            int version = SeedVersionFor(difficulty, options);
            IReadOnlyList<LevelShapeProfile> recent = options.RecentShapes ??
                RecentSketches(difficulty, levelNumber, version);
            LevelLayoutFamilyKind preferred = LevelLayoutFamily.Preferred(difficulty, levelNumber);
            Level best = null;
            double bestRank = double.NegativeInfinity;
            int attemptBudget = Math.Max(options.AttemptCount, 1);
            for (int attempt = 0; attempt < attemptBudget; attempt++)
            {
                int seed = LevelSeed.For(difficulty, levelNumber, version, options.AttemptOffset + attempt);
                var rng = new DeterministicRng(seed);
                if (!TrySearchAttempt(
                        rng, rows, cols, obstacleFraction, difficulty, levelNumber, options,
                        preferred, recent, out Level candidate, out double rank))
                    continue;
                if (rank <= bestRank) continue;
                best = candidate;
                bestRank = rank;
            }

            // A repeated shape is rejected above. Spend a few more seeds before
            // the readable recovery so the published board can still differ.
            if (best == null)
            {
                for (int extra = 0; extra < 4; extra++)
                {
                    int seed = LevelSeed.For(
                        difficulty, levelNumber, version, options.AttemptOffset + 50 + extra);
                    var rng = new DeterministicRng(seed);
                    if (!TrySearchAttempt(
                            rng, rows, cols, obstacleFraction, difficulty, levelNumber, options,
                            preferred, recent, out Level candidate, out double rank))
                        continue;
                    best = candidate;
                    bestRank = rank;
                    break;
                }
            }

            return best ?? GenerateReadableFallback(rows, cols, difficulty, levelNumber);
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
            IReadOnlyList<LevelShapeProfile> recent = options.RecentShapes ??
                RecentArenaProfiles(difficulty, levelNumber, rows, cols, version, options.AttemptOffset);

            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                int attemptNumber = options.AttemptOffset + attempt;
                int seed = LevelSeed.For(difficulty, levelNumber, version, attemptNumber);
                var rng = new DeterministicRng(seed);
                Level candidate = _extraHardBuilder.TryGenerate(
                    rng, rows, cols, obstacleFraction, difficulty, levelNumber, seed,
                    version, attemptNumber, recent,
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

            int openAttempt = options.AttemptOffset + 10000;
            int openFallbackSeed = LevelSeed.For(
                difficulty, levelNumber, version, openAttempt);
            Level openFallback = _extraHardBuilder.TryGenerate(
                new DeterministicRng(openFallbackSeed),
                rows, cols, obstacleFraction, difficulty, levelNumber,
                openFallbackSeed, version, openAttempt, recent, out _);
            if (openFallback != null)
                return openFallback;

            throw new InvalidOperationException(
                $"Unable to certify open Extra Hard layout ({rows}x{cols}, " +
                $"{difficulty} #{levelNumber}). Corridor recovery is diagnostic-only.");
        }

        private static int SeedVersionFor(Difficulty difficulty, LevelGenerationOptions options)
        {
            // Default options carry GeneratorVersion. VersionFor then scopes Easy,
            // Medium, and Hard to StandardGeneratorVersion so an Extra Hard bump does
            // not reshuffle them, and the reverse.
            return options.GeneratorVersion == GeneratorVersion
                ? VersionFor(difficulty)
                : options.GeneratorVersion;
        }

        private sealed class ShapeCandidate
        {
            public Level Level;
            public LevelQualityMetrics Quality;
            public LevelSlideStats Slides;
            public LevelLayoutFamilyKind Family;
            public bool Readable;
            public double PreRank;
        }

        private bool TrySearchAttempt(
            DeterministicRng rng,
            int rows,
            int cols,
            double targetFraction,
            Difficulty difficulty,
            int index,
            LevelGenerationOptions options,
            LevelLayoutFamilyKind preferred,
            IReadOnlyList<LevelShapeProfile> recent,
            out Level level,
            out double rank)
        {
            level = null;
            rank = double.NegativeInfinity;
            var readable = new List<ShapeCandidate>(8);

            foreach (double fraction in BuildFractionSchedule(targetFraction))
            {
                for (int inner = 0; inner < InnerCandidatesPerFraction; inner++)
                {
                    Level candidate = TryGenerateBoard(
                        rng, rows, cols, fraction, difficulty, index, preferred, out LevelLayoutFamilyKind family);
                    if (candidate == null) continue;

                    LevelQualityMetrics metrics = _scorer.Score(
                        candidate, difficulty, options.EvaluateMinMoves, options.MinMovesMaxStates);
                    if (!metrics.PassesQualityFloors) continue;

                    LevelSlideStats slides = LevelSlideStats.Measure(candidate, _movement);
                    bool readableSlides = LevelShapeComparison.HasReadableSlides(candidate, slides);
                    var shape = new ShapeCandidate
                    {
                        Level = candidate,
                        Quality = metrics,
                        Slides = slides,
                        Family = family,
                        Readable = readableSlides,
                        PreRank = PreRank(candidate, metrics, slides, family, preferred)
                    };
                    if (readableSlides)
                        KeepBest(readable, shape, 8);
                }
            }

            if (readable.Count == 0) return false;
            List<ShapeCandidate> pool = readable;

            pool.Sort((a, b) => b.PreRank.CompareTo(a.PreRank));
            bool found = false;
            int analyzed = 0;
            for (int i = 0; i < pool.Count && analyzed < 8; i++)
            {
                ShapeCandidate shape = pool[i];
                LevelShapeProfile identity = LevelShapeProfile.FromParts(
                    shape.Level,
                    shape.Slides,
                    shape.Quality.Layout,
                    turns: 0,
                    reversals: 0);
                if (MatchingRecent(identity, recent) != null)
                    continue;

                analyzed++;
                int analysisSeed = LevelSeed.For(
                    difficulty, index, SeedVersionFor(difficulty, options), 8100 + analyzed);
                LevelDifficultyMetrics difficultyMetrics = _difficultyAnalyzer.Analyze(
                    shape.Level, analysisSeed, StandardShapeOptions);
                LevelShapeProfile ranked = LevelShapeProfile.FromParts(
                    shape.Level,
                    shape.Slides,
                    shape.Quality.Layout,
                    difficultyMetrics.Turns,
                    difficultyMetrics.Reversals);
                if (LevelShapeComparison.IsRejected(ranked, recent))
                    continue;

                double candidateRank = RankStandard(
                    shape.Level, shape.Quality, shape.Slides, difficultyMetrics, difficulty, index,
                    shape.Family, preferred, recent, ranked);
                if (candidateRank <= rank) continue;
                level = shape.Level;
                rank = candidateRank;
                found = true;
            }

            return found;
        }

        private static LevelShapeProfile MatchingRecent(
            LevelShapeProfile profile,
            IReadOnlyList<LevelShapeProfile> recent)
        {
            if (recent == null) return null;
            foreach (LevelShapeProfile past in recent)
            {
                if (LevelShapeComparison.SameBoard(profile, past)) return past;
            }

            return null;
        }

        private static void KeepBest(List<ShapeCandidate> pool, ShapeCandidate candidate, int limit)
        {
            if (pool.Count < limit)
            {
                pool.Add(candidate);
                return;
            }

            int worst = 0;
            for (int i = 1; i < pool.Count; i++)
            {
                if (pool[i].PreRank < pool[worst].PreRank) worst = i;
            }

            if (candidate.PreRank > pool[worst].PreRank)
                pool[worst] = candidate;
        }

        private static double PreRank(
            Level level,
            LevelQualityMetrics quality,
            LevelSlideStats slides,
            LevelLayoutFamilyKind family,
            LevelLayoutFamilyKind preferred)
        {
            double rank = slides.LengthStdDev * 3.0;
            if (slides.OneCellRatio < 0.45) rank += 2.0;
            if (slides.PocketStopCount > 0) rank += 2.0;
            if (family == preferred) rank += 1.5;
            if (quality.IsolatedWallRatio < 0.2) rank += 1.0;
            if (level.TotalPaintable > 0 && slides.Longest > level.TotalPaintable * 0.5)
                rank -= 3.0;
            rank += LevelShapeComparison.StyleBonus(level, quality) * 0.35;
            return rank;
        }

        private static double RankStandard(
            Level level,
            LevelQualityMetrics quality,
            LevelSlideStats slides,
            LevelDifficultyMetrics metrics,
            Difficulty difficulty,
            int index,
            LevelLayoutFamilyKind family,
            LevelLayoutFamilyKind preferred,
            IReadOnlyList<LevelShapeProfile> recent,
            LevelShapeProfile profile)
        {
            double target = LevelDifficultyAnalyzer.TargetScore(difficulty, index);
            double rank = -Math.Abs(metrics.Score - target);
            rank += metrics.Reversals > 0 ? 3.0 : -2.5;
            rank += Math.Min(5.0, slides.LengthStdDev);
            if (metrics.RandomFailureRate >= 0.15 && metrics.RandomFailureRate < 0.95)
                rank += 2.0 + metrics.RandomFailureRate;
            else if (metrics.RandomFailureRate < 0.05)
                rank -= 1.5;
            if (family == preferred) rank += 1.25;
            rank += LevelShapeComparison.StyleBonus(level, quality);
            rank -= LevelShapeComparison.SimilarityPenalty(profile, recent);
            return rank;
        }

        private Level TryGenerateBoard(
            DeterministicRng rng,
            int rows,
            int cols,
            double obstacleFraction,
            Difficulty difficulty,
            int index,
            LevelLayoutFamilyKind preferred,
            out LevelLayoutFamilyKind family)
        {
            family = preferred;
            bool[,] mask = TryBuildIrregularMask(rows, cols, rng);
            if (mask == null) return null;

            var grid = MaskToGrid(mask, rows, cols);
            int rolled = rng.Next(LevelLayoutFamily.Count);
            family = rng.NextDouble() < 0.74 ? preferred : (LevelLayoutFamilyKind)rolled;
            if (!TryPlaceGrammar(grid, mask, rows, cols, rng, obstacleFraction, family))
            {
                family = LevelLayoutFamilyKind.ShortBarScatter;
                ResetMaskFloors(grid, mask, rows, cols);
                if (!TryScatterWalls(grid, mask, rows, cols, rng, obstacleFraction, false))
                    return null;
            }

            LevelTopology.SealEnclosedVoid(grid);
            if (!LevelTopology.HasExteriorVoid(grid)) return null;
            if (!IsFloorConnected(grid, rows, cols)) return null;

            List<Position> floors = CollectFloors(grid, rows, cols);
            if (floors.Count == 0) return null;

            rng.Shuffle(floors);
            int spawnTries = Math.Min(SpawnTriesFor(obstacleFraction), floors.Count);
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

        private static bool TryPlaceGrammar(
            Tile[,] grid,
            bool[,] mask,
            int rows,
            int cols,
            DeterministicRng rng,
            double obstacleFraction,
            LevelLayoutFamilyKind family)
        {
            bool placed = family switch
            {
                LevelLayoutFamilyKind.RoomWithGap => TryPlaceRoom(grid, mask, rows, cols, rng),
                LevelLayoutFamilyKind.LongDivider => TryPlaceDivider(grid, mask, rows, cols, rng),
                LevelLayoutFamilyKind.WallIsland => TryPlaceIsland(grid, mask, rows, cols, rng),
                LevelLayoutFamilyKind.SidePocket => TryPlacePocket(grid, mask, rows, cols, rng),
                _ => TryScatterWalls(grid, mask, rows, cols, rng, obstacleFraction, false)
            };
            return placed && IsFloorConnected(grid, rows, cols);
        }

        private static bool TryPlaceRoom(
            Tile[,] grid, bool[,] mask, int rows, int cols, DeterministicRng rng)
        {
            if (!TryMaskBounds(mask, rows, cols, out int minR, out int maxR, out int minC, out int maxC))
                return false;
            int height = Math.Min(4, maxR - minR + 1);
            int width = Math.Min(4, maxC - minC + 1);
            if (height < 4 || width < 4) return false;
            int rowSpan = maxR - minR + 1 - (height - 1);
            int colSpan = maxC - minC + 1 - (width - 1);
            if (rowSpan <= 0 || colSpan <= 0) return false;
            int r0 = minR + rng.Next(rowSpan);
            int c0 = minC + rng.Next(colSpan);
            int r1 = r0 + height - 1;
            int c1 = c0 + width - 1;

            var perimeter = new List<Position>();
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    bool edge = r == r0 || r == r1 || c == c0 || c == c1;
                    if (!edge || !mask[r, c]) continue;
                    perimeter.Add(new Position(r, c));
                }
            }

            if (perimeter.Count < 8) return false;
            var placed = new List<Position>(perimeter.Count);
            foreach (Position cell in perimeter)
            {
                grid[cell.Row, cell.Col] = Tile.Wall;
                placed.Add(cell);
            }

            Position gap = perimeter[rng.Next(perimeter.Count)];
            grid[gap.Row, gap.Col] = Tile.Floor;
            placed.Remove(gap);
            if (IsFloorConnected(grid, rows, cols) && HasInteriorFloor(grid, r0, c0, r1, c1))
                return true;
            RestoreFloors(grid, placed);
            return false;
        }

        private static bool TryPlaceDivider(
            Tile[,] grid, bool[,] mask, int rows, int cols, DeterministicRng rng)
        {
            if (!TryMaskBounds(mask, rows, cols, out int minR, out int maxR, out int minC, out int maxC))
                return false;
            bool horizontal = rng.Next(2) == 0;
            var line = new List<Position>();
            if (horizontal)
            {
                if (maxR - minR < 2) return false;
                int row = minR + 1 + rng.Next(maxR - minR - 1);
                for (int c = minC; c <= maxC; c++)
                {
                    if (mask[row, c]) line.Add(new Position(row, c));
                }
            }
            else
            {
                if (maxC - minC < 2) return false;
                int col = minC + 1 + rng.Next(maxC - minC - 1);
                for (int r = minR; r <= maxR; r++)
                {
                    if (mask[r, col]) line.Add(new Position(r, col));
                }
            }

            if (line.Count < 4) return false;
            int gap = line.Count / 3;
            int gapSpan = Math.Max(1, line.Count / 3);
            gap = Math.Min(line.Count - 1, gap + rng.Next(gapSpan));
            var placed = new List<Position>();
            for (int i = 0; i < line.Count; i++)
            {
                if (i == gap) continue;
                Position cell = line[i];
                grid[cell.Row, cell.Col] = Tile.Wall;
                placed.Add(cell);
            }

            if (IsFloorConnected(grid, rows, cols)) return true;
            RestoreFloors(grid, placed);
            return false;
        }

        private static bool TryPlaceIsland(
            Tile[,] grid, bool[,] mask, int rows, int cols, DeterministicRng rng)
        {
            if (!TryMaskBounds(mask, rows, cols, out int minR, out int maxR, out int minC, out int maxC))
                return false;
            int height = 2;
            int width = rng.Next(2) == 0 ? 2 : 3;
            if (rng.Next(2) == 0)
            {
                int swap = height;
                height = width;
                width = swap;
            }

            int rowSpan = maxR - minR + 1 - (height - 1);
            int colSpan = maxC - minC + 1 - (width - 1);
            if (rowSpan <= 0 || colSpan <= 0) return false;
            int r0 = minR + rng.Next(rowSpan);
            int c0 = minC + rng.Next(colSpan);
            var placed = new List<Position>();
            for (int r = r0; r < r0 + height; r++)
            {
                for (int c = c0; c < c0 + width; c++)
                {
                    if (!mask[r, c]) continue;
                    grid[r, c] = Tile.Wall;
                    placed.Add(new Position(r, c));
                }
            }

            if (placed.Count >= 4 && rng.Next(2) == 0)
            {
                Position corner = placed[placed.Count - 1];
                grid[corner.Row, corner.Col] = Tile.Floor;
                placed.RemoveAt(placed.Count - 1);
            }

            if (placed.Count >= 4 && IsFloorConnected(grid, rows, cols)) return true;
            RestoreFloors(grid, placed);
            return false;
        }

        private static bool TryPlacePocket(
            Tile[,] grid, bool[,] mask, int rows, int cols, DeterministicRng rng)
        {
            var origins = new List<Position>();
            for (int r = 0; r + 2 < rows; r++)
            {
                for (int c = 0; c + 2 < cols; c++)
                {
                    if (BlockIsMask(mask, r, c, 3, 3))
                        origins.Add(new Position(r, c));
                }
            }

            if (origins.Count == 0) return false;
            rng.Shuffle(origins);
            int tries = Math.Min(6, origins.Count);
            for (int i = 0; i < tries; i++)
            {
                Position origin = origins[i];
                int opening = rng.Next(4);
                var placed = new List<Position>();
                for (int dr = 0; dr < 3; dr++)
                {
                    for (int dc = 0; dc < 3; dc++)
                    {
                        bool center = dr == 1 && dc == 1;
                        bool mouth = opening switch
                        {
                            0 => dr == 0 && dc == 1,
                            1 => dr == 2 && dc == 1,
                            2 => dr == 1 && dc == 0,
                            _ => dr == 1 && dc == 2
                        };
                        if (center || mouth) continue;
                        int r = origin.Row + dr;
                        int c = origin.Col + dc;
                        grid[r, c] = Tile.Wall;
                        placed.Add(new Position(r, c));
                    }
                }

                if (IsFloorConnected(grid, rows, cols)) return true;
                RestoreFloors(grid, placed);
            }

            return false;
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

        private static void ResetMaskFloors(Tile[,] grid, bool[,] mask, int rows, int cols)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                    grid[r, c] = mask[r, c] ? Tile.Floor : Tile.Void;
            }
        }

        private static void RestoreFloors(Tile[,] grid, List<Position> placed)
        {
            foreach (Position cell in placed)
                grid[cell.Row, cell.Col] = Tile.Floor;
        }

        private static bool TryMaskBounds(
            bool[,] mask, int rows, int cols,
            out int minR, out int maxR, out int minC, out int maxC)
        {
            minR = rows;
            maxR = -1;
            minC = cols;
            maxC = -1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!mask[r, c]) continue;
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    if (c < minC) minC = c;
                    if (c > maxC) maxC = c;
                }
            }

            return maxR >= minR && maxC >= minC;
        }

        private static bool HasInteriorFloor(Tile[,] grid, int r0, int c0, int r1, int c1)
        {
            int floors = 0;
            for (int r = r0 + 1; r < r1; r++)
            {
                for (int c = c0 + 1; c < c1; c++)
                {
                    if (grid[r, c] == Tile.Floor) floors++;
                }
            }

            return floors >= 2;
        }

        private static bool BlockIsMask(bool[,] mask, int row, int col, int height, int width)
        {
            for (int r = row; r < row + height; r++)
            {
                for (int c = col; c < col + width; c++)
                {
                    if (!mask[r, c]) return false;
                }
            }

            return true;
        }

        private IReadOnlyList<LevelShapeProfile> RecentSketches(
            Difficulty difficulty, int levelNumber, int version)
        {
            var recent = new List<LevelShapeProfile>();
            int start = Math.Max(1, levelNumber - LevelShapeComparison.RecentWindow);
            for (int i = start; i < levelNumber; i++)
            {
                LevelShapeProfile profile = HistoryProfile(difficulty, i, version);
                if (profile != null) recent.Add(profile);
            }

            return recent;
        }

        /// <summary>
        /// Board this index publishes when judged only against unconstrained
        /// sketches. One step, not a chain back to level 1, and close to the
        /// board a player actually received for that index.
        /// </summary>
        private LevelShapeProfile HistoryProfile(Difficulty difficulty, int index, int version)
        {
            long key = SketchKey(version, difficulty, index) ^ (1L << 62);
            lock (_sketchGate)
            {
                if (_sketchMemory.TryGetValue(key, out LevelShapeProfile cached))
                    return cached;
            }

            var recent = new List<LevelShapeProfile>();
            int start = Math.Max(1, index - LevelShapeComparison.RecentWindow);
            for (int i = start; i < index; i++)
            {
                LevelShapeProfile sketch = SketchProfile(difficulty, i, version);
                if (sketch != null) recent.Add(sketch);
            }

            Level level = Generate(difficulty, index, new LevelGenerationOptions
            {
                AttemptCount = 1,
                RecentShapes = recent
            });
            LevelShapeProfile profile = level == null ? null : LevelShapeProfile.Capture(level);
            lock (_sketchGate)
            {
                _sketchMemory[key] = profile;
            }

            return profile;
        }

        private LevelShapeProfile SketchProfile(Difficulty difficulty, int index, int version)
        {
            long key = SketchKey(version, difficulty, index);
            lock (_sketchGate)
            {
                if (_sketchMemory.TryGetValue(key, out LevelShapeProfile cached))
                    return cached;
            }

            // Unconstrained search for this index. Used only as the one-step
            // history input so publication stays order-independent.
            Level sketch = Generate(difficulty, index, new LevelGenerationOptions
            {
                AttemptCount = 1,
                RecentShapes = Array.Empty<LevelShapeProfile>()
            });
            LevelShapeProfile profile = sketch == null ? null : LevelShapeProfile.Capture(sketch);
            lock (_sketchGate)
            {
                _sketchMemory[key] = profile;
            }

            return profile;
        }

        private static long SketchKey(int version, Difficulty difficulty, int index) =>
            ((long)version << 48) ^ ((long)(int)difficulty << 32) ^ (uint)index;

        private static List<LevelShapeProfile> RecentArenaProfiles(
            Difficulty difficulty,
            int levelNumber,
            int rows,
            int cols,
            int version,
            int attempt)
        {
            var recent = new List<LevelShapeProfile>();
            var config = DifficultyConfig.For(difficulty);
            int start = Math.Max(1, levelNumber - LevelShapeComparison.RecentWindow);
            for (int i = start; i < levelNumber; i++)
            {
                if (config.BoardRowsFor(i) != rows || config.BoardColsFor(i) != cols)
                    continue;
                int variant = ExtraHardTemplateVariety.PublishedVariant(
                    difficulty, i, rows, cols, version, attempt);
                Tile[,] grid = ArenaExtraHardTemplateBank.Build(rows, cols, variant, out Position spawn);
                recent.Add(LevelShapeProfile.Capture(new Level(grid, spawn, difficulty, i)));
            }

            return recent;
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
        /// Solvable recovery that is not a serpentine corridor. Used when a normal
        /// Easy, Medium, or Hard search accepts nothing.
        /// </summary>
        public Level GenerateReadableFallback(int rows, int cols, Difficulty difficulty, int index)
        {
            LevelLayoutFamilyKind preferred = LevelLayoutFamily.Preferred(difficulty, index);
            double fraction = DifficultyConfig.For(difficulty).ObstacleFractionFor(index);
            for (int attempt = 0; attempt < 16; attempt++)
            {
                int seed = LevelSeed.For(
                    difficulty, index, VersionFor(difficulty), 8800 + attempt);
                var rng = new DeterministicRng(seed);
                Level candidate = TryGenerateBoard(
                    rng, rows, cols, fraction, difficulty, index, preferred, out _);
                if (candidate == null) continue;
                if (LevelQualityScorer.LooksLikeSerpentine(candidate.Grid, rows, cols)) continue;
                LevelQualityMetrics quality = _scorer.Score(candidate, difficulty);
                if (!quality.PassesQualityFloors) continue;
                if (!LevelSafetyValidator.IsSafe(candidate, _solver, out _)) continue;
                return candidate;
            }

            return BuildReadableRecovery(rows, cols, difficulty, index);
        }

        /// <summary>
        /// Last recovery when grammar search accepts nothing. A bent corridor is
        /// tried first. Vertical lanes are the guarantee: the row detector does not
        /// treat them as the serpentine corridor, and one bridge keeps the route
        /// from being a single file of columns.
        /// </summary>
        private Level BuildReadableRecovery(int rows, int cols, Difficulty difficulty, int index)
        {
            int seed = LevelSeed.For(difficulty, index, VersionFor(difficulty), 9700);
            var rng = new DeterministicRng(seed);
            for (int variant = 0; variant < 4; variant++)
            {
                Tile[,] grid = BuildHamiltonianCorridorGrid(rows, cols, rng, out Position spawn);
                var bent = new Level(grid, spawn, difficulty, index);
                if (IsReadableRecovery(bent, difficulty))
                    return bent;
            }

            Tile[,] lanes = BuildColumnLaneGrid(rows, cols);
            ApplyColumnLaneVoids(lanes, rows, cols, difficulty, index);
            LevelTopology.SealEnclosedVoid(lanes);
            Tile[,] withoutBridge = CloneGrid(lanes);
            TryOpenLaneBridge(lanes, rows, cols, difficulty, index);
            Level selected = SelectFirstSolvableSpawn(lanes, rows, cols, difficulty, index);
            if (selected != null && IsReadableRecovery(selected, difficulty))
                return selected;

            RestoreGrid(withoutBridge, lanes);
            selected = SelectFirstSolvableSpawn(lanes, rows, cols, difficulty, index);
            if (selected != null && IsReadableRecovery(selected, difficulty))
                return selected;

            throw new InvalidOperationException(
                $"Unable to build a readable fallback ({rows}x{cols}, {difficulty} #{index}).");
        }

        private bool IsReadableRecovery(Level level, Difficulty difficulty)
        {
            if (level == null) return false;
            if (!LevelTopology.HasExteriorVoid(level.Grid)) return false;
            if (!LevelTopology.HasOnlyExteriorVoid(level)) return false;
            LevelQualityMetrics quality = _scorer.Score(level, difficulty);
            if (!quality.PassesQualityFloors) return false;
            LevelSlideStats slides = LevelSlideStats.Measure(level, _movement);
            return LevelShapeComparison.HasReadableSlides(level, slides) &&
                   LevelSafetyValidator.IsSafe(level, _solver, out _);
        }

        /// <summary>
        /// Even columns are open lanes. Odd columns are walls with a single
        /// connector at one end, so the route turns and comes back.
        /// </summary>
        private static Tile[,] BuildColumnLaneGrid(int rows, int cols)
        {
            var grid = new Tile[rows, cols];
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (c % 2 == 0)
                    {
                        grid[r, c] = Tile.Floor;
                        continue;
                    }

                    int connectorRow = ((c / 2) % 2 == 0) ? rows - 1 : 0;
                    grid[r, c] = r == connectorRow ? Tile.Floor : Tile.Wall;
                }
            }

            return grid;
        }

        private static void ApplyColumnLaneVoids(
            Tile[,] grid, int rows, int cols, Difficulty difficulty, int index)
        {
            var walls = new List<(int r, int c)>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    bool border = r == 0 || c == 0 || r == rows - 1 || c == cols - 1;
                    if (border && grid[r, c] == Tile.Wall)
                        walls.Add((r, c));
                }
            }

            if (walls.Count == 0) return;
            int span = Math.Max(1, walls.Count / 2);
            int start = Math.Abs(index * 5 + (int)difficulty * 3) % walls.Count;
            for (int i = 0; i < span; i++)
            {
                (int row, int col) = walls[(start + i) % walls.Count];
                grid[row, col] = Tile.Void;
            }
        }

        /// <summary>
        /// Opens the walled end of one odd column so two lanes join there.
        /// The ball has to slide across and come back, and the join depends on
        /// the level index so recoveries of the same size are not one puzzle.
        /// </summary>
        private void TryOpenLaneBridge(
            Tile[,] grid, int rows, int cols, Difficulty difficulty, int index)
        {
            var bridges = new List<(int row, int col)>();
            for (int col = 1; col < cols; col += 2)
            {
                int connectorRow = ((col / 2) % 2 == 0) ? rows - 1 : 0;
                int bridgeRow = connectorRow == 0 ? rows - 1 : 0;
                if (grid[bridgeRow, col] == Tile.Wall)
                    bridges.Add((bridgeRow, col));
            }

            if (bridges.Count == 0) return;
            (int row, int column) = bridges[Math.Abs(index) % bridges.Count];
            grid[row, column] = Tile.Floor;
            if (IsFloorConnected(grid, rows, cols) &&
                HasAnySolvableSpawn(grid, rows, cols, difficulty, index))
                return;

            grid[row, column] = Tile.Wall;
        }

        private Level SelectFirstSolvableSpawn(
            Tile[,] grid, int rows, int cols, Difficulty difficulty, int index)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] != Tile.Floor) continue;
                    var candidate = new Level(grid, new Position(r, c), difficulty, index);
                    if (_solver.IsAlwaysSolvable(candidate))
                        return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Guaranteed-valid fallback used when irregular generation fails. Varies structurally
        /// by difficulty and index so canonical fingerprints stay distinct.
        /// Diagnostic serpentine recovery stays on this path and is not a normal publication.
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
                int attemptNumber = 30000 + variant * 4 + retry;
                int seed = LevelSeed.For(
                    difficulty, index, VersionFor(difficulty), attemptNumber);
                Level candidate = _extraHardBuilder.TryGenerate(
                    new DeterministicRng(seed),
                    rows,
                    cols,
                    DifficultyConfig.For(difficulty).ObstacleFraction,
                    difficulty,
                    index,
                    seed,
                    VersionFor(difficulty),
                    attemptNumber,
                    null,
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
