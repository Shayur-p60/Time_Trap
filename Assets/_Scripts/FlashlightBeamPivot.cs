using UnityEngine;

// Holds the flashlight's Light. It sits under the camera, a little right of and below centre, and turns to follow
// the view with a small lag and a tiny sway, so the beam always points where the player looks instead of wherever
// the animated hand happens to point. The flashlight model stays in the hand; only the light lives here.
// While the pistol is drawn the light moves to the mount under the gun barrel, so the beam comes from the gun; it
// still aims where the player looks.
public class FlashlightBeamPivot : MonoBehaviour
{
    [Tooltip("What the beam follows. Left empty, the parent (the camera).")]
    public Transform view;

    [Header("Follow")]
    [Tooltip("How quickly the beam catches up with the view (per second). Higher = tighter.")]
    public float followSpeed = 12f;
    [Tooltip("The beam is never allowed to trail the view by more than this (degrees), however fast the player turns.")]
    public float maxLagAngle = 8f;
    [Tooltip("The beam is aimed at the point this far along the view line (metres), so it meets the screen centre there.")]
    public float convergeDistance = 10f;

    [Header("Sway")]
    [Tooltip("Size of the idle sway (degrees, each way).")]
    public float swayAngle = 0.4f;
    [Tooltip("Sway cycles per second.")]
    public float swaySpeed = 0.35f;

    [Header("Hand fill light")]
    [Tooltip("The faint light on the hand and flashlight (HandFill). Full strength while the flashlight is on, dimmed while it is off.")]
    public Light fillLight;
    [Tooltip("The flashlight's own light. Left empty, the Light on this object.")]
    public Light beamLight;
    [Tooltip("Fill strength while the flashlight is off, as a fraction of its normal strength.")]
    [Range(0f, 1f)] public float fillWhenOff = 0.2f;
    [Tooltip("Seconds the fill takes to fade between the two.")]
    public float fillFadeTime = 0.2f;

    [Header("Weapon light")]
    [Tooltip("While the pistol is drawn the light sits here instead (WeaponLightMount under the gun barrel). Set by PlayerWeaponState.")]
    public Transform mount;
    [Tooltip("0 = light at its own place beside the camera, 1 = light at the mount. Set by PlayerWeaponState.")]
    [Range(0f, 1f)] public float mountWeight;

    Vector3 homeLocalPosition;
    Quaternion smoothed;
    bool initialised;
    float fillFullIntensity;
    float fillLevel = 1f;

    // Current fill strength as a fraction of full (1 = flashlight on, Fill When Off = flashlight off).
    public float FillLevel => fillLevel;

    void Awake()
    {
        homeLocalPosition = transform.localPosition;
        if (beamLight == null) beamLight = GetComponent<Light>();
        if (fillLight != null) fillFullIntensity = fillLight.intensity;
    }

    // Angle between the beam and the view direction right now (degrees).
    public float AngleToView => view != null ? Vector3.Angle(transform.forward, view.forward) : 0f;

    void OnEnable()
    {
        initialised = false;
    }

    // LateUpdate: after the movement script has turned the camera this frame.
    void LateUpdate()
    {
        if (view == null) view = transform.parent;
        if (view == null) return;

        // where the light sits: beside the camera, or under the gun barrel while the pistol is drawn
        Vector3 home = transform.parent != null ? transform.parent.TransformPoint(homeLocalPosition) : transform.position;
        transform.position = mount != null && mountWeight > 0f ? Vector3.Lerp(home, mount.position, mountWeight) : home;

        // aim at a point straight ahead of the view; from the offset pivot this is a slight toe-in
        Vector3 aimPoint = view.position + view.forward * Mathf.Max(1f, convergeDistance);
        Quaternion target = Quaternion.LookRotation(aimPoint - transform.position, view.up);

        if (!initialised)
        {
            smoothed = target;
            initialised = true;
        }

        smoothed = Quaternion.Slerp(smoothed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        float lag = Quaternion.Angle(smoothed, target);
        if (lag > maxLagAngle) smoothed = Quaternion.Slerp(target, smoothed, maxLagAngle / lag);

        float t = Time.time * swaySpeed * Mathf.PI * 2f;
        Quaternion sway = Quaternion.Euler(Mathf.Sin(t * 1.3f + 1.1f) * swayAngle, Mathf.Sin(t) * swayAngle, 0f);

        transform.rotation = smoothed * sway;

        UpdateFill();
    }

    void UpdateFill()
    {
        if (fillLight == null || beamLight == null) return;
        float target = beamLight.enabled ? 1f : fillWhenOff;
        float rate = fillFadeTime > 0f ? (1f - fillWhenOff) / fillFadeTime : 1000f;
        fillLevel = Mathf.MoveTowards(fillLevel, target, Mathf.Max(rate, 0.01f) * Time.deltaTime);
        fillLight.intensity = fillFullIntensity * fillLevel;
    }
}
