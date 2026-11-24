# Sentinel Project Status: AI, Animation, Rifle and Game Readiness

**Audit date:** 2026-11-24  **Branch:** `chanumi/sentinel-ai` (HEAD `d82d655`, with uncommitted working-tree changes)  **Unity:** 6000.6.5f1

This audit was read-only. Nothing in the project was modified, executed in Unity or committed. The only command run was a standalone C# compile, and its output went to a temporary folder outside the project (see §6.1).

**Evidence labels used throughout:**

| Label | Meaning |
| --- | --- |
| **[Inspected]** | Read directly from the project files during this audit (YAML, `.meta`, `.cs`). |
| **[Log: project]** | A Unity log or report in this project folder shows the result, and that run targeted this project path. |
| **[Reported: copy]** | `Sentinel_AI.md` says it passed, but the run was on a temporary copy of the project and no log was kept here. It cannot be re-confirmed from project files. |
| **[Untested]** | No evidence that it has ever been run or observed. |

---

## 0. Executive summary

| Area | State |
| --- | --- |
| Sentinel FSM / navigation / perception / melee combat | Implemented and covered by Editor checks and Play Mode probes, but those were **[Reported: copy]**. The manual Play Mode checklist in `Sentinel_Test_Scene.md` has **not** been completed. |
| Commander integration | Works only with **uncommitted changes to teammate-owned `CommanderAgent.cs` and `AgentAnimatorDriver.cs`**. Without them SentinelAgent does not compile (`ReportInvestigationComplete`, `IsAttacking`, `SetExternalMovementLock`). |
| Sentinel animations | Seven clips on a Generic rig. The controller is wired for Idle/Walk/Run/Attack/TakeDamage/Death. The impact event drives damage. The `Patrol` state is orphaned (unreachable). There are **no rifle (armed) animations**. |
| Sentinel_Rifle | The asset is finished (model, 7 materials, 5 sockets, grip pivot). It is **not attached to anything**: no scene or prefab references it. Attachment work stopped at an inspection script that has never run. |
| Shooting | **Not implemented.** The Sentinel's only attack is a 1.8 m melee arm swing. |
| Actual game environment | **Not ready.** `Sentinel.prefab` is still the primitive capsule/cube version. No Sentinel or Commander exists in `Cargo_Blockout`. The real player has no `ISentinelDamageable`. |

---

## 1. Project inventory (Sentinel-related)

### 1.1 Ownership (from team agreement)

| Owner | Files |
| --- | --- |
| **Chanumi (you)** | `Assets/Scripts/AI/Sentinel/*`, `Assets/Scripts/Editor/Sentinel*.cs`, `SentinelTestScene`, `SentinelAnimator.controller`, `Sentinel_Robot.fbx`, `Sentinel.prefab`, `Sentinel_Rifle` assets, `Documentation/Sentinel_*.md` |
| **Vindya** (do not edit without approval) | `Assets/Scripts/AI/Commander/*` (incl. `CommanderAgent.cs`), `Shared/Movement/AgentMotor.cs`, `AgentAnimatorDriver.cs`, `Shared/Combat/*`, `Shared/Squad/*` (incl. `ISquadAgent.cs`), `Enemy_Base.prefab`, `Enemy_commander.prefab`, `Squad_Contract.md` |

### 1.2 Working-tree state [Inspected via `git status` / `git diff`, read-only]

**Modified, tracked:**
- `SentinelAgent.cs` (+107 lines), `SentinelNavMeshPrototype.cs` (+6), `SentinelRegressionChecks.cs` (+296), `SentinelTestSceneTools.cs` (+4), `Sentinel_AI.md`, `Sentinel_Test_Scene.md`
- **Teammate-owned:** `CommanderAgent.cs` (+92), `AgentAnimatorDriver.cs` (+44). See §2.8.
- `Agent_AnimatorController.controller` and `ProjectAuditorSettings.asset` show as modified, but `git diff --ignore-cr-at-eol` is empty: **line endings only**, no content change.

**Untracked (never committed):** `SentinelAnimator.controller`, `Sentinel_Robot.fbx`, the whole rifle set (`Prefabs/Weapons`, `Art/Models/Weapons`, `Art/Materials/Weapons`), `SentinelTestScene.unity` plus its NavMesh folder, `SentinelCommanderTestControls.cs`, Editor tools (`SentinelAnimationSetupCheck`, `SentinelCommanderPlayModeProbe`, `SentinelCommanderTestControlsEditor`, `SentinelCommanderTestSetup`, `SentinelRifleBuilder`, `SentinelRifleQualityTools`, `SentinelRifleAttachment`), several `.meta` files for Shared/Squad and `CorridorNodeMarker`, and `Artifacts/`.

> ⚠ Most of the current Sentinel work, including the animated model, the controller, the test scene and the rifle, exists **only in this working tree**. Back up or commit it (your decision; no Git was run here).

---

## 2. Sentinel AI system

### 2.1 Architecture [Inspected]

