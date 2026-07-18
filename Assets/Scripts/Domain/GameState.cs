using System.Collections.Generic;

namespace PaintMaze.Domain
{
    /// <summary>
    /// Mutable in-session game state for one level: which cells are painted,
    /// where the ball is, how many moves were made, and whether the board is
    /// fully painted. The presentation layer reads this to render.
    /// </summary>
    public sealed class GameState
    {
        public Level Level { get; }
        public HashSet<Position> Painted { get; }
        public Position BallPos { get; private set; }
        public int MoveCount { get; private set; }
        public bool IsComplete { get; private set; }

        /// <summary>Cells traversed by the most recent move (for trail animation).</summary>
        public IReadOnlyList<Position> LastPath { get; private set; }

        public GameState(Level level)
        {
            Level = level;
            Painted = new HashSet<Position> { level.Spawn };
            BallPos = level.Spawn;
            MoveCount = 0;
            IsComplete = false;
            LastPath = new List<Position>();
        }

        private GameState(
            Level level,
            HashSet<Position> painted,
            Position ballPos,
            int moveCount)
        {
            Level = level;
            Painted = painted;
            BallPos = ballPos;
            MoveCount = moveCount;
            IsComplete = painted.Count >= level.TotalPaintable;
            LastPath = new List<Position>();
        }

        public static bool TryRestore(
            Level level,
            Position ballPos,
            IEnumerable<Position> paintedPositions,
            int moveCount,
            out GameState state)
        {
            state = null;
            if (level == null || paintedPositions == null || moveCount <= 0 ||
                !level.IsFloor(ballPos))
                return false;

            var painted = new HashSet<Position>();
            foreach (Position position in paintedPositions)
            {
                if (!level.IsFloor(position) || !painted.Add(position))
                    return false;
            }

            if (!painted.Contains(level.Spawn) ||
                !painted.Contains(ballPos) ||
                painted.Count >= level.TotalPaintable)
                return false;

            state = new GameState(level, painted, ballPos, moveCount);
            return true;
        }

        public float ProgressFraction =>
            Level.TotalPaintable == 0 ? 1f : (float)Painted.Count / Level.TotalPaintable;

        public int ProgressPercent => (int)(ProgressFraction * 100f + 0.5f);

        /// <summary>
        /// Applies a resolved slide: moves the ball to <paramref name="stop"/> and
        /// paints every cell in <paramref name="path"/>. Returns the cells that were
        /// newly painted (not previously in the set).
        /// </summary>
        public List<Position> Apply(Position stop, IReadOnlyList<Position> path)
        {
            var newly = new List<Position>();
            foreach (var p in path)
                if (Painted.Add(p)) newly.Add(p);

            BallPos = stop;
            LastPath = path;
            MoveCount++;
            IsComplete = Painted.Count >= Level.TotalPaintable;
            return newly;
        }
    }
}
