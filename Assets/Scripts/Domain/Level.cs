using System;

namespace PaintMaze.Domain
{
    /// <summary>
    /// A single puzzle: a rectangular grid of <see cref="Tile"/>s, the ball's
    /// spawn cell, and metadata. <see cref="TotalPaintable"/> caches the number
    /// of floor cells (the win target).
    /// </summary>
    public sealed class Level
    {
        public Tile[,] Grid { get; }
        public Position Spawn { get; }
        public int Rows { get; }
        public int Cols { get; }
        public int TotalPaintable { get; }

        /// <summary>Mode this level belongs to (for theming/progression display).</summary>
        public Difficulty Difficulty { get; }

        /// <summary>1-based index within its mode.</summary>
        public int Index { get; }

        public Level(Tile[,] grid, Position spawn, Difficulty difficulty = Difficulty.Easy, int index = 1)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (grid.GetLength(0) <= 0 || grid.GetLength(1) <= 0)
                throw new ArgumentException("Level grid must have at least one row and column.", nameof(grid));

            Grid = grid;
            Rows = grid.GetLength(0);
            Cols = grid.GetLength(1);
            if (spawn.Row < 0 || spawn.Row >= Rows || spawn.Col < 0 || spawn.Col >= Cols)
                throw new ArgumentOutOfRangeException(nameof(spawn), spawn, "Spawn must be inside the level grid.");
            if (grid[spawn.Row, spawn.Col] != Tile.Floor)
                throw new ArgumentException("Spawn must be on a floor tile.", nameof(spawn));

            Spawn = spawn;
            Difficulty = difficulty;
            Index = index;

            int floors = 0;
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    if (grid[r, c] == Tile.Floor) floors++;
            TotalPaintable = floors;
        }

        public bool InBounds(int row, int col) =>
            row >= 0 && row < Rows && col >= 0 && col < Cols;

        public bool IsFloor(int row, int col) =>
            InBounds(row, col) && Grid[row, col] == Tile.Floor;

        public bool IsFloor(Position p) => IsFloor(p.Row, p.Col);
    }
}
