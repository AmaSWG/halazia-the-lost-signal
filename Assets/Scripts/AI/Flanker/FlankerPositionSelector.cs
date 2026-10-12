using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class FlankerPositionSelector : MonoBehaviour
{
    [SerializeField] private Transform leftPosition;
    [SerializeField] private Transform rightPosition;

    private void Start()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (leftPosition == null || rightPosition == null)
        {
            Debug.LogError(
                "Flanker: Assign both candidate positions.",
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

        Debug.Log(
            $"Flanker path distances: " +
            $"left = {leftDistance:F2}, right = {rightDistance:F2}",
            this);

        if (float.IsPositiveInfinity(leftDistance) &&
            float.IsPositiveInfinity(rightDistance))
        {
            Debug.LogWarning(
                "Flanker: Neither candidate has a complete path.",
                this);
            return;
        }

        bool chooseLeft = leftDistance <= rightDistance;

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