using UnityEngine;

// A trigger volume that kills the player: entering it raises PlayerEvents.OnPlayerKilled with this zone's cause.
// Besides the trigger message it also checks the player's position each frame, so a very fast fall (or a teleport)
// cannot skip through it. It raises once per entry, then sends the player back to the latest checkpoint.
[RequireComponent(typeof(Collider))]
public class DeathZone : MonoBehaviour
{
    [Tooltip("Passed to PlayerEvents.OnPlayerKilled: \"Fall\", \"OutOfBounds\", ...")]
    public string cause = "Fall";

    Collider zone;
    Transform player;
    bool playerInside;

    void Awake()
    {
        zone = GetComponent<Collider>();
    }

    // True if the point is inside the zone's volume.
    public bool Contains(Vector3 point)
    {
        if (zone == null) zone = GetComponent<Collider>();
        return zone != null && zone.enabled && (zone.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other is CharacterController && other.CompareTag("Player")) Kill();
    }

    void Update()
    {
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found == null) return;
            player = found.transform;
        }

        // the controller's centre, not its feet, so standing on a ledge just above the zone never counts
        bool inside = Contains(player.position + Vector3.up * 0.9f);
        if (inside) Kill();
        else playerInside = false;
    }

    void Kill()
    {
        if (playerInside) return;
        playerInside = true;
        PlayerEvents.RaisePlayerKilled(cause);
        // the penalty for this death ended the run: no respawn
        if (GameManager.IsGameOver) return;
        // back to the latest checkpoint. Without a CheckpointManager, TempRespawnHandler picks the event up instead.
        if (CheckpointManager.Instance != null) CheckpointManager.Instance.RespawnAtCheckpoint();
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.08f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.6f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
