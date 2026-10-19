namespace Halazia.Shared.Squad
{
    /// <summary>
    /// Enumerates all tactical task types the Commander can assign to a squad agent via SquadOrder.
    /// Priority ranking (highest to lowest): Pursue > Flank > CoverRoute > Investigate > HoldPosition > Patrol > Idle.
    /// </summary>
    public enum SquadTaskType
    {
        /// <summary>
        /// Stand-by fallback. Agent does nothing. Assigned when Commander has no better task available.
        /// Priority: 0.0
        /// </summary>
        Idle,

        /// <summary>
        /// Standard waypoint patrol across assigned deck zones. Used outside of combat mode.
        /// Primary role: Sentinel.
        /// Priority: 0.30
        /// </summary>
        Patrol,

        /// <summary>
        /// Move cautiously toward a position (e.g. last known player location or suspicious sound source)
        /// to search for the player. Fires OnTaskArrived when the destination is reached.
        /// Primary role: Hunter (secondary: any).
        /// Priority: 0.50
        /// </summary>
        Investigate,

        /// <summary>
        /// Sprint directly toward TargetPosition (last known player location).
        /// At close range (<=1.8m), execute melee attack while locking movement.
        /// Primary role: Hunter.
        /// Priority: 1.0
        /// </summary>
        Pursue,

        /// <summary>
        /// Navigate to a lateral position to create a pincer with the pursuing Hunter.
        /// Once in position or when line-of-sight is gained, initiate surprise engagement.
        /// Primary role: Flanker.
        /// Priority: 0.80
        /// </summary>
        Flank,

        /// <summary>
        /// Stop and guard current or designated position. Used for ambush setups or wounded agents.
        /// Primary roles: Flanker, Sentinel.
        /// Priority: 0.40
        /// </summary>
        HoldPosition,

        /// <summary>
        /// Rush to the predicted escape-route choke point (TargetNodeId) and hold position with
        /// weapon raised to deny the player that corridor. Destination is chosen by the Commander's
        /// Markov corridor prediction system.
        /// Primary role: Sentinel.
        /// Priority: 0.65 – 0.77 (scales with Markov prediction confidence)
        /// </summary>
        CoverRoute,

        /// <summary>
        /// Fall back toward the Commander or safe zone when overwhelmed or out of combat range.
        /// Priority: 0.90
        /// </summary>
        Retreat
    }
}
