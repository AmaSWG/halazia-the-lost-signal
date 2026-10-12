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
	
	[SerializeField, Min(0f)]
	private float switchDistanceAdvantage = 2f;

    [SerializeField, Min(0f)]
    private float frontExposurePenalty = 8f;

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

        float leftScore =
            leftDistance + CalculateFacingPenalty(leftPosition);

        float rightScore =
            rightDistance + CalculateFacingPenalty(rightPosition);

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
            (!rightEligible || leftScore <= rightScore);

        // Keep a valid current choice unless switching improves the score enough.
        if (leftEligible && rightEligible)
        {
            if (selectedPosition == leftPosition)
            {
                float improvement = leftScore - rightScore;

                chooseLeft =
                    improvement <= 0f ||
                    improvement < switchDistanceAdvantage;
            }
            else if (selectedPosition == rightPosition)
            {
                float improvement = rightScore - leftScore;

                chooseLeft =
                    improvement > 0f &&
                    improvement >= switchDistanceAdvantage;
            }
        }

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
            $"Left: distance={leftDistance:F2}, score={leftScore:F2}, " +
            $"clearSight={leftVisible}\n" +
            $"Right: distance={rightDistance:F2}, score={rightScore:F2}, " +
            $"clearSight={rightVisible}",
            this);
    }

    private float CalculateFacingPenalty(Transform candidate)
    {
        Vector3 toCandidate =
            candidate.position - playerReference.position;

        toCandidate.y = 0f;

        Vector3 playerForward = playerReference.forward;
        playerForward.y = 0f;

        // A position directly beneath the player is not a useful flank.
        if (toCandidate.sqrMagnitude < 0.0001f ||
            playerForward.sqrMagnitude < 0.0001f)
        {
            return frontExposurePenalty;
        }

        float alignment = Vector3.Dot(
            playerForward.normalized,
            toCandidate.normalized);

        float frontFactor =
            (Mathf.Clamp(alignment, -1f, 1f) + 1f) * 0.5f;

        return frontFactor * frontExposurePenalty;
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