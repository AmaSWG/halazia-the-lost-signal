using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Halazia.Sandbox
{
    /// <summary>
    /// Dual-input keyboard controller for moving the test player around the ship environment.
    /// Supports both the New Input System (UnityEngine.InputSystem) and Legacy Input Manager.
    /// Includes smooth character turning and automatic third-person follow camera.
    /// </summary>
    public class TestPlayerMover : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Movement speed in meters/second")]
        [SerializeField] private float _speed = 5.0f;
        [Tooltip("Rotation speed for facing movement direction")]
        [SerializeField] private float _rotationSpeed = 12.0f;

        [Header("Camera Follow")]
        [Tooltip("Automatically attaches and positions the Main Camera behind the player")]
        [SerializeField] private bool _autoFollowCamera = true;
        [SerializeField] private Vector3 _cameraOffset = new Vector3(-4.5f, 2.8f, 0f);

        private CharacterController _characterController;
        private Camera _mainCamera;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _mainCamera = Camera.main;
        }

        private void Start()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Object.FindAnyObjectByType<Camera>();
            }
        }

        private void Update()
        {
            Vector2 input = ReadInput();
            float h = input.x;
            float v = input.y;

            Vector3 move = Vector3.zero;

            // Compute movement relative to world or camera
            if (_mainCamera != null)
            {
                Vector3 camFwd = _mainCamera.transform.forward;
                Vector3 camRight = _mainCamera.transform.right;
                camFwd.y = 0f;
                camRight.y = 0f;
                camFwd.Normalize();
                camRight.Normalize();

                move = camFwd * v + camRight * h;
            }
            else
            {
                move = new Vector3(h, 0f, v);
            }

            if (move.sqrMagnitude > 1f) move.Normalize();

            if (move.sqrMagnitude > 0.01f)
            {
                // Smooth rotation toward movement direction
                Quaternion targetRot = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, _rotationSpeed * Time.deltaTime);

                // Move CharacterController
                Vector3 delta = move * (_speed * Time.deltaTime);
                delta.y = -2.0f * Time.deltaTime; // ground stick

                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(delta);
                }
                else
                {
                    transform.position += delta;
                }
            }
        }

        private void LateUpdate()
        {
            if (_autoFollowCamera && _mainCamera != null)
            {
                Vector3 desiredCamPos = transform.position + _cameraOffset;
                _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, desiredCamPos, 8f * Time.deltaTime);
                Vector3 lookTarget = transform.position + Vector3.up * 1.3f;
                _mainCamera.transform.LookAt(lookTarget);
            }
        }

        private Vector2 ReadInput()
        {
            float h = 0f;
            float v = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) h += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) h -= 1f;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
            {
                try
                {
                    h = Input.GetAxisRaw("Horizontal");
                    v = Input.GetAxisRaw("Vertical");
                }
                catch { }
            }
#endif

            return new Vector2(h, v);
        }
    }
}
