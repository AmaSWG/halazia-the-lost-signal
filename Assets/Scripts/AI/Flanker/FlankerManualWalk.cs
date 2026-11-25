using UnityEngine;
using UnityEngine.InputSystem;

// Temporary manual possession for checking actual CharacterController clearance.
// Disable AgentMotor, FlankerAgent and FlankerShooter while using this component.
[RequireComponent(typeof(CharacterController))]
public sealed class FlankerManualWalk : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private float walkSpeed = 2f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float eyeForwardOffset = 0.22f;
    private CharacterController body;
    private Transform originalCameraParent;
    private Vector3 originalCameraPosition;
    private Quaternion originalCameraRotation;
    private float pitch;
    private bool cameraCaptured;

    private void Awake()
    {
        body = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null)
        {
            Debug.LogError("Assign Main Camera to Flanker Manual Walk.", this);
            enabled = false;
            return;
        }
        originalCameraParent = viewCamera.transform.parent;
        originalCameraPosition = viewCamera.transform.position;
        originalCameraRotation = viewCamera.transform.rotation;
        viewCamera.transform.SetParent(null, true);
        cameraCaptured = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || !body.enabled || !viewCamera) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, look.x, 0f, Space.World);
            pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
        }
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        Vector3 direction = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        body.SimpleMove(direction * walkSpeed);
    }

    private void LateUpdate()
    {
        if (!viewCamera || !cameraCaptured) return;
        // Respect the capsule centre: the sandbox root may be at mid-body height.
        Vector3 eye = body.center + Vector3.up * (body.height * 0.40f);
        viewCamera.transform.position = transform.TransformPoint(eye) + transform.forward * eyeForwardOffset;
        viewCamera.transform.rotation = transform.rotation * Quaternion.Euler(pitch, 0f, 0f);
    }

    private void OnDisable()
    {
        if (viewCamera && cameraCaptured)
        {
            viewCamera.transform.SetParent(originalCameraParent, true);
            viewCamera.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);
        }
        cameraCaptured = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
