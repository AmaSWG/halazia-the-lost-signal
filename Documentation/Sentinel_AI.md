# Sentinel AI: status and verification (updated 2026-11-23)

## Architecture

The existing SentinelAgent FSM is unchanged: `Idle, Patrol, MoveToOrder, Guard, Investigate, Search, Chase, Attack, ReturnToPost, Dead`. Navigation is SentinelNavMeshPrototype (plans complete NavMesh corner paths) -> AgentMotor (the only thing that moves the body) -> CharacterController. No NavMeshAgent and no root motion are used.

Animation runs on the `Sentinel_Robot` child: Animator (`SentinelAnimator.controller`, Generic rig, the model's own Avatar, Apply Root Motion off), AgentAnimatorDriver and CombatActionsAdapter. SentinelAgent reaches them through `ICombatActions` / `AgentAnimatorDriver` found in its children.

## Verification actually executed (2026-11-23)

All runs used Unity 6000.6.5f1 in batch mode on an exact copy of this project's Assets/Packages/ProjectSettings (the project itself was open in the Editor).

| Check | Command | Result |
| --- | --- | --- |
| Compile (runtime + Editor) | `Tools/Compile-Sentinel.ps1` | PASS |
| Regression suite | `SentinelRegressionChecks.Run` | PASS: 13 regression + 15 integration |
| Commander integration, Play Mode | `SentinelCommanderPlayModeProbe.Run` | PASS: 14/14 (register, CoverRoute, HoldPosition, blocked report, cancel, Pursue, target-lost, Investigate, search, no re-issue, patrol resume) |
| Patrol, Play Mode (4 modes) | `SentinelPatrolPlayModeProbe.Run / RunStartPatrolRoute / RunIssueSelectedPatrol / RunTargetCollider` | PASS: 7-10 waypoint visits, 0 blocks, 0 navigation warnings |
| Baked paths incl. corridor nodes | `SentinelTestSceneTools.VerifyPaths` | PASS |
| Animation on the real model | `SentinelAnimationSetupCheck.Run` | PASS: Idle/Walk/Run loop, Attack/TakeDamage/Death play once, all curves bind to Sentinel bones |
| Attack damage sync, Play Mode (manual, by the author) | temporary SentinelAttackHitProbe (since removed) | PASS: 7 hits at the impact event, exactly 15 each (last hit 10 -> 0) |

New checks were also run against the previous code to prove they detect the bugs they guard (disabled-Commander adoption; off-mesh start).

## Combat and damage

- Attack requires a valid visible target within the defense radius, 1.8 m range and 30 degree facing; cooldown 1.2 s; damage 15.
- **Damage is applied at the Attack clip's impact frame**, not at swing start: `Sentinel_Rig|Attack` has an `AnimEvent_AttackHit` event at normalized time 0.5 (frame 15/30, arm fully extended), stored in `Sentinel_Robot.fbx.meta`. AgentAnimatorDriver raises `OnAttackHitWindow`; SentinelAgent applies the pending hit once.
- At impact the hit is re-checked: attacker alive/enabled/still attacking the same order; target active, alive and unchanged; in range; in line of sight; within the facing angle. Any failure cancels the hit (logged as `Attack missed at impact: <reason>` when Debug Logs is on).
- A pending hit is cancelled when the Sentinel takes damage (hit reaction), dies, changes state, is disabled, or the attack ends without an impact event. One swing can damage at most once.
- Without an animator driver (e.g. a primitive prefab) damage is applied immediately at the swing, as before.
- A dead target is no longer valid: the Sentinel logs "Contact lost", reports target loss once, then investigates/searches/returns.

## Commander integration

- The Sentinel registers only with an **active and enabled** CommanderAgent, re-checks every 2 s, and detaches (unregisters, removing the Commander's event handlers) if its Commander is disabled or destroyed.
- Remaining Commander-side gap (teammate-owned, proposal pending approval): CommanderAgent registers `_initialSquadMembers` in `Awake`, which Unity runs even for a disabled component, and never unregisters in `OnDisable`. A Commander that starts disabled with the Sentinel in that list still issues Investigate orders through events. Workaround in test scenes: deactivate the Commander GameObject, not only the component.
- SentinelAgent calls `CommanderAgent.ReportInvestigationComplete`, and the test tools use `IssueDirectOrder`. Both exist only in the uncommitted CommanderAgent changes in this working tree, which must be approved and committed by the Commander owner.

## Orders and reports

| Order | Behavior |
| --- | --- |
| Idle | Stop; suppress autonomous engagement |
| Patrol | Reach requested position, report, follow configured route; empty route guards |
| HoldPosition / CoverRoute / Flank | Reach destination, report, defend around that destination |
| Retreat | Reach fallback and hold; suppress engagement |
| Investigate / Pursue | Investigate supplied location, search and return; Pursue may engage a visible local target before arrival |

Orders replace prior orders immediately; the Commander owns allocation. Cancel restores the home defense center. Disabled agents keep orders for re-enable; dead agents reject them. Arrival and block are each reported once per accepted order; target loss once per contact loss; death once (then unregister). CoverRoute keeps TargetCorridorId and navigates to TargetPosition. With several Sentinels the allocator gives distinct tasks (verified with three: Pursue, a secondary task, and a CoverRoute fallback), and a blocked Sentinel waits its retry delay while the others are re-allocated.

## Navigation robustness

The path provider now finds the agent's own position on the NavMesh within 0.75 m (`_startSampleDistance`), while destinations keep the strict 0.1 m tolerance. Previously a Sentinel resting a few centimetres off the eroded NavMesh edge (corner cutting; CharacterController radius equals the agent radius) could never path again. This was reproduced in the Commander Play Mode probe and is now covered by a regression check.

## Regression inventory

13 regression checks: empty ordered patrol; old chase endpoint repath; target replacement; cancel timers; early Pursue combat; one-shot death/lock; completed-order re-enable; ordered death; Idle re-enable; deferred disabled order; arrival callback replacement; unreachable order reporting; off-mesh start recovery.

15 integration checks: animation/death lock ownership; invalid/valid damage; wall occlusion, cooldown and remembered position; CoverRoute center/leash/cancel; one-shot loss with replacement order; Commander registration/death/blocked retry cache; body bypass; Commander direct orders; Investigate lifecycle; impact-frame damage applied once; impact re-checks and interrupt cancellation (range, wall, facing, damage taken, state change, target replaced, attack ended); dead target ends attack; moving target re-path; disabled-Commander lifecycle; three-Sentinel allocation and blocked retry.

These are synchronous Editor checks (reflection, in-memory NavMesh, unsaved scene). The Play Mode probes above cover real frames.

## How to run

Batch mode needs the project closed in the Editor (or run against a copy of Assets/Packages/ProjectSettings):

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.5f1/Editor/Unity.exe'
& $unity -batchmode -nographics -projectPath (Get-Location).Path -executeMethod SentinelRegressionChecks.Run -quit -logFile 'Logs/SentinelRegression-current.log'
& $unity -batchmode -projectPath (Get-Location).Path -executeMethod SentinelCommanderPlayModeProbe.Run -logFile 'Logs/SentinelCommanderProbe.log'
& $unity -batchmode -projectPath (Get-Location).Path -executeMethod SentinelPatrolPlayModeProbe.Run -logFile 'Logs/SentinelPatrolProbe.log'
```

Require `SENTINEL_REGRESSION_PASS: 13 checks passed.`, `SENTINEL_INTEGRATION_PASS: 15 checks passed.`, `PROBE_PASS` from each probe, and no exceptions.

## Known gaps

- `Assets/Prefabs/Sentinel.prefab` is still the primitive version (capsule Body, no robot model, Animator, driver or adapter). The test scene's Sentinel is animated; the prefab is not. Regenerating it needs approval.
- **Prepare Evaluation Scene and Prefab** is out of date: it looks for a `SentinelVisual` Y Bot child, so on the current scene it would add a second visual and driver, replace the target's collider and save the scene. Do not run it until it is updated.
- The saved NavMesh (`SentinelNavMesh 1.asset`) was baked while ObstacleWall was active; the wall is inactive in the saved scene, so paths avoid a wall that is not there. Either re-activate the wall or re-bake. `SentinelNavMesh.asset` is an unreferenced earlier bake.
- Multiple Sentinel prefab instances share the Agent ID `Sentinel_Test` unless each instance is given a unique ID (logs only; references are by object).
- Ship decks/stairs/doors, the production player weapon/health and game-over flow are outside this fixture.

See [Sentinel_Test_Scene.md](Sentinel_Test_Scene.md) for the evaluation scene and manual checklist.
