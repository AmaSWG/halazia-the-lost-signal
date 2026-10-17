// ─────────────────────────────────────────────────────────────────────────────
// COMPATIBILITY SHIM
// The canonical definitions have moved to Assets/Scripts/Shared/Squad/ISquadAgent.cs
// (Halazia.Shared.Squad namespace) so teammates can import from there directly.
//
// This file keeps the Halazia.AI.Commander namespace working without editing
// SquadUtilityAllocator, MockSquadAgent, or SandboxAutoBuilder.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

namespace Halazia.AI.Commander
{
    // Re-export enums so existing code in this namespace compiles unchanged
    using AgentRole     = Halazia.Shared.Squad.AgentRole;
    using SquadTaskType = Halazia.Shared.Squad.SquadTaskType;
    using SquadOrder    = Halazia.Shared.Squad.SquadOrder;

    /// <summary>
    /// Local alias kept for backward-compatibility with SquadUtilityAllocator and MockSquadAgent.
    /// Extends the shared contract with the new reporting events required by Fix 3.
    /// </summary>
    public interface ISquadAgent : Halazia.Shared.Squad.ISquadAgent { }
}
