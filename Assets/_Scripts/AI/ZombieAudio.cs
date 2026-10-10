using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// A zombie's sounds: groans while it wanders, a scream when it spots the player, footsteps, and one-shots for the
// attack, being hit, dying and banging on a door. ZombieAI calls Tick and the Play methods; any list left empty is
// simply silent, so clips can be dropped in one at a time.
//
//   3D        every sound is fully 3D with a logarithmic rolloff and is gone by Max Distance (about 20 m).
//   Voice     one mouth: a scream, snarl, grunt or death sound cuts off a groan.
//   Groans    at most Max Groaners zombies groan at once, the nearest ones to the listener.
//   Steps     one per stride of distance walked: a short stride and slow shuffle while wandering, a long stride while
//             chasing, and never closer together than Min Step Gap.
//   Death     plays when the dissolve starts (ZombieShaderFX), or at once on a zombie without one.
//   Occlusion a line from the head to the listener is checked a few times a second. Level geometry in the way
//             brings in a low-pass filter and drops the volume, both smoothed.
[RequireComponent(typeof(AudioSource))]
public class ZombieAudio : MonoBehaviour
{
    public Sfx[] groans = new Sfx[0];
    public Sfx[] screams = new Sfx[0];
    public Sfx[] footsteps = new Sfx[0];
    public Sfx[] attacks = new Sfx[0];
    public Sfx[] hits = new Sfx[0];
    public Sfx[] deaths = new Sfx[0];
    public Sfx[] doorBangs = new Sfx[0];

    [Tooltip("The Zombies group of TimeTrapMixer.")]
    public AudioMixerGroup output;

    [Header("3D")]
    public float minDistance = 2f;
    [Tooltip("Nothing is heard beyond this (metres).")]
    public float maxDistance = 20f;

    [Header("Groans")]
    [Tooltip("Seconds between groans: random between these two values.")]
    public Vector2 groanInterval = new Vector2(4f, 9f);
    [Range(0f, 1f)] public float groanVolume = 0.5f;
    [Tooltip("How many zombies may groan at the same time. The nearest ones win.")]
    public int maxGroaners = 3;

    [Header("Footsteps")]
    [Tooltip("Metres walked per footstep while wandering, investigating or searching.")]
    public float stepLength = 0.55f;
    [Tooltip("Metres run per footstep while chasing.")]
    public float chaseStepLength = 1.3f;
    [Tooltip("Footsteps are never closer together than this (seconds).")]
    public float minStepGap = 0.28f;
    [Range(0f, 1f)] public float footstepVolume = 0.35f;
    [Range(0f, 1f)] public float chaseFootstepVolume = 0.55f;
    [Tooltip("Seconds of the shuffle clip played per step: wandering (x) and chasing (y).")]
    public Vector2 stepSeconds = new Vector2(0.5f, 0.26f);
    public float chaseStepPitch = 1.2f;

    [Header("One-shots")]
    [Range(0f, 1f)] public float screamVolume = 0.9f;
    [Range(0f, 1f)] public float attackVolume = 0.95f;
    [Range(0f, 1f)] public float hitVolume = 0.7f;
    [Range(0f, 1f)] public float deathVolume = 0.7f;
    [Range(0f, 1f)] public float doorBangVolume = 0.8f;

