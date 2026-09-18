using System;
using System.Collections.Generic;
using Bezi;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Maintains collider and layer rules for the Facility BaseMap without modifying rendering data.
/// </summary>
public sealed class MapCollisionPostprocessor : AssetPostprocessor
{
    private const string FacilityBaseMapPathFragment = "_Models/Facility/BaseMap";
    private const string MapObjectName = "Map";
    private const string EnvironmentLayerName = "Environment";
    private const string StructuralBrushPrefix = "classname_brush";
    private const float MinimumColliderDimension = 0.01f;

    private static readonly string[] DecorativeKeywords =
    {
        "light",
        "lighting",
        "neon",
        "cctv",
        "speaker_light",
        "speaker light",
        "redalarm",
        "spot_light",
        "spot light"
    };

    private void OnPostprocessModel(GameObject importedRoot)
    {
        if (!assetPath.Contains(FacilityBaseMapPathFragment, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyRules(importedRoot, requireMapRoot: false);
    }

    /// <summary>
    /// Removes decorative lighting and CCTV mesh colliders, replaces structural brush mesh colliders with fitted boxes, and preserves Environment layers.
    /// </summary>
    [BeziAction("Stabilizes FacilityBuild map collision by removing decorative lighting and CCTV mesh colliders and fitting box colliders to structural classname_brush objects.")]
    public static string StabilizeFacilityBuildMapPhysics()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        CollisionFixReport report = ApplyRules(map, requireMapRoot: true);
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(map.scene);
        return JsonUtility.ToJson(report, true);
    }


    /// <summary>
    /// Verifies the active FacilityBuild map uses stable structural colliders and has no lighting or CCTV mesh colliders.
    /// </summary>
    [BeziAction("Verifies FacilityBuild structural collider and Environment-layer invariants without changing the scene.", IsReadOnly = true)]
    public static string VerifyFacilityBuildMapPhysics()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        CollisionVerificationReport report = new CollisionVerificationReport();
        Transform[] transforms = map.GetComponentsInChildren<Transform>(true);

        for (int index = 0; index < transforms.Length; index++)
        {
            Transform transform = transforms[index];
            GameObject gameObject = transform.gameObject;

            if (IsStructuralBrush(gameObject.name))
            {
                report.structuralBrushCount++;
                report.structuralBrushesWithMeshColliders += gameObject.GetComponents<MeshCollider>().Length;
                report.structuralBoxCollidersTotal += gameObject.GetComponents<BoxCollider>().Length;
                if (gameObject.GetComponents<BoxCollider>().Length == 0)
                {
                    report.structuralBrushesMissingBoxColliders++;
                }

                if (gameObject.layer != environmentLayer)
                {
                    report.structuralBrushesOutsideEnvironment++;
                }

                continue;
            }

            if (IsDecorativeLightOrCctv(transform))
            {
                report.decorativeMeshCollidersRemaining += gameObject.GetComponents<MeshCollider>().Length;
            }
        }

        report.isStable = report.structuralBrushCount > 0
            && report.structuralBrushesWithMeshColliders == 0
            && report.structuralBrushesMissingBoxColliders == 0
            && report.structuralBrushesOutsideEnvironment == 0
            && report.decorativeMeshCollidersRemaining == 0;
        return JsonUtility.ToJson(report, true);
    }

    private static CollisionFixReport ApplyRules(GameObject root, bool requireMapRoot)
    {
        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        if (environmentLayer < 0)
        {
            throw new InvalidOperationException("The Environment layer does not exist.");
        }

        CollisionFixReport report = new CollisionFixReport();
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);

        for (int index = 0; index < transforms.Length; index++)
        {
            Transform transform = transforms[index];
            GameObject gameObject = transform.gameObject;

            if (IsStructuralBrush(gameObject.name))
            {
                SetLayerRecursively(transform, environmentLayer, ref report.structuralLayerAssignments);
                ReplaceMeshColliderWithFittedBoxes(gameObject, ref report);
                report.structuralBrushCount++;
                continue;
            }

            if (IsDecorativeLightOrCctv(transform))
            {
                report.decorativeMeshCollidersRemoved += RemoveMeshColliders(gameObject);
            }
        }

        if (requireMapRoot && report.structuralBrushCount == 0)
        {
            throw new InvalidOperationException("No classname_brush structural objects were found under Map.");
        }