```
SentinelTest (root GameObject)
├─ SentinelAgent           FSM + perception + combat + ISquadAgent + ISentinelDamageable
├─ SentinelNavMeshPrototype path planning only (NavMesh.CalculatePath → corner array)
├─ AgentMotor               the ONLY thing that moves the body (CharacterController.Move)
├─ CharacterController      h=2, r=0.5, center (0,1,0)
├─ SentinelEvaluationControls (test-only context menu)
├─ Body (inactive primitive cube)
└─ Sentinel_Robot (FBX prefab instance)
   ├─ Animator (SentinelAnimator.controller, model's own Generic Avatar)
   ├─ AgentAnimatorDriver   (motor source = SentinelTest's AgentMotor)
   └─ CombatActionsAdapter  (ICombatActions → driver)
```

- No `NavMeshAgent` and no root motion. In `Awake`, SentinelAgent disables itself if another `ISquadAgent`, another `IAgentMotor`, or an enabled `NavMeshAgent` is on the same object.
- **Detailed FSM** (`SentinelState`): `Idle, Patrol, MoveToOrder, Guard, Investigate, Search, Chase, Attack, ReturnToPost, Dead`.
- **Behaviour view** (`BehaviorState`, read-only projection for UI/debug): `Idle, Patrol, Guard, Alert, Combat, Return, Dead, Disabled`.
- `SetState` cancels any pending hit and stops navigation on every transition. Attack and Dead lock movement.

### 2.2 Patrol and NavMesh movement [Inspected; probes Reported: copy]

- Waypoints come from the serialized `_patrolWaypoints` (Transform[]), with `_loopPatrol`, 2 m/s patrol speed and a 2 s wait at each point. Null waypoints are skipped. An empty or all-null route falls back to Guard at the post.
- `StartPatrolRoute()` issues a Patrol order to the first non-null waypoint, which resets route progress. Resuming after combat keeps progress.
- `MoveTo()` checks arrival against the *requested* destination (0.6 m), not just the motor's event. It re-paths when the target moves more than 0.8 m or the 0.6 s re-path interval expires, and declares the path blocked after 3 s with no progress (0.1 m). It never accumulates stuck time while an attack animation plays.
- **Body bypass:** characters are excluded from the bake (NavMeshModifier). A head-on contact with a body of half-size ≤ 1.5 m triggers a NavMesh-validated two-point detour, at most twice. Larger contacts report blocked.
- **Path provider:** destination sample tolerance is 0.1 m (strict), start-position tolerance is 0.75 m. The start tolerance fixes agents stranded just off an eroded NavMesh edge; this was added in the uncommitted diff.
- Speed: the motor gets `speed / _motorBaseSpeed` as a multiplier. `_motorBaseSpeed` (4.2) **must equal** AgentMotor `_maxSpeed` (4.2 in the scene). If they drift apart, patrol and chase speeds will be wrong.

### 2.3 Player detection and target tracking [Inspected]

- The target is the serialized `_playerTarget` Transform only. **No scene searches**, so every instance must have it assigned (see §5).
- Perception runs every 0.1 s: range 15 m, FOV 110°, `RaycastNonAlloc` from `Position + (0,1.5,0)` to `target + (0,1,0)`, ignoring self, target and trigger colliders. If the 64-hit buffer is full, it treats the target as **not** visible (fails closed).
- Target health is found by `GetComponentsInParent` looking for `ISentinelDamageable` (retried every 2 s). If none is found it logs one warning, and the Sentinel tracks the target but **cannot damage it**.
- A target is valid only if it is active, not self, and (when it has health) alive. Replacing `_playerTarget` clears contact and health caches and sends Chase/Attack/Investigate/Search back to the post.
- Every visible tick calls `Commander.ReportPlayerSighted(lastKnown)`.

### 2.4 Pursuit and combat [Inspected; impact damage Reported: copy + manual probe]

- Engagement requires: target visible and valid, **both target and Sentinel inside the 12 m defense radius** around `_post`, the post-block retry delay elapsed, and an order that allows engagement. No order, or an order that has arrived, allows it. Pursue allows it before arrival. Idle, Retreat or a blocked order does not.
- **Chase:** moves to the clamped *last known* position (not the live position, so it never tracks through walls) while facing it. The path is rejected if any corner leaves the defense radius.
- **Attack (melee only):** range 1.8 m, facing within 30°, cooldown 1.2 s, damage 15. It calls `ICombatActions.Attack()` (attackType 0). When a driver swing starts, the hit is queued and applied on `AnimEvent_AttackHit`, then re-validated: still attacking the same order, same target alive, in range, line of sight, facing. One swing deals damage at most once. Without an animator driver, damage is applied immediately.
- Pending hits are cancelled by taking damage, death, any state change, disable, or `OnAttackFinished` before impact.
- **Taking damage:** NaN, Infinity and values ≤ 0 are ignored. If the Sentinel survives it plays `Hit`. On death it sets Dead, plays `TriggerDie`, clears the order, raises `OnDied` once and unregisters from the Commander.

### 2.5 Investigate and Search [Inspected]

