using System.IO;
using PaintMaze.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PaintMaze.EditorTools
{
    /// <summary>
    /// Renders every catalog ball skin to a PNG so shader changes can be
    /// reviewed without deploying to a device. Output lands in build/skins.
    /// </summary>
    public static class BallSkinPreviewTool
    {
        private const int Size = 512;
        private const string OutputDirectory = "build/skins";
        private const string IconOutputDirectory =
            "Assets/Resources/BallSkinIcons";

        [MenuItem("PaintMaze/Render Ball Skin Previews")]
        public static void Render()
        {
            Directory.CreateDirectory(OutputDirectory);

            var cameraObject = new GameObject("SkinPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 0.62f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            camera.transform.position = new Vector3(0f, 0f, -4f);
            camera.transform.rotation = Quaternion.identity;

            // Match AppRoot's lighting exactly so colours tuned here transfer.
            AmbientMode ambientModeBefore = RenderSettings.ambientMode;
            Color ambientBefore = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * 0.55f;

            var lightObject = new GameObject("SkinPreviewLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.75f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = Vector3.zero;
            sphere.transform.localScale = Vector3.one;
            sphere.transform.rotation = Quaternion.Euler(-14f, 26f, 8f);
            var renderer = sphere.GetComponent<MeshRenderer>();

            // Mirrors the in-game hierarchy: rings sit outside the spin node.
            var rings = new GameObject("Rings", typeof(MeshFilter), typeof(MeshRenderer));
            rings.transform.SetParent(sphere.transform.parent, false);
            rings.transform.localRotation = Quaternion.Euler(20f, 0f, 9f);
            Mesh ringMesh = SportsBallArt.BuildRingMesh(0.62f, 0.95f, 64);
            rings.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            rings.GetComponent<MeshRenderer>().sharedMaterial =
                SportsBallArt.RingMaterial();

            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
            camera.targetTexture = target;

            foreach (BallSkinDefinition skin in CosmeticCatalog.All)
            {
                bool ringed = SportsBallArt.HasRings(skin.Id);
                renderer.sharedMaterial = SportsBallArt.MaterialFor(skin.Id);
                rings.SetActive(ringed);
                camera.orthographicSize = ringed ? 1.05f : 0.62f;
                camera.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;

                File.WriteAllBytes(
                    Path.Combine(OutputDirectory, skin.Id + ".png"),
                    readback.EncodeToPNG());
                Object.DestroyImmediate(readback);

                Sprite icon = SportsBallArt.IconFor(skin.Id);
                if (icon != null && icon.texture != null)
                    WriteIcon(skin.Id, icon.texture);
            }

            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(ringMesh);
            Object.DestroyImmediate(rings);
            Object.DestroyImmediate(sphere);
            Object.DestroyImmediate(lightObject);
            Object.DestroyImmediate(cameraObject);
            RenderSettings.ambientMode = ambientModeBefore;
            RenderSettings.ambientLight = ambientBefore;
            Debug.Log("Ball skin previews written to " + OutputDirectory);
        }

        [MenuItem("PaintMaze/Render Shop Ball Icons")]
        public static void RenderShopIcons()
        {
            Directory.CreateDirectory(IconOutputDirectory);

            ThemeMode themeBefore = Theme.Mode;
            AmbientMode ambientModeBefore = RenderSettings.ambientMode;
            Color ambientBefore = RenderSettings.ambientLight;
            GameObject cameraObject = null;
            GameObject lightObject = null;
            GameObject sphere = null;
            GameObject rings = null;
            Mesh ringMesh = null;
            RenderTexture target = null;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white * 0.55f;

                cameraObject = new GameObject("ShopIconCamera");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.transform.position = new Vector3(0f, 0f, -4f);
                camera.transform.rotation = Quaternion.identity;

                lightObject = new GameObject("ShopIconLight");
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.75f;
                light.shadows = LightShadows.None;
                light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

                sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "ShopIconBall";
                sphere.transform.rotation = Quaternion.Euler(-14f, 26f, 8f);
                Object.DestroyImmediate(sphere.GetComponent<Collider>());
                MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                rings = new GameObject(
                    "ShopIconRings",
                    typeof(MeshFilter),
                    typeof(MeshRenderer));
                rings.transform.rotation = Quaternion.Euler(20f, 0f, 9f);
                ringMesh = SportsBallArt.BuildRingMesh(0.62f, 0.95f, 96);
                rings.GetComponent<MeshFilter>().sharedMesh = ringMesh;
                MeshRenderer ringRenderer = rings.GetComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = SportsBallArt.RingMaterial();
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;

                target = new RenderTexture(
                    Size,
                    Size,
                    24,
                    RenderTextureFormat.ARGB32)
                {
                    name = "ShopIconTarget",
                    antiAliasing = 8
                };
                target.Create();
                camera.targetTexture = target;

                foreach (BallSkinDefinition skin in CosmeticCatalog.All)
                {
                    if (skin.Id == SaveService.DefaultSkinId) continue;
                    RenderShopIcon(
                        camera,
                        renderer,
                        rings,
                        target,
                        skin.Id,
                        skin.Id);
                }

                Theme.Mode = ThemeMode.Light;
                RenderShopIcon(
                    camera,
                    renderer,
                    rings,
                    target,
                    SaveService.DefaultSkinId,
                    SaveService.DefaultSkinId + "_" + ThemeMode.Light);
                Theme.Mode = ThemeMode.Dark;
                RenderShopIcon(
                    camera,
                    renderer,
                    rings,
                    target,
                    SaveService.DefaultSkinId,
                    SaveService.DefaultSkinId + "_" + ThemeMode.Dark);
            }
            finally
            {
                Theme.Mode = themeBefore;
                RenderSettings.ambientMode = ambientModeBefore;
                RenderSettings.ambientLight = ambientBefore;
                if (cameraObject != null)
                    cameraObject.GetComponent<Camera>().targetTexture = null;
                if (target != null) Object.DestroyImmediate(target);
                if (ringMesh != null) Object.DestroyImmediate(ringMesh);
                if (rings != null) Object.DestroyImmediate(rings);
                if (sphere != null) Object.DestroyImmediate(sphere);
                if (lightObject != null) Object.DestroyImmediate(lightObject);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            }

            AssetDatabase.Refresh();
            ConfigureShopIconImports();
            Debug.Log("High-quality shop icons written to " + IconOutputDirectory);
        }

        private static void RenderShopIcon(
            Camera camera,
            MeshRenderer renderer,
            GameObject rings,
            RenderTexture target,
            string skinId,
            string outputName)
        {
            bool ringed = SportsBallArt.HasRings(skinId);
            renderer.sharedMaterial = SportsBallArt.MaterialFor(skinId);
            rings.SetActive(ringed);
            camera.orthographicSize = ringed ? 1.05f : 0.62f;
            camera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(
                Size,
                Size,
                TextureFormat.RGBA32,
                false);
            readback.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;

            string path = Path.Combine(
                    IconOutputDirectory,
                    outputName + ".png")
                .Replace('\\', '/');
            File.WriteAllBytes(path, readback.EncodeToPNG());
            Object.DestroyImmediate(readback);
        }

        private static void ConfigureShopIconImports()
        {
            foreach (string path in Directory.GetFiles(
                         IconOutputDirectory,
                         "*.png"))
            {
                string assetPath = path.Replace('\\', '/');
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceUpdate);
                var importer =
                    AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = Size;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = Size;
                importer.textureCompression =
                    TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // Icon textures upload without keeping CPU data, so round-trip them
        // through a render texture instead of reading them directly.
        private static void WriteIcon(string skinId, Texture source)
        {
            int size = source.width;
            RenderTexture temp = RenderTexture.GetTemporary(
                size,
                size,
                0,
                RenderTextureFormat.ARGB32);
            Graphics.Blit(source, temp);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = temp;
            var readback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0f, 0f, size, size), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(
                Path.Combine(OutputDirectory, skinId + "_icon.png"),
                readback.EncodeToPNG());
            Object.DestroyImmediate(readback);
            RenderTexture.ReleaseTemporary(temp);
        }
    }
}
