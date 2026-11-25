using System.Collections.Generic;
using System.Linq;
using System.Text;
using Halazia.Shared.Movement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Read-only check that the Sentinel robot's own Generic rig, Avatar and clips drive the actual
/// Sentinel_Robot meshes. Samples every controller state on a temporary instance of the model and
/// measures real skinned-mesh deformation, so a pass means the clip visibly moves this robot.
/// Menu: Tools/Halazia/Sentinel Test/Verify Sentinel Animation Setup
/// Batch: -executeMethod SentinelAnimationSetupCheck.Run (only when the project is not open elsewhere)
/// </summary>
public static class SentinelAnimationSetupCheck
{
    private const string ModelPath = "Assets/Art/Models/Agents/Sentinel/Sentinel_Robot.fbx";
    private const string ControllerPath = "Assets/Animations/Agents/SentinelAnimator.controller";

    // state name, should loop, bone that must visibly move, mesh part skinned to that bone
    private static readonly (string state, bool loop, string bone, string mesh)[] Expected =
    {
        ("Sentinel_Rig|Idle", true, "Head", "Sentinel_Head"),
        ("Sentinel_Rig|Walk", true, "LeftUpperLeg", "Sentinel_LeftUpperLeg"),
        ("Sentinel_Rig|Run", true, "LeftUpperLeg", "Sentinel_LeftUpperLeg"),
        ("Sentinel_Rig|Patrol", true, "LeftUpperLeg", "Sentinel_LeftUpperLeg"),
        ("Sentinel_Rig|Attack", false, "RightUpperArm", "Sentinel_RightUpperArm"),
        ("Sentinel_Rig|TakeDamage", false, "Chest", "Sentinel_Chest"),
        ("Sentinel_Rig|Death", false, "Hips", "Sentinel_Hips"),
    };

    [MenuItem("Tools/Halazia/Sentinel Test/Verify Sentinel Animation Setup")]
    public static void RunFromMenu() => Verify();

    public static void Run()
    {
        if (!Verify() && Application.isBatchMode) EditorApplication.Exit(1);
    }

    private static bool Verify()
    {
        var log = new StringBuilder("[SentinelAnimationSetupCheck]\n");
        int failures = 0;
        void Pass(string msg) => log.AppendLine("  PASS " + msg);
        void Fail(string msg) { failures++; log.AppendLine("  FAIL " + msg); }

        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (importer == null || modelPrefab == null || controller == null)
        {
            Debug.LogError($"[SentinelAnimationSetupCheck] Missing asset: model={modelPrefab != null}, controller={controller != null}");
            return false;
        }

        // 1. Rig / Avatar
        if (importer.animationType == ModelImporterAnimationType.Generic) Pass("Rig is Generic (robot uses its own non-humanoid rig).");
        else Fail($"Rig is {importer.animationType}; expected Generic.");
        if (importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel) Pass("Avatar Definition = Create From This Model.");
        else Fail($"Avatar Definition = {importer.avatarSetup}; expected CreateFromThisModel.");

        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar != null && avatar.isValid && !avatar.isHuman) Pass($"Model Avatar '{avatar.name}' is valid and Generic.");
        else Fail("Model Avatar is missing, invalid, or Humanoid.");

        var importedAnimator = modelPrefab.GetComponent<Animator>();
        if (importedAnimator != null && importedAnimator.avatar == avatar) Pass("Imported Sentinel_Robot Animator uses the Sentinel's own Avatar.");
        else Fail("Imported Sentinel_Robot Animator does not reference the Sentinel's own Avatar.");

