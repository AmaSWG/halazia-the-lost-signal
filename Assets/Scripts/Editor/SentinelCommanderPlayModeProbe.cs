using System.Collections.Generic;
using System.Reflection;
using Halazia.AI.Commander;
using Halazia.AI.Sentinel;
using Halazia.Shared.Squad;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch Play Mode integration probe:
/// Unity -batchmode -projectPath . -executeMethod SentinelCommanderPlayModeProbe.Run -logFile out.log
/// Configures Commander integration in memory (never saves), then runs timed phases and exits 0 on pass.</summary>
public static class SentinelCommanderPlayModeProbe
{
    private enum Phase { Autonomous, CoverRoute, HoldPosition, Blocked, Cancel, Combat, AfterCombat, Reacquire, Done }
    private static Phase _phase;
    private static float _phaseStart = -1f, _nextSample, _patrolSince = -1f;
    private static int _cannotReach, _commanderDispatches, _arrivalsAtPhaseStart, _blocksAtPhaseStart, _lossesAtPhaseStart;
    private static int _investigateDispatches, _investigateCompletions, _investigateDispatchesAfterCompletion;
    private static int _patrolVisitsBeforeInvestigation;
    private static bool _patrolBaselineSet;
    private static readonly List<string> Results = new List<string>();
    private static bool _failed, _sawPursue, _sawInvestigateAfterLoss, _sawSearch;
    // From the combat target loss on: Investigate orders the Sentinel received, Pursue dispatched before the
    // investigation completed, Search dwells held under the Investigate order, and Pursue after reacquisition.
    private static bool _lossSeen, _prevSearching;
    private static SquadTaskType _prevTask = SquadTaskType.Idle;
    private static int _investigateOrders, _pursueBeforeCompletion, _searchDwells, _reacquirePursue;
    private static float _searchStartedAt = -1f, _searchSeconds = -1f;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SentinelTestScene.unity", OpenSceneMode.Single);
        SentinelCommanderTestSetup.Configure(save: false);
        Application.logMessageReceived += (message, _, type) =>
        {
            if (message.Contains("Cannot reach destination")) _cannotReach++;
            if (message.StartsWith("[CommanderAgent] Dispatched order")) _commanderDispatches++;
            if (message.StartsWith("[CommanderAgent] Dispatched order [Pursue]")) _sawPursue = true;
            if (_phase == Phase.Combat && message.Contains("lost sight of target")) { _sawInvestigateAfterLoss = true; _lossSeen = true; }
            bool afterLoss = _lossSeen && (_phase == Phase.Combat || _phase == Phase.AfterCombat);
            // Investigate dispatched by automatic allocation after the loss = a re-issued or restarted investigation.
            if (afterLoss && message.StartsWith("[CommanderAgent] Dispatched order [Investigate]"))
            {
                _investigateDispatches++;
                if (_investigateCompletions > 0) _investigateDispatchesAfterCompletion++;
            }
            if (afterLoss && _investigateCompletions == 0 && message.StartsWith("[CommanderAgent] Dispatched order [Pursue]"))
                _pursueBeforeCompletion++;
            if (afterLoss && message.Contains("completed Investigate")) _investigateCompletions++;
            if (_phase == Phase.Reacquire && message.StartsWith("[CommanderAgent] Dispatched order [Pursue]")) _reacquirePursue++;
        };
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var harness = Object.FindAnyObjectByType<SentinelCommanderTestControls>();
        var sentinel = GameObject.Find("SentinelTest")?.GetComponent<SentinelAgent>();
        var commander = Object.FindAnyObjectByType<CommanderAgent>();
        var target = GameObject.Find("TestPlayerTarget");
        if (harness == null || sentinel == null || commander == null) return;
        float t = Time.time;
        if (_phaseStart < 0f)
        {
            // Keep the target out of every agent's sight until the combat phase.
            if (target != null) target.transform.position = new Vector3(20f, 0f, -15f);
            Physics.SyncTransforms();
            Begin(Phase.Autonomous, harness);
        }
        float elapsed = t - _phaseStart;
        if (_lossSeen && (_phase == Phase.Combat || _phase == Phase.AfterCombat))
        {
            // Per-frame order/state tracking from the target loss until patrol resumes.
            var task = sentinel.CurrentTask;
            bool searching = sentinel.State == SentinelAgent.SentinelState.Search && task == SquadTaskType.Investigate;
            if (task == SquadTaskType.Investigate && _prevTask != SquadTaskType.Investigate) _investigateOrders++;
            if (searching && !_prevSearching) { _searchDwells++; _searchStartedAt = t; }
            if (!searching && _prevSearching && _searchSeconds < 0f) _searchSeconds = t - _searchStartedAt;
            _prevTask = task;
            _prevSearching = searching;
        }
        if (t >= _nextSample)
        {
            _nextSample = t + 1f;
            Debug.Log($"PROBE[{_phase}] t={elapsed:F1} registered={harness.IsRegistered} task={sentinel.CurrentTask} " +
                      $"state={sentinel.State} pos={sentinel.Position:F1} arrivals={harness.ArrivedReports} " +
                      $"blocks={harness.BlockedReports} losses={harness.TargetLostReports} cannotReach={_cannotReach} " +
                      $"dispatches={_commanderDispatches} direct={commander.HasDirectOrder(sentinel)}");
        }

