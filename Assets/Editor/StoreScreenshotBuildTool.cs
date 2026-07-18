using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PaintMaze.EditorTools
{
    public static class StoreScreenshotBuildTool
    {
        [MenuItem("Paint Maze/Build Screenshot Player")]
        public static void BuildMacScreenshotPlayer()
        {
            IncludeRuntimeShaders(
                "Standard",
                "Unlit/Color",
                "Unlit/Texture",
                "Unlit/Transparent",
                "Sprites/Default",
                "Legacy Shaders/Particles/Additive",
                "Mobile/Particles/Additive");

            string previousProductName = PlayerSettings.productName;
            string previousCompanyName = PlayerSettings.companyName;
            UIOrientation previousOrientation =
                PlayerSettings.defaultInterfaceOrientation;
            BuildReport report;
            try
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                PlayerSettings.productName = "Paint Maze Screenshot";
                PlayerSettings.companyName = "PaintMaze";

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SetupTool.ScenePath },
                    locationPathName = "build/PaintMazeScreenshot.app",
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.None
                };
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                PlayerSettings.productName = previousProductName;
                PlayerSettings.companyName = previousCompanyName;
                PlayerSettings.defaultInterfaceOrientation = previousOrientation;
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[StoreCapture] Build result: " + report.summary.result);
            if (report.summary.result != BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        private static void IncludeRuntimeShaders(params string[] shaderNames)
        {
            Object[] assets =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty shaders =
                settings.FindProperty("m_AlwaysIncludedShaders");
            if (shaders == null) return;

            foreach (string name in shaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;

                bool present = false;
                for (int i = 0; i < shaders.arraySize; i++)
                {
                    if (shaders.GetArrayElementAtIndex(i).objectReferenceValue != shader)
                        continue;
                    present = true;
                    break;
                }
                if (present) continue;

                int index = shaders.arraySize;
                shaders.InsertArrayElementAtIndex(index);
                shaders.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            }

            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
