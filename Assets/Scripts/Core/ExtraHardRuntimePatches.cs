using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Narrow publication patches for runtime boards whose v6 grid is valid but whose
    /// original spawn makes the canonical board duplicate an earlier packaged level.
    /// Grid bytes remain unchanged; only a solver-certified alternate Floor spawn is used.
    /// </summary>
    public static class ExtraHardRuntimePatches
    {
        public static Level ApplySource(Level level, Difficulty sourceDifficulty, int sourceIndex)
        {
            if (level == null || sourceDifficulty != Difficulty.UltraHard)
                return level;

            return sourceIndex switch
            {
                3267 => WithSpawn(level, new Position(0, 0)),
                _ => level
            };
        }

        public static Level ApplyVisible(Level level, Difficulty difficulty, int visibleIndex)
        {
            if (level == null || difficulty != Difficulty.ExtraHard)
                return level;
            DifficultyCatalog.ResolveGenerationSource(
                difficulty,
                visibleIndex,
                out Difficulty sourceDifficulty,
                out int sourceIndex);
            return ApplySource(level, sourceDifficulty, sourceIndex);
        }

        private static Level WithSpawn(Level level, Position spawn)
        {
            if (level.Spawn == spawn || level.Grid[spawn.Row, spawn.Col] != Tile.Floor)
                return level;
            return new Level(level.Grid, spawn, level.Difficulty, level.Index);
        }
    }
}
