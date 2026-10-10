using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Time Trap/Navigation/Rebake NavMesh
//   Bakes Map/Navigation/NavSurface the way the zombies need it:
//     1. every door is made passable (Time Trap/Colliders/Doors Passable), so the NavMesh runs through the doorways;
//     2. Decoy doors are switched back to solid, because they never open;
//     3. the surface is baked and the data written over its asset next to the scene;
//     4. the doors are restored (Time Trap/Colliders/Doors Solid);
//     5. islands and areas are logged, measured from the player's position with no door carving.
//   At run time the NavMeshObstacles on the door panels (DoorNavObstacle) carve the closed doors back out.
//   Note: Unity leaves a collider out of the bake when its own GameObject carries a NavMeshObstacle. That is why the
//   Decoy doors have their obstacles on child objects: their colliders must be baked as walls.
public static class TimeTrapNavMeshRebake
{
    const string SurfacePath = "Map/Navigation/NavSurface";
    const string PassableMenu = "Time Trap/Colliders/Doors Passable (Test)";
    const string SolidMenu = "Time Trap/Colliders/Doors Solid";

    [MenuItem("Time Trap/Navigation/Rebake NavMesh")]
    static void MenuRebake()
    {
        Debug.Log(RebakeAndReport());
    }

    [MenuItem("Time Trap/Navigation/Rebake NavMesh", true)]
    static bool MenuRebakeValid()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    public static string RebakeAndReport()
    {
        var surfaceObject = GameObject.Find(SurfacePath);
        NavMeshSurface surface = surfaceObject != null ? surfaceObject.GetComponent<NavMeshSurface>() : Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null) return "[Time Trap] Rebake NavMesh: no NavMeshSurface found (expected " + SurfacePath + ").";

        string assetPath = surface.navMeshData != null ? AssetDatabase.GetAssetPath(surface.navMeshData) : "";
        if (string.IsNullOrEmpty(assetPath)) assetPath = DefaultAssetPath(surface);

