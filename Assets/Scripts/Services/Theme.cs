using UnityEngine;
using PaintMaze.Domain;

namespace PaintMaze.Services
{
    public enum ThemeMode { Dark = 0, Light = 1 }

    /// <summary>
    /// Mode-aware palette for the 2.5D extruded board. Two themes share the same
    /// geometry/style and only swap colors: a dark theme (charcoal bg, light
    /// blue-grey tiles, dark sides) and a light theme (cream bg, dark-slate tiles,
    /// light beige sides). Paint colors rotate through a fixed set per level,
    /// independent of difficulty. All colors are our own.
    /// </summary>
    public static class Theme
    {
        public static ThemeMode Mode = ThemeMode.Dark;
        public static bool IsDark => Mode == ThemeMode.Dark;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        private static Color Pick(string dark, string light) => Hex(IsDark ? dark : light);

        // ----- Board -----
        public static Color Background => Pick("#252B32", "#EEE8D6");  // Jet Black / Eggshell
        public static Color PlayableTop => Pick("#8C98B2", "#45535E");
        // Depth colors are derived from their source token so both themes preserve the
        // same lighting relationship without introducing a separate color scheme.
        public static Color PlayableSide => Color.Lerp(PlayableTop, Color.black, 0.36f);
        public static Color BlockedTop => Hole;       // compatibility alias for old previews
        public static Color BlockedSide => HoleInner;
        public static Color BoardRim => PlayableSide;
        public static Color BoardShadow => Color.Lerp(Background, Color.black, IsDark ? 0.46f : 0.30f);
        public static Color TileTop => PlayableTop;
        public static Color TileSide => PlayableSide;
        public static Color Hole => Background;
        public static Color HoleInner => Color.Lerp(Background, Color.black, IsDark ? 0.28f : 0.22f);
        public static Color SpawnTop => Hex("#F2B83D");              // gold start (both)
        public static Color Ball => Pick("#F2F2EE", "#FCFCF8");

        // Rotating paint palette, mode-specific (3 per theme).
        // Dark: Honey Bronze / Tomato Jam / Celadon. Light: Copperwood / Night Bordeaux / Fresh Sky.
        private static readonly string[] PaintDark =
            { "#DDA448", "#BB342F", "#74D3AE" };
        private static readonly string[] PaintLight =
            { "#BC6C25", "#49111C", "#39A9DB" };

        public static Color Paint(int levelIndex)
        {
            var pal = IsDark ? PaintDark : PaintLight;
            int i = Mathf.Abs(levelIndex - 1) % pal.Length;
            return Hex(pal[i]);
        }

        // ----- UI chrome -----
        public static Color Ink => Pick("#FFFFFF", "#2E333D");        // primary text
        public static Color InkSoft => Pick("#9AA3B2", "#A29B82");    // muted text
        public static Color CardBg => Pick("#2E343F", "#FBF7EC");     // dialog cards
        public static Color TrackBg => Pick("#3A4250", "#E0D7BD");    // bars / toggle tracks
        public static Color CircleBg => Pick("#1C212A", "#2E333D");   // round buttons / pills
        public static Color OnCircle => Hex("#FFFFFF");
        public static Color Gold => Hex("#F2B83D");                   // hint button

        // Difficulty colors (only used for accents like the mascot/play button).
        public static Color Accent(Difficulty d) => d switch
        {
            Difficulty.Easy => Hex("#37B86E"),
            Difficulty.Medium => Hex("#3E8EE6"),
            Difficulty.Hard => Hex("#F2982F"),
            Difficulty.ExtraHard => Hex("#E5503A"),
            Difficulty.UltraHard => Hex("#9B4DFF"),
            _ => Hex("#37B86E")
        };

        public static Color AccentDark(Difficulty d) => Color.Lerp(Accent(d), Color.black, 0.25f);

        public static Color HomeBackground(Difficulty d) => Background;

        public static string Name(Difficulty d) => DifficultyConfig.For(d).DisplayName;

        // ----- Aliases so existing screens keep compiling against old names -----
        public static Color BgCream => Background;
        public static Color BgDark => Background;
        public static Color Cream => Background;
        public static Color CreamDark => TrackBg;
        public static Color TrackCream => TrackBg;
        public static Color Panel => CardBg;
        public static Color PanelSoft => TrackBg;
        public static Color Slate => TileTop;
        public static Color SlateTile => TileTop;
        public static Color SlateEdge => TileSide;
        public static Color Wall => TileSide;
        public static Color FloorTop => Pick("#3A4250", "#F4F1E8"); // mascot face
        public static Color FloorSide => TileSide;
        public static Color Spawn => SpawnTop;
        public static Color TextLight => OnCircle;
        public static Color TextDim => InkSoft;
        public static Color ButtonDark => CircleBg;
    }
}
