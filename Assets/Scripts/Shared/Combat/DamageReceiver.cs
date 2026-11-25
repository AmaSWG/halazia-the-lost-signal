using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Halazia.AI.Sentinel;

namespace Halazia.Shared.Combat
{
    public class DamageReceiver : MonoBehaviour, ISentinelDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private UnityEvent onDied;

        public float Health { get; private set; }
        public bool IsAlive => Health > 0f;
        private bool gameOver;
        private float previousTimeScale;

        private void Awake()
        {
            Health = Mathf.Max(1f, maxHealth);
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;

            Health = Mathf.Max(0f, Health - amount);
            Debug.Log($"Player health: {Health:F0}", this);

            if (!IsAlive)
            {
                // Only the playable character owns the game-over screen; test targets
                // can continue to use this component without pausing their scenes.
                var playerController = GetComponent<FirstPersonController>();
                if (playerController != null)
                {
                    previousTimeScale = Time.timeScale;
                    gameOver = true;
                    playerController.enabled = false;
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                onDied?.Invoke();
            }
        }

        private void OnGUI()
        {
            if (!gameOver) return;
            float width = Mathf.Min(360f, Screen.width - 40f);
            var panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - 150f) * 0.5f, width, 150f);
            GUI.Box(panel, "GAME OVER");
            if (GUI.Button(new Rect(panel.x + 30f, panel.y + 65f, panel.width - 60f, 45f), "Restart"))
            {
                gameOver = false;
                Time.timeScale = previousTimeScale;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void OnDestroy()
        {
            if (gameOver) Time.timeScale = previousTimeScale;
        }
    }
}
