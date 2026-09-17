using UnityEngine;
using UnityEditor;

public class DisableLightShadows
{
    [MenuItem("Tools/Disable All Shadows")]
    public static void TurnOffShadows()
    {
        // Find every Light component currently in the scene
        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        int changedCount = 0;

        foreach (Light light in allLights)
        {
            // If the light has shadows turned on, turn them off
            if (light.shadows != LightShadows.None)
            {
                // This allows you to press Cmd+Z to undo if it makes the scene too dark
                Undo.RecordObject(light, "Disable Shadows");
                light.shadows = LightShadows.None;
                changedCount++;
            }
        }

        Debug.Log($"Successfully disabled shadows on {changedCount} lights!");
    }
}