using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Fills the PickupSpawnPoint markers at game start.
//   Groups are placed in order on randomly chosen markers: by default 5 chests and 3 power-ups first, then 2 batteries
//   and 2 ammo pickups on the markers that are left. Each item of a group is drawn at random from that group's
//   definitions (duplicates allowed), so adding a pickup type is just adding a definition asset to a group.
//   Guarantees: at least one battery source (battery or chest) on a Stage1 marker and at least one ammo source
//   (ammo or chest) on a Stage2Only marker. Items are swapped between markers when the random layout misses one.
//   Use Fixed Seed makes the layout (and the chest contents) identical every run.
public class PickupSpawner : MonoBehaviour
{
    [Serializable]
    public class SpawnGroup
    {
        public string label;
        [Tooltip("Each item in the group is one of these, drawn at random (duplicates allowed).")]
        public PickupDefinition[] definitions = new PickupDefinition[0];
        public int count;
    }

    public struct Assignment
    {
        public PickupSpawnPoint point;
        public PickupDefinition definition; // null = marker left empty
    }

    [Tooltip("The generic pickup prefab every pickup is made from.")]
    public GameObject pickupPrefab;
    [Tooltip("Placed in this order: chests and power-ups first, then batteries and ammo on what is left.")]
    public SpawnGroup[] groups = new SpawnGroup[0];

    [Header("Seed")]
    [Tooltip("On: the same layout every run (for demos and testing).")]
    public bool useFixedSeed;
    public int seed = 1;

    [Header("Markers")]
    [Tooltip("Markers are read from under this object. Left empty, every PickupSpawnPoint in the scene is used.")]
    public Transform markerRoot;
    public bool spawnOnStart = true;

    // Seed used by the most recent Spawn or Preview Layout.
    public int LastSeed { get; private set; }
    public readonly List<Pickup> Spawned = new List<Pickup>();

    void Start()
    {
        if (spawnOnStart) Spawn();
    }

    // ------------------------------------------------------------------ spawning

    public void Spawn()
    {
        Spawn(useFixedSeed ? seed : Environment.TickCount);
    }

    public void Spawn(int layoutSeed)
    {
        var layout = ComputeLayout(layoutSeed, out string problems);
        if (!string.IsNullOrEmpty(problems)) Debug.LogWarning("[Time Trap] PickupSpawner (seed " + layoutSeed + "): " + problems, this);

        // chest contents come from the same seed, in marker order
        var rng = new System.Random(unchecked(layoutSeed * 486187739 + 1));
        foreach (var a in layout)
        {
            if (a.definition == null) continue;
            var pickup = Pickup.Create(a.definition, pickupPrefab, a.point.transform.position, a.point.transform.rotation, transform);
            if (pickup is Chest chest) chest.rewardSeed = rng.Next(1, int.MaxValue);
            Spawned.Add(pickup);
        }
    }

    // ------------------------------------------------------------------ layout

    // Every marker, in a stable order (by name), so a seed always means the same layout.
    public List<PickupSpawnPoint> GetPoints()
    {
        var points = new List<PickupSpawnPoint>();
        if (markerRoot != null) points.AddRange(markerRoot.GetComponentsInChildren<PickupSpawnPoint>(false));
        else points.AddRange(FindObjectsByType<PickupSpawnPoint>(FindObjectsInactive.Exclude));
        points.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return points;
    }

    // Decides which marker gets which definition. Does not create anything. One entry per marker, in marker order.
    public List<Assignment> ComputeLayout(int layoutSeed, out string problems)
    {
        LastSeed = layoutSeed;
        var warn = new StringBuilder();
        var points = GetPoints();
        var rng = new System.Random(layoutSeed);

        // the items, in group order
        var items = new List<PickupDefinition>();
        foreach (var group in groups)
        {
            if (group == null || group.definitions == null) continue;
            var options = new List<PickupDefinition>();
            foreach (var d in group.definitions) if (d != null) options.Add(d);
            if (options.Count == 0)
            {
                if (group.count > 0) warn.Append("group '" + group.label + "' has no definitions. ");
                continue;
            }
            for (int i = 0; i < group.count; i++) items.Add(options[rng.Next(options.Count)]);
        }

        // random marker order (Fisher-Yates), then item i goes on marker i
        var order = new List<int>();
        for (int i = 0; i < points.Count; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }

        var definitions = new PickupDefinition[points.Count];
        for (int i = 0; i < items.Count && i < order.Count; i++) definitions[order[i]] = items[i];
        if (items.Count > points.Count) warn.Append(items.Count + " items but only " + points.Count + " markers: " + (items.Count - points.Count) + " not placed. ");

        // guarantees
        Func<PickupDefinition, bool> battery = d => d.CanYieldBattery;
        Func<PickupDefinition, bool> ammo = d => d.CanYieldAmmo;
        if (!Ensure(points, definitions, battery, StageReachable.Stage1, ammo, StageReachable.Stage2Only))
            warn.Append("could not put a battery source on a Stage1 marker. ");
        if (!Ensure(points, definitions, ammo, StageReachable.Stage2Only, battery, StageReachable.Stage1))
            warn.Append("could not put an ammo source on a Stage2Only marker. ");

        var layout = new List<Assignment>();
        for (int i = 0; i < points.Count; i++) layout.Add(new Assignment { point = points[i], definition = definitions[i] });
        problems = warn.ToString();
        return layout;
    }

