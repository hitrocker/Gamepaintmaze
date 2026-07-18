using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Routes each playable track at its baked boundary. Extra Hard uses 1..5000
    /// baked levels and runtime generation from 5001 onward.
    /// </summary>
    public sealed class ScalableLevelProvider :
        ILevelProvider,
        IPrefetchLevelProvider,
        ILevelMemoryWindow
    {
        private readonly SectionedLevelProvider _baked;
        private readonly DeterministicBatchLevelProvider _generated;

        public ScalableLevelProvider(
            SectionedLevelProvider baked,
            DeterministicBatchLevelProvider generated)
        {
            _baked = baked;
            _generated = generated;
        }

        public int BakedCountFor(Difficulty difficulty) => _baked.BakedCountFor(difficulty);

        public Level GetLevel(Difficulty difficulty, int index)
        {
            if (!DifficultyCatalog.IsPlayable(difficulty) ||
                index < 1 || index > DifficultyConfig.PracticalMaxLevel) return null;

            int bakedCount = BakedCountFor(difficulty);
            if (index <= bakedCount)
            {
                int catalogIndex = LevelCatalogOrder.Map(difficulty, index);
                Level source = _baked.GetLevel(difficulty, catalogIndex);
                return source == null
                    ? null
                    : new Level(source.Grid, source.Spawn, difficulty, index);
            }
            return _generated.GetLevel(difficulty, index);
        }

        public void Prefetch(Difficulty difficulty, int index)
        {
            if (!DifficultyCatalog.IsPlayable(difficulty) ||
                index < 1 || index > DifficultyConfig.PracticalMaxLevel) return;

            int bakedCount = BakedCountFor(difficulty);
            if (index <= bakedCount)
                _baked.Prefetch(
                    difficulty, LevelCatalogOrder.Map(difficulty, index));
            else
                _generated.Prefetch(difficulty, index);
        }

        public bool IsReady(Difficulty difficulty, int index)
        {
            if (!DifficultyCatalog.IsPlayable(difficulty) ||
                index < 1 || index > DifficultyConfig.PracticalMaxLevel) return false;

            int bakedCount = BakedCountFor(difficulty);
            return index <= bakedCount
                ? _baked.IsReady(
                    difficulty, LevelCatalogOrder.Map(difficulty, index))
                : _generated.IsReady(difficulty, index);
        }

        public void RetainMemoryWindow(
            Difficulty difficulty,
            int currentIndex,
            int levelsBehind,
            int levelsAhead)
        {
            _generated.RetainMemoryWindow(
                difficulty, currentIndex, levelsBehind, levelsAhead);
        }
    }
}
