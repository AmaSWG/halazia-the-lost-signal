using UnityEngine;

namespace Halazia.Shared.Combat
{
    /// <summary>
    /// Decoupled contract between the IS decision layer (Commander) and the Graphics animation layer.
    /// Any agent that can fight attaches a component implementing this interface.
    /// The Commander depends only on ICombatActions — it never references AgentAnimatorDriver directly.
    /// </summary>
    public interface ICombatActions
    {
        /// <summary>
        /// Triggers an attack animation.
        /// </summary>
        /// <param name="attackType">0 = Light Melee, 1 = Heavy Slash, 2 = Ranged Shot.</param>
        void Attack(int attackType = 0);

        /// <summary>
        /// Triggers a hit/stagger reaction animation.
        /// </summary>
        void Hit();
    }
}
