using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Editor-only collider generator for the facility map.
//   Time Trap/Colliders/Generate        - rebuild colliders by rule, then verify with rays
//   Time Trap/Colliders/Clear Generated - remove only what this tool added
//   Time Trap/Colliders/Verify          - re-run the ray verification only
//
// Ownership:
//   Map/Corridors, Map/Elevator, Map/Armory, Map/Doors : every existing non-trigger collider is
//       removed and rebuilt from the rules below.
//   Map/Rooms (UC rooms) : existing colliders are kept and structural gaps are filled; a room collider that
//       reaches outside its room's visible geometry is disabled (never removed).
// Every collider sits on its renderer's own GameObject. Boxes use the mesh's local bounds, so they rotate and
// scale with the object; thin or sparse shapes and desks/tables/partitions get a MeshCollider instead.
// Everything added carries a GeneratedCollider marker so the tool is re-runnable.
public static class TimeTrapColliderGenerator
{
    static readonly string[] OwnedAreas = { "Corridors", "Elevator", "Armory" };
    const string DoorsArea = "Doors";
    const string GapFillArea = "Rooms";

    static readonly string[] SkipWords = { "light", "lamp", "neon", "alarm", "speaker", "cctv", "cam", "sign", "decal", "poster", "paper" };
    static readonly string[] StructuralWords = { "brush", "straight", "crossing", "threeway", "room", "shell", "wall", "floor" };
    static readonly string[] FurnitureWords = { "desk", "chair", "table", "rack", "crate", "bench", "stool", "stand" };
    // always solid and always shaped like their mesh, whatever their size or the skip keywords of their parents
    static readonly string[] ShapeWords = { "desk", "table", "partition", "divider", "cubicle", "workbench", "counter" };

    const float MinSize = 0.3f;          // below this the renderer gets no collider
    const float StructuralSize = 2f;     // above this the renderer is structural
    const float ThinSize = 0.15f;        // a prop thinner than this in any direction gets a MeshCollider, not a box
    const float SparseFill = 0.2f;       // ... and so does one whose vertices touch less than this share of its box
    const float RoomOutlierMargin = 0.5f; // a room collider may reach this far past the room's visible geometry
    const float MinDoorThickness = 0.1f; // door boxes are never thinner than this (world metres)
    const float StepTolerance = 0.005f;
    const string ReportPath = "Library/TimeTrap_ColliderReport.txt";

    enum Kind { SkipLight, SkipName, SkipDebris, SkipSmall, SkipNoMesh, Structural, Prop, DoorLeaf }

    class Info
    {
        public Kind kind;
        public Vector3 dims;       // oriented size in metres (mesh bounds x lossy scale)
        public string why = "";    // matched keyword / reason
        public bool ancestorOnly;  // skip keyword matched an ancestor, not the object itself
        public float Max { get { return Mathf.Max(dims.x, Mathf.Max(dims.y, dims.z)); } }
        public float Min { get { return Mathf.Min(dims.x, Mathf.Min(dims.y, dims.z)); } }
    }

    class Tread
    {
        public float ySum, area;
        public Vector3 sum;
        public float Y { get { return ySum / area; } }
        public Vector3 Centroid { get { return sum / area; } }
    }

    // ------------------------------------------------------------------ menus

    [MenuItem("Time Trap/Colliders/Generate")]
    static void MenuGenerate()
    {
        string report = GenerateAndReport();
        Debug.Log("[Time Trap] Collider generation finished. Full report: " + ReportPath + "\n" + Truncate(report, 3500));
    }

    [MenuItem("Time Trap/Colliders/Clear Generated")]
    static void MenuClear()
    {
        int colliders, markers;
        ClearGenerated(out colliders, out markers);
        MarkDirty();
        Debug.Log("[Time Trap] Cleared " + colliders + " generated collider(s) from " + markers + " marker(s).");
    }

    [MenuItem("Time Trap/Colliders/Verify")]
    static void MenuVerify()
    {
        string report = VerifyReport();
        File.WriteAllText(ReportPath.Replace("Report", "VerifyReport"), report);
        Debug.Log("[Time Trap] Collider verification:\n" + Truncate(report, 3500));
    }

    static string Truncate(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "\n... (truncated)"; }

