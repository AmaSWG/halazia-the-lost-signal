using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class FlankerPositionSelector : MonoBehaviour
{
    [SerializeField] private Transform leftPosition;
    [SerializeField] private Transform rightPosition;
    [SerializeField] private Transform playerReference;

    [SerializeField, Min(0.1f)]
    private float sightHeight = 1.5f;

    [SerializeField]
    private LayerMask obstructionMask = ~0;

    [SerializeField, Min(0.1f)]
    private float reevaluationInterval = 0.5f;

    private NavMeshAgent agent;
    private Transform selectedPosition;
    private float nextEvaluationTime;
    private bool reportedNoCandidate;

    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (leftPosition == null ||
            rightPosition == null ||
            playerReference == null)
        {
            Debug.LogError(
                "Flanker: Assign both candidates and the player reference.",
                this);

            enabled = false;
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError(
                "Flanker: Agent is not on a matching NavMesh.",
                this);

            enabled = false;
            return;
        }

        EvaluatePositions();

        nextEvaluationTime =
            Time.time + reevaluationInterval;
    }

    private void Update()
    {
        if (Time.time < nextEvaluationTime)
        {
            return;
        }

        nextEvaluationTime =
            Time.time + reevaluationInterval;

        if (!agent.isActiveAndEnabled || !agent.isOnNavMesh)
        {
            return;
        }

        EvaluatePositions();
    }

    private void EvaluatePositions()
    {
        NavMeshPath leftPath = new NavMeshPath();
        NavMeshPath rightPath = new NavMeshPath();

        float leftDistance = CalculateDistance(
            leftPosition.position, leftPath);

        float rightDistance = CalculateDistance(
            rightPosition.position, rightPath);

        bool leftVisible = HasClearSight(leftPosition);
        bool rightVisible = HasClearSight(rightPosition);

        bool leftEligible =
            !float.IsPositiveInfinity(leftDistance) && leftVisible;

        bool rightEligible =
            !float.IsPositiveInfinity(rightDistance) && rightVisible;

        if (!leftEligible && !rightEligible)
        {
            agent.ResetPath();
            selectedPosition = null;

            if (!reportedNoCandidate)
            {
                Debug.LogWarning(
                    "Flanker: No reachable candidate has clear sight. Holding.",
                    this);

                reportedNoCandidate = true;
            }

            return;
        }

        bool chooseLeft =
            leftEligible &&
            (!rightEligible || leftDistance <= rightDistance);

        Transform nextPosition =
            chooseLeft ? leftPosition : rightPosition;

        NavMeshPath nextPath =
            chooseLeft ? leftPath : rightPath;

        if (nextPosition == selectedPosition)
        {
            return;
        }

        if (!agent.SetPath(nextPath))
        {
            Debug.LogError(
                "Flanker: Could not assign the selected path.",
                this);
            return;
        }

        selectedPosition = nextPosition;
        reportedNoCandidate = false;

        Debug.Log(
            $"Flanker selected: {selectedPosition.name}\n" +
            $"Left: distance={leftDistance:F2}, clearSight={leftVisible}\n" +
            $"Right: distance={rightDistance:F2}, clearSight={rightVisible}",
            this);
    }

    private bool HasClearSight(Transform candidate)
    {
        Vector3 start =
            candidate.position + Vector3.up * sightHeight;

        Vector3 end =
            playerReference.position + Vector3.up * sightHeight;

        bool blocked = Physics.Linecast(
            start,
            end,
            obstructionMask,
            QueryTriggerInteraction.Ignore);

        Debug.DrawLine(
            start,
            end,
            blocked ? Color.red : Color.green,
            reevaluationInterval);

        return !blocked;
    }

    private float CalculateDistance(
        Vector3 destination,
        NavMeshPath path)
    {
        bool calculated =
            agent.CalculatePath(destination, path);

        if (!calculated ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            return float.PositiveInfinity;
        }

        Vector3[] corners = path.corners;
        float distance = 0f;

        for (int i = 1; i < corners.Length; i++)
        {
            distance += Vector3.Distance(
                corners[i - 1], corners[i]);
        }

        return distance;
    }
}