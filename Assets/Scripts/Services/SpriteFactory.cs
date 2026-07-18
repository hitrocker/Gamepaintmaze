using System.Collections.Generic;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Generates simple UI sprites at runtime (solid rounded rectangles and
    /// circles) so the project needs no imported art. Sprites are cached by size.
    /// </summary>
    public static class SpriteFactory
    {
        private static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Circle(int size = 96)
        {
            string key = "circle_" + size;
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size / 2f;
            float cx = r, cy = r;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - cx + 0.5f) * (x - cx + 0.5f) + (y - cy + 0.5f) * (y - cy + 0.5f));
                    float a = Mathf.Clamp01(r - d); // 1px feathered edge
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            Cache[key] = s;
            return s;
        }

        /// <summary>A soft 4-pointed sparkle (astroid star) for celebration particles.</summary>
        public static Sprite Star(int size = 64)
        {
            string key = "star_" + size;
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f - r) / r;
                    float v = (y + 0.5f - r) / r;
                    // Astroid |u|^0.5 + |v|^0.5 <= 1 gives sharp concave star points.
                    float d = Mathf.Pow(Mathf.Abs(u), 0.5f) + Mathf.Pow(Mathf.Abs(v), 0.5f);
                    float a = Mathf.Clamp01((1f - d) * 5f);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            Cache[key] = s;
            return s;
        }

        /// <summary>A smooth vertical gradient sprite (bottom colour -> top colour).</summary>
        public static Sprite VerticalGradient(Color bottom, Color top, int height = 256)
        {
            string key = $"vgrad_{ColorUtility.ToHtmlStringRGBA(bottom)}_{ColorUtility.ToHtmlStringRGBA(top)}_{height}";
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[4 * height];
            for (int y = 0; y < height; y++)
            {
                float t = height > 1 ? y / (float)(height - 1) : 0f;
                var c = Color.Lerp(bottom, top, t);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 4, height), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = s;
            return s;
        }

        public static Sprite RoundedRect(int size = 96, int corner = 18)
        {
            string key = $"rrect_{size}_{corner}";
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    px[y * size + x] = new Color(1, 1, 1, InsideRounded(x, y, size, corner));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            // 9-slice border so corners stay crisp at any scale.
            var border = new Vector4(corner, corner, corner, corner);
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, border);
            Cache[key] = s;
            return s;
        }

        private static float InsideRounded(int x, int y, int size, int corner)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            float minx = Mathf.Min(fx, size - fx);
            float miny = Mathf.Min(fy, size - fy);
            if (minx >= corner || miny >= corner) return 1f;
            float dx = corner - minx;
            float dy = corner - miny;
            if (dx <= 0 || dy <= 0) return 1f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(corner - d);
        }
    }
}
