using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PaintMaze.Game;

namespace PaintMaze.EditorTools
{
    /// <summary>
    /// One-click project setup. Creates the single bootstrap scene (an AppRoot
    /// GameObject builds everything else at runtime) and registers it in Build
    /// Settings. Runnable from the menu or headlessly via -executeMethod.
    /// </summary>
    public static class SetupTool
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Paint Maze/Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var root = new GameObject("AppRoot");
            root.AddComponent<AppRoot>();

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            Debug.Log("[PaintMaze] Bootstrap scene created and added to Build Settings: " + ScenePath);
        }
    }
}
