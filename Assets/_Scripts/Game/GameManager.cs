// Dev hotkeys (editor and development builds only, for recording demo clips):
//   F5  complete the next objective (power, pathogen, data, then the escape)
//   F6  set the timer to 0:30
//   F7  simulate a zombie catch
//   F8  pause / resume the timer

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public enum GameState { Playing, Paused, Won, Lost }

// Runs the game: the countdown, the lives, winning, losing, pausing and restarting. One per scene, on
// Map/Gameplay/GameManager. It builds its own HUD and menus (GameManagerUI), so it needs no other scene setup.
//
//   Timer   counts down from Start Seconds in game time. It stops while paused or on an end screen (timeScale 0) and
//           keeps running through respawn fades and elevator rides. At 0:00 the run is lost: Portal Failure.
//   Lives   a zombie catch (PlayerEvents.OnPlayerKilled "Zombie", raised by PlayerCatch) costs one life and Catch
//           Penalty Seconds, then PlayerCatch's own checkpoint respawn runs. Any other death (the Bioreactors pit,
//           out of bounds) costs Fall Penalty Seconds and, while Fall Costs Life is on, a life. Losing the last life
//           ends the run with no respawn: Infection if a zombie took it, Lost in the Dark if a fall did. A penalty
//           that empties the clock is Portal Failure.
//   Win     GameProgress.OnPlayerEscaped.
//   Restart reloads the scene. Statics survive a reload, so ResetAllStatics runs first.
[DefaultExecutionOrder(-200)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Timer")]
    [Tooltip("Seconds on the clock at the start of a run.")]
    public float startSeconds = 300f;
    [Tooltip("Below this many seconds the timer turns red and pulses, and ScreenFX.SetLowTime (if there is one) ramps from 0 to 1.")]
    public float lowTimeSeconds = 60f;

    [Header("Lives")]
    public int startLives = 3;
    [Tooltip("Seconds a zombie catch takes off the clock.")]
    public float catchPenaltySeconds = 10f;
    [Tooltip("Seconds a fall (or any death that is not a catch) takes off the clock.")]
    public float fallPenaltySeconds = 10f;
    [Tooltip("On: a fall costs a life as well as the time. Off: time only.")]
    public bool fallCostsLife = true;

    [Header("Screens")]
    public KeyCode pauseKey = KeyCode.Escape;
    public string winTitle = "CONTAINMENT SUCCESSFUL";
    public string winText = "The outbreak never left the facility.";
    public string portalFailureTitle = "PORTAL FAILURE";
    public string portalFailureText = "The portal collapsed. History repeats itself.";
    public string infectionTitle = "INFECTED";
    public string infectionText = "You became what you were sent to stop.";
    [Tooltip("The last life was lost to a fall.")]
    public string fallTitle = "LOST IN THE DARK";
    public string fallText = "Your timeline ends here.";

    [Header("Look (matches GameHUD)")]
    [Tooltip("Optional. Leave empty to use the TextMeshPro default font.")]
    public TMPro.TMP_FontAsset font;
    public Color panelColour = new Color(0f, 0f, 0f, 0.7f);
    public Color textColour = Color.white;
    public Color lowTimeColour = new Color(1f, 0.22f, 0.18f, 1f);
    public Color lifeColour = new Color(0.92f, 0.2f, 0.2f, 1f);
    public Color lifeLostColour = new Color(0.3f, 0.34f, 0.3f, 0.55f);
    public Color winColour = new Color(0.45f, 0.9f, 0.5f, 1f);
    public Color lossColour = new Color(1f, 0.22f, 0.18f, 1f);

    public GameState State { get; private set; } = GameState.Playing;
    public float TimeLeft { get; private set; }
    public int Lives { get; private set; }
    // The countdown is held (dev hotkey F8). The game itself carries on.
    public bool TimerPaused { get; set; }
    public RunStats Stats { get; private set; } = new RunStats();
    public GameManagerUI UI { get; private set; }

    // Whole seconds shown on the HUD: 0 only when the clock is really empty.
    public int DisplaySeconds => Mathf.Max(0, Mathf.CeilToInt(TimeLeft));
    public bool IsLowTime => DisplaySeconds < lowTimeSeconds;
    // 0 with more than Low Time Seconds left, rising to 1 at 0:00.
    public float LowTime01 => lowTimeSeconds > 0f ? Mathf.Clamp01(1f - TimeLeft / lowTimeSeconds) : 0f;

    public event Action<GameState> OnStateChanged;
    // Won or lost: the final numbers.
    public event Action<RunStats> OnRunEnded;

    // The run is over (win or loss).
    public static bool IsGameOver => Instance != null && (Instance.State == GameState.Won || Instance.State == GameState.Lost);
    // Player input is off: paused, on an end screen, or the frame the game was resumed in (so the Esc or the click
    // that resumed it does nothing else). False in a scene without a game manager.
    public static bool InputBlocked => Instance != null && (Instance.State != GameState.Playing || Time.frameCount <= Instance.resumeFrame);

    int resumeFrame = -1;
    bool restartRequested;
    readonly List<KeyValuePair<Pistol, Action>> pistolHooks = new List<KeyValuePair<Pistol, Action>>();
    Action<float> screenFxLowTime;
    bool screenFxSearched;
    float sentLowTime = -1f;

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false; // no OnEnable for the duplicate
            Destroy(this);
            return;
        }
        Instance = this;

        Time.timeScale = 1f;
        AudioListener.pause = false;
        TimeLeft = startSeconds;
        Lives = startLives;
        Stats = new RunStats { timeRemaining = TimeLeft, livesLeft = Lives };

        EnsureEventSystem();

        var go = new GameObject("GameManagerUI");
        go.transform.SetParent(transform, false);
        UI = go.AddComponent<GameManagerUI>();
        UI.Build(this);
    }

    // The menu buttons need exactly one EventSystem. It lives in the scene (not DontDestroyOnLoad), so a restart
    // brings back a fresh one with the rest of the scene. A missing one is created; extras are removed.
    void EnsureEventSystem()
    {
        var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
        for (int i = 1; i < systems.Length; i++)
        {
            Debug.LogWarning("[GameManager] More than one EventSystem in the scene: removing '" + systems[i].name + "'.", systems[i]);
            Destroy(systems[i].gameObject);
        }
        if (systems.Length > 0) return;

        Debug.LogWarning("[GameManager] No EventSystem in the scene: creating one for the menu buttons.");
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    void OnEnable()
    {
        PlayerEvents.OnPlayerKilled += HandlePlayerKilled;
        GameProgress.OnPlayerEscaped += HandlePlayerEscaped;
        ZombieAI.OnAnyDeathStarted += HandleZombieDied;
    }

    void OnDisable()
    {
        PlayerEvents.OnPlayerKilled -= HandlePlayerKilled;
        GameProgress.OnPlayerEscaped -= HandlePlayerEscaped;
        ZombieAI.OnAnyDeathStarted -= HandleZombieDied;
    }

    void Start()
    {
        // the pistol sits inactive on the player until it is picked up
        foreach (var pistol in FindObjectsByType<Pistol>(FindObjectsInactive.Include))
        {
            Pistol p = pistol;
            Action hook = () => HandleShotFired(p);
            p.OnFired += hook;
            pistolHooks.Add(new KeyValuePair<Pistol, Action>(p, hook));
        }
        RefreshHud();
    }

    void OnDestroy()
    {
        foreach (var pair in pistolHooks)
            if (pair.Key != null) pair.Key.OnFired -= pair.Value;
        pistolHooks.Clear();

        if (Instance != this) return;
        Instance = null;
        // never leave the editor (or the next scene) frozen and silent
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(pauseKey))
        {
            if (State == GameState.Playing) Pause();
            else if (State == GameState.Paused) Resume();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (State == GameState.Playing)
        {
            if (Input.GetKeyDown(KeyCode.F5)) CompleteNextObjective();
            if (Input.GetKeyDown(KeyCode.F6)) SetTimeLeft(30f);
            if (Input.GetKeyDown(KeyCode.F7) && !SimulateCatch()) GameHUD.Message("Catch ignored: respawning or invulnerable");
            if (Input.GetKeyDown(KeyCode.F8))
            {
                TimerPaused = !TimerPaused;
                GameHUD.Message(TimerPaused ? "Timer paused" : "Timer running");
            }
        }
#endif

        if (State == GameState.Playing)
        {
            float dt = Time.deltaTime;
            Stats.timeSurvived += dt;
            if (!TimerPaused)
            {
                TimeLeft -= dt;
                if (TimeLeft <= 0f) EndRun(RunOutcome.PortalFailure);
            }
        }

        RefreshHud();
    }

    // ------------------------------------------------------------------ deaths, win, loss

    void HandlePlayerKilled(string cause)
    {
        if (State != GameState.Playing) return;

        bool caught = cause == "Zombie";
        if (caught) Stats.catches++;
        else Stats.falls++;

        bool costsLife = caught || fallCostsLife;
        float penalty = Mathf.Max(0f, caught ? catchPenaltySeconds : fallPenaltySeconds);

        if (costsLife) Lives = Mathf.Max(0, Lives - 1);
        if (penalty > 0f)
        {
            TimeLeft -= penalty;
            UI.ShowPenalty("-" + Mathf.RoundToInt(penalty) + "s");
        }

        // the last life goes before the clock, and the screen depends on what took it
        if (costsLife && Lives <= 0) EndRun(caught ? RunOutcome.Infection : RunOutcome.Fall);
        else if (TimeLeft <= 0f) EndRun(RunOutcome.PortalFailure);
        // otherwise the respawn is already on its way: PlayerCatch (a catch) and DeathZone (a fall) start it
    }

    void HandlePlayerEscaped()
    {
        if (State == GameState.Playing || State == GameState.Paused) EndRun(RunOutcome.Won);
    }

    void HandleZombieDied(ZombieAI zombie)
    {
        if (State == GameState.Playing) Stats.zombiesKilled++;
    }

    void HandleShotFired(Pistol pistol)
    {
        Stats.shotsFired++;
        if (!pistol.LastShotHit) return;
        Collider hit = pistol.LastHit.collider;
        if (hit != null && hit.GetComponentInParent<IDamageable>() != null) Stats.shotsHit++;
    }

    void EndRun(RunOutcome outcome)
    {
        if (IsGameOver) return;
        TimeLeft = Mathf.Max(0f, TimeLeft);
        // a catch that ends the run does not send the player back to a checkpoint
        PlayerCatch.respawnOnCatch = false;

        UpdateStats();
        Stats.outcome = outcome;
        Stats.escaped = outcome == RunOutcome.Won;

        Freeze();
        SetState(outcome == RunOutcome.Won ? GameState.Won : GameState.Lost);
        RefreshHud();

        switch (outcome)
        {
            case RunOutcome.Won: UI.ShowEnd(winTitle, winText, winColour, Stats); break;
            case RunOutcome.Infection: UI.ShowEnd(infectionTitle, infectionText, lossColour, Stats); break;
            case RunOutcome.Fall: UI.ShowEnd(fallTitle, fallText, lossColour, Stats); break;
            default: UI.ShowEnd(portalFailureTitle, portalFailureText, lossColour, Stats); break;
        }
        Debug.Log("[GameManager] Run ended: " + outcome + ", " + FormatTime(Stats.timeRemaining) + " left, " + Stats.livesLeft + " lives.");
        OnRunEnded?.Invoke(Stats);
    }

    void UpdateStats()
    {
        Stats.timeRemaining = Mathf.Max(0f, TimeLeft);
        Stats.livesLeft = Lives;
        var progress = GameProgress.Instance;
        if (progress == null) return;
        Stats.objectivesCompleted = (progress.HasPower ? 1 : 0) + (progress.HasPathogen ? 1 : 0) + (progress.HasData ? 1 : 0);
    }

    // ------------------------------------------------------------------ pause

    public void Pause()
    {
        if (State != GameState.Playing) return;
        Freeze();
        SetState(GameState.Paused);
        UI.ShowPause(true);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        resumeFrame = Time.frameCount;
        SetState(GameState.Playing);
        UI.ShowPause(false);
    }

    // Stops the game where it is: zombies, physics, animation and every timer run on scaled time, and the player
    // scripts check InputBlocked.
    void Freeze()
    {
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void SetState(GameState state)
    {
        if (State == state) return;
        State = state;
        OnStateChanged?.Invoke(state);
    }

    // ------------------------------------------------------------------ restart, quit

    // Reloads the scene as a fresh run. The reload itself waits for LateUpdate: a Button calls this from inside the
    // EventSystem's update, and the scene must not be switched off under it.
    public void Restart()
    {
        restartRequested = true;
    }

    void LateUpdate()
    {
        if (restartRequested) RestartNow();
    }

    void RestartNow()
    {
        restartRequested = false;
        Scene scene = SceneManager.GetActiveScene();
        int buildIndex = scene.buildIndex;
        string path = scene.path;

        // switch the old scene off first: every OnDisable runs (listeners dropped, zombies off the list, coroutines
        // stopped) and nothing in it can touch a static again between the reset below and the unload
        foreach (var root in scene.GetRootGameObjects()) root.SetActive(false);
        ResetAllStatics();

        if (buildIndex >= 0)
        {
            SceneManager.LoadScene(buildIndex);
            return;
        }
#if UNITY_EDITOR
        // playing a scene that is not in the build list
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(System.IO.Path.GetFileNameWithoutExtension(path));
#endif
    }

    // Statics survive a scene reload, so each one is put back to what a fresh play session starts with. Every class
    // already has that code for entering Play mode (its RuntimeInitializeOnLoadMethod); this runs the same methods in
    // the same order: SubsystemRegistration first, then BeforeSceneLoad, which hooks the static listeners up again.
    public static void ResetAllStatics()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Instance = null;

        // SubsystemRegistration
        PlayerEvents.ResetForNewPlaySession();      // OnPlayerKilled
        PowerState.ResetForNewPlaySession();        // IsPowered = false, OnPowerChanged
        GameProgress.ResetForNewPlaySession();      // Instance, OnStageChanged, OnObjectiveCompleted, OnPlayerEscaped, OnProgressReset
        CheckpointManager.ResetForNewPlaySession(); // OnPlayerRespawned
        NoiseSystem.ResetForNewPlaySession();       // OnNoise, OnNoiseDetailed
        PowerUps.ResetForNewPlaySession();          // OnFlashPulse, OnZombieFreeze, OnZombieFreezeEnded, FreezeEndTime (the zombie-freeze flag), CompassTargets
        ZombieAI.ResetForNewPlaySession();          // All, OnAnyDeathStarted, sceneDoors
        ElevatorTeleport.ResetForNewPlaySession();  // riding
        ScreenFX.ResetForNewPlaySession();          // low-time value, Instance, and the full-screen shader's global properties
        PickupHighlight.ResetForNewPlaySession();   // SharedMaterial (PickupHighlightConfig sets it again on load)

        // BeforeSceneLoad
        PlayerCatch.StartPlaySession();             // OnPlayerCaught, CatchCount, InvulnerableUntil, pending, player, respawnOnCatch; listens to OnPlayerRespawned again
        ZombieHearing.StartPlaySession();           // maxResponders, NoisesHeard, LastResponderCount; listens to OnNoiseDetailed again
    }

    // How many listeners each static event has, for checking that nothing doubles up after a restart.
    // (PowerUps.OnFlashPulse has one listener per live zombie, so that number moves with the zombie count.)
    public static string StaticListenerReport()
    {
        return "OnPlayerKilled=" + PlayerEvents.KilledListenerCount
            + " OnPlayerCaught=" + PlayerCatch.CaughtListenerCount
            + " OnPlayerRespawned=" + CheckpointManager.RespawnedListenerCount
            + " OnPowerChanged=" + PowerState.PowerChangedListenerCount
            + " OnStageChanged=" + GameProgress.StageChangedListenerCount
            + " OnObjectiveCompleted=" + GameProgress.ObjectiveCompletedListenerCount
            + " OnPlayerEscaped=" + GameProgress.PlayerEscapedListenerCount
            + " OnProgressReset=" + GameProgress.ProgressResetListenerCount
            + " OnNoise=" + NoiseSystem.NoiseListenerCount
            + " OnNoiseDetailed=" + NoiseSystem.NoiseDetailedListenerCount
            + " OnFlashPulse=" + PowerUps.FlashPulseListenerCount
            + " OnZombieFreeze=" + PowerUps.ZombieFreezeListenerCount
            + " OnZombieFreezeEnded=" + PowerUps.ZombieFreezeEndedListenerCount
            + " OnAnyDeathStarted=" + ZombieAI.AnyDeathStartedListenerCount;
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ dev tools (also used by tests)

    public void SetTimeLeft(float seconds)
    {
        if (State != GameState.Playing) return;
        TimeLeft = Mathf.Max(0f, seconds);
        if (TimeLeft <= 0f) EndRun(RunOutcome.PortalFailure);
    }

    // A catch through the real gate (PlayerCatch). False while the player is respawning or still invulnerable.
    public bool SimulateCatch()
    {
        return State == GameState.Playing && PlayerCatch.TryCatch(null);
    }

    // Power, then the pathogen sample, then the data, then the escape. False when there is nothing left to do.
    public bool CompleteNextObjective()
    {
        var progress = GameProgress.Instance;
        if (progress == null || State != GameState.Playing) return false;

        if (!progress.HasPower) return progress.Complete(Objective.Power);

        if (!progress.HasPathogen)
        {
            // through the sample itself, so the vaccine case disappears
            var sample = FindAnyObjectByType<PathogenSample>();
            if (sample != null && !sample.Taken) sample.Interact();
            else progress.Complete(Objective.Pathogen);
            return true;
        }

        if (!progress.HasData)
        {
            // through the download itself, so the console shows it as done
            var download = FindAnyObjectByType<DataDownload>();
            if (download != null && download.CanInteract) download.Hold(download.downloadTime);
            if (!progress.HasData) progress.Complete(Objective.Data);
            return true;
        }

        if (!progress.HasEscaped)
        {
            progress.RaisePlayerEscaped();
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ HUD

    void RefreshHud()
    {
        if (UI == null) return;
        UpdateStats();
        UI.SetTimer(FormatTime(DisplaySeconds), IsLowTime, TimerPaused);
        UI.SetLives(Lives, startLives);
        SendLowTime(IsGameOver ? 0f : LowTime01);
    }

    // m:ss
    public static string FormatTime(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.CeilToInt(seconds - 0.0001f));
        return (whole / 60) + ":" + (whole % 60).ToString("00");
    }

    // ScreenFX (shaders part B) is optional: it is looked up by name, so this compiles with or without it.
    // Either a static or an instance method "void SetLowTime(float)" is used.
    void SendLowTime(float value)
    {
        if (Mathf.Approximately(value, sentLowTime)) return;
        if (!screenFxSearched) FindScreenFx();
        if (screenFxLowTime == null) return;
        sentLowTime = value;
        screenFxLowTime(value);
    }

    void FindScreenFx()
    {
        screenFxSearched = true;
        Type type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType("ScreenFX");
            if (type != null) break;
        }
        if (type == null) return;

        MethodInfo method = type.GetMethod("SetLowTime", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, new[] { typeof(float) }, null);
        if (method == null)
        {
            Debug.LogWarning("[GameManager] ScreenFX has no public SetLowTime(float): the low-time effect is off.");
            return;
        }

        if (method.IsStatic)
        {
            screenFxLowTime = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), method);
            return;
        }
        foreach (UnityEngine.Object found in Resources.FindObjectsOfTypeAll(type))
        {
            // objects in the scene only, not prefabs
            var component = found as Component;
            if (component == null || !component.gameObject.scene.IsValid()) continue;
            screenFxLowTime = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), component, method);
            return;
        }
    }
}
