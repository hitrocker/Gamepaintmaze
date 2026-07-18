using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Small deterministic bank of solver-certified arena silhouettes. These are broad
    /// floor fields with compact blocker islands, used as safe roots and publication
    /// fallbacks while the diagnostic beam search creates index-specific variants.
    /// </summary>
    internal static class ArenaExtraHardTemplateBank
    {
        public static Tile[,] Build(
            int rows,
            int cols,
            int variant,
            out Position spawn)
        {
            string[] source = SourceFor(rows, cols);
            var grid = new Tile[rows, cols];
            spawn = default;
            bool foundSpawn = false;
            for (int r = 0; r < rows; r++)
            {
                if (source[r].Length != cols)
                    throw new InvalidOperationException(
                        $"Arena template {rows}x{cols} row {r} has " +
                        $"{source[r].Length} columns.");
                for (int c = 0; c < cols; c++)
                {
                    char ch = source[r][c];
                    grid[r, c] = ch == '#' ? Tile.Wall : Tile.Floor;
                    if (ch != 'S') continue;
                    spawn = new Position(r, c);
                    foundSpawn = true;
                }
            }
            if (!foundSpawn)
                throw new InvalidOperationException(
                    $"Arena template {rows}x{cols} has no spawn.");

            ConvertExteriorWallsToVoid(grid);
            if (rows == cols && (variant & 4) != 0)
            {
                grid = Transpose(grid);
                spawn = new Position(spawn.Col, spawn.Row);
            }
            if ((variant & 1) != 0)
            {
                grid = FlipHorizontal(grid);
                spawn = new Position(spawn.Row, cols - 1 - spawn.Col);
            }
            if ((variant & 2) != 0)
            {
                grid = FlipVertical(grid);
                spawn = new Position(rows - 1 - spawn.Row, spawn.Col);
            }
            return grid;
        }

        private static string[] SourceFor(int rows, int cols)
        {
            if (rows == 9 && cols == 9)
                return new[]
                {
                    "...###...",
                    ".........",
                    ".....###.",
                    "#....###.",
                    "#.....##.",
                    ".....S##.",
                    ".........",
                    ".........",
                    "....##..."
                };
            if (rows == 10 && cols == 10)
                return new[]
                {
                    "...###....",
                    "..........",
                    ".....####.",
                    "#....####.",
                    "#.....###.",
                    ".....S###.",
                    "......###.",
                    "..........",
                    "..........",
                    "....##...."
                };
            if (rows == 12 && cols == 14)
                return new[]
                {
                    "S..#..........",
                    "........####..",
                    "....#......#..",
                    ".####.........",
                    "..........##..",
                    "..##.........#",
                    "..........#...",
                    "..####........",
                    "..#......#....",
                    ".........####.",
                    "..##..........",
                    "##############"
                };
            if (rows == 14 && cols == 16)
                return new[]
                {
                    "S...............",
                    ".....##.#####...",
                    "#.......#####...",
                    "#..#....#####...",
                    "...###.........#",
                    ".......#........",
                    "............#...",
                    "#.##............",
                    "#..........####.",
                    "#.#####.##..###.",
                    ".....##.....###.",
                    ".###........###.",
                    "............##..",
                    "...##...#...##.."
                };
            if (rows == 14 && cols == 18)
                return new[]
                {
                    "#S................",
                    "#.......#.#####...",
                    "######....#####...",
                    "######....#####...",
                    "######...........#",
                    "#........#........",
                    "...#..............",
                    "...##.............",
                    "........#.###.....",
                    "....###...###..###",
                    ".##.......###..#.#",
                    ".#####.#..##.....#",
                    ".......#..##.#....",
                    "....##.#.........."
                };
            if (rows == 16 && cols == 18)
                return new[]
                {
                    "S..##.............",
                    "........#.###.....",
                    "....###...###..###",
                    ".##.......###..#.#",
                    ".#####.#..##.....#",
                    ".......#..##.#....",
                    "....##.#..........",
                    "#........##.......",
                    "...##.....#...##..",
                    "..........#.####..",
                    ".##.........####..",
                    ".##.....#.....##..",
                    ".###....#.....##..",
                    ".###.......##.....",
                    "...........##.....",
                    "...###...#..##...."
                };
            if (rows == 16 && cols == 20)
                return new[]
                {
                    "S...#....##.......#.",
                    ".#..#..........#..#.",
                    ".#.............#....",
                    ".##...#.##...#.##...",
                    "........#...........",
                    "........#..#........",
                    "..##.......#....##..",
                    "#......#......#....#",
                    "....#...###.......#.",
                    "........###.........",
                    "........###.........",
                    ".###..#.###..#.###..",
                    ".###..#.###..#.###..",
                    ".###...........###..",
                    ".###...........###..",
                    ".###.......#...###.."
                };
            if (rows == 16 && cols == 22)
                return new[]
                {
                    "S.........##..........",
                    ".####...........####..",
                    ".###..#.........###..#",
                    ".###.....##...#.###...",
                    ".###.....#......###...",
                    "..##.....#..#....##...",
                    "..##...#....#....##...",
                    "#................#..##",
                    "....##....###....#....",
                    ".....##.#.###.........",
                    ".....##.#.###.........",
                    "..##......###..#..###.",
                    "..##....#.###..#..###.",
                    "..####............###.",
                    "..####.#..........###.",
                    "..##...#.....#....###."
                };

            throw new ArgumentOutOfRangeException(
                nameof(rows), $"No arena template for {rows}x{cols}.");
        }

        private static void ConvertExteriorWallsToVoid(Tile[,] grid)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            var exterior = new bool[rows, cols];
            var queue = new Queue<Position>();

            void Add(int row, int col)
            {
                if (row < 0 || row >= rows || col < 0 || col >= cols ||
                    exterior[row, col] || grid[row, col] != Tile.Wall)
                    return;
                exterior[row, col] = true;
                queue.Enqueue(new Position(row, col));
            }

            for (int c = 0; c < cols; c++)
            {
                Add(0, c);
                Add(rows - 1, c);
            }
            for (int r = 0; r < rows; r++)
            {
                Add(r, 0);
                Add(r, cols - 1);
            }
            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                Add(current.Row - 1, current.Col);
                Add(current.Row + 1, current.Col);
                Add(current.Row, current.Col - 1);
                Add(current.Row, current.Col + 1);
            }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (exterior[r, c])
                        grid[r, c] = Tile.Void;
        }

        private static Tile[,] FlipHorizontal(Tile[,] source)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);
            var result = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    result[r, cols - 1 - c] = source[r, c];
            return result;
        }

        private static Tile[,] FlipVertical(Tile[,] source)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);
            var result = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    result[rows - 1 - r, c] = source[r, c];
            return result;
        }

        private static Tile[,] Transpose(Tile[,] source)
        {
            int size = source.GetLength(0);
            var result = new Tile[size, size];
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    result[c, r] = source[r, c];
            return result;
        }
    }
}
