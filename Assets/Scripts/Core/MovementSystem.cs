using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Resolves a swipe into a slide: the ball rolls in the swipe direction until
    /// the next cell is a wall or off the board, painting every floor cell it
    /// crosses. This is the single source of truth for movement, shared by the
    /// game runtime, the solver, and the generator validator.
    /// </summary>
    public sealed class MovementSystem
    {
        /// <summary>
        /// Slides from <paramref name="from"/> in <paramref name="dir"/>.
        /// Returns the stop position and the ordered list of cells traversed,
        /// excluding the start cell (so the first element is the first cell moved
        /// into and the last element is the stop). If the ball cannot move the
        /// stop equals <paramref name="from"/> and the path is empty.
        /// </summary>
        public (Position stop, List<Position> path) Slide(Level level, Position from, SwipeDirection dir)
        {
            int dr = dir.DRow();
            int dc = dir.DCol();
            var path = new List<Position>();

            int r = from.Row;
            int c = from.Col;
            while (true)
            {
                int nr = r + dr;
                int nc = c + dc;
                if (!level.IsFloor(nr, nc)) break; // wall or edge ahead
                r = nr;
                c = nc;
                path.Add(new Position(r, c));
            }

            return (new Position(r, c), path);
        }
    }
}
