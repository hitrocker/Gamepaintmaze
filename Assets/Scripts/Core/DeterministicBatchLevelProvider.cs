using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PaintMaze.Domain;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PaintMaze.Core
{
    /// <summary>
    /// Deterministically generates levels beyond the baked catalog.
    ///
    /// Each (generatorVersion, difficulty, index) maps to a single board that is a pure
    /// function of those inputs (see <see cref="GenerateSlot"/>). Each request hydrates,
    /// generates, validates, and persists only that index; the gameplay coordinator may
    /// issue two future requests sequentially to keep transitions ready.
    /// </summary>
    public sealed class DeterministicBatchLevelProvider :
        ILevelProvider,
        IPrefetchLevelProvider,
        ILevelMemoryWindow
    {
        private const int RuntimeAttemptCount = 1;
        private const int MaxOffsetBlocks = 12;
        private const int ExtraHardRuntimeAttempts = 1;
        private const int UltraHardRuntimeAttempts = 1;
        private const int ExtraHardMaxOffsetBlocks = 1;
        private const int UltraHardMaxOffsetBlocks = 1;

        private readonly LevelGenerator _generator;
        private readonly Solver _solver;
        private readonly LevelQualityScorer _scorer;
        private readonly LevelDifficultyAnalyzer _analyzer;
        private readonly LevelCacheStore _cache;
        private readonly IReadOnlyList<Level> _openFallbacks;
        private readonly bool _allowBakedRange;
        private readonly object _gate = new();
        private readonly Dictionary<(Difficulty difficulty, int index), Level> _slots = new();
        private readonly Dictionary<(Difficulty difficulty, int index), Lazy<Level>> _slotFactories = new();
        private readonly HashSet<(Difficulty difficulty, int index)> _scheduledPrefetches = new();

        private readonly struct GenerationTiming
        {
            public readonly double SearchMs;
            public readonly double ValidationMs;
            public readonly bool UsedFallback;

            public GenerationTiming(double searchMs, double validationMs, bool usedFallback)
            {
                SearchMs = searchMs;
                ValidationMs = validationMs;
                UsedFallback = usedFallback;
            }
        }

        public DeterministicBatchLevelProvider(
            LevelCacheStore cache,
            Solver solver = null,
            bool allowBakedRange = false)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _solver = solver ?? new Solver();
            _scorer = new LevelQualityScorer(_solver);
            _analyzer = new LevelDifficultyAnalyzer(_solver);
            _generator = new LevelGenerator(_solver, _scorer);
            _openFallbacks = OpenExtraHardFallbackBank.Load();
            _allowBakedRange = allowBakedRange;
        }

        public int BakedCountFor(Difficulty difficulty) => 0;

        public Level GetLevel(Difficulty difficulty, int index)
        {
            if (!DifficultyCatalog.IsPlayable(difficulty) ||
                index < 1 ||
                (!_allowBakedRange &&
                 index <= DifficultyCatalog.BakedCountFor(difficulty)) ||
                index > DifficultyConfig.PracticalMaxLevel) return null;

            return GetSlotValue(difficulty, index);
        }

        public void Prefetch(Difficulty difficulty, int index)
        {
            if (!DifficultyCatalog.IsPlayable(difficulty) ||
                index < 1 ||
                (!_allowBakedRange &&
                 index <= DifficultyCatalog.BakedCountFor(difficulty)) ||
                index > DifficultyConfig.PracticalMaxLevel) return;

            var key = (difficulty, index);
            lock (_gate)
            {
                if (_slots.ContainsKey(key) ||
                    _slotFactories.ContainsKey(key) ||
                    !_scheduledPrefetches.Add(key))
                    return;
            }

            // Warm only the requested slot without blocking the caller. The scheduled
            // set avoids redundant thread-pool work while Lazy still guarantees that a
            // racing synchronous request shares the same deterministic computation.
            Task.Run(() =>
            {
                try { GetSlotValue(difficulty, index); }
                finally
                {
                    lock (_gate) _scheduledPrefetches.Remove(key);
                }
            });
        }

        public bool IsReady(Difficulty difficulty, int index)
        {
            lock (_gate)
            {
                return _slots.ContainsKey((difficulty, index));
            }
        }

        public void RetainMemoryWindow(
            Difficulty difficulty,
            int currentIndex,
            int levelsBehind,
            int levelsAhead)
        {
            int minIndex = Math.Max(1, currentIndex - Math.Max(0, levelsBehind));
            int maxIndex = (int)Math.Min(
                DifficultyConfig.PracticalMaxLevel,
                (long)currentIndex + Math.Max(0, levelsAhead));
            lock (_gate)
            {
                var stale = new List<(Difficulty difficulty, int index)>();
                foreach (var pair in _slots)
                {
                    if (pair.Key.difficulty != difficulty ||
                        pair.Key.index < minIndex ||
                        pair.Key.index > maxIndex)
                        stale.Add(pair.Key);
                }

                foreach (var key in stale)
                    _slots.Remove(key);
            }
        }

        /// <summary>
        /// Returns the memoized level for a slot, computing it on the calling thread if
        /// necessary. Concurrent callers for the same slot share a single computation and
        /// receive the identical, deterministic board.
        /// </summary>
        private Level GetSlotValue(Difficulty difficulty, int index)
        {
            var key = (difficulty, index);
            Lazy<Level> factory;
            lock (_gate)
            {
                if (_slots.TryGetValue(key, out Level cached))
                {
                    Debug.Log($"[Perf] levelSlotHit source=memory difficulty={difficulty} index={index}");
                    return cached;
                }
                if (!_slotFactories.TryGetValue(key, out factory))
                {
                    factory = new Lazy<Level>(() => ProduceSlotValue(difficulty, index),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    _slotFactories[key] = factory;
                }
            }

            Level value = factory.Value; // computed once; other threads block until ready

            lock (_gate)
            {
                if (_slots.TryGetValue(key, out Level published))
                    return published;
                _slots[key] = value;
                _slotFactories.Remove(key);
                return value;
            }
        }

        /// <summary>Factory body for a slot; never throws so no failure is memoized.</summary>
        private Level ProduceSlotValue(Difficulty difficulty, int index)
        {
            var totalTimer = Stopwatch.StartNew();
            try
            {
                var cacheTimer = Stopwatch.StartNew();
                if (_cache.TryLoadLevel(difficulty, index, out Level cached))
                {
                    Level patched = ExtraHardRuntimePatches.ApplyVisible(
                        cached, difficulty, index);
                    bool wasPatched = !ReferenceEquals(patched, cached);
                    if (wasPatched)
                        TrySaveLevel(difficulty, index, patched);
                    cacheTimer.Stop();
                    Debug.Log(
                        $"[Perf] levelSlotReady source={(wasPatched ? "disk-patched" : "disk")} " +
                        $"difficulty={difficulty} index={index} " +
                        $"cacheMs={cacheTimer.Elapsed.TotalMilliseconds:F1} totalMs={totalTimer.Elapsed.TotalMilliseconds:F1}");
                    return patched;
                }
                cacheTimer.Stop();

                Level generated = GenerateSlot(difficulty, index, out GenerationTiming timing);
                var saveTimer = Stopwatch.StartNew();
                bool saved = TrySaveLevel(difficulty, index, generated);
                saveTimer.Stop();
                totalTimer.Stop();
                Debug.Log(
                    $"[Perf] levelSlotReady source={(timing.UsedFallback ? "fallback" : "generated")} " +
                    $"difficulty={difficulty} index={index} cacheMs={cacheTimer.Elapsed.TotalMilliseconds:F1} " +
                    $"searchMs={timing.SearchMs:F1} validationMs={timing.ValidationMs:F1} " +
                    $"saveMs={saveTimer.Elapsed.TotalMilliseconds:F1} saved={saved} " +
                    $"totalMs={totalTimer.Elapsed.TotalMilliseconds:F1}");
                return generated;
            }
            catch (Exception ex)
            {
                var recoveryTimer = Stopwatch.StartNew();
                Level fallback = CertifiedFallback(difficulty, index, "slot exception recovery");
                bool saved = TrySaveLevel(difficulty, index, fallback);
                recoveryTimer.Stop();
                totalTimer.Stop();
                Debug.LogWarning(
                    $"[Perf] levelSlotReady source=exception-fallback difficulty={difficulty} index={index} " +
                    $"recoveryMs={recoveryTimer.Elapsed.TotalMilliseconds:F1} saved={saved} " +
                    $"totalMs={totalTimer.Elapsed.TotalMilliseconds:F1} exception={ex.GetType().Name}");
                return fallback;
            }
        }

        private bool TrySaveLevel(Difficulty difficulty, int index, Level level)
        {
            try
            {
                _cache.SaveLevel(difficulty, index, level);
                return true;
            }
            catch (Exception)
            {
                // Cache is optional; deterministic regeneration is safe.
                return false;
            }
        }

        /// <summary>
        /// Deterministic single-level generation: a pure function of
        /// (generatorVersion, difficulty, index). No cross-slot state, so it is safe to
        /// call from any thread and in any order and always yields the same board.
        /// </summary>
        private Level GenerateSlot(
            Difficulty difficulty,
            int index,
            out GenerationTiming timing)
        {
            DifficultyCatalog.ResolveGenerationSource(
                difficulty, index, out Difficulty sourceDifficulty, out int sourceIndex);
            int attemptCount = RuntimeAttemptsFor(sourceDifficulty);
            int maxBlocks = MaxOffsetBlocksFor(sourceDifficulty);
            double searchMs = 0.0;
            double validationMs = 0.0;

            for (int block = 0; block < maxBlocks; block++)
            {
                var options = new LevelGenerationOptions
                {
                    AttemptCount = attemptCount,
                    AttemptOffset = block * attemptCount,
                    EvaluateMinMoves = false
                };

                var timer = Stopwatch.StartNew();
                Level candidate = _generator.Generate(sourceDifficulty, sourceIndex, options);
                candidate = ExtraHardRuntimePatches.ApplySource(
                    candidate, sourceDifficulty, sourceIndex);
                timer.Stop();
                searchMs += timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                if (sourceDifficulty >= Difficulty.ExtraHard)
                {
                    LevelQualityMetrics quality = _scorer.Score(candidate, sourceDifficulty);
                    LevelDifficultyMetrics difficultyMetrics = _analyzer.Analyze(
                        candidate,
                        LevelSeed.For(
                            sourceDifficulty, sourceIndex,
                            LevelGenerator.VersionFor(sourceDifficulty), attempt: 7001),
                        LevelDifficultyAnalysisOptions.Runtime);
                    if (!LevelBakeValidator.MeetsRequirements(
                        candidate, _solver, quality, difficultyMetrics,
                        sourceDifficulty, sourceIndex, out _))
                    {
                        timer.Stop();
                        validationMs += timer.Elapsed.TotalMilliseconds;
                        continue;
                    }
                    timer.Stop();
                    validationMs += timer.Elapsed.TotalMilliseconds;
                    timing = new GenerationTiming(searchMs, validationMs, usedFallback: false);
                    return new Level(candidate.Grid, candidate.Spawn, difficulty, index);
                }

                LevelQualityMetrics metrics = _scorer.Score(candidate, sourceDifficulty);
                if (!LevelBakeValidator.MeetsRequirements(
                    candidate, _solver, metrics, null,
                    sourceDifficulty, sourceIndex, out _))
                {
                    timer.Stop();
                    validationMs += timer.Elapsed.TotalMilliseconds;
                    continue;
                }
                timer.Stop();
                validationMs += timer.Elapsed.TotalMilliseconds;
                timing = new GenerationTiming(searchMs, validationMs, usedFallback: false);

                return new Level(candidate.Grid, candidate.Spawn, difficulty, index);
            }

            var fallbackTimer = Stopwatch.StartNew();
            Level fallback = CertifiedFallback(difficulty, index, "generation attempts exhausted");
            fallbackTimer.Stop();
            searchMs += fallbackTimer.Elapsed.TotalMilliseconds;
            timing = new GenerationTiming(searchMs, validationMs, usedFallback: true);
            return fallback;
        }

        private Level CertifiedFallback(Difficulty difficulty, int index, string context)
        {
            DifficultyCatalog.ResolveGenerationSource(
                difficulty, index, out Difficulty sourceDifficulty, out int sourceIndex);
            var cfg = DifficultyConfig.For(sourceDifficulty);
            int rows = cfg.BoardRowsFor(sourceIndex);
            int cols = cfg.BoardColsFor(sourceIndex);
            Level fallback;
            if (sourceDifficulty >= Difficulty.ExtraHard &&
                OpenExtraHardFallbackBank.TrySelect(
                    _openFallbacks, rows, cols, sourceDifficulty, sourceIndex,
                    out Level bankFallback))
            {
                fallback = bankFallback;
            }
            else
            {
                fallback = sourceDifficulty >= Difficulty.ExtraHard
                    ? _generator.GenerateCertifiedOpenFallback(
                        rows, cols, sourceDifficulty, sourceIndex)
                    : _generator.GenerateGuaranteedFallback(
                        rows, cols, sourceDifficulty, sourceIndex);
            }
            var published = new Level(fallback.Grid, fallback.Spawn, difficulty, index);
            return LevelSafetyValidator.RequireSafe(
                published, _solver, $"{context}: {difficulty} #{index}");
        }

        private static int RuntimeAttemptsFor(Difficulty difficulty) => difficulty switch
        {
            Difficulty.UltraHard => UltraHardRuntimeAttempts,
            Difficulty.ExtraHard => ExtraHardRuntimeAttempts,
            _ => RuntimeAttemptCount
        };

        private static int MaxOffsetBlocksFor(Difficulty difficulty) => difficulty switch
        {
            Difficulty.UltraHard => UltraHardMaxOffsetBlocks,
            Difficulty.ExtraHard => ExtraHardMaxOffsetBlocks,
            _ => MaxOffsetBlocks
        };
    }
}
