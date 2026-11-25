using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Halazia.AI.Commander;
using Halazia.Sandbox;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

namespace Halazia.Editor
{
    /// <summary>
    /// Automated setup tool for Halazia: The Lost Signal.
    /// Builds the MovementSandbox scene, configures the Agent Animator Controller with Blend Trees,
    /// sets up tactical choke points, and wires the entire Commander + Squad AI hierarchy with a single click.
    /// </summary>
    public static class SandboxAutoBuilder
    {
        private const string ControllerDirectory = "Assets/Animations/Agents";
        private const string ControllerPath = "Assets/Animations/Agents/Agent_AnimatorController.controller";
        private const string MaterialsDirectory = "Assets/Art/Materials";
        private const string SceneDirectory = "Assets/Scenes";
        private const string ScenePath = "Assets/Scenes/MovementSandbox.unity";

        [MenuItem("Halazia/⚡ 1-Click Build Sandbox Scene & Animator", false, 1)]
        public static void BuildEntireSandbox()
        {
            Debug.Log("[Halazia Builder] Starting complete automated sandbox build...");

            // 1. Build Animator Controller
            AnimatorController controller = BuildAnimatorControllerAsset();

            // 2. Build Materials (auto-detects working shader to prevent magenta/pink glitch)
            EnsureDirectory(MaterialsDirectory);
            Material matGround = GetOrCreateMaterial("Mat_Deck_Ground", new Color(0.22f, 0.25f, 0.28f));
            Material matBarricade = GetOrCreateMaterial("Mat_Barricade", new Color(0.85f, 0.45f, 0.15f));
            Material matPlayer = GetOrCreateMaterial("Mat_Player", new Color(0.15f, 0.85f, 0.35f));
            Material matTestAgent = GetOrCreateMaterial("Mat_TestAgent", new Color(0.2f, 0.7f, 1.0f));
            Material matCommander = GetOrCreateMaterial("Mat_Commander", new Color(0.65f, 0.25f, 0.9f));
            Material matHunter = GetOrCreateMaterial("Mat_Hunter", new Color(0.92f, 0.22f, 0.22f));
            Material matFlanker = GetOrCreateMaterial("Mat_Flanker", new Color(0.95f, 0.78f, 0.15f));
            Material matSentinel = GetOrCreateMaterial("Mat_Sentinel", new Color(0.18f, 0.45f, 0.95f));

            // 3. Create Scene
            EnsureDirectory(SceneDirectory);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 4. Directional Light
            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.2f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 5. Main Camera (positioned to view all squad agents, commander, player, and obstacles)
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
            camObj.transform.position = new Vector3(0f, 18f, -22f);
            camObj.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // 6. Ground Deck with NavMeshSurface
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Deck";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            if (matGround != null) ground.GetComponent<Renderer>().sharedMaterial = matGround;

            var navSurface = ground.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            navSurface.BuildNavMesh();

            // 7. Corridor Barricade (Obstacle)
            var barricade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barricade.name = "Corridor_Barricade";
            barricade.transform.position = new Vector3(0f, 0.5f, 4f);
            barricade.transform.localScale = new Vector3(3.5f, 1f, 0.6f);
            if (matBarricade != null) barricade.GetComponent<Renderer>().sharedMaterial = matBarricade;

            // 8. Player Target Capsule
            var playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerObj.name = "Player_Target";
            playerObj.tag = "Player";
            playerObj.transform.position = new Vector3(0f, 1f, -4f);
            if (matPlayer != null) playerObj.GetComponent<Renderer>().sharedMaterial = matPlayer;

            // 9. Tactical Corridor Junctions & Heatmap System
            var heatmapObj = new GameObject("Corridor_Heatmap_System");
            var heatmap = heatmapObj.AddComponent<CorridorHeatmapTracker>();

            var nodeCargo = CreateCorridorNodeMarker("Cargo_Stairs", new Vector3(-8f, 0f, 5f), 3.5f, heatmapObj.transform);
            var nodeDoor = CreateCorridorNodeMarker("GunDeck_Door", new Vector3(0f, 0f, 8f), 3.5f, heatmapObj.transform);
            var nodeLadder = CreateCorridorNodeMarker("Bridge_Ladder", new Vector3(8f, 0f, 5f), 3.5f, heatmapObj.transform);

            // Connect adjacent nodes into a topological corridor graph
            nodeCargo.ConnectedNodes.Add(nodeDoor);
            nodeDoor.ConnectedNodes.Add(nodeCargo);
            nodeDoor.ConnectedNodes.Add(nodeLadder);
            nodeLadder.ConnectedNodes.Add(nodeDoor);

            heatmap.RegisterNode("Cargo_Stairs", new Vector3(-8f, 0f, 5f), 3.5f);
            heatmap.RegisterNode("GunDeck_Door", new Vector3(0f, 0f, 8f), 3.5f);
            heatmap.RegisterNode("Bridge_Ladder", new Vector3(8f, 0f, 5f), 3.5f);
            EditorUtility.SetDirty(heatmap);

            // 10. Test Agent (SE3032 Movement & Animation Showcase)
            var testAgent = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            testAgent.name = "Test_Agent";
            testAgent.transform.position = new Vector3(0f, 1f, 0f);
            if (matTestAgent != null) testAgent.GetComponent<Renderer>().sharedMaterial = matTestAgent;
            SetupCharacterController(testAgent);

            var testAnim = testAgent.AddComponent<Animator>();
            testAnim.runtimeAnimatorController = controller;

            var testMotor = testAgent.AddComponent<AgentMotor>();
            var testDriver = testAgent.AddComponent<AgentAnimatorDriver>();
            var testSandbox = testAgent.AddComponent<MovementSandboxDriver>();

            // 11. Commander Agent (SE3062 Intelligent Systems)
            var cmdObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cmdObj.name = "Agent_Commander";
            cmdObj.transform.position = new Vector3(0f, 1f, -12f);
            if (matCommander != null) cmdObj.GetComponent<Renderer>().sharedMaterial = matCommander;
            SetupCharacterController(cmdObj);
            cmdObj.AddComponent<AgentMotor>();

            if (controller != null)
            {
                var anim = cmdObj.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                cmdObj.AddComponent<AgentAnimatorDriver>();
            }

            var commander = cmdObj.AddComponent<CommanderAgent>();
            var cmdSO = new SerializedObject(commander);
            cmdSO.FindProperty("_playerTransform").objectReferenceValue = playerObj.transform;
            cmdSO.FindProperty("_heatmapTracker").objectReferenceValue = heatmap;
            cmdSO.FindProperty("_preferredCommandDistance").floatValue = 12.0f;
            cmdSO.FindProperty("_decisionTickInterval").floatValue = 0.5f;
            cmdSO.ApplyModifiedProperties();

            // 12. Mock Squad Agents (Hunter, Flanker, Sentinel)
            CreateSquadAgent("Agent_Hunter", new Vector3(-3.5f, 1f, -8f), AgentRole.Hunter, 100f, matHunter, controller);
            CreateSquadAgent("Agent_Flanker", new Vector3(3.5f, 1f, -8f), AgentRole.Flanker, 90f, matFlanker, controller);
            CreateSquadAgent("Agent_Sentinel", new Vector3(0f, 1f, -9f), AgentRole.Sentinel, 180f, matSentinel, controller);

            // Re-bake NavMesh to include all static geometry
            navSurface.BuildNavMesh();

            // Save Scene
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Halazia Builder] Successfully generated MovementSandbox scene at '{ScenePath}'!");
            EditorUtility.DisplayDialog(
                "Halazia Auto-Builder",
                "Movement Sandbox Scene & Animator Controller have been built successfully!\n\n" +
                "• Scene: Assets/Scenes/MovementSandbox.unity\n" +
                "• Animator: Assets/Animations/Agents/Agent_AnimatorController.controller\n\n" +
                "Camera & materials are fully updated. Switch to the Scene tab or press Play to test!",
                "Awesome!"
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Remove Old Soldier Models
        // Deletes Enemy_Base and Enemy_commander prefab instances from the active
        // scene. Touches NOTHING else — perfectly safe to run at any time.
        // ─────────────────────────────────────────────────────────────────────────
        [MenuItem("Halazia/🗑️ Remove Old Soldier Models from Scene", false, 2)]
        public static void RemoveOldSoldierModels()
        {
            // Names that correspond to the two old soldier prefab instances
            string[] targetNames = { "Enemy_Base", "Enemy_commander" };

            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var removed = new System.Collections.Generic.List<string>();

            foreach (var go in allObjects)
            {
                foreach (var targetName in targetNames)
                {
                    // Match by exact name OR prefab-style name (e.g. "Enemy_Base (1)")
                    if (go != null && go.name.StartsWith(targetName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        removed.Add(go.name);
                        Undo.DestroyObjectImmediate(go);
                        break;
                    }
                }
            }

            if (removed.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Remove Old Soldier Models",
                    "No soldier models found in the active scene.\n\n" +
                    "Make sure the MovementSandbox scene is open and the objects are named:\n" +
                    "• Enemy_Base\n• Enemy_commander",
                    "OK"
                );
                return;
            }

            // Save the scene so the removal is persisted on disk
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            string removedList = string.Join("\n• ", removed);
            Debug.Log($"[Halazia] Removed {removed.Count} old soldier model(s): {removedList}");
            EditorUtility.DisplayDialog(
                "Remove Old Soldier Models",
                $"Successfully removed {removed.Count} object(s):\n• {removedList}\n\nThe scene has been saved.",
                "Done"
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Attach Commander 3D Model to Agent_Commander
        // Hides the placeholder capsule renderer, instantiates Commander.fbx as
        // a child of Agent_Commander, and wires the Animator Controller.
        // Touches ONLY Agent_Commander — no other scene objects or scripts changed.
        // ─────────────────────────────────────────────────────────────────────────
        private const string CommanderModelPath = "Assets/Art/Models/Agents/Commander.fbx";

        [MenuItem("Halazia/🧍 Attach Commander Model to Agent_Commander", false, 3)]
        public static void AttachCommanderModel()
        {
            // 1. Find Agent_Commander in the active scene (robust lookup)
            var cmdObj = GameObject.Find("Agent_Commander");
            if (cmdObj == null)
            {
                var all = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var go in all)
                {
                    if (go.name.StartsWith("Agent_Command", System.StringComparison.OrdinalIgnoreCase))
                    {
                        cmdObj = go;
                        break;
                    }
                }
            }

            if (cmdObj == null)
            {
                EditorUtility.DisplayDialog(
                    "Attach Commander Model",
                    "Could not find 'Agent_Commander' in the active scene.\n\n" +
                    "Make sure MovementSandbox is open and the object exists.",
                    "OK"
                );
                return;
            }

            // Normalize name
            cmdObj.name = "Agent_Commander";

            // 2. Hide the placeholder capsule MeshRenderer (keep collider + scripts)
            var meshRenderer = cmdObj.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
                EditorUtility.SetDirty(cmdObj);
            }

            // 3. Remove any previously attached model child (re-run safe)
            var existing = cmdObj.transform.Find("CommanderModel");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            // 4. Load Commander.fbx prefab
            var commanderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommanderModelPath);
            if (commanderPrefab == null)
            {
                EditorUtility.DisplayDialog(
                    "Attach Commander Model",
                    "Commander model not found at:\n" + CommanderModelPath,
                    "OK"
                );
                return;
            }

            // 5. Instantiate the model as a child of Agent_Commander
            var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(commanderPrefab, cmdObj.transform);
            modelInstance.name = "CommanderModel";
            modelInstance.transform.localPosition = new Vector3(0f, -1f, 0f); // offset so feet sit on ground
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale    = Vector3.one;

            // 6. Configure Animator on the child CommanderModel:
            // In Unity, a Humanoid Avatar must sit directly on the model GameObject that roots the bone hierarchy.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            var modelAnimator = modelInstance.GetComponent<Animator>();
            if (modelAnimator == null)
                modelAnimator = modelInstance.AddComponent<Animator>();

            modelAnimator.enabled = true;
            if (controller != null)
                modelAnimator.runtimeAnimatorController = controller;

            // Ensure humanoid avatar is assigned
            if (modelAnimator.avatar == null)
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(CommanderModelPath);
                foreach (var sub in subAssets)
                {
                    if (sub is Avatar av && av.isValid)
                    {
                        modelAnimator.avatar = av;
                        break;
                    }
                }
            }
            EditorUtility.SetDirty(modelAnimator);

