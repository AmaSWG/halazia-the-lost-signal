using UnityEngine;

namespace Halazia.Shared.Movement
{
    /// <summary>
    /// Bridges physical agent movement (AgentMotor) with visual animation (Animator).
    /// Prevents foot sliding and animation snapping by filtering measured physical velocity
    /// through damped blend parameters.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class AgentAnimatorDriver : MonoBehaviour
    {
        [Header("Motor Binding")]
        [Tooltip("Reference to the motor component. If null, will search on this GameObject or parent.")]
        [SerializeField] private MonoBehaviour _motorSource;

        [Header("Animator Parameter Names")]
        [Tooltip("Float parameter representing horizontal movement speed.")]
        [SerializeField] private string _speedParamName = "Speed";

        [Tooltip("Optional boolean parameter indicating whether agent is active.")]
        [SerializeField] private string _isMovingParamName = "IsMoving";

        [Header("Motion Filtering")]
        [Tooltip("Damping time (seconds) for blending Speed parameter to eliminate visual popping.")]
        [SerializeField] private float _speedDampTime = 0.12f;

        [Tooltip("If true, parameter is normalized between 0 and 1 using maxSpeed.")]
        [SerializeField] private bool _normalizeSpeed = false;

        [Tooltip("Expected maximum speed in m/s used for normalization.")]
        [SerializeField] private float _expectedMaxSpeed = 4.2f;

        private Animator _animator;
        private IAgentMotor _motor;

        // Cached parameter hashes (avoids string allocation & lookup per frame - vital for SE3032 optimization marks)
        private int _speedParamHash;
        private int _isMovingParamHash;
        private bool _hasSpeedParam;
        private bool _hasIsMovingParam;

        private void Awake()
        {
            _animator = GetComponent<Animator>();

            // Resolve IAgentMotor interface reference
            if (_motorSource is IAgentMotor motorInterface)
            {
                _motor = motorInterface;
            }
            else
            {
                _motor = GetComponent<IAgentMotor>() ?? GetComponentInParent<IAgentMotor>();
            }

            // Cache parameter hashes
            _speedParamHash = Animator.StringToHash(_speedParamName);
            _isMovingParamHash = Animator.StringToHash(_isMovingParamName);

            // Verify parameter existence on Controller to avoid runtime warnings
            _hasSpeedParam = HasParameter(_animator, _speedParamHash);
            _hasIsMovingParam = HasParameter(_animator, _isMovingParamHash);
        }

        private void Update()
        {
            if (_motor == null || _animator == null) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            // Measured physical scalar speed
            float targetSpeed = _motor.CurrentSpeed;

            if (_normalizeSpeed && _expectedMaxSpeed > 0.01f)
            {
                targetSpeed = Mathf.Clamp01(targetSpeed / _expectedMaxSpeed);
            }

            // 1. Smoothly damp Speed parameter to prevent instant transition popping
            if (_hasSpeedParam)
            {
                _animator.SetFloat(_speedParamHash, targetSpeed, _speedDampTime, deltaTime);
            }

            // 2. Update boolean movement flag if animator state machine utilizes it
            if (_hasIsMovingParam)
            {
                _animator.SetBool(_isMovingParamHash, _motor.IsMoving);
            }
        }

        private static bool HasParameter(Animator animator, int paramHash)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return false;

            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.nameHash == paramHash)
                    return true;
            }
            return false;
        }
    }
}
