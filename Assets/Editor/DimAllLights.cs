using UnityEngine;
using UnityEditor;

public class DimAllLights
{
    // Lower = darker. Change this and re-run if the result isn't dark enough.
    private const float DimFactor = 0.15f;

    [MenuItem("Tools/Time Trap/Dim All Lights In Scene")]
    static void DimLights()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var light in lights)
        {
            // Don't touch the player's flashlight - that's the one light source
            // that's supposed to stay bright
            if (light.transform.root.name.Contains("Player") ||
                light.gameObject.name.Contains("Flashlight"))
                continue;

            light.intensity *= DimFactor;
            count++;
        }

        Debug.Log($"Dimmed {count} light(s) out of {lights.Length} found (flashlight excluded).");
    }
}
