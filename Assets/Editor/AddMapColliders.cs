using UnityEngine;
using UnityEditor;

public class AddMapColliders
{
    [MenuItem("Tools/Generate Map Colliders")]
    public static void GenerateColliders()
    {
        // Find every object in the scene that has a visual 3D mesh
        MeshRenderer[] allRenderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        int addedCount = 0;

        foreach (MeshRenderer renderer in allRenderers)
        {
            GameObject obj = renderer.gameObject;
            
            // Check if the object is missing a collider, and add a MeshCollider if true
            if (obj.GetComponent<Collider>() == null)
            {
                obj.AddComponent<MeshCollider>();
                addedCount++;
            }
        }

        Debug.Log($"Successfully wrapped {addedCount} objects with Mesh Colliders!");
    }
}