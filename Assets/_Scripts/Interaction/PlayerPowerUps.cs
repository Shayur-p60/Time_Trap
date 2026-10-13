using UnityEngine;

// Runs the power-up effects on the player. Goes on the FirstPersonController.
//   Flash Pulse   2 s burst of wide bright light around the player (+ PowerUps.OnFlashPulse).
//   Speed Boost   +50% move speed for 8 s.
//   Zombie Freeze PowerUps.OnZombieFreeze, ended 5 s later by PowerUps.OnZombieFreezeEnded.
//   Compass       on-screen arrow to the nearest PowerUps.CompassTargets entry for 30 s.
public class PlayerPowerUps : MonoBehaviour
{
    public static PlayerPowerUps Instance { get; private set; }

    [Header("Flash Pulse")]
    public float flashDuration = 2f;
    public float flashIntensity = 60f;
    public float flashRange = 30f;
    public Color flashColour = Color.white;
    [Tooltip("Height of the flash light above the player's feet.")]
    public float flashHeight = 1.6f;

    [Header("Speed Boost")]
    public float speedMultiplier = 1.5f;
    public float speedDuration = 8f;

    [Header("Zombie Freeze")]
    public float freezeDuration = 5f;

    [Header("Compass")]
    public float compassDuration = 30f;

    PlayerMovementController movement;
    Light flashLight;
    float flashEndTime = -1f;
    float speedEndTime = -1f;
    float freezeEndTime = -1f;
    float compassEndTime = -1f;

    public bool SpeedBoostActive => speedEndTime >= 0f;
    public bool CompassActive => compassEndTime >= 0f;

    void Awake()
    {
        Instance = this;
        movement = GetComponent<PlayerMovementController>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Activate(PowerUpType type)
    {
        switch (type)
        {
            case PowerUpType.FlashPulse:
                EnsureFlashLight();
                flashLight.intensity = flashIntensity;
                flashLight.enabled = true;
                flashEndTime = Time.time + flashDuration;
                PowerUps.RaiseFlashPulse(transform.position, flashDuration);
                break;

            case PowerUpType.SpeedBoost:
                speedEndTime = Time.time + speedDuration;
                if (movement != null) movement.SpeedMultiplier = speedMultiplier;
                break;

            case PowerUpType.ZombieFreeze:
                freezeEndTime = Time.time + freezeDuration;
                PowerUps.RaiseZombieFreeze(freezeDuration);
                break;

            case PowerUpType.Compass:
                compassEndTime = Time.time + compassDuration;
                break;
        }
        // after the effect's own events, so a listener sees the freeze or the flash already counted
        PowerUps.RaisePowerUpUsed(type);
    }

    void Update()
    {
        float now = Time.time;

        if (flashEndTime >= 0f)
        {
            // full brightness, fading out over the last half second
            float left = flashEndTime - now;
            if (left <= 0f)
            {
                flashEndTime = -1f;
                flashLight.enabled = false;
            }
            else flashLight.intensity = flashIntensity * Mathf.Clamp01(left / 0.5f);
        }

        if (speedEndTime >= 0f && now >= speedEndTime)
        {
            speedEndTime = -1f;
            if (movement != null) movement.SpeedMultiplier = 1f;
        }

        if (freezeEndTime >= 0f && now >= freezeEndTime)
        {
            freezeEndTime = -1f;
            PowerUps.RaiseZombieFreezeEnded();
        }

        if (compassEndTime >= 0f)
        {
            if (now >= compassEndTime)
            {
                compassEndTime = -1f;
                GameHUD.Compass(false);
            }
            else UpdateCompass(compassEndTime - now);
        }
    }

    void UpdateCompass(float secondsLeft)
    {
        Transform target = PowerUps.NearestCompassTarget(transform.position);
        if (target == null)
        {
            GameHUD.Compass(true, false, 0f, "No signal");
            return;
        }
        Vector3 to = target.position - transform.position;
        float distance = to.magnitude;
        to.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        float angle = to.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(forward, to, Vector3.up) : 0f;
        GameHUD.Compass(true, true, angle, Mathf.RoundToInt(distance) + " m  (" + Mathf.CeilToInt(secondsLeft) + " s)");
    }

    void EnsureFlashLight()
    {
        if (flashLight != null) return;
        var go = new GameObject("FlashPulseLight");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, flashHeight, 0f);
        flashLight = go.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = flashColour;
        flashLight.range = flashRange;
        flashLight.shadows = LightShadows.None;
        flashLight.enabled = false;
    }

    void OnDisable()
    {
        if (movement != null) movement.SpeedMultiplier = 1f;
        if (flashLight != null) flashLight.enabled = false;
        if (freezeEndTime >= 0f) PowerUps.RaiseZombieFreezeEnded();
        if (compassEndTime >= 0f) GameHUD.Compass(false);
        flashEndTime = speedEndTime = freezeEndTime = compassEndTime = -1f;
    }
}
