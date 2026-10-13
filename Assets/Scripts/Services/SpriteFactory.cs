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

        /// <summary>An eye icon, optionally crossed to indicate hiding visible text.</summary>
        public static Sprite Eye(bool crossed, int size = 64)
        {
            string key = $"eye_{crossed}_{size}";
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            float r = size / 2f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f - r) / (r * 0.86f);
                    float ny = (y + 0.5f - r) / (r * 0.52f);
                    float ellipse = Mathf.Sqrt(nx * nx + ny * ny);
                    float outline = Mathf.Clamp01((0.11f - Mathf.Abs(ellipse - 0.82f)) * 18f);

                    float centerDistance = Mathf.Sqrt(
                        (x + 0.5f - r) * (x + 0.5f - r) +
                        (y + 0.5f - r) * (y + 0.5f - r)) / r;
                    float pupil = Mathf.Clamp01((0.19f - centerDistance) * 16f);

                    float slash = 0f;
                    if (crossed)
                    {
                        float sx = (x + 0.5f - r) / r;
                        float sy = (y + 0.5f - r) / r;
                        float distance = Mathf.Abs(sy - sx * 0.72f);
                        if (Mathf.Abs(sx) < 0.82f)
                            slash = Mathf.Clamp01((0.095f - distance) * 18f);
                    }

                    px[y * size + x] = new Color(1f, 1f, 1f,
                        Mathf.Max(outline, pupil, slash));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            s.name = crossed ? "EyeCrossed" : "EyeOpen";
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

        /// <summary>A polished gold coin with a raised rim and faceted emblem.</summary>
        public static Sprite Coins(int size = 64)
        {
            string key = "coin_emblem_" + size;
            if (Cache.TryGetValue(key, out var s)) return s;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float r = size / 2f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f - r) / r;
                    float v = (y + 0.5f - r) / r;
                    float distance = Mathf.Sqrt(u * u + v * v);
                    float alpha = Mathf.Clamp01((0.94f - distance) * size * 0.30f);
                    if (alpha <= 0f)
                    {
                        px[y * size + x] = Color.clear;
                        continue;
                    }

                    float sphereZ = Mathf.Sqrt(Mathf.Max(
                        0f,
                        1f - Mathf.Pow(distance / 0.94f, 2f)));
                    float light = Mathf.Clamp01(
                        Vector3.Dot(
                            new Vector3(u, v, sphereZ).normalized,
                            new Vector3(-0.38f, 0.58f, 0.72f).normalized));
                    Color color;
                    if (distance > 0.75f)
                    {
                        float rimBand = 0.5f +
                                        0.5f * Mathf.Sin((distance - 0.75f) * 44f);
                        color = Color.Lerp(
                            new Color(0.92f, 0.49f, 0.01f),
                            new Color(1f, 0.91f, 0.31f),
                            Mathf.Clamp01(light * 0.72f + rimBand * 0.28f));
                    }
                    else
                    {
                        color = Color.Lerp(
                            new Color(0.93f, 0.49f, 0.01f),
                            new Color(1f, 0.80f, 0.12f),
                            0.34f + light * 0.58f);
                        float innerRing = Mathf.Clamp01(
                            (0.055f - Mathf.Abs(distance - 0.68f)) * size * 0.16f);
                        color = Color.Lerp(
                            color,
                            new Color(1f, 0.92f, 0.38f),
                            innerRing * 0.72f);
                    }

                    float shadowDiamond =
                        Mathf.Abs(u - 0.035f) / 0.35f +
                        Mathf.Abs(v + 0.08f) / 0.46f;
                    float shadowCoverage = Mathf.Clamp01(
                        (1f - shadowDiamond) * size * 0.12f);
                    color = Color.Lerp(
                        color,
                        new Color(0.72f, 0.34f, 0.01f),
                        shadowCoverage * 0.72f);

                    float diamond =
                        Mathf.Abs(u) / 0.35f +
                        Mathf.Abs(v - 0.005f) / 0.46f;
                    float diamondCoverage = Mathf.Clamp01(
                        (1f - diamond) * size * 0.12f);
                    Color facet = u < 0f
                        ? new Color(1f, 0.94f, 0.52f)
                        : new Color(1f, 0.78f, 0.19f);
                    if (v > 0.005f)
                        facet = Color.Lerp(facet, Color.white, 0.26f);
                    color = Color.Lerp(color, facet, diamondCoverage * 0.94f);

                    float hx = (u + 0.31f) / 0.23f;
                    float hy = (v - 0.38f) / 0.14f;
                    float highlight = Mathf.Clamp01(
                        (1f - Mathf.Sqrt(hx * hx + hy * hy)) * size * 0.08f);
                    color = Color.Lerp(
                        color,
                        new Color(1f, 1f, 0.78f),
                        highlight * 0.75f);
                    color.a = alpha;
                    px[y * size + x] = color;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            s = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            s.name = "CoinEmblem";
            Cache[key] = s;
            return s;
        }

        /// <summary>A bold wire shopping cart with two hollow wheels.</summary>
        public static Sprite Cart(int size = 64)
        {
            string key = "cart_" + size;
            if (Cache.TryGetValue(key, out var s)) return s;

            Sprite supplied = Resources.Load<Sprite>("UiIcons/cart");
            if (supplied != null)
            {
                Cache[key] = supplied;
                return supplied;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear
            };
            float r = size / 2f;
            float stroke = 0.135f;
            var px = new Color32[size * size];
            Vector2 handleA = new(-0.86f, 0.70f);
            Vector2 handleB = new(-0.48f, 0.70f);
            Vector2 handleC = new(-0.34f, 0.37f);
            Vector2 basketA = new(-0.34f, 0.37f);
            Vector2 basketB = new(0.76f, 0.37f);
            Vector2 basketC = new(0.55f, -0.20f);
            Vector2 basketD = new(-0.17f, -0.20f);
            Vector2 axleA = new(-0.13f, -0.38f);
            Vector2 axleB = new(0.50f, -0.38f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(
                        (x + 0.5f - r) / r,
                        (y + 0.5f - r) / r);
                    float distance = Mathf.Min(
                        SegmentDistance(p, handleA, handleB),
                        SegmentDistance(p, handleB, handleC));
                    distance = Mathf.Min(distance, SegmentDistance(p, basketA, basketB));
                    distance = Mathf.Min(distance, SegmentDistance(p, basketB, basketC));
                    distance = Mathf.Min(distance, SegmentDistance(p, basketC, basketD));
                    distance = Mathf.Min(distance, SegmentDistance(p, basketD, basketA));
                    distance = Mathf.Min(distance, SegmentDistance(p, axleA, axleB));
                    float lines = Mathf.Clamp01((stroke - distance) * size * 0.22f);
                    float leftDistance =
                        Vector2.Distance(p, new Vector2(-0.04f, -0.65f));
                    float rightDistance =
                        Vector2.Distance(p, new Vector2(0.44f, -0.65f));
                    float leftWheel = Mathf.Min(
                        Mathf.Clamp01((0.19f - leftDistance) * size * 0.22f),
                        Mathf.Clamp01((leftDistance - 0.065f) * size * 0.22f));
                    float rightWheel = Mathf.Min(
                        Mathf.Clamp01((0.19f - rightDistance) * size * 0.22f),
                        Mathf.Clamp01((rightDistance - 0.065f) * size * 0.22f));
                    px[y * size + x] = new Color(
                        1f,
                        1f,
                        1f,
                        Mathf.Max(lines, Mathf.Max(leftWheel, rightWheel)));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            s.name = "Cart";
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

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denominator);
            return Vector2.Distance(p, a + ab * t);
        }

    }
}
