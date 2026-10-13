using System.Collections;
using System.Collections.Generic;
using TimeTrap.UI;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// Runs the intro cutscene: one coroutine that plays the five beats in order. One per scene, on
// Map/Gameplay/CutsceneDirector (not under IntroCutscene, which is switched off when the arrival starts).
//
//   Play (GameMenus) -> Begin -> fade to black over the menu -> GameManager.BeginCutscene (state Cutscene)
//   1 Typewriter   the IntroScript's opening lines, typed on black.
//   2 Void         Dr Birkin materialises in the void and speaks. Each line waits for the player.
//   3 Chamber      a glitch, and the same conversation carries on in the portal chamber, in first person. The mouse
//                  looks around (CutsceneCameraRig). The portal powers up during the last line.
//   4 Walk         the camera walks up the ramp into the portal: glitch, white flash, whoosh.
//   5 Arrival      the facility. The player steps out of the arrival portal, it collapses behind them, the HUD fades
//                  in, and GameManager.StartGame starts the clock. IntroCutscene is switched off.
//
// What happens inside a line (the camera move, Birkin's animation, the portal powering up) is in Timeline assets
// named on the line in the IntroScript: the director starts them and waits for the player. Durations, speeds and
// positions are all fields here, on the IntroScript, or in the Timelines.
//
// Voice: a line with a voice clip plays it through the Voice mixer group, and its text types out over the spoken
// part of the clip (the silence at the end of the file is left out), so the last character lands as he stops talking
// and the [E] prompt appears then. Pressing early stops the clip and shows the whole line. While he speaks,
// GameAudio.VoiceActive is on: the ambience and the music step back. A line with no clip types at the script's speed
// with blips. The arrival's radio line plays through the Radio group, with a crackle before and after.
//
// Radio: the arrival's line and the Guide's lines during the run (GuideRadio) all go through QueueRadio, one at a
// time, in the order they were asked for. A line is held by the pause menu and cut by a death or the end of the run.
//
// Skip: holding Skip Key for Skip Hold seconds at any point of beats 1 to 4 stops everything and goes to the arrival.
// "Always skip intro" (Settings) goes from the black screen straight to the arrival. Play Again and Restart never
// reach this script: they start the run without the menu.
//
// While it runs the game is in GameState.Cutscene: no input, no HUD, no clock, no zombies, no pause, no achievements.
// Everything is timed on real (unscaled) time.
[DefaultExecutionOrder(-160)]
public class CutsceneDirector : MonoBehaviour
{
    public enum Beat { None, FadeOut, Typewriter, Void, Chamber, Walk, Arrival, Done }

    public static CutsceneDirector Instance { get; private set; }
    // True while the cutscene camera has the picture (the player's camera is off and the player is not drawn).
    public static bool OwnsPicture { get; private set; }

    [Header("Script")]
    public IntroScript script;

    [Header("Scene")]
    [Tooltip("The root of everything the cutscene shows. Saved inactive; on only during beats 1 to 4.")]
    public GameObject introRoot;
    public GameObject voidStage;
    public GameObject chamberStage;
    public CutsceneCameraRig cameraRig;
    public BirkinActor birkin;
    [Tooltip("Where Birkin stands in the void.")]
    public Transform voidBirkinSpot;
    [Tooltip("The point in the void he faces (the camera's side).")]
    public Transform voidFacePoint;
    [Tooltip("The player's feet in the chamber.")]
    public Transform playerStart;
    [Tooltip("Where Birkin stands in the chamber.")]
    public Transform guideSpot;
    [Tooltip("The points on the floor the walk passes through, ending at the portal.")]
    public Transform[] walkPath = new Transform[0];
    public CutscenePortal portal;
    public ArrivalPortal arrivalPortal;
    [Tooltip("Plays the camera shots (the Shot of each line).")]
    public PlayableDirector shotDirector;
    [Tooltip("Plays the Action of each line.")]
    public PlayableDirector actionDirector;

    [Header("Fog")]
    public Color voidFogColour = new Color(0.02f, 0.09f, 0.11f, 1f);
    public float voidFogDensity = 0.045f;
    public Color chamberFogColour = new Color(0.015f, 0.04f, 0.05f, 1f);
    public float chamberFogDensity = 0.018f;

    [Header("Timing (seconds)")]
    [Tooltip("The fade to black over the menu after Play.")]
    public float fadeToBlack = 0.7f;
    [Tooltip("The fade from black into the void.")]
    public float voidFadeIn = 1.4f;
    [Tooltip("Birkin's first line starts this long after he begins to materialise.")]
    public float revealLead = 1.1f;
    [Tooltip("The glitch that carries the void into the chamber.")]
    public float transitionGlitch = 0.9f;
    [Tooltip("In the chamber, before his first line there.")]
    public float chamberSettle = 0.5f;

