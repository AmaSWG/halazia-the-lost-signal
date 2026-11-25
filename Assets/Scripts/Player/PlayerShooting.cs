
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PlayerShooting : MonoBehaviour
{
    [Header("Shooting")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private float shootingRange = 50f;

    private void Update()
    {
        if (ShootPressed())
        {
            Shoot();
        }
    }

    private bool ShootPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    private void Shoot()
    {
        if (aimCamera == null)
        {
            Debug.LogWarning("Aim Camera is not assigned!");
            return;
        }

        Ray ray = aimCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        if (Physics.Raycast(ray, out RaycastHit hit, shootingRange))
        {
            if (hit.collider.CompareTag("AI"))
            {
                Debug.Log("AI Agent detected: " + hit.collider.name);

                if (playerAnimator != null)
                {
                    playerAnimator.SetTrigger("Shoot");
                }

                Debug.Log("Shooting AI Agent: " + hit.collider.name);
            }
            else
            {
                Debug.Log("No AI Agent detected!");
            }
        }
        else
        {
            Debug.Log("Nothing detected!");
        }
    }
}
