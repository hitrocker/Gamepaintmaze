using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Serves hand-authored levels parsed from text packs (one pack per mode).
    /// The Unity layer loads the pack text from Resources/TextAssets and hands the
    /// raw strings here, keeping this class engine-free and unit-testable.
    /// Levels that fail validation are dropped (and reported via <see cref="Errors"/>).
    /// </summary>
    public sealed class FileLevelProvider : ILevelProvider
    {
        private readonly Dictionary<Difficulty, List<Level>> _byMode = new();
        public List<string> Errors { get; } = new();

        public FileLevelProvider(IReadOnlyDictionary<Difficulty, string> rawTextByMode, Solver solver = null, bool validate = true)
        {
            solver ??= new Solver();
            foreach (Difficulty d in System.Enum.GetValues(typeof(Difficulty)))
            {
                var list = new List<Level>();
                if (rawTextByMode != null && rawTextByMode.TryGetValue(d, out var text))
                {
                    var parsed = LevelParser.Parse(text, d, Errors);
                    int idx = 1;
                    foreach (var lvl in parsed)
                    {
                        if (validate && !solver.IsAlwaysSolvable(lvl))
                        {
                            Errors.Add($"{d} authored #{lvl.Index}: not never-stuck, dropped");
                            continue;
                        }
                        // Re-index sequentially after dropping invalids.
                        list.Add(new Level(lvl.Grid, lvl.Spawn, d, idx++));
                    }
                }
                _byMode[d] = list;
            }
        }

        public int BakedCountFor(Difficulty difficulty) =>
            _byMode.TryGetValue(difficulty, out var l) ? l.Count : 0;

        public Level GetLevel(Difficulty difficulty, int index)
        {
            if (!_byMode.TryGetValue(difficulty, out var list)) return null;
            if (index < 1 || index > list.Count) return null;
            return list[index - 1];
        }
    }
}
