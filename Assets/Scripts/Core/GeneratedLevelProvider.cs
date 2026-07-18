using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Procedurally generates irregular, never-stuck levels per mode. Each
    /// (difficulty,index) is deterministic (seeded), so a mode has a stable Level
    /// 1..N. Delegates generation to <see cref="LevelGenerator"/>.
    /// </summary>
    public sealed class GeneratedLevelProvider : ILevelProvider
    {
        private readonly LevelGenerator _generator;

        public GeneratedLevelProvider(Solver solver = null)
        {
            _generator = new LevelGenerator(solver);
        }

        public int BakedCountFor(Difficulty difficulty) => 0;

        public Level GetLevel(Difficulty difficulty, int index)
        {
            if (index < 1) index = 1;
            return _generator.Generate(difficulty, index);
        }

        /// <summary>Guaranteed-valid serpentine corridor (used by tests and fallback).</summary>
        public Level GenerateSerpentine(int size, Difficulty difficulty, int index) =>
            _generator.GenerateSerpentine(size, difficulty, index);
    }
}
