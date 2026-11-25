using System;
using System.Linq;
using Halazia.AI.Sentinel;
using Halazia.Shared.Combat;
using Halazia.Shared.Movement;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Reproducible, Sentinel-only setup. Never edits shared enemy assets.</summary>
public static class SentinelEvaluationSetup
{
    private const string ScenePath = "Assets/Scenes/SentinelTestScene.unity";
    private const string ControllerPath = "Assets/Animations/Agents/SentinelAnimator.controller";

    [MenuItem("Tools/Halazia/Sentinel Test/Prepare Evaluation Scene and Prefab")]
    public static void Prepare()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        if (Application.isBatchMode) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (SceneManager.GetActiveScene().path != ScenePath || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Open only SentinelTestScene first.");
        var root = GameObject.Find("SentinelTest");
        if (root == null) throw new InvalidOperationException("SentinelTest fixture is missing.");
        var sentinel = root.GetComponent<SentinelAgent>();
        var provider = root.GetComponent<SentinelNavMeshPrototype>();
        Set(sentinel, "_pathProvider", provider);
        Set(sentinel, "_guardPosition", GameObject.Find("GuardPosition").transform);
        var player = GameObject.Find("TestPlayerTarget");
        Set(sentinel, "_playerTarget", player.transform);
        var route = new SerializedObject(sentinel);
        var points = route.FindProperty("_patrolWaypoints");
        points.arraySize = 3;
        for (int i = 0; i < 3; i++) points.GetArrayElementAtIndex(i).objectReferenceValue = GameObject.Find("PatrolWaypoint" + (i + 1)).transform;
        route.ApplyModifiedPropertiesWithoutUndo();

        var controller = BuildController();
        var visual = root.transform.Find("SentinelVisual");
        if (visual == null)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Models/Agents/Y Bot.fbx");
            if (model == null) throw new InvalidOperationException("Existing Y Bot model is missing.");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            go.name = "SentinelVisual";
            visual = go.transform;
            visual.localPosition = Vector3.zero;
            visual.localRotation = Quaternion.identity;
        }
        var animator = GetOrAdd<Animator>(visual.gameObject);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        var driver = GetOrAdd<AgentAnimatorDriver>(visual.gameObject);
        GetOrAdd<CombatActionsAdapter>(visual.gameObject);
        Set(driver, "_motorSource", root.GetComponent<AgentMotor>());
        // Duration matches the actual clip; its animation events are optional.
        var settings = new SerializedObject(driver);
        settings.FindProperty("_defaultAttackDuration").floatValue = Clip("Standing Melee Attack Horizontal").length;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var body = root.transform.Find("Body");
        if (body != null) body.gameObject.SetActive(false);

        // A first-person player for reciprocal damage, using the existing controller unchanged.
        var oldCollider = player.GetComponent<BoxCollider>();
        if (oldCollider != null) UnityEngine.Object.DestroyImmediate(oldCollider);
        var capsule = GetOrAdd<CharacterController>(player);
        capsule.height = 2f; capsule.radius = 0.5f; capsule.center = Vector3.up;
        var camera = GameObject.Find("Main Camera").GetComponent<Camera>();
        camera.transform.SetParent(player.transform, false);
        camera.transform.localPosition = Vector3.up * 1.6f;
        camera.transform.localRotation = Quaternion.identity;
        Set(GetOrAdd<FirstPersonController>(player), "playerCamera", camera.transform);
        Set(GetOrAdd<SentinelTestWeapon>(player), "_aimCamera", camera);
        var playerBody = player.transform.Find("Body");
        if (playerBody != null) playerBody.gameObject.SetActive(false);

        // Full-height wall makes sight occlusion unambiguous.
        var wall = GameObject.Find("ObstacleWall").transform;
        wall.position = new Vector3(0.5f, 1.5f, 0.5f);
        wall.localScale = new Vector3(8f, 3f, 1f);
        // Second wall makes a 2m exit opening at x=5.5, z=0.5.
        var exitWall = GameObject.Find("SentinelExitWall");
        if (exitWall == null) { exitWall = GameObject.CreatePrimitive(PrimitiveType.Cube); exitWall.name = "SentinelExitWall"; }
        exitWall.transform.position = new Vector3(15.75f, 1.5f, 0.5f);
        exitWall.transform.localScale = new Vector3(18.5f, 3f, 1f);
        var marker = GameObject.Find("SentinelOrderPosition");
        if (marker == null) marker = new GameObject("SentinelOrderPosition");
        marker.transform.position = new Vector3(5.5f, 0f, 0.5f);
        var controls = GetOrAdd<SentinelEvaluationControls>(root);
        Set(controls, "_sentinel", sentinel); Set(controls, "_orderPosition", marker.transform);

        // Prefab cannot retain scene targets/routes. Clear only on a temporary clone.
        var clone = UnityEngine.Object.Instantiate(root);
        try
        {
            clone.name = "Sentinel";
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<SentinelEvaluationControls>());
            var cloneAgent = clone.GetComponent<SentinelAgent>();
            Set(cloneAgent, "_playerTarget", null); Set(cloneAgent, "_guardPosition", null);
            var serialized = new SerializedObject(cloneAgent);
            serialized.FindProperty("_patrolWaypoints").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(clone, "Assets/Prefabs/Sentinel.prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("SENTINEL_SETUP_PASS: evaluation scene and animated Sentinel prefab saved. Bake next; Play Mode remains separate.");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    private static AnimationClip Clip(string name)
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Animations/Agents/Clips/Y Bot@" + name + ".fbx")
            .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null) throw new InvalidOperationException("Missing animation clip: " + name);
        return clip;
    }
    private static AnimatorController BuildController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null) return existing;
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
        var sm = controller.layers[0].stateMachine;
        var locomotion = sm.AddState("Locomotion");
        var blend = new BlendTree { name = "Sentinel locomotion", blendParameter = "Speed", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(blend, controller);
        blend.AddChild(Clip("Pistol Idle"), 0f); blend.AddChild(Clip("Pistol Run"), 4.2f);
        locomotion.motion = blend; sm.defaultState = locomotion;
        var death = sm.AddState("Death"); death.motion = Clip("Death");
        var die = sm.AddAnyStateTransition(death);
        die.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        die.hasExitTime = false; die.duration = 0.05f; die.canTransitionToSelf = false;
        foreach (var pair in new[] { ("Attack", "Standing Melee Attack Horizontal"), ("Hit", "Hit Reaction") })
        {
            var state = sm.AddState(pair.Item1); state.motion = Clip(pair.Item2);
            var enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0, pair.Item1);
            enter.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
            enter.hasExitTime = false; enter.duration = 0.05f; enter.canTransitionToSelf = false;
            var leave = state.AddTransition(locomotion);
            leave.hasExitTime = true; leave.exitTime = 1f; leave.duration = 0.05f;
            leave.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
        }
        EditorUtility.SetDirty(controller);
        return controller;
    }
}
