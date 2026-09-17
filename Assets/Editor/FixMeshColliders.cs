using UnityEngine;
using UnityEditor;

public class FixMeshColliders
{
    [MenuItem("Tools/Time Trap/Set All Mesh Colliders Non-Convex")]
    static void SetNonConvex()
    {
        MeshCollider[] colliders = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var col in colliders)
        {
            if (col.convex)
            {
                col.convex = false;
                count++;
            }
        }

        Debug.Log($"Set {count} Mesh Collider(s) to non-convex out of {colliders.Length} found.");
    }
}
