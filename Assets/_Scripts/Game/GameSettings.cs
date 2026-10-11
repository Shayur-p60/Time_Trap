using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// The player's settings: kept in PlayerPrefs, applied when the scene loads and again whenever one changes. One per
// scene, on Map/Gameplay/GameSettings. The Settings panel (GameMenus) reads and writes the values through the static
// Get / Set calls here; nothing else needs to know where they are stored.
//
//   Audio     Master, Music, SFX and Zombies drive the TimeTrapMixer's exposed volumes. A slider is a gain on top of
//             the group's balanced level in the mixer: 100% is that level, 0% is -80 dB.
//   Controls  mouse sensitivity (a multiplier on the player's own) and Invert Y.
//   Display   brightness (post-exposure on a global Volume this object creates), fullscreen, resolution, field of
//             view, head bob.
// It also holds the two things the pause menu does to picture and sound: the Paused mixer snapshot and the
// desaturation of the game behind the menu.
[DefaultExecutionOrder(-180)]
public class GameSettings : MonoBehaviour
{
    public const string Master = "master", Music = "music", Sfx = "sfx", Zombies = "zombies";
    public const string Sensitivity = "sensitivity", InvertY = "invertY";
    public const string Brightness = "brightness", Fullscreen = "fullscreen", ResolutionWidth = "resW", ResolutionHeight = "resH";
    public const string Fov = "fov", HeadBobOn = "headBob";
    const string Prefix = "tt.set.";

    public const float MinSensitivity = 0.1f, MaxSensitivity = 3f;
    public const float MinFov = 60f, MaxFov = 90f;

    public static GameSettings Instance { get; private set; }
    // Settings -> Reset progress. Part B's achievements and records clear themselves from this.
    public static event Action OnResetProgress;

    [Tooltip("TimeTrapMixer. Its MasterVolume, MusicVolume, SFXVolume and ZombiesVolume parameters are driven from here.")]
    public AudioMixer mixer;
    [Tooltip("Snapshot names in the mixer: the normal mix, and the pause menu's (music low and muffled).")]
    public string normalSnapshot = "Snapshot";
    public string pausedSnapshot = "Paused";
    public float snapshotTime = 0.25f;

    [Header("Display")]
    [Tooltip("Stops of exposure the brightness slider adds at 100% (and takes away at 0%).")]
    public float brightnessRange = 1.2f;
    [Tooltip("Colour saturation of the game behind the pause menu (-100 = grey).")]
    public float pausedSaturation = -75f;
    public float pauseBlendTime = 0.3f;

    // The field of view the player camera has in the scene: the default of the FOV slider.
    public static float DefaultFov { get; private set; } = 60f;
    // 0 = playing, 1 = fully paused.
    public float PauseBlend { get; private set; }

    Camera playerCamera;
    PlayerMovementController movement;
    HeadBob headBob;
    ColorAdjustments colour;
    VolumeProfile profile;
    float baseExposure;
    float baseSaturation;
    bool paused;

    // ------------------------------------------------------------------ values

    public static float Default(string key)
    {
        switch (key)
        {
            case Master: return 0.8f;
            case Music: return 0.7f;
            case Sfx: return 1f;
            case Zombies: return 1f;
            case Sensitivity: return 1f;
            case InvertY: return 0f;
            case Brightness: return 0.5f;
            case Fullscreen: return 1f;
            case Fov: return DefaultFov;
            case HeadBobOn: return 1f;
            default: return 0f;
        }
    }

    public static float Get(string key)
    {
        return PlayerPrefs.GetFloat(Prefix + key, Default(key));
    }

    public static bool IsOn(string key)
    {
        return Get(key) > 0.5f;
    }

    // Stores a value and applies it straight away.
    public static void Set(string key, float value)
    {
        PlayerPrefs.SetFloat(Prefix + key, value);
        PlayerPrefs.Save();
        if (Instance == null) return;
        if (key == Fullscreen || key == ResolutionWidth || key == ResolutionHeight) Instance.ApplyDisplayMode();
        else Instance.Apply();
    }

