using Bezi;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ZombieNavigationSetupAction
{
    private const float AgentRadius = 0.4f;
    private const float AgentHeight = 1.8f;
    private const float AgentClimb = 0.3f;
    private const float AgentSlope = 45f;
    private const string NavMeshSettingsPath = "ProjectSettings/NavMeshAreas.asset";

    /// <summary>
    /// Configures the built-in Humanoid agent and bakes the active scene's NavMesh Surface.
    /// </summary>
    [BeziAction("Configures the built-in Humanoid agent and bakes the active FacilityBuild NavMesh Surface.")]
    public static string ConfigureHumanoidAndBakeFacilityNavMesh()
    {
        Object navMeshSettingsAsset = AssetDatabase.LoadMainAssetAtPath(NavMeshSettingsPath);
        if (navMeshSettingsAsset == null)
        {
            throw new System.InvalidOperationException("Unable to load ProjectSettings/NavMeshAreas.asset.");
        }

        SerializedObject serializedSettings = new SerializedObject(navMeshSettingsAsset);
        SerializedProperty settings = serializedSettings.FindProperty("m_Settings");
        if (settings == null || settings.arraySize == 0)
        {
            throw new System.InvalidOperationException("The built-in Humanoid NavMesh settings were not found.");
        }

        SerializedProperty humanoid = settings.GetArrayElementAtIndex(0);
        humanoid.FindPropertyRelative("agentRadius").floatValue = AgentRadius;
        humanoid.FindPropertyRelative("agentHeight").floatValue = AgentHeight;
        humanoid.FindPropertyRelative("agentClimb").floatValue = AgentClimb;
        humanoid.FindPropertyRelative("agentSlope").floatValue = AgentSlope;
        serializedSettings.ApplyModifiedPropertiesWithoutUndo();

        NavMeshSurface surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            throw new System.InvalidOperationException("No NavMeshSurface was found in the active scene.");
        }

        int environmentLayer = LayerMask.NameToLayer("Environment");
        if (environmentLayer < 0)
        {
            throw new System.InvalidOperationException("The Environment layer does not exist.");
        }

        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = 1 << environmentLayer;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
        surface.defaultArea = 0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        surface.overrideTileSize = false;
        surface.overrideVoxelSize = false;
        surface.buildHeightMesh = false;
        surface.BuildNavMesh();
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);

        return "Baked Humanoid NavMesh: Radius 0.4, Height 1.8, Step Height 0.3, Max Slope 45.";
    }
}
