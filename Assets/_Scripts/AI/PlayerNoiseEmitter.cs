using UnityEngine;

// Turns the player's movement into noise the zombies can hear. Goes on the FirstPersonController.
// Sprinting is loud, walking is quiet, crouching or standing still is silent.
public class PlayerNoiseEmitter : MonoBehaviour
{
    [Header("Noise radius by movement speed")]
    [Tooltip("Below this speed the player makes no noise at all.")]
    public float silentSpeedThreshold = 0.6f;
    public float walkNoiseRadius = 6f;
    public float sprintNoiseRadius = 16f;
    [Tooltip("Speed at which Sprint Noise Radius is reached.")]
    public float sprintSpeed = 6f;
    public bool crouchIsSilent = true;

    [Header("Timing")]
    [Tooltip("Seconds between footstep noises.")]
    public float emitInterval = 0.45f;

    public int NoisesMade { get; private set; }
    public float LastRadius { get; private set; }

    PlayerMovementController movement;
    Vector3 lastPosition;
    float emitTimer;

    void Awake()
    {
        movement = GetComponent<PlayerMovementController>();
    }

    void Start()
    {
        lastPosition = transform.position;
    }

    void Update()
    {
        // speed from the position change, so a teleport or respawn is not a footstep
        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;
        float speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        if (speed > sprintSpeed * 3f) speed = 0f;

        bool silent = speed < silentSpeedThreshold || (crouchIsSilent && movement != null && movement.IsCrouching);
        if (silent)
        {
            emitTimer = 0f;
            return;
        }

        emitTimer += Time.deltaTime;
        if (emitTimer < emitInterval) return;
        emitTimer = 0f;

        float t = Mathf.InverseLerp(silentSpeedThreshold, sprintSpeed, speed);
        EmitOneShot(Mathf.Lerp(walkNoiseRadius, sprintNoiseRadius, t));
    }

    // A one-off noise at the player's position.
    public void EmitOneShot(float radius)
    {
        NoisesMade++;
        LastRadius = radius;
        NoiseSystem.MakeNoise(transform.position, radius, NoiseKind.Player, transform);
    }
}