- On contact loss (not visible for 1.5 s, or target invalid), Chase/Attack becomes **Investigate** and `OnTargetLost` fires once. The Commander may replace the order synchronously; this is guarded by `_orderVersion`.
- Investigate walks to the clamped last-known position. On arrival it enters **Search**: rotates in place at 70°/s for 5 s via `transform.Rotate`. AgentMotor does not fight this, because its rotation only applies with a face target or while moving, and both are cleared.
- A Commander-issued Investigate/Pursue order: MoveToOrder → arrive (`OnTaskArrived` once) → Search.

### 2.6 Investigate completion and return to Patrol [Inspected; Reported: copy]

- When Search ends, state goes to **ReturnToPost**. If the current order is Investigate and has arrived, it calls `CommanderAgent.ReportInvestigationComplete(this)`. The Commander then marks that last-known position as investigated and calls `CancelOrder()` on the Sentinel, which restores the home post.
- Back at the post, `ResumeAtPost()` either continues an unfinished order, or **resumes Patrol** (no order, or a Patrol order with an unfinished route), or Guards.
- The Commander will not re-issue Investigate for the same position until the player is seen again (`_lastKnownPositionInvestigated`). It never issues Investigate at (0,0,0) before any sighting (`_hasLastKnownPlayerPosition`). Both changes are in the uncommitted Commander diff.

### 2.7 Commander integration and events [Inspected; Play Mode probe 14/14 Reported: copy]

| Direction | Mechanism |
| --- | --- |
| Sentinel → Commander | `RegisterSquadMember` (OnEnable, and every 2 s while unregistered), `UnregisterSquadMember` (OnDisable, death, or the Commander becoming disabled/destroyed), `ReportPlayerSighted`, `ReportInvestigationComplete` |
| Sentinel events | `OnTaskArrived`, `OnTaskBlocked` (once per order), `OnTargetLost` (once per contact loss), `OnDied` (once) |
| Commander → Sentinel | `AssignOrder(SquadOrder)`, `CancelOrder()`; direct orders through `IssueDirectOrder` (used by test tools) |

Order handling (Idle, Patrol, HoldPosition, CoverRoute, Flank, Retreat, Investigate, Pursue) is documented in `Sentinel_AI.md` and matches the code.

### 2.8 Dependencies and integration issues

| # | Issue | Severity | Owner |
| --- | --- | --- | --- |
| D1 | **SentinelAgent depends on uncommitted, owner-unreviewed edits** in `CommanderAgent.cs` (`ReportInvestigationComplete`, `IssueDirectOrder`/`ReleaseDirectOrder`/`HasDirectOrder`, blocked-retry delay, investigated-position flags, `isActiveAndEnabled` filtering) and `AgentAnimatorDriver.cs` (`IsAttacking`, `SetExternalMovementLock`, `_dead` guard, `OnDisable` reset, hit-event gating). If Vindya's branch is merged without them, **Sentinel will not compile.** | **Blocker for merge** | Vindya to approve |
| D2 | `CommanderAgent.Awake` registers `_initialSquadMembers` even when the component is disabled and never unregisters in `OnDisable`. A disabled Commander can still issue orders through events. Sentinel works around this by detaching on the Commander side; test scenes deactivate the whole GameObject. A fix has been proposed but not approved. | Medium | Vindya |
| D3 | `CommanderAgent.cs:443` uses obsolete `FindObjectsOfType` (CS0618). `CorridorHeatmapTracker.cs:92` uses an obsolete sort-mode overload. | Low | Vindya |
| D4 | `Sentinel.prefab` and the scene instance both use Agent ID `Sentinel_Test`. Several instances would share an ID (affects logs only; Commander references are by object). | Low | You |
| D5 | Engagement is hard-limited to the 12 m defense radius around the post. This is intended for a "sentinel", but confirm it fits the ship layout. | Design | You |
| D6 | `ReportPlayerSighted` is called every 0.1 s while the player is visible. Fine for one Commander; consider throttling if profiling shows cost. | Low | You |

---

## 3. Sentinel animations

### 3.1 Model and clips [Inspected `Sentinel_Robot.fbx.meta`; Log: project `Logs/SentinelCompile/AnimationCheck.log`, 2026-11-23, result "all Sentinel animation checks passed"]

- Rig **Generic**, Avatar created from this model (`Sentinel_RobotAvatar`), no root-motion node, `keepOriginalPosition` on.
- Skeleton: `Sentinel_Rig` (**scale 100**, −90° X; a Blender export with `bakeAxisConversion` off) → `Hips/Spine/Chest/Neck/Head`, `Left|RightShoulder/UpperArm/LowerArm/Hand`, `Left|RightUpperLeg/LowerLeg/Foot/Toes`. Each body part is a separate mesh (`Sentinel_Head`, `Sentinel_RightHand`, …) bound to the rig.
- Approximate height: the head mesh pivot is at **y = 2.34 m**, so the robot is roughly 2.4–2.6 m tall. Exact renderer bounds were not measured.

| Clip | Frames | Loop | Notes |
| --- | --- | --- | --- |
| `Sentinel_Rig|Idle` | 0–90 | ✔ | |
| `Sentinel_Rig|Walk` | 0–40 | ✔ | |
| `Sentinel_Rig|Run` | 0–30 | ✔ | |
| `Sentinel_Rig|Patrol` | 0–60 | ✔ | **Not reachable in the controller** |
| `Sentinel_Rig|Attack` | 0–30 | ✘ | Right-arm **melee** swing (RightUpperArm ≈83°). Event `AnimEvent_AttackHit` at normalized **0.5** |
| `Sentinel_Rig|TakeDamage` | 0–20 | ✘ | |
| `Sentinel_Rig|Death` | 0–50 | ✘ | Hips drop ≈ 98 cm |