    [Tooltip("Pitch of every sound: random between these two values, so two zombies never sound identical.")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    [Header("Occlusion")]
    [Tooltip("Seconds between line checks to the listener.")]
    public float occlusionInterval = 0.2f;
    [Tooltip("Low-pass cutoff when level geometry is in the way (Hz).")]
    public float occludedCutoff = 1200f;
    [Tooltip("Volume when level geometry is in the way, as a fraction.")]
    [Range(0f, 1f)] public float occludedVolume = 0.6f;
    [Tooltip("Seconds to fade between clear and muffled.")]
    public float occlusionFade = 0.3f;
    [Tooltip("Layers that muffle. Left empty: everything except Zombie, Player, Ignore Raycast, TransparentFX, UI and Interactable.")]
    public LayerMask occlusionMask;

    // For tests: how many times each kind was asked for, clip or no clip.
    public int GroanCount { get; private set; }
    public int ScreamCount { get; private set; }
    public int FootstepCount { get; private set; }
    public int DoorBangCount { get; private set; }
    public int AttackCount { get; private set; }
    public int HitCount { get; private set; }
    public int DeathCount { get; private set; }

    // 0 = clear line to the listener, 1 = fully muffled.
    public float Occlusion { get; private set; }
    public bool IsGroaning => voice != null && voice.Active && !voice.FadingOut && voice.tag == KindGroan;
    public AudioSource VoiceSource => voice != null ? voice.source : null;
    public AudioSource FeetSource => feet != null ? feet.source : null;
    public AudioLowPassFilter Filter => filter;

    // Zombies groaning right now.
    public static int ActiveGroaners
    {
        get
        {
            Prune();
            return groaners.Count;
        }
    }
    // Groans that were due but held back by the cap, and line checks made (for the cost test).
    public static int GroansHeldBack { get; private set; }
    public static int OcclusionChecks { get; private set; }

    const int KindGroan = 1, KindOther = 2;
    const float FilterOpen = 22000f;

    static readonly List<ZombieAudio> groaners = new List<ZombieAudio>();

    ZombieAI ai;
    ZombieSenses senses;
    ZombieShaderFX shaderFx;
    AudioLowPassFilter filter;
    SfxVoice voice;
    SfxVoice feet;
    readonly Sfx stepSlice = new Sfx();
    float nextGroan;
    float stepDistance;
    float lastStepTime = -999f;
    float basePitch = 1f;
    float occlusionTimer;
    float occlusionTarget;
    float listenerDistance;
    bool deathPending;
    int lastGroan = -1;
    int lastHit = -1;

    void Awake()
    {
        ai = GetComponent<ZombieAI>();
        senses = GetComponent<ZombieSenses>();
        shaderFx = GetComponent<ZombieShaderFX>();
        if (occlusionMask.value == 0)
            occlusionMask = ~LayerMask.GetMask("Zombie", "Player", "Ignore Raycast", "TransparentFX", "UI", "Interactable");

        // both sources sit on this object, so the one low-pass filter muffles the voice and the feet together
        var sources = GetComponents<AudioSource>();
        var voiceSource = sources[0];
        var feetSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
        Sfx3D.Apply(voiceSource, minDistance, maxDistance, output);
        Sfx3D.Apply(feetSource, minDistance, maxDistance, output);
        voiceSource.priority = 100;
        feetSource.priority = 160;
        voice = new SfxVoice(voiceSource);
        feet = new SfxVoice(feetSource);

        filter = GetComponent<AudioLowPassFilter>();
        if (filter == null) filter = gameObject.AddComponent<AudioLowPassFilter>();
        filter.cutoffFrequency = FilterOpen;
        filter.enabled = false;

        basePitch = Random.Range(pitchRange.x, pitchRange.y);
        occlusionTimer = Random.Range(0f, Mathf.Max(0.01f, occlusionInterval));
        listenerDistance = float.MaxValue;
        ScheduleGroan();
    }

    void OnDisable()
    {
        groaners.Remove(this);
        if (voice != null) voice.Stop();
        if (feet != null) feet.Stop();
    }

    void ScheduleGroan()
    {
        nextGroan = Time.time + Random.Range(groanInterval.x, groanInterval.y);
    }

    // Called every frame by ZombieAI while it is up and about: speed in m/s, and whether it is calm enough to groan.
    public void Tick(float speed, bool mayGroan, float deltaTime)
    {
        if (speed > 0.2f)
        {
            bool chasing = ai != null && (ai.CurrentState == ZombieAI.State.Chase || ai.CurrentState == ZombieAI.State.Attack);
            stepDistance += speed * deltaTime;
            if (stepDistance >= (chasing ? chaseStepLength : stepLength) && Time.time - lastStepTime >= minStepGap)
            {
                stepDistance = 0f;
                lastStepTime = Time.time;
                PlayFootstep(chasing);
            }
        }

        if (mayGroan && Time.time >= nextGroan) TryGroan();
    }

    void PlayFootstep(bool chasing)
    {
        FootstepCount++;
        Sfx source = Sfx.Pick(footsteps);
        GameAudio.Note(chasing ? "Zombie/FootstepChase" : "Zombie/Footstep", feet.source, source);
        if (source == null || !Audible) return;

        // the shuffle clip is one long take: each step is a short piece from a random place in it
        float seconds = chasing ? stepSeconds.y : stepSeconds.x;
        stepSlice.clip = source.clip;
        stepSlice.volume = source.volume;
        stepSlice.pitch = source.pitch;
        stepSlice.fadeOut = Mathf.Min(0.12f, seconds * 0.4f);
        if (source.length > 0f)
        {
            stepSlice.start = source.start;
            stepSlice.length = source.length;
        }
        else
        {
            stepSlice.start = Random.Range(0f, Mathf.Max(0f, source.clip.length - seconds - 0.05f));
            stepSlice.length = seconds;
        }
        feet.Play(stepSlice, chasing ? chaseFootstepVolume : footstepVolume, basePitch * (chasing ? chaseStepPitch : 1f));
    }

    // Makes the next groan due at once (for tests).
    public void GroanNow()
    {
        nextGroan = 0f;
    }

    // A groan is due. Only the Max Groaners zombies nearest the listener groan at once: a nearer zombie takes the
    // place of the farthest one, a farther one waits and tries again shortly.
    void TryGroan()
    {
        if (!Audible)
        {
            ScheduleGroan();
            return;
        }

        Prune();
        if (!groaners.Contains(this) && groaners.Count >= Mathf.Max(1, maxGroaners))
        {
            ZombieAudio farthest = null;
            foreach (var other in groaners)
                if (farthest == null || other.listenerDistance > farthest.listenerDistance) farthest = other;

            if (farthest == null || farthest.listenerDistance <= listenerDistance)
            {
                GroansHeldBack++;
                nextGroan = Time.time + Random.Range(1f, 2f);
                return;
            }
            farthest.voice.FadeOut(0.15f);
            groaners.Remove(farthest);
        }

        ScheduleGroan();
        GroanCount++;
        Sfx groan = Sfx.Pick(groans, ref lastGroan);
        GameAudio.Note("Zombie/Groan", voice.source, groan);
        if (!voice.Play(groan, groanVolume, basePitch)) return;
        voice.tag = KindGroan;
        if (!groaners.Contains(this)) groaners.Add(this);
    }

    static void Prune()
    {
        for (int i = groaners.Count - 1; i >= 0; i--)
            if (groaners[i] == null || !groaners[i].IsGroaning) groaners.RemoveAt(i);
    }

    public void PlayScream()
    {
        ScreamCount++;
        ScheduleGroan();
        PlayVoice("Zombie/Scream", Sfx.Pick(screams), screamVolume);
    }

    // The start of the attack wind-up: the player's warning.
    public void PlayAttack()
    {
        AttackCount++;
        ScheduleGroan();
        PlayVoice("Zombie/Attack", Sfx.Pick(attacks), attackVolume);
    }

    public void PlayHit()
    {
        HitCount++;
        PlayVoice("Zombie/Hit", Sfx.Pick(hits, ref lastHit), hitVolume);
    }

    // The zombie has died. The sound itself waits for the dissolve to start.
    public void PlayDeath()
    {
        if (voice != null && voice.tag == KindGroan) voice.FadeOut(0.15f);
        groaners.Remove(this);
        if (shaderFx != null && shaderFx.isActiveAndEnabled) deathPending = true;
        else PlayDeathNow();
    }

    void PlayDeathNow()
    {
        deathPending = false;
        DeathCount++;
        PlayVoice("Zombie/Death", Sfx.Pick(deaths), deathVolume);
    }

    public void PlayDoorBang()
    {
        DoorBangCount++;
        Sfx bang = Sfx.Pick(doorBangs);
        GameAudio.Note("Zombie/DoorBang", feet.source, bang);
        if (Audible) feet.Play(bang, doorBangVolume, basePitch);
    }

    void PlayVoice(string role, Sfx sfx, float volume)
    {
        GameAudio.Note(role, voice.source, sfx);
        groaners.Remove(this);
        if (!voice.Play(sfx, volume, basePitch)) return;
        voice.tag = KindOther;
    }

    // Close enough to the listener to be heard at all.
    bool Audible => listenerDistance <= maxDistance + 2f;

    Vector3 HeadPosition => senses != null ? senses.EyePosition : transform.position + Vector3.up * 1.6f;

    void Update()
    {
        float dt = Time.deltaTime;

        if (deathPending && (shaderFx == null || shaderFx.Dissolve > 0f)) PlayDeathNow();

        occlusionTimer -= dt;
        if (occlusionTimer <= 0f)
        {
            occlusionTimer = Mathf.Max(0.02f, occlusionInterval);
            Transform listener = GameAudio.Listener;
            if (listener == null)
            {
                listenerDistance = 0f;
                occlusionTarget = 0f;
            }
            else
            {
                Vector3 head = HeadPosition;
                Vector3 ear = listener.position;
                listenerDistance = Vector3.Distance(head, ear);
                // too far away to be heard: no line check
                if (!Audible) occlusionTarget = 0f;
                else
                {
                    OcclusionChecks++;
                    // both ways: much of the level is one-sided mesh colliders, which a line only hits from the front
                    bool blocked = Physics.Linecast(head, ear, occlusionMask, QueryTriggerInteraction.Ignore)
                        || Physics.Linecast(ear, head, occlusionMask, QueryTriggerInteraction.Ignore);
                    occlusionTarget = blocked ? 1f : 0f;
                }
            }
        }

        if (Occlusion != occlusionTarget)
            Occlusion = Mathf.MoveTowards(Occlusion, occlusionTarget, dt / Mathf.Max(0.01f, occlusionFade));

        bool muffled = Occlusion > 0.01f;
        if (filter.enabled != muffled) filter.enabled = muffled;
        // the cutoff moves evenly in pitch, not in Hz, so the fade sounds even
        if (muffled) filter.cutoffFrequency = Mathf.Exp(Mathf.Lerp(Mathf.Log(FilterOpen), Mathf.Log(Mathf.Max(100f, occludedCutoff)), Occlusion));

        float gain = Mathf.Lerp(1f, occludedVolume, Occlusion);
        voice.gain = gain;
        feet.gain = gain;
        voice.Tick();
        feet.Tick();
    }

    // Statics survive a scene reload: GameManager.ResetAllStatics calls this on Restart.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        groaners.Clear();
        GroansHeldBack = 0;
        OcclusionChecks = 0;
    }
}
