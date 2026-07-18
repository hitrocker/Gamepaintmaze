using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PaintMaze.Core;
using PaintMaze.Domain;
using UnityEditor;
using UnityEngine;

namespace PaintMaze.EditorTools
{
    /// <summary>
    /// Offline bake for the unlimited level system. Generates deterministic sectioned
    /// packs, metrics CSV, and review contact sheets. Run headless via:
    ///   Unity -batchmode -quit -projectPath . -executeMethod PaintMaze.EditorTools.LevelBakeTool.BakeAll
    /// </summary>
    public static class LevelBakeTool
    {
        private const int LevelsPerMode = 500;
        private const int LevelsPerSection = 50;
        private const int BakeAttemptCount = 8;
        private const int MaxOffsetBlocks = 128;
        private const int PreviewSamplesPerBand = 24;
        private const int ExtraHardExtensionSourceStart = 501;
        private const int ExtraHardExtensionSourceEnd = 4500;
        private const int ExtraHardExtensionWorkers = 4;
        private const string BakedRelativePath = "Assets/Resources/Levels/Baked";
        private const string MetricsRelativePath = "build/level-reports/level_metrics.csv";
        private const string PreviewsRelativePath = "build/level-previews";
        private const string PreviewCheckpointRelativePath = "build/extra-hard-preview";
        private const string ExtensionCacheRelativePath = "build/extra-hard-extension-cache";

        private static readonly Color32 FloorColor = new Color32(235, 235, 235, 255);
        private static readonly Color32 WallColor = new Color32(58, 42, 32, 255);
        private static readonly Color32 VoidColor = new Color32(36, 92, 168, 255);
        private static readonly Color32 SpawnColor = new Color32(255, 196, 48, 255);
        private static readonly Color32 SheetBackground = new Color32(18, 18, 22, 255);
        private static readonly Color32 ThumbBorder = new Color32(70, 70, 78, 255);

        private sealed class PreviewBand
        {
            public Difficulty Difficulty;
            public int Start;
            public int Rows;
            public int Cols;
        }

        private sealed class PreviewSample
        {
            public PreviewBand Band;
            public int Index;
            public BakeResult Result;
        }

        private static readonly PreviewBand[] ArenaPreviewBands =
        {
            new PreviewBand
                { Difficulty = Difficulty.ExtraHard, Start = 1, Rows = 9, Cols = 9 },
            new PreviewBand
                { Difficulty = Difficulty.ExtraHard, Start = 126, Rows = 10, Cols = 10 },
            new PreviewBand
                { Difficulty = Difficulty.ExtraHard, Start = 251, Rows = 12, Cols = 14 },
            new PreviewBand
                { Difficulty = Difficulty.ExtraHard, Start = 376, Rows = 14, Cols = 16 },
            new PreviewBand
                { Difficulty = Difficulty.UltraHard, Start = 126, Rows = 14, Cols = 18 },
            new PreviewBand
                { Difficulty = Difficulty.UltraHard, Start = 251, Rows = 16, Cols = 18 },
            new PreviewBand
                { Difficulty = Difficulty.UltraHard, Start = 376, Rows = 16, Cols = 20 },
            new PreviewBand
                { Difficulty = Difficulty.UltraHard, Start = 501, Rows = 16, Cols = 22 }
        };

