using System.Collections.Generic;
using UnityEngine;

namespace PaintMaze.Services
{
    /// <summary>
    /// Provides cached ball materials and high-quality UI sprites. Shop icons
    /// are baked from the real 3D materials, with deterministic CPU art kept as
    /// a safety fallback when a generated asset is missing.
    /// </summary>
    public static class SportsBallArt
    {
        private const int IconSize = 160;

        private static readonly Dictionary<string, Sprite> Icons = new();
        private static readonly Dictionary<string, Material> Materials = new();
        private static readonly Vector2[] SoccerIconCenters =
        {
            new(0f, 0.04f), new(-0.58f, 0.42f), new(0.60f, 0.44f),
            new(-0.38f, -0.58f), new(0.45f, -0.55f)
        };
        private static readonly float[] SoccerIconSizes =
            { 0.26f, 0.18f, 0.18f, 0.17f, 0.17f };
        private static readonly Vector3[] MercuryCraters = BuildCraters(11, 22);
        private static readonly Vector3[] MoonCraters = BuildCraters(29, 20);
        private static readonly Vector3[] PlutoCraters = BuildCraters(47, 14);

        public static Material MaterialFor(string skinId)
        {
            BallSkinDefinition skin = CosmeticCatalog.GetOrClassic(skinId);
            string key = skin.Id == SaveService.DefaultSkinId
                ? skin.Id + "_" + Theme.Mode
                : skin.Id;
            if (Materials.TryGetValue(key, out Material cached))
                return cached;

            Material material = skin.Id == SaveService.DefaultSkinId
                ? ClassicMaterial(key)
                : SportsMaterial(skin.Id, key);
            Materials[key] = material;
            return material;
        }

        private static Material ClassicMaterial(string key)
        {
            var material = new Material(Shader.Find("Standard"))
            {
                name = "BallSkin_" + key,
                color = Theme.Ball,
                mainTexture = Texture2D.whiteTexture
            };
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.58f);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            return material;
        }

