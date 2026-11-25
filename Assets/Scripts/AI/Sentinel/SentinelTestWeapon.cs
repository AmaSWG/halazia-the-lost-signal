using UnityEngine;
using UnityEngine.InputSystem;

namespace Halazia.AI.Sentinel
{
    /// <summary>Optional evaluation weapon; leaves the team's player controller untouched.</summary>
    public sealed class SentinelTestWeapon : MonoBehaviour
    {
        [SerializeField] private Camera _aimCamera;
        [SerializeField, Min(0f)] private float _damage = 25f;
        [SerializeField, Min(0.01f)] private float _cooldown = 0.3f;
        [SerializeField, Min(0.1f)] private float _range = 50f;
        [SerializeField] private LayerMask _hitMask = ~0;
        private float _nextShot;
        private SentinelTargetHealth _health;

        private void Awake() => _health = GetComponent<SentinelTargetHealth>();
        private void Update()
        {
            if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame) Fire();
        }

        public void Fire()
        {
            if (_aimCamera == null || Time.time < _nextShot || (_health != null && !_health.IsAlive)) return;
            _nextShot = Time.time + _cooldown;
            // The first solid hit blocks the shot, including walls and other agents.
            if (!Physics.Raycast(_aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f)),
                out var hit, _range, _hitMask, QueryTriggerInteraction.Ignore)) return;
            var sentinel = hit.collider.GetComponentInParent<SentinelAgent>();
            if (sentinel != null) sentinel.TakeDamage(_damage);
        }
    }
}
