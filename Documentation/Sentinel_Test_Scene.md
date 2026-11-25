# Sentinel evaluation scene and manual checks

## What is already saved

`Assets/Scenes/SentinelTestScene.unity` (state as of 2026-11-23) contains the ground/camera/light, SentinelTest, a stationary TestPlayerTarget with health at `(2, 0, 6)` (inside the 12 m defense radius around GuardPosition `(0, 0, -4)`), a guard marker, three patrol points and two corridor nodes (`Corridor_TestExitA/B`). SentinelAgent's provider, target, guard and patrol references are assigned. AgentMotor and CharacterController are on the same root; no NavMeshAgent is used. Characters are excluded from NavMesh baking by NavMeshModifier.

The visible Sentinel is the `Sentinel_Robot` model (child of SentinelTest) with Animator (`SentinelAnimator.controller`), AgentAnimatorDriver and CombatActionsAdapter; the primitive Body is inactive. The Attack clip carries an `AnimEvent_AttackHit` event at its impact frame.

For Sentinel-only testing, the `CommanderTest` GameObject and `CommanderSentinelTest` are **inactive** and `ObstacleWall` is inactive. **Add Commander Integration** re-activates the existing Commander objects (it no longer creates duplicates). The saved NavMesh was baked while ObstacleWall was active; see Remaining limitations.

SentinelEvaluationControls is attached to SentinelTest, with report counters and Inspector context-menu actions. `Assets/Prefabs/Sentinel.prefab` is still the standalone **primitive** Sentinel (no robot model or animation components) with no scene references.

## Required preparation in Unity

1. Open this project in **6000.6.5f1** and resolve any Console errors.
2. Save your work and open **only** `Assets/Scenes/SentinelTestScene.unity`. Stop Play Mode.
3. **Do not run Prepare Evaluation Scene and Prefab** in its current form. It predates the Sentinel_Robot model: it looks for a `SentinelVisual` Y Bot child, so it would add a second visual and driver, replace the target's box collider, reposition walls and save the scene.
4. Inspect SentinelTest: provider assigned, target=TestPlayerTarget, guard=GuardPosition, three patrol references, controller height=2/radius=0.5/center=(0,1,0), motor max speed=4.2, arrival tolerance=0.6. Inspect Sentinel_Robot: SentinelAnimator controller, the model's own Avatar, root motion off, driver motor reference = SentinelTest.
5. After any geometry change (including activating or deactivating ObstacleWall) run **Tools > Halazia > Sentinel Test > Bake and Verify NavMesh** and require `SENTINEL_SCENE_BAKE_PASS`; **Verify Baked Paths** must report `SENTINEL_SCENE_PATHS_PASS` (it also checks the corridor nodes). The tool saves a new bake asset each time; the previous one becomes unreferenced.
6. Run the batch regressions and probes described in Sentinel_AI.md with the project Editor closed. Require **13 regression + 15 integration checks** and `PROBE_PASS`.

## Manual Play Mode acceptance checklist

Use Scene view with Gizmos to observe Sentinel. Game view uses WASD/mouse and left click to fire a 25-damage test shot at screen center. Escape releases the cursor. The optional test weapon has no HUD/crosshair. Use **Log Play Mode Status** for health/state. Stop/restart to reset health. One missing-Commander warning is expected in the isolated fixture.

- [ ] **Patrol:** deactivate TestPlayerTarget temporarily; observe all three points, 2 s waits, loop and return home when Loop Patrol is off. Test empty/all-null route safely guarding. Restore the target and route.
- [ ] **Navigation:** observe turns around the wall/opening without straight-line clipping. Test actual ship stairs/decks/doors separately. Disable the provider or order `(100,0,0)` using the destination marker; expect one blocked report, no false arrival. Restore and issue a fresh reachable order.
- [ ] **Detection:** approach from front, behind and beyond 15 m. Solid walls must block sight and damage; triggers should not. Test separate decks in the ship scene. Confirm the ray intersects the wall before judging occlusion.
- [ ] **Tracking:** break LOS and move while hidden. LastKnownPlayerPosition must stay at the last visible point; investigate, search about 5 s, return. One loss report after 1.5 s; reacquisition permits a new loss report.
- [ ] **Combat:** enter the defense area and attack range (1.8 m). Target loses 15 per attack at the arm's full extension (Attack clip frame 15), no faster than 1.2 s and never during attack recovery; the 100-health target dies on the 7th hit and the Sentinel then logs "Contact lost". Moving out of range, behind a wall or outside the facing angle during the wind-up cancels that hit. Observe attack, hit, locomotion and death clips.
- [ ] **Movement locks:** leave range or issue an order mid-swing. No translation until recovery completes. Disable Sentinel mid-attack; it remains stopped. Kill it mid-attack; wait beyond recovery and confirm late events never unlock it or restart movement.
- [ ] **Player weapon/health:** aim center-screen at Sentinel and fire four 25-damage hits. Confirm hit reactions, zero health, one death report, no later attacks/orders and no revival after disable/re-enable. Shoot a wall in front of Sentinel; no damage should pass through. Dead test player cannot fire. Game-over/respawn UI is outside this fixture.
- [ ] **All eight orders:** on SentinelEvaluationControls select Task and use its component context menu **Issue Selected Order (Play Mode)**. Move SentinelOrderPosition to a reachable point. Check Idle, Patrol, Investigate, Pursue, Flank, HoldPosition, CoverRoute and Retreat. Arrival fires once; Idle/Retreat suppress attacks; cancel returns to the home post. Report counters remain visible in the Inspector.
- [ ] **Choke-point/escape denial:** issue CoverRoute at `(5.5,0,0.5)`. Verify movement through the baked opening, one arrival, local engagement and return to that post. Lead the player beyond the 12 m defense radius; Sentinel must not pursue indefinitely. For a strict doorway guard, lower Defense Radius before play and validate that paths stay inside it. Check the player cannot exploit collider gaps; one agent is not a guarantee that every route is sealed.
- [ ] **Commander integration:** use a separate saved copy or the team's integration scene. Assign Commander player reference and corridor/heatmap markers. Add the complete squad to test automatic CoverRoute allocation; with only Sentinel, Pursue may win. Confirm register once, visible sight reports, order interruption, arrival/block/loss/death reports, retry after a blockage and removal on death. Do not attach Commander to Sentinel's root. For isolated Sentinel tests deactivate the Commander **GameObject**: disabling only the CommanderAgent component still lets its Awake-time registration issue Investigate orders (Commander-side fix pending owner approval).
- [ ] **Production scene/prefab:** place Sentinel prefab instances, assign unique Agent IDs, player, guard and patrol transforms, and bake each intended navigation area. Check no competing AI/movement components, correct obstacle masks and no root motion. Scene targets cannot be stored in the prefab asset.
- [ ] Save the test setup outside Play Mode and record results/screenshots/video for the evaluation. Mark a check passed only after observing it.

## Remaining limitations

The automated checks and Play Mode probes listed in Sentinel_AI.md were executed on 2026-11-23 and passed; the manual checklist above still needs human observation. Known configuration gaps: the saved NavMesh still contains ObstacleWall's footprint although the wall is inactive (re-activate the wall or re-bake); `SentinelNavMesh.asset` is an unreferenced earlier bake; the Sentinel prefab is not yet animated. Existing player game-over/respawn and a production weapon system are not present; the supplied weapon is a deliberately isolated evaluation component. Commander route prediction is reused, not reimplemented by Sentinel.
