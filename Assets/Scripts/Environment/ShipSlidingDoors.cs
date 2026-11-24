using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ShipSlidingDoor : MonoBehaviour
{
    [Header("Door")]
    [SerializeField] private Transform panel;
    [SerializeField] private Vector3 openOffset = new Vector3(3.1f, 0f, 0f);
    [SerializeField, Min(0.1f)] private float speed = 2f;

    [Header("Interaction")]
    [Tooltip("Assign the player root. If empty, uses Main Camera for testing.")]
    [SerializeField] private Transform interactor;

    [Tooltip("Place this at the centre of the doorway, at floor level.")]
    [SerializeField] private Transform interactionPoint;

    [SerializeField, Min(0.1f)] private float interactionDistance = 2.5f;
    [SerializeField] private bool enableKeyboardInteraction = true;
    [SerializeField, Min(0.1f)] private float promptDuration = 5f;

    private bool wasNearby;
    private float promptUntil;

    private Vector3 closedPosition;
    private bool isOpen;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        if (panel == null)
        {
            Debug.LogError("Assign the door Panel.", this);
            enabled = false;
            return;
        }

        closedPosition = panel.localPosition;
    }

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

        Vector3 doorway = interactionPoint != null
            ? interactionPoint.position
            : transform.position;

        Vector3 difference = actor.position - doorway;

        // Measure horizontal distance, with a height check
        // to prevent interaction from another deck.
        float verticalDistance = Mathf.Abs(difference.y);
        difference.y = 0f;

        return verticalDistance <= 2f &&
               difference.sqrMagnitude <=
               interactionDistance * interactionDistance;
    }

    private void Update()
    {
        Vector3 target = closedPosition +
                        (isOpen ? openOffset : Vector3.zero);

        panel.localPosition = Vector3.MoveTowards(
            panel.localPosition, target, speed * Time.deltaTime);

        bool nearby = enableKeyboardInteraction && CanInteract();

        if (nearby && !wasNearby)
            promptUntil = Time.time + promptDuration;

        wasNearby = nearby;

        if (nearby && InteractPressed())
        {
            Toggle();
            promptUntil = 0f; // Hide immediately after using the door.
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
        if (!enableKeyboardInteraction || !CanInteract() || Time.time >= promptUntil)
            return;

        string message = "E - Open/ Close Door";

        GUI.Box(
            new Rect(Screen.width / 2f - 90f,
                     Screen.height - 90f, 180f, 32f),
            message);
    }

    [ContextMenu("Toggle Door (Play Mode)")]
    public void Toggle()
    {
        if (Application.isPlaying)
            isOpen = !isOpen;
    }

    public void Open() => isOpen = true;
    public void Close() => isOpen = false;
}