        // 2. Controller states, clip source and loop flags, then sample each on a temporary model instance
        var states = controller.layers[0].stateMachine.states.ToDictionary(s => s.state.name, s => s.state);
        var instance = (GameObject)Object.Instantiate(modelPrefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            foreach (var e in Expected)
            {
                if (!states.TryGetValue(e.state, out var state)) { Fail($"{e.state}: state missing from controller."); continue; }
                var clip = state.motion as AnimationClip;
                if (clip == null) { Fail($"{e.state}: state has no AnimationClip (broken motion reference)."); continue; }
                if (AssetDatabase.GetAssetPath(clip) != ModelPath) { Fail($"{e.state}: clip comes from '{AssetDatabase.GetAssetPath(clip)}', not Sentinel_Robot.fbx."); continue; }
                if (clip.isHumanMotion) Fail($"{e.state}: clip is Humanoid motion; expected Generic.");
                if (clip.isLooping != e.loop) Fail($"{e.state}: Loop Time = {clip.isLooping}; expected {e.loop}.");

                var bindings = AnimationUtility.GetCurveBindings(clip);
                var unresolved = bindings.Where(b => b.path.Length > 0 && instance.transform.Find(b.path) == null)
                                         .Select(b => b.path).Distinct().ToList();
                if (bindings.Length == 0 || unresolved.Count > 0)
                {
                    Fail($"{e.state}: {unresolved.Count} binding paths do not exist on Sentinel_Robot (e.g. {unresolved.FirstOrDefault()}).");
                    continue;
                }

                float boneDeg = SampleMaxBoneAngle(clip, instance, e.bone);
                float meshMove = SampleMaxMeshMotion(clip, instance, e.mesh);
                bool moves = boneDeg > 1f && meshMove > 0.005f;
                string detail = $"{e.state}: {bindings.Length} curves all bound to Sentinel bones, loop={clip.isLooping}, " +
                                $"{e.bone} rotates {boneDeg:F1}°, {e.mesh} mesh moves {meshMove * 100f:F1} cm";
                if (moves && clip.isLooping == e.loop) Pass(detail);
                else if (!moves) Fail(detail + " (no visible motion on the actual model)");
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        // 3. Scene instances using this controller (root motion must stay off; NavMesh/AgentMotor moves the body)
        foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include))
        {
            if (animator.runtimeAnimatorController != controller) continue;
            string n = animator.gameObject.name;
            if (animator.avatar == avatar) Pass($"Scene '{n}': Animator avatar is the Sentinel Avatar.");
            else Fail($"Scene '{n}': Animator avatar is '{(animator.avatar ? animator.avatar.name : "None")}', expected the Sentinel Avatar.");
            if (!animator.applyRootMotion) Pass($"Scene '{n}': Apply Root Motion is off.");
            else Fail($"Scene '{n}': Apply Root Motion is ON; it would fight AgentMotor/NavMesh movement.");
            if (animator.GetComponent<AgentAnimatorDriver>() != null) Pass($"Scene '{n}': AgentAnimatorDriver present.");
            else Fail($"Scene '{n}': AgentAnimatorDriver missing on the Animator object.");
        }

        log.AppendLine(failures == 0 ? "RESULT: all Sentinel animation checks passed." : $"RESULT: {failures} check(s) failed.");
        if (failures == 0) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
        return failures == 0;
    }

    private static float SampleMaxBoneAngle(AnimationClip clip, GameObject root, string boneName)
    {
        var bone = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == boneName);
        if (bone == null) return 0f;
        var rotations = new List<Quaternion>();
        foreach (float t in SampleTimes(clip)) { clip.SampleAnimation(root, t); rotations.Add(bone.localRotation); }
        float max = 0f;
        foreach (var r in rotations) max = Mathf.Max(max, Quaternion.Angle(rotations[0], r));
        return max;
    }

    private static float SampleMaxMeshMotion(AnimationClip clip, GameObject root, string meshName)
    {
        var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == meshName);
        if (smr == null) return 0f;
        var baked = new Mesh();
        try
        {
            var centers = new List<Vector3>();
            foreach (float t in SampleTimes(clip))
            {
                clip.SampleAnimation(root, t);
                smr.BakeMesh(baked, true);
                centers.Add(smr.transform.TransformPoint(baked.bounds.center));
            }
            float max = 0f;
            foreach (var c in centers) max = Mathf.Max(max, Vector3.Distance(centers[0], c));
            return max;
        }
        finally
        {
            Object.DestroyImmediate(baked);
        }
    }

    private static IEnumerable<float> SampleTimes(AnimationClip clip)
    {
        const int steps = 16;
        for (int i = 0; i <= steps; i++) yield return clip.length * i / steps;
    }
}
