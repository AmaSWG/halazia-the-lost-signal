using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Halazia.AI.Commander;
using Halazia.Shared.Movement;

namespace Halazia.Editor
{
    public static class ShipNavMeshVerifier
    {
        private const string ScenePath = "Assets/Scenes/halazia_Ship_World.unity";
        private static string LogFilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "../navmesh_verification_report.txt"));

        [MenuItem("Halazia/🔍 Verify Commander NavMesh Walkability", false, 2)]
        public static void RunVerification()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== SHIP ENVIRONMENT NAVMESH & COMMANDER WALKABILITY REPORT ===");
            sb.AppendLine($"Timestamp: {System.DateTime.Now}");
            
            // 1. Open Scene
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                sb.AppendLine($"[ERROR] Could not open scene: {ScenePath}");
                File.WriteAllText(LogFilePath, sb.ToString());
                return;
            }
            sb.AppendLine($"[SCENE] Successfully loaded '{ScenePath}'");

            // 2. Check NavMesh Triangulation
            var tri = NavMesh.CalculateTriangulation();
            int vertCount = tri.vertices != null ? tri.vertices.Length : 0;
            int polyCount = tri.indices != null ? tri.indices.Length / 3 : 0;
            sb.AppendLine($"[NAVMESH] Triangulation Vertices: {vertCount}, Polygons/Triangles: {polyCount}");

            if (vertCount == 0)
            {
                sb.AppendLine("[ERROR] NavMesh has 0 vertices! NavMesh is NOT baked or empty in this scene!");
                File.WriteAllText(LogFilePath, sb.ToString());
                return;
            }

            // Calculate NavMesh Bounding Box
            Vector3 min = tri.vertices[0];
            Vector3 max = tri.vertices[0];
            for (int i = 1; i < tri.vertices.Length; i++)
            {
                min = Vector3.Min(min, tri.vertices[i]);
                max = Vector3.Max(max, tri.vertices[i]);
            }
            sb.AppendLine($"[NAVMESH] NavMesh Bounds: Min({min.x:F2}, {min.y:F2}, {min.z:F2}) -> Max({max.x:F2}, {max.y:F2}, {max.z:F2})");
            sb.AppendLine($"[NAVMESH] NavMesh Size: Width(X)={(max.x - min.x):F2}m, Height(Y)={(max.y - min.y):F2}m, Depth(Z)={(max.z - min.z):F2}m");

            // 3. Find Commander Object
            GameObject commanderObj = GameObject.Find("Commander");
            if (commanderObj == null) commanderObj = GameObject.Find("Agent_Commander");

            if (commanderObj == null)
            {
                sb.AppendLine("[WARNING] Commander GameObject not found by name 'Commander' or 'Agent_Commander'. Searching for CommanderAgent component...");
                var agent = Object.FindAnyObjectByType<CommanderAgent>();
                if (agent != null) commanderObj = agent.gameObject;
            }

            if (commanderObj == null)
            {
                sb.AppendLine("[ERROR] No Commander found in scene!");
                File.WriteAllText(LogFilePath, sb.ToString());
                return;
            }

            Vector3 cmdPos = commanderObj.transform.position;
            sb.AppendLine($"[COMMANDER] Found Commander GameObject '{commanderObj.name}' at position: ({cmdPos.x:F2}, {cmdPos.y:F2}, {cmdPos.z:F2})");

            // Check components on Commander
            var cc = commanderObj.GetComponent<CharacterController>();
            var motor = commanderObj.GetComponent<AgentMotor>();
            var driver = commanderObj.GetComponent<AgentAnimatorDriver>();
            var commanderAgent = commanderObj.GetComponent<CommanderAgent>();

            sb.AppendLine($"[COMPONENTS] CharacterController: {(cc != null ? "Present (Radius=" + cc.radius + ", Height=" + cc.height + ")" : "MISSING")}");
            sb.AppendLine($"[COMPONENTS] AgentMotor: {(motor != null ? "Present" : "MISSING")}");
            sb.AppendLine($"[COMPONENTS] AgentAnimatorDriver: {(driver != null ? "Present" : "MISSING")}");
            sb.AppendLine($"[COMPONENTS] CommanderAgent: {(commanderAgent != null ? "Present" : "MISSING")}");

            // 4. Sample Commander Position on NavMesh
            bool grounded = NavMesh.SamplePosition(cmdPos, out NavMeshHit cmdHit, 2.0f, NavMesh.AllAreas);
            if (grounded)
            {
                float groundDist = Vector3.Distance(cmdPos, cmdHit.position);
                sb.AppendLine($"[GROUNDING] SUCCESS: Commander is on/near NavMesh! Nearest NavMesh point: ({cmdHit.position.x:F2}, {cmdHit.position.y:F2}, {cmdHit.position.z:F2}), Distance to surface: {groundDist:F3}m");
            }
            else
            {
                sb.AppendLine($"[GROUNDING] WARNING: Commander at ({cmdPos.x:F2}, {cmdPos.y:F2}, {cmdPos.z:F2}) is NOT within 2.0m of NavMesh surface!");
                if (NavMesh.SamplePosition(cmdPos, out NavMeshHit wideHit, 15.0f, NavMesh.AllAreas))
                {
                    sb.AppendLine($"[GROUNDING] Nearest NavMesh point within 15m is at ({wideHit.position.x:F2}, {wideHit.position.y:F2}, {wideHit.position.z:F2}) (distance: {wideHit.distance:F2}m)");
                }
            }

            // 5. Test Pathfinding in all directions from Commander
            sb.AppendLine("\n--- PATHFINDING TESTS FROM COMMANDER LOCATION ---");
            Vector3 startPos = grounded ? cmdHit.position : cmdPos;

            Vector3[] testDirections = new Vector3[]
            {
                new Vector3(5f, 0, 0),    // East +X
                new Vector3(-5f, 0, 0),   // West -X
                new Vector3(0, 0, 5f),    // North +Z
                new Vector3(0, 0, -5f),   // South -Z
                new Vector3(10f, 0, 0),   // Far East
                new Vector3(-10f, 0, 0),  // Far West
                new Vector3(0, 0, 10f),   // Far North
                new Vector3(0, 0, -10f),  // Far South
                new Vector3(15f, 0, 0),   // Corridor East
                new Vector3(-15f, 0, 0),  // Corridor West
            };

            int successCount = 0;
            int totalTests = testDirections.Length;

            for (int i = 0; i < testDirections.Length; i++)
            {
                Vector3 targetCandidate = startPos + testDirections[i];
                if (NavMesh.SamplePosition(targetCandidate, out NavMeshHit targetHit, 4.0f, NavMesh.AllAreas))
                {
                    NavMeshPath path = new NavMeshPath();
                    bool pathFound = NavMesh.CalculatePath(startPos, targetHit.position, NavMesh.AllAreas, path);
                    if (pathFound && path.status == NavMeshPathStatus.PathComplete)
                    {
                        float pathLength = 0f;
                        for (int c = 0; c < path.corners.Length - 1; c++)
                        {
                            pathLength += Vector3.Distance(path.corners[c], path.corners[c + 1]);
                        }
                        sb.AppendLine($"[TEST {i + 1}] Target ({targetHit.position.x:F1}, {targetHit.position.y:F1}, {targetHit.position.z:F1}): PathComplete! Corners={path.corners.Length}, TotalDistance={pathLength:F2}m");
                        successCount++;
                    }
                    else if (pathFound && path.status == NavMeshPathStatus.PathPartial)
                    {
                        sb.AppendLine($"[TEST {i + 1}] Target ({targetHit.position.x:F1}, {targetHit.position.y:F1}, {targetHit.position.z:F1}): PathPartial (Obstacle/Door blocking)");
                    }
                    else
                    {
                        sb.AppendLine($"[TEST {i + 1}] Target ({targetHit.position.x:F1}, {targetHit.position.y:F1}, {targetHit.position.z:F1}): PathInvalid (No route)");
                    }
                }
                else
                {
                    sb.AppendLine($"[TEST {i + 1}] Direction ({testDirections[i].x}, {testDirections[i].z}): Target candidate outside NavMesh");
                }
            }

            sb.AppendLine($"\nPathfinding Summary: {successCount}/{totalTests} direct corridor routes completely navigable.");

            // 6. Check Player Target & Perception
            sb.AppendLine("\n--- PLAYER TARGET & PERCEPTION CHECK ---");
            var playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                float playerDist = Vector3.Distance(commanderObj.transform.position, playerObj.transform.position);
                sb.AppendLine($"[PLAYER] Found GameObject tagged 'Player' at ({playerObj.transform.position.x:F2}, {playerObj.transform.position.y:F2}, {playerObj.transform.position.z:F2})");
                sb.AppendLine($"[PLAYER] Distance to Commander: {playerDist:F2}m");
            }
            else
            {
                sb.AppendLine("[PLAYER] WARNING: No GameObject with tag 'Player' currently exists in scene!");
                sb.AppendLine("[PLAYER] Recommendation: Run 'Halazia -> Setup Commander & Player in Ship World' or add a Player GameObject with 'Player' tag.");
            }

            // 7. Conclusion
            sb.AppendLine("\n=== CONCLUSION ===");
            if (grounded && successCount > 0)
            {
                sb.AppendLine("RESULT: PASSED! The Commander is successfully grounded on the ship NavMesh and can compute navigation paths through the corridors.");
            }
            else
            {
                sb.AppendLine("RESULT: FAILED or INCOMPLETE. Check detailed logs above.");
            }

            string report = sb.ToString();
            Debug.Log(report);
            File.WriteAllText(LogFilePath, report);
        }
    }
}
