using System;
using System.Collections.Generic;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Stable player-facing order for the validated baked catalog.
    /// Bump <see cref="OrderVersion"/> only when intentionally publishing a new order.
    /// </summary>
    public static class LevelCatalogOrder
    {
        public const int OrderVersion = 1;

        private const int OrderSeedTag = 0x4F524445;
        private static readonly object Gate = new();
        private static readonly Dictionary<Difficulty, int[]> Orders = new();

        /// <summary>
        /// Maps a one-based visible slot to a one-based baked catalog slot.
        /// </summary>
        public static int Map(Difficulty difficulty, int visibleIndex)
        {
            int count = DifficultyCatalog.BakedCountFor(difficulty);
            if (!DifficultyCatalog.IsPlayable(difficulty) || count <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(difficulty), difficulty, "Difficulty has no playable baked catalog.");
            if (visibleIndex < 1 || visibleIndex > count)
                throw new ArgumentOutOfRangeException(
                    nameof(visibleIndex), visibleIndex, $"Expected a value in [1, {count}].");

            int[] order;
            lock (Gate)
            {
                if (!Orders.TryGetValue(difficulty, out order))
                {
                    order = Build(difficulty, count);
                    Orders[difficulty] = order;
                }
            }
            return order[visibleIndex - 1];
        }

        private static int[] Build(Difficulty difficulty, int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i + 1;

            int seed = LevelSeed.For(
                difficulty, count, OrderVersion, OrderSeedTag);
            new DeterministicRng(seed).Shuffle(order);
            return order;
        }
    }
}
