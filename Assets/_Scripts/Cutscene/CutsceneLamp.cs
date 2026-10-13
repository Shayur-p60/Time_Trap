using UnityEngine;

// One red emergency lamp in the portal chamber of the intro cutscene: its light pulses slowly, and no two lamps keep
// the same time (each starts at a random point of the pulse, at a slightly different speed). While the portal powers
// up, CutscenePortal raises Flicker and the lamp stutters. Real (unscaled) time.
[RequireComponent(typeof(Light))]
public class CutsceneLamp : MonoBehaviour
{
    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 0.35f;
    [Tooltip("Each lamp's speed differs by up to this fraction either way.")]
    [Range(0f, 0.5f)] public float speedVariation = 0.25f;
    [Tooltip("Brightness at the bottom of the pulse, as a fraction of the light's intensity.")]
    [Range(0f, 1f)] public float minBrightness = 0.3f;
    [Tooltip("Where in the pulse this lamp starts (0-1). Negative = random.")]
    public float phase = -1f;

    // 0 = steady pulse, 1 = stuttering hard. Set by CutscenePortal.
    [System.NonSerialized] public float flicker;

    // Brightness right now, as a fraction of the light's own intensity (for tests).
    public float Level { get; private set; } = 1f;

    Light lamp;
    float intensity;
    float speed;
    float stutter = 1f;
    float nextStutter;

    void Awake()
    {
        lamp = GetComponent<Light>();
        intensity = lamp.intensity;
        if (phase < 0f) phase = Random.value;
        speed = pulseSpeed * (1f + Random.Range(-speedVariation, speedVariation));
    }

    void OnDisable()
    {
        if (lamp != null) lamp.intensity = intensity;
        flicker = 0f;
    }

    void Update()
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin((Time.unscaledTime * speed + phase) * Mathf.PI * 2f);
        Level = Mathf.Lerp(minBrightness, 1f, pulse);

        if (flicker > 0f)
        {
            // a new random brightness about twenty times a second
            nextStutter -= Time.unscaledDeltaTime;
            if (nextStutter <= 0f)
            {
                nextStutter = Random.Range(0.03f, 0.08f);
                stutter = Random.value < 0.35f ? Random.Range(0.05f, 0.4f) : Random.Range(0.8f, 1.5f);
            }
            Level *= Mathf.Lerp(1f, stutter, flicker);
        }
        lamp.intensity = intensity * Level;
    }
}
