using System;
using Halazia.AI.Commander;
using Halazia.AI.Sentinel;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds the existing Enemy_commander prefab, a corridor heatmap and two corridor markers to
/// SentinelTestScene only. Never edits the prefab asset or any other scene. Safe to run repeatedly.</summary>
public static class SentinelCommanderTestSetup
{
    private const string ScenePath = "Assets/Scenes/SentinelTestScene.unity";
    private const string CommanderPrefabPath = "Assets/Prefabs/Enemy_commander.prefab";
    private static readonly Vector3 CommanderSpawn = new Vector3(-10f, 0f, 8f);
    private static readonly (string id, Vector3 position)[] Corridors =
    {
        ("Corridor_TestExitA", new Vector3(-8f, 0f, -9f)),
        ("Corridor_TestExitB", new Vector3(9f, 0f, 5f)),
    };

    [MenuItem("Tools/Halazia/Sentinel Test/Add Commander Integration")]
    public static void AddFromMenu() => Configure(save: true);

    /// <summary>Batch entry point: opens the test scene, configures and saves it.</summary>
    public static void AddAndSave()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Configure(save: true);
    }

    public static void Configure(bool save)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open only SentinelTestScene first.");
        var sentinelRoot = GameObject.Find("SentinelTest");
        var sentinel = sentinelRoot != null ? sentinelRoot.GetComponent<SentinelAgent>() : null;
        var player = GameObject.Find("TestPlayerTarget");
        if (sentinel == null || player == null) throw new InvalidOperationException("SentinelTest or TestPlayerTarget is missing.");

        // Sentinel-only testing deactivates these roots; GameObject.Find skips inactive objects and
        // would add a duplicate Commander. Reuse and re-activate the existing ones instead.
        var commanderGo = FindRoot(scene, "CommanderTest");
        if (commanderGo == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommanderPrefabPath);
            if (prefab == null) throw new InvalidOperationException("Existing Commander prefab is missing: " + CommanderPrefabPath);
            commanderGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            commanderGo.name = "CommanderTest";
            commanderGo.transform.SetPositionAndRotation(CommanderSpawn, Quaternion.LookRotation(Vector3.back));
        }
        var commander = commanderGo.GetComponent<CommanderAgent>();
        if (commander == null) throw new InvalidOperationException("CommanderTest has no CommanderAgent.");
        commanderGo.SetActive(true);
        commander.enabled = true;
        // Characters stay out of NavMesh bakes, matching the Sentinel and target fixtures.
        var modifier = commanderGo.GetComponent<NavMeshModifier>() ?? commanderGo.AddComponent<NavMeshModifier>();
        modifier.ignoreFromBuild = true;

        var graph = GameObject.Find("CommanderCorridorGraph") ?? new GameObject("CommanderCorridorGraph");
        var tracker = graph.GetComponent<CorridorHeatmapTracker>() ?? graph.AddComponent<CorridorHeatmapTracker>();
        foreach (var (id, position) in Corridors)
        {
            var markerGo = GameObject.Find(id) ?? new GameObject(id);
            markerGo.transform.SetParent(graph.transform, true);
            markerGo.transform.position = position;
            var marker = markerGo.GetComponent<CorridorNodeMarker>() ?? markerGo.AddComponent<CorridorNodeMarker>();
            marker.NodeId = id;
            marker.Radius = 3f;
        }

        var so = new SerializedObject(commander);
        so.FindProperty("_playerTransform").objectReferenceValue = player.transform;
        so.FindProperty("_heatmapTracker").objectReferenceValue = tracker;
        var squad = so.FindProperty("_initialSquadMembers");
        squad.arraySize = 1;
        squad.GetArrayElementAtIndex(0).objectReferenceValue = sentinel;
        so.ApplyModifiedPropertiesWithoutUndo();

        var harnessGo = FindRoot(scene, "CommanderSentinelTest") ?? new GameObject("CommanderSentinelTest");
        harnessGo.SetActive(true);
        var harness = harnessGo.GetComponent<SentinelCommanderTestControls>() ?? harnessGo.AddComponent<SentinelCommanderTestControls>();
        var hs = new SerializedObject(harness);
        hs.FindProperty("_commander").objectReferenceValue = commander;
        hs.FindProperty("_sentinel").objectReferenceValue = sentinel;
        hs.FindProperty("_coverRoutePoint").objectReferenceValue = GameObject.Find(Corridors[1].id).transform;
        hs.FindProperty("_holdPoint").objectReferenceValue = GameObject.Find("PatrolWaypoint3").transform;
        hs.ApplyModifiedPropertiesWithoutUndo();

        // Verify with the Sentinel's own planner against the existing bake; no rebake is required.
        var provider = sentinelRoot.GetComponent<SentinelNavMeshPrototype>();
        Call(provider, "Awake");
        foreach (var (id, position) in Corridors)
            if (!provider.TryGetPath(sentinel.transform.position, position, out _))
                throw new InvalidOperationException($"No baked path from SentinelTest to {id}. Run Bake and Verify NavMesh.");
        if (!provider.TryGetPath(sentinel.transform.position, CommanderSpawn, out _))
            throw new InvalidOperationException("CommanderTest spawn is not on the baked NavMesh.");

        if (save)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("SENTINEL_COMMANDER_SETUP_PASS: CommanderTest (Enemy_commander instance), heatmap, 2 corridor markers " +
                  "and CommanderSentinelTest controls configured in SentinelTestScene." + (save ? " Scene saved." : " Not saved."));
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        return null;
    }

    private static void Call(object target, string method) =>
        target.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(target, null);
}
