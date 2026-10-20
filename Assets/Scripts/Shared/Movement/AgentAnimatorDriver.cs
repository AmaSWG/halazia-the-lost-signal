using System;
using System.Collections;
using UnityEngine;

namespace Halazia.Shared.Movement
{
    /// <summary>
    /// Bridges physical agent movement (AgentMotor) with visual animation (Animator).
    /// Prevents foot sliding and animation snapping by filtering measured physical velocity
    /// through damped blend parameters, and coordinates attack/combat animation states with locomotion.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class AgentAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private Animator animatorTarget;
        [Header("Motor Binding")]
        [Tooltip("Reference to the motor component. If null, will search on this GameObject or parent.")]
        [SerializeField] private MonoBehaviour _motorSource;

        [Header("Locomotion Parameter Names")]
        [Tooltip("Float parameter representing horizontal movement speed.")]
        [SerializeField] private string _speedParamName = "Speed";

        [Tooltip("Optional boolean parameter indicating whether agent is active.")]
        [SerializeField] private string _isMovingParamName = "IsMoving";

        [Header("Combat & Action Parameter Names")]
        [Tooltip("Trigger parameter for initiating an attack animation.")]
        [SerializeField] private string _attackTriggerName = "Attack";

        [Tooltip("Integer parameter specifying attack variation (0=Light Melee, 1=Heavy Slash, 2=Ranged Shot).")]
        [SerializeField] private string _attackTypeParamName = "AttackType";

        [Tooltip("Trigger parameter for hit/damage reaction.")]
        [SerializeField] private string _hitTriggerName = "Hit";

        [Tooltip("Boolean parameter indicating death state.")]
        [SerializeField] private string _isDeadParamName = "IsDead";

        [Header("Motion Filtering")]
        [Tooltip("Damping time (seconds) for blending Speed parameter to eliminate visual popping.")]
        [SerializeField] private float _speedDampTime = 0.12f;

        [Tooltip("If true, parameter is normalized between 0 and 1 using maxSpeed.")]
        [SerializeField] private bool _normalizeSpeed = false;

        [Tooltip("Expected maximum speed in m/s used for normalization.")]
        [SerializeField] private float _expectedMaxSpeed = 4.2f;

        [Header("Strafe / 2D Blend Tree Parameters")]
        [Tooltip("Float parameter name for local-space right/left velocity (X axis). Used for 2D Freeform Directional blend tree.")]
        [SerializeField] private string _velXParamName = "VelX";

        [Tooltip("Float parameter name for local-space forward/backward velocity (Z axis). Negative = retreating.")]
        [SerializeField] private string _velZParamName = "VelZ";

        [Header("Attack Locomotion Lock")]
        [Tooltip("If true, temporarily locks motor translation during attack animations to eliminate sliding.")]
        [SerializeField] private bool _lockMovementOnAttack = true;

        [Tooltip("Failsafe unlock duration (seconds) if animation events are omitted from animation clip.")]
        [SerializeField] private float _defaultAttackDuration = 0.85f;

        // Events for combat system integration
        public event Action OnAttackHitWindow;
        public event Action OnAttackFinished;
        public event Action OnFootstep;

        private Animator _animator;
        private IAgentMotor _motor;
        private Coroutine _attackLockCoroutine;

        // Cached parameter hashes (avoids string allocation & lookup per frame - vital for SE3032 optimization marks)
        private int _speedParamHash;
        private int _isMovingParamHash;
        private int _attackTriggerHash;
        private int _attackTypeParamHash;
        private int _hitTriggerHash;
        private int _isDeadParamHash;

        private bool _hasSpeedParam;
        private bool _hasIsMovingParam;
        private bool _hasAttackTrigger;
        private bool _hasAttackTypeParam;
        private bool _hasHitTrigger;
        private bool _hasIsDeadParam;
        private int _velXParamHash;
        private int _velZParamHash;
        private bool _hasVelXParam;
        private bool _hasVelZParam;

