using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class HunterHearing : MonoBehaviour
{
    public enum HunterState
    {
        Idle,
        Investigating,
        Searching,
        Returning
    }

    public HunterState currentState = HunterState.Idle;
    public float searchDuration = 4f;

    private NavMeshAgent agent;
    private Vector3 startingPosition;
    private float searchTimer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        startingPosition = transform.position;
    }

    void OnEnable()
    {
        SoundEmitter.SoundEmitted += HearSound;
    }

    void OnDisable()
    {
        SoundEmitter.SoundEmitted -= HearSound;
    }

    void Update()
    {
        if (!agent.isOnNavMesh)
            return;

        if (currentState == HunterState.Investigating && HasArrived())
        {
            currentState = HunterState.Searching;
            searchTimer = searchDuration;
            Debug.Log("Hunter: arrived—searching.");
        }
        else if (currentState == HunterState.Searching)
        {
            // Simple search placeholder: turn and look around.
            transform.Rotate(0f, 60f * Time.deltaTime, 0f);
            searchTimer -= Time.deltaTime;

            if (searchTimer <= 0f)
            {
                if (TryMoveTo(startingPosition))
                {
                    currentState = HunterState.Returning;
                    Debug.Log("Hunter: no new evidence—returning.");
                }
                else
                {
                    currentState = HunterState.Idle;
                    Debug.LogWarning("Hunter cannot return.");
                }
            }
        }
        else if (currentState == HunterState.Returning && HasArrived())
        {
            currentState = HunterState.Idle;
            agent.ResetPath();
            Debug.Log("Hunter: back at starting position.");
        }
    }

    void HearSound(Vector3 position, float radius)
    {
        if (Vector3.Distance(transform.position, position) > radius)
            return;

        if (TryMoveTo(position))
        {
            currentState = HunterState.Investigating;
            Debug.Log("Hunter: heard a sound—investigating!");
        }
    }

    bool TryMoveTo(Vector3 position)
    {
        if (!agent.isOnNavMesh)
            return false;

        if (!NavMesh.SamplePosition(
            position, out NavMeshHit hit, 2f, agent.areaMask))
            return false;

        NavMeshPath path = new NavMeshPath();

        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return false;

        return agent.SetPath(path);
    }

    bool HasArrived()
    {
        if (!agent.isOnNavMesh || agent.pathPending)
            return false;

        if (agent.pathStatus != NavMeshPathStatus.PathComplete)
            return false;

        float distance = agent.remainingDistance;

        if (float.IsInfinity(distance) || float.IsNaN(distance))
            return false;

        return distance <= agent.stoppingDistance + 0.2f;
    }
}