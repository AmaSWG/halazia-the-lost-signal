# Sentinel evaluation scene and manual checks

## What is already saved

`Assets/Scenes/SentinelTestScene.unity` contains the existing ground/camera/light, a full-height occlusion wall, SentinelTest, stationary TestPlayerTarget with health, a guard marker and three patrol points. SentinelAgent's provider, target, guard and patrol references are assigned. AgentMotor and CharacterController are on the same root; no NavMeshAgent is used. Characters are excluded from NavMesh baking by NavMeshModifier.

SentinelEvaluationControls is attached to SentinelTest, with report counters and Inspector context-menu actions. Until the setup command runs, its order destination is PatrolWaypoint3. `Assets/Prefabs/Sentinel.prefab` is a standalone primitive Sentinel with motor/controller/provider and no scene references. Its player/guard/route must be assigned per instance. No generated animated controller or baked NavMesh is claimed to exist yet.

## Required preparation in Unity

1. Activate a valid Unity Editor license in Hub. Open this project in **6000.6.5f1**, allow package/script import, and resolve any Console errors. Standalone compilation passed, but actual Editor import was blocked by licensing.
2. Save your work and open **only** `Assets/Scenes/SentinelTestScene.unity`. Stop Play Mode.
3. Run **Tools > Halazia > Sentinel Test > Prepare Evaluation Scene and Prefab**. Require `SENTINEL_SETUP_PASS`. This modifies only the evaluation scene and Sentinel-specific assets. It:
   - adds the existing Y Bot model, child Animator/AgentAnimatorDriver/CombatActionsAdapter, root motion off;
   - creates `Assets/Animations/Agents/SentinelAnimator.controller` using existing idle/run/attack/hit/death clips; death has no outgoing transition;
   - sets recovery duration from the attack clip and assigns the parent motor;
   - replaces the target's box collider with CharacterController, attaches the existing FirstPersonController and optional SentinelTestWeapon, and puts Main Camera at eye height;
   - creates an exit wall and a 2 m opening centered at `(5.5, 0, 0.5)`, with SentinelOrderPosition there;
   - saves the animated `Assets/Prefabs/Sentinel.prefab` with scene-only references cleared. The scene keeps its own configured Sentinel object.
4. Inspect SentinelTest: provider assigned, target=TestPlayerTarget, guard=GuardPosition, three patrol references, controller height=2/radius=0.5/center=(0,1,0), motor max speed=4.2, arrival tolerance=0.6. Inspect SentinelVisual: valid avatar/controller, root motion off, driver motor reference assigned. Adjust model scale only if its imported scale requires it. Setup has not been visually verified.
5. Run **Tools > Halazia > Sentinel Test > Bake and Verify NavMesh**. Require `SENTINEL_SCENE_BAKE_PASS`, save, reopen the scene, then run **Verify Baked Paths** and require `SENTINEL_SCENE_PATHS_PASS`. The tool saves a new bake asset each time. Re-bake after geometry changes. Inspect the blue NavMesh through the opening and around the wall.
6. Run the batch regressions described in Sentinel_AI.md with the project Editor closed. Require **12 original + 6 integration checks** to pass. Reopen the scene afterwards.

## Manual Play Mode acceptance checklist

Use Scene view with Gizmos to observe Sentinel. Game view uses WASD/mouse and left click to fire a 25-damage test shot at screen center. Escape releases the cursor. The optional test weapon has no HUD/crosshair. Use **Log Play Mode Status** for health/state. Stop/restart to reset health. One missing-Commander warning is expected in the isolated fixture.

- [ ] **Patrol:** deactivate TestPlayerTarget temporarily; observe all three points, 2 s waits, loop and return home when Loop Patrol is off. Test empty/all-null route safely guarding. Restore the target and route.
- [ ] **Navigation:** observe turns around the wall/opening without straight-line clipping. Test actual ship stairs/decks/doors separately. Disable the provider or order `(100,0,0)` using the destination marker; expect one blocked report, no false arrival. Restore and issue a fresh reachable order.
- [ ] **Detection:** approach from front, behind and beyond 15 m. Solid walls must block sight and damage; triggers should not. Test separate decks in the ship scene. Confirm the ray intersects the wall before judging occlusion.
- [ ] **Tracking:** break LOS and move while hidden. LastKnownPlayerPosition must stay at the last visible point; investigate, search about 5 s, return. One loss report after 1.5 s; reacquisition permits a new loss report.
- [ ] **Combat:** enter the defense area and attack range (1.8 m). Target loses 15 per attack, no faster than 1.2 s and never during attack recovery. Out-of-range, blocked or incorrectly faced attacks do no damage. Observe attack, hit, locomotion and death clips.
- [ ] **Movement locks:** leave range or issue an order mid-swing. No translation until recovery completes. Disable Sentinel mid-attack; it remains stopped. Kill it mid-attack; wait beyond recovery and confirm late events never unlock it or restart movement.
- [ ] **Player weapon/health:** aim center-screen at Sentinel and fire four 25-damage hits. Confirm hit reactions, zero health, one death report, no later attacks/orders and no revival after disable/re-enable. Shoot a wall in front of Sentinel; no damage should pass through. Dead test player cannot fire. Game-over/respawn UI is outside this fixture.
- [ ] **All eight orders:** on SentinelEvaluationControls select Task and use its component context menu **Issue Selected Order (Play Mode)**. Move SentinelOrderPosition to a reachable point. Check Idle, Patrol, Investigate, Pursue, Flank, HoldPosition, CoverRoute and Retreat. Arrival fires once; Idle/Retreat suppress attacks; cancel returns to the home post. Report counters remain visible in the Inspector.
- [ ] **Choke-point/escape denial:** issue CoverRoute at `(5.5,0,0.5)`. Verify movement through the baked opening, one arrival, local engagement and return to that post. Lead the player beyond the 12 m defense radius; Sentinel must not pursue indefinitely. For a strict doorway guard, lower Defense Radius before play and validate that paths stay inside it. Check the player cannot exploit collider gaps; one agent is not a guarantee that every route is sealed.
- [ ] **Commander integration:** use a separate saved copy or the team's integration scene. Assign Commander player reference and corridor/heatmap markers. Add the complete squad to test automatic CoverRoute allocation; with only Sentinel, Pursue may win. Confirm register once, visible sight reports, order interruption, arrival/block/loss/death reports, retry after a blockage and removal on death. Do not attach Commander to Sentinel's root. Disable Commander tactical updates for isolated manual order tests.
- [ ] **Production scene/prefab:** place Sentinel prefab instances, assign unique Agent IDs, player, guard and patrol transforms, and bake each intended navigation area. Check no competing AI/movement components, correct obstacle masks and no root motion. Scene targets cannot be stored in the prefab asset.
- [ ] Save the test setup outside Play Mode and record results/screenshots/video for the evaluation. Mark a check passed only after observing it.

## Remaining limitations

The licensed-Unity setup command, prefab/model visual validation, actual NavMesh bake, all 18 executable checks and all Play Mode observations remain unperformed in this session. The current license failure is logged in `Logs/SentinelRegression-current.log`. Existing player game-over/respawn and a production weapon system are not present; the supplied weapon is a deliberately isolated evaluation component. Commander route prediction is reused, not reimplemented by Sentinel.
