using System;
using System.Collections.Generic;
using System.Linq;
using Bezi;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class FacilityNavMeshDiagnosticsAction
{
    private const string MapObjectName = "Map";
    private const string EnvironmentLayerName = "Environment";
    private const float AdjacencyTolerance = 0.1f;
    private const float SignificantBoundsDifference = 0.5f;

    /// <summary>
    /// Diagnoses Map renderer layer coverage, room-piece bounds adjacency, and baked NavMesh coverage.
    /// </summary>
    [BeziAction("Diagnoses FacilityBuild Map renderer layers, room-piece gaps, and baked NavMesh bounds.", IsReadOnly = true)]
    public static string DiagnoseFacilityNavMesh()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        if (environmentLayer < 0)
        {
            throw new InvalidOperationException("Environment layer does not exist.");
        }

        Renderer[] renderers = map.GetComponentsInChildren<Renderer>(true);
        Renderer[] environmentRenderers = renderers.Where(renderer => renderer.gameObject.layer == environmentLayer).ToArray();
        Renderer[] nonLightMissingRenderers = renderers.Where(renderer =>
            renderer.gameObject.layer != environmentLayer && !IsGenuineLightFixture(renderer.transform, map.transform)).ToArray();

        List<Bounds> roomPieceBounds = new List<Bounds>();
        List<string> roomPiecePaths = new List<string>();
        Transform facilityRoot = map.transform.Find("EZ");
        if (facilityRoot == null)
        {
            throw new InvalidOperationException("Map/EZ was not found in the active scene.");
        }

        foreach (Transform child in facilityRoot)
        {
            Renderer[] childRenderers = child.GetComponentsInChildren<Renderer>(true);
            if (childRenderers.Length == 0)
            {
                continue;
            }

            roomPieceBounds.Add(CombineBounds(childRenderers));
            roomPiecePaths.Add(GetHierarchyPath(child));
        }

        List<string> disconnectedPairs = FindDisconnectedPiecePairs(roomPiecePaths, roomPieceBounds);
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        bool hasNavMesh = triangulation.vertices != null && triangulation.vertices.Length > 0;
        Bounds mapBounds = CombineBounds(renderers);
        Bounds navMeshBounds = hasNavMesh ? CalculateBounds(triangulation.vertices) : new Bounds();
        bool navMeshSignificantlySmaller = !hasNavMesh || IsSignificantlySmaller(navMeshBounds, mapBounds);

        return JsonUtility.ToJson(new DiagnosticReport
        {
            totalMapRendererCount = renderers.Length,
            environmentRendererCount = environmentRenderers.Length,
            nonEnvironmentNonLightRenderers = nonLightMissingRenderers.Select(renderer => GetHierarchyPath(renderer.transform)).OrderBy(path => path).ToArray(),
            roomPieceCount = roomPiecePaths.Count,
            disconnectedPiecePairs = disconnectedPairs.ToArray(),
            navMeshVertexCount = triangulation.vertices?.Length ?? 0,
            navMeshTriangleCount = (triangulation.indices?.Length ?? 0) / 3,
            mapBounds = ToBoundsData(mapBounds),
            navMeshBounds = hasNavMesh ? ToBoundsData(navMeshBounds) : new BoundsData(),
            navMeshSignificantlySmallerThanMap = navMeshSignificantlySmaller
        }, true);
    }

    /// <summary>
    /// Samples NavMesh points in EZ_upstairs and EZ_Shelter and calculates the route between them.
    /// </summary>
    [BeziAction("Checks whether a complete NavMesh path connects EZ_upstairs and EZ_Shelter.", IsReadOnly = true)]
    public static string CheckUpstairsToShelterNavMeshPath()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        Transform upstairs = map.transform.Find("EZ/EZ_upstairs");
        Transform shelter = map.transform.Find("EZ/EZ_Shelter");
        if (upstairs == null || shelter == null)
        {
            throw new InvalidOperationException("EZ_upstairs or EZ_Shelter was not found under Map/EZ.");
        }

        bool foundUpstairsPoint = TrySampleSectionNavMeshPoint(upstairs, out Vector3 upstairsPoint);
        bool foundShelterPoint = TrySampleSectionNavMeshPoint(shelter, out Vector3 shelterPoint);
        if (!foundUpstairsPoint || !foundShelterPoint)
        {
            return JsonUtility.ToJson(new PathCheckReport
            {
                foundStartPoint = foundUpstairsPoint,
                foundEndPoint = foundShelterPoint,
                startPoint = upstairsPoint,
                endPoint = shelterPoint,
                pathStatus = "SamplingFailed"
            }, true);
        }

        NavMeshPath path = new NavMeshPath();
        bool calculationSucceeded = NavMesh.CalculatePath(upstairsPoint, shelterPoint, NavMesh.AllAreas, path);
        return JsonUtility.ToJson(new PathCheckReport
        {
            foundStartPoint = true,
            foundEndPoint = true,
            startPoint = upstairsPoint,
            endPoint = shelterPoint,
            calculatePathReturnedTrue = calculationSucceeded,
            pathStatus = path.status.ToString(),
            cornerCount = path.corners?.Length ?? 0
        }, true);
    }

    [BeziAction("Returns a compact per-room summary of Map renderers missing the Environment layer.", IsReadOnly = true)]
    public static string SummarizeMissingEnvironmentRenderers()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        Renderer[] missingRenderers = map.GetComponentsInChildren<Renderer>(true).Where(renderer =>
            renderer.gameObject.layer != environmentLayer && !IsGenuineLightFixture(renderer.transform, map.transform)).ToArray();

        string[] roomSummaries = missingRenderers
            .GroupBy(renderer => GetFacilityPiecePath(renderer.transform, map.transform))
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}: {group.Count()} missing renderers")
            .ToArray();

        return JsonUtility.ToJson(new MissingRendererSummary
        {
            missingRendererCount = missingRenderers.Length,
            roomSummaries = roomSummaries
        }, true);
    }

    [BeziAction("Assigns missing Map renderers to Environment and rebuilds the active FacilityBuild NavMesh Surface.")]
    public static string FixMapLayersAndBakeFacilityNavMesh()
    {
        GameObject map = GameObject.Find(MapObjectName);
        if (map == null)
        {
            throw new InvalidOperationException("Map was not found in the active scene.");
        }

        int environmentLayer = LayerMask.NameToLayer(EnvironmentLayerName);
        if (environmentLayer < 0)
        {
            throw new InvalidOperationException("Environment layer does not exist.");
        }

        int assignedRendererCount = 0;
        foreach (Renderer renderer in map.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.layer == environmentLayer || IsGenuineLightFixture(renderer.transform, map.transform))
            {
                continue;
            }

            Undo.RecordObject(renderer.gameObject, "Assign Environment Layer");
            renderer.gameObject.layer = environmentLayer;
            assignedRendererCount++;
        }

        NavMeshSurface surface = UnityEngine.Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null)
        {
            throw new InvalidOperationException("No NavMeshSurface was found in the active scene.");
        }

        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.All;
        surface.layerMask = 1 << environmentLayer;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();
        EditorUtility.SetDirty(map);
        EditorUtility.SetDirty(surface);
        EditorSceneManager.MarkSceneDirty(map.scene);

        return $"Assigned Environment to {assignedRendererCount} missing non-light render meshes and rebuilt NavMesh.";
    }

    private static bool TrySampleSectionNavMeshPoint(Transform section, out Vector3 navMeshPoint)
    {
        Renderer[] renderers = section.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            navMeshPoint = Vector3.zero;
            return false;
        }

        Bounds sectionBounds = CombineBounds(renderers);
        Vector3[] sampleCandidates =
        {
            sectionBounds.center,
            new Vector3(sectionBounds.center.x, sectionBounds.min.y + 0.1f, sectionBounds.center.z),
            new Vector3(sectionBounds.min.x + 0.5f, sectionBounds.center.y, sectionBounds.min.z + 0.5f),
            new Vector3(sectionBounds.max.x - 0.5f, sectionBounds.center.y, sectionBounds.max.z - 0.5f)
        };

        foreach (Vector3 sampleCandidate in sampleCandidates)
        {
            if (NavMesh.SamplePosition(sampleCandidate, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            {
                navMeshPoint = hit.position;
                return true;
            }
        }

        navMeshPoint = Vector3.zero;
        return false;
    }

    private static bool IsGenuineLightFixture(Transform candidate, Transform mapRoot)
    {
        for (Transform current = candidate; current != null && current != mapRoot; current = current.parent)
        {
            if (current.GetComponent<Light>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private static Bounds CombineBounds(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return new Bounds();
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            combinedBounds.Encapsulate(renderers[index].bounds);
        }

        return combinedBounds;
    }

    private static Bounds CalculateBounds(Vector3[] vertices)
    {
        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        for (int index = 1; index < vertices.Length; index++)
        {
            bounds.Encapsulate(vertices[index]);
        }

        return bounds;
    }

    private static List<string> FindDisconnectedPiecePairs(IReadOnlyList<string> paths, IReadOnlyList<Bounds> bounds)
    {
        List<string> disconnectedPairs = new List<string>();
        for (int firstIndex = 0; firstIndex < bounds.Count; firstIndex++)
        {
            int closestIndex = -1;
            float closestDistance = float.PositiveInfinity;
            for (int secondIndex = 0; secondIndex < bounds.Count; secondIndex++)
            {
                if (firstIndex == secondIndex)
                {
                    continue;
                }

                float distance = BoundsDistance(bounds[firstIndex], bounds[secondIndex]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = secondIndex;
                }
            }

            if (closestIndex > firstIndex && closestDistance > AdjacencyTolerance)
            {
                disconnectedPairs.Add($"{paths[firstIndex]} <-> {paths[closestIndex]} | gap {closestDistance:F3}m");
            }
        }

        return disconnectedPairs;
    }

    private static float BoundsDistance(Bounds first, Bounds second)
    {
        float xDistance = Mathf.Max(0f, Mathf.Max(first.min.x - second.max.x, second.min.x - first.max.x));
        float yDistance = Mathf.Max(0f, Mathf.Max(first.min.y - second.max.y, second.min.y - first.max.y));
        float zDistance = Mathf.Max(0f, Mathf.Max(first.min.z - second.max.z, second.min.z - first.max.z));
        return Mathf.Sqrt(xDistance * xDistance + yDistance * yDistance + zDistance * zDistance);
    }

    private static bool IsSignificantlySmaller(Bounds navMeshBounds, Bounds mapBounds)
    {
        return navMeshBounds.min.x > mapBounds.min.x + SignificantBoundsDifference ||
               navMeshBounds.max.x < mapBounds.max.x - SignificantBoundsDifference ||
               navMeshBounds.min.z > mapBounds.min.z + SignificantBoundsDifference ||
               navMeshBounds.max.z < mapBounds.max.z - SignificantBoundsDifference;
    }

    private static string GetFacilityPiecePath(Transform transform, Transform mapRoot)
    {
        Transform facilityRoot = mapRoot.Find("EZ");
        if (facilityRoot == null)
        {
            return GetHierarchyPath(transform);
        }

        Transform current = transform;
        while (current.parent != null && current.parent != facilityRoot)
        {
            current = current.parent;
        }

        return GetHierarchyPath(current);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        Stack<string> names = new Stack<string>();
        for (Transform current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return "/" + string.Join("/", names);
    }

    private static BoundsData ToBoundsData(Bounds bounds)
    {
        return new BoundsData
        {
            minX = bounds.min.x,
            minY = bounds.min.y,
            minZ = bounds.min.z,
            maxX = bounds.max.x,
            maxY = bounds.max.y,
            maxZ = bounds.max.z
        };
    }

    [Serializable]
    private class PathCheckReport
    {
        public bool foundStartPoint;
        public bool foundEndPoint;
        public Vector3 startPoint;
        public Vector3 endPoint;
        public bool calculatePathReturnedTrue;
        public string pathStatus;
        public int cornerCount;
    }

    [Serializable]
    private class MissingRendererSummary
    {
        public int missingRendererCount;
        public string[] roomSummaries;
    }

    [Serializable]
    private class DiagnosticReport
    {
        public int totalMapRendererCount;
        public int environmentRendererCount;
        public string[] nonEnvironmentNonLightRenderers;
        public int roomPieceCount;
        public string[] disconnectedPiecePairs;
        public int navMeshVertexCount;
        public int navMeshTriangleCount;
        public BoundsData mapBounds;
        public BoundsData navMeshBounds;
        public bool navMeshSignificantlySmallerThanMap;
    }

    [Serializable]
    private class BoundsData
    {
        public float minX;
        public float minY;
        public float minZ;
        public float maxX;
        public float maxY;
        public float maxZ;
    }
}