No clip has an `AnimEvent_AttackComplete` or `AnimEvent_Footstep` event.

### 3.2 Animator Controller `SentinelAnimator.controller` [Inspected]

Parameters: `Speed` (float), `Attack` (trigger), `Hit` (trigger), `Die` (trigger, **unused**: the driver uses `IsDead`), `IsDead` (bool). One layer (Base). No `AttackType`, `IsMoving`, `VelX`, `VelZ`; the driver detects missing parameters and skips them safely.

```
Idle ──Speed>0.1──▶ Walk ──Speed>3──▶ Run
Idle ◀─Speed<0.1── Walk ◀─Speed<3──── Run
AnyState ─Attack (0.25 s)─▶ Attack ──exit 1.0──▶ Idle
AnyState ─Hit    (0.1 s)──▶ TakeDamage ─exit 1.0─▶ Idle
AnyState ─IsDead (0 s, no self)─▶ Death (terminal)
Patrol: no transitions in or out (orphan)
```

With patrol at 2 m/s the Sentinel uses Walk, and with chase at 4.2 m/s it uses Run (threshold 3).

### 3.3 AgentAnimatorDriver integration [Inspected]

- `Speed` = `AgentMotor.CurrentSpeed`, damped 0.12 s, not normalized.
- `TriggerAttack` sets `IsAttacking` and locks motor translation. **Unlock comes only from the 0.85 s failsafe**, because the clip has no `AnimEvent_AttackComplete`. The event at 0.5 normalized fires before 0.85 s at either 24 or 30 fps, so damage timing is unaffected. Movement can, however, resume while the last part of the Attack clip and its blend-out are still playing (possible brief sliding). **[Untested visually]**
- `TriggerHit` and `TriggerDie` are guarded after death. `TriggerDie` resets the Attack/Hit triggers and permanently locks the motor.
- SentinelAgent drives `SetExternalMovementLock` for decision locks (Attack/Dead/disabled), so a late animation event cannot unlock a dead or attacking agent.

### 3.4 What works / what needs attention

| ✔ Works (evidence) | ⚠ Needs attention |
| --- | --- |
| All 7 clips bind 220 curves to Sentinel bones; loop flags correct **[Log: project]** | `Patrol` state orphaned; decide to remove it or use it for patrol speed (e.g. a `Speed` band, or a bool set by SentinelAgent) |
| Idle/Walk/Run driven by measured speed **[Inspected]** | `Die` trigger unused; remove or document |
| Impact-frame damage applied once, with re-validation **[Reported: copy]**; 7 hits × 15 observed in a manual probe that has since been removed | Add `AnimEvent_AttackComplete` near the end of Attack instead of relying on the 0.85 s failsafe |
| Death is terminal and locks movement **[Reported: copy]** | **No armed/rifle locomotion, aim, fire or reload clips.** All current clips are unarmed |
| | No footstep events (driver supports them) |

---

## 4. Sentinel_Rifle

### 4.1 Assets [Inspected]

| Asset | Path |
| --- | --- |
| Prefab | `Assets/Prefabs/Weapons/Sentinel_Rifle.prefab` (GUID `0d91bd8d8bd7a7045a36c6c548b1221d`) |
| Meshes | `Assets/Art/Models/Weapons/Sentinel_Rifle/Sentinel_Rifle_Meshes.asset` (≈3 MB; per-part mesh sub-assets plus a combined preview mesh) |
| Materials | `Assets/Art/Materials/Weapons/Mat_Rifle_*.mat` (7) |
| Tools | `SentinelRifleBuilder.cs` (full regenerate: **do not run** on the finished asset), `SentinelRifleQualityTools.cs` (Audit / Polish existing), `SentinelRifleAttachment.cs` (inspection stub, §4.6) |
| Review artifacts | `Artifacts/SentinelRifle/*` (8 orthographic and perspective renders, `quality-audit.txt`, `verification.txt`, `file-report.txt`, the original reference image) |

### 4.2 Model structure and dimensions [Inspected prefab; Log: project `polish-final-unity.log` 2026-11-24 08:22, exit 0, `SENTINEL_RIFLE_VERIFY_PASS`, `SENTINEL_RIFLE_AUDIT_PASS`]

- **Bounds:** 0.12 (W) × 0.61 (H) × 1.51 (L) m.
- **Triangles:** 5,712, with 0 degenerate (88 zero-area triangles removed). Combined preview mesh: 17,268 vertices (flat-shaded, unshared).
- **Hierarchy:** root `Sentinel_Rifle` → groups `Receiver, Foreend, Stock, Grip, Controls, Magazine, Details, Rail, Barrel, Optic, FrontSight, Sockets`. **198 GameObjects, 180 MeshRenderers**, no colliders, no Rigidbody, no MonoBehaviours.
- Left/right mirrored parts share names (two `Shield`, `UpperArmor`, `InsigniaA`, …). Avoid `Find()` by name for these.
- Design: silver angular armor over a dark receiver, four slanted cyan fore-end vents, open triangular stock with cyan inset, slanted magazine with a charge light, angled grip, hooded optic, front sight, 29-tooth top rail, cylindrical muzzle brake, insignia and fasteners. The renders show no missing parts or pink materials.

