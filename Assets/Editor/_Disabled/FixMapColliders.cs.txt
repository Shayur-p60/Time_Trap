using UnityEngine;
using UnityEditor;

public class FixMapColliders
{
    [MenuItem("Tools/Time Trap/Fix Map Colliders (Corrected)")]
    static void FixColliders()
    {
        GameObject map = GameObject.Find("Map");
        if (map == null)
        {
            Debug.LogError("Could not find a 'Map' GameObject in the scene. Fix the object name in this script or the scene, then re-run.");
            return;
        }

        int lightsCleared = 0;
        int collidersAdded = 0;

        Transform[] allChildren = map.GetComponentsInChildren<Transform>(true);

        foreach (Transform t in allChildren)
        {
            GameObject go = t.gameObject;

            // 1. Anything with a Light component should have zero physical collision -
            // remove any collider sitting directly on it.
            Light light = go.GetComponent<Light>();
            if (light != null)
            {
                Collider existing = go.GetComponent<Collider>();
                if (existing != null)
                {
                    Object.DestroyImmediate(existing);
                    lightsCleared++;
                }
                continue; // don't add anything back onto a light
            }

            // 2. Anything with real geometry but no collider yet gets one,
            // regardless of naming - no fragile keyword matching.
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = go.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    mc = go.AddComponent<MeshCollider>();
                }
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false; // never convex - this building is hollow, convex would seal rooms shut
                mc.isTrigger = false;
                collidersAdded++;
            }
        }

        Debug.Log($"Fix Map Colliders: cleared colliders from {lightsCleared} light(s), " +
                  $"ensured non-convex Mesh Colliders on {collidersAdded} geometry object(s).");
    }
}
