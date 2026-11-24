using UnityEngine;
using UnityEngine.Events;

namespace Halazia.Shared.Combat
{
    public class DamageReceiver : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private UnityEvent onDied;

        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;

        private void Awake()
        {
            Health = Mathf.Max(1f, maxHealth);
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f) return;

            Health = Mathf.Max(0f, Health - amount);
            Debug.Log($"Player health: {Health:F0}", this);

            if (!IsAlive)
                onDied?.Invoke();
        }
    }
}