using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Services
{
    /// <summary>
    /// Central, UI-independent formatting and flow helpers for the five endless
    /// difficulty tracks. Keeping the strings and the "advance one level" rule in
    /// one pure place guarantees the home dock and in-game HUD never render a
    /// finite total, a "last level" state, or wrapped/campaign copy: level numbers
    /// are always shown verbatim within [1, PracticalMaxLevel] and there is always
    /// a next level.
    /// </summary>
    public static class LevelLabel
    {
        public const string Endless = "ENDLESS";

        /// <summary>Keeps a level number inside the playable [1, PracticalMaxLevel] range.</summary>
        public static int Clamp(int level) =>
            Mathf.Clamp(level, 1, DifficultyConfig.PracticalMaxLevel);

        /// <summary>Home dock position line, e.g. "EASY  ·  LEVEL 5" (never a total).</summary>
        public static string DockPosition(Difficulty difficulty, int level) =>
            $"{DifficultyConfig.For(difficulty).DisplayName.ToUpperInvariant()}  ·  LEVEL {Clamp(level)}";

        /// <summary>In-game HUD title, e.g. "Level 5" (unbounded, never "of N").</summary>
        public static string HudTitle(int level) => "Level " + Clamp(level);

        /// <summary>
        /// The next level in an endless track: exactly one more, never wrapping and
        /// never exceeding the practical ceiling.
        /// </summary>
        public static int NextLevel(int level) =>
            level < DifficultyConfig.PracticalMaxLevel
                ? Clamp(level) + 1
                : DifficultyConfig.PracticalMaxLevel;
    }
}
