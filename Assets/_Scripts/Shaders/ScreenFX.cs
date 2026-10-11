using UnityEngine;
using UnityEngine.Rendering.Universal;

// Drives the full-screen shader TimeTrap/TimeDistortionFullscreen. One object in the scene (Map/Gameplay/ScreenFX).
// Each state is a 0-1 number sent to the shader as a global property:
//   _Glitch   PlayerEvents.OnPlayerKilled and CheckpointManager.OnPlayerRespawned -> 1, fades over Glitch Time
//   _Pulse    ScreenFX.SetLowTime(0-1), which GameManager calls over the last minute; the beat speeds up with it
//   _Frost    PowerUps.OnZombieFreeze ... OnZombieFreezeEnded
//   _Flash    PowerUps.OnFlashPulse -> 1, fades over Flash Time
//   _Danger   how close the nearest chasing zombie is (within Danger Radius)
// While every state is 0 the renderer feature that draws the effect is switched off, so it costs nothing.
//
// Time: the glitch and the flash run on real (unscaled) time, so they always finish, even under an end screen.
// The other three hold still while the game is paused (timeScale 0) and fade out once the run has ended.
// Under the pause menu a faint glitch (Pause Glitch) keeps running on real time, so the frozen picture still shimmers.
[DefaultExecutionOrder(200)]
public class ScreenFX : MonoBehaviour
{
    [Tooltip("The Full Screen Pass Renderer Feature on PC_Renderer that draws the effect. Switched on only while an effect is showing.")]
    public ScriptableRendererFeature feature;

    [Header("Timing (seconds)")]
    public float glitchTime = 0.6f;
    [Tooltip("Strength of the glitch that runs behind the pause menu (0 = none).")]
    [Range(0f, 0.5f)] public float pauseGlitch = 0.1f;
    public float flashTime = 0.2f;
    [Tooltip("How quickly frost, danger and the low-time pulse follow their targets.")]
    public float fadeTime = 0.4f;
    [Tooltip("How quickly everything clears once the run has ended.")]
    public float clearTime = 0.3f;

    [Header("Low time")]
    [Tooltip("Heartbeats per second when the pulse starts (x) and when time is about to run out (y).")]
    public Vector2 beatsPerSecond = new Vector2(1.1f, 3.2f);

    [Header("Danger")]
    [Tooltip("A chasing zombie closer than this starts the danger effect (metres).")]
    public float dangerRadius = 8f;

    // Current values (0-1), for other scripts and tests.
    public float Glitch { get; private set; }
    public float Pulse { get; private set; }
    public float Frost { get; private set; }
    public float Flash { get; private set; }
    public float Danger { get; private set; }
    // True while the renderer feature is drawing.
    public bool IsDrawing { get; private set; }

    public static ScreenFX Instance { get; private set; }

    static readonly int GlitchId = Shader.PropertyToID("_Glitch");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int FrostId = Shader.PropertyToID("_Frost");
    static readonly int FlashId = Shader.PropertyToID("_Flash");
    static readonly int DangerId = Shader.PropertyToID("_Danger");
    static readonly int PulsePhaseId = Shader.PropertyToID("_PulsePhase");
    static readonly int ClockId = Shader.PropertyToID("_ScreenFxClock");

    // what GameManager last asked for (static, so it can be set before or without an instance)
    static float lowTime;

    bool frozen;
    float glitchFade;
    float pauseLevel;
    float pulsePhase;
    float gameClock;
    float realClock;
    Transform player;

