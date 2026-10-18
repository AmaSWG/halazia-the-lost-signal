using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class HunterMovementTest : MonoBehaviour
{
    public Transform target;

    void Start()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (!agent.isOnNavMesh)
        {
            Debug.LogError("Hunter is not on the NavMesh.");
            return;
        }

        if (target == null)
        {
            Debug.LogError("Assign the SoundSource as Target.");
            return;
        }

        if (NavMesh.SamplePosition(
            target.position, out NavMeshHit hit,
            2f, agent.areaMask))
        {
            NavMeshPath path = new NavMeshPath();

            if (agent.CalculatePath(hit.position, path) &&
                path.status == NavMeshPathStatus.PathComplete)
            {
                agent.SetPath(path);
                Debug.Log("Hunter is moving to the target.");
            }
            else
            {
                Debug.LogWarning("No complete path to the target.");
            }
        }
        else
        {
            Debug.LogWarning("Target is too far from the NavMesh.");
        }
    }
}