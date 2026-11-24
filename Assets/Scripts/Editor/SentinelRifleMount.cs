using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Two-handed rifle hold for the armed Sentinel (visual only; no AI, combat or shooting code).
///
/// Builds, from the existing Sentinel_Robot rig and Sentinel_Rifle prefab:
///  - Sentinel_RifleHold.anim   : constant upper-body pose (rifle shouldered at a low ready);
///  - Sentinel_RifleStrike.anim : 1 s rifle jab whose impact frame is at 0.5 s, the time of the existing
///                                AnimEvent_AttackHit on the melee Attack clip (the event itself is untouched);
///  - Sentinel_RifleHold.mask   : Generic avatar mask enabling only the 10 upper-body bones below;
///  - a "RifleHold" layer appended to SentinelAnimator.controller as a SYNCED layer of the Base Layer: it mirrors
///    the Base Layer's states, transitions and timing and only swaps the upper-body motion per state
///    (Attack -> rifle strike, every other state -> hold). The Base Layer, its transitions, parameters and the
///    Attack event are not modified, so AI-driven state changes and damage timing are unchanged;
///  - Sentinel_Robot_Armed.prefab: RightHand_WeaponMount re-placed so the rifle sits in the solved grip.
///
/// The hold bones are Spine, Neck, both shoulders and both arms. Spine, Neck and the shoulders are never animated by
/// the existing clips (arms only by unarmed swings), so no leg, hip or chest motion is removed: the TakeDamage chest
/// flinch and the Death fall still play and carry the arms and rifle with them.
/// </summary>
public static class SentinelRifleMount
{
    public const string ModelPath = "Assets/Art/Models/Agents/Sentinel/Sentinel_Robot.fbx";
    public const string RiflePath = "Assets/Prefabs/Weapons/Sentinel_Rifle.prefab";
    public const string ControllerPath = "Assets/Animations/Agents/SentinelAnimator.controller";
    public const string VariantPath = "Assets/Prefabs/Sentinel_Robot_Armed.prefab";
    public const string ClipPath = "Assets/Animations/Agents/Sentinel_RifleHold.anim";
    public const string StrikeClipPath = "Assets/Animations/Agents/Sentinel_RifleStrike.anim";
    public const string MaskPath = "Assets/Animations/Agents/Sentinel_RifleHold.mask";
    public const string LayerName = "RifleHold";
    public const string MountName = "RightHand_WeaponMount";
    public const string BaseOutDir = "Artifacts/SentinelRifle/Hold";
    private static string OutDir = BaseOutDir;
    private static readonly string[] HoldBones = { "Spine", "Neck", "LeftShoulder", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "LeftUpperArm", "LeftLowerArm", "LeftHand" };
    private static readonly string[] ClipOrder = { "Idle", "Walk", "Run", "Patrol", "Attack", "TakeDamage", "Death" };
    private static readonly string[] SocketNames = { "Grip_Socket", "Support_Hand_Socket", "Muzzle_Point", "Sight_Point", "Ejection_Point" };
    private const float AttackEventTime = 0.5f; // AnimEvent_AttackHit on Sentinel_Rig|Attack (verified below)

    // ---------------- pose parameters ----------------
    // Robot at origin facing +Z; metres / degrees. Chosen by a search that scored per-mesh interpenetration
    // together with joint naturalness (waist twist, elbow and wrist angles, butt-to-shoulder contact); see
    // Artifacts/SentinelRifle/Hold/hold-verification.txt for the measured result.
    public struct PoseParams
    {
        public float twist;   // Spine yaw to the right (bladed stance)
        public float lean;    // Spine pitch forward
        public float protL, protR; // shoulder (clavicle) protraction, forward
        public float bx, by, bz;   // rifle butt-plate centre in the Chest frame (shoulder pocket)
        public float yaw, pitch, cant; // rifle orientation relative to the hips (+yaw right, +pitch muzzle down)
        public float wrap, rTilt, rPole; // right hand rotation about the grip, tilt, elbow swing (+ out)
        public float lSlide, lRoll, lFing, lPole; // support hand: slide along handguard from Support_Hand_Socket, roll around it (+ = left side), finger heading, elbow swing
        public float headP;   // head pitch (looks down along the rifle)
        public float follow;  // strike only: 0..1, how rigidly the rifle and elbow poles follow the chest away from the Hold torso pose

        /// <summary>A strike key: torso/rifle values plus the arm settings re-solved for that torso pose.</summary>
        /// <summary>A strike key: the upper body (with rifle and arms locked to it) twisted/leaned to the given torso pose.</summary>
        public PoseParams Swing(float twist, float lean, float headP, float dBz = 0f)
        { var p = this; p.twist = twist; p.lean = lean; p.headP = headP; p.bz += dBz; p.follow = 1f; return p; }

        public PoseParams Key(float twist, float lean, float pitch, float headP, float protL, float protR, float wrap, float rTilt, float rPole,
            float lSlide, float lRoll, float lFing, float lPole)
        {
            var p = this;
            p.twist = twist; p.lean = lean; p.pitch = pitch; p.headP = headP; p.protL = protL; p.protR = protR; p.wrap = wrap; p.rTilt = rTilt; p.rPole = rPole;
            p.lSlide = lSlide; p.lRoll = lRoll; p.lFing = lFing; p.lPole = lPole;
            return p;
        }

        public static PoseParams Lerp(PoseParams a, PoseParams b, float t)
        {
            float L(float x, float y) => Mathf.Lerp(x, y, t);
            return new PoseParams
            {
                twist = L(a.twist, b.twist), lean = L(a.lean, b.lean), protL = L(a.protL, b.protL), protR = L(a.protR, b.protR),
                bx = L(a.bx, b.bx), by = L(a.by, b.by), bz = L(a.bz, b.bz), yaw = L(a.yaw, b.yaw), pitch = L(a.pitch, b.pitch), cant = L(a.cant, b.cant),
                wrap = L(a.wrap, b.wrap), rTilt = L(a.rTilt, b.rTilt), rPole = L(a.rPole, b.rPole),
                lSlide = L(a.lSlide, b.lSlide), lRoll = L(a.lRoll, b.lRoll), lFing = L(a.lFing, b.lFing), lPole = L(a.lPole, b.lPole), headP = L(a.headP, b.headP), follow = L(a.follow, b.follow),
            };
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "twist {0:F1}, lean {1:F1}, shoulder protraction L {2:F1} R {3:F1}, butt ({4:F3}, {5:F3}, {6:F3}) in Chest frame, rifle yaw {7:F1} pitch {8:F1} cant {9:F1}, right wrap {10:F1} tilt {11:F1} elbow {12:F1}, support slide {13:F3} roll {14:F1} fingers {15:F1} elbow {16:F1}, head pitch {17:F1}",
            twist, lean, protL, protR, bx, by, bz, yaw, pitch, cant, wrap, rTilt, rPole, lSlide, lRoll, lFing, lPole, headP);
    }

