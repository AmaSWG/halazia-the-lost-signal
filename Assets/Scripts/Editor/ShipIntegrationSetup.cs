using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using Halazia.AI.Commander;
using Halazia.Shared.Combat;
using Halazia.Shared.Movement;
using Halazia.Sandbox;

namespace Halazia.Editor
{
    /// <summary>
    /// Automated setup tool for integrating the Commander Agent and a playable test target
    /// directly into the ship environment scene (halazia_Ship_World.unity).
    /// Cleans any duplicate commanders, grounds the single Commander and Player in the main corridor,
    /// configures WASD player controls, and sets up the camera.
    /// </summary>
    public static class ShipIntegrationSetup
    {
        private const string ScenePath = "Assets/Scenes/halazia_Ship_World.unity";
        private const string CommanderModelPath = "Assets/Art/Models/Agents/Commander.fbx";
        private const string AnimatorControllerPath = "Assets/Animations/Agents/Agent_AnimatorController.controller";

        [MenuItem("Halazia/🚢 Setup Commander & Player in Ship World", false, 1)]
        public static void SetupShipWorld()
        {
            Debug.Log("[ShipIntegration] Cleaning duplicates and setting up Commander & Player...");

            // 1. Open scene if not open
            var currentScene = EditorSceneManager.GetActiveScene();
            if (currentScene.path != ScenePath)
            {
                currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            if (!currentScene.IsValid())
            {
                Debug.LogError($"[ShipIntegration] Could not open scene: {ScenePath}");
                return;
            }

            // 2. Clean up duplicate Commanders (keep exactly one)
            var allCommanders = new List<GameObject>();
            foreach (var go in currentScene.GetRootGameObjects())
            {
                if (go.name == "Commander" || go.name == "Agent_Commander" || go.GetComponent<CommanderAgent>() != null)
                {
                    allCommanders.Add(go);
                }
            }

            GameObject commanderObj = null;
            if (allCommanders.Count > 0)
            {
                // Prefer the one that has CommanderAgent or the original prefab
                commanderObj = allCommanders[0];
                for (int i = 1; i < allCommanders.Count; i++)
                {
                    Debug.Log($"[ShipIntegration] Removing duplicate Commander GameObject: '{allCommanders[i].name}'");
                    Undo.DestroyObjectImmediate(allCommanders[i]);
                }
            }

            // 3. Define Corridor Spawn Points
            // The ship's main deck corridor runs along the X axis at Z ≈ -0.85
            Vector3 commanderSpawn = new Vector3(18.5f, 0.08f, -0.85f);
            if (NavMesh.SamplePosition(commanderSpawn, out NavMeshHit cmdHit, 5.0f, NavMesh.AllAreas))
            {
                commanderSpawn = cmdHit.position;
            }

            Vector3 playerSpawn = new Vector3(13.5f, 0.08f, -0.85f); // 5m in front of Commander down the same corridor
            if (NavMesh.SamplePosition(playerSpawn, out NavMeshHit plyHit, 5.0f, NavMesh.AllAreas))
            {
                playerSpawn = plyHit.position;
            }

            // 4. Configure Single Commander
            if (commanderObj == null)
            {
                var fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommanderModelPath);
                if (fbxPrefab != null)
                {
                    commanderObj = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab);
                }
                else
                {
                    commanderObj = new GameObject("Commander");
                }
                Undo.RegisterCreatedObjectUndo(commanderObj, "Create Commander");
            }

            commanderObj.name = "Commander";
            commanderObj.transform.position = commanderSpawn;
            commanderObj.transform.rotation = Quaternion.Euler(0f, -90f, 0f); // Face down the corridor toward Player (-X)

            // CharacterController
            var cc = commanderObj.GetComponent<CharacterController>();
            if (cc == null) cc = commanderObj.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.4f;
            cc.slopeLimit = 45f;

            // AgentMotor
            var motor = commanderObj.GetComponent<AgentMotor>();
            if (motor == null) motor = commanderObj.AddComponent<AgentMotor>();