        return report;
    }

    private static bool IsStructuralBrush(string objectName)
    {
        return objectName.StartsWith(StructuralBrushPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDecorativeLightOrCctv(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (ContainsDecorativeKeyword(current.name) || current.GetComponent<Light>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDecorativeKeyword(string objectName)
    {
        for (int index = 0; index < DecorativeKeywords.Length; index++)
        {
            if (objectName.IndexOf(DecorativeKeywords[index], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int RemoveMeshColliders(GameObject gameObject)
    {
        MeshCollider[] meshColliders = gameObject.GetComponents<MeshCollider>();
        for (int index = 0; index < meshColliders.Length; index++)
        {
            Undo.DestroyObjectImmediate(meshColliders[index]);
        }

        return meshColliders.Length;
    }

    /// <summary>
    /// Replaces a classname_brush object's MeshCollider with one thin BoxCollider per disconnected
    /// mesh island (wall/floor/ceiling quad), instead of a single box over the whole hollow shell.
    /// A single AABB fitted to a whole room's combined mesh bounds would become a solid block that
    /// seals the room shut, so each disconnected surface must get its own tightly fitted box.
    /// </summary>
    private static void ReplaceMeshColliderWithFittedBoxes(GameObject gameObject, ref CollisionFixReport report)
    {
        report.structuralMeshCollidersRemoved += RemoveMeshColliders(gameObject);
        report.structuralBoxCollidersRemoved += RemoveBoxColliders(gameObject);

        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        Mesh sharedMesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (sharedMesh == null || !sharedMesh.isReadable)
        {
            return;
        }

        Vector3[] vertices = sharedMesh.vertices;
        int[] triangles = sharedMesh.triangles;
        int[] islandRootByVertex = BuildConnectedVertexIslands(vertices.Length, triangles);

        Dictionary<int, IslandBounds> islandBoundsByRoot = new Dictionary<int, IslandBounds>();
        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            int islandRoot = islandRootByVertex[triangles[triangleIndex]];
            if (!islandBoundsByRoot.TryGetValue(islandRoot, out IslandBounds islandBounds))
            {
                islandBounds = new IslandBounds();
                islandBoundsByRoot[islandRoot] = islandBounds;
            }

            islandBounds.Encapsulate(vertices[triangles[triangleIndex]]);
            islandBounds.Encapsulate(vertices[triangles[triangleIndex + 1]]);
            islandBounds.Encapsulate(vertices[triangles[triangleIndex + 2]]);
        }

        foreach (IslandBounds islandBounds in islandBoundsByRoot.Values)
        {
            BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(gameObject);
            boxCollider.center = islandBounds.Center;
            boxCollider.size = islandBounds.GetPaddedSize(MinimumColliderDimension);
            boxCollider.isTrigger = false;
            boxCollider.enabled = true;
            report.structuralBoxCollidersAdded++;
        }

        report.structuralMeshIslandsProcessed += islandBoundsByRoot.Count;
    }

    private static int RemoveBoxColliders(GameObject gameObject)
    {
        BoxCollider[] boxColliders = gameObject.GetComponents<BoxCollider>();
        for (int index = 0; index < boxColliders.Length; index++)
        {
            Undo.DestroyObjectImmediate(boxColliders[index]);
        }

        return boxColliders.Length;
    }

    /// <summary>
    /// Groups mesh vertices into connected islands via shared triangle edges using union-find.
    /// Brush-exported geometry typically keeps each flat quad (wall/floor/ceiling face) as its own
    /// unwelded vertex island, so this recovers the individual surfaces from one combined mesh.
    /// </summary>
    private static int[] BuildConnectedVertexIslands(int vertexCount, int[] triangles)
    {
        int[] parentByVertex = new int[vertexCount];
        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            parentByVertex[vertexIndex] = vertexIndex;
        }

        for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
        {
            Union(parentByVertex, triangles[triangleIndex], triangles[triangleIndex + 1]);
            Union(parentByVertex, triangles[triangleIndex + 1], triangles[triangleIndex + 2]);
            Union(parentByVertex, triangles[triangleIndex], triangles[triangleIndex + 2]);
        }

        int[] rootByVertex = new int[vertexCount];
        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            rootByVertex[vertexIndex] = Find(parentByVertex, vertexIndex);
        }

        return rootByVertex;
    }

    private static int Find(int[] parentByVertex, int vertexIndex)
    {
        while (parentByVertex[vertexIndex] != vertexIndex)
        {
            parentByVertex[vertexIndex] = parentByVertex[parentByVertex[vertexIndex]];
            vertexIndex = parentByVertex[vertexIndex];
        }

        return vertexIndex;
    }

    private static void Union(int[] parentByVertex, int firstVertexIndex, int secondVertexIndex)
    {
        int firstRoot = Find(parentByVertex, firstVertexIndex);
        int secondRoot = Find(parentByVertex, secondVertexIndex);
        if (firstRoot != secondRoot)
        {
            parentByVertex[firstRoot] = secondRoot;
        }
    }

    /// <summary>
    /// Accumulates a local-space axis-aligned bounds for one mesh island as its triangles are visited.
    /// </summary>
    private sealed class IslandBounds
    {
        private Vector3 minPoint = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        private Vector3 maxPoint = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        public Vector3 Center => (minPoint + maxPoint) * 0.5f;

        public void Encapsulate(Vector3 point)
        {
            minPoint = Vector3.Min(minPoint, point);
            maxPoint = Vector3.Max(maxPoint, point);
        }

        public Vector3 GetPaddedSize(float minimumDimension)
        {
            Vector3 rawSize = maxPoint - minPoint;
            return new Vector3(
                Mathf.Max(rawSize.x, minimumDimension),
                Mathf.Max(rawSize.y, minimumDimension),
                Mathf.Max(rawSize.z, minimumDimension));
        }
    }

    private static void SetLayerRecursively(Transform root, int layer, ref int assignmentCount)
    {
        Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < descendants.Length; index++)
        {
            GameObject gameObject = descendants[index].gameObject;
            if (gameObject.layer == layer)
            {
                continue;
            }

            Undo.RecordObject(gameObject, "Assign Environment Layer");
            gameObject.layer = layer;
            assignmentCount++;
        }
    }


    [Serializable]
    private sealed class CollisionVerificationReport
    {
        public int structuralBrushCount;
        public int structuralBrushesWithMeshColliders;
        public int structuralBoxCollidersTotal;
        public int structuralBrushesMissingBoxColliders;
        public int structuralBrushesOutsideEnvironment;
        public int decorativeMeshCollidersRemaining;
        public bool isStable;
    }


    [Serializable]
    private sealed class CollisionFixReport
    {
        public int structuralBrushCount;
        public int structuralMeshCollidersRemoved;
        public int structuralBoxCollidersRemoved;
        public int structuralBoxCollidersAdded;
        public int structuralMeshIslandsProcessed;
        public int structuralLayerAssignments;
        public int decorativeMeshCollidersRemoved;
    }
}
