namespace Halazia.Shared.Squad
{
    /// <summary>
    /// Identifies the tactical specialization of a squad agent.
    /// Used by the Commander's utility scoring system to calculate role affinity (S_role)
    /// when assigning tasks — e.g. Hunter scores highest for Pursue, Sentinel for CoverRoute.
    /// </summary>
    public enum AgentRole
    {
        /// <summary>Frontline pressure agent. Specializes in direct pursuit and close-quarters melee.</summary>
        Hunter,

        /// <summary>Lateral envelopment agent. Specializes in flanking maneuvers and pincer ambushes.</summary>
        Flanker,

        /// <summary>Area security agent. Specializes in patrol, choke-point blockades, and escape-route denial.</summary>
        Sentinel
    }
}
