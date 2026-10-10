using UnityEngine;

// Goes on the weapon's hold target under the camera (PistolHoldTarget): the point the gun hand is held on by the
// hand IK. Moving this target moves the gun and both hands, so this is where the gun's motion is made:
//   Look sway:  the gun trails a little behind the view when the player turns or looks up and down.
//   Walk bob:   a small bob in step with the head bob (it reads HeadBob's step phase).
//   Recoil:     a kick back and up on every shot, which springs back.
//   Reload dip: the gun drops and tilts while reloading, and comes back up at the end.
// Every frame the pose is the recorded rest pose plus these offsets, so nothing can build up.
public class ViewmodelMotion : MonoBehaviour
{
    [Header("References (found on the parents when empty)")]
    public CharacterController playerController;
    public HeadBob headBob;
    public Pistol pistol;

    [Header("Look sway")]
    [Tooltip("Metres the gun trails per degree-per-second of turning or looking.")]
    public float lookSwayPosition = 0.00008f;
    [Tooltip("Most the gun can trail (metres).")]
    public float lookSwayMaxPosition = 0.015f;
    [Tooltip("Degrees the gun tilts per degree-per-second of turning or looking.")]
    public float lookSwayRotation = 0.012f;
    [Tooltip("Most the gun can tilt (degrees).")]
    public float lookSwayMaxRotation = 2.5f;
    [Tooltip("How quickly the gun follows the sway and eases back (per second).")]
    public float swaySmoothing = 10f;

    [Header("Walk bob (in step with HeadBob)")]
    [Tooltip("Up-and-down bob at walking speed (metres, each way).")]
    public float bobHeight = 0.006f;
    [Tooltip("Side-to-side bob at walking speed (metres, each way).")]
    public float bobSide = 0.005f;
    [Tooltip("Speed (m/s) at which the bob is at its set size.")]
    public float referenceSpeed = 3.5f;

    [Header("Recoil (per shot)")]
    [Tooltip("How far the gun jumps back (metres).")]
    public float recoilBack = 0.035f;
    [Tooltip("How far the gun jumps up (metres).")]
    public float recoilUp = 0.01f;
    [Tooltip("How far the muzzle flips up (degrees).")]
    public float recoilPitch = 7f;
    [Tooltip("Random sideways twist per shot (degrees, each way).")]
    public float recoilYaw = 1.2f;
    [Tooltip("How quickly the kick arrives (per second).")]
    public float recoilSnap = 40f;
    [Tooltip("How quickly the gun settles back (per second).")]
    public float recoilReturn = 9f;
    [Tooltip("The kick never builds beyond this many shots' worth when firing fast.")]
    public float recoilMaxShots = 2f;

    [Header("Reload dip")]
    [Tooltip("How far the gun drops while reloading (metres).")]
    public float reloadDrop = 0.09f;
    [Tooltip("How far the gun tilts while reloading (degrees: pitch down, roll).")]
    public Vector2 reloadTilt = new Vector2(22f, 12f);
    [Tooltip("Fraction of the reload spent going down, and the same again coming back up.")]
    [Range(0.05f, 0.5f)] public float reloadEase = 0.2f;

    Vector3 restPosition;
    Quaternion restRotation;
    bool restCaptured;

    Vector3 swayPosition, swayRotation;        // smoothed
    Vector3 recoilTargetPosition, recoilTargetRotation;
    Vector3 recoilPosition, recoilRotation;    // what is applied
    float lastYaw, lastPitch;
    Pistol subscribedTo;

    // For the Inspector and tests.
    public Vector3 RestPosition => restPosition;
    public float DistanceFromRest => restCaptured ? (transform.localPosition - restPosition).magnitude : 0f;
    // Muzzle flip from recoil right now (degrees).
    public float RecoilPitchNow => -recoilRotation.x;
    // 0 = up, 1 = fully dipped for the reload.
    public float ReloadDip { get; private set; }
    public Vector3 BobOffset { get; private set; }

