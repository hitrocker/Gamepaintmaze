using System;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace PaintMaze.EditorTools
{
    public static class AndroidBranding
    {
        private const string LegacyIconPath =
            "Assets/Branding/PaintMazeIcon.png";
        private const string AdaptiveForegroundPath =
            "Assets/Branding/PaintMazeIconAdaptiveForeground.png";
        private const string AdaptiveBackgroundPath =
            "Assets/Branding/PaintMazeIconAdaptiveBackground.png";

        [MenuItem("Paint Maze/Apply Android App Icon")]
        public static void ApplyAppIcon()
        {
            Texture2D legacy = RequiredTexture(LegacyIconPath);
            Texture2D foreground = RequiredTexture(AdaptiveForegroundPath);
            Texture2D background = RequiredTexture(AdaptiveBackgroundPath);

#pragma warning disable CS0618
            SetSingleLayer(AndroidPlatformIconKind.Legacy, legacy);
            SetSingleLayer(AndroidPlatformIconKind.Round, legacy);
#pragma warning restore CS0618
            SetAdaptiveLayers(background, foreground);

            AssetDatabase.SaveAssets();
            Debug.Log("[PaintMaze] Android launcher icons configured.");
        }

        private static void SetSingleLayer(
            PlatformIconKind kind,
            Texture2D texture)
        {
            PlatformIcon[] icons =
                PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            for (int i = 0; i < icons.Length; i++)
                icons[i].SetTextures(texture);
            PlayerSettings.SetPlatformIcons(
                NamedBuildTarget.Android, kind, icons);
        }

        private static void SetAdaptiveLayers(
            Texture2D background,
            Texture2D foreground)
        {
            PlatformIconKind kind = AndroidPlatformIconKind.Adaptive;
            PlatformIcon[] icons =
                PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            for (int i = 0; i < icons.Length; i++)
                icons[i].SetTextures(background, foreground);
            PlayerSettings.SetPlatformIcons(
                NamedBuildTarget.Android, kind, icons);
        }

        private static Texture2D RequiredTexture(string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new InvalidOperationException(
                    "Required Android icon texture is missing: " + path);
            return texture;
        }
    }
}
