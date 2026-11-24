using System;
using UnityEngine;
using UnityEngine.AI;
using Halazia.Shared.Movement;
using Halazia.Shared.Squad;
using Halazia.AI.Commander;

public class FlankerAgent : MonoBehaviour, ISquadAgent
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private CommanderAgent commander;

    [Header("Flanking")]
    [SerializeField] private float flankRadius = 6f;
    [SerializeField] private float frontPenalty = 8f;
    [SerializeField] private float switchAdvantage = 2f;
    [SerializeField] private float decisionInterval = 0.5f;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private float sightHeight = 1.5f;

    [Header("Other tasks")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float searchDuration = 4f;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    private IAgentMotor motor;
    private SquadOrder order;
    private float health;
    private float nextDecision;
    private float searchUntil;
    private bool searching;
    private bool arrived;
    private bool blockedReported;
    private bool hasDestination;
    private Vector3 destination;
    private Vector3 blockedDestination;
    private float avoidUntil;
    private int patrolIndex;

    public string AgentId => gameObject.name;
    public AgentRole Role => AgentRole.Flanker;
    public Vector3 Position => transform.position;
    public float CurrentHealth => health;
    public float MaxHealth => maxHealth;
    public bool IsAlive => health > 0f;
    public bool IsEngagedWithPlayer =>
        IsAlive && CurrentTask == SquadTaskType.Flank && CanSeePlayer();
    public SquadTaskType CurrentTask => order.TaskType;

    public event Action<ISquadAgent> OnTaskArrived;
    public event Action<ISquadAgent> OnTaskBlocked;
    public event Action<ISquadAgent> OnTargetLost;
    public event Action<ISquadAgent> OnDied;
    public event Action<ISquadAgent> OnInvestigateComplete;

    private void Awake()
    {
        health = Mathf.Max(1f, maxHealth);
        motor = GetComponent<IAgentMotor>();

        if (motor == null)
        {
            Debug.LogError("Flanker needs AgentMotor.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (motor == null) return;
        motor.OnArrived += HandleArrival;
        motor.OnPathBlocked += HandleBlocked;
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject target = GameObject.FindWithTag("Player");
            if (target != null) player = target.transform;
        }

        if (commander == null)
            commander = FindAnyObjectByType<CommanderAgent>();

        commander?.RegisterSquadMember(this);
    }

    private void OnDisable()
    {
        if (motor != null)
        {
            motor.OnArrived -= HandleArrival;
            motor.OnPathBlocked -= HandleBlocked;
            motor.Stop();
        }

        commander?.UnregisterSquadMember(this);
    }

    public void AssignOrder(SquadOrder newOrder)
    {
        if (!IsAlive) return;

        bool changed = order.TaskType != newOrder.TaskType ||
            Vector3.Distance(order.TargetPosition,
                             newOrder.TargetPosition) > 0.75f;

        order = newOrder;

        if (!changed) return;

        searching = false;
        arrived = false;
        hasDestination = false;
        blockedReported = false;
        motor.Stop();
        motor.SetStrafe(false);
        nextDecision = 0f;
    }

    public void CancelOrder()
    {
        order = SquadOrder.Idle();
        searching = false;
        arrived = false;
        hasDestination = false;
        motor.Stop();
        motor.SetStrafe(false);
    }

    private void Update()
    {
        if (!IsAlive || motor == null) return;

        if (searching)
        {
            if (CanSeePlayer())
            {
                searching = false;
                commander?.ReportPlayerSighted(player.position);
            }
            else if (Time.time >= searchUntil)
            {
                searching = false;
                order = SquadOrder.Idle();
                OnInvestigateComplete?.Invoke(this);
            }

            return;
        }

        if (Time.time < nextDecision) return;
        nextDecision = Time.time + Mathf.Max(0.1f, decisionInterval);

        bool visible = CanSeePlayer();

        if (visible)
            commander?.ReportPlayerSighted(player.position);

        if (CurrentTask == SquadTaskType.Idle)
        {
            motor.Stop();
            return;
        }

        if (CurrentTask == SquadTaskType.Flank)
        {
            EvaluateFlank(visible);
            return;
        }

        if (arrived) return;

        Vector3 target = order.TargetPosition;

        if (CurrentTask == SquadTaskType.Patrol)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                motor.Stop();
                return;
            }

            Transform point = patrolPoints[patrolIndex];
            if (point == null) return;
            target = point.position;
        }

        MoveTo(target);
    }

    private void EvaluateFlank(bool visible)
    {
        // Use live player data only when visible.
        Vector3 centre = visible && player != null
            ? player.position : order.TargetPosition;

        Vector3 best = Vector3.zero;
        float bestScore = float.PositiveInfinity;
        NavMeshPath bestPath = null;

        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            Vector3 candidate = centre +
                new Vector3(Mathf.Cos(angle), 0f,
                            Mathf.Sin(angle)) * flankRadius;

            if (!TryPath(candidate, out Vector3 sampled,
                         out NavMeshPath path))
                continue;

            if (Time.time < avoidUntil &&
                Vector3.Distance(sampled, blockedDestination) < 1.5f)
                continue;

            if (!ClearSight(sampled, centre)) continue;

            float score = Score(path, sampled, centre, visible);

            if (score < bestScore)
            {
                bestScore = score;
                best = sampled;
                bestPath = path;
            }
        }

        if (bestPath == null)
        {
            ReportBlocked();
            return;
        }

        if (hasDestination &&
            TryPath(destination, out Vector3 current,
                    out NavMeshPath currentPath) &&
            ClearSight(current, centre) &&
            Vector3.Distance(current, centre) <= flankRadius * 1.5f)
        {
            float currentScore =
                Score(currentPath, current, centre, visible);

            if (Vector3.Distance(best, current) < 0.75f ||
                currentScore - bestScore < switchAdvantage)
                return;
        }

        Debug.Log(
        $"Flanker chose {best:F1}, cost {bestScore:F1}, " +
        $"player visible: {visible}, " +
        $"player angle: {(player != null ? player.eulerAngles.y : 0f):F0}",
        this);

        Follow(best, bestPath);
    }

    private float Score(NavMeshPath path, Vector3 point,
                        Vector3 centre, bool visible)
    {
        float cost = 0f;

        for (int i = 1; i < path.corners.Length; i++)
            cost += Vector3.Distance(
                path.corners[i - 1], path.corners[i]);

        if (visible && player != null)
        {
            Vector3 direction = point - centre;
            direction.y = 0f;

            Vector3 forward = player.forward;
            forward.y = 0f;

            float alignment = Vector3.Dot(
                forward.normalized, direction.normalized);

            cost += (alignment + 1f) * 0.5f * frontPenalty;
        }

        return cost;
    }

    private bool TryPath(Vector3 target, out Vector3 sampled,
                     out NavMeshPath path)
    {
        sampled = target;
        path = new NavMeshPath();

        if (!NavMesh.SamplePosition(transform.position,
                out NavMeshHit start, 2f, NavMesh.AllAreas))
            return false;

        if (!NavMesh.SamplePosition(target,
                out NavMeshHit end, 2f, NavMesh.AllAreas))
            return false;

        // Allow for the capsule's centre above the floor.
        if (Mathf.Abs(start.position.y - transform.position.y) > 1.5f ||
            Mathf.Abs(end.position.y - target.y) > 1.5f)
            return false;

        sampled = end.position;

        return NavMesh.CalculatePath(start.position, sampled,
                NavMesh.AllAreas, path) &&
            path.status == NavMeshPathStatus.PathComplete;
    }
    private bool CanSeePlayer()
    {
        return player != null &&
               Vector3.Distance(transform.position,
                                player.position) <= 22f &&
               ClearSight(transform.position, player.position);
    }

    private bool ClearSight(Vector3 from, Vector3 to)
    {
        Vector3 start = from + Vector3.up * sightHeight;
        Vector3 end = to + Vector3.up * sightHeight;
        Vector3 direction = end - start;

        if (direction.sqrMagnitude < 0.001f) return true;

        RaycastHit[] hits = Physics.RaycastAll(
            start, direction.normalized, direction.magnitude,
            obstacleMask, QueryTriggerInteraction.Ignore);

        foreach (RaycastHit hit in hits)
        {
            Transform obstacle = hit.collider.transform;

            if (obstacle == transform || obstacle.IsChildOf(transform))
                continue;

            if (player != null &&
                (obstacle == player || obstacle.IsChildOf(player)))
                continue;

            return false;
        }

        return true;
    }

    private void MoveTo(Vector3 target)
    {
        if (hasDestination && motor.IsMoving &&
            Vector3.Distance(destination, target) < 0.75f)
            return;

        if (TryPath(target, out Vector3 sampled, out NavMeshPath path))
            Follow(sampled, path);
        else
            ReportBlocked();
    }

    private void Follow(Vector3 target, NavMeshPath path)
    {
        destination = target;
        hasDestination = true;
        arrived = false;
        blockedReported = false;

        motor.SetStrafe(false);
        motor.SetPath(path.corners);
    }

    private void HandleArrival()
    {
        arrived = true;
        OnTaskArrived?.Invoke(this);

        if (CurrentTask == SquadTaskType.Investigate)
        {
            searching = true;
            searchUntil = Time.time + searchDuration;
        }
        else if (CurrentTask == SquadTaskType.Patrol &&
                 patrolPoints != null && patrolPoints.Length > 0)
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
            hasDestination = false;
            arrived = false;
        }
    }

    private void HandleBlocked()
    {
        blockedDestination = destination;
        avoidUntil = Time.time + 3f;
        hasDestination = false;
        ReportBlocked();
    }

    private void ReportBlocked()
    {
        motor.Stop();
        hasDestination = false;

        if (blockedReported) return;
        blockedReported = true;
        OnTaskBlocked?.Invoke(this);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f) return;

        health = Mathf.Max(0f, health - amount);

        if (IsAlive) return;

        motor.Stop();
        OnDied?.Invoke(this);
    }

    [ContextMenu("Test Flank")]
    private void TestFlank()
    {
        if (!Application.isPlaying || player == null) return;
        AssignOrder(SquadOrder.Flank(player.position));
    }
}