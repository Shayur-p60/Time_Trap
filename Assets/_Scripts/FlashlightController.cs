using UnityEngine;
using TimeTrap.UI;

public class FlashlightController : MonoBehaviour
{
    [Header("References")]
    public Light flashlightLight;

    [Header("Battery Settings")]
    public float maxBattery = 100f;
    [Tooltip("Battery used per second while the light is on.")]
    public float drainRate = 0.4f;
    [Tooltip("Battery regained per second while the light is off (including when it has run flat), up to Max Battery.")]
    public float rechargeRate = 0.2f;
    [Tooltip("The light cannot be switched on until the battery has at least this much charge (so it does not die the instant it is switched on).")]
    public float minBatteryToSwitchOn = 1f;

    [Header("Low Battery Flicker")]
    [Tooltip("Below this battery percentage (0-100) the light flickers occasionally.")]
    [Range(0f, 100f)] public float lowBatteryThresholdPercent = 15f;
    [Tooltip("Minimum gap between one dip ending and the next starting (seconds). 0.3 keeps it to about 2-3 dips a second at most.")]
    public float flickerMinInterval = 0.3f;
    [Tooltip("Maximum gap between dips (seconds).")]
    public float flickerMaxInterval = 1.5f;
    [Tooltip("How long a dip lasts (seconds): random between these two values.")]
    public float flickerDipMinDuration = 0.04f;
    public float flickerDipMaxDuration = 0.12f;
    [Tooltip("Intensity during a dip as a fraction of normal: random between these two values.")]
    [Range(0f, 1f)] public float flickerDipMinIntensity = 0.1f;
    [Range(0f, 1f)] public float flickerDipMaxIntensity = 0.5f;

    private float currentBattery;
    private bool isOn = true;
    private bool heldForMenu;

    // flicker state
    private float clock;
    private float baseIntensity;
    private bool baseIntensityCaptured;
    private bool dipping;
    private float dipEndTime;
    private float nextDipTime;

    void Start()
    {
        currentBattery = maxBattery;
        if (flashlightLight != null)
        {
            baseIntensity = flashlightLight.intensity;
            baseIntensityCaptured = true;
            flashlightLight.enabled = isOn;
        }
        ScheduleNextDip();
    }

    void Update()
    {
        // main menu and intro cutscene: the torch waits, switched off and not draining, until the fly-through (or the
        // cutscene camera) hands the picture over
        bool elsewhere = MenuCameraRig.Active || CutsceneDirector.OwnsPicture;
        if (elsewhere || GameManager.BeforeRun)
        {
            if (elsewhere && flashlightLight != null && flashlightLight.enabled)
            {
                flashlightLight.enabled = false;
                heldForMenu = true;
            }
            if (elsewhere) return;
        }
        if (heldForMenu)
        {
            heldForMenu = false;
            if (flashlightLight != null) flashlightLight.enabled = isOn;
        }
        if (GameManager.BeforeRun) return;

        if (Input.GetKeyDown(KeyCode.F) && !GameManager.InputBlocked)
            ToggleLight();

        Tick(Time.deltaTime);
    }

    public bool IsOn => isOn;

    // The switch was pressed and the light changed: (now on). Not raised when the battery runs flat.
    public event System.Action<bool> OnToggled;

    // F pressed: always allowed to switch off; switching on needs some charge.
    public void ToggleLight()
    {
        bool wasOn = isOn;
        if (isOn)
            SetOn(false);
        else if (currentBattery > 0f && currentBattery >= Mathf.Min(minBatteryToSwitchOn, maxBattery))
            SetOn(true);
        if (isOn != wasOn) OnToggled?.Invoke(isOn);
    }

    // One frame of battery and flicker logic.
    void Tick(float deltaTime)
    {
        clock += deltaTime;

        if (isOn)
        {
            currentBattery = Mathf.Max(currentBattery - drainRate * deltaTime, 0f);
            if (currentBattery <= 0f)
                SetOn(false);
        }
        else
        {
            currentBattery = Mathf.Min(currentBattery + rechargeRate * deltaTime, maxBattery);
        }

        UpdateFlicker();
    }

    void OnDisable()
    {
        RestoreIntensity();
    }

    void SetOn(bool value)
    {
        isOn = value;
        RestoreIntensity();
        if (flashlightLight != null)
            flashlightLight.enabled = value;
        if (value)
            ScheduleNextDip();
    }

    // Brief random dips in intensity while the battery is below the low-battery threshold.
    void UpdateFlicker()
    {
        if (flashlightLight == null || !baseIntensityCaptured)
            return;

        bool low = isOn && GetBatteryPercent() * 100f < lowBatteryThresholdPercent;
        if (!low)
        {
            if (dipping) RestoreIntensity();
            return;
        }

        if (dipping)
        {
            if (clock >= dipEndTime)
            {
                RestoreIntensity();
                ScheduleNextDip();
            }
        }
        else if (clock >= nextDipTime)
        {
            dipping = true;
            dipEndTime = clock + Random.Range(flickerDipMinDuration, flickerDipMaxDuration);
            flashlightLight.intensity = baseIntensity * Random.Range(flickerDipMinIntensity, flickerDipMaxIntensity);
        }
    }

    void ScheduleNextDip()
    {
        nextDipTime = clock + Random.Range(flickerMinInterval, flickerMaxInterval);
    }

    void RestoreIntensity()
    {
        dipping = false;
        if (flashlightLight != null && baseIntensityCaptured)
            flashlightLight.intensity = baseIntensity;
    }

    // Call this from your chest/pickup system when the player finds a battery reward
    public void AddBattery(float amount)
    {
        currentBattery = Mathf.Min(currentBattery + amount, maxBattery);
    }

    public float GetBatteryPercent() => currentBattery / maxBattery;
}