    public static readonly PoseParams Hold = new PoseParams
    {
        twist = 25.0f, lean = 4.8f, protL = 28.5f, protR = 8.3f, bx = 0.177f, by = 0.437f, bz = 0.316f,
        yaw = -6.0f, pitch = 12.0f, cant = 0.0f, wrap = -28.2f, rTilt = -8.8f, rPole = 62.7f,
        lSlide = -0.200f, lRoll = 66.6f, lFing = 7.1f, lPole = -64.7f, headP = 4.9f,
    };

    // Rifle strike (option B): a swinging muzzle smash driven by the upper body. The rifle and both arms stay locked
    // in the Hold geometry relative to the chest (moving the rifle relative to the chest was measured to push the
    // right forearm into the stock and put the support hand out of reach), so the strike adds no arm/rifle clipping.
    // Coil right and back with the muzzle rising, swing down-left through the target with the impact on the existing
    // damage event (0.5 s), follow through, recover. The head stays on the target throughout.
    private static readonly PoseParams HoldF = Hold.Swing(Hold.twist, Hold.lean, Hold.headP);
    private static readonly (float t, PoseParams p)[] StrikeKeys =
    {
        (0.00f, HoldF),
        (0.14f, Hold.Swing(twist: 30f, lean: 1f, headP: 3f)),
        (0.28f, Hold.Swing(twist: 35f, lean: -4f, headP: 2f)),  // coiled: muzzle up and to the right
        (0.40f, Hold.Swing(twist: 29f, lean: 7f, headP: 5f)),
        (AttackEventTime, Hold.Swing(twist: 19f, lean: 17f, headP: 9f)), // impact
        (0.62f, Hold.Swing(twist: 16f, lean: 16f, headP: 9f)), // follow-through
        (0.78f, Hold.Swing(twist: 21f, lean: 10f, headP: 7f)),
        (0.95f, HoldF),
        (1.00f, HoldF),
    };
    private const float StrikeLength = 1.0f;
    private const int StrikeFps = 30;

    // Hand-local contact points (hand bone axes, metres). The hands have no finger bones: fingers are straight,
    // rigid geometry, so the grip passes through the finger base (no true finger curl).
    public static readonly Vector3 RightGripPoint = new Vector3(-0.03f, 0.17f, -0.01f); // palm faces -X
    public static readonly Vector3 LeftPalmPoint = new Vector3(0.08f, 0.10f, -0.01f);   // palm faces +X
    private static readonly Vector3 HandguardCentre = new Vector3(0f, 0.194f, 0f);     // rifle frame (x, y); z from the socket
    private const float HandguardHalfW = 0.052f, HandguardHalfH = 0.082f;

    [MenuItem("Tools/Halazia/Sentinel Rifle/Build Armed Sentinel (Hold + Rifle Strike)")]
    public static void BuildFromMenu() => Execute(build: true, strike: true);

    [MenuItem("Tools/Halazia/Sentinel Rifle/Build Armed Sentinel (Hold Only)")]
    public static void BuildHoldOnlyFromMenu() => Execute(build: true, strike: false);

    [MenuItem("Tools/Halazia/Sentinel Rifle/Verify Armed Sentinel Prefab")]
    public static void VerifyFromMenu() => Execute(build: false, strike: DetectStrike());

    /// <summary>Batch: -executeMethod SentinelRifleMount.Run [-verifyOnly] [-holdOnly]</summary>
    public static void Run()
    {
        int code = 1;
        var args = Environment.GetCommandLineArgs();
        try { bool build = !args.Contains("-verifyOnly"); code = Execute(build, build ? !args.Contains("-holdOnly") : DetectStrike()) ? 0 : 2; }
        catch (Exception e) { Debug.LogException(e); }
        EditorApplication.Exit(code);
    }

