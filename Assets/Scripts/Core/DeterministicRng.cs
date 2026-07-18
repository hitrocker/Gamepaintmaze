using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Project-owned PRNG with identical output on every supported platform.
    /// Do not use <see cref="System.Random"/> for level generation.
    /// </summary>
    public sealed class DeterministicRng
    {
        private ulong _state;

        public DeterministicRng(int seed)
        {
            _state = unchecked((ulong)(uint)seed);
            if (_state == 0) _state = 0x9E3779B97F4A7C15UL;
            NextUInt64();
        }

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");

            ulong bound = (ulong)maxExclusive;
            ulong limit = ulong.MaxValue - (ulong.MaxValue % bound);
            ulong sample;
            do
            {
                sample = NextUInt64();
            } while (sample >= limit);

            return (int)(sample % bound);
        }

        /// <summary>Uniform double in [0, 1) using 53 random bits.</summary>
        public double NextDouble()
        {
            return (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>Fisher-Yates shuffle compatible with legacy generator call sites.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private ulong NextUInt64()
        {
            unchecked
            {
                ulong z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    /// <summary>Stable integer seeds for deterministic level generation attempts.</summary>
    public static class LevelSeed
    {
        public static int For(Difficulty difficulty, int levelNumber, int generatorVersion = 1, int attempt = 0)
        {
            unchecked
            {
                uint h = (uint)generatorVersion;
                h = Mix(h, (uint)((int)difficulty + 1));
                h = Mix(h, (uint)levelNumber);
                h = Mix(h, (uint)attempt);
                return (int)(h & 0x7FFFFFFFu);
            }
        }

        private static uint Mix(uint hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                hash *= 0x01000193u;
                hash ^= hash >> 16;
                hash *= 0x7FEB352Du;
                hash ^= hash >> 15;
                hash *= 0x846CA68Bu;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
