using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AreaSpawner : MonoBehaviour
{
    [SerializeField] private Vector2 spawnSize = Vector2.zero;
    [SerializeField] private List<int> alreadyMovedConnections = new List<int>();

    private float minXBound = 0f;
    private float maxXBound = 0f;
    private float minYBound = 0f;
    private float maxYBound = 0f;

    #region Draw Visualize Box
#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private Color gizmoColor = Color.white;
    [SerializeField] private float boxHeight = 0.5f;
    [SerializeField] private bool debug;

    private void OnDrawGizmosSelected()
    {
        //if(!debug) return;

        //Gizmos.color = gizmoColor;
        //var offsetPos = transform.position;
        //offsetPos.y += boxHeight / 2f;

        //var size = new Vector3(spawnSize.x, boxHeight, spawnSize.y);

        //Gizmos.DrawCube(offsetPos, size);
        if (!debug) return;

        Gizmos.color = gizmoColor;

        // Save the original Gizmos matrix
        Matrix4x4 originalMatrix = Gizmos.matrix;

        // Apply transformation matrix with rotation
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        // Draw the gizmo cube at the center with the given size
        var size = new Vector3(spawnSize.x, boxHeight, spawnSize.y);
        Gizmos.DrawCube(Vector3.zero, size);

        // Restore the original Gizmos matrix
        Gizmos.matrix = originalMatrix;
    }
#endif
    #endregion

    public Vector3 GetRandomSpawn()
    {
        minXBound = transform.position.x - spawnSize.x / 2;
        maxXBound = transform.position.x + spawnSize.x / 2;
        minYBound = transform.position.z - spawnSize.y / 2;
        maxYBound = transform.position.z + spawnSize.y / 2;

        var spawnPos = new Vector3(Random.Range(minXBound, maxXBound), transform.position.y, Random.Range(minYBound, maxYBound));

        return spawnPos;
    }
}
