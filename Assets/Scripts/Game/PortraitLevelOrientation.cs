using System;
using PaintMaze.Domain;

namespace PaintMaze.Game
{
    /// <summary>
    /// Creates a session-only portrait copy of a visibly landscape level.
    /// Provider, cache, and baked level bytes remain in their canonical orientation.
    /// </summary>
    public static class PortraitLevelOrientation
    {
        public static Level Apply(Level level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (!HasLandscapeFloorBounds(level)) return level;

            var rotated = new Tile[level.Cols, level.Rows];
            for (int row = 0; row < level.Rows; row++)
            {
                for (int col = 0; col < level.Cols; col++)
                    rotated[col, level.Rows - 1 - row] = level.Grid[row, col];
            }

            var spawn = new Position(
                level.Spawn.Col,
                level.Rows - 1 - level.Spawn.Row);
            return new Level(rotated, spawn, level.Difficulty, level.Index);
        }

        public static bool HasLandscapeFloorBounds(Level level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));

            int minRow = level.Rows;
            int maxRow = -1;
            int minCol = level.Cols;
            int maxCol = -1;
            for (int row = 0; row < level.Rows; row++)
            {
                for (int col = 0; col < level.Cols; col++)
                {
                    if (level.Grid[row, col] != Tile.Floor) continue;
                    minRow = Math.Min(minRow, row);
                    maxRow = Math.Max(maxRow, row);
                    minCol = Math.Min(minCol, col);
                    maxCol = Math.Max(maxCol, col);
                }
            }

            // Level guarantees that its spawn is Floor, so an empty bound is defensive.
            if (maxRow < minRow || maxCol < minCol) return false;
            int renderedRows = maxRow - minRow + 1;
            int renderedCols = maxCol - minCol + 1;
            return renderedCols > renderedRows;
        }
    }
}
