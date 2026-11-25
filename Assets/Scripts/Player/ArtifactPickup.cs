
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ArtifactPickup : MonoBehaviour
{
    [Header("Interaction")]
    [SerializeField] private Transform interactor;
    [SerializeField] private float interactionDistance = 2.5f;

    private bool collected;

    private bool CanInteract()
    {
        Transform actor = interactor;

        if (actor == null)
        {
            Camera camera = Camera.main;

            if (camera == null)
                return false;

            actor = camera.transform;
        }

        Vector3 difference = actor.position - transform.position;

        float verticalDistance = Mathf.Abs(difference.y);
        difference.y = 0f;

        return verticalDistance <= 2f &&
               difference.sqrMagnitude <=
               interactionDistance * interactionDistance;
    }

    private void Update()
    {
        if (collected || !CanInteract())
            return;

        if (InteractPressed())
        {
            collected = true;

            Debug.Log("Artifact collected!");

            Destroy(gameObject);
        }
    }

    private bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null &&
               Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.E);
#else
        return false;
#endif
    }

    private void OnGUI()
    {
        if (collected || !CanInteract())
            return;

        GUI.Box(
            new Rect(Screen.width / 2f - 100f,
                     Screen.height - 90f,
                     200f, 32f),
            "E - Pick Up Artifact"
        );
    }
}
