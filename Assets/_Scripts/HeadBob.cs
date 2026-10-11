using UnityEngine;

// Goes on the player camera. A small up-and-down bob with a tiny side sway while walking on the ground, so movement
// reads as steps instead of gliding. It is driven by distance walked, so it speeds up with Speed Boost and slows with
// crouch, and it eases back to rest when the player stops or leaves the ground. Sprinting takes a longer stride
// (Sprint Step Distance), so its rhythm is a run, not a faster walk.
//
// It only ever adds an offset on top of the camera's own position and takes that offset off again before the
// movement script runs (this component updates first), so crouch height and anything else that moves the camera
// keep working unchanged.
[DefaultExecutionOrder(-100)]
public class HeadBob : MonoBehaviour
{
    [Header("References (found on the parents when empty)")]
    public CharacterController controller;
    public PlayerMovementController movement;

    [Header("Bob")]
    [Tooltip("Height of the bob at walking speed, top to bottom (metres).")]
    public float bobHeight = 0.04f;
    [Tooltip("Side sway at walking speed, each way (metres).")]
    public float sideSway = 0.008f;
    [Tooltip("Metres walked per step at walking speed.")]
    public float stepDistance = 0.9f;
    [Tooltip("Metres run per step at sprint speed: a longer stride, so sprinting is about 3.6 steps a second instead of a blur. Between walk and sprint speed the stride is blended.")]
    public float sprintStepDistance = 1.67f;
    [Tooltip("How quickly the stride changes when the speed does (metres of stride per second), so the rhythm never jumps.")]
    public float strideChangeSpeed = 2.5f;
    [Tooltip("Speed (m/s) at which the bob is at its set size.")]
    public float referenceSpeed = 3.5f;
    [Tooltip("The bob grows with speed (sprint, Speed Boost) up to this many times its set size.")]
    public float maxSpeedScale = 1.5f;
    [Tooltip("Bob size while crouching, as a fraction.")]
    [Range(0f, 1f)] public float crouchScale = 0.6f;

    [Header("Easing")]
    [Tooltip("Below this speed (m/s) the player counts as standing still.")]
    public float minSpeed = 0.3f;
    [Tooltip("How quickly the bob fades in and out (per second).")]
    public float easeSpeed = 5f;
    [Tooltip("Seconds the bob keeps going after the ground is lost (stairs, small ledges).")]
    public float groundedGrace = 0.15f;

    Vector3 appliedOffset;
    float stepPhase;   // counts steps; whole numbers are footfalls
    float weight;      // 0 = at rest, 1 = full bob
    float lastGroundedTime = -999f;

    // The offset on the camera right now (local space, metres).
    public Vector3 CurrentOffset => appliedOffset;
    public float Weight => weight;
    // Counts steps: goes 0 to 2 and round again, whole numbers are footfalls. Other scripts (the gun bob) read this to stay in step.
    public float StepPhase => stepPhase;
    // Metres per step right now. ArmSway and the footsteps read the phase above, so everything keeps this one rhythm.
    public float CurrentStride => stride > 0f ? stride : stepDistance;

    float stride;

    // The stride for a ground speed: Step Distance up to walk speed, Sprint Step Distance at sprint speed, eased
    // between. Speed Boost makes the player faster, not longer-legged: its multiplier is taken out first.
    public float StrideFor(float speed)
    {
        if (movement == null) return stepDistance;
        float multiplier = Mathf.Max(0.01f, movement.SpeedMultiplier);
        float t = Mathf.InverseLerp(movement.WalkSpeed, movement.SprintSpeed, speed / multiplier);
        return Mathf.Lerp(stepDistance, sprintStepDistance, Mathf.SmoothStep(0f, 1f, t));
    }

    void Awake()
    {
        if (controller == null) controller = GetComponentInParent<CharacterController>();
        if (movement == null) movement = GetComponentInParent<PlayerMovementController>();
    }

    void OnDisable()
    {
        RemoveOffset();
        weight = 0f;
    }

    // Runs before the movement script: hand the camera back exactly as that script left it.
    void Update()
    {
        RemoveOffset();
    }

    void RemoveOffset()
    {
        if (appliedOffset == Vector3.zero) return;
        transform.localPosition -= appliedOffset;
        appliedOffset = Vector3.zero;
    }

    // Runs after everything has moved: add this frame's bob.
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || controller == null) return;

        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;

        if (controller.isGrounded) lastGroundedTime = Time.time;
        bool onGround = Time.time - lastGroundedTime <= groundedGrace;
        bool walking = onGround && speed > minSpeed;

        weight = Mathf.MoveTowards(weight, walking ? 1f : 0f, dt * easeSpeed);
        float targetStride = StrideFor(speed);
        stride = stride <= 0f ? targetStride : Mathf.MoveTowards(stride, targetStride, strideChangeSpeed * dt);
        if (walking) stepPhase = Mathf.Repeat(stepPhase + speed * dt / Mathf.Max(0.1f, stride), 2f);

        float size = weight * Mathf.Clamp(speed / Mathf.Max(0.1f, referenceSpeed), 0f, maxSpeedScale);
        if (movement != null && movement.IsCrouching) size *= crouchScale;

        // one dip per step; one left-right swing per two steps
        Vector3 offset = new Vector3(
            Mathf.Sin(stepPhase * Mathf.PI) * sideSway * size,
            Mathf.Sin(stepPhase * Mathf.PI * 2f) * bobHeight * 0.5f * size,
            0f);

        transform.localPosition += offset;
        appliedOffset = offset;
    }
}
