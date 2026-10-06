using UnityEngine;

// Play-mode counterpart of the Time Trap/Lighting preview menus: switches the two tagged light groups when the power changes.
//   Not powered (Stage 1): Light_Emergency on, Light_Powered off.
//   Powered (Stage 2):     Light_Powered on, Light_Emergency off.
// It only runs in Play mode, so it never touches the saved scene. One instance is enough (Map/Gameplay).
public class PowerLighting : MonoBehaviour
{
    public const string EmergencyTag = "Light_Emergency";
    public const string PoweredTag = "Light_Powered";

    public static PowerLighting Instance { get; private set; }

    Light[] emergencyLights = new Light[0];
    Light[] poweredLights = new Light[0];
    float[] poweredIntensity = new float[0];

    bool ramping;
    float rampTime;
    float rampDuration;

    void Awake()
    {
        Instance = this;

        var emergency = new System.Collections.Generic.List<Light>();
        var powered = new System.Collections.Generic.List<Light>();
        foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (l.CompareTag(EmergencyTag)) emergency.Add(l);
            else if (l.CompareTag(PoweredTag)) powered.Add(l);
        }
        emergencyLights = emergency.ToArray();
        poweredLights = powered.ToArray();
        poweredIntensity = new float[poweredLights.Length];
        for (int i = 0; i < poweredLights.Length; i++) poweredIntensity[i] = poweredLights[i].intensity;
    }

    void OnEnable()
    {
        PowerState.OnPowerChanged += Apply;
    }

    void OnDisable()
    {
        PowerState.OnPowerChanged -= Apply;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        Apply(PowerState.IsPowered);
    }

    // Fades the powered lights up from dark over the given time. The generator console calls this just before it
    // switches the power on; the emergency lights stay on until the power actually changes.
    public void BeginPowerUp(float duration)
    {
        if (duration <= 0f) return;
        ramping = true;
        rampTime = 0f;
        rampDuration = duration;
        for (int i = 0; i < poweredLights.Length; i++)
        {
            if (poweredLights[i] == null) continue;
            poweredLights[i].intensity = 0f;
            poweredLights[i].enabled = true;
        }
    }

    void Update()
    {
        if (!ramping) return;
        rampTime += Time.deltaTime;
        float k = Mathf.Clamp01(rampTime / rampDuration);
        k *= k;
        for (int i = 0; i < poweredLights.Length; i++)
            if (poweredLights[i] != null) poweredLights[i].intensity = poweredIntensity[i] * k;
    }

    void Apply(bool powered)
    {
        ramping = false;
        for (int i = 0; i < poweredLights.Length; i++)
        {
            if (poweredLights[i] == null) continue;
            poweredLights[i].intensity = poweredIntensity[i];
            poweredLights[i].enabled = powered;
        }
        foreach (var l in emergencyLights)
            if (l != null) l.enabled = !powered;
    }
}
