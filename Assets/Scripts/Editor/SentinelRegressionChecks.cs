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

            Check(agent =>
            {
                agent.AssignOrder(SquadOrder.HoldPosition(Vector3.forward * 4f));
                int deaths = 0;
                agent.OnDied += dying => { deaths++; Require(!agent.HasOrder, "Death callbacks must see a cleared order."); };
                agent.TakeDamage(1000f);
                Require(deaths == 1 && agent.Behavior == SentinelAgent.BehaviorState.Dead,
                    "An ordered Sentinel must enter Dead and clear its assignment.");
            }); passed++;

            Check(agent =>
            {
                agent.AssignOrder(SquadOrder.Idle());
                Call(agent, "OnDisable");
                Require(agent.GetComponent<AgentMotor>().IsMovementLocked, "Disable must lock motor translation.");
                Call(agent, "OnEnable");
                Require(agent.State == SentinelAgent.SentinelState.Idle, "Idle must remain Idle on re-enable.");
            }); passed++;

            Check(agent =>
            {
                agent.enabled = false;
                Call(agent, "OnDisable");
                agent.AssignOrder(SquadOrder.HoldPosition(Vector3.forward * 4f));
                Require(agent.HasOrder && agent.Behavior == SentinelAgent.BehaviorState.Disabled &&
                    agent.GetComponent<AgentMotor>().IsMovementLocked, "Disabled agents must defer order execution.");
                agent.enabled = true;
                Call(agent, "OnEnable");
                Require(agent.State == SentinelAgent.SentinelState.MoveToOrder, "Re-enable must resume the queued order.");
            }); passed++;

            Check(agent =>
            {
                int arrivals = 0;
                agent.AssignOrder(SquadOrder.CoverRoute(Vector3.zero, "exit"));
                agent.OnTaskArrived += _ => { arrivals++; agent.AssignOrder(SquadOrder.Retreat(Vector3.forward * 5f)); };
                Call(agent, "CompleteOrderArrival");
                Require(arrivals == 1 && agent.CurrentTask == SquadTaskType.Retreat &&
                    agent.State == SentinelAgent.SentinelState.MoveToOrder, "Arrival callbacks must preserve replacement orders.");
            }); passed++;

            Check(agent =>
            {
                int blocked = 0;
                agent.AssignOrder(SquadOrder.HoldPosition(Vector3.forward * 100f));
                agent.OnTaskBlocked += _ => blocked++;
                Call(agent, "MoveTo", Vector3.forward * 100f, 4f);
                Call(agent, "MoveTo", Vector3.forward * 100f, 4f);
                Require(blocked == 1 && !Get<bool>(agent, "_arrivedReported"), "Unreachable orders report blockage once, never arrival.");
            }); passed++;

            Debug.Log($"SENTINEL_REGRESSION_PASS: {passed} checks passed.");
            RunIntegrationChecks();
        }
        finally
        {
            instance.Remove();
            UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static void RunIntegrationChecks()
    {
        Check(agent =>
        {
            var driver = agent.gameObject.AddComponent<AgentAnimatorDriver>();
            Call(driver, "Awake");
            Set(agent, "_animation", driver);
            driver.SetExternalMovementLock(true);
            typeof(AgentAnimatorDriver).GetProperty("IsAttacking").GetSetMethod(true).Invoke(driver, new object[] { true });
            driver.AnimEvent_AttackComplete();
            Require(agent.GetComponent<AgentMotor>().IsMovementLocked, "Attack completion cannot clear a decision lock.");
            agent.TakeDamage(1000f);
            driver.AnimEvent_AttackComplete();
            driver.SetExternalMovementLock(false);
            Require(Get<bool>(driver, "_dead") && agent.GetComponent<AgentMotor>().IsMovementLocked,
                "Sentinel death must reach animation and remain locked after late callbacks.");
        });
        Check(agent =>
        {
            agent.TakeDamage(float.NaN); agent.TakeDamage(-10); agent.TakeDamage(float.PositiveInfinity);
            Require(agent.CurrentHealth == agent.MaxHealth, "Invalid damage must be ignored.");
            agent.TakeDamage(25f);
            Require(agent.CurrentHealth == agent.MaxHealth - 25f, "Finite damage must reduce health exactly once.");
        });
        Check(agent =>
        {
            var target = new GameObject("Health target");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var health = target.AddComponent<SentinelTargetHealth>(); Call(health, "Awake");
                target.transform.position = Vector3.forward * 1.5f;
                Set(agent, "_playerTarget", target.transform);
                wall.transform.position = new Vector3(0, 1, 0.75f);
                wall.transform.localScale = new Vector3(4, 3, 0.2f);
                Physics.SyncTransforms(); Call(agent, "ObservePlayer");
                Require(!Get<bool>(agent, "_visible"), "Wall must block detection.");
                float before = health.CurrentHealth;
                Call(agent, "TickAttack");
                Require(health.CurrentHealth == before, "Wall must block damage.");
                wall.SetActive(false); Physics.SyncTransforms(); Call(agent, "ObservePlayer");
                Require(Get<bool>(agent, "_visible"), "Front target must be acquired without the wall.");
                Call(agent, "TickAttack"); Call(agent, "TickAttack");
                Require(health.CurrentHealth == before - 15f, "Attack cooldown must prevent a duplicate hit.");
                target.transform.position = Vector3.back * 3f; Call(agent, "ObservePlayer");
                Require(!Get<bool>(agent, "_visible") && agent.LastKnownPlayerPosition == Vector3.forward * 1.5f,
                    "Hidden target must not update last-known coordinates.");
            }
            finally { UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(wall); }
        });
        Check(agent =>
        {
            Vector3 exit = Vector3.right * 5f;
            agent.AssignOrder(SquadOrder.CoverRoute(exit, "exit"));
            Require(Get<Vector3>(agent, "_post") == exit && agent.CurrentOrder.TargetCorridorId == "exit",
                "CoverRoute must establish the ordered choke point and preserve route identity.");
            agent.transform.position = exit; Call(agent, "CompleteOrderArrival");
            Require(agent.State == SentinelAgent.SentinelState.Guard, "CoverRoute must guard after arrival.");
            Require(!(bool)Call(agent, "WithinDefense", exit + Vector3.right * 13f), "Defense must remain bounded around the exit.");
            agent.CancelOrder();
            Require(Get<Vector3>(agent, "_post") == Vector3.zero, "Cancel must restore home defense.");
        });
        Check(agent =>
        {
            int losses = 0;
            agent.OnTargetLost += _ => { losses++; agent.AssignOrder(SquadOrder.Retreat(Vector3.right * 4f)); };
            Set(agent, "_tracking", true); Set(agent, "_visible", false);
            Set(agent, "_nextPerception", float.MaxValue); Set(agent, "_nextCommanderLookup", float.MaxValue);
            Set(agent, "_lastSeen", -100f);
            Call(agent, "Update"); Call(agent, "Update");
            Require(losses == 1 && agent.CurrentTask == SquadTaskType.Retreat,
                "Loss must report once and retain a synchronous replacement order.");
        });
        Check(agent =>
        {
            var root = new GameObject("Commander integration fixture");
            try
            {
                var commander = root.AddComponent<Halazia.AI.Commander.CommanderAgent>();
                Call(root.GetComponent<AgentMotor>(), "Awake"); Call(commander, "Awake");
                Call(agent, "OnEnable"); Call(agent, "OnDisable"); Call(agent, "OnEnable");
                var roster = Get<List<ISquadAgent>>(commander, "_registeredSquad");
                Require(roster.Count == 1, "Enable cycles must not duplicate Commander registration.");
                var dispatched = Get<Dictionary<ISquadAgent, Vector3>>(commander, "_lastDispatchedPositions");
                dispatched[agent] = Vector3.forward;
                Call(commander, "OnAgentTaskBlocked", agent);
                Require(!dispatched.ContainsKey(agent), "Blocked orders must invalidate dispatch suppression so the same destination can retry.");
                agent.TakeDamage(1000f);
                Require(roster.Count == 0, "Death must remove Sentinel from Commander roster.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        });
        Check(agent =>
        {
            // Characters are not baked into the NavMesh; a body on the straight path must be bypassed.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                body.transform.position = new Vector3(0f, 1f, 3f);
                body.transform.localScale = new Vector3(0.8f, 2f, 0.8f);
                wide.transform.position = new Vector3(10f, 1f, 10f);
                wide.transform.localScale = new Vector3(6f, 2f, 1f);
                Physics.SyncTransforms();
                var bypass = typeof(SentinelAgent).GetMethod("TryBypassContact", PrivateInstance);
                bool Bypass(Collider contact, Vector3 destination, out bool reached)
                {
                    Set(agent, "_moving", true);
                    Set(agent, "_contact", contact);
                    Set(agent, "_contactTime", Time.time);
                    var args = new object[] { destination, 2f, false };
                    bool result = (bool)bypass.Invoke(agent, args);
                    reached = (bool)args[2];
                    return result;
                }
                Require(Bypass(body.GetComponent<Collider>(), Vector3.forward * 6f, out bool reachedPast) && !reachedPast &&
                    Get<bool>(agent, "_moving") && Get<Vector3>(agent, "_destination") == Vector3.forward * 6f,
                    "A small body on the path must produce a detour to the same destination.");
                Require(Bypass(body.GetComponent<Collider>(), Vector3.forward * 3f, out bool reachedOccupied) && reachedOccupied,
                    "A destination occupied by a body must count as reached against it.");
                Require(!Bypass(wide.GetComponent<Collider>(), new Vector3(10f, 0f, 12f), out _),
                    "Large contacts must not be bypassed; they remain blocked navigation.");
            }
            finally { UnityEngine.Object.DestroyImmediate(body); UnityEngine.Object.DestroyImmediate(wide); }
        });
        Check(agent =>
        {
            var root = new GameObject("Commander order fixture");
            try
            {
                var commander = root.AddComponent<Halazia.AI.Commander.CommanderAgent>();
                Call(root.GetComponent<AgentMotor>(), "Awake"); Call(commander, "Awake");
                Call(agent, "OnEnable");
                Call(commander, "RunTacticalDecisionCycle");
                Require(!agent.HasOrder, "Commander must not order Investigate before any player sighting.");

                Require(commander.IssueDirectOrder(agent, SquadOrder.HoldPosition(Vector3.forward * 4f), 60f) &&
                    agent.CurrentTask == SquadTaskType.HoldPosition, "Commander direct HoldPosition must reach the Sentinel.");
                commander.ReportPlayerSighted(Vector3.right * 3f);
                Call(commander, "RunTacticalDecisionCycle");
                Require(agent.CurrentTask == SquadTaskType.HoldPosition, "Automatic allocation must not override a direct order.");
                commander.ReleaseDirectOrder(agent);
                Call(commander, "RunTacticalDecisionCycle");
                Require(agent.CurrentTask == SquadTaskType.Pursue, "Released agents must return to automatic allocation.");

                Require(commander.IssueDirectOrder(agent, SquadOrder.CoverRoute(Vector3.forward * 4f, "exit"), 60f) &&
                    agent.CurrentTask == SquadTaskType.CoverRoute && agent.CurrentOrder.TargetCorridorId == "exit",
                    "Commander direct CoverRoute must reach the Sentinel with its corridor id.");
                Call(commander, "OnAgentTaskBlocked", agent);
                Require(!commander.HasDirectOrder(agent), "A blocked report must end the direct order.");
                Call(commander, "RunTacticalDecisionCycle");
                Require(agent.CurrentTask == SquadTaskType.CoverRoute, "A blocked agent must not be re-dispatched immediately.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        });
        Debug.Log("SENTINEL_INTEGRATION_PASS: 8 checks passed.");
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