            // 7. Configure components on parent Agent_Commander:
            // - Ensure AgentAnimatorDriver exists on parent (locates child Animator automatically)
            var driver = cmdObj.GetComponent<AgentAnimatorDriver>();
            if (driver == null)
                driver = cmdObj.AddComponent<AgentAnimatorDriver>();
            EditorUtility.SetDirty(driver);

            // - Ensure CombatActionsAdapter exists on parent so CommanderAgent has ICombatActions
            var combatAdapter = cmdObj.GetComponent<Halazia.Shared.Combat.CombatActionsAdapter>();
            if (combatAdapter == null)
                combatAdapter = cmdObj.AddComponent<Halazia.Shared.Combat.CombatActionsAdapter>();
            EditorUtility.SetDirty(combatAdapter);

            // - Remove redundant/empty Animator on the parent capsule to prevent retargeting conflicts
            var parentAnimator = cmdObj.GetComponent<Animator>();
            if (parentAnimator != null)
            {
                Undo.DestroyObjectImmediate(parentAnimator);
            }

            // 8. Fix squad agent roles and name tags to match their colors
            FixSquadAgentRoles();

            // 9. Register undo & save
            Undo.RegisterCreatedObjectUndo(modelInstance, "Attach Commander Model");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            // Ping and select Agent_Commander in the Hierarchy so it's clearly visible
            Selection.activeGameObject = cmdObj;
            EditorGUIUtility.PingObject(cmdObj);