### 4.3 Materials and colours [Inspected]

All use the **Built-in `Standard` shader**. The project currently runs the **Built-in pipeline** (`GraphicsSettings` custom RP = none) even though the URP 17.6.0 package is installed. If the team switches to URP, these materials **and `Mat_Sentinel`** will render pink and need converting to URP/Lit.

| Material | Albedo (RGB) | Emission | Use |
| --- | --- | --- | --- |
| Mat_Rifle_CeramicBlack | 0.065, 0.075, 0.090 | — | receiver core, handguard, stock beams, rail teeth |
| Mat_Rifle_SilverArmor | 0.59, 0.64, 0.75 | — | armor shells, butt frame, muzzle brake |
| Mat_Rifle_Gunmetal | 0.22, 0.24, 0.29 | — | barrel, rail base, grip treads, optic hood |
| Mat_Rifle_GripRubber | 0.025, 0.028, 0.034 | — | grip/stock/magazine insets, butt pad |
| Mat_Rifle_EnergyCyan | 0.01, 0.58, 0.90 | 0, 0.65, 1.25 (`_EMISSION` on) | vents, lights, reticle |
| Mat_Rifle_StencilIvory | 0.80, 0.85, 0.94 | — (metallic 0, smoothness 0.22) | insignia |
| Mat_Rifle_OpticGlass | 0.012, 0.10, 0.16 | — | lens |

No textures (procedural flat colours). No weathering. No bloom in the scene, so emission will not glow.

### 4.4 Mesh quality

- ✔ Clean silhouette, no degenerate triangles, consistent materials, reasonable triangle budget.
- ⚠ **180 renderers means up to ~180 draw calls per rifle** (no LOD, no combined per-material mesh). For several Sentinels on screen, bake the parts into one mesh per material (≈7 sub-meshes) or add an LOD.
- ⚠ No collider. That is fine while it is held, but it needs one if the rifle is dropped on death.

### 4.5 Sockets, pivot and orientation [Inspected `quality-audit.txt`, consistent with the prefab]

Root transform is identity (position 0, rotation 0, scale 1). **Pivot = grip hold point.** **+Z = muzzle, +Y = up.**

| Socket | Local position (m) | Forward | Purpose |
| --- | --- | --- | --- |
| `Grip_Socket` | (0, 0, 0) | +Z | right-hand grip / attach point |
| `Support_Hand_Socket` | (0, 0.1144, 0.5948) | +Z | left-hand IK target on the fore-end |
| `Muzzle_Point` | (0, 0.2119, 1.0108) | +Z | projectile/raycast origin, muzzle flash |
| `Sight_Point` | (0, 0.3666, 0.1047) | +Z | aim line (aligned to the reticle) |
| `Ejection_Point` | (0.06, 0.2113, 0.338) | **+X** | casing/energy vent effect, right side |

### 4.6 Attachment to the Sentinel: **NOT DONE**

- No scene or prefab references the rifle GUID [Inspected]. **The rifle is not attached to RightHand.**
- `SentinelRifleAttachment.cs` (created 2026-11-24 08:27) is a **read-only inspection stub**: it instantiates the robot and writes hand/arm bone paths to `Artifacts/SentinelRifle/attachment-report.txt`. Status:
  - The batch run at 08:27 (`attachment-inspect.log`) **exited with code 1 before executing**, because the project was open in the Editor (`Temp/UnityLockfile`).
  - The script has **no `.meta`**: the open Editor has not imported it yet.
  - `Library/SentinelRifleAttach.request` **still exists**. When the Editor next refreshes, `[InitializeOnLoad]` will run `Inspect()` automatically. It instantiates the robot into the **currently open scene** and destroys it. Afterwards, check that the scene is not marked dirty before saving.
  - `attachment-report.txt` does not exist.
- **Target bone path:** `Sentinel_Robot/Sentinel_Rig/Hips/Spine/Chest/RightShoulder/RightUpperArm/RightLowerArm/RightHand` [Inspected from the skeleton in `.meta`].
- ⚠ **Scale trap:** `Sentinel_Rig` has local scale **100** and bone local positions are in centimetre-scale units (e.g. RightHand at 0.00385). A rifle parented directly under `RightHand` will inherit a world scale of about **100×** (a ~150 m rifle). Use an intermediate `RightHand_WeaponMount` whose local scale cancels the rig scale (≈0.01), then put the rifle at unit scale under it. Confirm with `lossyScale ≈ (1,1,1)` in the Editor.
- **Hand orientation is unknown:** the bone axes come from the Blender rig (−90° X conversion). The grip offset and rotation must be tuned visually in Play Mode on Idle/Walk/Run.

### 4.7 Support hand [Untested / not implemented]