            // AgentAnimatorDriver
            var driver = commanderObj.GetComponent<AgentAnimatorDriver>();
            if (driver == null) driver = commanderObj.AddComponent<AgentAnimatorDriver>();

            // CombatActionsAdapter
            var combat = commanderObj.GetComponent<CombatActionsAdapter>();
            if (combat == null) combat = commanderObj.AddComponent<CombatActionsAdapter>();

            // CommanderAgent
            var commanderAgent = commanderObj.GetComponent<CommanderAgent>();
            if (commanderAgent == null) commanderAgent = commanderObj.AddComponent<CommanderAgent>();

            // Animator setup
            var animator = commanderObj.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                var modelChild = commanderObj.transform.Find("CommanderModel");
                if (modelChild == null)
                {
                    var fbxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CommanderModelPath);
                    if (fbxPrefab != null)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxPrefab, commanderObj.transform);
                        instance.name = "CommanderModel";
                        modelChild = instance.transform;
                    }
                }
                if (modelChild != null)
                {
                    animator = modelChild.GetComponent<Animator>();
                    if (animator == null) animator = modelChild.gameObject.AddComponent<Animator>();
                }
            }

            if (animator != null)
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorControllerPath);
                if (controller != null) animator.runtimeAnimatorController = controller;

                if (animator.avatar == null)
                {
                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(CommanderModelPath);
                    foreach (var sub in subAssets)
                    {
                        if (sub is Avatar av && av.isValid)
                        {
                            animator.avatar = av;
                            break;
                        }
                    }
                }
                EditorUtility.SetDirty(animator);
            }

            // 5. Setup Player GameObject (Green Capsule in Corridor)
            var playerObj = GameObject.Find("Player");
            if (playerObj == null) playerObj = GameObject.FindWithTag("Player");

            if (playerObj == null)
            {
                playerObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                playerObj.name = "Player";
                Undo.RegisterCreatedObjectUndo(playerObj, "Create Player");
            }

            playerObj.tag = "Player";
            playerObj.transform.position = playerSpawn;
            playerObj.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // Face the Commander (+X)

            // Bright green material
            var rend = playerObj.GetComponent<MeshRenderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader);
                mat.color = new Color(0.1f, 0.95f, 0.2f, 1.0f);
                rend.sharedMaterial = mat;
            }

            // Ensure Player CharacterController
            var playerCC = playerObj.GetComponent<CharacterController>();
            if (playerCC == null) playerCC = playerObj.AddComponent<CharacterController>();
            playerCC.radius = 0.4f;
            playerCC.height = 1.8f;
            playerCC.center = new Vector3(0f, 0.9f, 0f);

            // TestPlayerMover (dual new/old input system + follow camera)
            var playerMover = playerObj.GetComponent<TestPlayerMover>();
            if (playerMover == null) playerMover = playerObj.AddComponent<TestPlayerMover>();

            // 6. Connect Player to CommanderAgent inspector field
            var so = new SerializedObject(commanderAgent);
            var playerProp = so.FindProperty("_playerTransform");
            if (playerProp != null)
            {
                playerProp.objectReferenceValue = playerObj.transform;
                so.ApplyModifiedProperties();
            }

            // 7. Setup Main Camera behind Player
            var cam = Camera.main;
            if (cam == null) cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                // Position behind Player, looking down corridor at Player and Commander
                cam.transform.position = playerSpawn + new Vector3(-4.0f, 2.2f, 0f);
                cam.transform.LookAt(playerSpawn + new Vector3(2.0f, 1.2f, 0f));
                EditorUtility.SetDirty(cam);
            }

            // 8. Mark scene dirty & save
            EditorSceneManager.MarkSceneDirty(currentScene);
            EditorSceneManager.SaveScene(currentScene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ShipIntegration] DONE! Single Commander at {commanderSpawn}, Player at {playerSpawn}. Press Play to test!");
        }
    }
}
