using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Generator room (UC_Bioreactors_1.0) alarm. Reacts to PowerState:
//   Not powered (Stage 1): the ceiling ring's emission is dimmed and 10 red lights (one per second ceiling fixture) flash
//                          sharply on/off about once per second with a short fade; the walkway floor strips pulse red together.
//   Powered (Stage 2):     everything goes back to exactly what was recorded: emission (property blocks cleared, so the shared
//                          material shows through unchanged) and every light's colour, intensity and enabled state (the red
//                          flash lights are off).
//
// Safety: materials are never edited. Emission is changed with a MaterialPropertyBlock on these two renderers only, so the other
// UC rooms that share the ring's material are not affected. The lights controlled here are untagged (no Light_Emergency or
// Light_Powered), so the Stage 1 / Stage 2 preview tag toggling does not fight this script.
//
// [ExecuteAlways] keeps it running in edit mode so the previews show in the Scene view (untick "Animate In Edit Mode" for a still
// frame). Set up with the menu Time Trap/Lighting/Setup Generator Room Alarm.
[ExecuteAlways]
[DisallowMultipleComponent]
public class GeneratorRoomAlarm : MonoBehaviour
{
    [Serializable]
    public class LightRecord
    {
        public Light light;
        public Color color;
        public float intensity;
        public bool enabled;
    }

    [Header("Alarm colour")]
    public Color alarmRed = new Color(1f, 25f / 255f, 10f / 255f, 1f);

    [Header("Ceiling flash")]
    [Tooltip("Flashes per second.")]
    [Min(0.05f)] public float flashSpeed = 1f;
    [Tooltip("Seconds taken by each fade edge of the flash (0 = hard on/off).")]
    [Min(0f)] public float flashFade = 0.08f;
    [Tooltip("The red lights, one per flashing ceiling fixture. Off = emission changes only.")]
    public bool useFixtureLights = true;
    public float flashLightIntensity = 30f;
    [Tooltip("Ceiling ring emission in Stage 1, as a fraction of its normal emission.")]
    [Range(0f, 1f)] public float ceilingDimFactor = 0.08f;

    [Header("Floor strips")]
    [Tooltip("Strip pulses per second (all strips together).")]
    [Min(0.05f)] public float stripPulseSpeed = 1f;
    [Tooltip("Emission strength of the strips at the top of the pulse (HDR multiplier on the red).")]
    public float stripEmission = 4f;
    [Tooltip("Strip glow at the bottom of the pulse, as a fraction of the top (0 = dark).")]
    [Range(0f, 1f)] public float stripMinGlow = 0.1f;

    [Header("Preview")]
    public bool animateInEditMode = true;

    [Header("Controlled fixtures (filled in by the setup menu)")]
    public Renderer ceilingRenderer;
    [Tooltip("Material slot of the ceiling renderer that carries the emission.")]
    public int ceilingMaterialIndex = 0;
    public Renderer stripRenderer;
    public int stripMaterialIndex = 0;
    public Light[] flashLights = new Light[0];
    public LightRecord[] lightRecords = new LightRecord[0];
    [Tooltip("Emission colour of the shared materials when recorded (for reference).")]
    public Color ceilingOriginalEmission = Color.white;
    public Color stripOriginalEmission = Color.white;

    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;
#if UNITY_EDITOR
    double lastEditorTick;
#endif

    // ------------------------------------------------------------------ lifecycle

    void OnEnable()
    {
        PowerState.OnPowerChanged += HandlePowerChanged;
#if UNITY_EDITOR
        EditorApplication.update += EditorTick;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved += OnSceneSaved;
#endif
        Refresh();
    }

    void OnDisable()
    {
        PowerState.OnPowerChanged -= HandlePowerChanged;
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
#endif
        RestoreOriginals();
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) Refresh();
    }

    void HandlePowerChanged(bool powered)
    {
        Refresh();
    }

    void Update()
    {
        // play mode: unscaled time so pausing logic later does not freeze the alarm
        if (Application.isPlaying && !PowerState.IsPowered) ApplyAlarmAt(Time.unscaledTime);
    }

#if UNITY_EDITOR
    void EditorTick()
    {
        if (this == null || Application.isPlaying || PowerState.IsPowered || !animateInEditMode) return;
        double now = EditorApplication.timeSinceStartup;
        if (now - lastEditorTick < 0.033) return;
        lastEditorTick = now;
        ApplyAlarmAt((float)now);
        SceneView.RepaintAll();
    }

    // Never save the scene in the middle of a flash: put every light back to its recorded state first.
    void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        if (scene == gameObject.scene) RestoreOriginals();
    }

    void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
    {
        if (scene == gameObject.scene) Refresh();
    }
