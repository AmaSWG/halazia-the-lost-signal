using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class FlankerPositionSelector : MonoBehaviour
{
    [SerializeField] private Transform leftPosition;
    [SerializeField] private Transform rightPosition;
    [SerializeField] private Transform playerReference;
    [SerializeField, Min(0.1f)] private float sightHeight = 1.5f;
    [SerializeField] private LayerMask obstructionMask = ~0;

    private void Start()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (leftPosition == null ||
            rightPosition == null ||
            playerReference == null)
        {
            Debug.LogError(
                "Flanker: Assign both candidates and the player reference.",
                this);
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError(
                "Flanker: Agent is not on a matching NavMesh.",
                this);
            return;
        }

        NavMeshPath leftPath = new NavMeshPath();
        NavMeshPath rightPath = new NavMeshPath();

        float leftDistance = CalculateDistance(
            agent, leftPosition.position, leftPath);

        float rightDistance = CalculateDistance(
            agent, rightPosition.position, rightPath);

        bool leftVisible = HasClearSight(leftPosition);
        bool rightVisible = HasClearSight(rightPosition);

        bool leftEligible =
            !float.IsPositiveInfinity(leftDistance) && leftVisible;

        bool rightEligible =
            !float.IsPositiveInfinity(rightDistance) && rightVisible;

        Debug.Log(
            $"Flanker left: distance={leftDistance:F2}, " +
            $"clearSight={leftVisible}, eligible={leftEligible}\n" +
            $"Flanker right: distance={rightDistance:F2}, " +
            $"clearSight={rightVisible}, eligible={rightEligible}",
            this);

        if (!leftEligible && !rightEligible)
        {
            agent.ResetPath();

            Debug.LogWarning(
                "Flanker: No reachable candidate has clear sight.",
                this);
            return;
        }

        bool chooseLeft =
            leftEligible &&
            (!rightEligible || leftDistance <= rightDistance);

        Transform selectedPosition =
            chooseLeft ? leftPosition : rightPosition;

        NavMeshPath selectedPath =
            chooseLeft ? leftPath : rightPath;

        if (!agent.SetPath(selectedPath))
        {
            Debug.LogError(
                "Flanker: Could not assign the selected path.",
                this);
            return;
        }

        Debug.Log(
            $"Flanker selected: {selectedPosition.name}",
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
            10f);

        return !blocked;
    }

    private float CalculateDistance(
        NavMeshAgent agent,
        Vector3 destination,
        NavMeshPath path)
    {
        bool calculated = agent.CalculatePath(destination, path);

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