    [Header("Chamber (first person)")]
    public float eyeHeight = 1.65f;
    public float chamberFieldOfView = 60f;
    [Tooltip("How much Birkin's head follows the player during the dialogue (0-1).")]
    [Range(0f, 1f)] public float dialogueLook = 0.5f;

    [Header("Walk into the portal")]
    public float walkSpeed = 2.2f;
    [Tooltip("The flash starts this many metres before the end of the path.")]
    public float flashDistance = 0.8f;
    public float flashFadeIn = 0.25f;
    [Tooltip("The white screen is held this long before the cut to the facility.")]
    public float whiteHold = 0.35f;
    public float portalGlitch = 0.9f;

    [Header("Arrival")]
    [Tooltip("Metres the player is walked forward out of the portal.")]
    public float arrivalWalkDistance = 2f;
    public float arrivalWalkSpeed = 1.7f;
    [Tooltip("The white flash fades out over this long.")]
    public float arrivalFlashFade = 0.9f;
    [Tooltip("After the walk, before the clock starts (the portal is collapsing).")]
    public float arrivalSettle = 0.5f;
    public float hudFade = 0.6f;

    [Header("Skip")]
    public KeyCode skipKey = KeyCode.Space;
    [Tooltip("Seconds the key must be held.")]
    public float skipHold = 1f;

    [Header("Sounds (empty slots fall back as noted)")]
    [Tooltip("The blip while dialogue text types. Empty = UI_Click, pitched up and very quiet.")]
    public Sfx blip = new Sfx();
    [Tooltip("The key clack of the typewriter. Empty = UI_Click.")]
    public Sfx keyClack = new Sfx();
    [Tooltip("Entering and leaving the portal. Empty = the menu's Play whoosh.")]
    public Sfx whoosh = new Sfx();
    [Range(0f, 1f)] public float voiceVolume = 1f;

    [Header("Voice")]
    [Tooltip("TimeTrapMixer/Voice. Empty = the SFX group.")]
    public AudioMixerGroup voiceGroup;
    [Tooltip("TimeTrapMixer/Voice/Radio: the arrival's radio line. Empty = the Voice group.")]
    public AudioMixerGroup radioGroup;
    [Tooltip("On: the silence at the end of a voice clip is not counted, so the text finishes as he stops talking.")]
    public bool trimTrailingSilence = true;
    [Tooltip("Anything quieter than this fraction of the clip's loudest sample counts as silence.")]
    [Range(0.005f, 0.3f)] public float silenceThreshold = 0.06f;
    [Tooltip("Seconds kept after the last sound, so the end of a word is not clipped by the prompt.")]
    public float speechTail = 0.12f;
    [Tooltip("Levelling, a safety net for new recordings: a clip whose loudest sample is above this level is turned down to it. The voice files are normalised to peak at about -1 dB (0.89), so they play as they are. A quieter clip cannot be turned up: normalise the file (VoiceOriginals/normalise_voice.py). 0 = no levelling.")]
    [Range(0f, 1f)] public float levelPeak = 0.9f;
    [Tooltip("Volume of the crackle before and after the radio line.")]
    [Range(0f, 1f)] public float crackleVolume = 0.55f;

    public Beat Current { get; private set; } = Beat.None;
    public bool Running { get; private set; }
    // The dialogue line on screen, counted from 1 across both beats (0 before the first).
    public int LineIndex { get; private set; }
    // 0-1: how long the skip key has been held.
    public float SkipHold01 { get; private set; }
    public bool Skipped { get; private set; }
    public CutsceneUI UI => ui;
    // A voice (Birkin's, or the radio line) is being spoken right now.
    public bool Speaking => (voiceSource != null && voiceSource.isPlaying && voiceSource.time < spokenUntil) || radioSpeaking;
    // A radio line is being played or is waiting its turn.
    public bool RadioBusy => radioRoutine != null;
    // Radio lines started since the scene loaded, and the clip of the last one (for tests).
    public int RadioLinesPlayed { get; private set; }
    public string LastRadioClip { get; private set; } = "";
    // The voice source is sounding at all (for tests: false the moment a line is cut or skipped).
    public bool VoicePlaying => (voiceSource != null && voiceSource.isPlaying) || (radioSource != null && radioSource.isPlaying);
    // The last voiced line: the file's length and the spoken part of it, in seconds (for tests).
    public float LastClipLength { get; private set; }
    public float LastSpokenLength { get; private set; }
    // The loudest sample of the last voiced line's file (0-1) and the volume it was played at after levelling.
    public float LastClipPeak { get; private set; }
    public float LastClipGain { get; private set; }
    // The voice source's clip name and volume right now (for tests).
    public string VoiceClipName => voiceSource != null && voiceSource.clip != null ? voiceSource.clip.name : "";
    public float VoiceSourceVolume => voiceSource != null ? voiceSource.volume : 0f;
    // The scene has an intro and it has not been played yet.
    public bool CanPlay => isActiveAndEnabled && script != null && introRoot != null && cameraRig != null && Current == Beat.None;