    // Low-time pulse strength: 0 = off, 1 = time almost up. GameManager finds this by name and calls it every
    // frame over the last Low Time Seconds.
    public static void SetLowTime(float value)
    {
        lowTime = Mathf.Clamp01(value);
    }

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this on Restart.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        lowTime = 0f;
        Instance = null;
        ClearGlobals();
    }

    static void ClearGlobals()
    {
        Shader.SetGlobalFloat(GlitchId, 0f);
        Shader.SetGlobalFloat(PulseId, 0f);
        Shader.SetGlobalFloat(FrostId, 0f);
        Shader.SetGlobalFloat(FlashId, 0f);
        Shader.SetGlobalFloat(DangerId, 0f);
    }

    void OnEnable()
    {
        Instance = this;
        PlayerEvents.OnPlayerKilled += HandlePlayerKilled;
        CheckpointManager.OnPlayerRespawned += HandleRespawned;
        PowerUps.OnZombieFreeze += HandleFreeze;
        PowerUps.OnZombieFreezeEnded += HandleFreezeEnded;
        PowerUps.OnFlashPulse += HandleFlashPulse;
        SetDrawing(false);
    }

    void OnDisable()
    {
        PlayerEvents.OnPlayerKilled -= HandlePlayerKilled;
        CheckpointManager.OnPlayerRespawned -= HandleRespawned;
        PowerUps.OnZombieFreeze -= HandleFreeze;
        PowerUps.OnZombieFreezeEnded -= HandleFreezeEnded;
        PowerUps.OnFlashPulse -= HandleFlashPulse;

        Glitch = Pulse = Frost = Flash = Danger = 0f;
        pauseLevel = 0f;
        frozen = false;
        ClearGlobals();
        SetDrawing(false);
        if (Instance == this) Instance = null;
    }

    void HandlePlayerKilled(string cause) { TriggerGlitch(); }
    void HandleRespawned(Vector3 position) { TriggerGlitch(); }
    void HandleFreeze(float duration) { frozen = true; }
    void HandleFreezeEnded() { frozen = false; }
    void HandleFlashPulse(Vector3 position, float duration) { TriggerFlash(); }

    public void TriggerGlitch() { TriggerGlitch(glitchTime); }

    // A glitch that fades over a given time instead of Glitch Time (the main menu's short ones).
    public void TriggerGlitch(float seconds)
    {
        Glitch = 1f;
        glitchFade = Mathf.Max(0.01f, seconds);
    }
    public void TriggerFlash() { Flash = 1f; }

    void Update()
    {
        float realDelta = Time.unscaledDeltaTime;
        float gameDelta = Time.deltaTime;
        bool paused = Time.timeScale == 0f;
        bool runEnded = GameManager.IsGameOver;

        // one-shot effects: always play out, on real time
        realClock += realDelta;
        Glitch = Mathf.MoveTowards(Glitch, 0f, realDelta / Mathf.Max(0.01f, glitchFade > 0f ? glitchFade : glitchTime));
        bool pauseMenu = GameManager.Instance != null && GameManager.Instance.State == GameState.Paused;
        pauseLevel = Mathf.MoveTowards(pauseLevel, pauseMenu ? pauseGlitch : 0f, realDelta / 0.3f);
        Flash = Mathf.MoveTowards(Flash, 0f, realDelta / Mathf.Max(0.01f, flashTime));

        if (runEnded)
        {
            // end screen: nothing may stay on
            float step = realDelta / Mathf.Max(0.01f, clearTime);
            Pulse = Mathf.MoveTowards(Pulse, 0f, step);
            Frost = Mathf.MoveTowards(Frost, 0f, step);
            Danger = Mathf.MoveTowards(Danger, 0f, step);
            frozen = false;
        }
        else if (!paused)
        {
            // the freeze flag in PowerUps is the truth; the events only make it start on the right frame
            if (frozen && !PowerUps.ZombiesFrozen) frozen = false;

            float step = gameDelta / Mathf.Max(0.01f, fadeTime);
            Pulse = Mathf.MoveTowards(Pulse, lowTime, step);
            Frost = Mathf.MoveTowards(Frost, frozen ? 1f : 0f, step);
            Danger = Mathf.MoveTowards(Danger, DangerTarget(), step);

            gameClock += gameDelta;
            // the heartbeat: faster as Pulse rises
            pulsePhase += gameDelta * Mathf.PI * 2f * Mathf.Lerp(beatsPerSecond.x, beatsPerSecond.y, Pulse);
            if (pulsePhase > Mathf.PI * 200f) pulsePhase -= Mathf.PI * 200f;
        }
        // paused: Pulse, Frost and Danger keep their values and the clocks stand still, so the picture is frozen

        // F9 (ShaderDevToggle) shows the game without custom shaders: the full-screen effect is off too
        bool show = !ShaderDevToggle.Plain;
        float glitch = Mathf.Max(Glitch, pauseLevel);
        Shader.SetGlobalFloat(GlitchId, show ? glitch : 0f);
        Shader.SetGlobalFloat(PulseId, show ? Pulse : 0f);
        Shader.SetGlobalFloat(FrostId, show ? Frost : 0f);
        // ease the flash out: fast at first, then a soft tail
        Shader.SetGlobalFloat(FlashId, show ? Flash * Flash : 0f);
        Shader.SetGlobalFloat(DangerId, show ? Danger : 0f);
        Shader.SetGlobalFloat(PulsePhaseId, pulsePhase);
        Shader.SetGlobalVector(ClockId, new Vector4(gameClock, realClock, 0f, 0f));

        SetDrawing(show && (glitch > 0.001f || Pulse > 0.001f || Frost > 0.001f || Flash > 0.001f || Danger > 0.001f));
    }

    // 0 with no chasing zombie inside the radius, rising to 1 as the nearest one reaches the player.
    float DangerTarget()
    {
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found == null) return 0f;
            player = found.transform;
        }

        float nearest = dangerRadius;
        foreach (var zombie in ZombieAI.All)
        {
            if (zombie == null) continue;
            var state = zombie.CurrentState;
            if (state != ZombieAI.State.Chase && state != ZombieAI.State.Attack) continue;
            float distance = Vector3.Distance(zombie.transform.position, player.position);
            if (distance < nearest) nearest = distance;
        }
        return dangerRadius > 0f ? 1f - nearest / dangerRadius : 0f;
    }

    // Switches the renderer feature on or off. Off means URP skips the pass completely.
    void SetDrawing(bool drawing)
    {
        IsDrawing = drawing;
        if (feature != null && feature.isActive != drawing) feature.SetActive(drawing);
    }
}