        [MenuItem("Paint Maze/Bake All Levels")]
        public static void BakeAll()
        {
            try
            {
                RunBake(extraHardOnly: false);
                RunExtraHardExtension();
                Debug.Log("[LevelBake] Bake completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelBake] Bake failed: " + ex.Message);
                EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Paint Maze/Bake Extra Hard Levels")]
        public static void BakeExtraHardCatalogs()
        {
            try
            {
                RunBake(extraHardOnly: true);
                RunExtraHardExtension();
                Debug.Log("[LevelBake] Extra Hard bake completed successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelBake] Extra Hard bake failed: " + ex.Message);
                EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Paint Maze/Extend Extra Hard Through 5000")]
        public static void BakeExtraHardExtension()
        {
            try
            {
                RunExtraHardExtension();
                Debug.Log("[LevelBake] Extra Hard extension completed successfully.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelBake] Extra Hard extension failed: " + ex);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Paint Maze/Bake Extra Hard Preview Checkpoint")]
        public static void BakeExtraHardPreviewCheckpoint()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.Combine(projectRoot, PreviewCheckpointRelativePath);
                RecreateDirectory(outputPath);

                var generator = new LevelGenerator();
                var solver = new Solver();
                var scorer = new LevelQualityScorer(solver);
                var analyzer = new LevelDifficultyAnalyzer(solver);
                var registry = new LevelFingerprintRegistry();
                var metricsRows = new List<string>
                {
                    "mode,index,fingerprint,rows,cols,floors,walls,voids,stops,branching,isolation,perimeter,score," +
                    "open_quads,open_cells,open_ratio,junctions,degree2_ratio,cycle_rank,longest_run,long_lane_ratio," +
                    "open_core_count,open_core_cells,open_core_ratio,max_clearance,degree4_ratio,largest_core_ratio," +
                    "wall_islands,wall_island_ratio,parallel_separator_ratio,exterior_cut_depth,concave_corners," +
                    "choice_stops,choice_ratio," +
                    "min_moves,turns,reversals,revisits,paint_per_move,forced_ratio,random_best,random_average," +
                    "random_worst,random_failed,random_spread,movement_choice_ratio,movement_cycle_rank," +
                    "difficulty_score,target_score"
                };

                var allSamples = new List<PreviewSample>(
                    ArenaPreviewBands.Length * PreviewSamplesPerBand);
                var distributionRows = new List<string>
                {
                    "mode,start,rows,cols,count,core_ratio_min,core_ratio_median,core_ratio_max," +
                    "clearance_min,clearance_median,clearance_max,separator_min,separator_median,separator_max"
                };

                foreach (PreviewBand band in ArenaPreviewBands)
                {
                    var levels = new List<Level>(PreviewSamplesPerBand);
                    var bandSamples = new List<PreviewSample>(PreviewSamplesPerBand);
                    for (int offset = 0; offset < PreviewSamplesPerBand; offset++)
                    {
                        int index = band.Start + offset;
                        BakeResult result = GeneratePreviewAcceptedLevel(
                            generator, solver, scorer, analyzer, registry,
                            band.Difficulty, index);
                        if (result.Level.Rows != band.Rows ||
                            result.Level.Cols != band.Cols)
                        {
                            throw new InvalidOperationException(
                                $"{band.Difficulty} preview #{index} produced " +
                                $"{result.Level.Rows}x{result.Level.Cols}, expected " +
                                $"{band.Rows}x{band.Cols}.");
                        }
                        if (result.DuplicateRetries + result.ValidationRetries > 8)
                        {
                            throw new InvalidOperationException(
                                $"{band.Difficulty} preview #{index} required " +
                                $"{result.DuplicateRetries} duplicate and " +
                                $"{result.ValidationRetries} validation retries.");
                        }
                        levels.Add(result.Level);
                        var sample = new PreviewSample
                        {
                            Band = band,
                            Index = index,
                            Result = result
                        };
                        bandSamples.Add(sample);
                        allSamples.Add(sample);
                        metricsRows.Add(MetricsRow(
                            band.Difficulty, index, result.Level, result.Fingerprint,
                            result.Metrics, result.DifficultyMetrics));
                    }
                    distributionRows.Add(DistributionRow(band, bandSamples));
                    string fileName =
                        $"{ModeSlug(band.Difficulty)}_{band.Start:D4}_" +
                        $"{band.Start + PreviewSamplesPerBand - 1:D4}_" +
                        $"{band.Rows}x{band.Cols}.png";
                    WriteContactSheet(
                        Path.Combine(outputPath, fileName),
                        levels,
                        $"{band.Difficulty} {band.Rows}X{band.Cols} " +
                        $"{band.Start:D4}-{band.Start + PreviewSamplesPerBand - 1:D4}",
                        6,
                        7);
                }

                File.WriteAllText(
                    Path.Combine(outputPath, "preview_metrics.csv"),
                    string.Join("\n", metricsRows) + "\n",
                    Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(outputPath, "preview_distributions.csv"),
                    string.Join("\n", distributionRows) + "\n",
                    Encoding.UTF8);
                List<Level> worstLevels = SelectWorstPreviewLevels(allSamples);
                WriteContactSheet(
                    Path.Combine(outputPath, "worst_arena_metrics.png"),
                    worstLevels,
                    "WORST OPEN CORE CLEARANCE SEPARATORS",
                    4,
                    8);
                Debug.Log(
                    $"[LevelPreview] Accepted {registry.Count} unique metric-gated boards in " +
                    PreviewCheckpointRelativePath);
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelPreview] Preview failed: " + ex.Message);
                EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Paint Maze/Bake Extra Hard High-Risk Spot Check")]
        public static void BakeExtraHardHighRiskSpotCheck()
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputPath = Path.Combine(projectRoot, "build/extra-hard-spot-check");
                RecreateDirectory(outputPath);
                var generator = new LevelGenerator();
                var solver = new Solver();
                var scorer = new LevelQualityScorer(solver);
                var analyzer = new LevelDifficultyAnalyzer(solver);
                var registry = new LevelFingerprintRegistry();
                var levels = new List<Level>(30);
                for (int index = 501; index <= 530; index++)
                {
                    BakeResult result = GeneratePreviewAcceptedLevel(
                        generator, solver, scorer, analyzer, registry,
                        Difficulty.UltraHard, index);
                    levels.Add(result.Level);
                }

                WriteContactSheet(
                    Path.Combine(outputPath, "ultrahard_0501_0530.png"),
                    levels,
                    "UltraHard spot check 0501-0530");
                Debug.Log("[LevelPreview] High-risk spot check accepted 30 boards.");
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelPreview] Spot check failed: " + ex.Message);
                EditorApplication.Exit(1);
                throw;
            }
        }

        [MenuItem("Paint Maze/Bake Open Runtime Fallback Bank")]
        public static void BakeOpenRuntimeFallbackBank()
        {
            try
            {
                var generator = new LevelGenerator();
                var solver = new Solver();
                var scorer = new LevelQualityScorer(solver);
                var specs = new[]
                {
                    new { Rows = 9, Cols = 9, Difficulty = Difficulty.ExtraHard, Index = 1 },
                    new { Rows = 10, Cols = 10, Difficulty = Difficulty.ExtraHard, Index = 126 },
                    new { Rows = 12, Cols = 14, Difficulty = Difficulty.ExtraHard, Index = 251 },
                    new { Rows = 14, Cols = 16, Difficulty = Difficulty.ExtraHard, Index = 376 },
                    new { Rows = 14, Cols = 18, Difficulty = Difficulty.UltraHard, Index = 126 },
                    new { Rows = 16, Cols = 18, Difficulty = Difficulty.UltraHard, Index = 251 },
                    new { Rows = 16, Cols = 20, Difficulty = Difficulty.UltraHard, Index = 376 },
                    new { Rows = 16, Cols = 22, Difficulty = Difficulty.UltraHard, Index = 501 }
                };
                string directory = Path.Combine(
                    Application.dataPath, "Resources/Levels/Fallback");
                Directory.CreateDirectory(directory);
                foreach (var spec in specs)
                {
                    Level level = generator.GenerateCertifiedOpenFallback(
                        spec.Rows, spec.Cols, spec.Difficulty, spec.Index);
                    LevelQualityMetrics quality = scorer.Score(
                        level, spec.Difficulty);
                    bool safe = LevelSafetyValidator.IsSafe(
                        level, solver, out string safety);
                    bool open = LevelLayoutAnalyzer.MeetsExtraHardFloor(
                        level, quality.Layout, out string layout);
                    if (!safe || !open)
                    {
                        throw new InvalidOperationException(
                            $"Fallback template {spec.Rows}x{spec.Cols} " +
                            $"failed certification: " +
                            (safety ?? layout));
                    }
                    File.WriteAllText(
                        Path.Combine(
                            directory, $"open_{spec.Rows}x{spec.Cols}.txt"),
                        LevelPackSerializer.ToPack(new[] { level }),
                        Encoding.UTF8);
                }
                AssetDatabase.Refresh();
                Debug.Log("[LevelFallback] Wrote 8 certified arena templates.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelFallback] Fallback bank failed: " + ex.Message);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void RunBake(bool extraHardOnly)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string bakedPath = Path.Combine(Application.dataPath, "Resources/Levels/Baked");
            string metricsPath = Path.Combine(projectRoot, MetricsRelativePath);
            string previewsPath = Path.Combine(projectRoot, PreviewsRelativePath);

            if (extraHardOnly)
            {
                Directory.CreateDirectory(bakedPath);
                Directory.CreateDirectory(previewsPath);
            }
            else
            {
                RecreateDirectory(bakedPath);
                RecreateDirectory(previewsPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(metricsPath));

            var generator = new LevelGenerator();
            var solver = new Solver();
            var scorer = new LevelQualityScorer(solver);
            var analyzer = new LevelDifficultyAnalyzer(solver);
            var registry = new LevelFingerprintRegistry();
            var metricsRows = new List<string>(LevelsPerMode * 5 + 1)
            {
                "mode,index,fingerprint,rows,cols,floors,walls,voids,stops,branching,isolation,perimeter,score," +
                "open_quads,open_cells,open_ratio,junctions,degree2_ratio,cycle_rank,longest_run,long_lane_ratio," +
                "open_core_count,open_core_cells,open_core_ratio,max_clearance,degree4_ratio,largest_core_ratio," +
                "wall_islands,wall_island_ratio,parallel_separator_ratio,exterior_cut_depth,concave_corners," +
                "choice_stops,choice_ratio," +
                "min_moves,turns,reversals,revisits,paint_per_move,forced_ratio,random_best,random_average," +
                "random_worst,random_failed,random_spread,movement_choice_ratio,movement_cycle_rank," +
                "difficulty_score,target_score"
            };

            int duplicateRetries = 0;
            int validationRetries = 0;
            int totalAccepted = 0;

            if (extraHardOnly)
                LoadUntouchedCatalogs(bakedPath, solver, scorer, registry, metricsRows);

            var difficulties = extraHardOnly
                ? new[] { Difficulty.ExtraHard, Difficulty.UltraHard }
                : (Difficulty[])Enum.GetValues(typeof(Difficulty));
            foreach (Difficulty difficulty in difficulties)
            {
                var accepted = new List<Level>(LevelsPerMode);

                for (int index = 1; index <= LevelsPerMode; index++)
                {
                    BakeResult result = GenerateAcceptedLevel(
                        generator, solver, scorer, analyzer, registry, difficulty, index);
                    duplicateRetries += result.DuplicateRetries;
                    validationRetries += result.ValidationRetries;
                    accepted.Add(result.Level);
                    totalAccepted++;

                    metricsRows.Add(MetricsRow(
                        difficulty, index, result.Level, result.Fingerprint,
                        result.Metrics, result.DifficultyMetrics));

                    if (index % 25 == 0 || index == LevelsPerMode)
                    {
                        Debug.Log($"[LevelBake] {difficulty}: {index}/{LevelsPerMode} accepted " +
                                  $"(global={registry.Count})");
                    }
                }

                if (accepted.Count != LevelsPerMode)
                {
                    throw new InvalidOperationException(
                        $"{difficulty} produced {accepted.Count} levels, expected {LevelsPerMode}.");
                }

                WriteSections(bakedPath, difficulty, accepted);
                WriteSectionPreviews(previewsPath, difficulty, accepted);
            }

            File.WriteAllText(metricsPath, string.Join("\n", metricsRows) + "\n", Encoding.UTF8);

            int expectedTotal = LevelsPerMode * Enum.GetValues(typeof(Difficulty)).Length;
            int expectedGenerated = LevelsPerMode * difficulties.Length;
            if (registry.Count != expectedTotal || totalAccepted != expectedGenerated)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedTotal} unique fingerprints and {expectedGenerated} generated levels, " +
                    $"accepted {totalAccepted} " +
                    $"(registry={registry.Count}).");
            }

            AssetDatabase.Refresh();

            Debug.Log("[LevelBake] Totals: modes=" + difficulties.Length + ", levelsPerMode=" + LevelsPerMode +
                      ", total=" + totalAccepted +
                      ", duplicateOffsetRetries=" + duplicateRetries +
                      ", validationOffsetRetries=" + validationRetries +
                      ", bakedDir=" + BakedRelativePath +
                      ", metrics=" + MetricsRelativePath +
                      ", previews=" + PreviewsRelativePath);
        }

        private sealed class BakeResult
        {
            public Level Level;
            public LevelFingerprint Fingerprint;
            public LevelQualityMetrics Metrics;
            public LevelDifficultyMetrics DifficultyMetrics;
            public int DuplicateRetries;
            public int ValidationRetries;
        }

        private static BakeResult GenerateAcceptedLevel(
            LevelGenerator generator,
            Solver solver,
            LevelQualityScorer scorer,
            LevelDifficultyAnalyzer analyzer,
            LevelFingerprintRegistry registry,
            Difficulty difficulty,
            int index)
        {
            int duplicateRetries = 0;
            int validationRetries = 0;
            string lastFailureReason = null;

            for (int block = 0; block < MaxOffsetBlocks; block++)
            {
                var options = new LevelGenerationOptions
                {
                    AttemptCount = BakeAttemptCount,
                    AttemptOffset = block * BakeAttemptCount,
                    // The bake validator performs one full Bake analysis below. Running
                    // exact analysis inside every generator attempt duplicates the most
                    // expensive work without changing the accepted deterministic board.
                    EvaluateMinMoves = false
                };

                Level candidate = generator.Generate(difficulty, index, options);
                LevelQualityMetrics metrics = scorer.Score(candidate, difficulty);
                LevelDifficultyMetrics difficultyMetrics = difficulty >= Difficulty.ExtraHard
                    ? analyzer.Analyze(
                        candidate,
                        LevelSeed.For(difficulty, index, LevelGenerator.VersionFor(difficulty), attempt: 7001),
                        LevelDifficultyAnalysisOptions.Bake)
                    : null;
                if (!LevelBakeValidator.MeetsRequirements(
                    candidate, solver, metrics, difficultyMetrics,
                    difficulty, index, out string reason))
                {
                    lastFailureReason = reason;
                    validationRetries++;
                    continue;
                }

                LevelFingerprint fingerprint = LevelFingerprint.From(candidate);
                if (registry.IsDuplicate(fingerprint))
                {
                    duplicateRetries++;
                    continue;
                }

                registry.Register(fingerprint);
                return new BakeResult
                {
                    Level = candidate,
                    Fingerprint = fingerprint,
                    Metrics = metrics,
                    DifficultyMetrics = difficultyMetrics,
                    DuplicateRetries = duplicateRetries,
                    ValidationRetries = validationRetries
                };
            }

            throw new InvalidOperationException(
                $"{difficulty} #{index}: exhausted {MaxOffsetBlocks} attempt-offset blocks " +
                $"(duplicateRetries={duplicateRetries}, validationRetries={validationRetries}, " +
                $"lastFailure={lastFailureReason ?? "duplicate"}).");
        }

        private static BakeResult GeneratePreviewAcceptedLevel(
            LevelGenerator generator,
            Solver solver,
            LevelQualityScorer scorer,
            LevelDifficultyAnalyzer analyzer,
            LevelFingerprintRegistry registry,
            Difficulty difficulty,
            int index)
        {
            string lastFailure = null;
            int validationRetries = 0;
            int duplicateRetries = 0;
            for (int block = 0; block < 32; block++)
            {
                var options = new LevelGenerationOptions
                {
                    AttemptCount = 2,
                    AttemptOffset = block * 2,
                    EvaluateMinMoves = false
                };
                Level level = generator.Generate(difficulty, index, options);
                LevelQualityMetrics quality = scorer.Score(level, difficulty);
                LevelDifficultyMetrics movement = analyzer.Analyze(
                    level,
                    LevelSeed.For(
                        difficulty, index, LevelGenerator.VersionFor(difficulty), 7001),
                    new LevelDifficultyAnalysisOptions
                    {
                        MaxExactStates = 0,
                        ExactFloorLimit = 0,
                        RandomRolloutCount = 8,
                        RolloutMaxMoves = 240
                    });
                if (!LevelBakeValidator.MeetsRequirements(
                        level, solver, quality, movement,
                        difficulty, index, out lastFailure))
                {
                    validationRetries++;
                    continue;
                }

                LevelFingerprint fingerprint = LevelFingerprint.From(level);
                if (registry.IsDuplicate(fingerprint))
                {
                    lastFailure = "canonical duplicate";
                    duplicateRetries++;
                    continue;
                }

                registry.Register(fingerprint);
                return new BakeResult
                {
                    Level = level,
                    Fingerprint = fingerprint,
                    Metrics = quality,
                    DifficultyMetrics = movement,
                    DuplicateRetries = duplicateRetries,
                    ValidationRetries = validationRetries
                };
            }

            throw new InvalidOperationException(
                $"Preview {difficulty} #{index} exhausted deterministic attempts: " +
                lastFailure);
        }

        private static void RunExtraHardExtension()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string bakedPath = Path.Combine(Application.dataPath, "Resources/Levels/Baked");
            string cachePath = Path.Combine(projectRoot, ExtensionCacheRelativePath);
            Directory.CreateDirectory(bakedPath);
            Directory.CreateDirectory(cachePath);

            var solver = new Solver();
            var scorer = new LevelQualityScorer(solver);
            var analyzer = new LevelDifficultyAnalyzer(solver);
            var registry = new LevelFingerprintRegistry();
            SeedExistingCatalogsForExtension(bakedPath, solver, registry);
            if (registry.Count != LevelsPerMode * 5)
            {
                throw new InvalidOperationException(
                    $"Expected 2500 existing fingerprints before extension, found {registry.Count}.");
            }

            var providers =
                new DeterministicBatchLevelProvider[ExtraHardExtensionWorkers];
            for (int worker = 0; worker < providers.Length; worker++)
            {
                var workerSolver = new Solver();
                providers[worker] = new DeterministicBatchLevelProvider(
                    new LevelCacheStore(cachePath, workerSolver),
                    workerSolver,
                    allowBakedRange: true);
            }
            int resumedSections = 0;
            int generatedSections = 0;
            for (int sectionStart = ExtraHardExtensionSourceStart;
                 sectionStart <= ExtraHardExtensionSourceEnd;
                 sectionStart += LevelsPerSection)
            {
                int sectionEnd = sectionStart + LevelsPerSection - 1;
                string path = Path.Combine(
                    bakedPath, $"ultrahard_{sectionStart:D4}_{sectionEnd:D4}.txt");
                if (File.Exists(path))
                {
                    List<Level> resumed = ParseSourceSection(
                        path, Difficulty.UltraHard, sectionStart);
                    RegisterExtensionSection(
                        resumed, solver, scorer, analyzer, registry, "resume");
                    resumedSections++;
                    Debug.Log(
                        $"[LevelBake] Extension resumed {sectionStart:D4}-{sectionEnd:D4} " +
                        $"(registry={registry.Count}).");
                    continue;
                }

                var visibleLevels = new Level[LevelsPerSection];
                var tasks = new Task[providers.Length];
                for (int worker = 0; worker < providers.Length; worker++)
                {
                    int workerIndex = worker;
                    tasks[worker] = Task.Run(() =>
                    {
                        for (int offset = workerIndex;
                             offset < LevelsPerSection;
                             offset += providers.Length)
                        {
                            int sourceIndex = sectionStart + offset;
                            int visibleIndex =
                                DifficultyCatalog.StandardBakedCount + sourceIndex;
                            visibleLevels[offset] = providers[workerIndex].GetLevel(
                                Difficulty.ExtraHard, visibleIndex);
                        }
                    });
                }
                Task.WaitAll(tasks);

                var section = new List<Level>(LevelsPerSection);
                for (int offset = 0; offset < visibleLevels.Length; offset++)
                {
                    int sourceIndex = sectionStart + offset;
                    int visibleIndex = DifficultyCatalog.StandardBakedCount + sourceIndex;
                    Level visible = visibleLevels[offset];
                    if (visible == null)
                    {
                        throw new InvalidOperationException(
                            $"Runtime producer returned null for Extra Hard #{visibleIndex}.");
                    }

                    var source = new Level(
                        visible.Grid,
                        visible.Spawn,
                        Difficulty.UltraHard,
                        sourceIndex);
                    ValidateExtensionLevel(
                        source, solver, scorer, analyzer,
                        $"runtime parity: Extra Hard #{visibleIndex}");
                    LevelFingerprint fingerprint = LevelFingerprint.From(source);
                    if (!registry.TryRegister(fingerprint))
                    {
                        throw new InvalidOperationException(
                            $"Runtime board is a global canonical duplicate: " +
                            $"Extra Hard #{visibleIndex} / Ultra Hard #{sourceIndex}.");
                    }
                    section.Add(source);
                }
                foreach (DeterministicBatchLevelProvider provider in providers)
                {
                    provider.RetainMemoryWindow(
                        Difficulty.ExtraHard,
                        DifficultyCatalog.StandardBakedCount + sectionEnd,
                        levelsBehind: 0,
                        levelsAhead: 0);
                }

                string payload = LevelPackSerializer.ToPack(section);
                VerifySerializedExtensionSection(
                    payload, section, sectionStart);
                WriteAllTextAtomic(path, payload);
                generatedSections++;
                Debug.Log(
                    $"[LevelBake] Extension wrote {sectionStart:D4}-{sectionEnd:D4} " +
                    $"(generatedSections={generatedSections}, registry={registry.Count}).");
            }

            int expectedExtensionCount =
                ExtraHardExtensionSourceEnd - ExtraHardExtensionSourceStart + 1;
            int expectedTotal = LevelsPerMode * 5 + expectedExtensionCount;
            if (registry.Count != expectedTotal)
            {
                throw new InvalidOperationException(
                    $"Expected {expectedTotal} globally unique boards after extension, " +
                    $"found {registry.Count}.");
            }

            AssetDatabase.Refresh();
            Debug.Log(
                $"[LevelBake] Extra Hard extension totals: generatedSections={generatedSections}, " +
                $"resumedSections={resumedSections}, boards={expectedExtensionCount}, " +
                $"globalRegistry={registry.Count}.");
        }

