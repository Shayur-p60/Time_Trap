using UnityEngine;
using UnityEngine.Audio;

// The music: three loops and two stingers, all on the Music mixer group. One per scene, on Map/Gameplay/MusicManager.
// Every slot may be left empty: an empty slot is simply silent, so the files can be dropped in later (see
// Assets/_Audio/Music/README.md for the expected names).
//
//   Menu loop      while the main menu is up.
//   Ambient loop   cross-fades in over Crossfade Time when Play is pressed, and runs for the whole game.
//   Chase layer    on top of the ambient loop while any zombie is chasing or attacking the player, and all through
//                  Stage 3. In over Chase Fade In; out again Chase Hold after the last chase ends.
//   Stingers       win or loss: played once, with the end screen's title, while the loops fade out.
// The sources ignore the paused listener, so the music carries on under the pause menu, where the mixer's Paused
// snapshot turns it down and muffles it. Levels move on real time for the same reason.
public class MusicManager : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    [Tooltip("TimeTrapMixer/Music. Empty = GameAudio's music group.")]
    public AudioMixerGroup musicGroup;

    [Header("Loops")]
    public Slot menuLoop = new Slot();
    public Slot ambientLoop = new Slot();
    public Slot chaseLayer = new Slot();

    [Header("Stingers")]
    public Slot winStinger = new Slot();
    public Slot lossStinger = new Slot();

    [Header("Timing (seconds)")]
    public float crossfadeTime = 2f;
    public float chaseFadeIn = 1f;
    [Tooltip("The chase layer stays up this long after the last chase ends, then fades over Chase Fade Out.")]
    public float chaseHold = 2f;
    public float chaseFadeOut = 1f;
    public float endFade = 1.5f;

    // Current levels (0-1, before the slot's volume), for tests.
    public float MenuLevel { get; private set; }
    public float AmbientLevel { get; private set; }
    public float ChaseLevel { get; private set; }
    public bool ChaseWanted { get; private set; }
    public int StingersPlayed { get; private set; }

    AudioSource menuSource, ambientSource, chaseSource, stingerSource;
    GameManager manager;
    float sinceChase = 999f;
    bool ended;

    void Awake()
    {
        menuSource = MakeSource("MenuLoop", true);
        ambientSource = MakeSource("AmbientLoop", true);
        chaseSource = MakeSource("ChaseLayer", true);
        stingerSource = MakeSource("Stinger", false);
    }

    AudioSource MakeSource(string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.priority = 10;
        source.ignoreListenerPause = true;
        return source;
    }

    void Start()
    {
        manager = GameManager.Instance;
        if (manager != null) manager.OnRunEnded += HandleRunEnded;
        if (musicGroup == null && GameAudio.Instance != null) musicGroup = GameAudio.Instance.musicGroup;
        foreach (var source in new[] { menuSource, ambientSource, chaseSource, stingerSource })
            source.outputAudioMixerGroup = musicGroup;
    }

    void OnDestroy()
    {
        if (manager != null) manager.OnRunEnded -= HandleRunEnded;
    }

    void HandleRunEnded(RunStats stats)
    {
        ended = true;
    }

    // The win or loss stinger, once. The end screen calls this as its title appears.
    public void PlayStinger(bool won)
    {
        Slot stinger = won ? winStinger : lossStinger;
        StingersPlayed++;
        if (stinger.clip == null) return;
        stingerSource.clip = stinger.clip;
        stingerSource.volume = stinger.volume;
        stingerSource.Play();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        GameState state = manager != null ? manager.State : GameState.Playing;
        // the cross-fade starts with the Play click, not when the camera arrives
        bool menu = state == GameState.Menu && !manager.Starting;

        if (ended)
        {
            float step = dt / Mathf.Max(0.01f, endFade);
            MenuLevel = Mathf.MoveTowards(MenuLevel, 0f, step);
            AmbientLevel = Mathf.MoveTowards(AmbientLevel, 0f, step);
            ChaseLevel = Mathf.MoveTowards(ChaseLevel, 0f, step);
        }
        else
        {
            float cross = dt / Mathf.Max(0.01f, crossfadeTime);
            MenuLevel = Mathf.MoveTowards(MenuLevel, menu ? 1f : 0f, cross);
            AmbientLevel = Mathf.MoveTowards(AmbientLevel, menu ? 0f : 1f, cross);

            // under the pause menu the chase stands still, and so does its layer
            if (state == GameState.Playing)
            {
                ChaseWanted = AnyChase();
                sinceChase = ChaseWanted ? 0f : sinceChase + dt;
                if (ChaseWanted) ChaseLevel = Mathf.MoveTowards(ChaseLevel, 1f, dt / Mathf.Max(0.01f, chaseFadeIn));
                else if (sinceChase >= chaseHold) ChaseLevel = Mathf.MoveTowards(ChaseLevel, 0f, dt / Mathf.Max(0.01f, chaseFadeOut));
            }
            else if (state == GameState.Menu)
            {
                ChaseWanted = false;
                ChaseLevel = 0f;
            }
        }

        Drive(menuSource, menuLoop, MenuLevel);
        Drive(ambientSource, ambientLoop, AmbientLevel);
        Drive(chaseSource, chaseLayer, ChaseLevel);
    }

    static bool AnyChase()
    {
        var progress = GameProgress.Instance;
        if (progress != null && progress.Stage == GameStage.Stage3) return true;
        foreach (var zombie in ZombieAI.All)
        {
            if (zombie == null) continue;
            var state = zombie.CurrentState;
            if (state == ZombieAI.State.Chase || state == ZombieAI.State.Attack) return true;
        }
        return false;
    }

    // An empty slot never plays: no source is started, so there is nothing to warn about.
    static void Drive(AudioSource source, Slot slot, float level)
    {
        if (slot.clip == null)
        {
            if (source.isPlaying) source.Stop();
            return;
        }
        if (level <= 0f)
        {
            if (source.isPlaying) source.Stop();
            return;
        }
        if (source.clip != slot.clip) source.clip = slot.clip;
        source.volume = slot.volume * level;
        if (!source.isPlaying) source.Play();
    }
}
