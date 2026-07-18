using System.Collections.Generic;
using System.Threading.Tasks;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Core
{
    /// <summary>
    /// Serves baked levels from 50-level Resources sections. Extra Hard 501..5000
    /// is backed by the retained legacy Ultra Hard 1..4500 source catalog.
    /// </summary>
    public sealed class SectionedLevelProvider : ILevelProvider
    {
        public const int SectionSize = 50;

        private readonly object _gate = new();
        private readonly Dictionary<(Difficulty difficulty, int sectionStart), Level[]> _sections = new();
        private readonly Dictionary<(Difficulty difficulty, int sectionStart), Task<Level[]>> _pending = new();

        public int BakedCountFor(Difficulty difficulty) =>
            DifficultyCatalog.BakedCountFor(difficulty);

        public Level GetLevel(Difficulty difficulty, int index)
        {
            int bakedCount = BakedCountFor(difficulty);
            if (index < 1 || index > bakedCount) return null;

            int sectionStart = SectionStartFor(index);
            Level[] section = LoadSection(difficulty, sectionStart);
            if (section == null) return null;

            int offset = index - sectionStart;
            if (offset < 0 || offset >= section.Length) return null;
            return section[offset];
        }

        /// <summary>
        /// Loads the small TextAsset reference on the main thread, then parses its fifty
        /// levels on a worker so the home screen remains responsive.
        /// </summary>
        public void Prefetch(Difficulty difficulty, int index)
        {
            int bakedCount = BakedCountFor(difficulty);
            if (index < 1 || index > bakedCount) return;

            int sectionStart = SectionStartFor(index);
            var key = (difficulty, sectionStart);
            lock (_gate)
            {
                if (_sections.ContainsKey(key) || _pending.ContainsKey(key)) return;
            }

            if (!TryReadSectionText(difficulty, sectionStart,
                    out Difficulty sourceDifficulty, out string text))
                return;

            Task<Level[]> task = Task.Run(
                () => ParseSection(difficulty, sectionStart, sourceDifficulty, text));
            lock (_gate)
            {
                if (!_sections.ContainsKey(key) && !_pending.ContainsKey(key))
                    _pending[key] = task;
            }
        }

        public bool IsReady(Difficulty difficulty, int index)
        {
            int bakedCount = BakedCountFor(difficulty);
            if (index < 1 || index > bakedCount) return false;

            int sectionStart = SectionStartFor(index);
            var key = (difficulty, sectionStart);
            Task<Level[]> pending;
            lock (_gate)
            {
                if (_sections.ContainsKey(key)) return true;
                if (!_pending.TryGetValue(key, out pending) || !pending.IsCompleted)
                    return false;
            }

            PublishPending(key, pending);
            lock (_gate) return _sections.ContainsKey(key);
        }

        public static int SectionStartFor(int index) =>
            ((index - 1) / SectionSize) * SectionSize + 1;

        public static int SectionEndFor(int sectionStart) =>
            sectionStart + SectionSize - 1;

        private Level[] LoadSection(Difficulty difficulty, int sectionStart)
        {
            var key = (difficulty, sectionStart);
            Task<Level[]> pending;
            lock (_gate)
            {
                if (_sections.TryGetValue(key, out Level[] cached))
                    return cached;
                _pending.TryGetValue(key, out pending);
            }

            Level[] loaded = pending != null
                ? pending.GetAwaiter().GetResult()
                : ReadSectionFromResources(difficulty, sectionStart);
            if (loaded == null) return null;

            lock (_gate)
            {
                _pending.Remove(key);
                if (_sections.TryGetValue(key, out Level[] cached))
                    return cached;
                _sections[key] = loaded;
                return loaded;
            }
        }

        private void PublishPending(
            (Difficulty difficulty, int sectionStart) key, Task<Level[]> pending)
        {
            Level[] loaded = pending.Status == TaskStatus.RanToCompletion ? pending.Result : null;
            lock (_gate)
            {
                _pending.Remove(key);
                if (loaded != null && !_sections.ContainsKey(key))
                    _sections[key] = loaded;
            }
        }

        private static Level[] ReadSectionFromResources(Difficulty difficulty, int sectionStart)
        {
            if (!TryReadSectionText(difficulty, sectionStart,
                    out Difficulty sourceDifficulty, out string text))
                return null;
            return ParseSection(difficulty, sectionStart, sourceDifficulty, text);
        }

        private static bool TryReadSectionText(Difficulty difficulty, int sectionStart,
            out Difficulty sourceDifficulty, out string text)
        {
            DifficultyCatalog.ResolveBakedSource(
                difficulty, sectionStart, out sourceDifficulty, out int sourceSectionStart);
            int sourceSectionEnd = SectionEndFor(sourceSectionStart);
            string resourcePath =
                $"Levels/Baked/{ModeSlug(sourceDifficulty)}_{sourceSectionStart:D4}_{sourceSectionEnd:D4}";
            var asset = Resources.Load<TextAsset>(resourcePath);
            text = asset != null ? asset.text : null;
            return !string.IsNullOrEmpty(text);
        }

        private static Level[] ParseSection(Difficulty difficulty, int sectionStart,
            Difficulty sourceDifficulty, string text)
        {
            var parsed = LevelParser.Parse(text, sourceDifficulty);
            if (parsed.Count != SectionSize) return null;

            var section = new Level[SectionSize];
            for (int i = 0; i < SectionSize; i++)
            {
                int globalIndex = sectionStart + i;
                Level level = parsed[i];
                section[i] = new Level(level.Grid, level.Spawn, difficulty, globalIndex);
            }

            return section;
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
    }
}
