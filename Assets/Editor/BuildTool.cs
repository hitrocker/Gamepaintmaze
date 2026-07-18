using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using DebugSymbolFormat = Unity.Android.Types.DebugSymbolFormat;
using DebugSymbolLevel = Unity.Android.Types.DebugSymbolLevel;

namespace PaintMaze.EditorTools
{
    /// <summary>
    /// Headless Android build helper. Configures IL2CPP + ARM64 and builds an APK
    /// to build/PaintMaze.apk. Run via:
    ///   Unity -batchmode -quit -projectPath . -executeMethod PaintMaze.EditorTools.BuildTool.BuildAndroid
    /// (Requires the Android Build Support module installed in the Editor.)
    /// </summary>
    public static class BuildTool
    {
        [MenuItem("Paint Maze/Build Android APK")]
        public static void BuildAndroid()
        {
            BuildAndroidPlayer("build/PaintMaze.apk", BuildOptions.None);
        }

        [MenuItem("Paint Maze/Build Android Development APK")]
        public static void BuildAndroidDevelopment()
        {
            BuildAndroidPlayer(
                "build/PaintMaze-dev.apk",
                BuildOptions.Development | BuildOptions.AllowDebugging);
        }

        [MenuItem("Paint Maze/Build Signed Device APK")]
        public static void BuildSignedDeviceApk()
        {
            string keystorePath = RequiredEnvironment("PAINTMAZE_KEYSTORE_PATH");
            string keystoreAlias = RequiredEnvironment("PAINTMAZE_KEYSTORE_ALIAS");
            string keystorePassword =
                RequiredEnvironment("PAINTMAZE_KEYSTORE_PASSWORD");
            string aliasPassword =
                RequiredEnvironment("PAINTMAZE_KEY_ALIAS_PASSWORD");
            if (!File.Exists(keystorePath))
                throw new FileNotFoundException(
                    "Android upload keystore was not found.", keystorePath);

            bool previousBundleSetting = EditorUserBuildSettings.buildAppBundle;
            bool previousCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string previousKeystoreName = PlayerSettings.Android.keystoreName;
            string previousKeystorePass = PlayerSettings.Android.keystorePass;
            string previousAliasName = PlayerSettings.Android.keyaliasName;
            string previousAliasPass = PlayerSettings.Android.keyaliasPass;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasName = keystoreAlias;
                PlayerSettings.Android.keyaliasPass = aliasPassword;
                BuildAndroidPlayer(
                    "build/PaintMaze-device.apk",
                    BuildOptions.Development | BuildOptions.AllowDebugging);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundleSetting;
                PlayerSettings.Android.useCustomKeystore = previousCustomKeystore;
                PlayerSettings.Android.keystoreName = previousKeystoreName;
                PlayerSettings.Android.keystorePass = previousKeystorePass;
                PlayerSettings.Android.keyaliasName = previousAliasName;
                PlayerSettings.Android.keyaliasPass = previousAliasPass;
                AssetDatabase.SaveAssets();
            }
        }