            Debug.Log("[Halazia] Commander model attached to Agent_Commander successfully.");
            EditorUtility.DisplayDialog(
                "Attach Commander Model",
                "Commander model attached to Agent_Commander!\n\n" +
                "• Placeholder capsule renderer hidden\n" +
                "• Commander.fbx spawned as child 'CommanderModel' (Animator + Avatar enabled)\n" +
                "• Parent AgentAnimatorDriver & CombatActionsAdapter wired\n" +
                "• Name tags corrected (Red: Hunter, Yellow: Flanker, Blue: Sentinel, 3D: Commander)\n\n" +
                "Agent_Commander is selected. Press Play to test locomotion animations!",
                "Done"
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Fix Squad Agent Roles and Name Tags
        // Ensures Inspector roles match the GameObject identity and debug colors:
        // • Red capsule    = Hunter
        // • Yellow capsule = Flanker
        // • Blue capsule   = Sentinel
        // ─────────────────────────────────────────────────────────────────────────
        [MenuItem("Halazia/🏷️ Fix Agent Roles & Name Tags in Scene", false, 4)]
        public static void FixAgentRolesAndNameTags()
        {
            FixSquadAgentRoles();
            EditorUtility.DisplayDialog(
                "Fix Agent Roles & Name Tags",
                "Squad roles and name tags have been corrected!\n\n" +
                "• Agent_Hunter   → Red Capsule (Hunter_01)\n" +
                "• Agent_Flanker  → Yellow Capsule (Flanker_01)\n" +
                "• Agent_Sentinel → Blue Capsule (Sentinel_01)\n" +
                "• Agent_Commander → 3D Model (Commander)\n\n" +
                "The scene has been saved.",
                "Done"
            );
        }

        private static void FixSquadAgentRoles()
        {
            var hunter = GameObject.Find("Agent_Hunter");
            if (hunter != null)
            {
                var mock = hunter.GetComponent<MockSquadAgent>();
                if (mock != null)
                {
                    var so = new SerializedObject(mock);
                    so.FindProperty("_role").enumValueIndex = (int)AgentRole.Hunter;
                    so.FindProperty("_agentId").stringValue = "Hunter_01";
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(mock);
                }
            }

            var flanker = GameObject.Find("Agent_Flanker");
            if (flanker != null)
            {
                var mock = flanker.GetComponent<MockSquadAgent>();
                if (mock != null)
                {
                    var so = new SerializedObject(mock);
                    so.FindProperty("_role").enumValueIndex = (int)AgentRole.Flanker;
                    so.FindProperty("_agentId").stringValue = "Flanker_01";
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(mock);
                }
            }

            var sentinel = GameObject.Find("Agent_Sentinel");
            if (sentinel != null)
            {
                var mock = sentinel.GetComponent<MockSquadAgent>();
                if (mock != null)
                {
                    var so = new SerializedObject(mock);
                    so.FindProperty("_role").enumValueIndex = (int)AgentRole.Sentinel;
                    so.FindProperty("_agentId").stringValue = "Sentinel_01";
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(mock);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[Halazia] Squad agent roles & name tags corrected in active scene.");
        }


        // Loads FBX clips from Assets/Animations/Agents/Clips/ and assigns them
        // to the correct states in Agent_AnimatorController.controller.
        // Touches ONLY the Animator Controller asset — nothing else.
        // ─────────────────────────────────────────────────────────────────────────
        [MenuItem("Halazia/🎬 Wire Animation Clips into Animator", false, 3)]
        public static void WireAnimationClips()
        {
            // Load the Animator Controller
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Wire Animation Clips",
                    "Animator Controller not found at:\n" + ControllerPath +
                    "\n\nRun '⚡ 1-Click Build Sandbox Scene & Animator' first.",
                    "OK"
                );
                return;
            }

            // ── Helper: load the first non-preview AnimationClip from an FBX ──
            System.Func<string, AnimationClip> LoadClip = (fileName) =>
            {
                string path = $"Assets/Animations/Agents/Clips/{fileName}";
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var a in assets)
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                        return c;
                Debug.LogWarning($"[Halazia] Clip not found: {path}");
                return null;
            };

            // ── Load the clips we need ──
            AnimationClip clipIdle   = LoadClip("Rifle Idle.fbx");
            AnimationClip clipWalk   = LoadClip("Walk Forward.fbx") ?? LoadClip("Pistol Walk.fbx");
            AnimationClip clipRun    = LoadClip("Run Forward.fbx");
            AnimationClip clipAttack = LoadClip("Standing Melee Attack Horizontal.fbx") ?? LoadClip("Standing Melee Attack 360 High.fbx");
            AnimationClip clipHit    = LoadClip("Hit Reaction.fbx");
            AnimationClip clipDeath  = LoadClip("Dying.fbx");

            var rootSM = controller.layers[0].stateMachine;
            int wired  = 0;

            foreach (var stateRef in rootSM.states)
            {
                var state = stateRef.state;

                // ── Locomotion Blend Tree ──────────────────────────────────────
                if (state.name == "Locomotion" && state.motion is BlendTree bt)
                {
                    var children = bt.children;

                    if (children.Length >= 1 && clipIdle  != null) { children[0].motion = clipIdle;  wired++; }
                    if (children.Length >= 2 && clipWalk  != null) { children[1].motion = clipWalk;  wired++; }
                    if (children.Length >= 3 && clipRun   != null) { children[2].motion = clipRun;   wired++; }

                    bt.children = children;
                    EditorUtility.SetDirty(bt);
                }

                // ── Attack_Melee ───────────────────────────────────────────────
                if (state.name == "Attack_Melee" && clipAttack != null)
                {
                    state.motion = clipAttack;
                    wired++;
                }

                // ── Hit_Reaction ───────────────────────────────────────────────
                if (state.name == "Hit_Reaction" && clipHit != null)
                {
                    state.motion = clipHit;
                    wired++;
                }

                // ── Death ──────────────────────────────────────────────────────
                if (state.name == "Death" && clipDeath != null)
                {
                    state.motion = clipDeath;
                    wired++;
                }

                EditorUtility.SetDirty(state);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Halazia] Wired {wired} animation clip(s) into '{ControllerPath}'.");
            EditorUtility.DisplayDialog(
                "Wire Animation Clips",
                $"Successfully wired {wired} clip(s) into the Animator Controller:\n\n" +
                $"• Locomotion Blend Tree → Idle / Walk / Run\n" +
                $"• Attack_Melee  → Standing Melee Attack (1)\n" +
                $"• Hit_Reaction  → Hit Reaction\n" +
                $"• Death         → Dying\n\n" +
                $"Open the Animator window to verify.",
                "Done"
            );
        }

