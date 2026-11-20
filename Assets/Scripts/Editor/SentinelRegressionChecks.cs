using System;
using System.Collections.Generic;
using System.Reflection;
using Halazia.AI.Sentinel;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Run with Unity -batchmode -nographics -executeMethod SentinelRegressionChecks.Run.
/// Uses an unsaved, empty scene and an in-memory NavMesh; never changes project scenes.</summary>
public static class SentinelRegressionChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run these checks in a separate batch-mode Unity process.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(new Vector3(0f, -0.1f, 0f), Quaternion.identity, Vector3.one),
                size = new Vector3(30f, 0.2f, 30f), area = 0
            }
        };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources,
            new Bounds(Vector3.zero, new Vector3(40f, 10f, 40f)), Vector3.zero, Quaternion.identity);
        if (data == null) throw new Exception("Could not build regression NavMesh.");
        var instance = NavMesh.AddNavMeshData(data);
        int passed = 0;
        try
        {
            Check(agent =>
            {
                agent.AssignOrder(SquadOrder.Patrol(Vector3.zero));
                Call(agent, "CompleteOrderArrival");
                Call(agent, "TickPatrol");
                Call(agent, "ResumeAtPost");
                Require(agent.State == SentinelAgent.SentinelState.Guard, "Empty ordered patrol must settle into Guard.");
            }); passed++;

            Check(agent =>
            {
                agent.AssignOrder(SquadOrder.Pursue(new Vector3(3f, 0f, 0f)));
                int blocked = 0;
                agent.OnTaskBlocked += _ => blocked++;
                Call(agent, "MoveTo", new Vector3(3f, 0f, 0f), 4f);
                Require(Get<bool>(agent, "_moving"), "Initial path must be accepted.");
                agent.transform.position = new Vector3(3f, 0f, 0f);
                Set(agent, "_motorArrived", true);
                Set(agent, "_nextPath", 0f);
                Call(agent, "MoveTo", new Vector3(4f, 0f, 0f), 4f);
                Require(blocked == 0 && Get<Vector3>(agent, "_destination") == new Vector3(4f, 0f, 0f),
                    "Arrival at an old target endpoint must repath, not report blockage.");
            }); passed++;

            Check(agent =>
            {
                var oldTarget = new GameObject("Old target");
                var newTarget = new GameObject("Replacement target");
                try
                {
                    newTarget.transform.position = Vector3.back * 3f;
                    Set(agent, "_cachedTarget", oldTarget.transform);
                    Set(agent, "_playerTarget", newTarget.transform);
                    Set(agent, "_tracking", true);
                    Set(agent, "_visible", true);
                    Set(agent, "_state", SentinelAgent.SentinelState.Chase);
                    Call(agent, "ObservePlayer");
                    Require(!Get<bool>(agent, "_tracking") && agent.State == SentinelAgent.SentinelState.ReturnToPost,
                        "Replacing a target behind the agent must clear its old contact.");
                }
                finally { UnityEngine.Object.DestroyImmediate(oldTarget); UnityEngine.Object.DestroyImmediate(newTarget); }
            }); passed++;

            Check(agent =>
            {
                Set(agent, "_waitUntil", float.MaxValue);
                Set(agent, "_retryAfter", float.MaxValue);
                agent.CancelOrder();
                Require(Get<float>(agent, "_waitUntil") == 0f && Get<float>(agent, "_retryAfter") == 0f,
                    "Cancel must clear old waiting and failure timers.");
            }); passed++;

            Check(agent =>
            {
                var target = new GameObject("Visible close target");
                try
                {
                    target.transform.position = Vector3.forward;
                    agent.AssignOrder(SquadOrder.Pursue(Vector3.forward * 10f));
                    Set(agent, "_playerTarget", target.transform);
                    Set(agent, "_visible", true);
                    Set(agent, "_nextPerception", float.MaxValue);
                    Set(agent, "_nextCommanderLookup", float.MaxValue);
                    Call(agent, "Update");
                    Require(agent.State == SentinelAgent.SentinelState.Attack && !Get<bool>(agent, "_arrivedReported"),
                        "Pursue must allow combat before reaching an obsolete reported destination.");
                }
                finally { UnityEngine.Object.DestroyImmediate(target); }
            }); passed++;

            Check(agent =>
            {
                int deaths = 0;
                agent.OnDied += _ => deaths++;
                agent.TakeDamage(1000f);
                agent.TakeDamage(1000f);
                agent.AssignOrder(SquadOrder.Pursue(Vector3.forward));
                Call(agent, "OnDisable");
                Require(deaths == 1 && agent.CurrentHealth == 0f && !agent.HasOrder &&
                    agent.GetComponent<AgentMotor>().IsMovementLocked, "Death must be reported once and retain the movement lock.");
            }); passed++;

            Check(agent =>
            {
                int arrivals = 0;
                agent.AssignOrder(SquadOrder.HoldPosition(Vector3.zero));
                agent.OnTaskArrived += _ => arrivals++;
                Call(agent, "CompleteOrderArrival");
                Call(agent, "OnDisable");
                Call(agent, "OnEnable");
                Require(agent.State == SentinelAgent.SentinelState.ReturnToPost && arrivals == 1,
                    "Re-enable must resume a completed guard order without restarting or re-reporting it.");
            }); passed++;

            Debug.Log($"SENTINEL_REGRESSION_PASS: {passed} checks passed.");
        }
        finally
        {
            instance.Remove();
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static void Check(Action<SentinelAgent> action)
    {
        var root = new GameObject("Sentinel regression fixture");
        try
        {
            var provider = root.AddComponent<SentinelNavMeshPrototype>();
            var agent = root.AddComponent<SentinelAgent>();
            // Ordinary MonoBehaviours do not execute Awake in Edit Mode.
            Call(provider, "Awake");
            Call(root.GetComponent<AgentMotor>(), "Awake");
            Call(agent, "Awake");
            action(agent);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, PrivateInstance).Invoke(target, args);
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
}