    // For tests: counts as holding the skip key.
    [System.NonSerialized] public bool scriptedSkipHeld;

    CutsceneUI ui;
    GameManager manager;
    PlayerMovementController movement;
    PlayerAudio playerAudio;
    Camera playerCamera;
    BirkinHologramFX hologram;
    BirkinFace face;
    AudioSource voiceSource, radioSource;
    AudioClip crackle;
    readonly Dictionary<AudioClip, float> spokenLengths = new Dictionary<AudioClip, float>();
    readonly Dictionary<AudioClip, float> clipPeaks = new Dictionary<AudioClip, float>();
    float spokenUntil;
    bool radioSpeaking;
    struct RadioItem
    {
        public AudioClip clip;
        public string speaker, text;
        public float seconds;
    }
    readonly Queue<RadioItem> radioQueue = new Queue<RadioItem>();
    Coroutine radioRoutine;
    readonly List<Renderer> hidden = new List<Renderer>();
    TimelineAsset currentShot;
    bool advanceRequested;
    int pressFrame = -1;       // the frame the last press was used in
    int lastFootstep = -1;

    bool fogSaved;
    bool savedFog;
    FogMode savedFogMode;
    Color savedFogColour;
    float savedFogDensity;

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

        var go = new GameObject("CutsceneUI");
        go.transform.SetParent(transform, false);
        ui = go.AddComponent<CutsceneUI>();

        voiceSource = gameObject.AddComponent<AudioSource>();
        voiceSource.playOnAwake = false;
        voiceSource.spatialBlend = 0f; // the listener stays with the player, up in the facility
        radioSource = gameObject.AddComponent<AudioSource>();
        radioSource.playOnAwake = false;
        radioSource.spatialBlend = 0f;

