# Sentinel AI

## Files and architecture

- `Assets/Scripts/AI/Sentinel/SentinelAgent.cs`: implements the existing `ISquadAgent` contract, all ten AI states, patrol, area defense, FOV/occlusion, last-known-position investigation, searching, attacks, health, lifecycle, and Commander reports. All Commander orders interrupt autonomous activity immediately, regardless of their numeric priority; the Commander owns allocation and replacement decisions.
- `SentinelPathProvider.cs`: synchronous path-planning abstraction. Implement `TryGetPath` over the team's Weighted A* graph when that dependency exists. Return complete world-space waypoint arrays, or `false` for unreachable destinations. It must not move the agent. No asynchronous requests remain outstanding when orders change.
- `SentinelNavMeshPrototype.cs`: explicitly optional **NavMesh prototype, not Weighted A***. Uses complete baked NavMesh paths, rejects partial paths, and never adds a `NavMeshAgent`. Path requests occur on destination changes, with throttled pursuit repaths; arrays allocate only on path requests.
- `ISentinelDamageable.cs`: Sentinel-local damage adapter contract. The existing shared `ICombatActions` only triggers animation and cannot apply damage. No shared interface was changed.
- `SentinelTargetHealth.cs`: optional working health receiver for the current player, whose `FirstPersonController` has no health API. Its Inspector death event can be wired to the game's death flow. Alternatively, implement `ISentinelDamageable` on an adapter for a future health system. Sentinel itself also implements this contract and exposes `TakeDamage(float)` like the sandbox agent.
- Matching `.meta` files give the scripts stable Unity asset identities.

Commander, shared interfaces, scenes, and prefabs are unchanged. There is no existing Weighted A* implementation in the inspected project. Production Weighted A* navigation remains a missing dependency; the supplied prototype enables gameplay testing now when explicitly selected.

## Inspector setup

1. On the Sentinel root, add `SentinelAgent`. Its required `AgentMotor` adds a `CharacterController`; fit the capsule to the model and place it on the deck. Keep exactly one movement/decision controller. Remove `HunterHearing`, `HunterMovementTest`, `MockSquadAgent`, and active `NavMeshAgent` components. Sentinel refuses to run alongside those competing controllers. Do not attach Commander to the same root.
2. Explicitly add `SentinelNavMeshPrototype` and assign it as **Path Provider** for prototype testing. Bake the deck's NavMesh using the existing AI Navigation package. For production Weighted A*, replace this provider with an implementation of `SentinelPathProvider`.
3. Set a unique **Agent Id** and **Max Health**. Optionally assign **Guard Position**; otherwise the starting position is the home post. The marker's position is captured at startup.
4. Assign patrol Transforms in route order, **Loop Patrol**, **Patrol Speed**, and **Patrol Wait**. Null entries are skipped. Empty/all-null routes safely guard; non-looping routes return home after the last point. Keep the route within the intended security area.
5. Assign **Player Target** to the player root. Add `SentinelTargetHealth` to that root to receive damage, or a component implementing `ISentinelDamageable` on that root/one of its parents. Target health is cached when the target reference changes; configure it before entering Play Mode. Set eye and target offsets so the sight ray passes through the character torso rather than the deck.
6. Set range, FOV, target-lost timeout, and search duration. **Sight Obstacle Mask must include every solid wall/door/deck layer**. Obstacles need non-trigger colliders. The default mask includes all layers. Self/target colliders are filtered out; triggers do not block sight. The fixed raycast buffer fails closed when full.
7. Set **Defense Radius**, attack range, cooldown, damage, and facing tolerance. Add `CombatActionsAdapter` with its required animator driver if animation is desired. Damage works without animation. Hits apply immediately on the attack decision, at most once per cooldown, after fresh LOS/range/facing checks; do not also apply the same damage from animation events.
8. Match **Motor Base Speed** to `AgentMotor`'s **Max Speed** (both default to 4.2). Sentinel converts its absolute speeds to the shared motor's multiplier. Keep Sentinel **Arrival Distance** at least the motor's stopping distance plus provider projection error (defaults: 0.6 versus 0.35 + 0.1). Use a small prototype sample distance on the multi-deck ship to avoid sampling another deck.
9. Add a Commander in the scene before enabling Sentinel for automatic registration. Disable/re-enable unregisters/re-registers living agents without healing. Late Commander spawning requires re-enabling Sentinel. Enable **Debug Logs** and select Sentinel for detection/FOV, defense, and attack gizmos.

## Order behavior and reports

| Actual task | Behavior |
| --- | --- |
| Idle | Stop and wait; suppress autonomous patrol/combat. |
| Patrol | First reach `TargetPosition`, report arrival, then follow the Inspector route. Hold the destination when no route exists. |
| CoverRoute | Reach the predicted choke point, then defend it. `TargetCorridorId` remains stored in `CurrentOrder`; navigation uses `TargetPosition`. |
| HoldPosition | Reach and defend the designated entrance/post. |
| Flank | Reach the designated lateral position, then defend/engage locally. |
| Retreat | Reach and hold fallback; suppress engagement while this order remains active. |
| Investigate | Reach the commanded position, search for the configured duration, then return to the home post. |
| Pursue | Reach the supplied last-known position, then engage a visible local target or search and return home. This Sentinel does not perform an unlimited Hunter pursuit. |

