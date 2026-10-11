using UnityEngine;

// Subtle first-person sway for the arms rig (the object under the camera that holds the arms and their Animator).
//   Look sway: the arms trail a little behind the view when the player turns or looks up and down.
//   Move bob:  a gentle figure-of-eight while walking on the ground, scaled by speed.
// Every frame the pose is the recorded rest pose plus a smoothed offset, so nothing can build up: when the player
// stops moving and looking, the arms ease back to exactly the rest pose.
public class ArmSway : MonoBehaviour
{
    [Header("References")]
    public CharacterController playerController; // for speed and the turn (yaw); found on a parent when empty
    [Tooltip("Found on a parent when empty. With one, the arms bob on its step phase, so camera, arms and footsteps keep one rhythm.")]
    public HeadBob headBob;

    [Header("Look sway")]
    [Tooltip("Metres the arms trail per degree-per-second of turning or looking.")]
    public float lookSwayPosition = 0.00008f;
    [Tooltip("Most the arms can trail (metres).")]
    public float lookSwayMaxPosition = 0.02f;
    [Tooltip("Degrees the arms tilt per degree-per-second of turning or looking.")]
    public float lookSwayRotation = 0.012f;
    [Tooltip("Most the arms can tilt (degrees).")]
    public float lookSwayMaxRotation = 2.5f;

    [Header("Move bob")]
    [Tooltip("Up-and-down bob at walking speed (metres, each way).")]
    public float bobHeight = 0.012f;
    [Tooltip("Side-to-side bob at walking speed (metres, each way).")]
    public float bobSide = 0.008f;
    [Tooltip("Metres walked per step. Only used without a HeadBob.")]
    public float stepDistance = 0.9f;
    [Tooltip("Speed (m/s) at which the bob is at its set size. Faster makes it a little bigger, up to 1.5 times.")]
    public float referenceSpeed = 3.5f;

    [Header("Smoothing")]
    [Tooltip("How quickly the arms follow the sway and ease back to rest (per second).")]
    public float smoothing = 9f;

    Vector3 restPosition;
    Quaternion restRotation;
    bool restCaptured;

    Vector3 positionOffset;
    Vector3 rotationOffset; // euler degrees
    float lastYaw, lastPitch;
    float stepPhase;
    float bobWeight;

    public Vector3 RestPosition => restPosition;
    // How far the arms are from rest right now (metres).
    public float DistanceFromRest => restCaptured ? (transform.localPosition - restPosition).magnitude : 0f;

    void Awake()
    {
        CaptureRest();
        if (playerController == null) playerController = GetComponentInParent<CharacterController>();
        if (headBob == null) headBob = GetComponentInParent<HeadBob>();
    }

    void OnEnable()
    {
        positionOffset = Vector3.zero;
        rotationOffset = Vector3.zero;
        bobWeight = 0f;
        ReadLook(out lastYaw, out lastPitch);
    }

    void OnDisable()
    {
        ResetToRest();
    }

    // Records the current local pose as the rest pose. Done once, before anything has moved the arms.
    void CaptureRest()
    {
        if (restCaptured) return;
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        restCaptured = true;
    }

    public void ResetToRest()
    {
        if (!restCaptured) return;
        positionOffset = Vector3.zero;
        rotationOffset = Vector3.zero;
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
    }

    void ReadLook(out float yaw, out float pitch)
    {
        yaw = playerController != null ? playerController.transform.eulerAngles.y : transform.root.eulerAngles.y;
        pitch = transform.parent != null ? transform.parent.localEulerAngles.x : 0f;
    }

    // LateUpdate: after the movement script has turned the player and the Animator has posed the arms.
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- look sway: how fast the view is turning
        ReadLook(out float yaw, out float pitch);
        float yawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        float pitchRate = Mathf.DeltaAngle(lastPitch, pitch) / dt;
        lastYaw = yaw;
        lastPitch = pitch;

        Vector3 targetPosition = new Vector3(
            Mathf.Clamp(-yawRate * lookSwayPosition, -lookSwayMaxPosition, lookSwayMaxPosition),
            Mathf.Clamp(pitchRate * lookSwayPosition, -lookSwayMaxPosition, lookSwayMaxPosition),
            0f);
        Vector3 targetRotation = new Vector3(
            Mathf.Clamp(-pitchRate * lookSwayRotation, -lookSwayMaxRotation, lookSwayMaxRotation),
            Mathf.Clamp(-yawRate * lookSwayRotation, -lookSwayMaxRotation, lookSwayMaxRotation),
            Mathf.Clamp(-yawRate * lookSwayRotation * 0.5f, -lookSwayMaxRotation, lookSwayMaxRotation));

        // --- move bob: only while walking on the ground
        float speed = 0f;
        bool grounded = true;
        if (playerController != null)
        {
            Vector3 velocity = playerController.velocity;
            velocity.y = 0f;
            speed = velocity.magnitude;
            grounded = playerController.isGrounded;
        }
        bool walking = grounded && speed > 0.3f;
        bobWeight = Mathf.MoveTowards(bobWeight, walking ? 1f : 0f, dt * 4f);
        // the camera's step counter when there is one (its stride lengthens for a sprint); otherwise our own
        if (headBob != null && headBob.isActiveAndEnabled) stepPhase = headBob.StepPhase;
        else if (walking) stepPhase = Mathf.Repeat(stepPhase + speed * dt / Mathf.Max(0.1f, stepDistance), 2f);
        float size = bobWeight * Mathf.Clamp(speed / Mathf.Max(0.1f, referenceSpeed), 0.4f, 1.5f);
        targetPosition.x += Mathf.Sin(stepPhase * Mathf.PI) * bobSide * size;
        targetPosition.y += Mathf.Sin(stepPhase * Mathf.PI * 2f) * bobHeight * size;

        // --- smooth towards the target (frame-rate independent); with no input the target is zero = rest
        float k = 1f - Mathf.Exp(-smoothing * dt);
        positionOffset = Vector3.Lerp(positionOffset, targetPosition, k);
        rotationOffset = Vector3.Lerp(rotationOffset, targetRotation, k);

        transform.localPosition = restPosition + positionOffset;
        transform.localRotation = restRotation * Quaternion.Euler(rotationOffset);
    }
}