        if (birkin != null)
        {
            hologram = birkin.GetComponent<BirkinHologramFX>();
            face = birkin.GetComponent<BirkinFace>();
            if (face != null) face.voice = voiceSource;
        }
    }

    void Start()
    {
        manager = GameManager.Instance;
        movement = FindAnyObjectByType<PlayerMovementController>();
        if (movement != null)
        {
            playerCamera = movement.GetComponentInChildren<Camera>(true);
            playerAudio = movement.GetComponent<PlayerAudio>();
        }
        if (playerAudio == null) playerAudio = FindAnyObjectByType<PlayerAudio>();
        AudioMixerGroup fallback = GameAudio.Instance != null ? GameAudio.Instance.sfxGroup : null;
        voiceSource.outputAudioMixerGroup = voiceGroup != null ? voiceGroup : fallback;
        radioSource.outputAudioMixerGroup = radioGroup != null ? radioGroup : voiceSource.outputAudioMixerGroup;
        ui.blip = blip;
        ui.keyClack = keyClack;
        if (manager != null) manager.OnRunEnded += HandleRunEnded;
    }

    // After GameManager.ResetAllStatics on a restart (it clears the event), as every listener of it does.
    void OnEnable()
    {
        PlayerEvents.OnPlayerKilled += HandlePlayerKilled;
    }

    void HandlePlayerKilled(string cause) { StopRadio(); }
    void HandleRunEnded(RunStats stats) { StopRadio(); }

    void OnDisable()
    {
        PlayerEvents.OnPlayerKilled -= HandlePlayerKilled;
        radioQueue.Clear();
        radioRoutine = null;
        // a reload (or leaving Play mode) in the middle of it: nothing may stay changed
        StopAllCoroutines();
        if (movement != null && Running) movement.scriptedInput = false;
        RestoreFog();
        if (OwnsPicture) GivePicture();
        Running = false;
        radioSpeaking = false;
        GameAudio.VoiceActive = false;
    }

    void OnDestroy()
    {
        if (manager != null) manager.OnRunEnded -= HandleRunEnded;
        if (Instance == this) Instance = null;
    }

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this on Restart.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        Instance = null;
        OwnsPicture = false;
    }

    // ------------------------------------------------------------------ starting, skipping

    // Play was pressed (GameMenus). Skip To Arrival: "Always skip intro" is on.
    public void Begin(bool skipToArrival)
    {
        if (!CanPlay || manager == null) return;
        Running = true;
        Skipped = skipToArrival;
        StartCoroutine(Run(skipToArrival));
    }

    // The same as pressing E, a left click or Enter (for tests: a key press cannot be made from a script).
    public void Advance()
    {
        advanceRequested = true;
    }

    // Straight to the arrival, from anywhere in beats 1 to 4.
    public void Skip()
    {
        if (!Running || Current < Beat.Typewriter || Current >= Beat.Arrival) return;
        Skipped = true;
        // every beat is part of the one coroutine, so this stops whatever it was in the middle of
        // (no radio line can be playing yet: they only start once the run has)
        StopAllCoroutines();
        StartCoroutine(ArrivalBeat());
    }

    void Update()
    {
        // the ambience and the music step back while he speaks, and return between lines
        GameAudio.VoiceActive = Speaking;

        if (!Running || Current < Beat.Typewriter || Current >= Beat.Arrival)
        {
            SkipHold01 = 0f;
            if (ui != null && ui.SkipVisible) ui.SetSkip(false, 0f);
            return;
        }

        bool held = Input.GetKey(skipKey) || scriptedSkipHeld;
        float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, skipHold);
        // let go: the ring empties, quicker than it filled
        SkipHold01 = held ? Mathf.Clamp01(SkipHold01 + step) : Mathf.MoveTowards(SkipHold01, 0f, step * 3f);
        ui.SetSkip(true, SkipHold01);
        if (SkipHold01 >= 1f) Skip();
    }

    // E, left click or Enter went down this frame. One press does one thing: a key is "down" for the whole frame,
    // and the next line starts in the same frame the last one ended, so without the frame check the press that
    // moved on from a line also completed the next one and cut its voice before it was heard.
    bool AdvancePressed()
    {
        if (Time.frameCount == pressFrame) return false;
        bool pressed = advanceRequested || Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        advanceRequested = false;
        if (pressed) pressFrame = Time.frameCount;
        return pressed;
    }

    static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    // ------------------------------------------------------------------ the beats

    IEnumerator Run(bool skipToArrival)
    {
        Current = Beat.FadeOut;
        ui.FadeBlack(1f, fadeToBlack);
        yield return Wait(fadeToBlack + 0.05f);

        // nothing of the menu can be seen now
        if (GameMenus.Instance != null) GameMenus.Instance.CloseForIntro();
        manager.BeginCutscene();

        // the spoken length of every clip, worked out now, behind the black screen
        PrepareVoices();

        if (!skipToArrival)
        {
            TakePicture();
            yield return TypewriterBeat();
            yield return VoidBeat();
            yield return ChamberBeat();
            yield return WalkBeat();
        }
        yield return ArrivalBeat();
    }

    // Beat 1: black screen, typed lines.
    IEnumerator TypewriterBeat()
    {
        Current = Beat.Typewriter;
        string[] lines = script.typewriterLines;
        ui.BeginTypewriter(script.typewriterSpeed);
        for (int i = 0; i < lines.Length; i++)
        {
            ui.TypeLine(lines[i]);
            while (ui.Typing) yield return null;
            yield return Wait(i == lines.Length - 1 ? script.lastLineHold : script.lineHold);
        }
        ui.FadeTypewriter(script.lastLineFade);
        yield return Wait(script.lastLineFade);
        ui.ClearTypewriter();
    }

    // Beat 2: the void.
    IEnumerator VoidBeat()
    {
        Current = Beat.Void;
        ShowStage(false);
        if (birkin != null)
        {
            Vector3 spot = voidBirkinSpot != null ? voidBirkinSpot.position : birkin.transform.position;
            birkin.Place(spot, voidFacePoint != null ? voidFacePoint.position : cameraRig.transform.position);
            birkin.lookTarget = null;
            birkin.Play(BirkinAnim.Idle, true);
        }
        if (hologram != null) hologram.SetVisible(false);
        // the first shot, so the fade from black already shows the right framing
        if (script.voidLines.Length > 0) PlayShot(script.voidLines[0].shot);

        ui.FadeBlack(0f, voidFadeIn);
        yield return Wait(voidFadeIn * 0.5f);
        if (hologram != null) hologram.Appear();
        yield return Wait(revealLead);

        for (int i = 0; i < script.voidLines.Length; i++)
            yield return PlayLine(script.voidLines[i], i > 0);
    }

    // Beat 3: the glitch out of the void, then the portal chamber in first person.
    IEnumerator ChamberBeat()
    {
        if (hologram != null) hologram.BigGlitch();
        if (ScreenFX.Instance != null) ScreenFX.Instance.TriggerGlitch(transitionGlitch);
        ui.HideDialogue();
        ui.NoiseBurst(1f, 0.3f, 0.15f, 0.55f);
        // the stage is changed behind the static, at its thickest
        yield return Wait(0.32f);

        Current = Beat.Chamber;
        StopTimelines();
        ShowStage(true);
        Vector3 feet = playerStart != null ? playerStart.position : cameraRig.transform.position;
        Vector3 eyes = feet + Vector3.up * eyeHeight;
        Vector3 ahead = walkPath.Length > 0 && walkPath[walkPath.Length - 1] != null ? walkPath[walkPath.Length - 1].position - feet : Vector3.forward;
        ahead.y = 0f;
        cameraRig.SetBase(eyes, Quaternion.LookRotation(ahead.sqrMagnitude > 0.001f ? ahead : Vector3.forward));
        cameraRig.SetFieldOfView(chamberFieldOfView);
        cameraRig.ResetLook();
        cameraRig.LookEnabled = true;

        if (birkin != null)
        {
            birkin.Place(guideSpot != null ? guideSpot.position : birkin.transform.position, feet);
            birkin.Play(BirkinAnim.Idle, true);
            birkin.lookTarget = cameraRig.transform;
            birkin.lookWeight = dialogueLook;
        }
        if (hologram != null)
        {
            hologram.SetVisible(true);
            hologram.BigGlitch();
        }
        if (portal != null) portal.Power = 0f;
        yield return Wait(0.7f + chamberSettle);

        for (int i = 0; i < script.chamberLines.Length; i++)
            yield return PlayLine(script.chamberLines[i], i > 0);
    }

    // One dialogue line: its Timelines start, the text types, and the player moves on when they are ready.
    IEnumerator PlayLine(IntroScript.Line line, bool glitchFirst)
    {
        LineIndex++;
        if (glitchFirst && hologram != null) hologram.SmallGlitch();
        if (line.shot != null && line.shot != currentShot) PlayShot(line.shot);
        if (line.action != null && actionDirector != null) actionDirector.Play(line.action, DirectorWrapMode.Hold);

        StopVoice();
        float spoken = 0f;
        if (line.voice != null)
        {
            spoken = SpokenLength(line.voice);
            LastClipLength = line.voice.length;
            LastSpokenLength = spoken;
            spokenUntil = spoken;
            voiceSource.clip = line.voice;
            voiceSource.volume = voiceVolume * LevelGain(line.voice);
            if (face != null) face.voicePeak = LastClipPeak;
            voiceSource.Play();
            GameAudio.Note("Cutscene/Voice", voiceSource, line.voice);
        }

        // with a voice the text is spread over the spoken part of the clip; without one it types at its own speed, with blips
        ui.ShowLine(script.speaker, line.text, script.charactersPerSecond, spoken);
        advanceRequested = false;
        // his mouth follows the clip (BirkinFace reads it); with no clip it moves while the text types
        if (face != null) face.Talking = line.voice == null;
        while (ui.LineTyping)
        {
            // while it is typing, a press completes the line and cuts the voice: it does not skip the line
            if (AdvancePressed())
            {
                ui.CompleteLine();
                StopVoice();
            }
            // the text keeps exact time with the voice: it is placed from the clip's own playback position
            else if (spoken > 0f && voiceSource.isPlaying) ui.SetLineProgress(voiceSource.time / spoken);
            yield return null;
        }

        // the text is all there: the mouth closes
        if (face != null) face.Talking = false;
        if (birkin != null && birkin.IsTalkLoop) birkin.Play(BirkinAnim.Idle);
        // a frame, so the press that completed the line is not also read as "next"
        yield return null;
        while (!AdvancePressed()) yield return null;
        StopVoice();
    }

    // Beat 4: the camera walks up the ramp and into the portal.
    IEnumerator WalkBeat()
    {
        Current = Beat.Walk;
        ui.HideDialogue();
        cameraRig.LookEnabled = false;
        if (birkin != null) birkin.lookWeight = 1f;
        // the portal must be open by now, whatever the last line's Timeline did
        if (portal != null && portal.Power < 1f && (actionDirector == null || actionDirector.state != PlayState.Playing)) portal.FadeTo(1f, 0.6f);

        var points = new List<Vector3>();
        points.Add((playerStart != null ? playerStart.position : cameraRig.transform.position - Vector3.up * eyeHeight) + Vector3.up * eyeHeight);
        foreach (var point in walkPath)
            if (point != null) points.Add(point.position + Vector3.up * eyeHeight);

        float total = 0f;
        for (int i = 1; i < points.Count; i++) total += Vector3.Distance(points[i - 1], points[i]);

        float travelled = 0f;
        bool flashed = false;
        Quaternion facing = points.Count > 1 ? Heading(points[0], points[1], Quaternion.identity) : Quaternion.identity;
        while (travelled < total)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float step = walkSpeed * dt;
            travelled += step;

            Vector3 position = PointOnPath(points, Mathf.Min(travelled, total), out int segment);
            facing = Quaternion.Slerp(facing, Heading(points[segment], points[segment + 1], facing), 1f - Mathf.Exp(-4f * dt));
            cameraRig.SetBase(position, facing);
            if (cameraRig.Walk(step)) Footstep();

            if (!flashed && total - travelled <= flashDistance)
            {
                flashed = true;
                EnterPortal();
            }
            yield return null;
        }
        if (!flashed) EnterPortal();
        cameraRig.StopWalk();
        yield return Wait(flashFadeIn + whiteHold);
    }

    // The moment the camera reaches the portal's surface.
    void EnterPortal()
    {
        if (ScreenFX.Instance != null)
        {
            ScreenFX.Instance.TriggerGlitch(portalGlitch);
            ScreenFX.Instance.TriggerFlash();
        }
        ui.FadeWhite(1f, flashFadeIn);
        PlayWhoosh();
    }

    // Beat 5: the facility. Also where a skip lands.
    IEnumerator ArrivalBeat()
    {
        Current = Beat.Arrival;
        manager.BeginArrival();

        // the cutscene's own things go, behind a white screen
        StopTimelines();
        StopVoice();
        if (face != null) face.Talking = false;
        ui.HideAll();
        ui.SetWhite(1f);
        RestoreFog();
        if (OwnsPicture) GivePicture();
        if (introRoot != null) introRoot.SetActive(false);
        // the torch arm comes up into view instead of just being there
        var weapons = FindAnyObjectByType<PlayerWeaponState>();
        if (weapons != null) weapons.RaiseTorchFromBelow();

        if (arrivalPortal != null) arrivalPortal.Open();
        if (ScreenFX.Instance != null) ScreenFX.Instance.TriggerGlitch(0.5f);
        if (Skipped) PlayWhoosh();
        // a frame for the player's camera to draw before the white lifts
        yield return null;
        ui.FadeWhite(0f, arrivalFlashFade);

        // out of the portal: the movement script walks the player, so the head bob and the footsteps are the game's own
        if (movement != null)
        {
            Vector3 from = movement.transform.position;
            float input = Mathf.Clamp(arrivalWalkSpeed / Mathf.Max(0.1f, movement.WalkSpeed), 0.1f, 1f);
            float limit = arrivalWalkDistance / Mathf.Max(0.1f, arrivalWalkSpeed) + 1f;
            movement.scriptedSprint = false;
            movement.scriptedMove = new Vector2(0f, input);
            movement.scriptedInput = true;
            for (float t = 0f; t < limit; t += Time.unscaledDeltaTime)
            {
                Vector3 moved = movement.transform.position - from;
                moved.y = 0f;
                if (moved.magnitude >= arrivalWalkDistance) break;
                yield return null;
            }
            movement.scriptedMove = Vector2.zero;
            movement.scriptedInput = false;
        }

        if (arrivalPortal != null) arrivalPortal.Collapse();
        yield return Wait(arrivalSettle);

        // the clock starts here, and only here
        manager.StartGame(hudFade);
        Current = Beat.Done;
        Running = false;
        QueueRadio(script.radioVoice, script.radioSpeaker, script.radioLine, script.radioSeconds);
    }

    // ------------------------------------------------------------------ radio lines

    // A radio line: a subtitle, and with a clip the voice through the Radio group with a crackle before and after.
    // Lines never overlap: one asked for while another is playing waits its turn. Only during the run.
    public bool QueueRadio(AudioClip clip, string speaker, string text, float minSeconds)
    {
        if (manager == null || !isActiveAndEnabled || (manager.State != GameState.Playing && manager.State != GameState.Paused)) return false;
        radioQueue.Enqueue(new RadioItem { clip = clip, speaker = speaker, text = text, seconds = minSeconds });
        if (radioRoutine == null) radioRoutine = StartCoroutine(RadioLoop());
        return true;
    }

    // Cuts the line that is playing and drops the ones waiting (a death, the end of the run).
    public void StopRadio()
    {
        radioQueue.Clear();
        if (radioRoutine != null) StopCoroutine(radioRoutine);
        radioRoutine = null;
        if (radioSource != null) radioSource.Stop();
        radioSpeaking = false;
        if (ui != null) ui.HideSubtitle();
    }

    IEnumerator RadioLoop()
    {
        while (radioQueue.Count > 0)
        {
            RadioItem item = radioQueue.Dequeue();
            // not over the pause menu
            while (manager.State == GameState.Paused) yield return null;
            RadioLinesPlayed++;
            LastRadioClip = item.clip != null ? item.clip.name : "";

            if (item.clip == null)
            {
                ui.Subtitle(item.speaker, item.text, item.seconds);
                yield return RadioWait(item.seconds);
            }
            else
            {
                float spoken = SpokenLength(item.clip);
                LastClipLength = item.clip.length;
                LastSpokenLength = spoken;
                radioSpeaking = true;
                ui.Subtitle(item.speaker, item.text, Mathf.Max(item.seconds, spoken + 1f));
                Crackle();
                yield return RadioWait(0.14f);

                radioSource.clip = item.clip;
                radioSource.volume = voiceVolume * LevelGain(item.clip);
                radioSource.Play();
                GameAudio.Note("Cutscene/RadioVoice", radioSource, item.clip);
                // a source is "not playing" while the listener is paused (the pause menu): that is not the end of the line
                while (radioSource.time < spoken && (radioSource.isPlaying || AudioListener.pause)) yield return null;
                radioSource.Stop();
                Crackle();
                radioSpeaking = false;
            }
            // a breath between two lines
            if (radioQueue.Count > 0) yield return RadioWait(0.6f);
        }
        radioRoutine = null;
    }

    // Real seconds that do not count under the pause menu.
    IEnumerator RadioWait(float seconds)
    {
        for (float t = 0f; t < seconds; t += manager.State == GameState.Paused ? 0f : Time.unscaledDeltaTime) yield return null;
    }

    // ------------------------------------------------------------------ helpers

    void PlayShot(TimelineAsset shot)
    {
        currentShot = shot;
        if (shot == null || shotDirector == null) return;
        shotDirector.Play(shot, DirectorWrapMode.Hold);
        // the first pose now, not a frame later
        shotDirector.Evaluate();
    }

    void StopTimelines()
    {
        if (shotDirector != null && shotDirector.gameObject.activeInHierarchy) shotDirector.Stop();
        if (actionDirector != null && actionDirector.gameObject.activeInHierarchy) actionDirector.Stop();
        currentShot = null;
    }

    void StopVoice()
    {
        if (voiceSource != null && voiceSource.isPlaying) voiceSource.Stop();
        spokenUntil = 0f;
    }

    void PrepareVoices()
    {
        foreach (var line in script.voidLines) if (line.voice != null) SpokenLength(line.voice);
        foreach (var line in script.chamberLines) if (line.voice != null) SpokenLength(line.voice);
        if (script.radioVoice != null) SpokenLength(script.radioVoice);
    }

    // The volume a clip is played at so that its loudest sample comes out at Level Peak.
    float LevelGain(AudioClip clip)
    {
        SpokenLength(clip);
        float peak = clipPeaks.TryGetValue(clip, out float found) ? found : 0f;
        LastClipPeak = peak;
        LastClipGain = peak > 0.0001f && levelPeak > 0f ? Mathf.Clamp01(levelPeak / peak) : 1f;
        return LastClipGain;
    }

    // Seconds from the start of a clip to the end of its last sound (plus Speech Tail): the file's length without
    // the silence recorded after the last word. The whole length when the samples cannot be read (a streamed clip).
    float SpokenLength(AudioClip clip)
    {
        if (spokenLengths.TryGetValue(clip, out float known)) return known;
        float length = clip.length;
        var data = new float[clip.samples * Mathf.Max(1, clip.channels)];
        if (data.Length > 0 && clip.GetData(data, 0))
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            clipPeaks[clip] = peak;
            if (!trimTrailingSilence)
            {
                spokenLengths[clip] = length;
                return length;
            }
            float quiet = peak * silenceThreshold;
            int last = data.Length - 1;
            while (last > 0 && Mathf.Abs(data[last]) < quiet) last--;
            length = Mathf.Min(clip.length, last / (float)(clip.frequency * Mathf.Max(1, clip.channels)) + speechTail);
        }
        spokenLengths[clip] = length;
        return length;
    }

    // A burst of radio static: noise that dies away, made once. It goes through the Radio group's band-pass.
    void Crackle()
    {
        if (crackleVolume <= 0f) return;
        if (crackle == null)
        {
            const int rate = 22050;
            int count = Mathf.RoundToInt(rate * 0.16f);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)count;
                // a click at the start, then crackling noise fading out
                float envelope = Mathf.Exp(-t * 7f) * (Random.value < 0.35f ? 1f : 0.25f);
                samples[i] = Random.Range(-1f, 1f) * envelope;
            }
            crackle = AudioClip.Create("RadioCrackle", count, 1, rate, false);
            crackle.SetData(samples, 0);
        }
        radioSource.PlayOneShot(crackle, crackleVolume);
    }

    // The void or the chamber: one of the two stages, and its fog.
    void ShowStage(bool chamber)
    {
        if (voidStage != null) voidStage.SetActive(!chamber);
        if (chamberStage != null) chamberStage.SetActive(chamber);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = chamber ? chamberFogColour : voidFogColour;
        RenderSettings.fogDensity = chamber ? chamberFogDensity : voidFogDensity;
    }

    // The cutscene camera takes over: the player's camera is off and the player (body, arms, torch) is not drawn.
    void TakePicture()
    {
        fogSaved = true;
        savedFog = RenderSettings.fog;
        savedFogMode = RenderSettings.fogMode;
        savedFogColour = RenderSettings.fogColor;
        savedFogDensity = RenderSettings.fogDensity;

        // the stages start hidden behind the black screen; the root has to be on before its camera can draw
        if (introRoot != null) introRoot.SetActive(true);
        ShowStage(false);
        cameraRig.Camera.enabled = true;
        OwnsPicture = true;

        if (playerCamera == null) return;
        playerCamera.enabled = false;
        var body = playerCamera.GetComponentInParent<CharacterController>();
        Transform top = body != null ? body.transform : playerCamera.transform;
        foreach (var r in top.GetComponentsInChildren<Renderer>(true))
        {
            if (r.forceRenderingOff) continue;
            r.forceRenderingOff = true;
            hidden.Add(r);
        }
    }

    // The picture goes back to the player.
    void GivePicture()
    {
        OwnsPicture = false;
        foreach (var r in hidden)
            if (r != null) r.forceRenderingOff = false;
        hidden.Clear();
        if (cameraRig != null) cameraRig.Camera.enabled = false;
        if (playerCamera != null) playerCamera.enabled = true;
    }

    void RestoreFog()
    {
        if (!fogSaved) return;
        fogSaved = false;
        RenderSettings.fog = savedFog;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogColor = savedFogColour;
        RenderSettings.fogDensity = savedFogDensity;
    }

    // The point a distance along a line of points, and which segment it is on.
    static Vector3 PointOnPath(List<Vector3> points, float distance, out int segment)
    {
        segment = 0;
        for (int i = 1; i < points.Count; i++)
        {
            float length = Vector3.Distance(points[i - 1], points[i]);
            segment = i - 1;
            if (distance <= length || i == points.Count - 1)
                return Vector3.Lerp(points[i - 1], points[i], length > 0.0001f ? Mathf.Clamp01(distance / length) : 1f);
            distance -= length;
        }
        return points[points.Count - 1];
    }

    // Looking from one point to the next: the slope of the ramp tilts the view a little, at half strength.
    static Quaternion Heading(Vector3 from, Vector3 to, Quaternion fallback)
    {
        Vector3 direction = to - from;
        direction.y *= 0.5f;
        return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : fallback;
    }

    // One of the player's own walking steps.
    void Footstep()
    {
        var audio = GameAudio.Instance;
        if (audio == null || playerAudio == null) return;
        Sfx step = Sfx.Pick(playerAudio.footsteps, ref lastFootstep);
        audio.Play2D("Cutscene/Footstep", step, audio.sfxGroup, true, playerAudio.walkVolume);
    }

    void PlayWhoosh()
    {
        var audio = GameAudio.Instance;
        if (audio == null) return;
        Sfx sound = whoosh != null && whoosh.HasClip ? whoosh : (GameMenus.Instance != null ? GameMenus.Instance.playWhoosh : null);
        audio.Play2D("Cutscene/Whoosh", sound, audio.uiGroup, true);
    }
}
