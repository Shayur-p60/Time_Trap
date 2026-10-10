using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

// Goes on a Door, next to it. Each door panel carries a carving NavMeshObstacle; this switches them with the door:
// they carve the NavMesh while the door is closed (zombies path around it, or have no path) and are switched off as
// soon as the door starts to open, the same moment its colliders go.
// Decoy, External and PowerLocked doors need nothing special: they carve for as long as they stay closed.
// A doorway too narrow to bake (the Nap room's) is bridged by a NavMeshLink instead. Carving does not cut a link, so
// those are handled the other way round: on only while the door is open.
[RequireComponent(typeof(Door))]
public class DoorNavObstacle : MonoBehaviour
{
    [Tooltip("The carving obstacles on this door's panels.")]
    public NavMeshObstacle[] obstacles = new NavMeshObstacle[0];
    [Tooltip("NavMeshLinks through this doorway. Left empty, the ones on this door's child objects are used. On only while the door is open.")]
    public NavMeshLink[] links = new NavMeshLink[0];

    Door door;
    bool appliedOpen;

    // True while the obstacles are carving (the door is closed).
    public bool IsCarving => !appliedOpen;

    void Awake()
    {
        door = GetComponent<Door>();
        FindLinks();
    }

    void FindLinks()
    {
        if (links == null || links.Length == 0) links = GetComponentsInChildren<NavMeshLink>(true);
    }

    void OnEnable()
    {
        if (door == null) door = GetComponent<Door>();
        door.OnDoorMoved += HandleDoorMoved;
        Apply(door.IsOpen);
    }

    void OnDisable()
    {
        if (door != null) door.OnDoorMoved -= HandleDoorMoved;
    }

    void HandleDoorMoved(Door moved, bool opening)
    {
        Apply(opening);
    }

    void LateUpdate()
    {
        // Door.SetOpenImmediate raises no event
        if (door.IsOpen != appliedOpen) Apply(door.IsOpen);
    }

    // Matches the obstacles to the door's state now. Public so it can be called without Play mode.
    public void Sync()
    {
        if (door == null) door = GetComponent<Door>();
        FindLinks();
        Apply(door.IsOpen);
    }

    void Apply(bool open)
    {
        appliedOpen = open;
        foreach (var obstacle in obstacles)
            if (obstacle != null) obstacle.enabled = !open;
        foreach (var link in links)
            if (link != null) link.enabled = open;
    }
}