        private static void SeedExistingCatalogsForExtension(
            string bakedPath,
            Solver solver,
            LevelFingerprintRegistry registry)
        {
            foreach (Difficulty difficulty in new[]
                     {
                         Difficulty.Easy,
                         Difficulty.Medium,
                         Difficulty.Hard,
                         Difficulty.ExtraHard,
                         Difficulty.UltraHard
                     })
            {
                string slug = ModeSlug(difficulty);
                for (int start = 1; start <= LevelsPerMode; start += LevelsPerSection)
                {
                    int end = start + LevelsPerSection - 1;
                    string path = Path.Combine(
                        bakedPath, $"{slug}_{start:D4}_{end:D4}.txt");
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException(
                            $"Extra Hard extension requires existing {difficulty} section " +
                            $"{start:D4}-{end:D4}.", path);
                    }

                    List<Level> levels = ParseSourceSection(path, difficulty, start);
                    foreach (Level level in levels)
                    {
                        LevelSafetyValidator.RequireSafe(
                            level, solver,
                            $"extension seed: {difficulty} #{level.Index}");
                        LevelFingerprint fingerprint = LevelFingerprint.From(level);
                        if (!registry.TryRegister(fingerprint))
                        {
                            throw new InvalidOperationException(
                                $"Existing catalog duplicate while seeding extension: " +
                                $"{difficulty} #{level.Index}.");
                        }
                    }
                }
            }
        }