    // The saved resolution, or the one the game is running at when none has been chosen.
    public static Vector2Int Resolution
    {
        get
        {
            int width = Mathf.RoundToInt(PlayerPrefs.GetFloat(Prefix + ResolutionWidth, 0f));
            int height = Mathf.RoundToInt(PlayerPrefs.GetFloat(Prefix + ResolutionHeight, 0f));
            return width > 0 && height > 0 ? new Vector2Int(width, height) : new Vector2Int(Screen.width, Screen.height);
        }
    }

    public static void SetResolution(int width, int height)
    {
        PlayerPrefs.SetFloat(Prefix + ResolutionWidth, width);
        PlayerPrefs.SetFloat(Prefix + ResolutionHeight, height);
        PlayerPrefs.Save();
        if (Instance != null) Instance.ApplyDisplayMode();
    }

    // Everything back to its default. Progress (achievements, records) is not a setting and stays.
    public static void ResetToDefaults()
    {
        foreach (string key in new[] { Master, Music, Sfx, Zombies, Sensitivity, InvertY, Brightness, Fullscreen, ResolutionWidth, ResolutionHeight, Fov, HeadBobOn })
            PlayerPrefs.DeleteKey(Prefix + key);
        PlayerPrefs.Save();
        if (Instance == null) return;
        Instance.Apply();
        Instance.ApplyDisplayMode();
    }

    public static void ResetProgress()
    {
        Debug.Log("[GameSettings] Reset progress.");
        OnResetProgress?.Invoke();
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }
        Instance = this;

        movement = FindAnyObjectByType<PlayerMovementController>();
        if (movement != null)
        {
            playerCamera = movement.GetComponentInChildren<Camera>(true);
            headBob = movement.GetComponentInChildren<HeadBob>(true);
        }
        if (playerCamera != null) DefaultFov = playerCamera.fieldOfView;

