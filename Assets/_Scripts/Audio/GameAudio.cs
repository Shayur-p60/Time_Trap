using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// Every sound that is not a zombie's or the player's own: the world (generator, doors, elevator, data download,
// portal, chests), the ambience layers, the tension sounds (heartbeat, clock tick) and the stings. One per scene, on
// Map/Gameplay/GameAudio. It also owns the pool of sources that PlayerAudio plays through.
//
// It listens to the game's events and watches a few values, so the gameplay scripts need almost no audio code:
//   PowerState.OnPowerChanged        clunk at the generator, then the generator loop there and the Stage 2 ambience
//   GeneratorConsole.IsStarting      the power-up surge while the button's delay runs
//   Door.OnDoorMoved                 hiss at the doorway (every door, the elevator's included)
//   ElevatorTeleport.IsRiding        motor during the ride, ding at the car the player arrives in
//   DataDownload.NoisePulses         a beep at the processor on every noise pulse, and a loop while downloading
//   EscapePortal.IsOpen              the portal hum, at the portal
//   ScreenFX.Danger                  heartbeat volume and rate
//   GameManager.LowTime01            clock tick under Low Time Seconds, speeding up
//   GameProgress.OnObjectiveCompleted  a "collected" sound with the HUD banner (power, pathogen, data)
//   PlayerEvents.OnPlayerKilled      catch sting (with the glitch effect)
//   GameManager.OnRunEnded           ambience, heartbeat and tick fade out; the end screen calls EndSting with its title
//   GameAudio.VoiceActive            while a voice line is being spoken (the intro's Dr Birkin), the ambience layers here
//                                    and the music (MusicManager) come down by Duck Decibels, and return when it stops
//
// Pause: AudioListener.pause holds every source. The UI group's sounds (stings, button clicks) ignore it. On an end
// screen the ambience and the heartbeat are let through the pause for a moment so they can fade out instead of cut.
// Restart: everything here lives in the scene, so a reload leaves nothing playing.
// A sound with no clip is silent and still counted in Played, so a clip can be dropped in later.
[DefaultExecutionOrder(-150)]
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Mixer groups (TimeTrapMixer)")]
    public AudioMixerGroup zombiesGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup ambienceGroup;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup uiGroup;
    [Tooltip("SFX/Heartbeat: extra gain and an EQ lift, so the low heartbeat is heard on small speakers. Empty = SFX.")]
    public AudioMixerGroup heartbeatGroup;
    [Tooltip("SFX/Boost: +10 dB for clips that are too quiet at full volume (the pickup sound). Empty = SFX.")]
    public AudioMixerGroup boostGroup;

    [Header("Generator (3D, at the generator console)")]
    public Sfx generatorSurge = new Sfx();
    public Sfx powerClunk = new Sfx();
    public SfxLoop generatorLoop = new SfxLoop();
    [Tooltip("Seconds the generator loop takes to come up after the clunk.")]
    public float generatorLoopFadeIn = 4f;

    [Header("Doors and elevator (3D)")]
    public Sfx doorOpen = new Sfx();
    public Sfx doorClose = new Sfx();
    [Tooltip("Loops during the ride, at the ceiling of the car the player is in.")]
    public SfxLoop elevatorMotor = new SfxLoop();
    public Sfx elevatorDing = new Sfx();

    [Header("Data download (3D, at the processor)")]
    public Sfx downloadBeep = new Sfx();
    public SfxLoop downloadLoop = new SfxLoop();

    [Header("Portal (3D, at the portal)")]
    public SfxLoop portalHum = new SfxLoop();
    public float portalMinDistance = 2.5f;
    public float portalMaxDistance = 30f;

    [Header("Pickups")]
    [Tooltip("2D.")]
    public Sfx pickup = new Sfx();
    [Tooltip("2D. An objective was completed (power, pathogen, data): plays with the HUD banner.")]
    public Sfx objectiveComplete = new Sfx();
    [Tooltip("3D, at the chest.")]
    public Sfx chestOpen = new Sfx();

    [Header("Ambience (2D loops)")]
    [Tooltip("Facility drone, from the start.")]
    public SfxLoop drone = new SfxLoop();
    [Tooltip("Electrical hum, from Stage 2.")]
    public SfxLoop stage2Hum = new SfxLoop();
    [Tooltip("Machinery bed, from Stage 2.")]
    public SfxLoop stage2Bed = new SfxLoop();
    public float ambienceFade = 2.5f;

    [Header("Tension (2D)")]
    public SfxLoop heartbeat = new SfxLoop();
    [Tooltip("The player's panting. PlayerAudio sets its level from the stamina.")]
    public SfxLoop panting = new SfxLoop();
    [Tooltip("SFX/Panting: make-up gain for the quiet breathing recording. Empty = SFX.")]
    public AudioMixerGroup pantingGroup;
    [Tooltip("Heartbeat pitch (and so its rate) with danger just starting (x) and at its worst (y).")]
    public Vector2 heartbeatPitch = new Vector2(1f, 1.6f);
    public Sfx clockTick = new Sfx();
    [Tooltip("Seconds between ticks when the low-time warning starts (x) and when time is about to run out (y).")]
    public Vector2 tickInterval = new Vector2(1f, 0.3f);

    [Header("Stings and UI (2D)")]
    public Sfx catchSting = new Sfx();
    public Sfx winSting = new Sfx();
    public Sfx loseSting = new Sfx();
    public Sfx uiClick = new Sfx();
    [Tooltip("Seconds the ambience, heartbeat and tick take to fade out on an end screen.")]
    public float endFade = 1.5f;

    [Header("Ducking under a voice line")]
    [Tooltip("How far the ambience and the music come down while a voice line is spoken (decibels).")]
    public float duckDecibels = 6f;
    [Tooltip("Seconds to come down, and to come back.")]
    public float duckAttack = 0.15f;
    public float duckRelease = 0.5f;

    [Header("3D defaults")]
    public float worldMinDistance = 2f;
    public float worldMaxDistance = 22f;
    [Tooltip("Sources shared by every one-shot.")]
    public int voiceCount = 14;

    // What has been played, by role, for the tests.
    public struct PlayInfo
    {
        public int count;
        public bool hadClip;
        public string clip;
        public string group;
        public float spatialBlend;
        public bool ignoresPause;
    }
    public static readonly Dictionary<string, PlayInfo> Played = new Dictionary<string, PlayInfo>();

    // For tests.
    public float HeartbeatLevel => heartbeatLayer != null ? heartbeatLayer.Level : 0f;
    public float HeartbeatPitch { get; private set; } = 1f;
    public int TickCount { get; private set; }
    public bool RunEnded => ended;
    public float DroneLevel => droneLayer != null ? droneLayer.Level : 0f;
    public float Stage2Level => humLayer != null ? humLayer.Level : 0f;
    public float PortalLevel => portalLayer != null ? portalLayer.Level : 0f;
    public float GeneratorLevel => generatorLayer != null ? generatorLayer.Level : 0f;
    // The panting loop's volume right now (0-1, before the mixer).
    public float PantingLevel => pantingLayer != null ? pantingLayer.Level : 0f;
    public AudioSource PantingSource => pantingLayer != null ? pantingLayer.Source : null;

    // A voice line is being spoken right now. Set every frame by whoever plays it (CutsceneDirector).
    public static bool VoiceActive;
    // What the ambience and the music are multiplied by: 1 normally, lower under a voice line.
    public static float DuckGain { get; private set; } = 1f;

    static Transform listener;

    // The AudioListener (the player camera).
    public static Transform Listener
    {
        get
        {
            if (listener == null)
            {
                var found = FindAnyObjectByType<AudioListener>();
                if (found != null) listener = found.transform;
            }
            return listener;
        }
    }

    readonly List<SfxVoice> voices = new List<SfxVoice>();
    readonly List<LoopLayer> layers = new List<LoopLayer>();
    LoopLayer droneLayer, humLayer, bedLayer, heartbeatLayer, generatorLayer, portalLayer, downloadLayer, motorLayer, pantingLayer;
    bool pantingOn;

    GameManager manager;
    GeneratorConsole generator;
    DataDownload download;
    EscapePortal portal;
    ElevatorTeleport[] elevators = new ElevatorTeleport[0];
    Door[] doors = new Door[0];

    bool powered;
    bool generatorStarting;
    int downloadPulses;
    bool riding;
    bool portalOpen;
    bool inDanger;
    ElevatorTeleport rideFrom;
    float tickTimer;
    bool ended;

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

        for (int i = 0; i < Mathf.Max(4, voiceCount); i++)
        {
            var go = new GameObject("Voice" + i);
            go.transform.SetParent(transform, false);
            voices.Add(new SfxVoice(go.AddComponent<AudioSource>()));
        }

        droneLayer = AddLayer("Drone", drone, ambienceGroup, false);
        humLayer = AddLayer("Stage2Hum", stage2Hum, ambienceGroup, false);
        bedLayer = AddLayer("Stage2Bed", stage2Bed, ambienceGroup, false);
        heartbeatLayer = AddLayer("Heartbeat", heartbeat, heartbeatGroup != null ? heartbeatGroup : sfxGroup, false);
        pantingLayer = AddLayer("Panting", panting, pantingGroup != null ? pantingGroup : sfxGroup, false);
        motorLayer = AddLayer("ElevatorMotor", elevatorMotor, sfxGroup, true, 1.5f, 14f);
        generatorLayer = AddLayer("GeneratorLoop", generatorLoop, sfxGroup, true);
        downloadLayer = AddLayer("DownloadLoop", downloadLoop, sfxGroup, true);
        portalLayer = AddLayer("PortalHum", portalHum, sfxGroup, true, portalMinDistance, portalMaxDistance);
    }

    LoopLayer AddLayer(string name, SfxLoop loop, AudioMixerGroup group, bool spatial, float min = 0f, float max = 0f)
    {
        var layer = new LoopLayer(name, transform, loop, group, spatial, min > 0f ? min : worldMinDistance, max > 0f ? max : worldMaxDistance);
        layers.Add(layer);
        return layer;
    }

    void OnEnable()
    {
        PowerState.OnPowerChanged += HandlePowerChanged;
        PlayerEvents.OnPlayerKilled += HandlePlayerKilled;
        GameProgress.OnObjectiveCompleted += HandleObjectiveCompleted;
    }

    void OnDisable()
    {
        PowerState.OnPowerChanged -= HandlePowerChanged;
        PlayerEvents.OnPlayerKilled -= HandlePlayerKilled;
        GameProgress.OnObjectiveCompleted -= HandleObjectiveCompleted;
        if (manager != null) manager.OnRunEnded -= HandleRunEnded;
        foreach (var door in doors)
            if (door != null) door.OnDoorMoved -= HandleDoorMoved;
        foreach (var voice in voices) voice.Stop();
        foreach (var layer in layers) layer.StopNow();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        manager = GameManager.Instance;
        if (manager != null) manager.OnRunEnded += HandleRunEnded;

        generator = FindAnyObjectByType<GeneratorConsole>();
        download = FindAnyObjectByType<DataDownload>();
        portal = FindAnyObjectByType<EscapePortal>();
        elevators = FindObjectsByType<ElevatorTeleport>(FindObjectsInactive.Exclude);
        doors = FindObjectsByType<Door>(FindObjectsInactive.Include);
        foreach (var door in doors) door.OnDoorMoved += HandleDoorMoved;

        if (generator != null) generatorLayer.MoveTo(generator.transform.position);
        if (download != null) downloadLayer.MoveTo(download.transform.position);
        if (portal != null) portalLayer.MoveTo(portal.transform.position + Vector3.up * 1.2f);

        Note("Ambience/Drone", droneLayer.Source, drone.clip);
        droneLayer.SetTarget(1f, ambienceFade);
        // a scene that starts with the power on (a Stage 2 preview): straight to the Stage 2 ambience, no clunk
        powered = PowerState.IsPowered;
        if (powered) SetStage2(true, ambienceFade);
    }

    void Update()
    {
        float realDelta = Time.unscaledDeltaTime;
        bool playing = !ended && (manager == null || manager.State == GameState.Playing);

        if (playing)
        {
            WatchGenerator();
            WatchDownload();
            WatchElevator();
            WatchPortal();
            UpdateHeartbeat();
            UpdateTick();
        }

        // under a voice line the ambience (here) and the music (MusicManager reads DuckGain) step back
        float ducked = Mathf.Pow(10f, -Mathf.Abs(duckDecibels) / 20f);
        float duckStep = (1f - ducked) * realDelta / Mathf.Max(0.01f, VoiceActive ? duckAttack : duckRelease);
        DuckGain = Mathf.MoveTowards(DuckGain, VoiceActive ? ducked : 1f, duckStep);
        droneLayer.gain = humLayer.gain = bedLayer.gain = DuckGain;

        foreach (var layer in layers) layer.Tick(realDelta);
        foreach (var voice in voices) voice.Tick();
    }

    // ------------------------------------------------------------------ world

    void HandlePowerChanged(bool on)
    {
        // raised on every SetPowered, even with the same value
        if (on == powered) return;
        powered = on;
        if (on && !ended) PlayAt("World/PowerClunk", powerClunk, GeneratorPosition);
        SetStage2(on, on ? generatorLoopFadeIn : 0.5f);
    }

    void SetStage2(bool on, float fade)
    {
        float level = on ? 1f : 0f;
        if (on)
        {
            Note("World/GeneratorLoop", generatorLayer.Source, generatorLoop.clip);
            Note("Ambience/Stage2Hum", humLayer.Source, stage2Hum.clip);
            Note("Ambience/Stage2Bed", bedLayer.Source, stage2Bed.clip);
        }
        generatorLayer.SetTarget(level, fade);
        humLayer.SetTarget(level, on ? ambienceFade : 0.5f);
        bedLayer.SetTarget(level, on ? ambienceFade : 0.5f);
    }

    Vector3 GeneratorPosition => generator != null ? generator.transform.position : transform.position;

    void WatchGenerator()
    {
        bool starting = generator != null && generator.IsStarting;
        if (starting && !generatorStarting) PlayAt("World/GeneratorSurge", generatorSurge, GeneratorPosition);
        generatorStarting = starting;
    }

    void WatchDownload()
    {
        if (download == null) return;
        int pulses = download.NoisePulses;
        if (pulses > downloadPulses) PlayAt("World/DownloadBeep", downloadBeep, download.transform.position);
        downloadPulses = pulses;
        downloadLayer.SetTarget(download.IsDownloading ? 1f : 0f, 0.15f);
    }

    void WatchPortal()
    {
        bool open = portal != null && portal.IsOpen;
        if (open == portalOpen) return;
        portalOpen = open;
        if (open) Note("World/PortalHum", portalLayer.Source, portalHum.clip);
        // the hum grows in with the portal itself
        portalLayer.SetTarget(open ? 1f : 0f, open ? Mathf.Max(0.1f, portal.activateTime) : 0.5f);
    }

    void WatchElevator()
    {
        bool now = ElevatorTeleport.IsRiding;
        if (now)
        {
            // the motor stays with the player: the car they leave from, then the one they arrive in
            ElevatorTeleport car = NearestElevator();
            if (car != null && car.carPivot != null) motorLayer.MoveTo(car.carPivot.position + Vector3.up * 2.2f);
        }
        if (now == riding) return;
        riding = now;
        if (now)
        {
            rideFrom = NearestElevator();
            Note("World/ElevatorMotor", motorLayer.Source, elevatorMotor.clip);
            motorLayer.SetTarget(1f, 0.3f);
            return;
        }

        motorLayer.SetTarget(0f, 0.4f);
        // the ride was called off (the player stepped out as the door closed): no arrival
        ElevatorTeleport arrived = NearestElevator();
        if (arrived != null && arrived != rideFrom && arrived.carPivot != null)
            PlayAt("World/ElevatorDing", elevatorDing, arrived.carPivot.position + Vector3.up * 2f);
    }

    ElevatorTeleport NearestElevator()
    {
        Transform ear = Listener;
        if (ear == null) return null;
        ElevatorTeleport best = null;
        float bestDistance = float.MaxValue;
        foreach (var car in elevators)
        {
            if (car == null || car.carPivot == null) continue;
            float distance = (car.carPivot.position - ear.position).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = car;
        }
        return best;
    }

    void HandleDoorMoved(Door door, bool opening)
    {
        if (ended || door == null) return;
        if (opening) PlayAt("World/DoorOpen", doorOpen, door.DoorwayPosition);
        else PlayAt("World/DoorClose", doorClose, door.DoorwayPosition);
    }

    // ------------------------------------------------------------------ tension

    void UpdateHeartbeat()
    {
        float danger = ScreenFX.Instance != null ? ScreenFX.Instance.Danger : 0f;
        if (danger > 0f && !inDanger) Note("Tension/Heartbeat", heartbeatLayer.Source, heartbeat.clip);
        inDanger = danger > 0f;
        heartbeatLayer.SetTarget(danger, 0.1f);
        HeartbeatPitch = Mathf.Lerp(heartbeatPitch.x, heartbeatPitch.y, danger);
        heartbeatLayer.SetPitch(HeartbeatPitch);
    }

    void UpdateTick()
    {
        if (manager == null || !manager.IsLowTime)
        {
            tickTimer = 0f;
            return;
        }

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f) return;
        tickTimer = Mathf.Lerp(tickInterval.x, tickInterval.y, manager.LowTime01);
        TickCount++;
        Play2D("Tension/ClockTick", clockTick, sfxGroup);
    }

    void HandlePlayerKilled(string cause)
    {
        // GameManager hears this first: if the death ended the run, the lose sting plays instead
        if (ended || GameManager.IsGameOver) return;
        Play2D("Sting/Catch", catchSting, sfxGroup);
    }

    void HandleObjectiveCompleted(Objective objective)
    {
        if (ended) return;
        Play2D("UI/ObjectiveComplete", objectiveComplete, sfxGroup);
    }

    void HandleRunEnded(RunStats stats)
    {
        ended = true;
        // let these through the paused listener so they fade instead of cutting off
        droneLayer.FadeOutThroughPause(endFade);
        humLayer.FadeOutThroughPause(endFade);
        bedLayer.FadeOutThroughPause(endFade);
        heartbeatLayer.FadeOutThroughPause(endFade);
        pantingLayer.SetTarget(0f, 0.1f);
        foreach (var voice in voices)
            if (voice.Active && !voice.source.ignoreListenerPause) voice.Stop();

    }

    // The win or lose sting. The end screen calls this as its title appears.
    public static void EndSting(bool won)
    {
        if (Instance == null) return;
        if (won) Instance.Play2D("Sting/Win", Instance.winSting, Instance.uiGroup, true);
        else Instance.Play2D("Sting/Lose", Instance.loseSting, Instance.uiGroup, true);
    }

    // ------------------------------------------------------------------ hooks for the gameplay scripts

    public static void Pickup()
    {
        if (Instance != null) Instance.Play2D("Player/Pickup", Instance.pickup, Instance.boostGroup != null ? Instance.boostGroup : Instance.sfxGroup);
    }

    public static void ChestOpen(Vector3 position)
    {
        if (Instance != null) Instance.PlayAt("World/ChestOpen", Instance.chestOpen, position);
    }

    // The player's panting: level is its volume (0-1), reached over fadeSeconds. PlayerAudio calls this every frame.
    public void SetPanting(float level, float fadeSeconds)
    {
        if (ended) level = 0f;
        bool on = level > 0f;
        if (on && !pantingOn) Note("Player/Panting", pantingLayer.Source, panting.clip);
        pantingOn = on;
        pantingLayer.SetTarget(level, fadeSeconds);
    }

    // Menu buttons: heard while the game is paused.
    public static void UiClick()
    {
        if (Instance != null) Instance.Play2D("UI/Click", Instance.uiClick, Instance.uiGroup, true);
    }

    // ------------------------------------------------------------------ playing

    public SfxVoice Play2D(string role, Sfx sfx, AudioMixerGroup group, bool ignorePause = false, float volumeScale = 1f, float pitchScale = 1f)
    {
        SfxVoice voice = FreeVoice();
        voice.source.ignoreListenerPause = ignorePause;
        Sfx3D.Apply2D(voice.source, group);
        voice.source.priority = 60;
        Note(role, voice.source, sfx);
        return voice.Play(sfx, volumeScale, pitchScale) ? voice : null;
    }

    public SfxVoice PlayAt(string role, Sfx sfx, Vector3 position, float volumeScale = 1f)
    {
        SfxVoice voice = FreeVoice();
        voice.source.ignoreListenerPause = false;
        voice.source.transform.position = position;
        Sfx3D.Apply(voice.source, worldMinDistance, worldMaxDistance, sfxGroup);
        voice.source.priority = 110;
        Note(role, voice.source, sfx);
        return voice.Play(sfx, volumeScale) ? voice : null;
    }

    // An idle voice, or the one that has been playing longest.
    SfxVoice FreeVoice()
    {
        SfxVoice oldest = voices[0];
        foreach (var voice in voices)
        {
            if (!voice.Active) return voice;
            if (voice.StartedAt < oldest.StartedAt) oldest = voice;
        }
        oldest.Stop();
        return oldest;
    }

    public static void Note(string role, AudioSource source, Sfx sfx)
    {
        Note(role, source, sfx != null ? sfx.clip : null);
    }

    public static void Note(string role, AudioSource source, AudioClip clip)
    {
        Played.TryGetValue(role, out PlayInfo info);
        info.count++;
        info.hadClip = clip != null;
        info.clip = clip != null ? clip.name : "";
        if (source != null)
        {
            info.group = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "";
            info.spatialBlend = source.spatialBlend;
            info.ignoresPause = source.ignoreListenerPause;
        }
        Played[role] = info;
    }

    // How many sources of this object are sounding right now (for the restart and pause tests).
    public int PlayingSourceCount()
    {
        int count = 0;
        foreach (var source in GetComponentsInChildren<AudioSource>())
            if (source.isPlaying) count++;
        return count;
    }

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this on Restart.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        Instance = null;
        listener = null;
        Played.Clear();
        VoiceActive = false;
        DuckGain = 1f;
    }

    // ------------------------------------------------------------------ loops

    // One looping sound with a level that fades towards a target. Runs on real time, so it can fade under a pause.
    class LoopLayer
    {
        readonly SfxLoop loop;
        readonly AudioSource[] sources;
        readonly bool sliced;
        float target;
        float rate = 1f;
        float pitch = 1f;
        int current;
        float resumeTime;

        public float Level { get; private set; }
        public AudioSource Source => sources[0];
        // Multiplied into the volume (ducking).
        public float gain = 1f;

        public LoopLayer(string name, Transform parent, SfxLoop loop, AudioMixerGroup group, bool spatial, float min, float max)
        {
            this.loop = loop;
            sliced = loop.clip != null && loop.length > 0f && loop.start + loop.length < loop.clip.length + 0.001f;
            sources = new AudioSource[sliced ? 2 : 1];
            for (int i = 0; i < sources.Length; i++)
            {
                var go = new GameObject(name + (i == 0 ? "" : "B"));
                go.transform.SetParent(parent, false);
                var source = go.AddComponent<AudioSource>();
                if (spatial) Sfx3D.Apply(source, min, max, group);
                else Sfx3D.Apply2D(source, group);
                source.clip = loop.clip;
                source.loop = !sliced;
                source.volume = 0f;
                source.priority = spatial ? 90 : 40;
                sources[i] = source;
            }
        }

        public void MoveTo(Vector3 position)
        {
            foreach (var source in sources) source.transform.position = position;
        }

        public void SetTarget(float level, float fadeSeconds)
        {
            target = Mathf.Clamp01(level);
            rate = fadeSeconds > 0f ? 1f / fadeSeconds : 1000f;
        }

        public void SetPitch(float value)
        {
            pitch = Mathf.Max(0.05f, value);
        }

        public void FadeOutThroughPause(float seconds)
        {
            foreach (var source in sources) source.ignoreListenerPause = true;
            SetTarget(0f, seconds);
        }

        public void StopNow()
        {
            target = 0f;
            Level = 0f;
            foreach (var source in sources)
                if (source != null) source.Stop();
        }

        public void Tick(float realDelta)
        {
            Level = Mathf.MoveTowards(Level, target, rate * realDelta);
            if (loop.clip == null) return;

            if (Level <= 0f && target <= 0f)
            {
                foreach (var source in sources)
                    if (source.isPlaying) source.Stop();
                return;
            }

            float volume = loop.volume * Level * gain;
            float clipPitch = (loop.pitch > 0f ? loop.pitch : 1f) * pitch;
            if (!sliced)
            {
                AudioSource source = sources[0];
                source.pitch = clipPitch;
                source.volume = volume;
                if (source.isPlaying) resumeTime = source.time;
                else if (!Held(source))
                {
                    // carry on from where it was (a source let through the paused listener to fade out)
                    source.Play();
                    if (resumeTime > 0f && resumeTime < loop.clip.length - 0.1f) source.time = resumeTime;
                }
                return;
            }

            // the looped part: when the pass that is playing reaches the overlap, the other source starts the next one
            float end = loop.start + loop.length;
            float overlap = Mathf.Clamp(loop.overlap, 0.01f, loop.length * 0.5f);
            AudioSource active = sources[current];
            if (!active.isPlaying && !Held(active)) StartPass(active);
            else if (active.isPlaying && active.time >= end - overlap)
            {
                current = 1 - current;
                StartPass(sources[current]);
            }

            foreach (var source in sources)
            {
                if (!source.isPlaying) continue;
                float time = source.time;
                if (time >= end || time < loop.start - 0.05f)
                {
                    source.Stop();
                    continue;
                }
                source.pitch = clipPitch;
                source.volume = volume * Mathf.Clamp01(Mathf.Min((time - loop.start) / overlap, (end - time) / overlap));
            }
        }

        void StartPass(AudioSource source)
        {
            source.Stop();
            source.volume = 0f;
            source.time = Mathf.Clamp(loop.start, 0f, loop.clip.length - 0.01f);
            source.Play();
        }

        // Not playing only because the listener is paused.
        static bool Held(AudioSource source)
        {
            return AudioListener.pause && !source.ignoreListenerPause;
        }
    }
}