        private static List<Level> ParseSourceSection(
            string path,
            Difficulty difficulty,
            int expectedStart)
        {
            var errors = new List<string>();
            List<Level> levels = LevelParser.Parse(
                File.ReadAllText(path, Encoding.UTF8), difficulty, errors);
            if (errors.Count > 0 || levels.Count != LevelsPerSection)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} is not a valid {LevelsPerSection}-level section: " +
                    string.Join("; ", errors));
            }

            var normalized = new List<Level>(levels.Count);
            for (int i = 0; i < levels.Count; i++)
            {
                int expectedIndex = expectedStart + i;
                Level parsed = levels[i];
                normalized.Add(new Level(
                    parsed.Grid, parsed.Spawn, difficulty, expectedIndex));
            }
            return normalized;
        }

        private static void RegisterExtensionSection(
            IReadOnlyList<Level> levels,
            Solver solver,
            LevelQualityScorer scorer,
            LevelDifficultyAnalyzer analyzer,
            LevelFingerprintRegistry registry,
            string context)
        {
            foreach (Level level in levels)
            {
                ValidateExtensionLevel(level, solver, scorer, analyzer, context);
                LevelFingerprint fingerprint = LevelFingerprint.From(level);
                if (!registry.TryRegister(fingerprint))
                {
                    throw new InvalidOperationException(
                        $"{context} section contains a global canonical duplicate: " +
                        $"{level.Difficulty} #{level.Index}.");
                }
            }
        }

        private static void ValidateExtensionLevel(
            Level source,
            Solver solver,
            LevelQualityScorer scorer,
            LevelDifficultyAnalyzer analyzer,
            string context)
        {
            LevelSafetyValidator.RequireSafe(
                source, solver, $"{context}: {source.Difficulty} #{source.Index}");
            LevelQualityMetrics quality = scorer.Score(source, source.Difficulty);
            LevelDifficultyMetrics difficultyMetrics = analyzer.Analyze(
                source,
                LevelSeed.For(
                    source.Difficulty,
                    source.Index,
                    LevelGenerator.VersionFor(source.Difficulty),
                    attempt: 7001),
                LevelDifficultyAnalysisOptions.Runtime);
            if (!LevelBakeValidator.MeetsRequirements(
                    source,
                    solver,
                    quality,
                    difficultyMetrics,
                    source.Difficulty,
                    source.Index,
                    out string reason))
            {
                throw new InvalidOperationException(
                    $"{context}: {source.Difficulty} #{source.Index} failed validation ({reason}).");
            }
        }

        private static void VerifySerializedExtensionSection(
            string payload,
            IReadOnlyList<Level> expected,
            int sectionStart)
        {
            var errors = new List<string>();
            List<Level> parsed = LevelParser.Parse(
                payload, Difficulty.UltraHard, errors);
            if (errors.Count > 0 || parsed.Count != expected.Count)
            {
                throw new InvalidOperationException(
                    $"Serialized extension section {sectionStart:D4} failed round-trip: " +
                    string.Join("; ", errors));
            }

            for (int i = 0; i < expected.Count; i++)
            {
                Level before = expected[i];
                Level raw = parsed[i];
                var after = new Level(
                    raw.Grid,
                    raw.Spawn,
                    Difficulty.UltraHard,
                    sectionStart + i);
                if (before.Index != after.Index ||
                    before.Spawn != after.Spawn ||
                    !LevelFingerprint.CanonicalBytesEqual(
                        LevelFingerprint.From(before).CanonicalBytes,
                        LevelFingerprint.From(after).CanonicalBytes))
                {
                    throw new InvalidOperationException(
                        $"Serialized extension parity mismatch at Ultra Hard #{before.Index}.");
                }
            }
        }

        private static void WriteAllTextAtomic(string path, string payload)
        {
            string tempPath = path + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            File.WriteAllText(tempPath, payload, Encoding.UTF8);
            if (File.Exists(path))
                throw new IOException($"Refusing to replace existing extension section: {path}");
            File.Move(tempPath, path);
        }

        private static void LoadUntouchedCatalogs(
            string bakedPath,
            Solver solver,
            LevelQualityScorer scorer,
            LevelFingerprintRegistry registry,
            List<string> metricsRows)
        {
            foreach (Difficulty difficulty in new[]
                     {
                         Difficulty.Easy,
                         Difficulty.Medium,
                         Difficulty.Hard
                     })
            {
                string slug = ModeSlug(difficulty);
                for (int start = 1; start <= LevelsPerMode; start += LevelsPerSection)
                {
                    int end = start + LevelsPerSection - 1;
                    string path = Path.Combine(bakedPath, $"{slug}_{start:D4}_{end:D4}.txt");
                    if (!File.Exists(path))
                        throw new FileNotFoundException(
                            $"Selective Extra Hard bake requires the existing {difficulty} catalog.", path);

                    var errors = new List<string>();
                    List<Level> levels = LevelParser.Parse(
                        File.ReadAllText(path, Encoding.UTF8), difficulty, errors);
                    if (errors.Count > 0 || levels.Count != LevelsPerSection)
                    {
                        throw new InvalidOperationException(
                            $"{difficulty} section {start}-{end} could not seed uniqueness: " +
                            string.Join("; ", errors));
                    }

                    foreach (Level level in levels)
                    {
                        LevelSafetyValidator.RequireSafe(
                            level, solver, $"selective bake seed: {difficulty} #{level.Index}");
                        LevelFingerprint fingerprint = LevelFingerprint.From(level);
                        if (registry.IsDuplicate(fingerprint))
                            throw new InvalidOperationException(
                                $"Existing catalog duplicate while seeding: {difficulty} #{level.Index}");
                        registry.Register(fingerprint);
                        LevelQualityMetrics metrics = scorer.Score(level, difficulty);
                        metricsRows.Add(MetricsRow(
                            difficulty, level.Index, level, fingerprint, metrics, null));
                    }
                }
            }
        }

        private static string DistributionRow(
            PreviewBand band,
            List<PreviewSample> samples)
        {
            var core = new List<double>(samples.Count);
            var clearance = new List<double>(samples.Count);
            var separator = new List<double>(samples.Count);
            foreach (PreviewSample sample in samples)
            {
                LevelLayoutMetrics layout = sample.Result.Metrics.Layout;
                core.Add(layout.OpenFloorCoreCellRatio);
                clearance.Add(layout.MaximumFloorClearance);
                separator.Add(layout.ParallelSeparatorWallRatio);
            }
            core.Sort();
            clearance.Sort();
            separator.Sort();
            string Metric(double value) => value.ToString("F3");
            return string.Join(",",
                ModeSlug(band.Difficulty),
                band.Start,
                band.Rows,
                band.Cols,
                samples.Count,
                Metric(core[0]),
                Metric(Median(core)),
                Metric(core[core.Count - 1]),
                Metric(clearance[0]),
                Metric(Median(clearance)),
                Metric(clearance[clearance.Count - 1]),
                Metric(separator[0]),
                Metric(Median(separator)),
                Metric(separator[separator.Count - 1]));
        }

        private static double Median(List<double> sorted)
        {
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) * 0.5;
        }

        private static List<Level> SelectWorstPreviewLevels(
            List<PreviewSample> samples)
        {
            var result = new List<Level>(12);
            var selected = new HashSet<ulong>();
            AddWorst(
                sample => sample.Result.Metrics.Layout.OpenFloorCoreCellRatio,
                descending: false);
            AddWorst(
                sample => sample.Result.Metrics.Layout.MaximumFloorClearance,
                descending: false);
            AddWorst(
                sample => sample.Result.Metrics.Layout.ParallelSeparatorWallRatio,
                descending: true);
            return result;

            void AddWorst(
                Func<PreviewSample, double> metric,
                bool descending)
            {
                var ordered = new List<PreviewSample>(samples);
                ordered.Sort((left, right) =>
                {
                    int comparison = metric(left).CompareTo(metric(right));
                    if (descending) comparison = -comparison;
                    if (comparison != 0) return comparison;
                    comparison = left.Band.Rows.CompareTo(right.Band.Rows);
                    if (comparison != 0) return comparison;
                    return left.Index.CompareTo(right.Index);
                });
                int added = 0;
                foreach (PreviewSample sample in ordered)
                {
                    if (!selected.Add(sample.Result.Fingerprint.Hash)) continue;
                    result.Add(sample.Result.Level);
                    if (++added >= 4) break;
                }
            }
        }

        private static string MetricsRow(
            Difficulty difficulty,
            int index,
            Level level,
            LevelFingerprint fingerprint,
            LevelQualityMetrics metrics,
            LevelDifficultyMetrics difficultyMetrics)
        {
            string Optional(int? value) => value.HasValue ? value.Value.ToString() : "";
            string Metric(double value) => value.ToString("F3");
            return string.Join(",",
                ModeSlug(difficulty),
                index,
                fingerprint.Hash.ToString("X16"),
                level.Rows,
                level.Cols,
                metrics.FloorCount,
                metrics.WallCount,
                metrics.VoidCount,
                metrics.StopPositionCount,
                Metric(metrics.AverageBranching),
                Metric(metrics.IsolatedWallRatio),
                Metric(metrics.SilhouettePerimeter),
                Metric(metrics.Score),
                metrics.Layout.OpenFloorQuadCount,
                metrics.Layout.OpenFloorCellCount,
                Metric(metrics.Layout.OpenFloorCellRatio),
                metrics.Layout.FloorJunctionCount,
                Metric(metrics.Layout.FloorDegreeTwoRatio),
                metrics.Layout.FloorGraphCycleRank,
                metrics.Layout.LongestStraightFloorRun,
                Metric(metrics.Layout.LongLaneFloorRatio),
                metrics.Layout.OpenFloorCoreCount,
                metrics.Layout.OpenFloorCoreCellCount,
                Metric(metrics.Layout.OpenFloorCoreCellRatio),
                metrics.Layout.MaximumFloorClearance,
                Metric(metrics.Layout.FloorDegreeFourRatio),
                Metric(metrics.Layout.LargestOpenCoreComponentRatio),
                metrics.Layout.InteriorWallIslandCount,
                Metric(metrics.Layout.WallIslandCellRatio),
                Metric(metrics.Layout.ParallelSeparatorWallRatio),
                metrics.Layout.ExteriorCutDepth,
                metrics.Layout.SilhouetteConcaveCornerCount,
                metrics.Layout.ChoiceStopCount,
                Metric(metrics.Layout.MeaningfulChoiceRatio),
                Optional(difficultyMetrics?.MinMoves),
                difficultyMetrics?.Turns.ToString() ?? "",
                difficultyMetrics?.Reversals.ToString() ?? "",
                difficultyMetrics?.RevisitedStops.ToString() ?? "",
                difficultyMetrics != null ? Metric(difficultyMetrics.AveragePaintPerMove) : "",
                difficultyMetrics != null ? Metric(difficultyMetrics.ForcedMoveRatio) : "",
                difficultyMetrics?.RandomSolutionBest.ToString() ?? "",
                difficultyMetrics != null ? Metric(difficultyMetrics.RandomSolutionAverage) : "",
                difficultyMetrics?.RandomSolutionWorst.ToString() ?? "",
                difficultyMetrics?.RandomSolutionFailed.ToString() ?? "",
                difficultyMetrics != null ? Metric(difficultyMetrics.RandomSolutionSpread) : "",
                difficultyMetrics != null ? Metric(difficultyMetrics.MeaningfulChoiceRatio) : "",
                difficultyMetrics?.FloorGraphCycleRank.ToString() ?? "",
                difficultyMetrics != null ? Metric(difficultyMetrics.Score) : "",
                difficulty >= Difficulty.ExtraHard
                    ? Metric(LevelDifficultyAnalyzer.TargetScore(difficulty, index))
                    : "");
        }

        private static void WriteSections(string bakedPath, Difficulty difficulty, List<Level> levels)
        {
            string slug = ModeSlug(difficulty);
            for (int start = 1; start <= LevelsPerMode; start += LevelsPerSection)
            {
                int end = start + LevelsPerSection - 1;
                var section = levels.GetRange(start - 1, LevelsPerSection);
                if (section.Count != LevelsPerSection)
                {
                    throw new InvalidOperationException(
                        $"{difficulty} section {start}-{end} has {section.Count} levels.");
                }

                string fileName = $"{slug}_{start:D4}_{end:D4}.txt";
                string path = Path.Combine(bakedPath, fileName);
                File.WriteAllText(path, LevelPackSerializer.ToPack(section), Encoding.UTF8);
            }
        }

        private static void WriteSectionPreviews(string previewsPath, Difficulty difficulty, List<Level> levels)
        {
            string slug = ModeSlug(difficulty);
            for (int start = 1; start <= LevelsPerMode; start += LevelsPerSection)
            {
                int end = start + LevelsPerSection - 1;
                var section = levels.GetRange(start - 1, LevelsPerSection);
                string fileName = $"{slug}_{start:D4}_{end:D4}.png";
                string path = Path.Combine(previewsPath, fileName);
                WriteContactSheet(path, section, $"{difficulty} {start:D4}-{end:D4}");
            }
        }

        private static void WriteContactSheet(
            string path,
            IReadOnlyList<Level> levels,
            string title,
            int sheetCols = 10,
            int tilePx = 3)
        {
            const int thumbPad = 3;
            const int headerHeight = 28;

            if (levels.Count < 1)
            {
                throw new InvalidOperationException(
                    "Contact sheet requires at least one level.");
            }
            int sheetRows = (levels.Count + sheetCols - 1) / sheetCols;

            int maxBoard = 0;
            for (int i = 0; i < levels.Count; i++)
            {
                Level level = levels[i];
                maxBoard = Math.Max(maxBoard, Math.Max(level.Rows, level.Cols));
            }

            int thumbInner = maxBoard * tilePx;
            int thumbSize = thumbInner + thumbPad * 2;
            int sheetWidth = sheetCols * thumbSize;
            int sheetHeight = headerHeight + sheetRows * thumbSize;

            var texture = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[sheetWidth * sheetHeight];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = SheetBackground;
            texture.SetPixels32(pixels);

            DrawHeaderLabel(texture, title, sheetWidth, headerHeight);

            for (int i = 0; i < levels.Count; i++)
            {
                int col = i % sheetCols;
                int row = i / sheetCols;
                int originX = col * thumbSize;
                int originY = headerHeight + row * thumbSize;
                DrawThumbBorder(texture, originX, originY, thumbSize);
                DrawLevelThumb(texture, levels[i], originX + thumbPad, originY + thumbPad, tilePx);
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void DrawHeaderLabel(Texture2D texture, string title, int width, int height)
        {
            int x = 4;
            int y = 6;
            foreach (char ch in title)
            {
                DrawGlyph(texture, ch, x, y, new Color32(220, 220, 228, 255));
                x += 6;
                if (x > width - 8) break;
            }
        }

        private static void DrawGlyph(Texture2D texture, char ch, int originX, int originY, Color32 color)
        {
            // Minimal 5x7 bitmap font for section titles.
            if (!MiniFont.Glyphs.TryGetValue(char.ToUpperInvariant(ch), out string[] rows))
                return;

            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] != '1') continue;
                    SetPixel(texture, originX + c, originY + r, color);
                }
            }
        }

        private static void DrawThumbBorder(Texture2D texture, int originX, int originY, int size)
        {
            for (int x = originX; x < originX + size; x++)
            {
                SetPixel(texture, x, originY, ThumbBorder);
                SetPixel(texture, x, originY + size - 1, ThumbBorder);
            }

            for (int y = originY; y < originY + size; y++)
            {
                SetPixel(texture, originX, y, ThumbBorder);
                SetPixel(texture, originX + size - 1, y, ThumbBorder);
            }
        }

        private static void DrawLevelThumb(Texture2D texture, Level level, int originX, int originY, int tilePx)
        {
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    Color32 color;
                    if (level.Spawn.Row == r && level.Spawn.Col == c)
                    {
                        color = SpawnColor;
                    }
                    else
                    {
                        color = level.Grid[r, c] switch
                        {
                            Tile.Floor => FloorColor,
                            Tile.Wall => WallColor,
                            Tile.Void => VoidColor,
                            _ => SheetBackground
                        };
                    }

                    FillRect(texture, originX + c * tilePx, originY + r * tilePx, tilePx, tilePx, color);
                }
            }
        }

        private static void FillRect(Texture2D texture, int x, int y, int w, int h, Color32 color)
        {
            for (int dy = 0; dy < h; dy++)
            {
                for (int dx = 0; dx < w; dx++)
                    SetPixel(texture, x + dx, y + dy, color);
            }
        }

        private static void SetPixel(Texture2D texture, int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= texture.width || y >= texture.height) return;
            texture.SetPixel(x, y, color);
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        private static string ModeSlug(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => "easy",
            Difficulty.Medium => "medium",
            Difficulty.Hard => "hard",
            Difficulty.ExtraHard => "extrahard",
            Difficulty.UltraHard => "ultrahard",
            _ => "easy"
        };

        private static class MiniFont
        {
            public static readonly Dictionary<char, string[]> Glyphs = BuildGlyphs();

            private static Dictionary<char, string[]> BuildGlyphs()
            {
                return new Dictionary<char, string[]>
                {
                    [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" },
                    ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
                    ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
                    ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
                    ['2'] = new[] { "01110", "10001", "00001", "00110", "01000", "10000", "11111" },
                    ['3'] = new[] { "01110", "10001", "00001", "00110", "00001", "10001", "01110" },
                    ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
                    ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
                    ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
                    ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
                    ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
                    ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
                    ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
                    ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
                    ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
                    ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
                    ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" },
                    ['M'] = new[] { "10001", "11011", "10101", "10001", "10001", "10001", "10001" },
                    ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
                    ['S'] = new[] { "01110", "10001", "10000", "01110", "00001", "10001", "01110" },
                    ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
                    ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
                    ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
                    ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" },
                };
            }
        }
    }
}
