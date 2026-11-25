using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class CargoNavigationTest : MonoBehaviour
{
    [SerializeField] private Transform target;

    private void Start()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (target == null)
        {
            Debug.LogError(
                "CargoNavigationTest: Assign the Target in the Inspector.",
                this);
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogError(
                "CargoNavigationTest: The agent is not on a matching NavMesh.",
                this);
            return;
        }

        if (!agent.SetDestination(target.position))
        {
            Debug.LogError(
                "CargoNavigationTest: Destination request failed.",
                this);
        }
    }
}