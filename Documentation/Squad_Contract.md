# Squad AI Interface Contract (`ISquadAgent`)
**Project:** HALAZIA: The Lost Signal  
**Architecture:** Shared Squad Decoupling Contract  
**Namespace:** `Halazia.Shared.Squad`  
**Target Roles:** Amaya (Hunter), Piyumi (Flanker), Chanumi (Sentinel)  
**Coordinator:** Vindya (Commander AI & Locomotion Motor)  

---

## 1. Overview & Purpose
To fulfill SE3062 multi-agent coordination requirements without monolithic tight-coupling, all autonomous squad agents communicate with the **Commander AI** using a standardized, two-way contract:
1. **Outbound Orders:** The Commander assigns tactical orders via `AssignOrder(SquadOrder)`.
2. **Inbound Reports:** Agents inform the Commander of combat developments in real time using C# events (`OnDied`, `OnTaskBlocked`, `OnTaskArrived`, `OnTargetLost`).

> **Rule:** You do **not** need to touch the Commander's code. Simply implement `ISquadAgent` on your agent's `MonoBehaviour` script.

---

## 2. Interface Definition

```csharp
namespace Halazia.Shared.Squad
{
    public interface ISquadAgent
    {
        // Identification & State
        string AgentId { get; }
        AgentRole Role { get; }
        SquadTaskType CurrentTask { get; }
        Vector3 Position { get; }
        float Health01 { get; }      // 0.0f = dead, 1.0f = full health
        bool IsAlive { get; }

        // Order Execution
        void AssignOrder(SquadOrder order);
        void CancelOrder();

        // Reporting Events (Agent -> Commander)
        event Action<ISquadAgent> OnDied;
        event Action<ISquadAgent> OnTaskBlocked;
        event Action<ISquadAgent> OnTaskArrived;
        event Action<ISquadAgent> OnTargetLost;
    }
}
```

---

## 3. Teammate Implementation Requirements

### 3.1 Amaya — Hunter Agent (`AgentRole.Hunter`)
- **Primary Tactical Role:** Frontline pressure, aggressive pursuit, close-quarters combat (1.8m melee).
- **Task Mapping:**
  - `SquadTaskType.Pursue`: Engage in direct sprint toward `order.TargetPosition` using `IAgentMotor.SetPath()`. At ≤1.8m, execute melee attack swing while locking movement.
  - `SquadTaskType.Investigate`: Move cautiously toward `order.TargetPosition` to search for suspicious sound / last known player location.
  - `SquadTaskType.Idle`: Return to ambient patrol or stand by.
- **When to fire events:**
  - `OnTaskArrived?.Invoke(this)` when reaching investigation destination.
  - `OnTargetLost?.Invoke(this)` if player breaks line-of-sight during pursuit.
  - `OnTaskBlocked?.Invoke(this)` if door/bulkhead is blocked.
  - `OnDied?.Invoke(this)` when HP hits 0.

---

### 3.2 Piyumi — Flanker Agent (`AgentRole.Flanker`)
- **Primary Tactical Role:** Lateral envelopment, pincer ambushes, corridor cutoffs.
- **Task Mapping:**
  - `SquadTaskType.Flank`: Traverse toward the lateral position provided in `order.TargetPosition`. Once in position or when player line-of-sight is gained, initiate surprise engagement.
  - `SquadTaskType.Pursue`: Secondary frontal assault if ordered.
  - `SquadTaskType.HoldPosition`: Conceal in cover at `order.TargetPosition` awaiting ambush trigger.
- **When to fire events:**
  - `OnTaskArrived?.Invoke(this)` when reaching the designated flank vantage point.
  - `OnTaskBlocked?.Invoke(this)` if side corridor is barricaded (triggers Commander reallocation).
  - `OnDied?.Invoke(this)` when HP hits 0.

---

### 3.3 Chanumi — Sentinel Agent (`AgentRole.Sentinel`)
- **Primary Tactical Role:** Area security, deck patrol, and preemptive escape-route blockade.
- **Task Mapping:**
  - `SquadTaskType.CoverRoute`: Rush to the predicted escape choke point (`order.TargetPosition` / `order.TargetNodeId`) and hold position with weapon raised to block the escaping player.
  - `SquadTaskType.Patrol`: Standard waypoint patrolling across assigned deck zones.
  - `SquadTaskType.HoldPosition`: Guard current bulkhead entrance.
- **When to fire events:**
  - `OnTaskArrived?.Invoke(this)` when set up at the designated choke point / bulkhead.
  - `OnTaskBlocked?.Invoke(this)` if path to choke point is obstructed.
  - `OnDied?.Invoke(this)` when HP hits 0.

---

## 4. Lifecycle & Registration

1. **Auto-Discovery:** If agents exist in the scene at startup, the Commander automatically discovers and registers them via `ISquadAgent`.
2. **Dynamic Spawning:** If you instantiate agents at runtime, register them directly:
   ```csharp
   CommanderAgent commander = FindObjectOfType<CommanderAgent>();
   commander?.RegisterSquadMember(this);
   ```
3. **Destruction/Death:** Always fire `OnDied?.Invoke(this);` and/or call `commander.UnregisterSquadMember(this)` before destroying your GameObject. The Commander handles null-checks safely, but explicit cleanup guarantees instantaneous task reassignment to remaining squad members.
