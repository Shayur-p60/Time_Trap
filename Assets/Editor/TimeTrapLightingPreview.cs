using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only helper: toggles the two facility light groups by tag so the
// scene can be previewed in either lighting stage.
//   Stage 1 (game start): Light_Emergency ON, Light_Powered OFF
//   Stage 2 (power restored): Light_Powered ON, Light_Emergency OFF
// The saved scene should be left in Stage 1.
public static class TimeTrapLightingPreview
{
    public const string EmergencyTag = "Light_Emergency";
    public const string PoweredTag = "Light_Powered";

    [MenuItem("Time Trap/Lighting/Preview Stage 1")]
    static void PreviewStage1() => Apply(emergencyOn: true, poweredOn: false, "Stage 1 (Emergency on, Powered off)");

    [MenuItem("Time Trap/Lighting/Preview Stage 2")]
    static void PreviewStage2() => Apply(emergencyOn: false, poweredOn: true, "Stage 2 (Powered on, Emergency off)");

    static void Apply(bool emergencyOn, bool poweredOn, string label)
    {
        int emergency = 0, powered = 0;
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var l in lights)
        {
            bool isEmergency = l.CompareTag(EmergencyTag);
            bool isPowered = l.CompareTag(PoweredTag);
            if (!isEmergency && !isPowered) continue;

            bool target = isEmergency ? emergencyOn : poweredOn;
            if (l.enabled != target)
            {
                Undo.RecordObject(l, "Time Trap lighting preview");
                l.enabled = target;
                EditorUtility.SetDirty(l);
            }
            if (isEmergency) emergency++; else powered++;
        }

        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));

        Debug.Log($"[Time Trap] {label}: {emergency} Light_Emergency and {powered} Light_Powered lights updated.");
    }
}