Ordered movement takes precedence over local engagement until the ordered destination is reached. Direct commanded moves can relocate the agent beyond its previous defense radius. Defensive orders establish their destination as the new defense center; Investigate/Pursue retain the home defense center. After arrival, local chase and investigation destinations and path corners stay within the defense sphere. Seeing a player beyond that sphere never starts a chase. An unseen player's live transform is never used for navigation. Sight loss starts investigation immediately; `OnTargetLost` is reported after the contact timeout, once until reacquisition.

`OnTaskArrived` fires once per accepted order when the requested destination is physically reached within tolerance. Continuing to guard, local combat, or returning to a post does not repeatedly report arrival. `OnTaskBlocked` fires once per order for failed/incomplete/invalid paths, a motor block, or no progress. Ordered failures wait for a new order/cancel; autonomous failures retry after a delay. `OnDied` fires once at zero health, stops decisions and navigation, locks movement, and unregisters. The shared motor handles deceleration/gravity. New orders are rejected after death. Cancel clears the order and returns home before resuming patrol/guard.

Commander callbacks may synchronously replace orders. Reports update internal state before firing, and target-loss handling checks the order version to preserve those replacements. The current Commander may replace a target-loss investigation with its own last-known player position; that is existing Commander behavior.

## Play Mode verification checklist

These are manual tests to run in Unity; they have not been executed as part of this implementation. Temporarily disable Commander tactical updates for isolated autonomous tests, or use a separate test scene without a Commander. Restore it for integration tests.

1. **Patrol:** assign three reachable points and hide the player. Verify route order, speed, waits, and looping. Turn looping off: verify return home after the last point. Try empty, null, and destroyed waypoint entries; verify no exceptions.
2. **Perception:** approach in front, behind, and beyond range. Verify acquisition only inside range/FOV. Place a solid wall and closed door between the characters; verify no acquisition or damage through them. Open the door and reacquire. Test another ship deck above/below.
3. **Combat:** add target health, approach inside the defense area, and watch health fall by the configured damage once per cooldown. Move outside attack range, behind a wall, or out of facing alignment: verify no hit. Verify optional attack animations and movement stopping.
4. **Loss/search:** break LOS after acquisition. Observe Investigate toward the last visible position, then turning Search, then ReturnToPost. Move while hidden: the investigation destination must stay at the old position. Count one target-loss report per contact loss after the timeout; reacquire and repeat.
5. **Leash:** lead Sentinel toward the defense boundary and leave the radius. Verify it returns to its post. Test a path whose detour leaves the defense sphere: local chase/investigation must reject it instead of abandoning the area.
6. **Orders:** issue each actual task using `AssignOrder(new SquadOrder(task, position))` from a test driver. Verify patrol/investigation interrupt immediately. CoverRoute/HoldPosition defend the new destination and return there after threats disappear. Retreat and Idle must suppress engagement. Investigate/Pursue search after reaching their destinations. Cancel restores home patrol/guard.
7. **Reports:** subscribe counters to the four `ISquadAgent` events in a test driver. Arrival must occur once, with no repeats during a long guard. Choose a disconnected island/off-mesh point or block movement: expect one blocked report and no false arrival. Assign a fresh order and verify progress resumes. Test an event subscriber that assigns a new order during arrival/loss/block reporting; it must remain active.
8. **Health/lifecycle:** call `TakeDamage(10)`, negative/NaN damage (ignored), and lethal damage. Verify health clamps to zero, one death event, no further damage/orders/actions, and Commander removal. Disable/re-enable living and dead agents; verify no duplicate registration, duplicate reports, or revival.
9. **Navigation:** test corners, doors, stairs and deck changes. Move the visible target repeatedly: paths should update at the configured interval rather than every frame. Remove/disable the provider: verify explicit failure reporting rather than straight-line movement. Try competing controllers: verify the setup error.

## Compilation dependency repair

`ISentinelDamageable` already exists in `Halazia.AI.Sentinel`, with `IsAlive` and
`TakeDamage(float)` implemented by both SentinelAgent and SentinelTargetHealth.
The generated Assembly-CSharp.csproj omitted the supporting Sentinel scripts;
its compile entries now include them. Unity may regenerate this ignored project
file: after importing the scripts, regenerate project files in Unity's External
Tools preferences if the IDE still shows missing types. No Assets assembly
definitions restrict these types; they share the runtime Assembly-CSharp assembly.

`ISentinelPathProvider` now defines the existing
`TryGetPath(Vector3, Vector3, out Vector3[])` contract. SentinelPathProvider
implements it and retains the serialized component type used by SentinelAgent,
so existing Inspector assignments remain valid. SentinelNavMeshPrototype provides
the concrete implementation. No Weighted A* graph/planner exists in this checkout;
it still requires a real provider implementation before production integration.

Both runtime scripts and Editor scripts (including SentinelRegressionChecks) were
compiled during this repair with Unity 6000.6.5f1's installed Roslyn compiler,
Unity assembly references, and the installed Input System assembly. This is a C#
compilation check, not a Unity player build or Play Mode execution.

## Verification performed

The runtime gameplay scripts were compiled with the installed Unity 6000.6.5f1 Roslyn compiler and actual Unity/project assembly references, including all new Sentinel scripts. Compilation succeeded with no errors; warnings include existing obsolete Commander APIs, unused events, and Inspector-assigned fields. `git diff --check` was also run. This checks C# and API compatibility, not scene configuration, physics, animation timing, baked navigation, or runtime event behavior. Unity Play Mode testing remains necessary.
