using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Topology rules for the three-state board model. Void represents only background
    /// reachable from outside the board; enclosed non-board pockets are repaired to Wall.
    /// </summary>
    public static class LevelTopology
    {
        public static bool HasOnlyExteriorVoid(Level level) =>
            level != null && HasOnlyExteriorVoid(level.Grid);

        public static bool HasExteriorVoid(Tile[,] grid)
        {
            if (grid == null) return false;
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            if (rows == 0 || cols == 0) return false;
            for (int c = 0; c < cols; c++)
            {
                if (grid[0, c] == Tile.Void || grid[rows - 1, c] == Tile.Void)
                    return true;
            }
            for (int r = 0; r < rows; r++)
            {
                if (grid[r, 0] == Tile.Void || grid[r, cols - 1] == Tile.Void)
                    return true;
            }
            return false;
        }

        public static bool HasOnlyExteriorVoid(Tile[,] grid)
        {
            if (grid == null) return false;
            bool[,] exterior = FindExteriorVoid(grid);
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] == Tile.Void && !exterior[r, c])
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Converts every enclosed Void pocket to Wall. Returns the number of repaired
        /// cells. Edge-connected Void remains untouched as the irregular exterior.
        /// </summary>
        public static int SealEnclosedVoid(Tile[,] grid)
        {
            if (grid == null) return 0;
            bool[,] exterior = FindExteriorVoid(grid);
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            int repaired = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (grid[r, c] != Tile.Void || exterior[r, c]) continue;
                    grid[r, c] = Tile.Wall;
                    repaired++;
                }
            }
            return repaired;
        }

        private static bool[,] FindExteriorVoid(Tile[,] grid)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            var exterior = new bool[rows, cols];
            var queue = new Queue<Position>();

            void EnqueueIfVoid(int row, int col)
            {
                if (row < 0 || row >= rows || col < 0 || col >= cols) return;
                if (exterior[row, col] || grid[row, col] != Tile.Void) return;
                exterior[row, col] = true;
                queue.Enqueue(new Position(row, col));
            }

            for (int c = 0; c < cols; c++)
            {
                EnqueueIfVoid(0, c);
                EnqueueIfVoid(rows - 1, c);
            }
            for (int r = 0; r < rows; r++)
            {
                EnqueueIfVoid(r, 0);
                EnqueueIfVoid(r, cols - 1);
            }

            while (queue.Count > 0)
            {
                Position p = queue.Dequeue();
                EnqueueIfVoid(p.Row - 1, p.Col);
                EnqueueIfVoid(p.Row + 1, p.Col);
                EnqueueIfVoid(p.Row, p.Col - 1);
                EnqueueIfVoid(p.Row, p.Col + 1);
            }

            return exterior;
        }
    }
}
