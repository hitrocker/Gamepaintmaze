using System.Collections.Generic;

namespace PaintMaze.Domain
{
    /// <summary>
    /// Public difficulty tracks and legacy source routing. UltraHard keeps numeric value
    /// 4 for old saves/resources, but is no longer directly playable.
    /// </summary>
    public static class DifficultyCatalog
    {
        public const int StandardBakedCount = 500;
        public const int ExtraHardBakedCount = 5000;

        private static readonly Difficulty[] PlayableValues =
        {
            Difficulty.Easy,
            Difficulty.Medium,
            Difficulty.Hard,
            Difficulty.ExtraHard
        };

        public static IReadOnlyList<Difficulty> Playable => PlayableValues;

        public static bool IsPlayable(Difficulty difficulty) =>
            difficulty >= Difficulty.Easy && difficulty <= Difficulty.ExtraHard;

        public static Difficulty NormalizePlayable(Difficulty difficulty) =>
            difficulty == Difficulty.UltraHard ? Difficulty.ExtraHard :
            IsPlayable(difficulty) ? difficulty : Difficulty.Easy;

        public static int BakedCountFor(Difficulty difficulty) => difficulty switch
        {
            Difficulty.ExtraHard => ExtraHardBakedCount,
            Difficulty.UltraHard => 0,
            _ => IsPlayable(difficulty) ? StandardBakedCount : 0
        };

        public static void ResolveBakedSource(
            Difficulty difficulty,
            int visibleIndex,
            out Difficulty sourceDifficulty,
            out int sourceIndex)
        {
            if (difficulty == Difficulty.ExtraHard &&
                visibleIndex > StandardBakedCount &&
                visibleIndex <= ExtraHardBakedCount)
            {
                sourceDifficulty = Difficulty.UltraHard;
                sourceIndex = visibleIndex - StandardBakedCount;
                return;
            }

            sourceDifficulty = difficulty;
            sourceIndex = visibleIndex;
        }

        public static void ResolveGenerationSource(
            Difficulty difficulty,
            int visibleIndex,
            out Difficulty sourceDifficulty,
            out int sourceIndex)
        {
            if (difficulty == Difficulty.ExtraHard &&
                visibleIndex > StandardBakedCount)
            {
                sourceDifficulty = Difficulty.UltraHard;
                sourceIndex = visibleIndex - StandardBakedCount;
                return;
            }

            sourceDifficulty = difficulty;
            sourceIndex = visibleIndex;
        }
    }
}
