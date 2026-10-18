using UnityEngine;
using UnityEngine.AI;

public class NavMeshCheck : MonoBehaviour
{
    void Start()
    {
        var mesh = NavMesh.CalculateTriangulation();

        Debug.Log("NavMesh triangle count: " +
                  mesh.indices.Length / 3);
    }
}