    void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        restCaptured = true;
        if (playerController == null) playerController = GetComponentInParent<CharacterController>();
        if (headBob == null) headBob = GetComponentInParent<HeadBob>();
        if (pistol == null && playerController != null) pistol = playerController.GetComponentInChildren<Pistol>(true);
    }

    void OnEnable()
    {
        swayPosition = swayRotation = Vector3.zero;
        recoilTargetPosition = recoilTargetRotation = recoilPosition = recoilRotation = Vector3.zero;
        ReadLook(out lastYaw, out lastPitch);
        Subscribe();
    }

    void OnDisable()
    {
        if (subscribedTo != null) subscribedTo.OnFired -= Kick;
        subscribedTo = null;
        if (restCaptured)
        {
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
        }
    }

    void Subscribe()
    {
        if (subscribedTo == pistol) return;
        if (subscribedTo != null) subscribedTo.OnFired -= Kick;
        subscribedTo = pistol;
        if (subscribedTo != null) subscribedTo.OnFired += Kick;
    }

    // One shot's worth of recoil. Called by the Pistol; can also be called directly.
    public void Kick()
    {
        recoilTargetPosition += new Vector3(0f, recoilUp, -recoilBack);
        recoilTargetRotation += new Vector3(-recoilPitch, Random.Range(-recoilYaw, recoilYaw), 0f);

        float most = Mathf.Max(1f, recoilMaxShots);
        recoilTargetPosition.y = Mathf.Min(recoilTargetPosition.y, recoilUp * most);
        recoilTargetPosition.z = Mathf.Max(recoilTargetPosition.z, -recoilBack * most);
        recoilTargetRotation.x = Mathf.Max(recoilTargetRotation.x, -recoilPitch * most);
        recoilTargetRotation.y = Mathf.Clamp(recoilTargetRotation.y, -recoilYaw * most, recoilYaw * most);
    }

    void ReadLook(out float yaw, out float pitch)
    {
        yaw = playerController != null ? playerController.transform.eulerAngles.y : transform.root.eulerAngles.y;
        pitch = transform.parent != null ? transform.parent.localEulerAngles.x : 0f;
    }

    // LateUpdate: after the movement script has turned the player and HeadBob has moved the camera.
    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Subscribe();

        // --- look sway
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
        float k = 1f - Mathf.Exp(-swaySmoothing * dt);
        swayPosition = Vector3.Lerp(swayPosition, targetPosition, k);
        swayRotation = Vector3.Lerp(swayRotation, targetRotation, k);

        // --- walk bob, on HeadBob's step phase so gun and head move together
        Vector3 bob = Vector3.zero;
        if (headBob != null && playerController != null)
        {
            Vector3 velocity = playerController.velocity;
            velocity.y = 0f;
            float size = headBob.Weight * Mathf.Clamp(velocity.magnitude / Mathf.Max(0.1f, referenceSpeed), 0f, 1.5f);
            float phase = headBob.StepPhase;
            bob = new Vector3(Mathf.Sin(phase * Mathf.PI) * bobSide * size, Mathf.Sin(phase * Mathf.PI * 2f) * bobHeight * size, 0f);
        }
        BobOffset = bob;

        // --- recoil: the kick arrives fast, then both the kick and the gun settle back
        float settle = 1f - Mathf.Exp(-recoilReturn * dt);
        recoilTargetPosition = Vector3.Lerp(recoilTargetPosition, Vector3.zero, settle);
        recoilTargetRotation = Vector3.Lerp(recoilTargetRotation, Vector3.zero, settle);
        float snap = 1f - Mathf.Exp(-recoilSnap * dt);
        recoilPosition = Vector3.Lerp(recoilPosition, recoilTargetPosition, snap);
        recoilRotation = Vector3.Lerp(recoilRotation, recoilTargetRotation, snap);

        // --- reload dip: down at the start, held, back up at the end
        float dip = 0f;
        if (pistol != null && pistol.IsReloading)
        {
            float p = pistol.ReloadProgress;
            dip = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / reloadEase)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - p) / reloadEase));
        }
        ReloadDip = Mathf.MoveTowards(ReloadDip, dip, dt * 12f);
        Vector3 reloadPosition = new Vector3(0f, -reloadDrop * ReloadDip, 0f);
        Vector3 reloadRotation = new Vector3(reloadTilt.x * ReloadDip, 0f, reloadTilt.y * ReloadDip);

        transform.localPosition = restPosition + swayPosition + bob + recoilPosition + reloadPosition;
        transform.localRotation = restRotation * Quaternion.Euler(swayRotation + recoilRotation + reloadRotation);
    }
}
