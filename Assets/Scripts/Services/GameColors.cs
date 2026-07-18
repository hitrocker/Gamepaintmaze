using System.Collections.Generic;
using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Services
{
    /// <summary>
    /// UI color helper requested by the UI spec. To keep a single source of truth,
    /// the per-difficulty accents are sourced from <see cref="Theme"/> (the approved
    /// palette) rather than hard-coded, so changing the theme updates these too.
    /// </summary>
    public static class GameColors
    {
        public static Color HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            if (hex[0] != '#') hex = "#" + hex;
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        private static Dictionary<string, Color> _accents;

        /// <summary>Difficulty display name (UPPERCASE) → accent color.</summary>
        public static IReadOnlyDictionary<string, Color> AccentColors
        {
            get
            {
                if (_accents != null) return _accents;
                _accents = new Dictionary<string, Color>();
                foreach (Difficulty d in DifficultyCatalog.Playable)
                    _accents[DifficultyConfig.For(d).DisplayName.ToUpper()] = Theme.Accent(d);
                return _accents;
            }
        }

        public static Color Accent(string difficultyName)
        {
            if (difficultyName != null && AccentColors.TryGetValue(difficultyName.ToUpper(), out var c))
                return c;
            return Theme.Gold;
        }
    }
}
