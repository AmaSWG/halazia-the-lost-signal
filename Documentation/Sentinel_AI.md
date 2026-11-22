# Sentinel AI — completion audit (2026-11-22)

## Status and architecture

The existing SentinelAgent FSM is retained. Navigation remains SentinelNavMeshPrototype -> AgentMotor -> CharacterController. The provider plans complete NavMesh corner paths; only the motor moves the character. The provider's historical name is preserved for serialized compatibility. Weighted A* is not required by any supplied requirement and is not a missing completion dependency.

| Feature | Current implementation | Verification |
| --- | --- | --- |
| Navigation | Complete-path checks, endpoint tolerance, throttled repaths, stuck/blocked reports | Code reviewed; runtime pending |
| Patrol/guard | Ordered route, waits, looping/non-looping, null-route fallback, home return | Existing regression checks reviewed; execution blocked |
| Detection/tracking | FOV, range, solid-wall LOS, remembered visible position, timeout/search/return | New physics regression written; execution blocked |
| Commander | Registration, all eight orders, arrival/block/loss/death events, visible sight reports | New integration checks written; execution blocked |
| Combat/health | LOS/range/facing/cooldown gates, finite positive damage, terminal death | New damage/occlusion checks written; execution blocked |
| Animation | Child combat adapter lookup, death dispatch, independent decision/attack/death locks, recovery pause | Compiles; visual timing pending |
| Escape denial | CoverRoute relocates defense center to choke point; preserves corridor ID; bounded engagement and return | Logic checks added; physical corridor trial pending |
| Reciprocal damage | Optional SentinelTestWeapon raycasts first solid hit to Sentinel health | Compiles; no existing production weapon exists |
| Scene/prefab | Wired primitive test scene and standalone Sentinel prefab saved; animated/player setup builder supplied | Static references checked; Unity import/bake pending |

There are no claims of successful Play Mode testing or passing runtime regressions. Unity 6000.6.5f1 was invoked on 2026-11-22 but exited before project load with code **198**, `No valid Unity Editor license found. Please activate your license.` See `Logs/SentinelRegression-current.log`.

`Tools/Compile-Sentinel.ps1` successfully compiles all Assets/Scripts runtime and Editor C# using the installed Unity compiler/assemblies and installed template Input System/AI Navigation DLLs. Existing obsolete-API, unused-event and Inspector-assignment warnings remain. This fallback does not prove import against the exact resolved package versions, a player build, native physics execution, or animation behavior. Run the actual Unity checks after licensing.

## Changes and scope

- Preserved the pre-existing uncommitted Sentinel implementation, twelve checks, and scene work.
- Sentinel now reports only visible last-known coordinates through Commander's existing ReportPlayerSighted API, including observations made while guarding/under orders.
- Sentinel resolves optional child combat/animation components and calls TriggerDie on lethal damage.
- AgentAnimatorDriver now composes external decision, attack-recovery and death locks. Late completion events cannot resurrect motion, and repeated completion is ignored. Sentinel waits through recovery before starting a path or counting stuck time. This shared-file edit is necessary because the previous animation coroutine could unlock a dead/disabled Sentinel.
- Commander receives three small integration fixes: clear dispatch suppression on a blocked report so the same order can retry, exclude disabled members from allocation, and avoid rediscovering dead/disabled members. Tactical scoring/prediction is unchanged.
- Added SentinelTestWeapon and SentinelEvaluationControls without modifying FirstPersonController, other role AI, shared enemy prefabs, or shared controllers.
- Added SentinelEvaluationSetup to configure the existing fixture, create a Sentinel-only animator from existing clips, save an animated Sentinel prefab, and set up a first-person test target. It must still run in licensed Unity.

Damage is applied once at the attack decision, not by an animation hit event. Do not connect OnAttackHitWindow to a second damage call. The animation recovery may make attacks slower than the cooldown; it never permits faster attacks. Player death exposes SentinelTargetHealth's existing death event; a production game-over/respawn flow remains the player team's integration responsibility.

## Orders and reports

| Order | Behavior |
| --- | --- |
| Idle | Stop; suppress autonomous engagement |
| Patrol | Reach requested position, report, follow configured route; empty route guards |
| HoldPosition / CoverRoute / Flank | Reach destination, report, defend around that destination |
| Retreat | Reach fallback and hold; suppress engagement |
| Investigate / Pursue | Investigate supplied location, search and return; Pursue may engage a visible local target before arrival |

Orders replace prior orders immediately irrespective of numeric priority; Commander owns allocation. Cancel restores the initial home defense center. Disabled agents store orders for re-enable. Dead agents reject orders.

Arrival requires physical proximity and reports once per accepted order. Block reports once per accepted order. Target loss reports once per contact loss, with callback replacement orders preserved. Death clears assignment, reports once and unregisters. CoverRoute stores TargetCorridorId but navigates to TargetPosition; Sentinel does not predict escape routes itself. Local chase/investigation endpoints and corners are constrained to the defense sphere. Direct ordered moves may relocate to a new area.

A single Sentinel does not guarantee Commander will select CoverRoute: its existing allocator prioritizes available high-priority tasks and may choose Pursue. Demonstrate deterministic denial using the fixture's CoverRoute control, then test actual allocation with the full Hunter/Flanker/Sentinel squad and configured corridor markers. Route denial means occupying/defending a route, not magically sealing every exit; the defense radius and corridor width matter.

## Regression inventory

The original twelve checks remain: empty ordered patrol; old chase endpoint repath; target replacement; cancel timers; early Pursue combat; one-shot death/lock; completed-order re-enable; ordered death; Idle re-enable; deferred disabled order; arrival callback replacement; unreachable order reporting.

Six additional integration checks cover: animation/death lock ownership; invalid/valid damage; wall occlusion/cooldown/remembered position; CoverRoute center/leash/cancel; one-shot loss callback replacement; Commander registration/death/removal and blocked-order retry cache.

These are synchronous Editor checks using reflection, an in-memory NavMesh and an unsaved scene. They do not simulate full frames, bake the ship or validate visual animation timing. None executed in this session because licensing failed.

After activating Unity, close the project Editor and run from the repository root:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe' -batchmode -nographics -projectPath (Get-Location).Path -executeMethod SentinelRegressionChecks.Run -quit -logFile 'Logs/SentinelRegression-current.log'
```

Require both `SENTINEL_REGRESSION_PASS: 12 checks passed.` and `SENTINEL_INTEGRATION_PASS: 6 checks passed.`, with no exceptions or compilation errors. The first marker alone is insufficient if an integration check later fails.

See [Sentinel_Test_Scene.md](Sentinel_Test_Scene.md) for the exact evaluation setup and manual checklist.
