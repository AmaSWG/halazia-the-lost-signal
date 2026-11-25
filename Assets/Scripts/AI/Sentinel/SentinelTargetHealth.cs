using UnityEngine;
using UnityEngine.Events;

namespace Halazia.AI.Sentinel
{
    /// <summary>Optional target health for the current player, which has no health system.
    /// A future player health adapter can implement ISentinelDamageable instead.</summary>
    [DisallowMultipleComponent]
    public sealed class SentinelTargetHealth : MonoBehaviour, ISentinelDamageable
    {
        [SerializeField, Min(0.01f)] private float _maxHealth = 100f;
        [SerializeField] private UnityEvent _onDied = new UnityEvent();
        public float CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0f;
        private void Awake() => CurrentHealth = Mathf.Max(0.01f, _maxHealth);
        public void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            CurrentHealth = Mathf.Clamp(CurrentHealth - damage, 0f, _maxHealth);
            if (!IsAlive) _onDied.Invoke();
        }
        private void OnValidate() => _maxHealth = Mathf.Max(0.01f, _maxHealth);
    }
}
