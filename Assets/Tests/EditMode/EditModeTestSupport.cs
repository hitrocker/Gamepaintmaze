using System;
using System.IO;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Tests
{
    internal static class EditModeTestSupport
    {
        public static readonly Difficulty[] AllDifficulties =
        {
            Difficulty.Easy,
            Difficulty.Medium,
            Difficulty.Hard,
            Difficulty.ExtraHard,
            Difficulty.UltraHard
        };

        public static readonly Difficulty[] PlayableDifficulties =
        {
            Difficulty.Easy,
            Difficulty.Medium,
            Difficulty.Hard,
            Difficulty.ExtraHard
        };

        public static string CreateTempDirectory(string prefix = "pm-editmode-")
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static void DeleteDirectoryIfExists(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try { Directory.Delete(path, true); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }

        public static void ClearPlayerPrefs()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        public static Tile[,] CloneGrid(Tile[,] grid)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            var copy = new Tile[rows, cols];
            Array.Copy(grid, copy, grid.Length);
            return copy;
        }

        public static bool GridsEqual(Tile[,] a, Tile[,] b)
        {
            if (a.GetLength(0) != b.GetLength(0) || a.GetLength(1) != b.GetLength(1))
                return false;
            int rows = a.GetLength(0);
            int cols = a.GetLength(1);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    if (a[r, c] != b[r, c]) return false;
            return true;
        }

        public static Level TransformLevel(Level level, int rotation, bool mirror)
        {
            int rows = level.Rows;
            int cols = level.Cols;
            int outRows = rotation % 2 == 0 ? rows : cols;
            int outCols = rotation % 2 == 0 ? cols : rows;
            var grid = new Tile[outRows, outCols];
            Position spawn = TransformPosition(level.Spawn, rows, cols, rotation, mirror);

            for (int r = 0; r < outRows; r++)
            {
                for (int c = 0; c < outCols; c++)
                {
                    Position source = InverseTransform(r, c, rows, cols, rotation, mirror);
                    grid[r, c] = level.Grid[source.Row, source.Col];
                }
            }

            return new Level(grid, spawn, level.Difficulty, level.Index);
        }

        private static Position TransformPosition(Position p, int rows, int cols, int rotation, bool mirror)
        {
            int r = p.Row;
            int c = p.Col;
            if (mirror) c = cols - 1 - c;

            return rotation switch
            {
                0 => new Position(r, c),
                1 => new Position(c, rows - 1 - r),
                2 => new Position(rows - 1 - r, cols - 1 - c),
                3 => new Position(cols - 1 - c, r),
                _ => throw new ArgumentOutOfRangeException(nameof(rotation))
            };
        }

        private static Position InverseTransform(int r, int c, int rows, int cols, int rotation, bool mirror)
        {
            int srcR;
            int srcC;
            switch (rotation)
            {
                case 0:
                    srcR = r;
                    srcC = c;
                    break;
                case 1:
                    srcR = rows - 1 - c;
                    srcC = r;
                    break;
                case 2:
                    srcR = rows - 1 - r;
                    srcC = cols - 1 - c;
                    break;
                case 3:
                    srcR = c;
                    srcC = cols - 1 - r;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rotation));
            }

            if (mirror) srcC = cols - 1 - srcC;
            return new Position(srcR, srcC);
        }
    }
}
