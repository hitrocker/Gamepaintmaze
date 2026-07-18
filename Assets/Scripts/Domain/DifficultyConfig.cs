namespace PaintMaze.Domain
{
    /// <summary>
    /// Per-mode tuning knobs that drive level generation and progression.
    /// Kept as pure data (no engine types) so the generator and tests can use it
    /// directly. Colors live in the presentation Theme, not here.
    ///
    ///  - Base/Max rows:    board height progression
    ///  - Base/Max columns: board width progression
    ///  - ObstacleFraction: fraction of the open room filled with wall blocks
    ///  - BakedLevelCount:  deterministic sectioned levels bundled with the game
    /// </summary>
    public sealed class DifficultyConfig
    {
        private readonly Difficulty _difficulty;

        public string DisplayName { get; }
        public int BaseRows { get; }
        public int MaxRows { get; }
        public int BaseCols { get; }
        public int MaxCols { get; }
        public double ObstacleFraction { get; }
        public int BakedLevelCount { get; }
        public const int PracticalMaxLevel = 2_000_000_000;

        private DifficultyConfig(Difficulty difficulty, string displayName, int baseRows, int maxRows,
            int baseCols, int maxCols, double obstacleFraction, int bakedLevelCount = 500)
        {
            _difficulty = difficulty;
            DisplayName = displayName;
            BaseRows = baseRows;
            MaxRows = maxRows;
            BaseCols = baseCols;
            MaxCols = maxCols;
            ObstacleFraction = obstacleFraction;
            BakedLevelCount = bakedLevelCount;
        }

        public static DifficultyConfig For(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => new DifficultyConfig(difficulty, "Easy", 7, 8, 6, 7, 0.14),
            Difficulty.Medium => new DifficultyConfig(difficulty, "Medium", 8, 9, 7, 8, 0.18),
            Difficulty.Hard => new DifficultyConfig(difficulty, "Hard", 9, 10, 8, 9, 0.22),
            Difficulty.ExtraHard => new DifficultyConfig(difficulty, "Extra Hard", 9, 14, 9, 16, 0.26,
                DifficultyCatalog.ExtraHardBakedCount),
            Difficulty.UltraHard => new DifficultyConfig(difficulty, "Ultra Hard", 10, 16, 10, 22, 0.30),
            _ => new DifficultyConfig(Difficulty.Easy, "Easy", 7, 8, 6, 7, 0.14)
        };

        public int BoardRowsFor(int levelNumber)
        {
            if (_difficulty == Difficulty.ExtraHard)
                return ExtraHardDimensions(levelNumber).rows;
            if (_difficulty == Difficulty.UltraHard)
                return UltraHardDimensions(levelNumber).rows;
            return Grow(BaseRows, MaxRows, levelNumber);
        }

        public int BoardColsFor(int levelNumber)
        {
            if (_difficulty == Difficulty.ExtraHard)
                return ExtraHardDimensions(levelNumber).cols;
            if (_difficulty == Difficulty.UltraHard)
                return UltraHardDimensions(levelNumber).cols;
            return Grow(BaseCols, MaxCols, levelNumber);
        }

        /// <summary>Largest board dimension, retained for square fallback/test helpers.</summary>
        public int BoardSizeFor(int levelNumber) =>
            System.Math.Max(BoardRowsFor(levelNumber), BoardColsFor(levelNumber));

        private static int Grow(int start, int max, int levelNumber)
        {
            int band = System.Math.Max(0, levelNumber - 1) / 125;
            return System.Math.Min(max, start + band);
        }

        private static (int rows, int cols) ExtraHardDimensions(int levelNumber)
        {
            int index = System.Math.Max(1, levelNumber);
            if (index <= 125) return (9, 9);
            if (index <= 250) return (10, 10);
            if (index <= 375) return (12, 14);
            return (14, 16);
        }

        private static (int rows, int cols) UltraHardDimensions(int levelNumber)
        {
            int index = System.Math.Max(1, levelNumber);
            if (index <= 125) return (14, 16);
            if (index <= 250) return (14, 18);
            if (index <= 375) return (16, 18);
            if (index <= 500) return (16, 20);
            return (16, 22);
        }
    }
}
