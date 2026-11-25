using System.Reflection;
using Halazia.AI.Sentinel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch-mode Play Mode probe: Unity -batchmode -executeMethod SentinelPatrolPlayModeProbe.Run.
/// Plays SentinelTestScene without a Commander, samples patrol progress, then exits. Never saves the scene.</summary>
public static class SentinelPatrolPlayModeProbe
{
    private const string ScenePath = "Assets/Scenes/SentinelTestScene.unity";
    private static float _start = -1f, _nextSample;
    private static int _losses, _arrivals, _blocks;
    private static string _mode;

    public static void Run() => Begin("auto");
    public static void RunStartPatrolRoute() => Begin("route");
    public static void RunIssueSelectedPatrol() => Begin("selected");
    /// <summary>Enables TestPlayerTarget's BoxCollider at runtime only and counts navigation warnings.</summary>
    public static void RunTargetCollider() => Begin("collider");
    private static int _navWarnings;

    private static void Begin(string mode)
    {
        _mode = mode;
        Application.logMessageReceived += (message, _, type) =>
        {
            if (type == LogType.Warning && (message.Contains("Obstacle detected") || message.Contains("Cannot reach destination")))
                _navWarnings++;
        };
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // These probes test the Sentinel alone; the Commander integration has its own probe.
        var commander = GameObject.Find("CommanderTest");
        if (commander != null) commander.SetActive(false); // in memory only, never saved
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var go = GameObject.Find("SentinelTest");
        var agent = go != null ? go.GetComponent<SentinelAgent>() : null;
        if (agent == null) return;
        if (_start < 0f)
        {
            _start = Time.time;
            agent.OnTargetLost += _ => _losses++;
            agent.OnTaskArrived += _ => _arrivals++;
            agent.OnTaskBlocked += _ => _blocks++;
            if (_mode == "route")
            {
                var controls = go.GetComponent<SentinelEvaluationControls>();
                if (controls != null) controls.StartPatrolRoute(); else agent.StartPatrolRoute();
            }
            else if (_mode == "selected")
            {
                // Same path as choosing Task = Patrol and pressing Issue Selected Order.
                var controls = go.GetComponent<SentinelEvaluationControls>();
                typeof(SentinelEvaluationControls).GetField("_task", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(controls, Halazia.Shared.Squad.SquadTaskType.Patrol);
                controls.IssueOrder();
            }
            else if (_mode == "collider")
            {
                var targetCollider = GameObject.Find("TestPlayerTarget").GetComponent<BoxCollider>();
                targetCollider.enabled = true;
                Physics.SyncTransforms();
            }
        }
        if (Time.time >= _nextSample)
        {
            _nextSample = Time.time + 1f;
            var visible = typeof(SentinelAgent).GetField("_visible", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(agent);
            var targetGo = GameObject.Find("TestPlayerTarget");
            var health = targetGo != null ? targetGo.GetComponent<SentinelTargetHealth>() : null;
            Debug.Log($"PROBE[{_mode}] t={Time.time - _start:F1} state={agent.State} task={agent.CurrentTask} pos={agent.Position:F2} " +
                      $"visits={agent.PatrolVisits} last={agent.LastPatrolWaypoint} next={agent.NextPatrolWaypointNumber} visible={visible} " +
                      $"losses={_losses} arrivals={_arrivals} blocks={_blocks} navWarnings={_navWarnings} " +
                      $"targetHealth={(health != null ? health.CurrentHealth : -1f)}");
        }
        if (Time.time - _start >= 75f)
        {
            bool pass = agent.PatrolVisits >= 3 && _blocks == 0 && _navWarnings == 0;
            Debug.Log($"PROBE_{(pass ? "PASS" : "FAIL")}[{_mode}] visits={agent.PatrolVisits} losses={_losses} arrivals={_arrivals} " +
                      $"blocks={_blocks} navWarnings={_navWarnings}");
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorApplication.Exit(pass ? 0 : 2);
        }
    }
}
