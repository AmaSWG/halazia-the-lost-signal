using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Halazia.Presentation.Editor
{
    public static class HalaziaOpeningSetup
    {
        [MenuItem("HALAZIA/Create Opening Scene")]
        public static void Create()
        {
            const string opening = "Assets/Scenes/Halazia_Opening.unity";
            const string gameplay = "Assets/Scenes/halazia_Ship_World.unity";
            if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode first."); return; }
            if (!File.Exists(gameplay)) { Debug.LogError("Cannot find " + gameplay); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(opening))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("HALAZIA Opening Director").AddComponent<HalaziaOpening>();
                EditorSceneManager.SaveScene(scene, opening);
            }
            else EditorSceneManager.OpenScene(opening, OpenSceneMode.Single);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(opening, true) };
            bool hasGameplay = false;
            foreach (var entry in EditorBuildSettings.scenes)
            {
                if (entry.path == opening) continue;
                if (entry.path == gameplay) { scenes.Add(new EditorBuildSettingsScene(gameplay, true)); hasGameplay = true; }
                else scenes.Add(entry);
            }
            if (!hasGameplay) scenes.Add(new EditorBuildSettingsScene(gameplay, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Selection.activeGameObject = GameObject.Find("HALAZIA Opening Director");
            Debug.Log("Opening ready. Press Play to preview the 60-second intro. SPACE/ESC skips to the ship scene.");
        }
    }
}
