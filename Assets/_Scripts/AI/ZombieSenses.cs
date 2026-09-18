using UnityEngine;

/// <summary>
/// Handles "can I see the player" so the FSM doesn't have to. Keeping this
/// separate means you can tune detection per zombie type (a blind shambler
/// with big ears, a fast one with a wide cone) without touching state logic.
/// </summary>
public class ZombieSenses : MonoBehaviour
{
    [Header("Sight")]
    public float sightRange = 14f;
    [Range(0f, 360f)] public float fieldOfView = 110f;
    [Tooltip("Eye height above the pivot, used as the raycast origin.")]
    public float eyeHeight = 1.6f;

    [Header("Proximity")]
    [Tooltip("Detected at this distance regardless of facing direction.")]
    public float proximityRange = 3f;

    [Header("Layers")]
    [Tooltip("Walls, floors, props. Anything that should block line of sight.")]
    public LayerMask obstructionMask;

    [Header("Debug")]
    public bool drawGizmos = true;

    public Transform Player { get; private set; }
    public bool HasLineOfSight { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public float TimeSinceLastSeen => Time.time - lastSeenTime;

    private float lastSeenTime = -999f;

    private void Awake()
    {
        GameObject found = GameObject.FindGameObjectWithTag("Player");
        if (found != null)
        {
            Player = found.transform;
        }
        else
        {
            Debug.LogError(
                $"{name}: no GameObject tagged 'Player' in the scene. " +
                "Tag your FirstPersonController prefab root as Player.", this);
        }
    }

    private void Update()
    {
        HasLineOfSight = CheckSight();
        if (HasLineOfSight)
        {
            lastSeenTime = Time.time;
            LastKnownPosition = Player.position;
        }
    }

    private bool CheckSight()
    {
        if (Player == null) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 targetPoint = Player.position + Vector3.up * 1.0f;
        Vector3 toPlayer = targetPoint - eye;
        float distance = toPlayer.magnitude;

        if (distance > sightRange) return false;

        // Close enough that facing doesn't matter.
        bool withinProximity = distance <= proximityRange;

        if (!withinProximity)
        {
            Vector3 flatDirection = toPlayer;
            flatDirection.y = 0f;
            float angle = Vector3.Angle(transform.forward, flatDirection);
            if (angle > fieldOfView * 0.5f) return false;
        }

        // Linecast returns true when something BLOCKS the view.
        if (Physics.Linecast(eye, targetPoint, obstructionMask,
                             QueryTriggerInteraction.Ignore))
            return false;

        return true;
    }

    /// <summary>Call after a kill or a reset so stale memory doesn't linger.</summary>
    public void ForgetPlayer()
    {
        lastSeenTime = -999f;
        HasLineOfSight = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        Gizmos.color = HasLineOfSight ? Color.red : Color.yellow;
        Vector3 left = Quaternion.Euler(0, -fieldOfView * 0.5f, 0) * transform.forward;
        Vector3 right = Quaternion.Euler(0, fieldOfView * 0.5f, 0) * transform.forward;
        Gizmos.DrawRay(eye, left * sightRange);
        Gizmos.DrawRay(eye, right * sightRange);

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, sightRange);

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, proximityRange);
    }
}