- No IK exists. Unity's built-in `OnAnimatorIK` works **only for Humanoid** rigs, and this rig is Generic. The **Animation Rigging** package is **not installed** (`Packages/manifest.json`).
- The existing clips do not move the left hand to a fore-end position, so the left hand **will not** reach `Support_Hand_Socket` (0.59 m in front of the grip) without new clips or a two-bone IK solution.

### 4.8 Behaviour with existing animations [Untested; predicted from the clip data]

| Clip | Expected result with a hand-attached rifle |
| --- | --- |
| Idle / Walk / Run / Patrol | Rifle swings with the right arm only, in an unarmed arm pose. Likely clipping into the leg or torso; left hand free. |
| Attack | Right-arm melee swing (≈83° shoulder rotation): the rifle will be **swung like a club**. Does not match a "rifle" enemy. |
| TakeDamage | Follows the hand; probably acceptable. |
| Death | Rifle stays in the hand through a ~1 m fall. It may intersect the floor. Consider detaching and dropping it. |

---

## 5. Readiness for the actual game environment

| Check | Status | Detail |
| --- | --- | --- |
| Robot model | ✔ asset / ✘ prefab | The animated robot exists **only in `SentinelTestScene`**. `Assets/Prefabs/Sentinel.prefab` is still the primitive version (cube Body, **no Animator, driver or adapter**, `_playerTarget` = null, no waypoints, no guard, ID `Sentinel_Test`). |
| Body vs model size | ⚠ | CharacterController h=2 m vs robot ≈2.4–2.6 m; eye offset 1.5 m vs head ≈2.3 m. The robot meshes have **no colliders**, so player shots resolved through the CharacterController will **miss the upper ~0.5 m** of the visible robot. Sight from the eye position is also lower than it looks. |
| NavMesh compatibility | ✔ agent settings | Humanoid agent type (r 0.5, h 2, slope 45°, climb 0.75) matches the Sentinel's CharacterController. |
| Test-scene NavMesh | ⚠ | `SentinelNavMesh 1.asset` was baked with `ObstacleWall` active, but the wall is now inactive. `SentinelNavMesh.asset` is an unreferenced old bake. |
| `Cargo_Blockout` (game env., in Build Settings) | ✘ no Sentinel | Has a NavMeshSurface with a baked NavMesh, walls, central cargo and `Player` (tag Player, FirstPersonController, CC r 0.3/h 2). It also has `Navigation_TestAgent` (a NavMeshAgent test object) and an `Enemy_Start` marker. **No Sentinel, no Commander, no patrol/guard points.** Ship decks, stairs and doors do not exist yet. |
| Player damage | ✘ | `FirstPersonController` player has **no `ISentinelDamageable`**. The Sentinel will warn once and deal no damage until a player health adapter exists. `SentinelTargetHealth` is the test stand-in. |
| Scene references (test scene) | ✔ | All 17 script/asset GUIDs in `SentinelTestScene.unity` resolve [Inspected]. Commander (`CommanderTest`) and `CommanderSentinelTest` are **inactive** by design. |
| Test weapon | ⚠ doc mismatch | `Sentinel_Test_Scene.md` mentions WASD and left-click firing, but `SentinelTestScene` contains **no FirstPersonController and no `SentinelTestWeapon`**. `TestPlayerTarget` is stationary. Damage the Sentinel through `SentinelEvaluationControls → Damage Sentinel 25`. |
| Build Settings | ⚠ | Lists `Flanker_Test` **twice** plus `Cargo_Blockout`. `SentinelTestScene` is not included (fine for a test scene). |
| Commander in game | ✘ | Not placed in `Cargo_Blockout`; depends on D1/D2. |
| Weapon setup | ✘ | See §4.6–4.8; no shooting code. |
| Runtime spawning | ⚠ | SentinelAgent's `_playerTarget`, `_patrolWaypoints`, `_guardPosition`, `_agentId` are private serialized fields with **no runtime setters**. Code spawning needs an initializer API (Sentinel-owned, so allowed) or pre-wired scene instances. A spawn point must be within 0.75 m of the NavMesh. The home post is captured in `Awake`, so set the guard point before or at instantiation. The Commander is found automatically within 2 s. |

---

## 6. Verification

### 6.1 Compilation

| Check | Source | Result |
| --- | --- | --- |
| Unity compile in the open Editor, session started 2026-11-24 08:24 | **[Log: project]** `Logs/Editor.log` | Initial refresh compiled all scripts; **0 `error CS` lines**. Excludes `SentinelRifleAttachment.cs`, which was created at 08:27 and not yet imported. |
| Standalone `csc` compile, **this audit**, 2026-11-24 | Same method as `Tools/Compile-Sentinel.ps1`, output to a temp folder outside the project; reads the current working-tree sources | Runtime: 31 files, **0 errors**, 21 warnings. Editor: 13 files (incl. `SentinelRifleAttachment.cs`), **0 errors**. *Not a Unity import, domain reload or Play Mode run.* |
| Rifle batch build/audit, 2026-11-24 08:21–08:22 | **[Log: project]** `Artifacts/SentinelRifle/polish*-unity.log` | Exit 0, `SENTINEL_RIFLE_VERIFY_PASS`, `SENTINEL_RIFLE_AUDIT_PASS` |

