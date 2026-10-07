using UnityEngine;

// Tells shaders where the torch beam is. Sits on FlashlightBeamPivot (the object with the torch's spot light).
// Every frame it sets three global shader properties, which TimeTrap/DustParticles reads to make motes sparkle
// inside the beam:
//   _TorchPositionWS    where the light is
//   _TorchDirectionWS   which way it points
//   _TorchParams        x = cos(outer half-angle), y = cos(inner half-angle), z = range, w = brightness 0-1
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

    float fullIntensity;

    void Awake()
    {
        if (spot == null) spot = GetComponent<Light>();
        if (flashlight == null) flashlight = FindAnyObjectByType<FlashlightController>();
        if (spot != null) fullIntensity = spot.intensity;
    }

    void LateUpdate()
    {
        if (spot == null) return;

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
    }
}
