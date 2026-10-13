using System;
using PaintMaze.Domain;

namespace PaintMaze.Core
{
    /// <summary>
    /// Keeps consecutive Extra Hard levels from starting on the same arena orientation.
    /// Template flips share a fingerprint, so a repeated roll is advanced one step.
    /// </summary>
    internal static class ExtraHardTemplateVariety
    {
        public static int Span(int rows, int cols) => rows == cols ? 8 : 4;

        public static int PublishedVariant(
            Difficulty difficulty,
            int index,
            int rows,
            int cols,
            int generatorVersion,
            int attempt)
        {
            int span = Span(rows, cols);
            var config = DifficultyConfig.For(difficulty);
            int start = Math.Max(1, index);
            while (start > 1 &&
                   config.BoardRowsFor(start - 1) == rows &&
                   config.BoardColsFor(start - 1) == cols)
                start--;

            int variant = 0;
            for (int i = start; i <= index; i++)
            {
                int seed = LevelSeed.For(difficulty, i, generatorVersion, attempt);
                int rolled = new DeterministicRng(seed).Next(span);
                if (i > start && rolled == variant)
                    rolled = (rolled + 1) % span;
                variant = rolled;
            }

            return variant;
        }
    }
}