Warnings (all pre-existing, none block): CS0618 obsolete Find APIs (Commander, CorridorHeatmapTracker); CS0649 "never assigned" on serialized fields (false positives outside Unity); CS0067 unused events on CommanderAgent and MockSquadAgent.

### 6.2 Missing references

- None found in `SentinelTestScene.unity`: all GUIDs resolve [Inspected].
- **Unassigned by design:** `Sentinel.prefab` has `_playerTarget` = null, an empty route and no guard. It would warn and never engage when dropped into a scene.
- `SentinelRifleAttachment.cs` has no `.meta` (not imported yet).

### 6.3 Console errors and warnings

- The current `Editor.log` and `Editor-prev.log` contain no compile errors and no exceptions [Log: project].
- Expected runtime warnings in isolated tests: `No active CommanderAgent found in scene` (Commander inactive); `Target has no ISentinelDamageable` on any target without health; `Cannot reach destination for <task>` on blocked paths.
- The Play Mode console was not observed during this audit.

### 6.4 Tests already performed

| Test | Where it ran | Result |
| --- | --- | --- |
| `SentinelAnimationSetupCheck.Run`, 2026-11-23 18:05 | **Actual project** [Log: project] | PASS: all checks |
| `SentinelRegressionChecks.Run`, 2026-11-23 18:05 | **Actual project** [Log: project `Logs/SentinelCompile/Regression.log`] | PASS: **12 regression + 9 integration** (older suite) |
| `SentinelRegressionChecks.Run`, current suite | **Scratch copy** [Reported: copy] | PASS: 13 regression + 15 integration |
| `SentinelCommanderPlayModeProbe.Run` | **Scratch copy** [Reported: copy] | PASS 14/14 |
| `SentinelPatrolPlayModeProbe` (4 entry points) | **Scratch copy** [Reported: copy] | PASS |
| `SentinelTestSceneTools.VerifyPaths` | **Scratch copy** [Reported: copy] | PASS |
| Impact-frame damage (temporary SentinelAttackHitProbe, since removed) | Manual, per `Sentinel_AI.md` | PASS: 7 hits × 15 |
| Rifle build, audit, 8-view renders | **Actual project** [Log: project] | PASS |
| Standalone compile | This audit (temp output) | PASS |

> The current 13 + 15 suite and all Play Mode probes have **never run against the actual project folder**. Their logs were not kept in the project.

### 6.5 Tests not yet performed

- The manual Play Mode checklist in `Sentinel_Test_Scene.md` (patrol, navigation, detection, tracking, choke-point, combat, death, Commander), observed by a person.
- Current regression suite and probes on the **actual** project (Editor closed).
- `SentinelRifleAttachment.Inspect` (never executed).
- Rifle attached to RightHand: scale, offset, rotation, clipping in every clip.
- Support-hand placement and IK.
- Any shooting / ranged attack.
- Sentinel in `Cargo_Blockout` or any real level; NavMesh paths there; multi-deck navigation.
- Damage to and from the real player; game-over flow.
- Multiple Sentinels with unique IDs in one scene with the Commander enabled.
- Performance with several rifles (180 renderers each).
- URP compatibility of the materials (only relevant if the pipeline changes).

---

## 7. Prioritised remaining tasks

Ownership: **[You]** Sentinel-owned; **[Vindya]** needs teammate approval; **[Team]** shared decision.

### P0: Stabilise and protect current work
- [ ] **[You]** Back up or commit the untracked Sentinel assets (§1.2). Make sure `.meta` files go with them.
- [ ] **[Vindya]** Review and approve the `CommanderAgent.cs` and `AgentAnimatorDriver.cs` changes (D1). Until merged by the owner, the Sentinel branch cannot be merged cleanly.
- [ ] **[Vindya]** Decide on the Commander `Awake`/`OnDisable` lifecycle fix (D2).
- [ ] **[You]** With the Editor **closed**, run on the **actual project**: `SentinelRegressionChecks.Run` (expect 13 + 15), `SentinelCommanderPlayModeProbe.Run`, `SentinelPatrolPlayModeProbe.Run` (+3 variants), `SentinelTestSceneTools.VerifyPaths`, `SentinelAnimationSetupCheck.Run`. Keep the logs in `Logs/`.
- [ ] **[You]** Re-bake the test-scene NavMesh with `ObstacleWall` in its intended state, and delete the unreferenced `SentinelNavMesh.asset` only after confirming it is unused.
- [ ] **[You]** Fix the `Sentinel_Test_Scene.md` test-weapon statement, or add a player with `SentinelTestWeapon` to the test scene.

### P1: Rifle attachment (visual only, no behaviour change)
- [ ] **[You]** Run `SentinelRifleAttachment.Inspect` (Editor menu or batch with the Editor closed), read `attachment-report.txt`, and record the RightHand bone world position, rotation and **lossyScale**.
- [ ] **[You]** In `SentinelTestScene`, add `RightHand_WeaponMount` under `.../RightLowerArm/RightHand` with a local scale that cancels the rig scale (target rifle `lossyScale = 1`). Place the `Sentinel_Rifle` prefab instance under it with `Grip_Socket` at the palm.
- [ ] **[You]** Tune the mount's local position and rotation so the muzzle points forward in Idle. Check Walk, Run, TakeDamage and Death for clipping. Record final values.
- [ ] **[You]** Decide the death behaviour: keep in hand, or detach and drop (needs a collider plus Rigidbody on drop).

