using UnityEngine;
using UnityEditor;

public class RemoveLightColliders
{
    // This creates a new button in Unity's top menu bar
    [MenuItem("Tools/Remove Light Colliders")]
    public static void CleanUpLights()
    {
        // Find every single Mesh Collider currently in the scene
        MeshCollider[] allColliders = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
        int removedCount = 0;

        foreach (MeshCollider mc in allColliders)
        {
            string objName = mc.gameObject.name.ToLower();
            
            // Check if the object name implies it is a lighting element
            if (objName.Contains("light") || objName.Contains("ambient") || objName.Contains("glow"))
            {
                // Safely destroy the component so you can Cmd+Z to undo if needed
                Undo.DestroyObjectImmediate(mc);
                removedCount++;
            }
        }

        Debug.Log($"Successfully removed {removedCount} Mesh Colliders from lighting objects!");
    }
}