    static bool Holds(List<PickupSpawnPoint> points, PickupDefinition[] definitions, Func<PickupDefinition, bool> isSource, StageReachable stage)
    {
        for (int i = 0; i < points.Count; i++)
            if (definitions[i] != null && points[i].stageReachable == stage && isSource(definitions[i])) return true;
        return false;
    }

    // Makes sure a source sits on a marker of the given stage, by swapping one in from another marker.
    // A swap that would break the other guarantee (when it already holds) is not used.
    static bool Ensure(List<PickupSpawnPoint> points, PickupDefinition[] definitions,
        Func<PickupDefinition, bool> isSource, StageReachable stage,
        Func<PickupDefinition, bool> otherSource, StageReachable otherStage)
    {
        if (Holds(points, definitions, isSource, stage)) return true;
        bool otherHeld = Holds(points, definitions, otherSource, otherStage);

        for (int from = 0; from < points.Count; from++)
        {
            if (definitions[from] == null || points[from].stageReachable == stage || !isSource(definitions[from])) continue;
            for (int to = 0; to < points.Count; to++)
            {
                if (points[to].stageReachable != stage) continue;
                Swap(definitions, from, to);
                if (!otherHeld || Holds(points, definitions, otherSource, otherStage)) return true;
                Swap(definitions, from, to);
            }
        }
        return false;
    }

    static void Swap(PickupDefinition[] definitions, int a, int b)
    {
        var t = definitions[a]; definitions[a] = definitions[b]; definitions[b] = t;
    }

    // True when the layout meets both guarantees.
    public static bool GuaranteesHold(List<Assignment> layout, out bool batteryOnStage1, out bool ammoOnStage2Only)
    {
        batteryOnStage1 = false;
        ammoOnStage2Only = false;
        foreach (var a in layout)
        {
            if (a.definition == null) continue;
            if (a.point.stageReachable == StageReachable.Stage1 && a.definition.CanYieldBattery) batteryOnStage1 = true;
            if (a.point.stageReachable == StageReachable.Stage2Only && a.definition.CanYieldAmmo) ammoOnStage2Only = true;
        }
        return batteryOnStage1 && ammoOnStage2Only;
    }

    // ------------------------------------------------------------------ preview

    // Shows which marker gets what as gizmo labels on the markers and in the Console. Nothing is spawned.
    [ContextMenu("Preview Layout")]
    public string PreviewLayout()
    {
        return PreviewLayout(useFixedSeed ? seed : Environment.TickCount);
    }

    public string PreviewLayout(int layoutSeed)
    {
        var layout = ComputeLayout(layoutSeed, out string problems);
        var sb = new StringBuilder();
        sb.AppendLine("[Time Trap] Pickup layout for seed " + layoutSeed + (useFixedSeed ? " (fixed)" : " (random: Use Fixed Seed is off)") + ":");
        foreach (var a in layout)
        {
            string what = a.definition != null ? a.definition.displayName : "(empty)";
            a.point.previewLabel = "seed " + layoutSeed + ": " + what;
            sb.AppendLine("  " + a.point.name + "  [" + a.point.stageReachable + "]  ->  " + what);
        }
        GuaranteesHold(layout, out bool battery, out bool ammo);
        sb.AppendLine("  Battery source on a Stage1 marker: " + (battery ? "yes" : "NO") + ".  Ammo source on a Stage2Only marker: " + (ammo ? "yes" : "NO") + ".");
        if (!string.IsNullOrEmpty(problems)) sb.AppendLine("  Problems: " + problems);
        string report = sb.ToString();
        Debug.Log(report, this);
        return report;
    }

    [ContextMenu("Clear Preview")]
    public void ClearPreview()
    {
        foreach (var p in GetPoints()) p.previewLabel = null;
    }
}
