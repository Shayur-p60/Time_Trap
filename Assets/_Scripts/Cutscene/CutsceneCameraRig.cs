using UnityEngine;

// The camera of the intro cutscene. Goes on the IntroCutscene camera. It is off until the director takes the picture.
// Three ways it is moved:
//   Shot   a Timeline's Camera Shot track calls SetShot every frame (the void: slow push-ins and cuts). The poses are
//          local to Shot Space, so the whole stage can be moved without redoing the shots.
//   Look   in the portal chamber the director sets a base pose (the player's eyes) and switches Look on: the mouse
//          turns the view, clamped to Yaw Limit and Pitch Limit, and it drifts back to the centre when left alone.
//   Walk   the director moves the base pose along the path into the portal and reports the distance with Walk, which
//          adds a gentle head bob and counts the steps (for the footstep sounds).
// Real (unscaled) time throughout.
[RequireComponent(typeof(Camera))]
public class CutsceneCameraRig : MonoBehaviour
{
    [Tooltip("Shot poses are local to this transform (the void stage). Empty = world space.")]
    public Transform shotSpace;

    [Header("Look (portal chamber)")]
    public float yawLimit = 45f;
    public float pitchLimit = 20f;
    [Tooltip("Degrees per unit of mouse movement, before the Settings sensitivity.")]
    public float sensitivity = 2f;
    [Tooltip("Seconds without mouse movement before the view starts drifting back.")]
    public float returnDelay = 1.2f;
    [Tooltip("How quickly it drifts back (per second).")]
    public float returnRate = 1.6f;

    [Header("Walk")]
    [Tooltip("Height of the bob, top to bottom (metres).")]
    public float bobHeight = 0.045f;
    [Tooltip("Side sway, each way (metres).")]
    public float sideSway = 0.012f;
    [Tooltip("Metres walked per step.")]
    public float stride = 0.9f;

    public bool LookEnabled { get; set; }
    // x = yaw, y = pitch, in degrees from the base pose.
    public Vector2 LookAngles { get; private set; }
    public int Steps { get; private set; }
    public Camera Camera => cam != null ? cam : (cam = GetComponent<Camera>());

    // For tests: mouse movement per frame, added to the real mouse.
    [System.NonSerialized] public Vector2 scriptedLook;

    Camera cam;
    Vector3 basePosition;
    Quaternion baseRotation = Quaternion.identity;
    float idle;
    float walked;
    float bobWeight;
    bool walking;

    void Awake()
    {
        cam = GetComponent<Camera>();
        basePosition = transform.position;
        baseRotation = transform.rotation;
    }

    // A Timeline shot: a pose local to Shot Space. Clears the look and the bob.
    public void SetShot(Vector3 localPosition, Quaternion localRotation, float fieldOfView)
    {
        if (shotSpace != null)
        {
            basePosition = shotSpace.TransformPoint(localPosition);
            baseRotation = shotSpace.rotation * localRotation;
        }
        else
        {
            basePosition = localPosition;
            baseRotation = localRotation;
        }
        Camera.fieldOfView = fieldOfView;
        LookEnabled = false;
        LookAngles = Vector2.zero;
        bobWeight = 0f;
        ApplyPose();
    }

    // The pose the look and the bob are added to (world space).
    public void SetBase(Vector3 position, Quaternion rotation)
    {
        basePosition = position;
        baseRotation = rotation;
    }

    public void SetFieldOfView(float fieldOfView)
    {
        Camera.fieldOfView = fieldOfView;
    }

    public void ResetLook()
    {
        LookAngles = Vector2.zero;
        idle = 0f;
    }

    // The base pose moved this far along the ground this frame. True when a foot lands.
    public bool Walk(float metres)
    {
        walking = metres > 0f;
        if (!walking) return false;
        float before = walked;
        walked += metres;
        float s = Mathf.Max(0.1f, stride);
        // a footfall at the lowest point of each dip, as in HeadBob
        int stepBefore = Mathf.FloorToInt(before / s + 0.25f);
        int stepNow = Mathf.FloorToInt(walked / s + 0.25f);
        if (stepNow == stepBefore) return false;
        Steps++;
        return true;
    }

    public void StopWalk()
    {
        walking = false;
    }

    void LateUpdate()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Vector2 angles = LookAngles;

        Vector2 mouse = Vector2.zero;
        if (LookEnabled)
        {
            float scale = sensitivity * Mathf.Clamp(GameSettings.Get(GameSettings.Sensitivity), GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
            mouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) + scriptedLook;
            mouse *= scale;
            if (GameSettings.IsOn(GameSettings.InvertY)) mouse.y = -mouse.y;
        }

        if (mouse.sqrMagnitude > 0.0001f)
        {
            idle = 0f;
            angles.x = Mathf.Clamp(angles.x + mouse.x, -yawLimit, yawLimit);
            angles.y = Mathf.Clamp(angles.y - mouse.y, -pitchLimit, pitchLimit);
        }
        else
        {
            idle += dt;
            // left alone (or with the look locked): ease back to the centre
            if (!LookEnabled || idle >= returnDelay) angles = Vector2.Lerp(angles, Vector2.zero, 1f - Mathf.Exp(-Mathf.Max(0.1f, returnRate) * dt));
        }
        LookAngles = angles;

        bobWeight = Mathf.MoveTowards(bobWeight, walking ? 1f : 0f, dt * 4f);
        ApplyPose();
    }

    void ApplyPose()
    {
        Quaternion rotation = baseRotation * Quaternion.Euler(LookAngles.y, LookAngles.x, 0f);
        Vector3 position = basePosition;
        if (bobWeight > 0f)
        {
            float phase = walked / Mathf.Max(0.1f, stride);
            // one dip per step; one left-right swing per two steps
            position += baseRotation * new Vector3(
                Mathf.Sin(phase * Mathf.PI) * sideSway,
                Mathf.Sin(phase * Mathf.PI * 2f) * bobHeight * 0.5f,
                0f) * bobWeight;
        }
        transform.SetPositionAndRotation(position, rotation);
    }
}
