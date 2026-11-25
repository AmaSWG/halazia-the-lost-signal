using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SentinelRifleAttachment
{
    static SentinelRifleAttachment() { EditorApplication.delayCall += CheckRequest; }
    static void CheckRequest()
    {
        const string request = "Library/SentinelRifleAttach.request";
        if (!File.Exists(request)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += CheckRequest; return; }
        File.Delete(request);
        try { Inspect(); }
        catch (Exception e) { File.WriteAllText("Artifacts/SentinelRifle/attachment-failure.txt", e.ToString()); Debug.LogException(e); }
    }
    const string Model = "Assets/Art/Models/Agents/Sentinel/Sentinel_Robot.fbx";
    const string Rifle = "Assets/Prefabs/Weapons/Sentinel_Rifle.prefab";
    const string Scene = "Assets/Scenes/SentinelTestScene.unity";
    const string Controller = "Assets/Animations/Agents/SentinelAnimator.controller";
    const string Report = "Artifacts/SentinelRifle/attachment-report.txt";

    public static void Inspect()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var instance = UnityEngine.Object.Instantiate(model);
        try
        {
            var lines = instance.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.Contains("Hand") || t.name.Contains("Arm"))
                .Select(t => t.name + " | " + PathOf(t, instance.transform) + " | pos " + t.position.ToString("F3") + " | rot " + t.rotation.eulerAngles.ToString("F1"));
            Directory.CreateDirectory("Artifacts/SentinelRifle");
            File.WriteAllLines(Report, lines);
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }

    static string PathOf(Transform t, Transform root)
    {
        var path = t.name;
        while (t.parent && t.parent != root) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }
}
