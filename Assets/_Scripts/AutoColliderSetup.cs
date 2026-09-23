using UnityEngine;

public class AutoColliderSetup : MonoBehaviour
{
    [ContextMenu("Setup Invisible Colliders")]
    public void ApplyColliders()
    {
        int processedCount = 0;

        // Loop through every single child piece attached to this object
        foreach (Transform piece in GetComponentsInChildren<Transform>(true))
        {
            // Skip the parent container itself
            if (piece == transform) continue;

            // 1. Turn off the MeshRenderer to make it invisible
            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            // 2. Add a BoxCollider if the piece has a mesh but no collider yet
            if (piece.GetComponent<MeshFilter>() != null && piece.GetComponent<BoxCollider>() == null)
            {
                piece.gameObject.AddComponent<BoxCollider>();
                processedCount++;
            }
        }

        Debug.Log($"Successfully hid meshes and added Box Colliders to {processedCount} pieces.");
    }
}