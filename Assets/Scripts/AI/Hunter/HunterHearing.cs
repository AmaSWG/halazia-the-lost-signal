using System;
using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;
using Halazia.Shared.Combat;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(AgentMotor))]
public class HunterHearing : MonoBehaviour, ISquadAgent
{
    public enum HunterState
    {
        Idle,
        Investigating,
        Searching,
        Returning,
        Pursuing,
        Attacking,
        Dead
    }

    public HunterState currentState = HunterState.Idle;
    public float searchDuration = 4f;

    [SerializeField] private string agentId = "Hunter_01";
    [SerializeField] private float maxHealth = 100f;

    [Header("Melee Combat")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private float meleeRange = 1.8f;
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Vision")]
    [SerializeField] private float viewDistance = 15f;
    [SerializeField, Range(0f, 360f)] private float viewAngle = 120f;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float repathInterval = 0.5f;
    [SerializeField] private float targetMoveThreshold = 0.5f;
    [SerializeField] private bool canSeePlayer;

    private ICombatActions combat;
    private IAgentMotor motor;
    private CharacterController body;

    private Vector3 startingPosition;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 lastPathTarget;

    private float nextAttackTime;
    private float nextPathTime;
    private float searchTimer;
    private float currentHealth;

    private bool hadVisualContact;
    private int actionVersion;
    private SquadTaskType currentTask = SquadTaskType.Idle;

    public string AgentId => agentId;
    public AgentRole Role => AgentRole.Hunter;
    public Vector3 Position => transform.position;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float Health01 =>
        maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public bool IsAlive => currentHealth > 0f;
    public bool IsEngagedWithPlayer =>
        currentState == HunterState.Pursuing ||
        currentState == HunterState.Attacking;
    public SquadTaskType CurrentTask => currentTask;

    public event Action<ISquadAgent> OnTaskArrived;
    public event Action<ISquadAgent> OnTaskBlocked;
    public event Action<ISquadAgent> OnTargetLost;
    public event Action<ISquadAgent> OnDied;

    private void Awake()
    {
        motor = GetComponent<IAgentMotor>();
        combat = GetComponent<ICombatActions>();
        body = GetComponent<CharacterController>();

        startingPosition = transform.position;
        currentHealth = maxHealth;
    }

    private void OnEnable()
    {
        SoundEmitter.SoundEmitted += HearSound;
        motor.OnArrived += HandleArrived;
        motor.OnPathBlocked += HandleBlocked;
    }

    private void OnDisable()
    {
        SoundEmitter.SoundEmitted -= HearSound;

        if (motor != null)
        {
            motor.OnArrived -= HandleArrived;
            motor.OnPathBlocked -= HandleBlocked;
            motor.Stop();
        }

        hadVisualContact = false;
        canSeePlayer = false;
    }

    private void Update()
    {
        if (!IsAlive)
            return;

        canSeePlayer = CheckPlayerVisibility();

        if (canSeePlayer)
        {
            bool newlySeen = !hadVisualContact;
            hadVisualContact = true;
            lastKnownPlayerPosition = playerTransform.position;

            if (newlySeen)
                Debug.Log("Hunter: player spotted!");

            UpdateVisiblePursuit(newlySeen);
            return;
        }

        if (hadVisualContact)
        {
            hadVisualContact = false;

            Debug.Log(
                "Hunter: lost sight—checking last known position.");

            AssignOrder(new SquadOrder(
                SquadTaskType.Investigate,
                lastKnownPlayerPosition));
        }

        if (currentState == HunterState.Searching)
            UpdateSearch();
    }

    private bool CheckPlayerVisibility()
    {
        if (playerTransform == null ||
            !playerTransform.gameObject.activeInHierarchy)
            return false;

        // Use collider centres so capsule pivots do not affect eye height.
        Vector3 eye = body.bounds.center +
            Vector3.up * body.bounds.extents.y * 0.5f;

        Collider playerCollider =
            playerTransform.GetComponentInChildren<Collider>();

        Vector3 target = playerCollider != null
            ? playerCollider.bounds.center
            : playerTransform.position + Vector3.up;

        Vector3 toTarget = target - eye;

        if (toTarget.magnitude > viewDistance)
            return false;

        Vector3 flatDirection = toTarget;
        flatDirection.y = 0f;

        if (flatDirection.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, flatDirection)
                > viewAngle * 0.5f)
            return false;

        // Only objects on the selected obstacle layers block vision.
        return !Physics.Linecast(
            eye, target, obstacleMask,
            QueryTriggerInteraction.Ignore);
    }

    private void UpdateVisiblePursuit(bool newlySeen)
    {
        currentTask = SquadTaskType.Pursue;

        if (TryMeleeAttack())
            return;

        bool leavingAttack =
            currentState == HunterState.Attacking;

        bool enteringPursuit =
            currentState != HunterState.Pursuing;

        currentState = HunterState.Pursuing;

        if (motor.IsMovementLocked)
            return;

        bool targetMoved =
            Vector3.Distance(lastPathTarget,
                lastKnownPlayerPosition) >= targetMoveThreshold;

        bool refreshDue = Time.time >= nextPathTime;

        if (newlySeen || leavingAttack || enteringPursuit ||
            (refreshDue && (targetMoved || !motor.IsMoving)))
        {
            nextPathTime = Time.time + repathInterval;

            if (!TryMoveTo(lastKnownPlayerPosition))
                HandleBlocked();
        }
    }

