using System;
using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;

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
        Dead
    }

    public HunterState currentState = HunterState.Idle;
    public float searchDuration = 4f;

    [SerializeField] private string agentId = "Hunter_01";
    [SerializeField] private float maxHealth = 100f;

    private IAgentMotor motor;
    private Vector3 startingPosition;
    private float searchTimer;
    private float currentHealth;
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
        currentState == HunterState.Pursuing;
    public SquadTaskType CurrentTask => currentTask;

    public event Action<ISquadAgent> OnTaskArrived;
    public event Action<ISquadAgent> OnTaskBlocked;
    public event Action<ISquadAgent> OnTargetLost;
    public event Action<ISquadAgent> OnDied;

    private void Awake()
    {
        motor = GetComponent<IAgentMotor>();
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
    }

    private void Update()
    {
        if (!IsAlive || currentState != HunterState.Searching)
            return;

        // Ask the motor to look around rather than rotating directly.
        float angle = Time.time * 60f * Mathf.Deg2Rad;
        Vector3 lookDirection =
            new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

        motor.SetFaceTarget(
            (Vector3?)(transform.position + lookDirection * 3f));

        searchTimer -= Time.deltaTime;

        if (searchTimer > 0f)
            return;

        // A Commander event handler may immediately issue another order.
        int versionBeforeReport = actionVersion;
        OnTargetLost?.Invoke(this);

        if (actionVersion != versionBeforeReport || !IsAlive)
            return;

        currentTask = SquadTaskType.Idle;
        currentState = HunterState.Returning;

        if (!TryMoveTo(startingPosition))
            HandleBlocked();
    }

    private void HearSound(Vector3 position, float radius)
    {
        if (!IsAlive || currentState == HunterState.Pursuing)
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

        // This first version supports the Hunter's primary orders.
        if (order.TaskType != SquadTaskType.Investigate &&
            order.TaskType != SquadTaskType.Pursue)
        {
            motor.Stop();
            motor.SetFaceTarget((Transform)null);
            currentTask = SquadTaskType.Idle;
            currentState = HunterState.Idle;
            OnTaskBlocked?.Invoke(this);
            return;
        }

        currentTask = order.TaskType;
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

        // Update state before reporting, so a new order is not overwritten.
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
}