    static void MarkDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++) EditorSceneManager.MarkSceneDirty(SceneManager.GetSceneAt(i));
    }

    static string F(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }
    static string V(Vector3 v) { return "(" + v.x.ToString("0.0", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.0", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.0", CultureInfo.InvariantCulture) + ")"; }

    static string Stem(string n)
    {
        int e = n.Length;
        while (e > 0 && (char.IsDigit(n[e - 1]) || n[e - 1] == '.' || n[e - 1] == '_' || n[e - 1] == '(' || n[e - 1] == ')' || n[e - 1] == ' ')) e--;
        return e > 0 ? n.Substring(0, e) : n;
    }

    static bool ContainsAny(string lower, string[] words, out string hit)
    {
        for (int i = 0; i < words.Length; i++)
            if (lower.Contains(words[i])) { hit = words[i]; return true; }
        hit = "";
        return false;
    }

    static Transform FindArea(Transform map, string name) { return map.Find(name); }

    // ------------------------------------------------------------------ classification

    static Info Classify(Renderer r, Transform areaRoot)
    {
        var info = new Info();
        var mf = r.GetComponent<MeshFilter>();
        Mesh mesh = (r is MeshRenderer && mf != null) ? mf.sharedMesh : null;
        if (mesh == null) { info.kind = Kind.SkipNoMesh; info.why = "no mesh"; return info; }

        Vector3 ls = r.transform.lossyScale;
        Vector3 bs = mesh.bounds.size;
        info.dims = new Vector3(bs.x * Mathf.Abs(ls.x), bs.y * Mathf.Abs(ls.y), bs.z * Mathf.Abs(ls.z));
        float max = info.Max;
        string lname = r.name.ToLowerInvariant();

        if (r.GetComponentInParent<Light>(true) != null) { info.kind = Kind.SkipLight; info.why = "on/under a Light"; return info; }
        if (max <= StructuralSize && r.GetComponentInChildren<Light>(true) != null) { info.kind = Kind.SkipLight; info.why = "fixture with a Light child"; return info; }
        if (lname.Contains("armory_debris")) { info.kind = Kind.SkipDebris; info.why = "Armory_Debris"; return info; }

        string hit;
        if (ContainsAny(lname, SkipWords, out hit)) { info.kind = Kind.SkipName; info.why = hit; return info; }
        if (ContainsAny(lname, ShapeWords, out hit) && !lname.Contains("desktop")) { info.kind = Kind.Structural; info.why = hit; return info; }
        for (Transform t = r.transform.parent; t != null && t != areaRoot; t = t.parent)
        {
            if (ContainsAny(t.name.ToLowerInvariant(), SkipWords, out hit))
            {
                info.kind = Kind.SkipName; info.why = hit + " (in '" + t.name + "')"; info.ancestorOnly = true; return info;
            }
        }

        if (max < MinSize) { info.kind = Kind.SkipSmall; info.why = "< " + F(MinSize) + " m"; return info; }

        // door leaves: a panel named "door" (not a frame), 1-3 m tall/wide and thin. Frames keep MeshColliders so the opening stays free;
        // big meshes that merely contain "door" in their name (the Blast_Door room shell) are not leaves.
        if (lname.Contains("door") && !lname.Contains("frame") && max >= 1f && max <= 3f && info.Min <= 0.45f) { info.kind = Kind.DoorLeaf; return info; }

        string sw;
        if (max > StructuralSize || ContainsAny(lname, StructuralWords, out sw)) { info.kind = Kind.Structural; return info; }
        info.kind = Kind.Prop;
        return info;
    }

    // ------------------------------------------------------------------ mesh helpers

    static void WorldMesh(Renderer r, out Vector3[] wv, out int[] idx)
    {
        Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] v = mesh.vertices;
        idx = mesh.triangles;
        Matrix4x4 m = r.transform.localToWorldMatrix;
        wv = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) wv[i] = m.MultiplyPoint3x4(v[i]);
    }

    // Finds flights of stairs: runs of at least 5 evenly spaced up-facing levels (risers 12-50 cm) whose
    // tread-sized levels also advance 15-80 cm horizontally from one to the next (furniture and light
    // fixtures have stacked levels but do not advance like that).
    static List<List<Tread>> FindStairFlights(Renderer r)
    {
        var flights = new List<List<Tread>>();
        Vector3[] wv; int[] idx;
        try { WorldMesh(r, out wv, out idx); } catch { return flights; }

        var levels = new List<Tread>();
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            Vector3 a = wv[idx[i]], b = wv[idx[i + 1]], c = wv[idx[i + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            float ar = n.magnitude * 0.5f;
            if (ar < 1e-5f) continue;
            n.Normalize();
            if (n.y < 0.97f) continue;
            Vector3 cen = (a + b + c) / 3f;
            Tread hit = null;
            for (int k = 0; k < levels.Count; k++) if (Mathf.Abs(levels[k].Y - cen.y) < 0.03f) { hit = levels[k]; break; }
            if (hit == null) { hit = new Tread(); levels.Add(hit); }
            hit.area += ar; hit.ySum += cen.y * ar; hit.sum += cen * ar;
        }
        levels.RemoveAll(L => L.area < 0.15f || L.area > 6f);
        levels.Sort((p, q) => p.Y.CompareTo(q.Y));
        // merge levels that sit within 8 cm of each other (tread nosing / landing seams)
        for (int i = levels.Count - 2; i >= 0; i--)
            if (levels[i + 1].Y - levels[i].Y < 0.08f && Vector3.Distance(levels[i].Centroid, levels[i + 1].Centroid) < 1.0f)
            {
                // keep the higher level: the walkable surface is the top one
                float top = Mathf.Max(levels[i].Y, levels[i + 1].Y);
                levels[i].area += levels[i + 1].area; levels[i].sum += levels[i + 1].sum;
                levels[i].ySum = top * levels[i].area;
                levels.RemoveAt(i + 1);
            }
        if (levels.Count < 5) return flights;

        int s = 0;
        while (s < levels.Count - 1)
        {
            float g0 = levels[s + 1].Y - levels[s].Y;
            if (g0 < 0.12f || g0 > 0.5f || !AdvancesLikeStairs(levels[s], levels[s + 1])) { s++; continue; }
            int e = s + 1;
            while (e + 1 < levels.Count)
            {
                float g = levels[e + 1].Y - levels[e].Y;
                if (g < 0.12f || g > 0.5f || Mathf.Abs(g - g0) > 0.07f || !AdvancesLikeStairs(levels[e], levels[e + 1])) break;
                e++;
            }
            if (e - s + 1 >= 5) flights.Add(levels.GetRange(s, e - s + 1));
            s = Mathf.Max(e, s + 1);
        }
        return flights;
    }

    // Landings (large levels) are allowed to jump; consecutive tread-sized levels must step 15-80 cm sideways.
    static bool AdvancesLikeStairs(Tread a, Tread b)
    {
        if (a.area > 1.5f || b.area > 1.5f) return true;
        Vector3 d = a.Centroid - b.Centroid;
        float h = new Vector2(d.x, d.z).magnitude;
        return h >= 0.15f && h <= 0.8f;
    }

    static void RiserStats(List<Tread> flight, out float median, out float max)
    {
        var gaps = new List<float>();
        for (int i = 1; i < flight.Count; i++) gaps.Add(flight[i].Y - flight[i - 1].Y);
        gaps.Sort();
        median = gaps[gaps.Count / 2];
        max = gaps[gaps.Count - 1];
    }

    // ------------------------------------------------------------------ clear

    static void ClearGenerated(out int removedColliders, out int removedMarkers)
    {
        removedColliders = 0; removedMarkers = 0;
        var markers = Object.FindObjectsByType<GeneratedCollider>(FindObjectsInactive.Include);
        foreach (var m in markers)
        {
            if (m == null) continue;
            GameObject go = m.gameObject;
            bool owns = m.ownsGameObject;
            if (m.colliders != null)
                foreach (var c in m.colliders) if (c != null) { Object.DestroyImmediate(c); removedColliders++; }
            removedMarkers++;
            if (owns) Object.DestroyImmediate(go); else Object.DestroyImmediate(m);
        }
    }

    // ------------------------------------------------------------------ add helpers

    static GeneratedCollider Mark(GameObject go, Collider c, string rule)
    {
        var m = go.GetComponent<GeneratedCollider>();
        if (m == null) m = go.AddComponent<GeneratedCollider>();
        var list = new List<Collider>();
        if (m.colliders != null) list.AddRange(m.colliders);
        list.Add(c);
        m.colliders = list.ToArray();
        m.rule = rule;
        return m;
    }

    static void AddMeshCollider(Renderer r, string rule)
    {
        var mc = r.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
        mc.convex = false;
        Mark(r.gameObject, mc, rule);
    }

    // A box only suits a prop that is box-like: thin panels and sparse shapes (a board on a stand, a monitor on a foot)
    // would get a box far bigger than what is visible.
    static bool PropNeedsMesh(Renderer r, Info info)
    {
        if (info.Min < ThinSize) return true;
        Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds;
        Vector3[] v = mesh.vertices;
        if (v.Length == 0) return false;
        const int G = 6;
        var cells = new HashSet<int>();
        for (int i = 0; i < v.Length; i++)
        {
            int ix = Mathf.Min(G - 1, (int)((v[i].x - b.min.x) / Mathf.Max(b.size.x, 1e-5f) * G));
            int iy = Mathf.Min(G - 1, (int)((v[i].y - b.min.y) / Mathf.Max(b.size.y, 1e-5f) * G));
            int iz = Mathf.Min(G - 1, (int)((v[i].z - b.min.z) / Mathf.Max(b.size.z, 1e-5f) * G));
            cells.Add((ix * G + iy) * G + iz);
        }
        return cells.Count < SparseFill * G * G * G;
    }

    // The box for a renderer, in its own local space: the mesh's local bounds, so it rotates and scales with the object.
    static Bounds LocalBox(Renderer r, string rule)
    {
        Bounds b = r.GetComponent<MeshFilter>().sharedMesh.bounds;
        if (rule == "Door")
        {
            // door leaves are never thinner than MinDoorThickness in the world
            Vector3 size = b.size;
            Vector3 ls = r.transform.lossyScale;
            for (int a = 0; a < 3; a++)
            {
                float minLocal = MinDoorThickness / Mathf.Max(Mathf.Abs(ls[a]), 1e-4f);
                if (size[a] < minLocal) size[a] = minLocal;
            }
            b.size = size;
        }
        return b;
    }

    // True when the box would pass Verify: at least 90% of points on the visible surface have the box face within
    // VerifyTolerance. Chairs, stools and screens on a foot fail this and get a MeshCollider instead.
    static bool BoxFits(Renderer r, Bounds box)
    {
        var m = new MeshData();
        try { WorldMesh(r, out m.wv, out m.idx); } catch { return true; }
        int triCount = m.idx.Length / 3;
        if (triCount == 0) return true;
        var areas = new float[triCount];
        float total = 0f;
        for (int t = 0; t < triCount; t++)
        {
            Vector3 a = m.wv[m.idx[t * 3]], b = m.wv[m.idx[t * 3 + 1]], c = m.wv[m.idx[t * 3 + 2]];
            areas[t] = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            total += areas[t];
        }
        if (total < 1e-5f) return true;
        int n = 80;
        if ((long)n * triCount > VerifyTriBudget) n = Mathf.Max(8, (int)(VerifyTriBudget / triCount));
        Transform tr = r.transform;
        float step = total / n, next = step * 0.5f, acc = 0f;
        int s = 0, tested = 0, ok = 0;
        for (int t = 0; t < triCount && s < n; t++)
        {
            acc += areas[t];
            while (acc >= next && s < n)
            {
                next += step; s++;
                if (areas[t] < 1e-7f) continue;
                Vector3 a = m.wv[m.idx[t * 3]], b = m.wv[m.idx[t * 3 + 1]], c = m.wv[m.idx[t * 3 + 2]];
                float u = Halton(s, 2), v = Halton(s, 3);
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                Vector3 p = Vector3.Lerp(a + (b - a) * u + (c - a) * v, (a + b + c) / 3f, 0.1f);
                Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
                Vector3 o = p + nrm * VerifyStandoff, d = -nrm;
                float dV;
                if (!RayMesh(o, d, m, VerifyStandoff + 0.01f, out dV)) dV = VerifyStandoff;
                tested++;
                // the same ray against the box, in the object's local space (one unit of the parameter is one world metre)
                Vector3 lo = tr.InverseTransformPoint(o), ld = tr.InverseTransformPoint(o + d) - lo;
                if (box.Contains(lo)) continue; // a ray starting inside the box never hits it
                float tmin = 0f, tmax = VerifyStandoff + 0.3f;
                bool hit = true;
                for (int ax = 0; ax < 3 && hit; ax++)
                {
                    if (Mathf.Abs(ld[ax]) < 1e-9f) { if (lo[ax] < box.min[ax] || lo[ax] > box.max[ax]) hit = false; continue; }
                    float t1 = (box.min[ax] - lo[ax]) / ld[ax], t2 = (box.max[ax] - lo[ax]) / ld[ax];
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
                    if (tmin > tmax) hit = false;
                }
                if (hit && Mathf.Abs(tmin - dV) <= VerifyTolerance) ok++;
            }
        }
        return tested == 0 || ok >= 0.9f * tested;
    }

    static void AddBoxCollider(Renderer r, string rule)
    {
        Bounds b = LocalBox(r, rule);
        var bc = r.gameObject.AddComponent<BoxCollider>();
        bc.center = b.center;
        bc.size = b.size;
        Mark(r.gameObject, bc, rule);
    }

    // A box where a box matches the visible shape, otherwise the mesh itself. Returns true when a MeshCollider was used.
    static bool AddFittedCollider(Renderer r, string rule)
    {
        if (BoxFits(r, LocalBox(r, rule))) { AddBoxCollider(r, rule); return false; }
        AddMeshCollider(r, rule);
        return true;
    }

    // Skinned door leaves have no MeshFilter: the box is the renderer's bounds brought into the leaf's own space.
    static void AddSkinnedDoorBox(SkinnedMeshRenderer r)
    {
        Bounds wb = r.bounds;
        Transform t = r.transform;
        var lb = new Bounds(t.InverseTransformPoint(wb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            lb.Encapsulate(t.InverseTransformPoint(wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        Vector3 size = lb.size, ls = t.lossyScale;
        for (int a = 0; a < 3; a++)
        {
            float minLocal = MinDoorThickness / Mathf.Max(Mathf.Abs(ls[a]), 1e-4f);
            if (size[a] < minLocal) size[a] = minLocal;
        }
        var bc = r.gameObject.AddComponent<BoxCollider>();
        bc.center = lb.center;
        bc.size = size;
        Mark(r.gameObject, bc, "Door");
    }

    // Every collider that closes a doorway: the generated ones with the "Door" rule (Map/Doors boxes and the EZ door
    // leaves) plus the leaf colliders of every Door component. The second group covers the UC room doors
    // (sm40_146_autodoorcommon...), which keep the colliders they came with and so carry no GeneratedCollider marker.
    // They are deliberately not given a marker: Generate and Clear Generated destroy marked colliders.
    static List<Collider> DoorColliders()
    {
        var found = new HashSet<Collider>();
        foreach (var m in Object.FindObjectsByType<GeneratedCollider>(FindObjectsInactive.Include))
        {
            if (m.rule != "Door" || m.colliders == null) continue;
            foreach (var c in m.colliders) if (c != null) found.Add(c);
        }
        foreach (var door in Object.FindObjectsByType<Door>(FindObjectsInactive.Include))
        {
            if (door.leafColliders == null) continue;
            foreach (var c in door.leafColliders) if (c != null) found.Add(c);
        }
        return new List<Collider>(found);
    }

    // Enables or disables every door collider. Editor-only test helper.
    static int SetDoorColliders(bool enabled)
    {
        int n = 0;
        foreach (var c in DoorColliders())
            if (c.enabled != enabled) { c.enabled = enabled; EditorUtility.SetDirty(c); n++; }
        MarkDirty();
        return n;
    }

    static int CountDoorColliders(out int enabledCount)
    {
        int total = 0; enabledCount = 0;
        foreach (var c in DoorColliders()) { total++; if (c.enabled) enabledCount++; }
        return total;
    }

    [MenuItem("Time Trap/Colliders/Doors Passable (Test)")]
    static void MenuDoorsPassable()
    {
        int n = SetDoorColliders(false);
        int en; int total = CountDoorColliders(out en);
        Debug.Log("[Time Trap] Doors passable (test): disabled " + n + " door collider(s); " + en + " of " + total + " enabled. Run 'Doors Solid' before saving.");
    }

    [MenuItem("Time Trap/Colliders/Doors Solid")]
    static void MenuDoorsSolid()
    {
        int n = SetDoorColliders(true);
        int en; int total = CountDoorColliders(out en);
        Debug.Log("[Time Trap] Doors solid: enabled " + n + " door collider(s); " + en + " of " + total + " enabled.");
    }

    // Rooms keep the colliders they came with, but one that reaches outside the room's visible geometry (a hidden
    // or mis-scaled piece) blocks other parts of the map, so it is disabled.
    static List<string> DisableRoomOutliers(Transform roomsRoot)
    {
        var disabled = new List<string>();
        Physics.SyncTransforms();
        foreach (Transform room in roomsRoot)
        {
            bool any = false;
            var foot = new Bounds();
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { foot = r.bounds; any = true; } else foot.Encapsulate(r.bounds);
            }
            if (!any) continue;
            foreach (var c in room.GetComponentsInChildren<Collider>(false))
            {
                if (!c.enabled || c.isTrigger) continue;
                Bounds cb = c.bounds;
                if (cb.size == Vector3.zero) continue;
                float over = 0f;
                for (int a = 0; a < 3; a++) over = Mathf.Max(over, Mathf.Max(foot.min[a] - cb.min[a], cb.max[a] - foot.max[a]));
                if (over <= RoomOutlierMargin) continue;
                c.enabled = false;
                EditorUtility.SetDirty(c);
                disabled.Add(PathOf(c.transform, roomsRoot) + " (" + c.GetType().Name + ", " + V(cb.size) + " m, " + F(over) + " m outside the room)");
            }
        }
        return disabled;
    }

    // ------------------------------------------------------------------ generate

    public static string GenerateAndReport()
    {
        var sb = new StringBuilder();
        var mapGo = GameObject.Find("Map");
        if (mapGo == null) { return "No 'Map' object in the open scene."; }
        Transform map = mapGo.transform;

        // doors left passable for testing stay passable after the rebuild
        int doorsOnBefore; int doorsBefore = CountDoorColliders(out doorsOnBefore);
        bool keepDoorsPassable = doorsBefore > 0 && doorsOnBefore == 0;

        // 1. remove what this tool added before
        int clearedColliders, clearedMarkers;
        ClearGenerated(out clearedColliders, out clearedMarkers);

        // 2. remove existing colliders in the owned areas (scene components; importer-made ones cannot be removed here)
        var owned = new List<Transform>();
        foreach (var a in OwnedAreas) { var t = FindArea(map, a); if (t != null) owned.Add(t); }
        var doorsRoot = FindArea(map, DoorsArea);
        var removalRoots = new List<Transform>(owned);
        if (doorsRoot != null) removalRoots.Add(doorsRoot);
        int removedExisting = 0, keptTriggers = 0, importerColliders = 0;
        foreach (var root in removalRoots)
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (c.isTrigger) { keptTriggers++; continue; }
                if (PrefabUtility.IsPartOfPrefabInstance(c) && PrefabUtility.GetCorrespondingObjectFromSource(c) != null) { importerColliders++; continue; }
                Object.DestroyImmediate(c);
                removedExisting++;
            }

        // 3. classify and add
        var counts = new Dictionary<string, int>();
        var skipWordCounts = new Dictionary<string, int>();
        var unsureThin = new Dictionary<string, string>();
        var unsureFurniture = new Dictionary<string, string>();
        var doorLeaves = new List<string>();
        var doorFrames = new List<string>();
        var doorOther = new List<string>();
        var unsureTabletop = new Dictionary<string, int>();
        var unsureSkipLarge = new Dictionary<string, string>();
        var unsureAncestor = new Dictionary<string, int>();
        var stairs = new List<string>();
        int inactiveAdded = 0, stairsTooTall = 0, propMesh = 0, doorMesh = 0;

        CharacterController cc = Object.FindFirstObjectByType<CharacterController>();
        float sceneStep = cc != null ? cc.stepOffset : 0.3f;
        // PlayerMovementController overwrites the controller's step offset in Awake, so that value is what applies in play
        float effectiveStep = RuntimeStepOffset();
        if (effectiveStep <= 0f) effectiveStep = sceneStep;

        foreach (var area in owned)
        {
            foreach (var r in area.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Info info = Classify(r, area);
                string key = area.name + "/" + info.kind;
                counts[key] = counts.ContainsKey(key) ? counts[key] + 1 : 1;
                string nm = r.name;

                if (info.kind == Kind.SkipName)
                {
                    string w = info.why;
                    int p = w.IndexOf(' '); if (p > 0) w = w.Substring(0, p);
                    skipWordCounts[w] = skipWordCounts.ContainsKey(w) ? skipWordCounts[w] + 1 : 1;
                    if (info.Max > 1f && !unsureSkipLarge.ContainsKey(Stem(nm))) unsureSkipLarge[Stem(nm)] = F(info.Max) + " m, matched '" + info.why + "'";
                    if (info.ancestorOnly) { string s = Stem(nm); unsureAncestor[s] = unsureAncestor.ContainsKey(s) ? unsureAncestor[s] + 1 : 1; }
                }
                if (info.kind == Kind.Structural)
                {
                    AddMeshCollider(r, "Structural");
                    if (!r.gameObject.activeInHierarchy) inactiveAdded++;
                    string lname = nm.ToLowerInvariant();
                    string fw;
                    if (info.Max > StructuralSize && ContainsAny(lname, FurnitureWords, out fw) && !unsureFurniture.ContainsKey(Stem(nm)))
                        unsureFurniture[Stem(nm)] = F(info.dims.x) + " x " + F(info.dims.y) + " x " + F(info.dims.z) + " m";
                    if (info.Max > StructuralSize)
                    {
                        float[] d = { info.dims.x, info.dims.y, info.dims.z };
                        System.Array.Sort(d);
                        if (d[1] < 0.15f && !unsureThin.ContainsKey(Stem(nm))) unsureThin[Stem(nm)] = F(info.dims.x) + " x " + F(info.dims.y) + " x " + F(info.dims.z) + " m";
                    }
                    if (lname.Contains("frame") && area.name == "Corridors") doorFrames.Add(DoorLine(r, info));
                    else if (lname.Contains("door") && area.name == "Corridors") doorOther.Add(DoorLine(r, info) + "  (named door, but a " + F(info.Max) + " m structure, not a leaf)");

                    foreach (var flight in FindStairFlights(r))
                    {
                        float rm, rx;
                        RiserStats(flight, out rm, out rx);
                        bool tooTall = rx > effectiveStep + StepTolerance;
                        bool aboveScene = rx > sceneStep + StepTolerance;
                        if (tooTall) stairsTooTall++;
                        stairs.Add(nm + (r.gameObject.activeInHierarchy ? "" : " [inactive]") + ": flight of " + flight.Count + " treads, riser median " + F(rm) + " m, max " + F(rx) + " m, y " + F(flight[0].Y) + " to " + F(flight[flight.Count - 1].Y)
                            + (tooTall ? "  ** TALLER THAN THE STEP OFFSET (" + F(effectiveStep) + ") - ramp needed **" : aboveScene ? "  (above the scene's " + F(sceneStep) + ", within the runtime " + F(effectiveStep) + ")" : ""));
                    }
                }
                else if (info.kind == Kind.DoorLeaf)
                {
                    if (AddFittedCollider(r, "Door")) doorMesh++;
                    if (!r.gameObject.activeInHierarchy) inactiveAdded++;
                    doorLeaves.Add(DoorLine(r, info));
                }
                else if (info.kind == Kind.Prop)
                {
                    if (PropNeedsMesh(r, info)) { AddMeshCollider(r, "Prop"); propMesh++; } else if (AddFittedCollider(r, "Prop")) propMesh++;
                    if (!r.gameObject.activeInHierarchy) inactiveAdded++;
                    if (info.Max < 0.6f) { string s = Stem(nm); unsureTabletop[s] = unsureTabletop.ContainsKey(s) ? unsureTabletop[s] + 1 : 1; }
                }
            }
        }

        // doors: one box per door piece, on the piece itself
        int doorBoxes = 0, doorsWithoutRenderers = 0;
        if (doorsRoot != null)
            foreach (Transform door in doorsRoot)
            {
                int before = doorBoxes;
                foreach (var r in door.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Info info = Classify(r, doorsRoot);
                    if (info.kind == Kind.SkipNoMesh || info.Max < MinSize) continue;
                    if (AddFittedCollider(r, "Door")) doorMesh++;
                    doorBoxes++;
                }
                foreach (var r in door.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (r.bounds.size.magnitude < MinSize) continue;
                    AddSkinnedDoorBox(r);
                    doorBoxes++;
                }
                if (doorBoxes == before) doorsWithoutRenderers++;
            }

        // UC rooms: existing colliders untouched, fill structural gaps only
        int gapFilled = 0, roomPropGaps = 0, roomsStructuralHaveCollider = 0, roomsSkipped = 0;
        var gapNames = new List<string>();
        var roomsRoot = FindArea(map, GapFillArea);
        if (roomsRoot != null)
            foreach (var r in roomsRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Info info = Classify(r, roomsRoot);
                bool has = r.GetComponent<Collider>() != null;
                if (info.kind == Kind.Structural)
                {
                    if (has) roomsStructuralHaveCollider++;
                    else { AddMeshCollider(r, "GapFill"); gapFilled++; if (gapNames.Count < 12) gapNames.Add(r.name + (r.enabled ? "" : " (renderer disabled)")); }
                }
                else if (info.kind == Kind.Prop) { if (!has) roomPropGaps++; }
                else roomsSkipped++;
            }

        var roomOutliers = roomsRoot != null ? DisableRoomOutliers(roomsRoot) : new List<string>();
        if (keepDoorsPassable) SetDoorColliders(false);

        Physics.SyncTransforms();
        MarkDirty();

        // ---- report
        sb.AppendLine("=== COLLIDER GENERATION REPORT ===");
        sb.AppendLine("Cleared from previous run: " + clearedColliders + " generated collider(s) (" + clearedMarkers + " marker(s)).");
        sb.AppendLine("Existing colliders removed in Corridors/Elevator/Armory/Doors: " + removedExisting + " scene component(s); importer-generated still present: " + importerColliders + "; trigger colliders kept: " + keptTriggers + ".");
        sb.AppendLine();
        sb.AppendLine("-- Counts per rule (renderers) --");
        int tStruct = 0, tProp = 0;
        foreach (var area in owned)
        {
            string a = area.name;
            Func(counts, a + "/" + Kind.Structural, out int st); Func(counts, a + "/" + Kind.Prop, out int pr);
            Func(counts, a + "/" + Kind.SkipLight, out int sl); Func(counts, a + "/" + Kind.SkipName, out int sn);
            Func(counts, a + "/" + Kind.SkipDebris, out int sd); Func(counts, a + "/" + Kind.SkipSmall, out int ss); Func(counts, a + "/" + Kind.SkipNoMesh, out int sm);
            Func(counts, a + "/" + Kind.DoorLeaf, out int dl);
            tStruct += st; tProp += pr;
            sb.AppendLine(a + ": structural MeshCollider " + st + " | prop " + pr + " | door-leaf BoxCollider " + dl + " | skipped: light " + sl + ", name " + sn + ", Armory_Debris " + sd + ", <0.3 m " + ss + ", no mesh " + sm);
        }
        sb.AppendLine("Map/Armory has no renderers of its own; the Armory geometry is Corridors/U_Corridor_blockout/Armory.");
        sb.AppendLine("TOTAL structural MeshCollider (non-convex): " + tStruct + "   props: " + tProp + " (" + (tProp - propMesh) + " BoxCollider, " + propMesh + " MeshCollider where a box did not fit)   door colliders: " + (doorBoxes + doorLeaves.Count) + " (" + doorBoxes + " in Map/Doors + " + doorLeaves.Count + " EZ leaves; " + doorMesh + " of them MeshCollider because a box did not fit)   ramps: 0   UC-room gap fills: " + gapFilled);
        sb.AppendLine("Colliders added on currently inactive objects (EZ_Intercom, Endoof.001): " + inactiveAdded);
        sb.Append("Skipped by name keyword: ");
        foreach (var kv in skipWordCounts) sb.Append(kv.Key + " " + kv.Value + ", ");
        sb.AppendLine();
        if (doorsWithoutRenderers > 0) sb.AppendLine("Doors without renderers (no box): " + doorsWithoutRenderers);
        sb.AppendLine("UC rooms (Map/Rooms): structural renderers already collidable " + roomsStructuralHaveCollider + ", gaps filled " + gapFilled + (gapNames.Count > 0 ? " (" + string.Join(", ", gapNames.ToArray()) + ")" : "") + "; non-structural renderers without collider left alone: " + roomPropGaps + "; skipped by rule " + roomsSkipped + ".");
        sb.AppendLine("Room colliders disabled for reaching outside their room: " + roomOutliers.Count);
        foreach (var l in roomOutliers) sb.AppendLine("    " + l);
        sb.AppendLine();
        sb.AppendLine("-- Stairs --");
        float runtimeStep = RuntimeStepOffset();
        sb.AppendLine("Player CharacterController step offset: " + F(sceneStep) + " in the scene; PlayerMovementController overwrites it with " + F(runtimeStep) + " in Awake, so " + F(effectiveStep) + " is what applies in play and is the value risers are compared with.");
        if (stairs.Count == 0) sb.AppendLine("No stair-like meshes found.");
        foreach (var s in stairs) sb.AppendLine(s);
        sb.AppendLine(stairsTooTall == 0 ? "No flight has a riser taller than the effective step offset, so stairs keep their normal MeshCollider and no ramps were added." : stairsTooTall + " flight(s) are taller than the step offset: ramp colliders still need to be added.");
        sb.AppendLine();
        sb.AppendLine("-- Player CharacterController --");
        if (cc != null)
            sb.AppendLine("on '" + cc.name + "': radius " + F(cc.radius) + ", height " + F(cc.height) + ", center " + V(cc.center) + ", step offset " + F(cc.stepOffset) + ", slope limit " + F(cc.slopeLimit) + ", skin width " + F(cc.skinWidth));
        else sb.AppendLine("No CharacterController found.");
        sb.AppendLine();
        sb.AppendLine("-- EZ door pieces --");
        int enabledDoors; int totalDoors = CountDoorColliders(out enabledDoors);
        sb.AppendLine("Door colliders (Map/Doors boxes + EZ leaves): " + totalDoors + ", enabled " + enabledDoors + ". Toggle with Time Trap/Colliders/Doors Passable (Test) and Doors Solid.");
        sb.AppendLine("Leaves, fitted BoxCollider marked Door (" + doorLeaves.Count + "):");
        foreach (var l in doorLeaves) sb.AppendLine("    " + l);
        sb.AppendLine("Frames, kept as MeshCollider so the opening stays free (" + doorFrames.Count + "):");
        foreach (var l in doorFrames) sb.AppendLine("    " + l);
        sb.AppendLine("Named door but not a leaf, kept as MeshCollider (" + doorOther.Count + "):");
        foreach (var l in doorOther) sb.AppendLine("    " + l);
        sb.AppendLine();
        sb.AppendLine("-- Unsure how to classify (review these) --");
        DumpStr(sb, "Furniture-named but over 2 m, so given a MeshCollider (non-convex, may be hollow/L-shaped)", unsureFurniture);
        DumpStr(sb, "Long and thin (two smallest sides < 15 cm, longest > 2 m), given a MeshCollider", unsureThin);
        DumpStr(sb, "Skipped by name but larger than 1 m", unsureSkipLarge);
        DumpInt(sb, "Tabletop-size props (0.3 to 0.6 m) given a BoxCollider", unsureTabletop);
        DumpInt(sb, "Skipped only because an ancestor's name matched a skip keyword", unsureAncestor);

        sb.AppendLine();
        sb.AppendLine(VerifyReport());

        string text = sb.ToString();
        File.WriteAllText(ReportPath, text);
        return text;
    }

    static void Func(Dictionary<string, int> d, string k, out int v) { v = d.ContainsKey(k) ? d[k] : 0; }

    // "Module/.../Name | active | world centre | size" for the door report
    static string DoorLine(Renderer r, Info info)
    {
        string path = r.name;
        Transform t = r.transform.parent;
        int depth = 0;
        while (t != null && depth < 3) { path = t.name + "/" + path; t = t.parent; depth++; }
        return path + " | " + (r.gameObject.activeInHierarchy ? "active" : "INACTIVE") + " | at " + V(r.bounds.center) + " | " + F(info.dims.x) + " x " + F(info.dims.y) + " x " + F(info.dims.z) + " m";
    }

    static void DumpStr(StringBuilder sb, string title, Dictionary<string, string> d)
    {
        sb.AppendLine("* " + title + ": " + d.Count);
        int n = 0;
        foreach (var kv in d) { if (n++ >= 25) { sb.AppendLine("    ..."); break; } sb.AppendLine("    " + kv.Key + " - " + kv.Value); }
    }

    static void DumpInt(StringBuilder sb, string title, Dictionary<string, int> d)
    {
        int total = 0; foreach (var kv in d) total += kv.Value;
        sb.AppendLine("* " + title + ": " + total + " object(s) in " + d.Count + " name group(s)");
        int n = 0;
        foreach (var kv in d) { if (n++ >= 25) { sb.AppendLine("    ..."); break; } sb.AppendLine("    " + kv.Key + " x" + kv.Value); }
    }

    static float RuntimeStepOffset()
    {
        var pm = Object.FindFirstObjectByType<PlayerMovementController>();
        if (pm == null) return -1f;
        var so = new SerializedObject(pm);
        var p = so.FindProperty("stepOffset");
        return p != null ? p.floatValue : -1f;
    }

    // ------------------------------------------------------------------ verification

    const float VerifyTolerance = 0.05f; // a physics hit must be this close to the visible surface
    const float VerifyStandoff = 0.25f;  // rays start this far off the surface, along its normal
    const long VerifyTriBudget = 4000000L; // samples x triangles allowed per renderer

    class Zone
    {
        public string name, category;
        public float density = 2f; // samples per m2 of visible surface
        public int maxSamples = 200;
        public List<Renderer> sources = new List<Renderer>();
    }

    class MeshData { public Vector3[] wv; public int[] idx; }

    class Fail { public string zone, renderer, collider, reason; public Vector3 pos; public float dev; }

    class ZoneResult { public Zone zone; public int total, pass, occluded, passableDoor; }

    static readonly Dictionary<Renderer, MeshData> meshCache = new Dictionary<Renderer, MeshData>();

    static MeshData GetMesh(Renderer r)
    {
        MeshData d;
        if (meshCache.TryGetValue(r, out d)) return d;
        d = null;
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            d = new MeshData();
            WorldMesh(r, out d.wv, out d.idx);
            if (d.wv.Length == 0 || d.idx.Length == 0) d = null;
        }
        meshCache[r] = d;
        return d;
    }

    // Nearest front-facing intersection of a ray with the visible mesh (the renderer's triangles, not its collider).
    static bool RayMesh(Vector3 o, Vector3 d, MeshData m, float maxDist, out float dist)
    {
        dist = float.MaxValue;
        Vector3[] wv = m.wv; int[] idx = m.idx;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            Vector3 a = wv[idx[i]];
            Vector3 e1 = wv[idx[i + 1]] - a, e2 = wv[idx[i + 2]] - a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (det < 1e-9f) continue; // back-facing or parallel
            float inv = 1f / det;
            Vector3 t = o - a;
            float u = Vector3.Dot(t, p) * inv;
            if (u < -1e-4f || u > 1.0001f) continue;
            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-4f || u + v > 1.0001f) continue;
            float dd = Vector3.Dot(e2, q) * inv;
            if (dd >= 0f && dd < dist) dist = dd;
        }
        return dist <= maxDist;
    }

    static float Halton(int i, int b)
    {
        float f = 1f, r = 0f;
        i++;
        while (i > 0) { f /= b; r += f * (i % b); i /= b; }
        return r;
    }

    // A renderer is verified when it is expected to be solid: it has a collider, or the rules say it should have one.
    static void AddZoneSources(Zone z, Transform root, Transform areaRoot, bool ownedArea)
    {
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
        {
            if (!r.enabled) continue;
            var info = Classify(r, areaRoot);
            if (info.kind == Kind.SkipNoMesh || info.Max < MinSize) continue;
            bool has = r.GetComponent<Collider>() != null;
            if (has || info.kind == Kind.Structural || info.kind == Kind.DoorLeaf || (ownedArea && info.kind == Kind.Prop)) z.sources.Add(r);
        }
    }

    static List<Zone> BuildZones(Transform map)
    {
        var zones = new List<Zone>();
        var corridors = map.Find("Corridors");
        var ez = map.Find("Corridors/EZ");
        if (ez != null)
            foreach (Transform m in ez)
            {
                if (!m.gameObject.activeInHierarchy) continue;
                string n = m.name;
                var z = new Zone { name = "EZ/" + n };
                z.category = n.Contains("Crossing") ? "crossing" : n.Contains("Straight") ? "corridor" : n.Contains("ThreeWay") ? "three-way" : "module/room";
                AddZoneSources(z, m, corridors, true);
                zones.Add(z);
            }
        var uc = map.Find("Corridors/U_Corridor_blockout/U_Corridor");
        if (uc != null) { var z = new Zone { name = "U corridor", category = "U corridor" }; AddZoneSources(z, uc, corridors, true); zones.Add(z); }
        var arm = map.Find("Corridors/U_Corridor_blockout/Armory");
        if (arm != null) { var z = new Zone { name = "Armory", category = "armory" }; AddZoneSources(z, arm, corridors, true); zones.Add(z); }
        var elevator = map.Find("Elevator");
        if (elevator != null) { var z = new Zone { name = "Elevator", category = "elevator" }; AddZoneSources(z, elevator, elevator, true); zones.Add(z); }
        var doors = map.Find(DoorsArea);
        if (doors != null)
        {
            var z = new Zone { name = "Map/Doors", category = "doors" };
            foreach (var r in doors.GetComponentsInChildren<MeshRenderer>(false))
                if (r.enabled && r.GetComponent<Collider>() != null) z.sources.Add(r);
            zones.Add(z);
        }
        var rooms = map.Find(GapFillArea);
        if (rooms != null)
            foreach (Transform room in rooms)
            {
                if (!room.gameObject.activeInHierarchy) continue;
                var z = new Zone { name = "Rooms/" + room.name, category = "UC room", density = 0.5f, maxSamples = 40 };
                AddZoneSources(z, room, rooms, false);
                zones.Add(z);
            }
        return zones;
    }

    // For each renderer: points spread evenly over its visible surface. From 0.25 m off each point, one ray against the
    // renderer's own triangles gives where the visible surface is, and the same ray against physics gives where the
    // player is stopped. The sample passes only when the two agree within VerifyTolerance.
    static ZoneResult VerifyZone(Zone z, List<Fail> fails)
    {
        var res = new ZoneResult { zone = z };
        foreach (var r in z.sources)
        {
            MeshData m = GetMesh(r);
            if (m == null) continue;
            var own = r.GetComponents<Collider>();
            var gm = r.GetComponent<GeneratedCollider>();
            bool doorOff = gm != null && gm.rule == "Door" && own.Length > 0;
            if (doorOff) foreach (var c in own) if (c.enabled) doorOff = false;
            string ownDesc = own.Length == 0 ? "no collider" : own[0].GetType().Name;

            int triCount = m.idx.Length / 3;
            var areas = new float[triCount];
            float total = 0f;
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = m.wv[m.idx[t * 3]], b = m.wv[m.idx[t * 3 + 1]], c = m.wv[m.idx[t * 3 + 2]];
                areas[t] = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                total += areas[t];
            }
            if (total < 1e-4f) continue;
            int n = Mathf.Clamp(Mathf.CeilToInt(total * z.density), 6, z.maxSamples);
            if ((long)n * triCount > VerifyTriBudget) n = Mathf.Max(4, (int)(VerifyTriBudget / triCount));
            float step = total / n, next = step * 0.5f, acc = 0f;
            int s = 0;
            for (int t = 0; t < triCount && s < n; t++)
            {
                acc += areas[t];
                while (acc >= next && s < n)
                {
                    next += step; s++;
                    if (areas[t] < 1e-6f) continue;
                    Vector3 a = m.wv[m.idx[t * 3]], b = m.wv[m.idx[t * 3 + 1]], c = m.wv[m.idx[t * 3 + 2]];
                    float u = Halton(s, 2), v = Halton(s, 3);
                    if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                    Vector3 p = Vector3.Lerp(a + (b - a) * u + (c - a) * v, (a + b + c) / 3f, 0.1f);
                    Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
                    Vector3 o = p + nrm * VerifyStandoff, d = -nrm;

                    if (doorOff) { res.passableDoor++; continue; }
                    float dV;
                    if (!RayMesh(o, d, m, VerifyStandoff + 0.01f, out dV)) dV = VerifyStandoff;
                    res.total++;
                    RaycastHit h;
                    if (!Physics.Raycast(o, d, out h, VerifyStandoff + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        fails.Add(new Fail { zone = z.name, renderer = r.name, collider = ownDesc, reason = "no collider at the surface", pos = p, dev = 0.3f });
                        continue;
                    }
                    float dev = h.distance - dV;
                    if (Mathf.Abs(dev) <= VerifyTolerance) { res.pass++; continue; }
                    string hitDesc = h.collider.GetType().Name + (h.collider.gameObject == r.gameObject ? "" : " of '" + h.collider.name + "'");
                    if (dev < 0f)
                    {
                        // something else is in front: fine when that collider sits on its own visible surface
                        var other = h.collider.GetComponent<Renderer>();
                        if (other != null && other != r && other.enabled)
                        {
                            MeshData om = GetMesh(other);
                            float dO;
                            if (om != null && RayMesh(o, d, om, h.distance + VerifyTolerance, out dO) && Mathf.Abs(h.distance - dO) <= VerifyTolerance)
                            {
                                res.occluded++; res.pass++; continue;
                            }
                        }
                        fails.Add(new Fail { zone = z.name, renderer = r.name, collider = hitDesc, reason = "collider in front of the surface", pos = p, dev = -dev });
                    }
                    else fails.Add(new Fail { zone = z.name, renderer = r.name, collider = hitDesc, reason = "collider behind the surface", pos = p, dev = dev });
                }
            }
        }
        return res;
    }

    // The reverse check: every enabled collider must belong to a visible renderer on its own GameObject and stay inside it.
    static string PhantomReport(Transform map)
    {
        var sb = new StringBuilder();
        int checkedCount = 0, bad = 0;
        foreach (var c in map.GetComponentsInChildren<Collider>(false))
        {
            if (!c.enabled || c.isTrigger) continue;
            checkedCount++;
            var r = c.GetComponent<Renderer>();
            string why = null;
            if (r == null) why = "no renderer on its GameObject";
            else if (!r.enabled) why = "its renderer is disabled";
            else
            {
                Bounds cb = c.bounds, rb = r.bounds;
                float over = 0f;
                for (int a = 0; a < 3; a++) over = Mathf.Max(over, Mathf.Max(rb.min[a] - cb.min[a], cb.max[a] - rb.max[a]));
                if (over > VerifyTolerance) why = "extends " + F(over) + " m beyond its renderer";
            }
            if (why == null) continue;
            bad++;
            if (bad <= 25) sb.AppendLine("  " + PathOf(c.transform, map) + " (" + c.GetType().Name + ", bounds " + V(c.bounds.size) + " at " + V(c.bounds.center) + "): " + why);
        }
        if (bad > 25) sb.AppendLine("  ...");
        return "-- Colliders without a matching visible surface: " + bad + " of " + checkedCount + " enabled colliders --\n" + sb;
    }

    static string PathOf(Transform t, Transform stop)
    {
        string s = t.name;
        while (t.parent != null && t.parent != stop) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    public static string VerifyReport()
    {
        var sb = new StringBuilder();
        var mapGo = GameObject.Find("Map");
        if (mapGo == null) return "No 'Map' object in the open scene.";
        Physics.SyncTransforms();
        meshCache.Clear();

        var zones = BuildZones(mapGo.transform);
        var results = new List<ZoneResult>();
        var fails = new List<Fail>();
        foreach (var z in zones) results.Add(VerifyZone(z, fails));
        meshCache.Clear();

        sb.AppendLine("=== SURFACE VERIFICATION ===");
        int doorsOn; int doorsAll = CountDoorColliders(out doorsOn);
        sb.AppendLine("Door colliders enabled while verifying: " + doorsOn + " of " + doorsAll + (doorsOn == doorsAll ? " (doors solid)" : "  (doors passable - test mode: door leaves are counted separately, not as failures)"));
        sb.AppendLine("Method: points spread over the visible surface of every renderer that should be solid. From " + F(VerifyStandoff) + " m off each point, one ray against the renderer's triangles finds the visible surface and the same ray against physics finds the collider. A sample passes only when the two are within " + F(VerifyTolerance) + " m.");
        int tT = 0, tP = 0, tO = 0, tD = 0;
        foreach (var r in results)
        {
            tT += r.total; tP += r.pass; tO += r.occluded; tD += r.passableDoor;
            sb.AppendLine(string.Format("{0,-40} [{1}]  {2}/{3} pass ({4} behind another verified surface){5}", r.zone.name, r.zone.category, r.pass, r.total, r.occluded, r.passableDoor > 0 ? ", " + r.passableDoor + " on passable doors" : ""));
        }
        sb.AppendLine("TOTAL " + tP + "/" + tT + " pass, " + (tT - tP) + " fail" + (tD > 0 ? ", " + tD + " samples on passable doors not counted" : ""));
        if (tT > 0 && tP == 0) sb.AppendLine("WARNING: every sample failed - physics queries may not be seeing the colliders (check Physics.SyncTransforms / collider cooking).");

        if (fails.Count > 0)
        {
            var groups = new Dictionary<string, List<Fail>>();
            foreach (var f in fails)
            {
                string k = f.zone + " | " + Stem(f.renderer) + " | " + f.reason + " (" + f.collider + ")";
                if (!groups.ContainsKey(k)) groups[k] = new List<Fail>();
                groups[k].Add(f);
            }
            var keys = new List<string>(groups.Keys);
            keys.Sort((p, q) => groups[q].Count.CompareTo(groups[p].Count));
            sb.AppendLine("Failures grouped by zone | renderer | reason:");
            int n = 0;
            foreach (var k in keys)
            {
                if (n++ >= 40) { sb.AppendLine("  ... " + (keys.Count - 40) + " more group(s)"); break; }
                float worst = 0f; Fail w = groups[k][0];
                foreach (var f in groups[k]) if (f.dev > worst) { worst = f.dev; w = f; }
                sb.AppendLine("  " + k + " x" + groups[k].Count + "  worst " + F(worst) + " m at " + V(w.pos));
            }
        }
        else sb.AppendLine("No failed samples.");

        sb.AppendLine(PhantomReport(mapGo.transform));
        sb.AppendLine(StairClimbTest(mapGo.transform));
        return sb.ToString();
    }

    // Walks a temporary CharacterController (player dimensions) up every detected flight of active stairs.
    static string StairClimbTest(Transform map)
    {
        var sb = new StringBuilder();
        var cc0 = Object.FindFirstObjectByType<CharacterController>();
        float slope = cc0 != null ? cc0.slopeLimit : 45f;
        sb.AppendLine("-- Stair climb test (temporary CharacterController, radius 0.4, height 1.8, slope limit " + F(slope) + " = the player's) --");
        float sceneStep = cc0 != null ? cc0.stepOffset : 0.3f;
        float runtimeStep = RuntimeStepOffset();
        bool any = false;
        var corridors = map.Find("Corridors");
        if (corridors == null) return sb.ToString();
        foreach (var r in corridors.GetComponentsInChildren<MeshRenderer>(false))
        {
            var info = Classify(r, corridors);
            if (info.kind != Kind.Structural || r.GetComponent<Collider>() == null) continue;
            foreach (var flight in FindStairFlights(r))
            {
                any = true;
                foreach (float step in new float[] { sceneStep, runtimeStep })
                {
                    if (step <= 0f) continue;
                    sb.AppendLine("  " + r.name + " flight y " + F(flight[0].Y) + " to " + F(flight[flight.Count - 1].Y) + ", step offset " + F(step) + ": " + ClimbOnce(flight, step, slope));
                }
            }
        }
        if (!any) sb.AppendLine("  no active stairs found.");
        return sb.ToString();
    }

    // Tests every riser on its own: stand on tread i, walk toward tread i+1, and check the controller ended up on it.
    static string ClimbOnce(List<Tread> treads, float stepOffset, float slopeLimit)
    {
        GameObject go = null;
        try
        {
            go = new GameObject("__TT_CC_Test");
            go.hideFlags = HideFlags.HideAndDontSave;
            var cc = go.AddComponent<CharacterController>();
            cc.radius = 0.4f; cc.height = 1.8f; cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = slopeLimit; cc.skinWidth = 0.08f; cc.stepOffset = stepOffset;
            int ok = 0;
            var failed = new List<string>();
            for (int i = 0; i + 1 < treads.Count; i++)
            {
                Vector3 from = treads[i].Centroid, to = treads[i + 1].Centroid;
                cc.enabled = false;
                go.transform.position = new Vector3(from.x, treads[i].Y + 0.03f, from.z);
                cc.enabled = true;
                Physics.SyncTransforms();
                cc.Move(Vector3.down * 0.1f); // settle onto the tread
                for (int it = 0; it < 40; it++)
                {
                    Vector3 pos = go.transform.position;
                    Vector3 d = new Vector3(to.x - pos.x, 0, to.z - pos.z);
                    if (d.magnitude < 0.1f) break;
                    cc.Move(d.normalized * Mathf.Min(0.08f, d.magnitude) + Vector3.down * 0.05f);
                }
                float endY = go.transform.position.y;
                if (endY >= treads[i + 1].Y - 0.1f) ok++;
                else
                {
                    // report how steep the surface between the two treads is: a smooth ramp at or above the slope limit blocks the controller
                    float steepest = 0f;
                    for (int k = 1; k <= 8; k++)
                    {
                        Vector3 q = Vector3.Lerp(from, to, k / 8f);
                        RaycastHit sh;
                        if (Physics.Raycast(new Vector3(q.x, Mathf.Max(treads[i].Y, treads[i + 1].Y) + 0.6f, q.z), Vector3.down, out sh, 2f, ~0, QueryTriggerInteraction.Ignore))
                            steepest = Mathf.Max(steepest, Vector3.Angle(sh.normal, Vector3.up));
                    }
                    failed.Add("step from y " + F(treads[i].Y) + " to " + F(treads[i + 1].Y) + " at " + V(from) + " (steepest surface between: " + F(steepest) + " deg, controller slope limit " + F(cc.slopeLimit) + ")");
                }
            }
            int total = treads.Count - 1;
            return ok + " of " + total + " risers climbable" + (failed.Count > 0 ? "; blocked: " + string.Join("; ", failed.ToArray()) : "");
        }
        catch (System.Exception e) { return "test could not run: " + e.Message; }
        finally { if (go != null) Object.DestroyImmediate(go); }
    }
}
