using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Parses authorable level packs from plain text. A pack contains one or more
    /// level blocks. A block is a run of consecutive grid rows; blocks are
    /// separated by blank lines or any non-grid line (which acts as a comment).
    ///
    /// Grid characters:
    ///   'S' = spawn (exactly one per level)   '.' = floor
    ///   '#' = raised wall                     '_' = exterior void
    ///
    /// A line is treated as a grid row only if it is non-empty and every char is
    /// one of S . # _ — so human headers like "Easy 1:" are ignored as comments.
    /// </summary>
    public static class LevelParser
    {
        public static bool IsGridRow(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            foreach (char ch in line)
                if (ch != 'S' && ch != '.' && ch != '#' && ch != '_') return false;
            return true;
        }

        /// <summary>
        /// Parses all level blocks. Malformed blocks (non-rectangular, no/many
        /// spawns) are skipped. <paramref name="errors"/> collects reasons.
        /// </summary>
        public static List<Level> Parse(string text, Difficulty difficulty, List<string> errors = null)
        {
            var levels = new List<Level>();
            if (string.IsNullOrEmpty(text)) return levels;

            var rows = new List<string>();
            int index = 1;

            void Flush()
            {
                if (rows.Count == 0) return;
                var built = BuildLevel(rows, difficulty, index, errors);
                if (built != null)
                {
                    levels.Add(built);
                    index++;
                }
                rows.Clear();
            }

            foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (IsGridRow(line)) rows.Add(line);
                else Flush(); // blank or comment ends the current block
            }
            Flush();

            return levels;
        }

        private static Level BuildLevel(List<string> rows, Difficulty difficulty, int index, List<string> errors)
        {
            int height = rows.Count;
            int width = rows[0].Length;
            for (int r = 0; r < height; r++)
            {
                if (rows[r].Length != width)
                {
                    errors?.Add($"{difficulty} #{index}: non-rectangular (row {r})");
                    return null;
                }
            }

            var grid = new Tile[height, width];
            bool hasSpawn = false;
            Position spawn = default;
            int spawnCount = 0;

            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    char ch = rows[r][c];
                    if (ch == '#')
                    {
                        grid[r, c] = Tile.Wall;
                    }
                    else if (ch == '_')
                    {
                        grid[r, c] = Tile.Void;
                    }
                    else
                    {
                        grid[r, c] = Tile.Floor;
                        if (ch == 'S')
                        {
                            hasSpawn = true;
                            spawn = new Position(r, c);
                            spawnCount++;
                        }
                    }
                }
            }

            if (!hasSpawn || spawnCount != 1)
            {
                errors?.Add($"{difficulty} #{index}: expected exactly one 'S' (found {spawnCount})");
                return null;
            }

            return new Level(grid, spawn, difficulty, index);
        }
    }
}
