using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Layers bundled levels over an endless deterministic generator. Indices stay
    /// continuous across the baked/runtime boundary.
    /// </summary>
    public sealed class CompositeLevelProvider : ILevelProvider
    {
        private readonly ILevelProvider _authored;
        private readonly ILevelProvider _generated;

        public CompositeLevelProvider(ILevelProvider authored, ILevelProvider generated)
        {
            _authored = authored;
            _generated = generated;
        }

        public int BakedCountFor(Difficulty difficulty) =>
            _authored?.BakedCountFor(difficulty) ?? 0;

        public Level GetLevel(Difficulty difficulty, int index)
        {
            if (index < 1 || index > DifficultyConfig.PracticalMaxLevel) return null;
            int authoredCount = BakedCountFor(difficulty);
            if (index <= authoredCount)
                return _authored.GetLevel(difficulty, index);
            return _generated.GetLevel(difficulty, index);
        }
    }
}
