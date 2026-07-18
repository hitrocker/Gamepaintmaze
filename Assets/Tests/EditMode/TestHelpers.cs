using PaintMaze.Domain;

namespace PaintMaze.Tests
{
    internal static class TestHelpers
    {
        /// <summary>Builds a level from rows of S/./#/_ (first 'S' is the spawn).</summary>
        public static Level Make(params string[] rows)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var grid = new Tile[h, w];
            Position spawn = new Position(0, 0);
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    char ch = rows[r][c];
                    grid[r, c] = ch == '#' ? Tile.Wall : ch == '_' ? Tile.Void : Tile.Floor;
                    if (ch == 'S') spawn = new Position(r, c);
                }
            }
            return new Level(grid, spawn);
        }
    }
}
