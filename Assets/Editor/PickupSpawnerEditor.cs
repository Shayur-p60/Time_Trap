using UnityEditor;
using UnityEngine;

// Adds "Preview Layout" and "Clear Preview" buttons to the PickupSpawner inspector. The preview writes what each
// marker would get as a gizmo label on the marker (and a table in the Console) without entering Play mode.
[CustomEditor(typeof(PickupSpawner))]
public class PickupSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var spawner = (PickupSpawner)target;
        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview Layout"))
            {
                spawner.PreviewLayout();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Clear Preview"))
            {
                spawner.ClearPreview();
                SceneView.RepaintAll();
            }
        }
        if (!spawner.useFixedSeed)
            EditorGUILayout.HelpBox("Use Fixed Seed is off: each preview (and each run) uses a different seed.", MessageType.Info);
    }
}