        int decoysKeptSolid = 0;
        EditorApplication.ExecuteMenuItem(PassableMenu);
        try
        {
            foreach (var door in Object.FindObjectsByType<Door>(FindObjectsInactive.Include))
            {
                if (door.mode != Door.Mode.Decoy || door.leafColliders == null) continue;
                foreach (var c in door.leafColliders)
                    if (c != null) { c.enabled = true; decoysKeptSolid++; }
            }
            Physics.SyncTransforms();

            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath); // replaces the old data at the same path
            EditorUtility.SetDirty(surface);
        }
        finally
        {
            EditorApplication.ExecuteMenuItem(SolidMenu);
            Physics.SyncTransforms();
        }

        string stats = Measure(surface);

        foreach (var nav in Object.FindObjectsByType<DoorNavObstacle>(FindObjectsInactive.Include)) nav.Sync();
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
        AssetDatabase.SaveAssets();

        return "[Time Trap] NavMesh rebaked for agent type '" + NavMesh.GetSettingsNameFromID(surface.agentTypeID) + "'. " + stats
            + "\nDoors: all passable for the bake except " + decoysKeptSolid + " Decoy door collider(s) kept solid; now restored to solid."
            + "\nData: " + assetPath + ". Save the scene to keep it.";
    }

    static string DefaultAssetPath(NavMeshSurface surface)
    {
        string scenePath = surface.gameObject.scene.path;
        string folder = scenePath.Substring(0, scenePath.Length - ".unity".Length);
        if (!AssetDatabase.IsValidFolder(folder))
        {
            int slash = folder.LastIndexOf('/');
            AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
        }
        return folder + "/NavMesh-" + surface.name + ".asset";
    }

    // Islands and areas with every door open: the door obstacles are switched off and the doorway links (off while
    // their door is closed) switched on while it is measured.
    static string Measure(NavMeshSurface surface)
    {
        var carving = new List<NavMeshObstacle>();
        foreach (var o in Object.FindObjectsByType<NavMeshObstacle>(FindObjectsInactive.Include))
            if (o.enabled) { o.enabled = false; carving.Add(o); }
        var doorLinks = new List<NavMeshLink>();
        foreach (var nav in Object.FindObjectsByType<DoorNavObstacle>(FindObjectsInactive.Include))
            foreach (var link in nav.GetComponentsInChildren<NavMeshLink>(true))
                if (!link.enabled) { link.enabled = true; doorLinks.Add(link); }
        surface.RemoveData();
        surface.AddData();

        var sb = new StringBuilder();
        try
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            int count = tri.indices.Length / 3;
            var filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = NavMesh.AllAreas };

            // islands: triangles that share a corner belong together
            int[] parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            var corner = new Dictionary<Vector3Int, int>();
            for (int t = 0; t < count; t++)
                for (int e = 0; e < 3; e++)
                {
                    Vector3Int key = Vector3Int.RoundToInt(tri.vertices[tri.indices[t * 3 + e]] * 25f);
                    if (corner.TryGetValue(key, out int other)) Union(parent, t, other);
                    else corner[key] = t;
                }

            bool haveStart = false;
            NavMeshHit start = default;
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) haveStart = NavMesh.SamplePosition(player.transform.position, out start, 2f, filter);

            var islandArea = new Dictionary<int, float>();
            var islandReachable = new Dictionary<int, bool>();
            float total = 0f, reachable = 0f;
            var path = new NavMeshPath();
            for (int t = 0; t < count; t++)
            {
                Vector3 a = tri.vertices[tri.indices[t * 3]], b = tri.vertices[tri.indices[t * 3 + 1]], c = tri.vertices[tri.indices[t * 3 + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                total += area;
                bool reached = haveStart && NavMesh.CalculatePath(start.position, (a + b + c) / 3f, filter, path) && path.status == NavMeshPathStatus.PathComplete;
                if (reached) reachable += area;
                int root = Find(parent, t);
                islandArea.TryGetValue(root, out float soFar);
                islandArea[root] = soFar + area;
                islandReachable.TryGetValue(root, out bool was);
                islandReachable[root] = was || reached;
            }

            int unreachableIslands = 0;
            foreach (var kv in islandReachable) if (!kv.Value) unreachableIslands++;

            sb.Append(count + " triangles, " + total.ToString("F1") + " m2 in " + islandArea.Count + " island(s)" + (doorLinks.Count > 0 ? " (" + doorLinks.Count + " doorway link(s) join islands for walking)." : "."));
            if (haveStart)
                sb.Append(" From the player start: " + reachable.ToString("F1") + " m2 reachable, " + (total - reachable).ToString("F1") + " m2 unreachable in " + unreachableIslands + " island(s).");
            else
                sb.Append(" No object tagged Player on the NavMesh, so reachability was not measured.");

            if (unreachableIslands > 0)
            {
                sb.Append("\nUnreachable islands (largest first):");
                var roots = new List<int>(islandArea.Keys);
                roots.Sort((x, y) => islandArea[y].CompareTo(islandArea[x]));
                int shown = 0;
                foreach (int root in roots)
                {
                    if (islandReachable[root]) continue;
                    Vector3 sum = Vector3.zero; int n = 0;
                    for (int t = 0; t < count; t++)
                    {
                        if (Find(parent, t) != root) continue;
                        sum += tri.vertices[tri.indices[t * 3]]; n++;
                    }
                    sb.Append("\n  " + islandArea[root].ToString("F1") + " m2 near " + (sum / n).ToString("F1"));
                    if (++shown >= 12) break;
                }
            }
        }
        finally
        {
            foreach (var o in carving) o.enabled = true;
            foreach (var link in doorLinks) link.enabled = false;
            surface.RemoveData();
            surface.AddData();
        }
        return sb.ToString();
    }

    static int Find(int[] parent, int x)
    {
        while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
        return x;
    }

    static void Union(int[] parent, int a, int b)
    {
        int ra = Find(parent, a), rb = Find(parent, b);
        if (ra != rb) parent[ra] = rb;
    }
}
