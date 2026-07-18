using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Canonical board identity invariant under rotation and mirror symmetries.
    /// </summary>
    public sealed class LevelFingerprint
    {
        public const int GeneratorVersion = LevelGenerator.LegacyGeneratorVersion;
        public const int ExtraHardGeneratorVersion = LevelGenerator.GeneratorVersion;

        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public ulong Hash { get; }
        public byte[] CanonicalBytes { get; }

        private LevelFingerprint(ulong hash, byte[] canonicalBytes)
        {
            Hash = hash;
            CanonicalBytes = canonicalBytes;
        }

        public static LevelFingerprint From(Level level) =>
            From(level.Grid, level.Spawn, GeneratorVersion);

        public static LevelFingerprint From(Tile[,] grid, Position spawn)
            => From(grid, spawn, GeneratorVersion);

        private static LevelFingerprint From(Tile[,] grid, Position spawn, int version)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);
            byte[] canonical = Canonicalize(grid, spawn, rows, cols, version);
            return new LevelFingerprint(ComputeFnv1a64(canonical), canonical);
        }

        public static ulong HashFor(Level level) => From(level).Hash;

        public static bool CanonicalBytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        private static byte[] Canonicalize(Tile[,] grid, Position spawn, int rows, int cols, int version)
        {
            byte[] best = null;
            for (int rotation = 0; rotation < 4; rotation++)
            {
                for (int mirror = 0; mirror < 2; mirror++)
                {
                    byte[] candidate = SerializeTransformed(
                        grid, spawn, rows, cols, rotation, mirror == 1, version);
                    if (best == null || CompareLex(candidate, best) < 0)
                        best = candidate;
                }
            }

            return best;
        }

        private static byte[] SerializeTransformed(Tile[,] grid, Position spawn, int rows, int cols,
            int rotation, bool mirror, int version)
        {
            int outRows = (rotation % 2 == 0) ? rows : cols;
            int outCols = (rotation % 2 == 0) ? cols : rows;
            var bytes = new List<byte>(8 + outRows * outCols);
            bytes.Add((byte)version);
            AppendInt32(bytes, outRows);
            AppendInt32(bytes, outCols);

            Position transformedSpawn = Transform(spawn, rows, cols, rotation, mirror);
            AppendInt32(bytes, transformedSpawn.Row);
            AppendInt32(bytes, transformedSpawn.Col);

            for (int r = 0; r < outRows; r++)
            {
                for (int c = 0; c < outCols; c++)
                {
                    Position source = InverseTransform(r, c, rows, cols, rotation, mirror);
                    bytes.Add(TileCode(grid[source.Row, source.Col]));
                }
            }

            return bytes.ToArray();
        }

        private static Position Transform(Position p, int rows, int cols, int rotation, bool mirror)
        {
            int r = p.Row;
            int c = p.Col;
            if (mirror) c = cols - 1 - c;

            switch (rotation)
            {
                case 0: return new Position(r, c);
                case 1: return new Position(c, rows - 1 - r);
                case 2: return new Position(rows - 1 - r, cols - 1 - c);
                case 3: return new Position(cols - 1 - c, r);
                default: throw new ArgumentOutOfRangeException(nameof(rotation));
            }
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

        private static byte TileCode(Tile tile)
        {
            switch (tile)
            {
                case Tile.Floor: return 0;
                case Tile.Wall: return 1;
                case Tile.Void: return 2;
                default: return 255;
            }
        }

        private static void AppendInt32(List<byte> bytes, int value)
        {
            unchecked
            {
                bytes.Add((byte)value);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)(value >> 16));
                bytes.Add((byte)(value >> 24));
            }
        }

        private static int CompareLex(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            return a.Length - b.Length;
        }

        private static ulong ComputeFnv1a64(byte[] data)
        {
            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= FnvPrime;
            }

            return hash;
        }
    }
}
