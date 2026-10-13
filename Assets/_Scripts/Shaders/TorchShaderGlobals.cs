using UnityEngine;

// Tells shaders where the torch beam is. Sits on FlashlightBeamPivot (the object with the torch's spot light).
// Every frame it sets three global shader properties, which TimeTrap/DustParticles reads to make motes sparkle
// inside the beam:
//   _TorchPositionWS    where the light is
//   _TorchDirectionWS   which way it points
//   _TorchParams        x = cos(outer half-angle), y = cos(inner half-angle), z = range, w = brightness 0-1
//   _TorchTint          white, or gold once Time Lord is unlocked (see Golden)
// Brightness (Level) is 0 while the torch is off, follows the low-battery flicker, and dims as the battery runs down.
// FlashlightBeamCone uses the same Level for the visible beam.
[DefaultExecutionOrder(100)]   // after FlashlightBeamPivot has aimed the light this frame
public class TorchShaderGlobals : MonoBehaviour
{
    [Tooltip("The torch's spot light. Left empty, the Light on this object.")]
    public Light spot;
    [Tooltip("Read for the battery level. Left empty, found in the scene.")]
    public FlashlightController flashlight;
    [Tooltip("Below this battery fraction (0-1) the beam starts to dim.")]
    [Range(0.01f, 1f)] public float dimBelowBattery = 0.3f;
    [Tooltip("Beam brightness with the battery almost flat, as a fraction of full.")]
    [Range(0f, 1f)] public float levelWhenFlat = 0.45f;

    // 0 = torch off, 1 = on at full strength.
    public float Level { get; private set; }

    static readonly int PositionId = Shader.PropertyToID("_TorchPositionWS");
    static readonly int DirectionId = Shader.PropertyToID("_TorchDirectionWS");
    static readonly int ParamsId = Shader.PropertyToID("_TorchParams");
    static readonly int TintId = Shader.PropertyToID("_TorchTint");

    // The Time Lord reward: the torch's light, its beam and the dust it lights turn this colour.
    public static readonly Color Gold = new Color32(0xFF, 0xC8, 0x3D, 0xFF);
    // Gold carries less light than white at the same intensity (it has almost no blue), so each gold colour is
    // scaled up until it is as bright to the eye as the white it replaces. The light's intensity is not touched: the
    // battery flicker and the beam level are worked out from it.
    public static float Luminance(Color linear)
    {
        return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
    }

    // The gold for a Light whose normal colour is given (both are sRGB colours, as Light.color is).
    public static Color GoldLight(Color normal)
    {
        float gain = Luminance(normal.linear) / Mathf.Max(0.001f, Luminance(Gold.linear));
        // the light's colour is converted to linear (about a power of 2.2) after this multiply
        Color c = Gold * Mathf.Pow(gain, 1f / 2.2f);
        c.a = 1f;
        return c;
    }

    // The gold for a value shaders use as it is (a linear or HDR colour): the same brightness as the normal one.
    public static Color GoldLinear(Color normalLinear)
    {
        Color gold = Gold.linear;
        Color c = gold * (Luminance(normalLinear) / Mathf.Max(0.001f, Luminance(gold)));
        c.a = normalLinear.a;
        return c;
    }

    // True while the torch is gold (Time Lord unlocked and the Settings switch on).
    public bool Golden { get; private set; }
    Color normalColour = Color.white;
    bool colourRead;

    float fullIntensity;

    void Awake()
    {
        if (spot == null) spot = GetComponent<Light>();
        if (flashlight == null) flashlight = FindAnyObjectByType<FlashlightController>();
        if (spot != null)
        {
            fullIntensity = spot.intensity;
            normalColour = spot.color;
            colourRead = true;
        }
    }

    void LateUpdate()
    {
        if (spot == null) return;

        // the light keeps its intensity; only its colour changes
        bool golden = Achievements.GoldenBeamActive;
        if (golden != Golden && colourRead)
        {
            Golden = golden;
            spot.color = golden ? GoldLight(normalColour) : normalColour;
        }
        // a vector, not a colour: the value is already linear and goes above 1
        Shader.SetGlobalVector(TintId, Golden ? (Vector4)GoldLinear(Color.white) : Vector4.one);

        // the flashlight script dips the light's intensity to flicker, so intensity / full intensity follows it
        float level = spot.enabled && fullIntensity > 0f ? spot.intensity / fullIntensity : 0f;
        if (flashlight != null)
            level *= Mathf.Lerp(levelWhenFlat, 1f, Mathf.Clamp01(flashlight.GetBatteryPercent() / dimBelowBattery));
        Level = Mathf.Clamp01(level);

        Shader.SetGlobalVector(PositionId, transform.position);
        Shader.SetGlobalVector(DirectionId, transform.forward);
        Shader.SetGlobalVector(ParamsId, new Vector4(
            Mathf.Cos(spot.spotAngle * 0.5f * Mathf.Deg2Rad),
            Mathf.Cos(spot.innerSpotAngle * 0.5f * Mathf.Deg2Rad),
            spot.range,
            Level));
    }

    void OnDisable()
    {
        Level = 0f;
        Shader.SetGlobalVector(ParamsId, new Vector4(1f, 1f, 1f, 0f));
        Shader.SetGlobalVector(TintId, Vector4.one);
        if (Golden && spot != null) spot.color = normalColour;
        Golden = false;
    }
}
