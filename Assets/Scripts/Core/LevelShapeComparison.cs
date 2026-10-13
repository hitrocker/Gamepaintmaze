using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>Slide-length signals used to reject one-cell grinds and single huge slides.</summary>
    public readonly struct LevelSlideStats
    {
        public int SampleCount { get; }
        public int Longest { get; }
        public double OneCellRatio { get; }
        public double LengthStdDev { get; }
        public int PocketStopCount { get; }

        public LevelSlideStats(
            int sampleCount,
            int longest,
            double oneCellRatio,
            double lengthStdDev,
            int pocketStopCount)
        {
            SampleCount = sampleCount;
            Longest = longest;
            OneCellRatio = oneCellRatio;
            LengthStdDev = lengthStdDev;
            PocketStopCount = pocketStopCount;
        }

        public static LevelSlideStats Measure(Level level, MovementSystem movement = null)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            movement ??= new MovementSystem();

            var lengths = new List<int>();
            int pockets = 0;
            var visited = new HashSet<Position> { level.Spawn };
            var queue = new Queue<Position>();
            queue.Enqueue(level.Spawn);
            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                int legal = 0;
                foreach (SwipeDirection direction in SwipeDirectionExtensions.All)
                {
                    var slide = movement.Slide(level, current, direction);
                    if (slide.path.Count == 0) continue;
                    legal++;
                    lengths.Add(slide.path.Count);
                    if (visited.Add(slide.stop))
                        queue.Enqueue(slide.stop);
                }

                if (legal == 1) pockets++;
            }

            int longest = 0;
            int oneCell = 0;
            double sum = 0;
            foreach (int length in lengths)
            {
                if (length > longest) longest = length;
                if (length == 1) oneCell++;
                sum += length;
            }

            double mean = lengths.Count > 0 ? sum / lengths.Count : 0;
            double variance = 0;
            foreach (int length in lengths)
            {
                double delta = length - mean;
                variance += delta * delta;
            }

            if (lengths.Count > 0) variance /= lengths.Count;
            double oneCellRatio = lengths.Count > 0 ? (double)oneCell / lengths.Count : 0;
            return new LevelSlideStats(
                lengths.Count,
                longest,
                oneCellRatio,
                Math.Sqrt(variance),
                pockets);
        }
    }

    /// <summary>
    /// Orientation-invariant shape of an accepted board. Rotations and mirrors share
    /// a fingerprint, so a flipped copy compares as the same puzzle.
    /// </summary>
    public sealed class LevelShapeProfile
    {
        public int Rows { get; }
        public int Cols { get; }
        public double WallRatio { get; }
        public int LongestSlide { get; }
        public int Turns { get; }
        public int Reversals { get; }
        public int OpenFloorCoreCount { get; }
        public double Silhouette { get; }
        public ulong FingerprintHash { get; }
        public byte[] CanonicalBytes { get; }
        public LevelLayoutFamilyKind Family { get; }

        private LevelShapeProfile(
            int rows,
            int cols,
            double wallRatio,
            int longestSlide,
            int turns,
            int reversals,
            int openFloorCoreCount,
            double silhouette,
            ulong fingerprintHash,
            byte[] canonicalBytes,
            LevelLayoutFamilyKind family)
        {
            Rows = rows;
            Cols = cols;
            WallRatio = wallRatio;
            LongestSlide = longestSlide;
            Turns = turns;
            Reversals = reversals;
            OpenFloorCoreCount = openFloorCoreCount;
            Silhouette = silhouette;
            FingerprintHash = fingerprintHash;
            CanonicalBytes = canonicalBytes;
            Family = family;
        }

        public static LevelShapeProfile Capture(Level level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            var movement = new MovementSystem();
            LevelSlideStats slides = LevelSlideStats.Measure(level, movement);
            LevelLayoutMetrics layout = new LevelLayoutAnalyzer(movement).Analyze(level);
            MeasureRoute(level, movement, out int turns, out int reversals);
            return FromParts(level, slides, layout, turns, reversals);
        }

        public static LevelShapeProfile FromParts(
            Level level,
            LevelSlideStats slides,
            LevelLayoutMetrics layout,
            int turns,
            int reversals)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            int walls = 0;
            int occupied = 0;
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] == Tile.Void) continue;
                    occupied++;
                    if (level.Grid[r, c] == Tile.Wall) walls++;
                }
            }

            LevelFingerprint fingerprint = LevelFingerprint.From(level);
            double wallRatio = occupied > 0 ? (double)walls / occupied : 0;
            return new LevelShapeProfile(
                level.Rows,
                level.Cols,
                wallRatio,
                slides.Longest,
                turns,
                reversals,
                layout?.OpenFloorCoreCount ?? 0,
                layout?.SilhouetteConcaveCornerCount ?? 0,
                fingerprint.Hash,
                fingerprint.CanonicalBytes,
                LevelLayoutFamily.Classify(level));
        }

        private static void MeasureRoute(
            Level level,
            MovementSystem movement,
            out int turns,
            out int reversals)
        {
            turns = 0;
            reversals = 0;
            var painted = new HashSet<Position> { level.Spawn };
            Position current = level.Spawn;
            SwipeDirection? previous = null;
            int guard = Math.Max(8, level.TotalPaintable * 4);
            for (int step = 0; step < guard && painted.Count < level.TotalPaintable; step++)
            {
                SwipeDirection? bestDir = null;
                int bestGain = -1;
                Position bestStop = current;
                List<Position> bestPath = null;
                foreach (SwipeDirection direction in SwipeDirectionExtensions.All)
                {
                    var slide = movement.Slide(level, current, direction);
                    if (slide.path.Count == 0) continue;
                    int gain = 0;
                    foreach (Position position in slide.path)
                    {
                        if (!painted.Contains(position)) gain++;
                    }

                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestDir = direction;
                        bestStop = slide.stop;
                        bestPath = slide.path;
                    }
                }

                if (!bestDir.HasValue || bestPath == null || bestGain <= 0) break;
                if (previous.HasValue && previous.Value != bestDir.Value)
                {
                    turns++;
                    if (IsReverse(previous.Value, bestDir.Value)) reversals++;
                }

                foreach (Position position in bestPath) painted.Add(position);
                current = bestStop;
                previous = bestDir;
            }
        }

        private static bool IsReverse(SwipeDirection a, SwipeDirection b) =>
            a.DRow() == -b.DRow() && a.DCol() == -b.DCol();
    }

    /// <summary>
    /// Maze-Paint style comparison against recently accepted puzzles, plus the
    /// smaller / prettier / fewer-walls / less-messy preferences.
    /// </summary>
    public static class LevelShapeComparison
    {
        public const int RecentWindow = 5;

        public static bool HasReadableSlides(Level level, LevelSlideStats slides)
        {
            if (level == null || slides.SampleCount <= 0) return false;
            if (slides.SampleCount >= 6 && slides.OneCellRatio >= 0.75) return false;
            if (level.TotalPaintable >= 6 &&
                slides.Longest >= level.TotalPaintable * 0.65)
                return false;
            return true;
        }

        public static bool SameBoard(LevelShapeProfile a, LevelShapeProfile b)
        {
            if (a == null || b == null) return false;
            if (a.FingerprintHash != b.FingerprintHash) return false;
            return LevelFingerprint.CanonicalBytesEqual(a.CanonicalBytes, b.CanonicalBytes);
        }

        public static bool IsNearDuplicate(LevelShapeProfile a, LevelShapeProfile b)
        {
            if (a == null || b == null) return false;
            if (SameBoard(a, b)) return true;
            if (!SizesMatch(a, b)) return false;
            if (Math.Abs(a.WallRatio - b.WallRatio) > 0.04) return false;
            if (Math.Abs(a.LongestSlide - b.LongestSlide) > 1) return false;
            if (Math.Abs(a.Turns - b.Turns) > 1) return false;
            if (Math.Abs(a.Reversals - b.Reversals) > 1) return false;
            if (Math.Abs(a.OpenFloorCoreCount - b.OpenFloorCoreCount) > 1) return false;
            double silhouetteTolerance = Math.Max(
                2.0, 0.12 * Math.Max(a.Silhouette, b.Silhouette));
            return Math.Abs(a.Silhouette - b.Silhouette) <= silhouetteTolerance;
        }

        public static bool IsRejected(Level level, IReadOnlyList<LevelShapeProfile> recent)
        {
            if (level == null || recent == null || recent.Count == 0) return false;
            LevelShapeProfile profile = LevelShapeProfile.Capture(level);
            return IsRejected(profile, recent);
        }

        public static bool IsRejected(LevelShapeProfile profile, IReadOnlyList<LevelShapeProfile> recent)
        {
            if (profile == null || recent == null) return false;
            foreach (LevelShapeProfile past in recent)
            {
                if (IsNearDuplicate(profile, past)) return true;
            }

            return false;
        }

        /// <summary>
        /// 1 when the boards are the same puzzle under rotation or mirror, otherwise
        /// the fraction of shape axes that still agree.
        /// </summary>
        public static double GetSimilarityScore(LevelShapeProfile a, LevelShapeProfile b)
        {
            if (a == null || b == null) return 0;
            if (SameBoard(a, b)) return 1;

            double size = SizesMatch(a, b) ? 1 : 0;
            double walls = Band(Math.Abs(a.WallRatio - b.WallRatio), 0.12);
            double slide = Band(Math.Abs(a.LongestSlide - b.LongestSlide), 4);
            double route = (Band(Math.Abs(a.Turns - b.Turns), 4) +
                            Band(Math.Abs(a.Reversals - b.Reversals), 2)) * 0.5;
            double core = Band(Math.Abs(a.OpenFloorCoreCount - b.OpenFloorCoreCount), 4);
            double silhouetteScale = Math.Max(6.0, 0.35 * Math.Max(a.Silhouette, b.Silhouette));
            double silhouette = Band(Math.Abs(a.Silhouette - b.Silhouette), silhouetteScale);
            return (size + walls + slide + route + core + silhouette) / 6.0;
        }

        public static double SimilarityPenalty(
            LevelShapeProfile candidate,
            IReadOnlyList<LevelShapeProfile> recent)
        {
            if (candidate == null || recent == null || recent.Count == 0) return 0;
            double penalty = 0;
            foreach (LevelShapeProfile past in recent)
            {
                if (past == null) continue;
                if (SameBoard(candidate, past)) return 40;
                if (IsNearDuplicate(candidate, past))
                    penalty = Math.Max(penalty, 22);
                else
                    penalty = Math.Max(penalty, GetSimilarityScore(candidate, past) * 3.0);
            }

            return penalty;
        }

        /// <summary>
        /// Secondary tastes applied after solution shape: smaller boards, a cleaner
        /// silhouette, fewer walls, and less isolated-wall mess.
        /// </summary>
        public static double StyleBonus(Level level, LevelQualityMetrics quality)
        {
            if (level == null || quality == null) return 0;
            int cells = Math.Max(1, level.Rows * level.Cols);
            double smaller = Math.Max(0, 120 - cells) * 0.015;
            int occupied = Math.Max(1, quality.WallCount + quality.FloorCount);
            double wallRatio = (double)quality.WallCount / occupied;
            double fewerWalls = Math.Max(0, 0.28 - wallRatio) * 5.0;
            int corners = quality.Layout?.SilhouetteConcaveCornerCount ?? 0;
            double prettier = corners >= 1 && corners <= 6 ? 1.1 : 0;
            prettier += (1.0 - quality.IsolatedWallRatio) * 1.4;
            double messy = quality.IsolatedWallRatio * 8.0;
            if (quality.Layout != null && quality.Layout.ParallelSeparatorWallRatio > 0.75)
                messy += 2.0;
            return smaller + fewerWalls + prettier - messy;
        }

        private static bool SizesMatch(LevelShapeProfile a, LevelShapeProfile b)
        {
            int aMin = Math.Min(a.Rows, a.Cols);
            int aMax = Math.Max(a.Rows, a.Cols);
            int bMin = Math.Min(b.Rows, b.Cols);
            int bMax = Math.Max(b.Rows, b.Cols);
            return aMin == bMin && aMax == bMax;
        }

        private static double Band(double delta, double fullScale)
        {
            if (fullScale <= 0) return delta == 0 ? 1 : 0;
            double t = 1.0 - delta / fullScale;
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }
    }
}
