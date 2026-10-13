using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Readable wall grammars. Short-bar scatter remains one option; the others are
    /// structures a player can see: a room, a divider, an island, or a side pocket.
    /// </summary>
    public enum LevelLayoutFamilyKind
    {
        ShortBarScatter = 0,
        RoomWithGap = 1,
        LongDivider = 2,
        WallIsland = 3,
        SidePocket = 4
    }

    /// <summary>
    /// Picks and recognizes layout families. Recognition is geometric so a rotation
    /// of the same structure still counts as that family.
    /// </summary>
    public static class LevelLayoutFamily
    {
        public const int Count = 5;

        public static LevelLayoutFamilyKind Preferred(Difficulty difficulty, int levelNumber)
        {
            int index = Math.Max(1, levelNumber);
            int seed = LevelSeed.For(
                difficulty, index, LevelGenerator.VersionFor(difficulty), 0x474D4152);
            return (LevelLayoutFamilyKind)new DeterministicRng(seed).Next(Count);
        }

        public static LevelLayoutFamilyKind Classify(Level level)
        {
            if (level == null) return LevelLayoutFamilyKind.ShortBarScatter;
            if (HasRoomWithSingleGap(level)) return LevelLayoutFamilyKind.RoomWithGap;
            if (HasSidePocket(level)) return LevelLayoutFamilyKind.SidePocket;
            if (HasLongDivider(level)) return LevelLayoutFamilyKind.LongDivider;
            if (HasCompactIsland(level)) return LevelLayoutFamilyKind.WallIsland;
            return LevelLayoutFamilyKind.ShortBarScatter;
        }

        private static bool HasRoomWithSingleGap(Level level)
        {
            int rows = level.Rows;
            int cols = level.Cols;
            for (int r0 = 0; r0 < rows; r0++)
            {
                for (int c0 = 0; c0 < cols; c0++)
                {
                    for (int r1 = r0 + 3; r1 < rows; r1++)
                    {
                        for (int c1 = c0 + 3; c1 < cols; c1++)
                        {
                            if (IsSingleGapRoom(level, r0, c0, r1, c1))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsSingleGapRoom(Level level, int r0, int c0, int r1, int c1)
        {
            int walls = 0;
            int floors = 0;
            int other = 0;
            for (int r = r0; r <= r1; r++)
            {
                for (int c = c0; c <= c1; c++)
                {
                    bool edge = r == r0 || r == r1 || c == c0 || c == c1;
                    if (!edge) continue;
                    switch (level.Grid[r, c])
                    {
                        case Tile.Wall: walls++; break;
                        case Tile.Floor: floors++; break;
                        default: other++; break;
                    }
                }
            }

            if (floors != 1 || other > 0 || walls < 8) return false;

            int interiorFloors = 0;
            for (int r = r0 + 1; r < r1; r++)
            {
                for (int c = c0 + 1; c < c1; c++)
                {
                    if (level.Grid[r, c] == Tile.Floor) interiorFloors++;
                }
            }

            return interiorFloors >= 2;
        }

        private static bool HasSidePocket(Level level)
        {
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (level.Grid[r, c] != Tile.Floor) continue;
                    if (FloorNeighbors(level, r, c) != 1) continue;
                    if (WallsInNeighborhood(level, r, c) >= 5) return true;
                }
            }

            return false;
        }

        private static bool HasLongDivider(Level level)
        {
            int span = Math.Max(level.Rows, level.Cols);
            int minimumWalls = Math.Max(4, span - 2);
            for (int r = 0; r < level.Rows; r++)
            {
                if (IsDividerLine(level, r, horizontal: true, minimumWalls)) return true;
            }

            for (int c = 0; c < level.Cols; c++)
            {
                if (IsDividerLine(level, c, horizontal: false, minimumWalls)) return true;
            }

            return false;
        }

        private static bool IsDividerLine(Level level, int fixedIndex, bool horizontal, int minimumWalls)
        {
            int length = horizontal ? level.Cols : level.Rows;
            int walls = 0;
            int occupied = 0;
            int gaps = 0;
            bool inGap = false;
            bool seenWall = false;
            for (int i = 0; i < length; i++)
            {
                int r = horizontal ? fixedIndex : i;
                int c = horizontal ? i : fixedIndex;
                Tile tile = level.Grid[r, c];
                if (tile == Tile.Void) continue;
                occupied++;
                if (tile == Tile.Wall)
                {
                    walls++;
                    if (inGap && seenWall) gaps++;
                    inGap = false;
                    seenWall = true;
                }
                else if (seenWall)
                {
                    inGap = true;
                }
            }

            if (inGap) gaps = Math.Max(0, gaps);
            return walls >= minimumWalls &&
                   walls >= 4 &&
                   gaps <= 1 &&
                   occupied > 0 &&
                   walls * 2 >= occupied;
        }

        private static bool HasCompactIsland(Level level)
        {
            var seen = new bool[level.Rows, level.Cols];
            for (int r = 0; r < level.Rows; r++)
            {
                for (int c = 0; c < level.Cols; c++)
                {
                    if (seen[r, c] || level.Grid[r, c] != Tile.Wall) continue;
                    if (IsCompactComponent(level, seen, r, c)) return true;
                }
            }

            return false;
        }

        private static bool IsCompactComponent(Level level, bool[,] seen, int startRow, int startCol)
        {
            var queue = new Queue<Position>();
            queue.Enqueue(new Position(startRow, startCol));
            seen[startRow, startCol] = true;
            int count = 0;
            int minR = startRow, maxR = startRow, minC = startCol, maxC = startCol;
            while (queue.Count > 0)
            {
                Position current = queue.Dequeue();
                count++;
                if (current.Row < minR) minR = current.Row;
                if (current.Row > maxR) maxR = current.Row;
                if (current.Col < minC) minC = current.Col;
                if (current.Col > maxC) maxC = current.Col;
                TryEnqueueWall(level, seen, queue, current.Row - 1, current.Col);
                TryEnqueueWall(level, seen, queue, current.Row + 1, current.Col);
                TryEnqueueWall(level, seen, queue, current.Row, current.Col - 1);
                TryEnqueueWall(level, seen, queue, current.Row, current.Col + 1);
            }

            int height = maxR - minR + 1;
            int width = maxC - minC + 1;
            int box = height * width;
            return count >= 4 &&
                   count <= 12 &&
                   height <= 4 &&
                   width <= 4 &&
                   box > 0 &&
                   count * 2 >= box;
        }

        private static void TryEnqueueWall(
            Level level, bool[,] seen, Queue<Position> queue, int row, int col)
        {
            if (row < 0 || col < 0 || row >= level.Rows || col >= level.Cols) return;
            if (seen[row, col] || level.Grid[row, col] != Tile.Wall) return;
            seen[row, col] = true;
            queue.Enqueue(new Position(row, col));
        }

        private static int FloorNeighbors(Level level, int row, int col)
        {
            int count = 0;
            if (level.IsFloor(row - 1, col)) count++;
            if (level.IsFloor(row + 1, col)) count++;
            if (level.IsFloor(row, col - 1)) count++;
            if (level.IsFloor(row, col + 1)) count++;
            return count;
        }

        private static int WallsInNeighborhood(Level level, int row, int col)
        {
            int walls = 0;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int r = row + dr;
                    int c = col + dc;
                    if (r < 0 || c < 0 || r >= level.Rows || c >= level.Cols) continue;
                    if (level.Grid[r, c] == Tile.Wall) walls++;
                }
            }

            return walls;
        }
    }
}
