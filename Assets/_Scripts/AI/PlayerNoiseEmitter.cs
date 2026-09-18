using UnityEngine;

/// <summary>
/// Turns player movement into noise the zombies can hear. Attach to the same
/// object as your FirstPersonController (the one tagged Player).
///
/// Sprinting makes you loud, walking makes you quiet, crouching or standing
/// still makes you effectively silent. This is what feeds the Investigate state.
/// </summary>
public class PlayerNoiseEmitter : MonoBehaviour
{
    [Header("Noise radius by movement speed")]
    [Tooltip("Below this speed you make no noise at all.")]
    public float silentSpeedThreshold = 0.6f;
    public float walkNoiseRadius = 6f;
    public float sprintNoiseRadius = 16f;
    [Tooltip("Speed at which sprintNoiseRadius is reached.")]
    public float sprintSpeed = 6f;

    [Header("Timing")]
    [Tooltip("How often a footstep noise is emitted, in seconds.")]
    public float emitInterval = 0.45f;

    private Vector3 lastPosition;
    private float emitTimer;

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        // Measure speed from position delta so this works with a CharacterController,
        // a Rigidbody, or anything else driving the player.
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;

        if (speed < silentSpeedThreshold)
        {
            emitTimer = 0f;
            return;
        }

        emitTimer += Time.deltaTime;
        if (emitTimer < emitInterval) return;
        emitTimer = 0f;

        float t = Mathf.InverseLerp(silentSpeedThreshold, sprintSpeed, speed);
        float radius = Mathf.Lerp(walkNoiseRadius, sprintNoiseRadius, t);

        NoiseEvent.Emit(transform.position, radius, transform);
    }

    /// <summary>Call this for one-off loud events: a door slam, a dropped item, a gunshot.</summary>
    public void EmitOneShot(float radius)
    {
        NoiseEvent.Emit(transform.position, radius, transform);
    }
}
