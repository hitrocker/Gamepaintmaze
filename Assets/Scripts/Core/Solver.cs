using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    public enum NeverStuckFailureKind
    {
        None,
        UncoveredFloor,
        NotStronglyConnected
    }

    public sealed class NeverStuckAnalysis
    {
        public bool Passes => FailureKind == NeverStuckFailureKind.None;
        public NeverStuckFailureKind FailureKind { get; }
        public IReadOnlyList<Position> UncoveredFloors { get; }
        public IReadOnlyList<Position> ReachableStops { get; }
        public IReadOnlyList<Position> StrandedStops { get; }
        public int CoveredFloorCount { get; }
        public int TotalFloorCount { get; }

        public NeverStuckAnalysis(
            NeverStuckFailureKind failureKind,
            IReadOnlyList<Position> uncoveredFloors,
            IReadOnlyList<Position> reachableStops,
            IReadOnlyList<Position> strandedStops,
            int coveredFloorCount,
            int totalFloorCount)
        {
            FailureKind = failureKind;
            UncoveredFloors = uncoveredFloors;
            ReachableStops = reachableStops;
            StrandedStops = strandedStops;
            CoveredFloorCount = coveredFloorCount;
            TotalFloorCount = totalFloorCount;
        }
    }

    /// <summary>
    /// Search utilities over the slide mechanic:
    ///  - <see cref="SolutionFrom"/>: bounded shortest move list for analysis.
    ///  - <see cref="SuggestProgressMove"/>: bounded stop-graph navigation for hints.
    ///  - <see cref="IsAlwaysSolvable"/>: the never-stuck guarantee.
    ///  - <see cref="SolveMinMoves"/>: difficulty / triviality gate.
    /// </summary>
    public sealed class Solver
    {
        private readonly MovementSystem _movement;

        public Solver(MovementSystem movement = null)
        {
            _movement = movement ?? new MovementSystem();
        }

        private readonly struct SearchState
        {
            public readonly Position Pos;
            public readonly string Key; // canonical painted-set signature + pos

            public SearchState(Position pos, string key)
            {
                Pos = pos;
                Key = key;
            }
        }

        private static string Signature(Position pos, HashSet<Position> painted, int rows, int cols)
        {
            // Compact bitmap signature: one char per cell, plus ball position.
            var chars = new char[rows * cols + 8];
            for (int i = 0; i < rows * cols; i++) chars[i] = '0';
            foreach (var p in painted) chars[p.Row * cols + p.Col] = '1';
            int n = rows * cols;
            chars[n] = '|';
            string posPart = pos.Row + "," + pos.Col;
            posPart.CopyTo(0, chars, n + 1, posPart.Length);
            return new string(chars, 0, n + 1 + posPart.Length);
        }

        /// <summary>
        /// Returns a shortest sequence of swipes that paints the whole board from
        /// <paramref name="fromPos"/> given <paramref name="alreadyPainted"/>, or
        /// null if none is found within <paramref name="maxStates"/>. The first
        /// element is the suggested hint move.
        /// </summary>
        public List<SwipeDirection> SolutionFrom(Level level, Position fromPos, HashSet<Position> alreadyPainted, int maxStates = 200000)
        {
            if (alreadyPainted.Count >= level.TotalPaintable) return new List<SwipeDirection>();

            var startPainted = new HashSet<Position>(alreadyPainted);
            string startSig = Signature(fromPos, startPainted, level.Rows, level.Cols);

            var visited = new HashSet<string> { startSig };
            var paintedByState = new Dictionary<string, HashSet<Position>> { [startSig] = startPainted };
            var parent = new Dictionary<string, (string prev, SwipeDirection dir)>();
            var queue = new Queue<SearchState>();
            queue.Enqueue(new SearchState(fromPos, startSig));

            int explored = 0;
            while (queue.Count > 0 && explored < maxStates)
            {
                var cur = queue.Dequeue();
                explored++;
                var curPainted = paintedByState[cur.Key];

                foreach (var dir in SwipeDirectionExtensions.All)
                {
                    var (stop, path) = _movement.Slide(level, cur.Pos, dir);
                    if (stop == cur.Pos) continue;

                    var newPainted = new HashSet<Position>(curPainted);
                    foreach (var p in path) newPainted.Add(p);

                    string sig = Signature(stop, newPainted, level.Rows, level.Cols);
                    if (!visited.Add(sig)) continue;

                    paintedByState[sig] = newPainted;
                    parent[sig] = (cur.Key, dir);

                    if (newPainted.Count >= level.TotalPaintable)
                        return Reconstruct(parent, startSig, sig);

                    queue.Enqueue(new SearchState(stop, sig));
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the first move on the shortest stop-graph route that paints at least
        /// one new cell. Unlike <see cref="SolutionFrom"/>, this search tracks only stop
        /// positions, so hint cost is bounded by the board area rather than the painted
        /// state space. Never-stuck levels guarantee that a progress move can be reached.
        /// </summary>
        public SwipeDirection? SuggestProgressMove(
            Level level,
            Position fromPos,
            HashSet<Position> alreadyPainted)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (alreadyPainted == null) throw new ArgumentNullException(nameof(alreadyPainted));
            if (alreadyPainted.Count >= level.TotalPaintable) return null;

            var visited = new HashSet<Position> { fromPos };
            var queue = new Queue<(Position position, SwipeDirection? firstMove)>();
            queue.Enqueue((fromPos, null));

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (SwipeDirection direction in SwipeDirectionExtensions.All)
                {
                    var (stop, path) = _movement.Slide(level, current.position, direction);
                    if (stop == current.position) continue;

                    SwipeDirection firstMove = current.firstMove ?? direction;
                    foreach (Position position in path)
                    {
                        if (!alreadyPainted.Contains(position))
                            return firstMove;
                    }

                    if (visited.Add(stop))
                        queue.Enqueue((stop, firstMove));
                }
            }

            return null;
        }

        private static List<SwipeDirection> Reconstruct(Dictionary<string, (string prev, SwipeDirection dir)> parent, string start, string goal)
        {
            var moves = new List<SwipeDirection>();
            string s = goal;
            while (s != start)
            {
                var (prev, dir) = parent[s];
                moves.Add(dir);
                s = prev;
            }
            moves.Reverse();
            return moves;
        }

        /// <summary>
        /// Minimum swipes to paint the whole board, or null if not solved within
        /// the budget. Doubles as a difficulty score.
        /// </summary>
        public int? SolveMinMoves(Level level, int maxStates = 200000)
        {
            var sol = SolutionFrom(level, level.Spawn, new HashSet<Position> { level.Spawn }, maxStates);
            return sol?.Count;
        }

        /// <summary>
        /// The never-stuck guarantee: true only if the level can never be played
        /// into an unsolvable state regardless of move order. Works on the slide
        /// graph (nodes = stop positions, edges = one swipe). Requires:
        ///  1. Every floor cell is covered by some slide path (paintable), and
        ///  2. The spawn-reachable component is strongly connected (the ball can
        ///     always navigate back to spawn, so no move strands it).
        /// Because painted tiles never block movement, full navigability makes
        /// move order irrelevant to solvability.
        /// </summary>
        public bool IsAlwaysSolvable(Level level) => AnalyzeNeverStuck(level).Passes;

        public NeverStuckAnalysis AnalyzeNeverStuck(Level level)
        {
            var start = level.Spawn;
            var forward = new Dictionary<Position, List<Position>>();
            var reachable = new HashSet<Position> { start };
            var covered = new HashSet<Position> { start };
            var queue = new Queue<Position>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var dir in SwipeDirectionExtensions.All)
                {
                    var (stop, path) = _movement.Slide(level, cur, dir);
                    if (stop == cur) continue;
                    foreach (var p in path) covered.Add(p);
                    if (!forward.TryGetValue(cur, out var list))
                    {
                        list = new List<Position>();
                        forward[cur] = list;
                    }
                    list.Add(stop);
                    if (reachable.Add(stop)) queue.Enqueue(stop);
                }
            }

            var uncovered = new List<Position>();
            int totalFloors = 0;
            int coveredFloors = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] != Tile.Floor) continue;
                    totalFloors++;
                    var position = new Position(r, c);
                    if (covered.Contains(position))
                        coveredFloors++;
                    else
                        uncovered.Add(position);
                }
            }

            var reverse = new Dictionary<Position, List<Position>>();
            foreach (var kv in forward)
                foreach (var to in kv.Value)
                {
                    if (!reverse.TryGetValue(to, out var list))
                    {
                        list = new List<Position>();
                        reverse[to] = list;
                    }
                    list.Add(kv.Key);
                }

            var canReachStart = new HashSet<Position> { start };
            var rq = new Queue<Position>();
            rq.Enqueue(start);
            while (rq.Count > 0)
            {
                var cur = rq.Dequeue();
                if (!reverse.TryGetValue(cur, out var prevs)) continue;
                foreach (var prev in prevs)
                    if (canReachStart.Add(prev)) rq.Enqueue(prev);
            }

            var stranded = new List<Position>();
            foreach (Position position in reachable)
                if (!canReachStart.Contains(position))
                    stranded.Add(position);

            var reachableList = new List<Position>(reachable);
            SortPositions(reachableList);
            SortPositions(stranded);
            NeverStuckFailureKind failureKind = uncovered.Count > 0
                ? NeverStuckFailureKind.UncoveredFloor
                : stranded.Count > 0
                    ? NeverStuckFailureKind.NotStronglyConnected
                    : NeverStuckFailureKind.None;

            return new NeverStuckAnalysis(
                failureKind,
                uncovered,
                reachableList,
                stranded,
                coveredFloors,
                totalFloors);
        }

        private static void SortPositions(List<Position> positions)
        {
            positions.Sort((a, b) =>
            {
                int row = a.Row.CompareTo(b.Row);
                return row != 0 ? row : a.Col.CompareTo(b.Col);
            });
        }
    }
}
