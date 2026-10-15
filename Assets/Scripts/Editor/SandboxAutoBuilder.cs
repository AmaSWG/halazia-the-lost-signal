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

            // 9. Corridor Heatmap System
            var heatmapObj = new GameObject("Corridor_Heatmap_System");
            var heatmap = heatmapObj.AddComponent<CorridorHeatmapTracker>();
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

        [MenuItem("Halazia/⚡ Fix Pink Shaders & Reset Camera", false, 2)]
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

        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