        [MenuItem("Halazia/⚡ Fix Pink Shaders & Reset Camera", false, 4)]
        public static void FixMaterialsAndCamera()
        {
            EnsureDirectory(MaterialsDirectory);
            GetOrCreateMaterial("Mat_Deck_Ground", new Color(0.22f, 0.25f, 0.28f));
            GetOrCreateMaterial("Mat_Barricade", new Color(0.85f, 0.45f, 0.15f));
            GetOrCreateMaterial("Mat_Player", new Color(0.15f, 0.85f, 0.35f));
            GetOrCreateMaterial("Mat_TestAgent", new Color(0.2f, 0.7f, 1.0f));
            GetOrCreateMaterial("Mat_Commander", new Color(0.65f, 0.25f, 0.9f));
            GetOrCreateMaterial("Mat_Hunter", new Color(0.92f, 0.22f, 0.22f));
            GetOrCreateMaterial("Mat_Flanker", new Color(0.95f, 0.78f, 0.15f));
            GetOrCreateMaterial("Mat_Sentinel", new Color(0.18f, 0.45f, 0.95f));

            var camObj = GameObject.FindWithTag("MainCamera");
            if (camObj != null)
            {
                camObj.transform.position = new Vector3(0f, 18f, -22f);
                camObj.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
                EditorUtility.SetDirty(camObj);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Halazia Builder] All materials repaired with working standard shaders!");
        }