        private static Material SportsMaterial(string skinId, string key)
        {
            // Procedural surface painted from the sphere's 3D direction so
            // seams never pinch at the poles. Types 16-25 live in a second
            // shader because Unity's Cg parser overflows in Custom/BallSkin.
            Shader shader = Shader.Find(
                UsesExtraSportsShader(skinId)
                    ? "Custom/BallSkinSports"
                    : "Custom/BallSkin");
            if (shader == null) // safety: keep a ball rather than magenta
                return ClassicMaterial(key);

            var material = new Material(shader) { name = "BallSkin_" + key };
            material.SetFloat("_LitStrength", 1f);
            switch (skinId)
            {
                case "cricket":
                    material.SetInt("_SkinType", 1);
                    material.SetColor("_BaseColor", Theme.Hex("#A91F2B"));
                    material.SetColor("_LineColor", Theme.Hex("#6F111C"));
                    material.SetColor("_AccentColor", Theme.Hex("#F5E8CF"));
                    material.SetFloat("_Glossiness", 0.5f);
                    material.SetFloat("_BumpStrength", 1.1f);
                    material.SetFloat("_AoStrength", 0.35f);
                    break;
                case "soccer":
                    material.SetInt("_SkinType", 2);
                    material.SetColor("_BaseColor", Theme.Hex("#F7F7F2"));
                    material.SetColor("_LineColor", Theme.Hex("#5E5F63"));
                    material.SetColor("_AccentColor", Theme.Hex("#141414"));
                    material.SetFloat("_Glossiness", 0.20f);
                    material.SetFloat("_BumpStrength", 0.9f);
                    material.SetFloat("_AoStrength", 0.30f);
                    break;
                case "basketball":
                    material.SetInt("_SkinType", 3);
                    material.SetColor("_BaseColor", Theme.Hex("#D96B20"));
                    material.SetColor("_LineColor", Theme.Hex("#241813"));
                    material.SetColor("_AccentColor", Theme.Hex("#241813"));
                    material.SetFloat("_Glossiness", 0.28f);
                    material.SetFloat("_BumpStrength", 1.4f);
                    material.SetFloat("_AoStrength", 0.40f);
                    break;
                case "baseball":
                    material.SetInt("_SkinType", 4);
                    material.SetColor("_BaseColor", Theme.Hex("#F4F0E5"));
                    material.SetColor("_LineColor", Theme.Hex("#C32632"));
                    material.SetColor("_AccentColor", Theme.Hex("#C32632"));
                    material.SetFloat("_Glossiness", 0.16f);
                    material.SetFloat("_BumpStrength", 1.1f);
                    material.SetFloat("_AoStrength", 0.35f);
                    break;
                case "tennis":
                    material.SetInt("_SkinType", 16);
                    material.SetColor("_BaseColor", Theme.Hex("#C8E04A"));
                    material.SetColor("_LineColor", Theme.Hex("#F4F4EE"));
                    material.SetColor("_AccentColor", Theme.Hex("#EEF0E4"));
                    material.SetFloat("_Glossiness", 0.22f);
                    material.SetFloat("_BumpStrength", 1.0f);
                    material.SetFloat("_AoStrength", 0.28f);
                    break;
                case "volleyball":
                    material.SetInt("_SkinType", 17);
                    material.SetColor("_BaseColor", Theme.Hex("#F4F5F2"));
                    material.SetColor("_LineColor", Theme.Hex("#2F6FBF"));
                    material.SetColor("_AccentColor", Theme.Hex("#E2B43A"));
                    material.SetFloat("_Glossiness", 0.32f);
                    material.SetFloat("_BumpStrength", 1.0f);
                    material.SetFloat("_AoStrength", 0.30f);
                    break;
                case "golf":
                    material.SetInt("_SkinType", 18);
                    material.SetColor("_BaseColor", Theme.Hex("#F3F4F0"));
                    material.SetColor("_LineColor", Theme.Hex("#C5C7C0"));
                    material.SetColor("_AccentColor", Theme.Hex("#D8DAD4"));
                    material.SetFloat("_Glossiness", 0.18f);
                    material.SetFloat("_BumpStrength", 1.3f);
                    material.SetFloat("_AoStrength", 0.32f);
                    break;
                case "bowling":
                    material.SetInt("_SkinType", 19);
                    material.SetColor("_BaseColor", Theme.Hex("#2A2C32"));
                    material.SetColor("_LineColor", Theme.Hex("#7A1F28"));
                    material.SetColor("_AccentColor", Theme.Hex("#0C0D10"));
                    material.SetFloat("_Glossiness", 0.55f);
                    material.SetFloat("_BumpStrength", 1.2f);
                    material.SetFloat("_AoStrength", 0.40f);
                    break;
                case "eightball":
                    material.SetInt("_SkinType", 20);
                    material.SetColor("_BaseColor", Theme.Hex("#141414"));
                    material.SetColor("_LineColor", Theme.Hex("#111111"));
                    material.SetColor("_AccentColor", Theme.Hex("#F4F4F0"));
                    material.SetFloat("_Glossiness", 0.62f);
                    material.SetFloat("_BumpStrength", 0.2f);
                    material.SetFloat("_AoStrength", 0.10f);
                    break;
                case "beachball":
                    material.SetInt("_SkinType", 21);
                    material.SetColor("_BaseColor", Theme.Hex("#E23B2E"));
                    material.SetColor("_LineColor", Theme.Hex("#2A6FDB"));
                    material.SetColor("_AccentColor", Theme.Hex("#F5D14A"));
                    material.SetFloat("_Glossiness", 0.45f);
                    material.SetFloat("_BumpStrength", 0.3f);
                    material.SetFloat("_AoStrength", 0.12f);
                    break;
                case "wiffle":
                    material.SetInt("_SkinType", 22);
                    material.SetColor("_BaseColor", Theme.Hex("#F6F6F2"));
                    material.SetColor("_LineColor", Theme.Hex("#1A1A1A"));
                    material.SetColor("_AccentColor", Theme.Hex("#D0D0CC"));
                    material.SetFloat("_Glossiness", 0.12f);
                    material.SetFloat("_BumpStrength", 1.4f);
                    material.SetFloat("_AoStrength", 0.45f);
                    break;
                case "spikeball":
                    material.SetInt("_SkinType", 23);
                    material.SetColor("_BaseColor", Theme.Hex("#E6C31A"));
                    material.SetColor("_LineColor", Theme.Hex("#161616"));
                    material.SetColor("_AccentColor", Theme.Hex("#F0D64A"));
                    material.SetFloat("_Glossiness", 0.30f);
                    material.SetFloat("_BumpStrength", 0.4f);
                    material.SetFloat("_AoStrength", 0.18f);
                    break;
                case "dodgeball":
                    material.SetInt("_SkinType", 24);
                    material.SetColor("_BaseColor", Theme.Hex("#E24B3C"));
                    material.SetColor("_LineColor", Theme.Hex("#1A1A1A"));
                    material.SetColor("_AccentColor", Theme.Hex("#C43B30"));
                    material.SetFloat("_Glossiness", 0.38f);
                    material.SetFloat("_BumpStrength", 0.8f);
                    material.SetFloat("_AoStrength", 0.22f);
                    break;
                case "takraw":
                    material.SetInt("_SkinType", 25);
                    material.SetColor("_BaseColor", Theme.Hex("#C4A056"));
                    material.SetColor("_LineColor", Theme.Hex("#5A3D1C"));
                    material.SetColor("_AccentColor", Theme.Hex("#E2C888"));
                    material.SetFloat("_Glossiness", 0.14f);
                    material.SetFloat("_BumpStrength", 1.5f);
                    material.SetFloat("_AoStrength", 0.38f);
                    break;
                case "jupiter":
                    material.SetInt("_SkinType", 5);
                    material.SetColor("_BaseColor", Theme.Hex("#D2BE96"));
                    material.SetColor("_LineColor", Theme.Hex("#805033"));
                    material.SetColor("_AccentColor", Theme.Hex("#A94735"));
                    material.SetFloat("_Glossiness", 0.18f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "mars":
                    material.SetInt("_SkinType", 6);
                    material.SetColor("_BaseColor", Theme.Hex("#A8492E"));
                    material.SetColor("_LineColor", Theme.Hex("#63301F"));
                    material.SetColor("_AccentColor", Theme.Hex("#C8C3B8"));
                    material.SetFloat("_Glossiness", 0.12f);
                    material.SetFloat("_BumpStrength", 0.7f);
                    material.SetFloat("_AoStrength", 0.18f);
                    break;
                case "venus":
                    material.SetInt("_SkinType", 7);
                    material.SetColor("_BaseColor", Theme.Hex("#C5A86D"));
                    material.SetColor("_LineColor", Theme.Hex("#76502A"));
                    material.SetColor("_AccentColor", Theme.Hex("#D9C996"));
                    material.SetFloat("_Glossiness", 0.18f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "uranus":
                    material.SetInt("_SkinType", 8);
                    material.SetColor("_BaseColor", Theme.Hex("#69B5C5"));
                    material.SetColor("_LineColor", Theme.Hex("#347487"));
                    material.SetColor("_AccentColor", Theme.Hex("#A4D4DC"));
                    material.SetFloat("_Glossiness", 0.04f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "neptune":
                    material.SetInt("_SkinType", 9);
                    material.SetColor("_BaseColor", Theme.Hex("#2C56B8"));
                    material.SetColor("_LineColor", Theme.Hex("#12255F"));
                    material.SetColor("_AccentColor", Theme.Hex("#101F3E"));
                    material.SetFloat("_Glossiness", 0.24f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "mercury":
                    material.SetInt("_SkinType", 10);
                    material.SetColor("_BaseColor", Theme.Hex("#595650"));
                    material.SetColor("_LineColor", Theme.Hex("#292725"));
                    material.SetColor("_AccentColor", Theme.Hex("#7F7A72"));
                    material.SetFloat("_Glossiness", 0.08f);
                    material.SetFloat("_BumpStrength", 1.6f);
                    material.SetFloat("_AoStrength", 0.40f);
                    break;
                case "moon":
                    material.SetInt("_SkinType", 11);
                    material.SetColor("_BaseColor", Theme.Hex("#9F9B94"));
                    material.SetColor("_LineColor", Theme.Hex("#44423F"));
                    material.SetColor("_AccentColor", Theme.Hex("#BDB9B0"));
                    material.SetFloat("_Glossiness", 0.06f);
                    material.SetFloat("_BumpStrength", 1.5f);
                    material.SetFloat("_AoStrength", 0.38f);
                    break;
                case "pluto":
                    material.SetInt("_SkinType", 12);
                    material.SetColor("_BaseColor", Theme.Hex("#8B7058"));
                    material.SetColor("_LineColor", Theme.Hex("#493828"));
                    material.SetColor("_AccentColor", Theme.Hex("#D1C0A8"));
                    material.SetFloat("_Glossiness", 0.08f);
                    material.SetFloat("_BumpStrength", 1.4f);
                    material.SetFloat("_AoStrength", 0.36f);
                    break;
                case "earth":
                    material.SetInt("_SkinType", 13);
                    material.SetColor("_BaseColor", Theme.Hex("#1D60A6"));
                    material.SetColor("_LineColor", Theme.Hex("#0F3565"));
                    material.SetColor("_AccentColor", Theme.Hex("#C9D8DE"));
                    material.SetFloat("_Glossiness", 0.20f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "sun":
                    material.SetInt("_SkinType", 14);
                    material.SetColor("_BaseColor", Theme.Hex("#D86F1E"));
                    material.SetColor("_LineColor", Theme.Hex("#7C2C16"));
                    material.SetColor("_AccentColor", Theme.Hex("#F2B33A"));
                    material.SetColor("_EmissionColor", new Color(0.58f, 0.38f, 0.14f));
                    material.SetFloat("_LitStrength", 0.32f);
                    material.SetFloat("_Glossiness", 0f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                case "saturn":
                    material.SetInt("_SkinType", 15);
                    material.SetColor("_BaseColor", Theme.Hex("#C7B27B"));
                    material.SetColor("_LineColor", Theme.Hex("#80623A"));
                    material.SetColor("_AccentColor", Theme.Hex("#D8CAA5"));
                    material.SetFloat("_Glossiness", 0.04f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
                default:
                    material.SetInt("_SkinType", 0);
                    material.SetColor("_BaseColor", Theme.Ball);
                    material.SetFloat("_Glossiness", 0.58f);
                    material.SetFloat("_BumpStrength", 0f);
                    material.SetFloat("_AoStrength", 0f);
                    break;
            }
            return material;
        }

        private static bool UsesExtraSportsShader(string skinId) =>
            skinId is "tennis" or "volleyball" or "golf" or "bowling"
                or "eightball" or "beachball" or "wiffle" or "spikeball"
                or "dodgeball" or "takraw";

        public static bool HasRings(string skinId) =>
            CosmeticCatalog.GetOrClassic(skinId).Id == "saturn";

        /// <summary>
        /// Unlit banded material for Saturn's ring mesh. The texture runs
        /// inner-to-outer across U, with alpha carving the Cassini division.
        /// </summary>
        public static Material RingMaterial()
        {
            const string key = "saturn_rings";
            if (Materials.TryGetValue(key, out Material cached))
                return cached;

            Shader shader = Shader.Find("Unlit/Transparent");
            var material = new Material(shader != null ? shader : Shader.Find("Standard"))
            {
                name = "BallSkin_" + key,
                mainTexture = BuildRingTexture()
            };
            Materials[key] = material;
            return material;
        }

        /// <summary>
        /// Flat annulus in the XZ plane, emitted twice so it reads from either
        /// side without relying on the ring shader disabling culling. U runs
        /// from the inner to the outer edge.
        /// </summary>
        public static Mesh BuildRingMesh(float inner, float outer, int segments)
        {
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = dir * inner;
                vertices[i * 2 + 1] = dir * outer;
                uv[i * 2] = new Vector2(0f, 0.5f);
                uv[i * 2 + 1] = new Vector2(1f, 0.5f);
            }

            var triangles = new int[segments * 12];
            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
            }

            var mesh = new Mesh { name = "BallRings" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D BuildRingTexture()
        {
            const int width = 128;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "BallRingTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width];
            for (int i = 0; i < width; i++)
            {
                float t = (i + 0.5f) / width;
                Color band = Color.Lerp(
                    Theme.Hex("#C7B57F"),
                    Theme.Hex("#6F5E3D"),
                    Mathf.Abs(t - 0.5f) * 1.5f);

                float alpha = 0.82f *
                              Smoothstep(0f, 0.07f, t) *
                              (1f - Smoothstep(0.90f, 1f, t));
                alpha *= 1f - 0.94f * (1f - Smoothstep(0.020f, 0.045f, Mathf.Abs(t - 0.53f)));
                alpha *= Mathf.Lerp(0.72f, 1f, 0.5f + 0.5f * Mathf.Sin(t * 46f));
                pixels[i] = new Color(band.r, band.g, band.b, Mathf.Clamp01(alpha));
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        public static Sprite IconFor(string skinId)
        {
            BallSkinDefinition skin = CosmeticCatalog.GetOrClassic(skinId);
            string key = skin.Id == SaveService.DefaultSkinId
                ? skin.Id + "_" + Theme.Mode
                : skin.Id;
            if (Icons.TryGetValue(key, out Sprite cached))
                return cached;

            Sprite baked = Resources.Load<Sprite>("BallSkinIcons/" + key);
            if (baked != null)
            {
                Icons[key] = baked;
                return baked;
            }

            Texture2D texture = BuildIcon(skin.Id);
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
            sprite.name = "BallIcon_" + key;
            Icons[key] = sprite;
            return sprite;
        }

        private static Texture2D BuildIcon(string skinId)
        {
            var texture = new Texture2D(
                IconSize,
                IconSize,
                TextureFormat.RGBA32,
                false)
            {
                name = "BallIconTexture_" + skinId,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[IconSize * IconSize];
            // Saturn is drawn smaller so its rings fit inside the icon.
            bool ringed = skinId == "saturn";
            bool solar =
                CosmeticCatalog.GetOrClassic(skinId).Category ==
                SkinCategory.SolarSystem;
            float radius = IconSize * (ringed ? 0.29f : 0.46f);
            Vector2 center = Vector2.one * (IconSize * 0.5f);
            for (int y = 0; y < IconSize; y++)
            {
                for (int x = 0; x < IconSize; x++)
                {
                    Vector2 p = (new Vector2(x + 0.5f, y + 0.5f) - center) / radius;
                    float r = p.magnitude;
                    Color ringColor = Color.clear;
                    bool onRing = ringed && SaturnRingColor(p, out ringColor);
                    if (r > 1f)
                    {
                        pixels[y * IconSize + x] = onRing ? ringColor : Color.clear;
                        continue;
                    }

                    // Only the near half of the ring passes in front of the disc.
                    if (onRing && p.y < 0f)
                    {
                        pixels[y * IconSize + x] = ringColor;
                        continue;
                    }

                    Color color = IconColor(skinId, p);
                    float sphereZ = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
                    Vector3 normal = new Vector3(p.x, p.y, sphereZ);
                    float light = Mathf.Clamp01(
                        Vector3.Dot(normal, new Vector3(-0.42f, 0.58f, 0.70f).normalized));
                    float shade = Mathf.Lerp(
                        0.58f,
                        solar ? 1.04f : 1.12f,
                        light);
                    color = new Color(
                        Mathf.Clamp01(color.r * shade),
                        Mathf.Clamp01(color.g * shade),
                        Mathf.Clamp01(color.b * shade),
                        Mathf.Clamp01((1f - r) * 28f));

                    float glint = Mathf.Clamp01(
                        (0.12f - Vector2.Distance(p, new Vector2(-0.32f, 0.40f))) * 7f);
                    color = Color.Lerp(
                        color,
                        Color.white,
                        glint * (solar ? 0.28f : 0.62f));
                    pixels[y * IconSize + x] = color;
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Color IconColor(string skinId, Vector2 p)
        {
            return skinId switch
            {
                "cricket" => CricketIcon(p),
                "soccer" => SoccerIcon(p),
                "basketball" => BasketballIcon(p),
                "baseball" => BaseballIcon(p),
                "tennis" => TennisIcon(p),
                "volleyball" => VolleyballIcon(p),
                "golf" => GolfIcon(p),
                "bowling" => BowlingIcon(p),
                "eightball" => EightBallIcon(p),
                "beachball" => BeachBallIcon(p),
                "wiffle" => WiffleIcon(p),
                "spikeball" => SpikeballIcon(p),
                "dodgeball" => DodgeballIcon(p),
                "takraw" => TakrawIcon(p),
                "jupiter" => JupiterIcon(p),
                "mars" => MarsIcon(p),
                "venus" => VenusIcon(p),
                "uranus" => UranusIcon(p),
                "neptune" => NeptuneIcon(p),
                "mercury" => MercuryIcon(p),
                "moon" => MoonIcon(p),
                "pluto" => PlutoIcon(p),
                "earth" => EarthIcon(p),
                "sun" => SunIcon(p),
                "saturn" => SaturnIcon(p),
                _ => Theme.Ball
            };
        }

        private static Color EarthIcon(Vector2 p)
        {
            // Icon faces the Atlantic so both American and African outlines show.
            float land = 0f;
            land = Mathf.Max(land, IconBlob(p, new Vector2(-0.40f, 0.38f), 0.46f, 0.42f));
            land = Mathf.Max(land, IconBlob(p, new Vector2(-0.24f, -0.40f), 0.30f, 0.42f));
            land = Mathf.Max(land, IconBlob(p, new Vector2(0.40f, -0.02f), 0.40f, 0.46f));
            land = Mathf.Max(land, IconBlob(p, new Vector2(0.34f, 0.52f), 0.30f, 0.24f));
            land += 0.10f * Mathf.Sin(p.x * 13f) * Mathf.Sin(p.y * 11f);

            Color color = land > 0.30f
                ? Color.Lerp(
                    Theme.Hex("#3F8550"),
                    Theme.Hex("#A69255"),
                    Smoothstep(0.58f, 0.95f, land))
                : Color.Lerp(
                    Theme.Hex("#0F3565"),
                    Theme.Hex("#1D60A6"),
                    Smoothstep(0f, 0.30f, land));
            if (Mathf.Abs(p.y) > 0.86f + 0.03f * Mathf.Sin(p.x * 8f))
                color = Theme.Hex("#C9D8DE");
            return color;
        }

        private static float IconBlob(Vector2 p, Vector2 center, float rx, float ry)
        {
            Vector2 q = p - center;
            return Mathf.Clamp01(
                1f - Mathf.Sqrt((q.x * q.x) / (rx * rx) + (q.y * q.y) / (ry * ry)));
        }

        private static Color SunIcon(Vector2 p)
        {
            Color color = Color.Lerp(
                Theme.Hex("#D86F1E"),
                Theme.Hex("#F2B33A"),
                Smoothstep(0.36f, 0.78f, Fbm2D(p * 7f)));
            float spots = Smoothstep(0.70f, 0.86f, Fbm2D(p * 2.4f + new Vector2(11f, 5f)));
            return Color.Lerp(color, Theme.Hex("#7C2C16"), spots * 0.84f);
        }

        // Mirrors the shader's value noise so icons and materials share a look.
        private static float Hash21(Vector2 p)
        {
            float h = Mathf.Sin(p.x * 127.1f + p.y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static float ValueNoise2D(Vector2 p)
        {
            var i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            Vector2 f = p - i;
            f = new Vector2(
                f.x * f.x * (3f - 2f * f.x),
                f.y * f.y * (3f - 2f * f.y));
            float a = Hash21(i);
            float b = Hash21(i + Vector2.right);
            float c = Hash21(i + Vector2.up);
            float d = Hash21(i + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(c, d, f.x), f.y);
        }

        private static float Fbm2D(Vector2 p) =>
            (0.5f * ValueNoise2D(p) +
             0.25f * ValueNoise2D(p * 2.03f) +
             0.125f * ValueNoise2D(p * 4.01f)) / 0.875f;

        private static Color SaturnIcon(Vector2 p)
        {
            float band = 0.5f + 0.5f * Mathf.Sin(p.y * 9.6f + 0.4f * Mathf.Sin(p.x * 2.6f));
            band = Smoothstep(0.26f, 0.74f, band);
            return Color.Lerp(
                Theme.Hex("#C7B27B"),
                Theme.Hex("#80623A"),
                band * 0.78f);
        }

        // Rings projected as a tilted ellipse measured in planet radii.
        private static bool SaturnRingColor(Vector2 p, out Color color)
        {
            const float tilt = 0.32f;
            const float inner = 1.24f;
            const float outer = 1.98f;
            float v = p.y / tilt;
            float ringRadius = Mathf.Sqrt(p.x * p.x + v * v);
            color = Color.clear;
            if (ringRadius < inner || ringRadius > outer) return false;

            float t = Mathf.InverseLerp(inner, outer, ringRadius);
            if (t > 0.48f && t < 0.58f) return false; // Cassini division

            Color band = Color.Lerp(
                Theme.Hex("#C7B57F"),
                Theme.Hex("#6F5E3D"),
                Mathf.Abs(t - 0.5f) * 1.5f);
            float edgeFade = Mathf.Min(
                Smoothstep(inner, inner + 0.08f, ringRadius),
                1f - Smoothstep(outer - 0.12f, outer, ringRadius));
            color = new Color(band.r, band.g, band.b, Mathf.Clamp01(edgeFade) * 0.86f);
            return true;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // Fixed crater layouts so icons stay deterministic between runs.
        private static Vector3[] BuildCraters(int seed, int count)
        {
            var random = new System.Random(seed);
            var craters = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float radial = Mathf.Sqrt((float)random.NextDouble()) * 0.92f;
                float size = Mathf.Lerp(0.06f, 0.20f, (float)random.NextDouble());
                craters[i] = new Vector3(
                    Mathf.Cos(angle) * radial,
                    Mathf.Sin(angle) * radial,
                    size);
            }
            return craters;
        }

        private static float CraterShade(Vector2 p, Vector3[] craters)
        {
            float height = 0f;
            for (int i = 0; i < craters.Length; i++)
            {
                var center = new Vector2(craters[i].x, craters[i].y);
                float t = Vector2.Distance(p, center) / craters[i].z;
                if (t > 1.4f) continue;
                height += -(1f - Smoothstep(0f, 0.9f, t)) +
                          0.55f * (1f - Smoothstep(0f, 0.32f, Mathf.Abs(t - 0.95f)));
            }
            return Mathf.Clamp(height, -1.2f, 0.8f);
        }

        private static Color MercuryIcon(Vector2 p)
        {
            // Icons carry their own baked shading, so cratered surfaces use a
            // slightly lifted palette while preserving the material contrast.
            float c = CraterShade(p, MercuryCraters);
            Color color = Color.Lerp(
                Theme.Hex("#74706A"),
                Theme.Hex("#373431"),
                Mathf.Clamp01(-c * 0.75f));
            return Color.Lerp(color, Theme.Hex("#9A958C"), Mathf.Clamp01(c) * 0.55f);
        }

        private static Color MoonIcon(Vector2 p)
        {
            float maria = Smoothstep(
                0.60f,
                0.80f,
                0.5f + 0.42f * Mathf.Sin(p.x * 2.4f + 1.1f) * Mathf.Sin(p.y * 2.1f - 0.6f));
            Color color = Color.Lerp(
                Theme.Hex("#B9B5AD"),
                Theme.Hex("#686560"),
                maria);
            float c = CraterShade(p, MoonCraters) * (1f - 0.78f * maria);
            color *= Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(1f + c));
            return Color.Lerp(color, Theme.Hex("#D0CBC1"), Mathf.Clamp01(c) * 0.45f);
        }

        private static Color PlutoIcon(Vector2 p)
        {
            float c = CraterShade(p, PlutoCraters);
            Color color = Color.Lerp(
                Theme.Hex("#A98C70"),
                Theme.Hex("#634D39"),
                Mathf.Clamp01(-c * 0.65f));
            Vector2 q = (p - new Vector2(0.02f, -0.10f)) / 0.62f;
            float a = q.x * q.x + q.y * q.y - 1f;
            float heart = a * a * a - q.x * q.x * q.y * q.y * q.y;
            if (heart < 0f)
                color = Theme.Hex("#D1C0A8");
            return color;
        }

        // Icons are flat discs, so latitude bands are just the y coordinate.
        // Each planet mirrors the shader's signature feature so the shop
        // preview matches the ball in play.
        private static Color JupiterIcon(Vector2 p)
        {
            float warp = 0.05f * Mathf.Sin(p.x * 5.4f + 1.1f) +
                         0.03f * Mathf.Sin(p.x * 11f);
            float belt = 0.5f + 0.5f * Mathf.Sin((p.y + warp) * 12.5f);
            belt = belt * 0.70f + 0.30f * (0.5f + 0.5f * Mathf.Sin(p.y * 5.2f + 1.4f));
            Color color = Color.Lerp(
                Theme.Hex("#D2BE96"),
                Theme.Hex("#805033"),
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.24f, 0.72f, belt)));
            Vector2 q = p - new Vector2(0.34f, -0.30f);
            if (Ellipse(q, 0.26f, 0.15f) < 1f)
                color = Theme.Hex("#A94735");
            return color;
        }

        private static Color MarsIcon(Vector2 p)
        {
            float maria = 0.5f +
                          0.32f * Mathf.Sin(p.x * 4.1f + 0.7f) *
                          Mathf.Sin(p.y * 3.4f - 1.2f) +
                          0.18f * Mathf.Sin(p.x * 7.9f - p.y * 6.2f);
            Color color = Color.Lerp(
                Theme.Hex("#BD5737"),
                Theme.Hex("#743821"),
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.46f, 0.74f, maria)));
            if (Mathf.Abs(p.y) > 0.90f + 0.03f * Mathf.Sin(p.x * 9f))
                color = Theme.Hex("#D5D1C8");
            return color;
        }

        private static Color VenusIcon(Vector2 p)
        {
            float swirl = 1.5f * Mathf.Sin(p.x * 2.2f + p.y * 1.4f);
            float band = 0.5f + 0.5f * Mathf.Sin(p.y * 6.4f + swirl);
            band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.78f, band));
            Color color = Color.Lerp(
                Theme.Hex("#C5A86D"),
                Theme.Hex("#76502A"),
                band * 0.92f);
            float bright = Mathf.Clamp01(
                (Mathf.Sin(p.y * 11f + 1.6f * Mathf.Sin(p.x * 2.4f)) - 0.62f) * 2.6f);
            return Color.Lerp(color, Theme.Hex("#D9C996"), bright * 0.68f);
        }

        private static Color UranusIcon(Vector2 p)
        {
            // Tipped axis: bands run vertically.
            float band = 0.5f + 0.5f * Mathf.Sin(p.x * 6.4f);
            band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.82f, band));
            Color color = Color.Lerp(
                Theme.Hex("#69B5C5"),
                Theme.Hex("#347487"),
                band * 0.96f);
            return Color.Lerp(
                color,
                Theme.Hex("#A4D4DC"),
                Mathf.Clamp01(0.6f - Mathf.Abs(p.x) * 1.6f) * 0.28f);
        }

        private static Color NeptuneIcon(Vector2 p)
        {
            float band = 0.5f + 0.5f * Mathf.Sin(p.y * 8.4f + 0.7f * Mathf.Sin(p.x * 3f));
            band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.80f, band));
            Color color = Color.Lerp(
                Theme.Hex("#3765C9"),
                Theme.Hex("#183075"),
                band * 0.95f);
            Vector2 q = p - new Vector2(-0.30f, -0.26f);
            if (Ellipse(q, 0.22f, 0.14f) < 1f)
                color = Theme.Hex("#13264A");
            return color;
        }

        private static float Ellipse(Vector2 q, float rx, float ry) =>
            (q.x * q.x) / (rx * rx) + (q.y * q.y) / (ry * ry);

        private static Color CricketIcon(Vector2 p)
        {
            Color leather = Theme.Hex("#A91F2B");
            float curve = p.x + 0.16f * Mathf.Sin(p.y * 3.2f);
            float distance = Mathf.Abs(curve);
            if (distance < 0.035f) return Theme.Hex("#F5E8CF");
            if (distance < 0.07f) return Theme.Hex("#69111B");
            float stitch = Mathf.Repeat((p.y + 1f) * 7.5f, 1f);
            if (distance < 0.13f && Mathf.Abs(stitch - 0.5f) < 0.16f)
                return Theme.Hex("#F5E8CF");
            return leather;
        }

        private static Color SoccerIcon(Vector2 p)
        {
            Color white = Theme.Hex("#F5F3EA");
            for (int i = 0; i < SoccerIconCenters.Length; i++)
            {
                Vector2 local = p - SoccerIconCenters[i];
                if (InsidePentagon(local, SoccerIconSizes[i]))
                    return Theme.Hex("#141414");
            }

            // Panel joins radiating from the central pentagon.
            for (int i = 1; i < SoccerIconCenters.Length; i++)
            {
                if (DistanceToSegment(
                        p,
                        SoccerIconCenters[0],
                        SoccerIconCenters[i]) < 0.022f)
                    return Theme.Hex("#77787A");
            }
            return white;
        }

        private static Color BasketballIcon(Vector2 p)
        {
            Color orange = Theme.Hex("#D96B20");
            float vertical = Mathf.Abs(p.x);
            float horizontal = Mathf.Abs(p.y);
            float arcA = Mathf.Abs(p.y - (0.52f * p.x * p.x - 0.32f));
            float arcB = Mathf.Abs(p.x - (-0.52f * p.y * p.y + 0.35f));
            if (vertical < 0.032f || horizontal < 0.032f ||
                arcA < 0.035f || arcB < 0.035f)
                return Theme.Hex("#241813");
            return orange;
        }

        private static Color BaseballIcon(Vector2 p)
        {
            Color leather = Theme.Hex("#F4F0E5");
            float leftCurve = p.x - (-0.43f + 0.24f * p.y * p.y);
            float rightCurve = p.x - (0.43f - 0.24f * p.y * p.y);
            float distance = Mathf.Min(Mathf.Abs(leftCurve), Mathf.Abs(rightCurve));
            if (distance < 0.025f) return Theme.Hex("#C32632");

            float stitch = Mathf.Repeat((p.y + 1f) * 9f, 1f);
            if (distance < 0.095f && Mathf.Abs(stitch - 0.5f) < 0.15f)
                return Theme.Hex("#C32632");
            return leather;
        }

        private static Color TennisIcon(Vector2 p)
        {
            Color felt = Theme.Hex("#C8E04A");
            float field = p.y - 0.55f * Mathf.Sin(p.x * 3.0f);
            float distance = Mathf.Abs(field);
            if (distance < 0.028f) return Theme.Hex("#F4F4EE");
            if (distance < 0.070f) return Theme.Hex("#EEF0E4");
            return felt;
        }

        private static Color VolleyballIcon(Vector2 p)
        {
            float c1 = p.y - 0.52f * Mathf.Sin(p.x * 3.1f);
            float c2 = p.y - 0.52f * Mathf.Sin(p.x * 3.1f + 2.1f);
            float c3 = p.y - 0.52f * Mathf.Sin(p.x * 3.1f + 4.2f);
            float seam = Mathf.Min(Mathf.Abs(c1), Mathf.Min(Mathf.Abs(c2), Mathf.Abs(c3)));
            if (seam < 0.03f) return Theme.Hex("#1A1A1C");
            int id = (c1 > 0f ? 1 : 0) + (c2 > 0f ? 2 : 0) + (c3 > 0f ? 4 : 0);
            if (id == 1 || id == 6) return Theme.Hex("#2F6FBF");
            if (id == 2 || id == 5) return Theme.Hex("#E2B43A");
            return Theme.Hex("#F4F5F2");
        }

        private static Color GolfIcon(Vector2 p)
        {
            Color white = Theme.Hex("#F3F4F0");
            for (int iy = -4; iy <= 4; iy++)
            {
                for (int ix = -4; ix <= 4; ix++)
                {
                    var center = new Vector2(ix * 0.30f, iy * 0.26f);
                    if ((iy & 1) == 1) center.x += 0.15f;
                    if (Vector2.Distance(p, center) < 0.085f)
                        return Theme.Hex("#C5C7C0");
                }
            }
            return white;
        }

        private static Color BowlingIcon(Vector2 p)
        {
            Color body = p.x < 0.02f * Mathf.Sin(p.y * 4f)
                ? Theme.Hex("#7A1F28")
                : Theme.Hex("#2A2C32");
            Vector2[] holes =
            {
                new(-0.16f, 0.38f),
                new(0.16f, 0.38f),
                new(0.00f, 0.12f)
            };
            foreach (Vector2 hole in holes)
                if (Vector2.Distance(p, hole) < 0.11f)
                    return Theme.Hex("#0C0D10");
            return body;
        }

        private static Color EightBallIcon(Vector2 p)
        {
            Color black = Theme.Hex("#141414");
            var center = new Vector2(0f, 0.04f);
            if (Vector2.Distance(p, center) > 0.38f) return black;
            Color white = Theme.Hex("#F4F4F0");
            float e1 = Ellipse(p - new Vector2(0f, 0.12f), 0.11f, 0.11f);
            float e2 = Ellipse(p - new Vector2(0f, -0.08f), 0.11f, 0.11f);
            bool ring = (e1 > 0.55f && e1 < 1.05f) || (e2 > 0.55f && e2 < 1.05f);
            return ring ? Theme.Hex("#111111") : white;
        }

        private static Color BeachBallIcon(Vector2 p)
        {
            float ang = Mathf.Atan2(p.y, p.x);
            int slice = Mathf.FloorToInt((ang / (Mathf.PI * 2f) + 1.5f) * 8f) & 7;
            float seam = Mathf.Abs(Mathf.Repeat(ang / (Mathf.PI * 0.25f), 1f) - 0.5f);
            if (seam < 0.06f) return Theme.Hex("#1A1A1C");
            return (slice % 4) switch
            {
                1 => Theme.Hex("#F6F6F4"),
                2 => Theme.Hex("#F5D14A"),
                3 => Theme.Hex("#2A6FDB"),
                _ => Theme.Hex("#E23B2E")
            };
        }

        private static Color WiffleIcon(Vector2 p)
        {
            for (int i = 0; i < 8; i++)
            {
                float ang = (i + 0.5f) * (Mathf.PI * 0.25f);
                var center = new Vector2(Mathf.Cos(ang) * 0.52f, Mathf.Sin(ang) * 0.52f);
                Vector2 q = p - center;
                float e = Ellipse(q, 0.22f, 0.10f);
                if (e < 1f) return Theme.Hex("#1A1A1A");
            }
            return Theme.Hex("#F6F6F2");
        }

        private static Color SpikeballIcon(Vector2 p)
        {
            float u = (Mathf.Atan2(p.y, p.x) / Mathf.PI + 1f) * 2.4f;
            float v = p.magnitude * 5.2f;
            u += 0.5f * Mathf.Floor(v);
            var f = new Vector2(u - Mathf.Floor(u) - 0.5f, v - Mathf.Floor(v) - 0.5f);
            float hex = Mathf.Max(Mathf.Abs(f.x) * 0.866f + Mathf.Abs(f.y) * 0.5f, Mathf.Abs(f.y));
            return hex > 0.34f ? Theme.Hex("#161616") : Theme.Hex("#E6C31A");
        }

        private static Color DodgeballIcon(Vector2 p)
        {
            if (Mathf.Abs(p.x) < 0.03f || Mathf.Abs(p.y) < 0.03f)
                return Theme.Hex("#1A1A1A");
            return Theme.Hex("#E24B3C");
        }

        private static Color TakrawIcon(Vector2 p)
        {
            float u = (Mathf.Atan2(p.y, p.x) / Mathf.PI + 1f) * 6f;
            float v = (p.y + 1f) * 7f;
            float su = Mathf.Abs(Mathf.Repeat(u, 1f) - 0.5f);
            float sv = Mathf.Abs(Mathf.Repeat(v, 1f) - 0.5f);
            float checker = Mathf.Repeat(Mathf.Floor(u) + Mathf.Floor(v), 2f);
            float strip = checker >= 1f ? su : sv;
            return strip < 0.16f
                ? Color.Lerp(Theme.Hex("#C4A056"), Theme.Hex("#E2C888"), 0.35f)
                : Theme.Hex("#5A3D1C");
        }

        private static bool InsidePentagon(Vector2 p, float radius)
        {
            float angle = Mathf.Atan2(p.y, p.x) + Mathf.PI * 0.5f;
            float sector = Mathf.PI * 2f / 5f;
            float edgeRadius = radius * Mathf.Cos(Mathf.PI / 5f) /
                               Mathf.Cos(Mathf.Repeat(angle + sector * 0.5f, sector) -
                                         sector * 0.5f);
            return p.magnitude <= edgeRadius;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = Vector2.Dot(ab, ab);
            if (denominator <= 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denominator);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