#endif

    // Re-evaluates the current power state: restores everything when powered, otherwise shows the alarm.
    public void Refresh()
    {
        if (PowerState.IsPowered)
        {
            RestoreOriginals();
            return;
        }
        float t = Application.isPlaying ? Time.unscaledTime : 0.25f;
#if UNITY_EDITOR
        if (!Application.isPlaying && animateInEditMode) t = (float)EditorApplication.timeSinceStartup;
#endif
        ApplyAlarmAt(t);
    }

    // ------------------------------------------------------------------ alarm

    // Shows the Stage 1 alarm as it looks at the given time (seconds). Public so a render or test can freeze a moment.
    public void ApplyAlarmAt(float time)
    {
        // ceiling ring: original emission, dimmed
        SetEmission(ceilingRenderer, ceilingMaterialIndex, ceilingOriginalEmission * ceilingDimFactor);

        // red flash lights
        float flash = FlashLevel(time);
        foreach (var l in flashLights)
        {
            if (l == null) continue;
            l.color = alarmRed;
            l.intensity = flashLightIntensity * flash;
            l.enabled = useFixtureLights;
        }

        // floor strips: red, all together
        float pulse = 0.5f - 0.5f * Mathf.Cos(time * stripPulseSpeed * 2f * Mathf.PI);
        SetEmission(stripRenderer, stripMaterialIndex, alarmRed * (stripEmission * Mathf.Lerp(stripMinGlow, 1f, pulse)));
    }

    float FlashLevel(float time)
    {
        float period = 1f / Mathf.Max(0.05f, flashSpeed);
        float onTime = period * 0.5f;
        float p = Mathf.Repeat(time, period);
        float fade = Mathf.Min(flashFade, onTime * 0.9f);
        if (fade <= 0f) return p < onTime ? 1f : 0f;
        if (p < fade) return p / fade;                          // fade in
        if (p < onTime) return 1f;                              // on
        if (p < onTime + fade) return 1f - (p - onTime) / fade; // fade out
        return 0f;                                              // off
    }

    // ------------------------------------------------------------------ restore / record

    // Puts everything back: property blocks cleared (the shared materials show through unchanged) and every light
    // back to its recorded colour, intensity and enabled state.
    public void RestoreOriginals()
    {
        ClearEmission(ceilingRenderer, ceilingMaterialIndex);
        ClearEmission(stripRenderer, stripMaterialIndex);
        foreach (var r in lightRecords)
        {
            if (r == null || r.light == null) continue;
            r.light.color = r.color;
            r.light.intensity = r.intensity;
            r.light.enabled = r.enabled;
        }
    }

    // Records the original emission (read from the shared materials) and the state of every controlled light.
    // Call while nothing is in alarm state (the setup menu does).
    public void RecordOriginals()
    {
        ceilingOriginalEmission = ReadEmission(ceilingRenderer, ceilingMaterialIndex, ceilingOriginalEmission);
        stripOriginalEmission = ReadEmission(stripRenderer, stripMaterialIndex, stripOriginalEmission);
        var records = new System.Collections.Generic.List<LightRecord>();
        foreach (var l in flashLights)
            if (l != null) records.Add(new LightRecord { light = l, color = l.color, intensity = l.intensity, enabled = l.enabled });
        lightRecords = records.ToArray();
    }

    static Color ReadEmission(Renderer r, int index, Color fallback)
    {
        if (r == null) return fallback;
        var mats = r.sharedMaterials;
        if (index < 0 || index >= mats.Length || mats[index] == null || !mats[index].HasProperty(EmissionId)) return fallback;
        return mats[index].GetColor(EmissionId);
    }

    // ------------------------------------------------------------------ property blocks

    void SetEmission(Renderer r, int index, Color colour)
    {
        if (r == null || index < 0 || index >= r.sharedMaterials.Length) return;
        if (block == null) block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block, index);
        block.SetColor(EmissionId, colour);
        r.SetPropertyBlock(block, index);
    }

    static void ClearEmission(Renderer r, int index)
    {
        if (r == null || index < 0 || index >= r.sharedMaterials.Length) return;
        r.SetPropertyBlock(null, index);
    }
}
