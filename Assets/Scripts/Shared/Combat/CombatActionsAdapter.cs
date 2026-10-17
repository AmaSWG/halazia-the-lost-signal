using UnityEngine;
using Halazia.Shared.Movement;

namespace Halazia.Shared.Combat
{
    /// <summary>
    /// Adapter component that implements ICombatActions by delegating to AgentAnimatorDriver.
    /// Add this alongside AgentAnimatorDriver on any combat-capable agent prefab.
    /// The IS layer (Commander) resolves ICombatActions at Awake and never touches AgentAnimatorDriver.
    /// </summary>
    [RequireComponent(typeof(AgentAnimatorDriver))]
    [DisallowMultipleComponent]
    public class CombatActionsAdapter : MonoBehaviour, ICombatActions
    {
        private AgentAnimatorDriver _driver;

        private void Awake()
        {
            _driver = GetComponent<AgentAnimatorDriver>();
        }

        /// <inheritdoc/>
        public void Attack(int attackType = 0)
        {
            _driver?.PlayAttack(attackType);
        }

        /// <inheritdoc/>
        public void Hit()
        {
            _driver?.TriggerHit();
        }
    }
}
