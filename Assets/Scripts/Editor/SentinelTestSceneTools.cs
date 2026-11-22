using System;
using System.Collections.Generic;
using Halazia.AI.Sentinel;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>Editor-only bake and diagnostics for the isolated Sentinel test scene.</summary>
public static class SentinelTestSceneTools
{
    private const string ScenePath = "Assets/Scenes/SentinelTestScene.unity";

    [MenuItem("Tools/Halazia/Sentinel Test/Bake and Verify NavMesh")]
    public static void BakeAndVerify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before baking.");
        if (Application.isBatchMode) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open only SentinelTestScene before baking (Collect All includes loaded scenes).");
        var surface = GameObject.Find("Navigation").GetComponent<NavMeshSurface>();
        if (surface == null || surface.agentTypeID != 0 || surface.collectObjects != CollectObjects.All ||
            surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders)
            throw new InvalidOperationException("Expected Humanoid (0), All Game Objects, Physics Colliders on Navigation.");
        Physics.SyncTransforms();
        surface.BuildNavMesh();
        if (surface.navMeshData == null || NavMesh.CalculateTriangulation().indices.Length == 0)
            throw new InvalidOperationException("Bake produced no NavMesh triangles.");
        // Keep each bake as a real asset; never overwrite another surface's asset.
        const string folder = "Assets/Scenes/SentinelTestScene";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scenes", "SentinelTestScene");
        AssetDatabase.CreateAsset(surface.navMeshData,
            AssetDatabase.GenerateUniqueAssetPath(folder + "/SentinelNavMesh.asset"));
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        VerifyPaths();
        Debug.Log("SENTINEL_SCENE_BAKE_PASS: NavMesh saved; spawn, guard, patrol and target paths verified. This is not a Play Mode test.");
    }

    [MenuItem("Tools/Halazia/Sentinel Test/Verify Baked Paths")]
    public static void VerifyPaths()
    {
        if (SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open SentinelTestScene first.");
        var provider = GameObject.Find("SentinelTest").GetComponent<SentinelNavMeshPrototype>();
        var names = new List<string> { "SentinelTest", "GuardPosition", "PatrolWaypoint1", "PatrolWaypoint2", "PatrolWaypoint3", "TestPlayerTarget" };
        if (GameObject.Find("SentinelOrderPosition") != null) names.Add("SentinelOrderPosition");
        foreach (string from in names)
            foreach (string to in names)
                if (from != to && !provider.TryGetPath(GameObject.Find(from).transform.position,
                    GameObject.Find(to).transform.position, out _))
                    throw new InvalidOperationException($"No complete path: {from} -> {to} (provider sample tolerance 0.1m).");
        Debug.Log("SENTINEL_SCENE_PATHS_PASS: all fixture positions connected using the existing provider.");
    }

    [MenuItem("Tools/Halazia/Sentinel Test/Log Play Mode Status")]
    public static void LogStatus()
    {
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Enter Play Mode in SentinelTestScene first.");
        var sentinel = GameObject.Find("SentinelTest").GetComponent<SentinelAgent>();
        var target = GameObject.Find("TestPlayerTarget").GetComponent<SentinelTargetHealth>();
        Debug.Log($"Sentinel: {sentinel.State}, health {sentinel.CurrentHealth}/{sentinel.MaxHealth}; target health: {target.CurrentHealth}, alive: {target.IsAlive}", sentinel);
    }
}