    private void UpdateSearch()
    {
        float angle = Time.time * 60f * Mathf.Deg2Rad;

        Vector3 lookDirection = new Vector3(
            Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        motor.SetFaceTarget(
            (Vector3?)(transform.position + lookDirection * 3f));

        searchTimer -= Time.deltaTime;

        if (searchTimer > 0f)
            return;

        Debug.Log("Hunter: search finished—target lost.");

        int versionBeforeReport = actionVersion;
        OnTargetLost?.Invoke(this);

        // A listener may synchronously issue a new order.
        if (actionVersion != versionBeforeReport || !IsAlive)
            return;

        currentTask = SquadTaskType.Idle;
        currentState = HunterState.Returning;

        if (!TryMoveTo(startingPosition))
            HandleBlocked();
    }

    private void HearSound(Vector3 position, float radius)
    {
        if (!IsAlive || IsEngagedWithPlayer)
            return;

        if (Vector3.Distance(transform.position, position) > radius)
            return;

        AssignOrder(new SquadOrder(
            SquadTaskType.Investigate, position));

        Debug.Log("Hunter: heard a sound—investigating.");
    }

    public void AssignOrder(SquadOrder order)
    {
        if (!IsAlive)
            return;

        actionVersion++;

        if (order.TaskType == SquadTaskType.Idle)
        {
            CancelOrder();
            return;
        }

        if (order.TaskType != SquadTaskType.Investigate &&
            order.TaskType != SquadTaskType.Pursue)
        {
            CancelOrder();
            Debug.LogWarning(
                $"Hunter: unsupported order {order.TaskType}.");
            return;
        }

        currentTask = order.TaskType;

        // Check visibility here too: orders cannot enable attacks
        // through walls.
        canSeePlayer = CheckPlayerVisibility();

        if (TryMeleeAttack())
            return;

        currentState = order.TaskType == SquadTaskType.Pursue
            ? HunterState.Pursuing
            : HunterState.Investigating;

        if (!TryMoveTo(order.TargetPosition))
            HandleBlocked();
    }

    public void CancelOrder()
    {
        actionVersion++;
        motor.Stop();
        motor.SetFaceTarget((Transform)null);
        currentTask = SquadTaskType.Idle;
        currentState = HunterState.Idle;
    }

    private bool TryMoveTo(Vector3 targetPosition)
    {
        motor.Stop();
        motor.SetFaceTarget((Transform)null);

        if (!NavMesh.SamplePosition(
                transform.position, out NavMeshHit start,
                2f, NavMesh.AllAreas))
            return false;

        if (!NavMesh.SamplePosition(
                targetPosition, out NavMeshHit target,
                2f, NavMesh.AllAreas))
            return false;

        NavMeshPath path = new NavMeshPath();

        if (!NavMesh.CalculatePath(
                start.position, target.position,
                NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete ||
            path.corners.Length == 0)
            return false;

        lastPathTarget = targetPosition;
        motor.SetPath(path.corners);
        return true;
    }

    private void HandleArrived()
    {
        if (!IsAlive)
            return;

        if (currentState == HunterState.Returning)
        {
            currentState = HunterState.Idle;
            currentTask = SquadTaskType.Idle;
            Debug.Log("Hunter: back at starting position.");
            return;
        }

        if (currentState != HunterState.Investigating &&
            currentState != HunterState.Pursuing)
            return;

        if (canSeePlayer)
        {
            // Continue engagement instead of searching for a visible player.
            OnTaskArrived?.Invoke(this);
            return;
        }

        currentState = HunterState.Searching;
        currentTask = SquadTaskType.Investigate;
        searchTimer = searchDuration;

        Debug.Log("Hunter: arrived—searching.");
        OnTaskArrived?.Invoke(this);
    }

    private void HandleBlocked()
    {
        if (!IsAlive)
            return;

        motor.Stop();
        motor.SetFaceTarget((Transform)null);
        currentState = HunterState.Idle;
        currentTask = SquadTaskType.Idle;

        Debug.LogWarning("Hunter: cannot reach destination.");
        OnTaskBlocked?.Invoke(this);
    }

    public void TakeDamage(float damage)
    {
        if (!IsAlive || damage <= 0f)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (IsAlive)
            return;

        actionVersion++;
        motor.Stop();
        motor.SetFaceTarget((Transform)null);
        currentState = HunterState.Dead;
        currentTask = SquadTaskType.Idle;
        OnDied?.Invoke(this);
    }

    private bool TryMeleeAttack()
    {
        if (!IsAlive || !canSeePlayer ||
            playerTransform == null ||
            currentTask != SquadTaskType.Pursue)
            return false;

        float distance = Vector3.Distance(
            transform.position, playerTransform.position);

        if (distance > meleeRange)
            return false;

        motor.Stop();
        motor.SetFaceTarget(playerTransform);
        currentState = HunterState.Attacking;

        if (Time.time >= nextAttackTime &&
            !motor.IsMovementLocked)
        {
            nextAttackTime = Time.time + attackCooldown;
            combat?.Attack(0);
            Debug.Log("Hunter: melee attack!");
        }

        return true;
    }
}