### P2: Armed animation set and support hand
- [ ] **[You]** Author or obtain **rifle-held** clips for this Generic rig: Rifle Idle, Rifle Walk, Rifle Run, Aim/Fire (and optionally Reload, Hit, Death). Import into `Sentinel_Robot.fbx` or a separate FBX on the same rig.
- [ ] **[You]** Rewire `SentinelAnimator.controller`: replace the unarmed locomotion, remove or repurpose the orphan `Patrol` state, remove the unused `Die` trigger, add an `AttackType` int if both melee and ranged are kept.
- [ ] **[You]** Add `AnimEvent_AttackComplete` (and `AnimEvent_AttackHit` / fire event) to the new attack/fire clips. Optionally add `AnimEvent_Footstep` to Walk/Run.
- [ ] **[You/Team]** Support hand: either bake the left hand onto the fore-end in the new clips, **or** install Animation Rigging and add a Two-Bone IK constraint targeting `Support_Hand_Socket` (Generic-compatible). Package install is a project-wide change, so agree it with the team.

### P3: Shooting integration (inside the existing FSM, no new states)
- [ ] **[You]** Keep the `Attack` state but add a ranged mode: a longer `_attackRange` for ranged, `ICombatActions.Attack(2)` (the contract already defines 2 = Ranged Shot), and on the fire/impact event a raycast or projectile from `Muzzle_Point` toward the target, reusing the existing impact re-validation (LOS from the muzzle, facing, range).
- [ ] **[You]** Give SentinelAgent a serialized reference to the rifle's `Muzzle_Point` (Sentinel-owned component; no change to the shared contract).
- [ ] **[You]** Muzzle flash/VFX at `Muzzle_Point`, ejection effect at `Ejection_Point`, firing audio. Add scene bloom if the emissive cyan should glow.
- [ ] **[You]** Extend the regression suite: ranged hit applied once per shot, blocked by walls between muzzle and target, no fire when out of range or not facing.
- [ ] **[Team]** Agree damage, fire rate and accuracy balance with the game design.

### P4: Production prefab and game environment
- [ ] **[You]** Rebuild `Sentinel.prefab` from the animated `SentinelTest` setup (robot, Animator, driver, adapter, rifle mount), with a unique-ID strategy. **Do not run "Prepare Evaluation Scene and Prefab"** until it is updated for the robot model; it still expects the old Y Bot `SentinelVisual`.
- [ ] **[You]** Match the body to the model: raise CharacterController height (≈2.4–2.5 m, centre ≈1.2) **only after checking NavMesh agent height and clearance**, raise `_eyeOffset` to ≈2.2 m, and add hit colliders on the robot (child colliders under the SentinelAgent root so `GetComponentInParent<SentinelAgent>` still resolves).
- [ ] **[You]** Add a runtime initializer such as `Initialize(Transform player, Transform guard, Transform[] route, string id)` for spawned Sentinels. Called before `OnEnable`, or have it reset `_homePost`/`_post`.
- [ ] **[Team]** Player health: implement `ISentinelDamageable` on the real player (or an adapter), plus game-over/respawn.
- [ ] **[You]** Place Sentinels, guard posts and patrol routes in `Cargo_Blockout` (and later the ship decks). Re-bake its NavMeshSurface with characters excluded. Verify paths.
- [ ] **[Vindya/You]** Place an enabled Commander in the game scene with its player reference and corridor graph.

### P5: Final game-environment verification
- [ ] Full manual checklist in the real level: patrol, detect, chase, ranged attack, contact loss → investigate → search → Commander completion → patrol resume, damage both ways, death.
- [ ] Several Sentinels plus Commander: distinct orders, blocked retry, no duplicate IDs in logs.
- [ ] Performance pass: rifle draw calls (merge per material or LOD), perception cost with several agents.
- [ ] Clean console in Play Mode (no errors; only expected warnings).
- [ ] Update `Sentinel_AI.md` and `Sentinel_Test_Scene.md`, and mark this report superseded.

---

## 8. Quick reference: exact next steps (in order)

1. Close the Unity Editor, then back up or commit the untracked Sentinel files (your call).
2. Run the five verification entry points on the **actual project** (§7 P0) and keep the logs.
3. Ask Vindya to review the `CommanderAgent.cs` / `AgentAnimatorDriver.cs` diffs.
4. Reopen the Editor and run **`SentinelRifleAttachment.Inspect`** (the pending `Library/SentinelRifleAttach.request` will trigger it on import). Read `Artifacts/SentinelRifle/attachment-report.txt`.
5. Create the scale-compensated `RightHand_WeaponMount` in `SentinelTestScene` and fit the rifle (§7 P1).
6. Plan the armed animation set and the support-hand approach (§7 P2) before writing any shooting code (§7 P3).
