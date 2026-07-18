using System.Collections.Generic;
using PaintMaze.Core;
using PaintMaze.Domain;

namespace PaintMaze.Game
{
    /// <summary>One legal wall-to-wall slide in a visual solution route.</summary>
    public sealed class HintRouteSegment
    {
        public SwipeDirection Direction { get; }
        public Position Start { get; }
        public Position Stop { get; }
        public IReadOnlyList<Position> Cells { get; }

        public HintRouteSegment(
            SwipeDirection direction,
            Position start,
            Position stop,
            IReadOnlyList<Position> cells)
        {
            Direction = direction;
            Start = start;
            Stop = stop;
            Cells = cells;
        }
    }

    /// <summary>
    /// Replays solver directions through the gameplay movement source of truth without
    /// mutating the level or live game state.
    /// </summary>
    public static class HintRouteBuilder
    {
        public static List<HintRouteSegment> Build(
            Level level,
            Position start,
            IReadOnlyList<SwipeDirection> directions)
        {
            var result = new List<HintRouteSegment>();
            if (level == null || directions == null) return result;

            var movement = new MovementSystem();
            Position current = start;
            for (int i = 0; i < directions.Count; i++)
            {
                SwipeDirection direction = directions[i];
                var (stop, path) = movement.Slide(level, current, direction);
                if (path.Count == 0) break;

                result.Add(new HintRouteSegment(
                    direction, current, stop, path.ToArray()));
                current = stop;
            }

            return result;
        }
    }
}