        switch (_phase)
        {
            case Phase.Autonomous:
                if (elapsed < 8f) return;
                Check(harness.IsRegistered, "Sentinel registered with Commander");
                Check(_cannotReach == 0, $"No blocked-path spam during autonomous allocation (cannotReach={_cannotReach})");
                harness.CommanderCoverRoute();
                Begin(Phase.CoverRoute, harness);
                break;
            case Phase.CoverRoute:
                if (harness.ArrivedReports > _arrivalsAtPhaseStart && sentinel.State == SentinelAgent.SentinelState.Guard)
                {
                    Check(sentinel.CurrentTask == SquadTaskType.CoverRoute, "CoverRoute executed and arrival reported");
                    if (elapsed < 4f) return; // stays on guard; Commander must not override
                    Check(sentinel.CurrentTask == SquadTaskType.CoverRoute, "Commander kept CoverRoute (no override)");
                    harness.CommanderHoldPosition();
                    Begin(Phase.HoldPosition, harness);
                }
                else if (elapsed > 25f) Fail("CoverRoute arrival not reported within 25s", harness);
                break;
            case Phase.HoldPosition:
                if (harness.ArrivedReports > _arrivalsAtPhaseStart && sentinel.State == SentinelAgent.SentinelState.Guard)
                {
                    Check(sentinel.CurrentTask == SquadTaskType.HoldPosition, $"HoldPosition executed and arrival reported in {elapsed:F1}s");
                    harness.CommanderBlockedPath();
                    Begin(Phase.Blocked, harness);
                }
                else if (elapsed > 25f) Fail("HoldPosition arrival not reported within 25s", harness);
                break;
            case Phase.Blocked:
                if (harness.BlockedReports > _blocksAtPhaseStart)
                {
                    Check(!commander.HasDirectOrder(sentinel), "Blocked report reached Commander (direct order released)");
                    harness.CancelSentinelOrder();
                    Begin(Phase.Cancel, harness);
                }
                else if (elapsed > 10f) Fail("Blocked path not reported within 10s", harness);
                break;
            case Phase.Cancel:
                if (elapsed < 0.2f) return;
                Check(!sentinel.HasOrder || sentinel.CurrentTask != SquadTaskType.HoldPosition,
                    $"Cancel cleared the Sentinel order (task now {sentinel.CurrentTask})");
                // Combat: bring the target into the Sentinel's view and let Commander allocate automatically.
                if (target != null) { target.SetActive(true); target.transform.position = sentinel.Position + sentinel.transform.forward * 5f; }
                Physics.SyncTransforms();
                Begin(Phase.Combat, harness);
                break;
            case Phase.Combat:
                // Count only losses in this phase; autonomous allocation may already have reported one.
                if (harness.TargetLostReports > _lossesAtPhaseStart && elapsed > 3f)
                {
                    Check(_sawPursue, "Commander dispatched Pursue after a squad sighting");
                    Check(_sawInvestigateAfterLoss, "Commander received target-lost report");
                    // Keep the player out of sight so the Commander's combat window expires.
                    if (target != null) target.transform.position = new Vector3(20f, 0f, -15f);
                    Physics.SyncTransforms();
                    Begin(Phase.AfterCombat, harness);
                }
                else if (elapsed > 40f) Fail("No target-lost report within 40s of combat", harness);
                break;
            case Phase.AfterCombat:
                if (sentinel.State == SentinelAgent.SentinelState.Search && sentinel.CurrentTask == SquadTaskType.Investigate)
                {
                    _sawSearch = true;
                    if (!_patrolBaselineSet)
                    {
                        _patrolVisitsBeforeInvestigation = sentinel.PatrolVisits;
                        _patrolBaselineSet = true;
                    }
                }
                bool patrolling = sentinel.CurrentTask == SquadTaskType.Patrol &&
                    sentinel.State == SentinelAgent.SentinelState.Patrol &&
                    sentinel.PatrolVisits > _patrolVisitsBeforeInvestigation;
                if (_investigateCompletions > 0 && patrolling)
                {
                    if (_patrolSince < 0f) _patrolSince = t;
                    if (t - _patrolSince < 3f) return; // must stay released, not be re-ordered
                    Check(_investigateOrders == 1 && _investigateDispatches == 0,
                        $"Exactly one Investigate order after target loss (orders={_investigateOrders}, re-dispatched={_investigateDispatches})");
                    Check(_pursueBeforeCompletion == 0,
                        $"No Pursue replaced the Investigate while the player was out of sight (Pursue dispatches={_pursueBeforeCompletion})");
                    Check(_sawSearch, "Sentinel searched while holding the Investigate order");
                    float dwell = SearchDuration(sentinel);
                    Check(_searchDwells == 1 && _searchSeconds >= dwell - 0.25f,
                        $"Exactly one complete Search dwell (dwells={_searchDwells}, {_searchSeconds:F1}s of {dwell:F1}s)");
                    Check(_investigateCompletions == 1, $"Commander completed Investigate once ({_investigateCompletions})");
                    Check(_investigateDispatchesAfterCompletion == 0, "Commander did not re-issue the completed Investigate");
                    Check(sentinel.CurrentTask == SquadTaskType.Patrol && sentinel.State == SentinelAgent.SentinelState.Patrol &&
                        sentinel.PatrolVisits > _patrolVisitsBeforeInvestigation, "Sentinel resumed its patrol route after search");
                    // Reacquisition: restore the target's health and put it in front of the patrolling Sentinel.
                    if (target != null)
                    {
                        target.SetActive(true);
                        var health = target.GetComponent<SentinelTargetHealth>();
                        if (health != null)
                            typeof(SentinelTargetHealth).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(health, null);
                        target.transform.position = sentinel.Position + sentinel.transform.forward * 5f;
                    }
                    Physics.SyncTransforms();
                    Begin(Phase.Reacquire, harness);
                }
                else if (elapsed > 60f) Fail($"Sentinel did not resume patrol after Investigate within 60s " +
                    $"(completions={_investigateCompletions}, task={sentinel.CurrentTask}, state={sentinel.State})", harness);
                break;
            case Phase.Reacquire:
                if (_reacquirePursue > 0 && sentinel.CurrentTask == SquadTaskType.Pursue && sentinel.IsEngagedWithPlayer)
                {
                    Check(true, $"Commander dispatched Pursue when the player was reacquired ({elapsed:F1}s)");
                    Check(true, $"Sentinel engaged the reacquired player (state {sentinel.State})");
                    Finish(harness);
                }
                else if (elapsed > 15f) Fail($"Player reacquisition did not produce Pursue and engagement within 15s " +
                    $"(Pursue dispatches={_reacquirePursue}, task={sentinel.CurrentTask}, state={sentinel.State})", harness);
                break;
        }
    }

    private static float SearchDuration(SentinelAgent sentinel) =>
        (float)typeof(SentinelAgent).GetField("_searchDuration", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sentinel);

    private static void Begin(Phase phase, SentinelCommanderTestControls harness)
    {
        _phase = phase;
        _phaseStart = Time.time;
        _arrivalsAtPhaseStart = harness.ArrivedReports;
        _blocksAtPhaseStart = harness.BlockedReports;
        _lossesAtPhaseStart = harness.TargetLostReports;
        Debug.Log($"PROBE_PHASE {phase}");
    }

    private static void Check(bool ok, string what)
    {
        string line = (ok ? "PASS: " : "FAIL: ") + what;
        if (!Results.Contains(line)) { Results.Add(line); Debug.Log("PROBE_CHECK " + line); }
        _failed |= !ok;
    }

    private static void Fail(string what, SentinelCommanderTestControls harness) { Check(false, what); Finish(harness); }

    private static void Finish(SentinelCommanderTestControls harness)
    {
        _phase = Phase.Done;
        Debug.Log((_failed ? "PROBE_FAIL" : "PROBE_PASS") + "\n" + string.Join("\n", Results));
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
        EditorApplication.Exit(_failed ? 2 : 0);
    }
}
