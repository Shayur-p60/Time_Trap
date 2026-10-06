using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor setup for GeneratorRoomAlarm (the Bioreactors / generator room alarm). No meshes or materials are changed.
//   Time Trap/Lighting/Setup Generator Room Alarm  - build or rebuild it (re-runnable)
//   Time Trap/Lighting/Remove Generator Room Alarm - delete it again
//
// The ceiling ring is one mesh made of 20 identical lamp units. This finds the 20 unit positions (18 degrees apart) and puts one
// untagged red flash light at every second unit (10 lights), just inside and below the ring. The lights start off (intensity 0,
// disabled), which is what gets recorded as their original state.
public static class GeneratorRoomAlarmSetup
{
    const string RoomPath = "Rooms/UC_Bioreactors_1.0";
    const string RingName = "sm35_084_labolight01a_00ms.010";
    const string DeckPath = "sm43_526_movablebridge01a_00md/LODGroup_1_MainMesh_7_SubMesh_1.001";
    const int RingUnits = 20;

    [MenuItem("Time Trap/Lighting/Setup Generator Room Alarm")]
    static void Setup()
    {
        var mapGo = GameObject.Find("Map");
        if (mapGo == null) { Debug.LogError("[Time Trap] No 'Map' object in the open scene."); return; }
        var map = mapGo.transform;
        var room = map.Find(RoomPath);
        var ringT = room != null ? room.Find(RingName) : null;
        var deckT = room != null ? room.Find(DeckPath) : null;
        if (ringT == null || deckT == null) { Debug.LogError("[Time Trap] Could not find the Bioreactors ceiling ring or deck strip renderers."); return; }

        var lighting = map.Find("Lighting");
        var group = lighting.Find("Bioreactors");
        if (group == null)
        {
            var g = new GameObject("Bioreactors");
            Undo.RegisterCreatedObjectUndo(g, "Create Bioreactors lighting group");
            g.transform.SetParent(lighting, false);
            group = g.transform;
        }

        // clean slate if it already exists
        var alarmT = group.Find("GeneratorAlarm");
        if (alarmT != null)
        {
            var old = alarmT.GetComponent<GeneratorRoomAlarm>();
            if (old != null) old.RestoreOriginals();
            for (int i = alarmT.childCount - 1; i >= 0; i--) Object.DestroyImmediate(alarmT.GetChild(i).gameObject);
        }
        else
        {
            var go = new GameObject("GeneratorAlarm");
            Undo.RegisterCreatedObjectUndo(go, "Create GeneratorAlarm");
            go.transform.SetParent(group, false);
            alarmT = go.transform;
        }
        var alarm = alarmT.GetComponent<GeneratorRoomAlarm>();
        if (alarm == null) alarm = alarmT.gameObject.AddComponent<GeneratorRoomAlarm>();

        // the room centre (x,z of the ceiling light)
        var umbrella = room.Find("Light_UmbrellaCeiling");
        Vector3 centre = umbrella != null ? umbrella.position : new Vector3(111.5f, 0f, -12.86f);

        // the 20 lamp units of the ring: group the mesh's connected pieces by 18-degree sector around the room centre
        Bounds[] unit = new Bounds[RingUnits]; bool[] has = new bool[RingUnits];
        var mesh = ringT.GetComponent<MeshFilter>().sharedMesh;
        var comps = Analyse(mesh, ringT.localToWorldMatrix);
        foreach (var b in comps)
        {
            Vector3 d = b.center - centre;
            float ang = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
            int s = Mathf.FloorToInt(Mathf.Repeat(ang + 180f / RingUnits, 360f) / (360f / RingUnits)) % RingUnits;
            if (!has[s]) { unit[s] = b; has[s] = true; } else unit[s].Encapsulate(b);
        }

        var lights = new List<Light>();
        for (int i = 0; i < RingUnits; i += 2)
        {
            if (!has[i]) continue;
            Vector3 p = unit[i].center;
            var lg = new GameObject("Flash_Ceiling_" + i.ToString("00"));
            lg.transform.SetParent(alarmT, false);
            Vector3 inward = centre - p; inward.y = 0; inward.Normalize();
            lg.transform.position = p + inward * 1.0f + Vector3.down * 0.5f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 26f; l.shadows = LightShadows.None;
            l.color = new Color(0.83f, 0.97f, 1f); l.intensity = 0f; l.enabled = false; // original state: off
            lights.Add(l);
        }

        alarm.ceilingRenderer = ringT.GetComponent<MeshRenderer>();
        alarm.ceilingMaterialIndex = 0;
        alarm.stripRenderer = deckT.GetComponent<MeshRenderer>();
        alarm.stripMaterialIndex = 0;
        alarm.flashLights = lights.ToArray();
        alarm.RecordOriginals();
        alarm.Refresh();

        EditorUtility.SetDirty(alarm);
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
        Debug.Log("[Time Trap] Generator room alarm set up: " + lights.Count + " flash lights on " + CountUnits(has) + " ring units found; strip renderer '" + deckT.name + "'.");
    }

    [MenuItem("Time Trap/Lighting/Remove Generator Room Alarm")]
    static void Remove()
    {
        var mapGo = GameObject.Find("Map");
        if (mapGo == null) return;
        var alarmT = mapGo.transform.Find("Lighting/Bioreactors/GeneratorAlarm");
        if (alarmT == null) { Debug.Log("[Time Trap] No GeneratorAlarm to remove."); return; }
        var alarm = alarmT.GetComponent<GeneratorRoomAlarm>();
        if (alarm != null) alarm.RestoreOriginals();
        Undo.DestroyObjectImmediate(alarmT.gameObject);
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i));
        Debug.Log("[Time Trap] Generator room alarm removed.");
    }

    static int CountUnits(bool[] has) { int n = 0; foreach (var h in has) if (h) n++; return n; }

    static int Find(int[] p, int a)
    {
        while (p[a] != a) { p[a] = p[p[a]]; a = p[a]; }
        return a;
    }

    // World bounds of each connected piece of a mesh (vertices welded by position).
    static List<Bounds> Analyse(Mesh src, Matrix4x4 localToWorld)
    {
        var v = src.vertices; var idx = src.triangles;
        var weld = new Dictionary<Vector3Int, int>(); var map = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 1000f), Mathf.RoundToInt(v[i].y * 1000f), Mathf.RoundToInt(v[i].z * 1000f));
            int id; if (!weld.TryGetValue(k, out id)) { id = weld.Count; weld[k] = id; }
            map[i] = id;
        }
        var parent = new int[weld.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        for (int t = 0; t < idx.Length; t += 3)
        {
            int a = Find(parent, map[idx[t]]), b = Find(parent, map[idx[t + 1]]), c = Find(parent, map[idx[t + 2]]);
            parent[b] = a; parent[Find(parent, c)] = a;
        }
        var bounds = new Dictionary<int, Bounds>();
        for (int t = 0; t < idx.Length; t += 3)
        {
            int r = Find(parent, map[idx[t]]);
            for (int j = 0; j < 3; j++)
            {
                Vector3 w = localToWorld.MultiplyPoint3x4(v[idx[t + j]]);
                if (!bounds.ContainsKey(r)) bounds[r] = new Bounds(w, Vector3.zero);
                else { var b = bounds[r]; b.Encapsulate(w); bounds[r] = b; }
            }
        }
        return new List<Bounds>(bounds.Values);
    }
}