        BuildVolume();
    }

    void Start()
    {
        // the mixer takes SetFloat from Start onwards (in Awake it is still loading its snapshot)
        SetSnapshot(false, 0f); // a scene reloaded from the pause menu
        Apply();
        if (PlayerPrefs.HasKey(Prefix + Fullscreen) || PlayerPrefs.HasKey(Prefix + ResolutionWidth)) ApplyDisplayMode();
    }

    void OnDestroy()
    {
        // leaving from the pause menu must not leave the mixer muffled
        if (paused) SetSnapshot(false, 0f);
        if (colour != null) Destroy(colour);
        if (profile != null) Destroy(profile);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        float target = paused ? 1f : 0f;
        if (Mathf.Approximately(PauseBlend, target)) return;
        PauseBlend = Mathf.MoveTowards(PauseBlend, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, pauseBlendTime));
        ApplyColour();
    }

    // A global Volume above the scene's own, overriding only exposure and saturation. The scene profile's values
    // are the starting point, so brightness 50% and no pause leave the picture exactly as it was.
    void BuildVolume()
    {
        float bestPriority = float.NegativeInfinity;
        foreach (var volume in FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
        {
            if (!volume.isGlobal || volume.sharedProfile == null || volume.priority < bestPriority) continue;
            if (!volume.sharedProfile.TryGet(out ColorAdjustments found) || !found.active) continue;
            bestPriority = volume.priority;
            baseExposure = found.postExposure.overrideState ? found.postExposure.value : 0f;
            baseSaturation = found.saturation.overrideState ? found.saturation.value : 0f;
        }

        var go = new GameObject("SettingsVolume");
        go.transform.SetParent(transform, false);
        var mine = go.AddComponent<Volume>();
        mine.isGlobal = true;
        mine.priority = 1000f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.hideFlags = HideFlags.HideAndDontSave;
        colour = profile.Add<ColorAdjustments>(false);
        colour.postExposure.overrideState = true;
        colour.saturation.overrideState = true;
        mine.sharedProfile = profile;
        ApplyColour();
    }

    // ------------------------------------------------------------------ applying

    public void Apply()
    {
        SetVolume("MasterVolume", Get(Master));
        SetVolume("MusicVolume", Get(Music));
        SetVolume("SFXVolume", Get(Sfx));
        SetVolume("ZombiesVolume", Get(Zombies));

        if (movement != null)
        {
            movement.SensitivityMultiplier = Mathf.Clamp(Get(Sensitivity), MinSensitivity, MaxSensitivity);
            movement.InvertY = IsOn(InvertY);
        }
        if (headBob != null) headBob.visualBob = IsOn(HeadBobOn);
        if (playerCamera != null) playerCamera.fieldOfView = Mathf.Clamp(Get(Fov), MinFov, MaxFov);
        ApplyColour();
    }

    // Stops of exposure the brightness setting adds right now.
    public float BrightnessStops => (Mathf.Clamp01(Get(Brightness)) - 0.5f) * 2f * brightnessRange;

    void ApplyColour()
    {
        if (colour == null) return;
        colour.postExposure.value = baseExposure + BrightnessStops;
        colour.saturation.value = Mathf.Lerp(baseSaturation, pausedSaturation, PauseBlend);
    }

    // 0-1 on a slider to decibels on top of the group's own level: 1 = no change, 0 = -80 dB (silent).
    public static float SliderToDecibels(float value)
    {
        return value <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(value));
    }

    // The group's balanced level is what the mixer gives the parameter before any script has set it. It is read
    // once per play session and kept: once SetFloat has been used the mixer only ever reports the script's value
    // (also straight after ClearFloat, and after a scene reload), so reading it again would stack gain on gain.
    static readonly Dictionary<string, float> balancedLevels = new Dictionary<string, float>();

    bool Balanced(string parameter, out float level)
    {
        if (balancedLevels.TryGetValue(parameter, out level)) return true;
        if (mixer == null || !mixer.GetFloat(parameter, out level)) return false;
        balancedLevels[parameter] = level;
        return true;
    }

    void SetVolume(string parameter, float slider)
    {
        if (!Balanced(parameter, out float balanced)) return;
        float gain = SliderToDecibels(slider);
        mixer.SetFloat(parameter, gain <= -80f ? -80f : Mathf.Max(-80f, balanced + gain));
    }

    // The level a volume parameter is at now, and the level the mixer itself gives it (for the tests).
    public bool ReadVolume(string parameter, out float current, out float balanced)
    {
        current = balanced = 0f;
        return mixer != null && Balanced(parameter, out balanced) && mixer.GetFloat(parameter, out current);
    }

    // A new play session: the mixer is back on its own levels. Not part of ResetAllStatics, which runs on every
    // scene reload, when the mixer still holds the levels set here.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ForgetBalancedLevels()
    {
        balancedLevels.Clear();
    }

    void ApplyDisplayMode()
    {
        Vector2Int size = Resolution;
        bool fullscreen = IsOn(Fullscreen);
        if (Screen.width == size.x && Screen.height == size.y && Screen.fullScreen == fullscreen) return;
        Screen.SetResolution(size.x, size.y, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    // ------------------------------------------------------------------ pause

    // The pause menu opened or closed: music low and muffled, the picture behind it drained of colour.
    public void SetPaused(bool on)
    {
        paused = on;
        SetSnapshot(on, snapshotTime);
    }

    void SetSnapshot(bool pausedMix, float seconds)
    {
        if (mixer == null) return;
        AudioMixerSnapshot snapshot = mixer.FindSnapshot(pausedMix ? pausedSnapshot : normalSnapshot);
        if (snapshot != null) snapshot.TransitionTo(seconds);
    }

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this. Listeners to OnResetProgress are
    // dropped; saved settings and saved progress are not touched.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        Instance = null;
        OnResetProgress = null;
    }
}
