using UnityEngine;

// A zombie's sounds: groans while it wanders, a scream when it spots the player, footsteps, and one-shots for the
// attack, being hit, dying and banging on a door. ZombieAI calls the Play methods; any list left empty is simply silent,
// so placeholder clips can be dropped in one at a time.
[RequireComponent(typeof(AudioSource))]
public class ZombieAudio : MonoBehaviour
{
    public AudioClip[] groans = new AudioClip[0];
    public AudioClip[] screams = new AudioClip[0];
    public AudioClip[] footsteps = new AudioClip[0];
    public AudioClip[] attacks = new AudioClip[0];
    public AudioClip[] hits = new AudioClip[0];
    public AudioClip[] deaths = new AudioClip[0];
    public AudioClip[] doorBangs = new AudioClip[0];

    [Header("Groans")]
    [Tooltip("Seconds between groans: random between these two values.")]
    public Vector2 groanInterval = new Vector2(6f, 14f);
    [Range(0f, 1f)] public float groanVolume = 0.7f;

    [Header("Footsteps")]
    [Tooltip("Metres walked per footstep.")]
    public float stepLength = 0.7f;
    [Range(0f, 1f)] public float footstepVolume = 0.5f;

    [Tooltip("Pitch of every sound: random between these two values, so two zombies never sound identical.")]
    public Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    // For tests: how many times each kind was asked for, clip or no clip.
    public int GroanCount { get; private set; }
    public int ScreamCount { get; private set; }
    public int FootstepCount { get; private set; }
    public int DoorBangCount { get; private set; }

    AudioSource source;
    float nextGroan;
    float stepDistance;
    float basePitch = 1f;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        basePitch = Random.Range(pitchRange.x, pitchRange.y);
        source.pitch = basePitch;
        ScheduleGroan();
    }

    void ScheduleGroan()
    {
        nextGroan = Time.time + Random.Range(groanInterval.x, groanInterval.y);
    }

    // Called every frame by ZombieAI: speed in m/s, and whether the zombie is calm enough to groan.
    public void Tick(float speed, bool mayGroan, float deltaTime)
    {
        if (speed > 0.2f)
        {
            stepDistance += speed * deltaTime;
            if (stepDistance >= stepLength)
            {
                stepDistance = 0f;
                FootstepCount++;
                Play(footsteps, footstepVolume);
            }
        }

        if (mayGroan && Time.time >= nextGroan)
        {
            ScheduleGroan();
            GroanCount++;
            Play(groans, groanVolume);
        }
    }

    public void PlayScream()
    {
        ScreamCount++;
        ScheduleGroan();
        Play(screams, 1f);
    }

    public void PlayAttack() => Play(attacks, 1f);
    public void PlayHit() => Play(hits, 0.9f);
    public void PlayDeath() => Play(deaths, 1f);

    public void PlayDoorBang()
    {
        DoorBangCount++;
        Play(doorBangs, 1f);
    }

    void Play(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0 || source == null || !source.enabled) return;
        var clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) source.PlayOneShot(clip, volume);
    }
}