        private void Awake()
        {
            _animator = animatorTarget != null
                ? animatorTarget
                : GetComponent<Animator>();

            // Resolve IAgentMotor interface reference
            if (_motorSource is IAgentMotor motorInterface)
            {
                _motor = motorInterface;
            }
            else
            {
                _motor = GetComponent<IAgentMotor>() ?? GetComponentInParent<IAgentMotor>();
            }

            // Cache parameter hashes (avoids string allocation & lookup per frame)
            _speedParamHash      = Animator.StringToHash(_speedParamName);
            _isMovingParamHash   = Animator.StringToHash(_isMovingParamName);
            _attackTriggerHash   = Animator.StringToHash(_attackTriggerName);
            _attackTypeParamHash = Animator.StringToHash(_attackTypeParamName);
            _hitTriggerHash      = Animator.StringToHash(_hitTriggerName);
            _isDeadParamHash     = Animator.StringToHash(_isDeadParamName);
            _velXParamHash       = Animator.StringToHash(_velXParamName);
            _velZParamHash       = Animator.StringToHash(_velZParamName);

            // Verify parameter existence on Controller to avoid runtime warnings
            _hasSpeedParam      = HasParameter(_animator, _speedParamHash);
            _hasIsMovingParam   = HasParameter(_animator, _isMovingParamHash);
            _hasAttackTrigger   = HasParameter(_animator, _attackTriggerHash);
            _hasAttackTypeParam = HasParameter(_animator, _attackTypeParamHash);
            _hasHitTrigger      = HasParameter(_animator, _hitTriggerHash);
            _hasIsDeadParam     = HasParameter(_animator, _isDeadParamHash);
            _hasVelXParam       = HasParameter(_animator, _velXParamHash);
            _hasVelZParam       = HasParameter(_animator, _velZParamHash);
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

            // 3. Feed local-space velocity into VelX / VelZ for 2D Freeform Directional blend tree.
            //    Normalized by max speed so the blend tree range is [-1, 1] on both axes.
            if (_hasVelXParam || _hasVelZParam)
            {
                Vector3 localVel = _motor.LocalVelocity;
                float normFactor = _expectedMaxSpeed > 0.01f ? 1.0f / _expectedMaxSpeed : 1.0f;

                if (_hasVelXParam)
                    _animator.SetFloat(_velXParamHash, localVel.x * normFactor, _speedDampTime, deltaTime);
                if (_hasVelZParam)
                    _animator.SetFloat(_velZParamHash, localVel.z * normFactor, _speedDampTime, deltaTime);
            }
        }

        #region Combat & Attack Action Triggers

        /// <summary>
        /// Fires an attack animation on the Animator and coordinates movement locking with the motor.
        /// </summary>
        /// <param name="attackType">Index selecting specific attack variation (0=Light Melee, 1=Heavy Slash, 2=Shot).</param>
        public void PlayAttack(int attackType = 0) => TriggerAttack(attackType);

        /// <summary>
        /// Fires an attack animation on the Animator and coordinates movement locking with the motor.
        /// </summary>
        /// <param name="attackType">Index selecting specific attack variation (0=Light Melee, 1=Heavy Slash, 2=Shot).</param>
        public void TriggerAttack(int attackType = 0)
        {
            if (_animator == null) return;

            if (_hasAttackTypeParam)
            {
                _animator.SetInteger(_attackTypeParamHash, attackType);
            }

            if (_hasAttackTrigger)
            {
                _animator.SetTrigger(_attackTriggerHash);
            }

            if (_lockMovementOnAttack && _motor != null)
            {
                _motor.SetMovementLocked(true);

                if (_attackLockCoroutine != null)
                {
                    StopCoroutine(_attackLockCoroutine);
                }
                _attackLockCoroutine = StartCoroutine(FailsafeUnlockRoutine(_defaultAttackDuration));
            }
        }

        /// <summary>
        /// Triggers hit/stagger reaction animation when taking damage.
        /// </summary>
        public void TriggerHit()
        {
            if (_hasHitTrigger)
            {
                _animator.SetTrigger(_hitTriggerHash);
            }
        }

        /// <summary>
        /// Sets agent to death animation state and ceases all movement.
        /// </summary>
        public void TriggerDie()
        {
            if (_hasIsDeadParam)
            {
                _animator.SetBool(_isDeadParamHash, true);
            }

            if (_motor != null)
            {
                _motor.Stop();
                _motor.SetMovementLocked(true);
            }
        }

        private IEnumerator FailsafeUnlockRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            CompleteAttack();
        }

        private void CompleteAttack()
        {
            if (_attackLockCoroutine != null)
            {
                StopCoroutine(_attackLockCoroutine);
                _attackLockCoroutine = null;
            }

            if (_motor != null)
            {
                _motor.SetMovementLocked(false);
            }

            OnAttackFinished?.Invoke();
        }

        #endregion

        #region Animation Event Hooks (Called directly from Animation Clips in Unity)

        /// <summary>
        /// Hook called on the exact frame of weapon impact/firing.
        /// Attach as an Animation Event on the attack animation clip.
        /// </summary>
        public void AnimEvent_AttackHit()
        {
            OnAttackHitWindow?.Invoke();
        }

        /// <summary>
        /// Hook called when the attack recovery window concludes.
        /// Attach as an Animation Event at the end of the attack animation clip.
        /// </summary>
        public void AnimEvent_AttackComplete()
        {
            CompleteAttack();
        }

        /// <summary>
        /// Hook called when a foot strikes the deck.
        /// Attach as an Animation Event on Walk/Run clips for footstep audio.
        /// </summary>
        public void AnimEvent_Footstep()
        {
            OnFootstep?.Invoke();
        }

        #endregion

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