    /// <summary>True when the controller's RifleHold layer plays the rifle strike for Attack (option B).</summary>
    private static bool DetectStrike()
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var s = AssetDatabase.LoadAssetAtPath<AnimationClip>(StrikeClipPath);
        if (c == null || s == null || c.layers.Length < 2) return false;
        var attack = c.layers[0].stateMachine.states.Select(x => x.state).FirstOrDefault(x => x.name.EndsWith("|Attack", StringComparison.Ordinal));
        return attack != null && c.layers[1].GetOverrideMotion(attack) == s;
    }

    private static bool Execute(bool build, bool strike)
    {
        var log = new StringBuilder();
        log.AppendLine($"Sentinel two-handed rifle hold {(build ? "build + verify" : "verify")}, option {(strike ? "B (hold + rifle strike for Attack)" : "A (hold only; original Attack timing and chest motion)")} ({DateTime.Now:yyyy-MM-dd HH:mm})");
        OutDir = $"{BaseOutDir}/{(strike ? "OptionB_HoldAndStrike" : "OptionA_HoldOnly")}";
        Directory.CreateDirectory(OutDir);
        bool pass = false;
        try
        {
            if (build) Build(log, strike);
            pass = Verify(log);
        }
        catch (Exception e) { log.AppendLine("EXCEPTION: " + e); Debug.LogException(e); }
        log.AppendLine(pass ? "SENTINEL_RIFLE_HOLD_PASS" : "SENTINEL_RIFLE_HOLD_FAIL");
        File.WriteAllText(Path.Combine(OutDir, "hold-verification.txt"), log.ToString());
        Debug.Log(log.ToString());
        return pass;
    }

    private static T Load<T>(string path) where T : Object =>
        AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset: " + path);

    private static Dictionary<string, AnimationClip> LoadClips() =>
        AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToDictionary(c => c.name.Substring(c.name.IndexOf('|') + 1), c => c);

    private static Transform Find(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
        ?? throw new InvalidOperationException("Transform not found: " + name);

    private static string PathFrom(Transform root, Transform t)
    {
        var s = new StringBuilder(t.name);
        for (var p = t.parent; p != null && p != root; p = p.parent) s.Insert(0, p.name + "/");
        return s.ToString();
    }

    private static Vector3 RifleLocal(GameObject rifle, string socket) => rifle.transform.InverseTransformPoint(Find(rifle.transform, socket).position);

    // ---------------- pose solving ----------------

    public sealed class PoseInfo
    {
        public Vector3 rifleRoot; public Quaternion rifleRot; public bool ok;
        public float rReach, lReach, rElbow, lElbow, rSwing, lSwing;
    }

    /// <summary>The Sentinel skeleton plus the rifle-frame data the solver needs.</summary>
    public sealed class Rig
    {
        public readonly Transform root, spine, neck, chest, lSh, rSh, rU, rL, rH, lU, lL, lH;
        public readonly Transform[] bones;
        private readonly Quaternion[] bindLocal;
        private readonly Quaternion neckBindWorldRel, chestBindWorldRel;
        public readonly Vector3 gripLocal, supportLocal, buttLocal, gripAxisLocal;

        public Rig(GameObject robot, GameObject rifleAsset)
        {
            root = robot.transform;
            bones = HoldBones.Select(n => Find(root, n)).ToArray();
            spine = bones[0]; neck = bones[1]; lSh = bones[2]; rSh = bones[3]; rU = bones[4]; rL = bones[5]; rH = bones[6]; lU = bones[7]; lL = bones[8]; lH = bones[9];
            chest = Find(root, "Chest");
            bindLocal = bones.Select(b => b.localRotation).ToArray();
            neckBindWorldRel = Quaternion.Inverse(root.rotation) * neck.rotation;
            chestBindWorldRel = Quaternion.Inverse(root.rotation) * chest.rotation;
            gripLocal = RifleLocal(rifleAsset, "Grip_Socket");
            supportLocal = RifleLocal(rifleAsset, "Support_Hand_Socket");
            var all = new List<Vector3>();
            Vector3 axis = Vector3.up;
            foreach (var mf in rifleAsset.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = rifleAsset.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToList();
                all.AddRange(vs);
                if (mf.name == "GripCore") axis = PrincipalAxis(vs);
            }
            gripAxisLocal = axis.y < 0 ? -axis : axis;
            float minZ = all.Min(v => v.z);
            var butt = all.Where(v => v.z < minZ + 0.02f).ToList();
            buttLocal = new Vector3(0f, (butt.Min(v => v.y) + butt.Max(v => v.y)) * 0.5f, minZ);
        }

        public void ResetHoldBones() { for (int i = 0; i < bones.Length; i++) bones[i].localRotation = bindLocal[i]; }

        /// <summary>
        /// Poses the hold bones. The Chest keeps whatever local rotation the base clip gave it unless
        /// <paramref name="ownChest"/> is set, in which case the Spine is solved so the Chest ends up exactly where the
        /// parameters say (used by the strike so the melee clip's chest twist does not steer the rifle).
        /// </summary>
        public PoseInfo Apply(PoseParams p, bool ownChest)
        {
            ResetHoldBones();
            Quaternion rr = root.rotation;
            Vector3 up = root.up, right = root.right, fwd = root.forward;
            var bodyTurn = Quaternion.AngleAxis(p.twist, up) * Quaternion.AngleAxis(p.lean, right);
            if (ownChest) spine.rotation = bodyTurn * rr * chestBindWorldRel * Quaternion.Inverse(chest.localRotation);
            else spine.rotation = bodyTurn * spine.rotation;
            neck.rotation = Quaternion.AngleAxis(p.yaw, up) * Quaternion.AngleAxis(p.headP, right) * rr * neckBindWorldRel;
            Vector3 cu = chest.up;
            lSh.rotation = Quaternion.AngleAxis(p.protL, cu) * lSh.rotation;
            rSh.rotation = Quaternion.AngleAxis(-p.protR, cu) * rSh.rotation;

            // follow > 0: the rifle (and the elbow pole frame) turns with the chest by its rotation away from the Hold torso
            // pose, so the arms keep the Hold geometry relative to the upper body while the torso swings.
            var holdTurn = Quaternion.AngleAxis(Hold.twist, up) * Quaternion.AngleAxis(Hold.lean, right);
            var delta = Quaternion.Slerp(Quaternion.identity, bodyTurn * Quaternion.Inverse(holdTurn), p.follow);
            var R = delta * Quaternion.AngleAxis(p.yaw, up) * Quaternion.AngleAxis(p.pitch, right) * Quaternion.AngleAxis(p.cant, fwd) * rr;
            Vector3 butt = chest.position + chest.rotation * new Vector3(p.bx, p.by, p.bz);
            Vector3 rifleRoot = butt - R * buttLocal;
            Vector3 G = rifleRoot + R * gripLocal;
            Vector3 g = R * gripAxisLocal, rRight = R * Vector3.right, rFwd = R * Vector3.forward;

            // Right hand: grip runs through the fist along hand Z (thumb up), palm against the grip's right side.
            Vector3 rz = -g, rx = Quaternion.AngleAxis(p.wrap, g) * Vector3.ProjectOnPlane(rRight, g).normalized;
            var rRot = Quaternion.AngleAxis(p.rTilt, rx) * Quaternion.LookRotation(rz, Vector3.Cross(rz, rx));
            // Left hand: palm on the handguard surface at roll lRoll (0 = under it, + = round its left side).
            float ro = p.lRoll * Mathf.Deg2Rad;
            var o = new Vector3(-Mathf.Sin(ro), -Mathf.Cos(ro), 0f);
            var sLocal = new Vector3(HandguardCentre.x + HandguardHalfW * o.x, HandguardCentre.y + HandguardHalfH * o.y, supportLocal.z + p.lSlide);
            Vector3 S = rifleRoot + R * sLocal;
            Vector3 lx = R * -o;
            Vector3 ly = Quaternion.AngleAxis(p.lFing, lx) * Vector3.ProjectOnPlane(rFwd, lx).normalized;
            var lRot = Quaternion.LookRotation(Vector3.Cross(lx, ly), ly);

            var info = new PoseInfo { rifleRoot = rifleRoot, rifleRot = R };
            bool rOk = SolveArm(rU, rL, rH, G - rRot * RightGripPoint, rRot, p.rPole, +1, delta * right, delta * Vector3.down, out info.rElbow, out info.rReach);
            bool lOk = SolveArm(lU, lL, lH, S - lRot * LeftPalmPoint, lRot, p.lPole, -1, delta * right, delta * Vector3.down, out info.lElbow, out info.lReach);
            info.ok = rOk && lOk;
            info.rSwing = Vector3.Angle(rL.up, rH.up);
            info.lSwing = Vector3.Angle(lL.up, lH.up);
            return info;
        }
    }

    // Two-bone IK that keeps the elbow a pure hinge about local X (the axis the existing clips bend on), flexing
    // the natural way (negative about X). poleDeg swings the elbow from straight down toward the outside.
    private static bool SolveArm(Transform upper, Transform lower, Transform hand, Vector3 wrist, Quaternion handRot, float poleDeg, int side, Vector3 bodyRight, Vector3 downRef,
        out float elbowDeg, out float reach)
    {
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        Vector3 S = upper.position, d = wrist - S;
        float dist = d.magnitude;
        reach = dist / (a + b);
        bool ok = dist <= (a + b) * 0.995f && dist >= Mathf.Abs(a - b) + 0.02f;
        dist = Mathf.Clamp(dist, Mathf.Abs(a - b) + 0.02f, (a + b) * 0.995f);
        Vector3 dir = d.normalized;
        float cosA = Mathf.Clamp((a * a + dist * dist - b * b) / (2 * a * dist), -1, 1), sinA = Mathf.Sqrt(1 - cosA * cosA);
        Vector3 down = Vector3.ProjectOnPlane(downRef, dir);
        if (down.sqrMagnitude < 1e-4f) down = Vector3.ProjectOnPlane(Vector3.back, dir);
        down.Normalize();
        Vector3 outward = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(bodyRight * side, dir), down).normalized;
        Vector3 perp = (Mathf.Cos(poleDeg * Mathf.Deg2Rad) * down + Mathf.Sin(poleDeg * Mathf.Deg2Rad) * outward).normalized;
        Vector3 E = S + a * (cosA * dir + sinA * perp), W = S + dir * dist;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Vector3 n = Vector3.Cross(E - S, W - E).normalized * (attempt == 0 ? 1 : -1);
            Vector3 y1 = (E - S).normalized, y2 = (W - E).normalized;
            upper.rotation = Quaternion.LookRotation(Vector3.Cross(n, y1), y1);
            lower.rotation = Quaternion.LookRotation(Vector3.Cross(n, y2), y2);
            var q = lower.localRotation;
            float hinge = 2f * Mathf.Atan2(q.x, q.w) * Mathf.Rad2Deg;
            if (hinge > 180) hinge -= 360; if (hinge < -180) hinge += 360;
            if (hinge < 1f) break;
        }
        hand.rotation = handRot;
        elbowDeg = Vector3.Angle(E - S, W - E);
        return ok;
    }

    private static Vector3 PrincipalAxis(List<Vector3> pts)
    {
        Vector3 c = pts.Aggregate(Vector3.zero, (acc, p) => acc + p) / pts.Count;
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in pts) { var d = p - c; xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z; }
        Vector3 v = Vector3.up;
        for (int i = 0; i < 50; i++)
            v = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z).normalized;
        return v;
    }

    private static PoseParams StrikeAt(float t)
    {
        for (int i = 0; i < StrikeKeys.Length - 1; i++)
        {
            var (t0, p0) = StrikeKeys[i]; var (t1, p1) = StrikeKeys[i + 1];
            if (t > t1) continue;
            float u = Mathf.Clamp01((t - t0) / Mathf.Max(1e-4f, t1 - t0));
            return PoseParams.Lerp(p0, p1, u * u * (3f - 2f * u)); // ease in/out between keys
        }
        return StrikeKeys[StrikeKeys.Length - 1].p;
    }

    // ---------------- Build ----------------

    private static void Build(StringBuilder log, bool withStrike)
    {
        var model = Load<GameObject>(ModelPath);
        var rifleAsset = Load<GameObject>(RiflePath);
        var controller = Load<AnimatorController>(ControllerPath);
        Load<GameObject>(VariantPath);
        var clips = LoadClips();
        var attackEvents = AnimationUtility.GetAnimationEvents(clips["Attack"]);
        if (!attackEvents.Any(e => e.functionName == "AnimEvent_AttackHit" && Mathf.Abs(e.time - AttackEventTime) < 1e-3f))
            throw new InvalidOperationException("Attack clip's AnimEvent_AttackHit is not at " + AttackEventTime + " s; strike timing would not match.");
        log.AppendLine("BUILD");
        log.AppendLine("  Hold: " + Hold);

        var scene = EditorSceneManager.NewPreviewScene();
        string[] paths;
        Quaternion[] holdLocal;
        var strikeFrames = new List<Quaternion[]>();
        Vector3 mountPos, mountScale; Quaternion mountRot;
        try
        {
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var rig = new Rig(robot, rifleAsset);
            paths = rig.bones.Select(b => PathFrom(robot.transform, b)).ToArray();

            var info = rig.Apply(Hold, ownChest: false);
            if (!info.ok) throw new InvalidOperationException($"Hold unreachable (right reach {info.rReach:F2}, left {info.lReach:F2}).");
            holdLocal = rig.bones.Select(b => b.localRotation).ToArray();
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Hold solve: reach R {0:F2} L {1:F2}; elbow flex R {2:F0} L {3:F0} deg; wrist bend R {4:F0} L {5:F0} deg",
                info.rReach, info.lReach, info.rElbow, info.lElbow, info.rSwing, info.lSwing));
            Vector3 s = rig.rH.lossyScale;
            if (Mathf.Abs(s.x - s.y) > 0.01f * s.x || Mathf.Abs(s.x - s.z) > 0.01f * s.x) throw new InvalidOperationException("RightHand world scale is not uniform.");
            mountPos = rig.rH.InverseTransformPoint(info.rifleRoot);
            mountRot = Quaternion.Inverse(rig.rH.rotation) * info.rifleRot;
            mountScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);

            // Strike: sample the melee Attack clip frame by frame (so the Chest is where the base layer puts it), then solve.
            float worstR = 0, worstL = 0;
            for (int f = 0; withStrike && f <= Mathf.RoundToInt(StrikeLength * StrikeFps); f++)
            {
                float t = f / (float)StrikeFps;
                clips["Attack"].SampleAnimation(robot, t / StrikeLength * clips["Attack"].length);
                var fi = rig.Apply(StrikeAt(t / StrikeLength), ownChest: true);
                worstR = Mathf.Max(worstR, fi.rReach); worstL = Mathf.Max(worstL, fi.lReach);
                if (!fi.ok) throw new InvalidOperationException($"Strike frame {f} unreachable (right reach {fi.rReach:F2}, left {fi.lReach:F2}).");
                strikeFrames.Add(rig.bones.Select(b => b.localRotation).ToArray());
            }
            log.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Strike solve: {0} frames, max reach R {1:F2} L {2:F2}", strikeFrames.Count, worstR, worstL));
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        string[] props = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        AnimationClip WriteClip(string path, string name, bool loop, Func<int, int, float> value, int frames, float fps)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (created) clip = new AnimationClip { name = name };
            clip.ClearCurves();
            clip.frameRate = fps;
            for (int i = 0; i < paths.Length; i++)
                for (int k = 0; k < 4; k++)
                {
                    var curve = new AnimationCurve();
                    for (int f = 0; f < frames; f++) curve.AddKey(new Keyframe(f / fps, value(i * 4 + k, f)));
                    for (int f = 0; f < curve.length; f++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, f, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, f, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(paths[i], typeof(Transform), props[k]), curve);
                }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, path); else EditorUtility.SetDirty(clip);
            log.AppendLine($"  {(created ? "Created" : "Updated")} {path}: {paths.Length} bones x 4 rotation curves, {frames} keys, {(loop ? "looping" : "one-shot")}");
            return clip;
        }
        float Comp(Quaternion q, int k) => k == 0 ? q.x : k == 1 ? q.y : k == 2 ? q.z : q.w;

        // 1. Constant hold clip (two identical keys, 1 s).
        var hold = WriteClip(ClipPath, "Sentinel_RifleHold", true, (ik, f) => Comp(holdLocal[ik / 4], ik % 4), 2, 1f);
        // 2. Strike clip: quaternion signs made continuous so interpolation never takes the long way round.
        for (int f = 1; f < strikeFrames.Count; f++)
            for (int i = 0; i < paths.Length; i++)
                if (Quaternion.Dot(strikeFrames[f - 1][i], strikeFrames[f][i]) < 0f)
                { var q = strikeFrames[f][i]; strikeFrames[f][i] = new Quaternion(-q.x, -q.y, -q.z, -q.w); }
        var strike = !withStrike ? null : WriteClip(StrikeClipPath, "Sentinel_RifleStrike", false, (ik, f) => Comp(strikeFrames[f][ik / 4], ik % 4), strikeFrames.Count, StrikeFps);

        // 3. Generic avatar mask: every model transform listed, only the hold bones active.
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        bool newMask = mask == null;
        if (newMask) mask = new AvatarMask { name = "Sentinel_RifleHold" };
        mask.transformCount = 0;
        mask.AddTransformPath(model.transform, true);
        for (int i = 0; i < mask.transformCount; i++) mask.SetTransformActive(i, paths.Contains(mask.GetTransformPath(i)));
        if (newMask) AssetDatabase.CreateAsset(mask, MaskPath); else EditorUtility.SetDirty(mask);
        log.AppendLine($"  {(newMask ? "Created" : "Updated")} {MaskPath}: {mask.transformCount} paths, {Enumerable.Range(0, mask.transformCount).Count(mask.GetTransformActive)} active");

        // 4. Synced override layer appended to SentinelAnimator.controller (Base Layer untouched).
        var layers = controller.layers;
        int li = Array.FindIndex(layers, l => l.name == LayerName);
        if (li >= 0 && layers[li].syncedLayerIndex != 0)
            throw new InvalidOperationException($"Controller already has an unsynced '{LayerName}' layer; remove it before building.");
        if (li < 0)
        {
            controller.AddLayer(LayerName);
            layers = controller.layers;
            li = layers.Length - 1;
        }
        var layer = layers[li];
        layer.syncedLayerIndex = 0;
        layer.syncedLayerAffectsTiming = false; // synced motions are stretched to the Base Layer state's length
        layer.defaultWeight = 1f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = mask;
        layer.iKPass = false;
        foreach (var cs in layers[0].stateMachine.states)
            layer.SetOverrideMotion(cs.state, withStrike && cs.state.name.EndsWith("|Attack", StringComparison.Ordinal) ? strike : hold);
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
        log.AppendLine($"  Layer '{LayerName}' at index {li}: synced to Base Layer (timing from Base Layer), Override, weight 1, mask {mask.name}; Attack -> {(withStrike ? strike.name : hold.name)}, other states -> {hold.name}");

        // 5. Re-place the weapon mount in the armed prefab.
        var contents = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            var mount = Find(contents.transform, MountName);
            mount.localPosition = mountPos;
            mount.localRotation = mountRot;
            mount.localScale = mountScale;
            PrefabUtility.SaveAsPrefabAsset(contents, VariantPath, out bool saved);
            if (!saved) throw new InvalidOperationException("Failed to save " + VariantPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.SaveAssets();
        log.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Mount local: pos ({0:F6}, {1:F6}, {2:F6}) rot {3:F2} scale ({4:F6})",
            mountPos.x, mountPos.y, mountPos.z, mountRot.eulerAngles, mountScale.x));
    }

    // ---------------- Verify ----------------

    private sealed class Piece { public string part; public int[] verts, tris; public Mesh mesh; public MeshCollider col; public Bounds bounds; public SkinnedMeshRenderer smr; }

    private static AnimationClip OverrideFor(string clipName, AnimationClip hold, AnimationClip strike) => clipName == "Attack" ? strike : hold;

    private static bool Verify(StringBuilder log)
    {
        var fails = new List<string>();
        void Check(bool ok, string msg) { log.AppendLine((ok ? "  PASS " : "  FAIL ") + msg); if (!ok) fails.Add(msg); }
        var model = Load<GameObject>(ModelPath);
        var rifleAsset = Load<GameObject>(RiflePath);
        var controller = Load<AnimatorController>(ControllerPath);
        var variant = Load<GameObject>(VariantPath);
        var hold = Load<AnimationClip>(ClipPath);
        var strike = AssetDatabase.LoadAssetAtPath<AnimationClip>(StrikeClipPath);
        bool optB = DetectStrike();
        var attackOver = optB ? strike : hold;
        var mask = Load<AvatarMask>(MaskPath);
        var clips = LoadClips();

        log.AppendLine("CONTROLLER");
        var layers = controller.layers;
        Check(layers.Length == 2 && layers[1].name == LayerName, $"layers: {string.Join(", ", layers.Select(l => l.name))}");
        var baseStates = layers[0].stateMachine.states.Select(s => s.state).ToList();
        string[] expectedStates = ClipOrder.Select(n => "Sentinel_Rig|" + n).ToArray();
        Check(expectedStates.All(n => baseStates.Any(s => s.name == n && s.motion is AnimationClip c && AssetDatabase.GetAssetPath(c) == ModelPath)) &&
              baseStates.Count == 7 && layers[0].stateMachine.anyStateTransitions.Length == 3 && layers[0].avatarMask == null && layers[0].syncedLayerIndex == -1,
            "Base Layer intact: 7 states bound to Sentinel_Robot.fbx clips, 3 Any State transitions, no mask, not synced");
        Check(controller.parameters.Select(p => p.name).SequenceEqual(new[] { "Speed", "Attack", "Hit", "Die", "IsDead" }), "parameters unchanged (Speed, Attack, Hit, Die, IsDead)");
        var ev = AnimationUtility.GetAnimationEvents(clips["Attack"]).FirstOrDefault(e => e.functionName == "AnimEvent_AttackHit");
        Check(ev != null && Mathf.Abs(ev.time - AttackEventTime) < 1e-3f, $"Attack clip still carries AnimEvent_AttackHit at {AttackEventTime} s (damage timing unchanged)");
        Check(new[] { hold, attackOver }.All(x => AnimationUtility.GetAnimationEvents(x).Length == 0), "RifleHold layer clips carry no animation events");
        if (layers.Length > 1)
        {
            var l = layers[1];
            bool motionsOk = baseStates.All(s => l.GetOverrideMotion(s) == (s.name.EndsWith("|Attack") ? attackOver : hold));
            Check(l.syncedLayerIndex == 0 && !l.syncedLayerAffectsTiming && l.defaultWeight == 1f && l.blendingMode == AnimatorLayerBlendingMode.Override &&
                  l.avatarMask == mask && !l.iKPass && motionsOk,
                "RifleHold layer: synced to Base Layer (Base Layer timing), Override, weight 1, Sentinel_RifleHold mask; " + (optB ? "Attack -> Sentinel_RifleStrike, other 6 states -> Sentinel_RifleHold" : "all 7 states -> Sentinel_RifleHold"));
        }
        var activePaths = Enumerable.Range(0, mask.transformCount).Where(mask.GetTransformActive).Select(mask.GetTransformPath).OrderBy(x => x).ToList();
        foreach (var c in new[] { hold, attackOver }.Distinct())
        {
            var bindings = AnimationUtility.GetCurveBindings(c);
            Check(bindings.Length == HoldBones.Length * 4 && bindings.All(b => b.propertyName.StartsWith("m_LocalRotation")) &&
                  bindings.Select(b => b.path).Distinct().OrderBy(x => x).SequenceEqual(activePaths) && activePaths.Count == HoldBones.Length,
                $"{c.name} animates only local rotations of the {HoldBones.Length} masked bones ({string.Join(", ", activePaths.Select(p => p.Split('/').Last()))})");
        }
        Check((!optB || (Mathf.Abs(strike.length - StrikeLength) < 1e-3f && !strike.isLooping)) && hold.isLooping, optB ? $"strike clip {strike.length:F2} s one-shot; hold clip looping" : "hold clip looping");

        log.AppendLine("PREFAB");
        var scene = EditorSceneManager.NewPreviewScene();
        var pieces = new List<Piece>();
        var boxes = new List<(BoxCollider box, string group, Renderer r, string part)>();
        try
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(variant, scene);
            var animator = root.GetComponent<Animator>();
            Check(PrefabUtility.GetPrefabAssetType(variant) == PrefabAssetType.Variant && PrefabUtility.GetCorrespondingObjectFromSource(variant) == model, "variant of Sentinel_Robot.fbx");
            Check(animator != null && animator.runtimeAnimatorController == controller && !animator.applyRootMotion, "Animator: SentinelAnimator, root motion off");
            var rH = Find(root.transform, "RightHand"); var lH = Find(root.transform, "LeftHand");
            var mount = rH.Find(MountName);
            Check(mount != null && mount.childCount == 1, "RightHand_WeaponMount directly under RightHand with one child");
            var rifle = mount.GetChild(0).gameObject;
            Check(PrefabUtility.GetCorrespondingObjectFromOriginalSource(rifle) == rifleAsset && rifle.transform.localPosition == Vector3.zero &&
                  rifle.transform.localRotation == Quaternion.identity && rifle.transform.localScale == Vector3.one, "rifle: nested Sentinel_Rifle at identity (grip pivot = mount)");
            foreach (var n in SocketNames)
            {
                var a = Find(rifleAsset.transform, n); var b = Find(rifle.transform, n);
                Check((a.localPosition - b.localPosition).sqrMagnitude < 1e-12f && Quaternion.Angle(a.localRotation, b.localRotation) < 0.001f, "socket preserved: " + n);
            }
            var contents = PrefabUtility.LoadPrefabContents(VariantPath);
            try
            {
                var saved = Find(contents.transform, MountName).GetChild(0).gameObject;
                var mods = PrefabUtility.GetPropertyModifications(saved) ?? Array.Empty<PropertyModification>();
                Check(mods.All(m => m.target == null || m.target == rifleAsset.transform || (m.target == rifleAsset && m.propertyPath == "m_Name")) &&
                      PrefabUtility.GetAddedComponents(saved).Count == 0 && PrefabUtility.GetAddedGameObjects(saved).Count == 0,
                    "saved asset: no overrides or additions inside the rifle");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Check(root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 && rifle.GetComponentsInChildren<Collider>(true).Length == 0, "no scripts or colliders added");

            // Collision proxies: rifle part boxes and per-piece convex hulls of the robot meshes.
            foreach (var f in rifle.GetComponentsInChildren<MeshFilter>(true))
            {
                var bc = f.gameObject.AddComponent<BoxCollider>();
                bc.center = f.sharedMesh.bounds.center; bc.size = Vector3.Max(f.sharedMesh.bounds.size, Vector3.one * 0.002f);
                boxes.Add((bc, f.transform.parent != null ? f.transform.parent.name : "", f.GetComponent<Renderer>(), f.name));
            }
            var handguard = boxes.Where(b => b.group == "Foreend" && (b.part == "HandguardCore" || b.part == "LowerArmor")).Select(b => b.box).ToList();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) pieces.AddRange(Split(smr, root.transform));

            log.AppendLine("ANIMATION (each Base Layer clip sampled 25x, then the RifleHold layer's motion for that state at the same normalized time, as the synced layer plays it: weight 1, masked bones only)");
            var grip = Find(rifle.transform, "Grip_Socket"); var support = Find(rifle.transform, "Support_Hand_Socket"); var muzzle = Find(rifle.transform, "Muzzle_Point");
            var holdBones = HoldBones.Select(n => Find(root.transform, n)).ToArray();
            Cook(pieces);
            var baseline = ArmDepths(pieces);
            foreach (var name in ClipOrder)
            {
                var c = clips[name];
                var over = OverrideFor(name, hold, attackOver);
                float minS = 9, maxS = 0, maxGrip = 0, maxPalm = 0, minSock = 9, maxSock = 0, minYaw = 999, maxYaw = -999, minPitch = 999, maxPitch = -999, rifleMinY = 9, bodyMinY = 9, baseMinY = 9, maxStep = 0;
                var deathLine = new List<string>();
                Vector3 muzzle0 = Vector3.zero, muzzleHit = Vector3.zero;
                var depth = new Dictionary<string, float>(); var depthT = new Dictionary<string, float>();
                Quaternion[] prev = null;
                int steps = name == "Attack" ? 30 : 24;
                for (int i = 0; i <= steps; i++)
                {
                    float u = i / (float)steps;
                    c.SampleAnimation(root, c.length * u);
                    // Original look first (Base Layer clip only: the hold bones get the clip's own values), for the floor measurement.
                    Cook(pieces);
                    float stepBaseMin = 9; string stepBasePart = "";
                    foreach (var p in pieces) { float y = p.bounds.min.y - root.transform.position.y; if (y < stepBaseMin) { stepBaseMin = y; stepBasePart = p.part.Replace("Sentinel_", ""); } }
                    baseMinY = Mathf.Min(baseMinY, stepBaseMin);
                    over.SampleAnimation(root, over.length * u);
                    var now = holdBones.Select(b => b.rotation).ToArray();
                    if (prev != null) for (int k = 0; k < now.Length; k++) maxStep = Mathf.Max(maxStep, Quaternion.Angle(prev[k], now[k]) * steps / (c.length * 30f));
                    prev = now;
                    Vector3 ls = rifle.transform.lossyScale;
                    minS = Mathf.Min(minS, ls.x, ls.y, ls.z); maxS = Mathf.Max(maxS, ls.x, ls.y, ls.z);
                    // Hand-local points are in hand bone units (the rig is scaled x100), hence * 0.01.
                    maxGrip = Mathf.Max(maxGrip, Vector3.Distance(rH.TransformPoint(RightGripPoint * 0.01f), grip.position));
                    Vector3 palm = lH.TransformPoint(LeftPalmPoint * 0.01f);
                    float pd = handguard.Min(b => Vector3.Distance(palm, Physics.ClosestPoint(palm, b, b.transform.position, b.transform.rotation)));
                    maxPalm = Mathf.Max(maxPalm, pd);
                    float sd = Vector3.Distance(palm, support.position); minSock = Mathf.Min(minSock, sd); maxSock = Mathf.Max(maxSock, sd);
                    Vector3 f = root.transform.InverseTransformDirection(muzzle.forward);
                    float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1, 1)) * Mathf.Rad2Deg;
                    minYaw = Mathf.Min(minYaw, yaw); maxYaw = Mathf.Max(maxYaw, yaw); minPitch = Mathf.Min(minPitch, pitch); maxPitch = Mathf.Max(maxPitch, pitch);
                    if (i == 0) muzzle0 = root.transform.InverseTransformPoint(muzzle.position);
                    if (Mathf.Abs(u * c.length - AttackEventTime) < 0.5f / steps * c.length) muzzleHit = root.transform.InverseTransformPoint(muzzle.position);
                    foreach (var bx in boxes) rifleMinY = Mathf.Min(rifleMinY, bx.r.bounds.min.y - root.transform.position.y);
                    Cook(pieces);
                    float layerMin = 9; string layerPart = ""; foreach (var p in pieces) { float y = p.bounds.min.y - root.transform.position.y; if (y < layerMin) { layerMin = y; layerPart = p.part.Replace("Sentinel_", ""); } }
                    bodyMinY = Mathf.Min(bodyMinY, layerMin);
                    float rMin = boxes.Min(bx => bx.r.bounds.min.y) - root.transform.position.y;
                    if (name == "Death" && i % 3 == 0) deathLine.Add(string.Format(CultureInfo.InvariantCulture, "t {0:F2} s: {1:F2} m ({2}) | {3:F2} m ({4}) | {5:F2} m", u * c.length, stepBaseMin, stepBasePart, layerMin, layerPart, rMin));
                    foreach (var bx in boxes)
                    {
                        var rb = bx.r.bounds;
                        foreach (var p in pieces)
                        {
                            if (!rb.Intersects(p.bounds) || !Overlap(bx.box, p.col, out float d)) continue;
                            if (p.part == "Sentinel_RightHand" && (bx.group == "Grip" || bx.group == "Controls")) continue; // the grip passes through the fist
                            string key = p.part + " <-> rifle " + bx.group;
                            if (!depth.TryGetValue(key, out float o) || d > o) { depth[key] = d; depthT[key] = u * c.length; }
                        }
                    }
                    foreach (var kv in ArmDepths(pieces))
                    {
                        float extra = kv.Value - (baseline.TryGetValue(kv.Key, out float b0) ? b0 : 0f);
                        string key = kv.Key + " (beyond bind-pose overlap)";
                        if (extra > 0.005f && (!depth.TryGetValue(key, out float o) || extra > o)) { depth[key] = extra; depthT[key] = u * c.length; }
                    }
                }
                bool sOk = minS > 0.99f && maxS < 1.01f;
                bool cOk = maxGrip < 0.01f && maxPalm < 0.025f;
                // Visible clipping budget: the support palm rests on the handguard (contact up to 3 cm into the box proxy);
                // anything else must stay under 2 cm.
                float worst = depth.Where(kv => !(kv.Key.StartsWith("Sentinel_LeftHand <-> rifle Foreend"))).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
                float palmIn = depth.TryGetValue("Sentinel_LeftHand <-> rifle Foreend", out float pi) ? pi : 0f;
                bool clipOk = worst < 0.02f && palmIn < 0.03f;
                Check(sOk && cOk && clipOk && maxStep < 20f,
                    $"{name}: rifle scale {minS:F3}-{maxS:F3}; right grip point to Grip_Socket max {maxGrip * 100f:F1} cm; left palm to handguard surface max {maxPalm * 100f:F1} cm; " +
                    $"largest interpenetration {worst * 100f:F1} cm (support palm into handguard {palmIn * 100f:F1} cm); fastest hold-bone rotation {maxStep:F1} deg/frame");
                log.AppendLine($"    {name}: muzzle yaw {minYaw:F0}..{maxYaw:F0} deg, pitch {minPitch:F0}..{maxPitch:F0} deg (+ = down); left palm {minSock * 100f:F0}-{maxSock * 100f:F0} cm from Support_Hand_Socket; rifle lowest {rifleMinY:F2} m; body lowest {bodyMinY:F2} m with the layer, {baseMinY:F2} m with the original clip alone (floor = 0)");
                if (name == "Death") log.AppendLine("    Death sinking timeline, lowest point below/above floor (original clip alone | with rifle layer | rifle): " + string.Join("; ", deathLine));
                if (name == "Attack")
                {
                    Vector3 dm = muzzleHit - muzzle0;
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture, "    Attack: at the damage event (t = {0:F2} s) the muzzle has moved {1:F2} m forward, {2:F2} m down from the ready position", AttackEventTime, dm.z, -dm.y));
                }
                foreach (var kv in depth.Where(kv => kv.Value >= 0.005f).OrderByDescending(kv => kv.Value).Take(8))
                    log.AppendLine($"    interpenetration {kv.Key}: {kv.Value * 100f:F1} cm (worst at t = {depthT[kv.Key]:F2} s)");
                if (!depth.Any(kv => kv.Value >= 0.005f)) log.AppendLine("    no interpenetration >= 0.5 cm");
            }
        }
        finally
        {
            foreach (var p in pieces) { Object.DestroyImmediate(p.mesh); if (p.col != null) Object.DestroyImmediate(p.col.gameObject); }
            EditorSceneManager.ClosePreviewScene(scene);
        }

        log.AppendLine("RENDERS");
        var idle = clips["Idle"];
        string Rel(string f) => f.Replace('\\', '/');
        string full = Path.Combine(OutDir, "pose_8views.png");
        RenderSheet(variant, idle, hold, new[] { 0f }, AllViews, new Vector3(0.1f, 1.75f, 0.3f), 4.6f, full, 4);
        log.AppendLine($"  {Rel(full)}: Idle + hold; front, front-right 45, right, back-right 45 / back, left, front-left 45, top");
        string close = Path.Combine(OutDir, "pose_8views_close.png");
        RenderSheet(variant, idle, hold, new[] { 0f }, AllViews, new Vector3(0.22f, 1.85f, 0.85f), 2.4f, close, 4);
        log.AppendLine($"  {Rel(close)}: same views, close on the hands");
        if (optB)
            foreach (var (label, at) in new[] { ("coil", 0.28f), ("impact", AttackEventTime), ("followthrough", 0.62f) })
            {
                string sf = Path.Combine(OutDir, $"strike_{label}_8views.png");
                RenderSheet(variant, clips["Attack"], strike, new[] { at * clips["Attack"].length }, AllViews, new Vector3(0.1f, 1.75f, 0.45f), 4.8f, sf, 4);
                log.AppendLine($"  {Rel(sf)}: Attack at t = {at:F2} s ({label}), same 8 views");
            }
        foreach (var name in ClipOrder)
        {
            var c = clips[name];
            var times = name == "Attack" ? new[] { 0f, 0.28f, 0.4f, 0.5f, 0.62f, 0.9f }.Select(x => x * c.length).ToArray()
                : c.isLooping ? new[] { 0f, c.length / 3f, c.length * 2f / 3f } : new[] { 0f, c.length * 0.5f, c.length };
            string file = Path.Combine(OutDir, $"state_{name}.png");
            RenderSheet(variant, c, OverrideFor(name, hold, attackOver), times, StateViews, new Vector3(0.1f, 1.35f, 0.35f), 6.5f, file, times.Length);
            log.AppendLine($"  {Rel(file)}: columns t = {string.Join(", ", times.Select(t => t.ToString("F2", CultureInfo.InvariantCulture)))} s; rows = front, front-right 45, right side, back");
        }
        log.AppendLine(fails.Count == 0 ? "RESULT: all checks passed." : "RESULT: FAILED: " + string.Join("; ", fails));
        return fails.Count == 0;
    }

    private static readonly Vector3[] AllViews = {
        new Vector3(0, 0.12f, 1), new Vector3(0.75f, 0.15f, 0.75f), new Vector3(1, 0.08f, 0), new Vector3(0.75f, 0.15f, -0.75f),
        new Vector3(0, 0.12f, -1), new Vector3(-1, 0.08f, 0), new Vector3(-0.75f, 0.15f, 0.75f), new Vector3(0.1f, 1, 0.35f) };
    private static readonly Vector3[] StateViews = { new Vector3(0, 0.12f, 1), new Vector3(0.75f, 0.15f, 0.75f), new Vector3(1, 0.08f, 0), new Vector3(0, 0.12f, -1) };

    private static IEnumerable<Piece> Split(SkinnedMeshRenderer smr, Transform parent)
    {
        var mesh = smr.sharedMesh; var verts = mesh.vertices; var tris = mesh.triangles;
        var weld = new Dictionary<Vector3Int, int>(); var id = new int[verts.Length];
        for (int v = 0; v < verts.Length; v++)
        {
            var key = Vector3Int.RoundToInt(verts[v] * 10000f);
            if (!weld.TryGetValue(key, out int w)) { w = weld.Count; weld[key] = w; }
            id[v] = w;
        }
        var par = Enumerable.Range(0, weld.Count).ToArray();
        int Root(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
        for (int t = 0; t < tris.Length; t += 3) { int a = Root(id[tris[t]]); par[Root(id[tris[t + 1]])] = a; par[Root(id[tris[t + 2]])] = a; }
        foreach (var group in Enumerable.Range(0, verts.Length).GroupBy(v => Root(id[v])))
        {
            var vs = group.ToArray();
            var local = new Dictionary<int, int>();
            for (int k = 0; k < vs.Length; k++) local[vs[k]] = k;
            var pt = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
                if (local.ContainsKey(tris[t]) && local.ContainsKey(tris[t + 1]) && local.ContainsKey(tris[t + 2]))
                { pt.Add(local[tris[t]]); pt.Add(local[tris[t + 1]]); pt.Add(local[tris[t + 2]]); }
            if (pt.Count < 12) continue;
            var go = EditorUtility.CreateGameObjectWithHideFlags("Piece", HideFlags.HideAndDontSave, typeof(MeshCollider));
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<MeshCollider>();
            col.convex = true;
            yield return new Piece { part = smr.name, smr = smr, verts = vs, tris = pt.ToArray(), mesh = new Mesh(), col = col };
        }
    }

    private static readonly List<Vector3> _v = new List<Vector3>(), _pv = new List<Vector3>();
    private static void Cook(List<Piece> pieces)
    {
        var baked = new Mesh();
        try
        {
            foreach (var g in pieces.GroupBy(p => p.smr))
            {
                g.Key.BakeMesh(baked, true);
                baked.GetVertices(_v);
                var m = g.Key.transform.localToWorldMatrix;
                for (int k = 0; k < _v.Count; k++) _v[k] = m.MultiplyPoint3x4(_v[k]);
                foreach (var p in g)
                {
                    _pv.Clear(); foreach (int v in p.verts) _pv.Add(_v[v]);
                    p.mesh.Clear(); p.mesh.SetVertices(_pv); p.mesh.SetTriangles(p.tris, 0); p.mesh.RecalculateBounds(); p.bounds = p.mesh.bounds;
                    p.col.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    p.col.sharedMesh = null; p.col.sharedMesh = p.mesh;
                }
            }
        }
        finally { Object.DestroyImmediate(baked); }
    }

    private static bool Overlap(Collider a, Collider b, out float d) =>
        Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation, out _, out d) && d > 0f;

    private static readonly string[] ArmParts = { "Sentinel_RightUpperArm", "Sentinel_RightLowerArm", "Sentinel_RightHand", "Sentinel_LeftUpperArm", "Sentinel_LeftLowerArm", "Sentinel_LeftHand" };
    private static readonly string[] TorsoParts = { "Sentinel_Chest", "Sentinel_Spine", "Sentinel_Hips", "Sentinel_Neck", "Sentinel_Head", "Sentinel_LeftUpperLeg", "Sentinel_RightUpperLeg" };

    private static Dictionary<string, float> ArmDepths(List<Piece> pieces)
    {
        var res = new Dictionary<string, float>();
        foreach (var a in pieces.Where(p => ArmParts.Contains(p.part)))
            foreach (var t in pieces)
            {
                bool otherArm = ArmParts.Contains(t.part) && t.part.Contains("Left") != a.part.Contains("Left");
                if (!(TorsoParts.Contains(t.part) || otherArm) || !a.bounds.Intersects(t.bounds) || !Overlap(a.col, t.col, out float d)) continue;
                string key = a.part + " <-> " + t.part;
                res[key] = Mathf.Max(res.TryGetValue(key, out float o) ? o : 0f, d);
            }
        return res;
    }

    private static void RenderSheet(GameObject variant, AnimationClip baseClip, AnimationClip over, float[] times, Vector3[] views, Vector3 focus, float dist, string file, int cols)
    {
        const int tile = 400;
        int n = times.Length * views.Length;
        if (times.Length > 1) cols = times.Length; // one row per view, one column per time
        int rows = (n + cols - 1) / cols;
        var sheet = new Texture2D(tile * cols, tile * rows, TextureFormat.RGBA32, false);
        var preview = new PreviewRenderUtility();
        var tmp = EditorSceneManager.NewPreviewScene();
        var floor = new Material(Shader.Find("Standard")) { color = new Color(0.33f, 0.35f, 0.38f) };
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(variant, tmp);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            preview.AddSingleGO(inst);
            var cam = preview.camera;
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.055f, 0.065f, 0.085f);
            int idx = 0;
            for (int v = 0; v < views.Length; v++)
                for (int k = 0; k < times.Length; k++, idx++)
                {
                    baseClip.SampleAnimation(inst, times[k]);
                    over.SampleAnimation(inst, times[k] / baseClip.length * over.length);
                    cam.transform.position = focus + views[v].normalized * dist;
                    if (views[v].y > 0.9f) cam.transform.LookAt(focus, Vector3.forward); else cam.transform.LookAt(focus);
                    preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = cam.transform.rotation * Quaternion.Euler(25f, -30f, 0f);
                    preview.lights[1].intensity = 0.9f; preview.lights[1].transform.rotation = cam.transform.rotation * Quaternion.Euler(-15f, 40f, 0f);
                    preview.ambientColor = new Color(0.32f, 0.34f, 0.38f);
                    preview.BeginStaticPreview(new Rect(0, 0, tile, tile));
                    preview.DrawMesh(quad, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one * 8f), floor, 0);
                    preview.Render();
                    var tex = preview.EndStaticPreview();
                    int w = Mathf.Min(tile, tex.width), h = Mathf.Min(tile, tex.height);
                    sheet.SetPixels((idx % cols) * tile, (rows - 1 - idx / cols) * tile, w, h, tex.GetPixels(0, 0, w, h));
                    Object.DestroyImmediate(tex);
                }
            File.WriteAllBytes(file, sheet.EncodeToPNG());
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = async;
            preview.Cleanup();
            EditorSceneManager.ClosePreviewScene(tmp);
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(floor);
        }
    }
}
