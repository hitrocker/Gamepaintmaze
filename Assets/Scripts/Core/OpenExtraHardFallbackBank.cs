using System;
using System.Collections.Generic;
using PaintMaze.Domain;
using UnityEngine;

namespace PaintMaze.Core
{
    /// <summary>
    /// Preloads one certified arena root for every Extra Hard physical size on the main
    /// thread. Selection and transformation are then pure managed operations, so
    /// asynchronous generation never calls the Unity Resources API from a worker thread.
    /// </summary>
    public static class OpenExtraHardFallbackBank
    {
        private static readonly string[] ResourcePaths =
        {
            "Levels/Fallback/open_9x9",
            "Levels/Fallback/open_10x10",
            "Levels/Fallback/open_12x14",
            "Levels/Fallback/open_14x16",
            "Levels/Fallback/open_14x18",
            "Levels/Fallback/open_16x18",
            "Levels/Fallback/open_16x20",
            "Levels/Fallback/open_16x22"
        };

        public static IReadOnlyList<Level> Load()
        {
            var accepted = new List<Level>();
            var solver = new Solver();
            var analyzer = new LevelLayoutAnalyzer();
            foreach (string resourcePath in ResourcePaths)
            {
                TextAsset asset = Resources.Load<TextAsset>(resourcePath);
                if (asset == null) continue;
                var errors = new List<string>();
                List<Level> levels = LevelParser.Parse(
                    asset.text, Difficulty.UltraHard, errors);
                if (errors.Count > 0) continue;
                foreach (Level level in levels)
                {
                    if (!IsSupportedSize(level.Rows, level.Cols) ||
                        !LevelSafetyValidator.IsSafe(level, solver, out _))
                        continue;
                    LevelLayoutMetrics layout = analyzer.Analyze(level);
                    if (LevelLayoutAnalyzer.MeetsExtraHardFloor(
                            level, layout, out _))
                        accepted.Add(level);
                }
            }
            return accepted;
        }

        public static bool TrySelect(
            IReadOnlyList<Level> templates,
            int rows,
            int cols,
            Difficulty difficulty,
            int index,
            out Level level)
        {
            level = null;
            if (templates == null || templates.Count == 0 ||
                !IsSupportedSize(rows, cols))
                return false;

            int mixed = unchecked(index * 1103515245 + (int)difficulty * 12345);
            int matching = 0;
            foreach (Level template in templates)
                if (template.Rows == rows && template.Cols == cols)
                    matching++;
            if (matching == 0) return false;
            int selectedMatch = (int)((uint)mixed % (uint)matching);
            Level source = null;
            foreach (Level template in templates)
            {
                if (template.Rows != rows || template.Cols != cols) continue;
                if (selectedMatch-- != 0) continue;
                source = template;
                break;
            }
            if (source == null) return false;
            int transform = (int)(((uint)mixed >> 16) & 3u);
            Tile[,] grid = Transform(source.Grid, transform);
            Position spawn = Transform(source.Spawn, rows, cols, transform);
            level = new Level(grid, spawn, difficulty, index);
            return true;
        }

        private static bool IsSupportedSize(int rows, int cols)
        {
            return (rows == 9 && cols == 9) ||
                   (rows == 10 && cols == 10) ||
                   (rows == 12 && cols == 14) ||
                   (rows == 14 && cols == 16) ||
                   (rows == 14 && cols == 18) ||
                   (rows == 16 && cols == 18) ||
                   (rows == 16 && cols == 20) ||
                   (rows == 16 && cols == 22);
        }

        private static Tile[,] Transform(Tile[,] source, int transform)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);
            var result = new Tile[rows, cols];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int sourceRow = (transform & 2) != 0 ? rows - 1 - r : r;
                    int sourceCol = (transform & 1) != 0 ? cols - 1 - c : c;
                    result[r, c] = source[sourceRow, sourceCol];
                }
            }
            return result;
        }

        private static Position Transform(
            Position source,
            int rows,
            int cols,
            int transform)
        {
            int row = (transform & 2) != 0
                ? rows - 1 - source.Row
                : source.Row;
            int col = (transform & 1) != 0
                ? cols - 1 - source.Col
                : source.Col;
            return new Position(row, col);
        }
    }
}