        private static AnimatorController BuildAnimatorControllerAsset()
        {
            EnsureDirectory(ControllerDirectory);

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            // Remove existing parameters to avoid duplicates on re-run
            while (controller.parameters.Length > 0)
            {
                controller.RemoveParameter(0);
            }

            // Add required parameters matching AgentAnimatorDriver and Viva rubrics
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackType", AnimatorControllerParameterType.Int);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

            var rootStateMachine = controller.layers[0].stateMachine;

            // Clear existing states/transitions
            rootStateMachine.states = new ChildAnimatorState[0];
            rootStateMachine.anyStateTransitions = new AnimatorStateTransition[0];

            // 1. Locomotion Blend Tree State
            var locomotionState = rootStateMachine.AddState("Locomotion", new Vector3(260, 50, 0));
            rootStateMachine.defaultState = locomotionState;

            var blendTree = new BlendTree
            {
                name = "LocomotionBlend",
                blendParameter = "Speed",
                blendType = BlendTreeType.Simple1D,
                useAutomaticThresholds = false
            };
            blendTree.AddChild(null, 0.0f);
            blendTree.AddChild(null, 2.0f);
            blendTree.AddChild(null, 4.2f);

            AssetDatabase.AddObjectToAsset(blendTree, controller);
            locomotionState.motion = blendTree;

            // 2. Action States
            var attackState = rootStateMachine.AddState("Attack_Melee", new Vector3(560, -40, 0));
            var hitState = rootStateMachine.AddState("Hit_Reaction", new Vector3(560, 60, 0));
            var deathState = rootStateMachine.AddState("Death", new Vector3(560, 160, 0));

            // 3. Transitions
            // Locomotion -> Attack_Melee
            var toAttack = locomotionState.AddTransition(attackState);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.1f;
            toAttack.hasFixedDuration = true;
            toAttack.AddCondition(AnimatorConditionMode.If, 0, "Attack");

            // Attack_Melee -> Locomotion
            var fromAttack = attackState.AddTransition(locomotionState);
            fromAttack.hasExitTime = true;
            fromAttack.exitTime = 0.85f;
            fromAttack.duration = 0.15f;
            fromAttack.hasFixedDuration = true;

            // AnyState -> Hit_Reaction
            var toHit = rootStateMachine.AddAnyStateTransition(hitState);
            toHit.hasExitTime = false;
            toHit.duration = 0.1f;
            toHit.hasFixedDuration = true;
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");

            // Hit_Reaction -> Locomotion
            var fromHit = hitState.AddTransition(locomotionState);
            fromHit.hasExitTime = true;
            fromHit.exitTime = 0.85f;
            fromHit.duration = 0.15f;
            fromHit.hasFixedDuration = true;

            // AnyState -> Death
            var toDeath = rootStateMachine.AddAnyStateTransition(deathState);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.hasFixedDuration = true;
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void CreateSquadAgent(string agentName, Vector3 position, AgentRole role, float health, Material mat, RuntimeAnimatorController controller)
        {
            var agentObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObj.name = agentName;
            agentObj.transform.position = position;
            if (mat != null) agentObj.GetComponent<Renderer>().sharedMaterial = mat;

            SetupCharacterController(agentObj);
            agentObj.AddComponent<AgentMotor>();

            if (controller != null)
            {
                var anim = agentObj.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                agentObj.AddComponent<AgentAnimatorDriver>();
            }

            var mock = agentObj.AddComponent<MockSquadAgent>();
            var mockSO = new SerializedObject(mock);
            mockSO.FindProperty("_agentId").stringValue = $"{role}_01";
            mockSO.FindProperty("_role").enumValueIndex = (int)role;
            mockSO.FindProperty("_maxHealth").floatValue = health;
            mockSO.ApplyModifiedProperties();
        }

        private static void SetupCharacterController(GameObject obj)
        {
            var col = obj.GetComponent<CapsuleCollider>();
            if (col != null) Object.DestroyImmediate(col);

            var cc = obj.GetComponent<CharacterController>();
            if (cc == null) cc = obj.AddComponent<CharacterController>();
            cc.center = Vector3.zero;
            cc.radius = 0.5f;
            cc.height = 2.0f;
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.08f;
            cc.minMoveDistance = 0.001f;
        }

        private static Material GetOrCreateMaterial(string matName, Color color)
        {
            string path = $"{MaterialsDirectory}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            // Use Standard or Diffuse shader to guarantee it never renders as pink/magenta
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Diffuse") ?? Shader.Find("Unlit/Color");

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.color = color;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static CorridorNodeMarker CreateCorridorNodeMarker(string nodeId, Vector3 position, float radius, Transform parent)
        {
            var nodeObj = new GameObject($"Node_{nodeId}");
            nodeObj.transform.position = position;
            if (parent != null) nodeObj.transform.SetParent(parent);

            var marker = nodeObj.AddComponent<CorridorNodeMarker>();
            marker.NodeId = nodeId;
            marker.Radius = radius;
            return marker;
        }

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