        [MenuItem("Paint Maze/Build Android App Bundle")]
        public static void BuildAndroidAppBundle()
        {
            string keystorePath = RequiredEnvironment("PAINTMAZE_KEYSTORE_PATH");
            string keystoreAlias = RequiredEnvironment("PAINTMAZE_KEYSTORE_ALIAS");
            string keystorePassword =
                RequiredEnvironment("PAINTMAZE_KEYSTORE_PASSWORD");
            string aliasPassword =
                RequiredEnvironment("PAINTMAZE_KEY_ALIAS_PASSWORD");
            if (!File.Exists(keystorePath))
                throw new FileNotFoundException(
                    "Android upload keystore was not found.", keystorePath);

            EnsureAlwaysIncluded("Standard", "Unlit/Color", "Unlit/Texture",
                "Unlit/Transparent", "Sprites/Default",
                "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive");
            AndroidBranding.ApplyAppIcon();

            bool previousBundleSetting = EditorUserBuildSettings.buildAppBundle;
            bool previousCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            string previousKeystoreName = PlayerSettings.Android.keystoreName;
            string previousKeystorePass = PlayerSettings.Android.keystorePass;
            string previousAliasName = PlayerSettings.Android.keyaliasName;
            string previousAliasPass = PlayerSettings.Android.keyaliasPass;
            DebugSymbolLevel previousSymbolLevel =
                UserBuildSettings.DebugSymbols.level;
            DebugSymbolFormat previousSymbolFormat =
                UserBuildSettings.DebugSymbols.format;
            UIOrientation previousOrientation =
                PlayerSettings.defaultInterfaceOrientation;
            string previousProductName = PlayerSettings.productName;
            string previousCompanyName = PlayerSettings.companyName;

            BuildReport report;
            try
            {
                EditorUserBuildSettings.buildAppBundle = true;
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.allowedAutorotateToPortrait = true;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
                PlayerSettings.allowedAutorotateToLandscapeRight = false;
                PlayerSettings.SetScriptingBackend(
                    NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                UserBuildSettings.DebugSymbols.level =
                    DebugSymbolLevel.SymbolTable;
                UserBuildSettings.DebugSymbols.format =
                    DebugSymbolFormat.IncludeInBundle | DebugSymbolFormat.Zip;
                PlayerSettings.SetApplicationIdentifier(
                    NamedBuildTarget.Android, "com.hitrocker.paintmaze");
                PlayerSettings.productName = "Paint Maze";
                PlayerSettings.companyName = "PaintMaze";
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = keystorePassword;
                PlayerSettings.Android.keyaliasName = keystoreAlias;
                PlayerSettings.Android.keyaliasPass = aliasPassword;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SetupTool.ScenePath },
                    locationPathName = "build/PaintMaze-release.aab",
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundleSetting;
                PlayerSettings.Android.useCustomKeystore = previousCustomKeystore;
                PlayerSettings.Android.keystoreName = previousKeystoreName;
                PlayerSettings.Android.keystorePass = previousKeystorePass;
                PlayerSettings.Android.keyaliasName = previousAliasName;
                PlayerSettings.Android.keyaliasPass = previousAliasPass;
                UserBuildSettings.DebugSymbols.level = previousSymbolLevel;
                UserBuildSettings.DebugSymbols.format = previousSymbolFormat;
                PlayerSettings.defaultInterfaceOrientation = previousOrientation;
                PlayerSettings.productName = previousProductName;
                PlayerSettings.companyName = previousCompanyName;
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[PaintMaze] Android App Bundle result: " +
                      report.summary.result + " size=" +
                      report.summary.totalSize + " bytes");
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        private static void BuildAndroidPlayer(string outputPath, BuildOptions buildOptions)
        {
            // The board builds its materials at runtime via Shader.Find(...).
            // Built players strip shaders no asset references, so force-include them.
            // (Sprites/Default & Unlit/Transparent drive the gradient background and the
            // fake ball shadow; the additive particle shader drives the ball sparks.)
            EnsureAlwaysIncluded("Standard", "Unlit/Color", "Unlit/Texture",
                "Unlit/Transparent", "Sprites/Default",
                "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive");
            AndroidBranding.ApplyAppIcon();

            // Portrait-only puzzle game.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.hitrocker.paintmaze");
            PlayerSettings.productName = "Paint Maze";
            PlayerSettings.companyName = "PaintMaze";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SetupTool.ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[PaintMaze] Android build result: " + report.summary.result +
                      " size=" + report.summary.totalSize + " bytes");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        private static string RequiredEnvironment(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    "Missing required signing environment variable: " + name);
            return value;
        }

        private static void EnsureAlwaysIncluded(params string[] shaderNames)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) return;

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;

                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }

                if (!present)
                {
                    int idx = arr.arraySize;
                    arr.InsertArrayElementAtIndex(idx);
                    arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
                    Debug.Log("[PaintMaze] Added always-included shader: " + name);
                }
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
