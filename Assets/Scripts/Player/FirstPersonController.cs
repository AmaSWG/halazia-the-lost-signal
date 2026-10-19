using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [SerializeField] private Transform playerCamera;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float gravity = -20f;

    [Header("Running and Footsteps")]
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private float runningFootstepInterval = 0.45f;

    private SoundEmitter soundEmitter;
    private float footstepTimer;

    private CharacterController controller;
    private float verticalSpeed;
    private float cameraPitch;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        soundEmitter = GetComponent<SoundEmitter>();

        if (playerCamera == null)
        {
            Debug.LogError("Assign PlayerCamera in the Inspector.", this);
            enabled = false;
        }
    }

    private void Start()
    {
        SetCursorLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        if (mouse.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        // Look left/right by rotating the Player.
        Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
        transform.Rotate(Vector3.up * look.x);

        // Look up/down by rotating the camera.
        cameraPitch -= look.y;
        cameraPitch = Mathf.Clamp(cameraPitch, -80f, 80f);

        playerCamera.localRotation =
            Quaternion.Euler(cameraPitch, 0f, 0f);

        // Read the WASD keys.
        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;

        // Keep diagonal movement at the same speed.
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 movement =
            transform.right * input.x +
            transform.forward * input.y;

        bool isRunning =
        keyboard.leftShiftKey.isPressed &&
        input.sqrMagnitude > 0.01f;

        movement *= isRunning ? runSpeed : moveSpeed;

        // Apply gravity.
        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        verticalSpeed += gravity * Time.deltaTime;
        movement.y = verticalSpeed;

        // Measure actual movement so pushing against a wall
        // does not generate running footsteps.
        Vector3 positionBeforeMove = transform.position;

        controller.Move(movement * Time.deltaTime);

        Vector3 displacement = transform.position - positionBeforeMove;
        displacement.y = 0f;

        float actualSpeed = Time.deltaTime > 0f
            ? displacement.magnitude / Time.deltaTime
            : 0f;

        if (isRunning && controller.isGrounded && actualSpeed > 0.5f)
        {
            footstepTimer += Time.deltaTime;

            if (footstepTimer >= runningFootstepInterval)
            {
                footstepTimer = 0f;
                soundEmitter?.EmitRunningFootstep();
            }
        }
        else
        {
            footstepTimer = 0f;
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked
            ? CursorLockMode.Locked
            : CursorLockMode.None;

        Cursor.visible = !locked;
    }

    private void OnDisable()
    {
        SetCursorLocked(false);
    }
}