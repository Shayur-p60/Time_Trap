using System.Collections.Generic;
using UnityEngine;

// Dr Birkin's face in the intro cutscene: blinks and mouth movement, through the blend shapes of his head. Goes on
// the DR_Birkin object. The head, the teeth and the brows carry shapes with the same names, so each value is written
// to every face mesh that has it.
//
//   Blink   eyeClose_L and eyeClose_R, every 2.5 to 6 seconds.
//   Mouth   mouthOpen and jawDrop.
//           With a voice clip playing on Voice, the mouth follows the clip's loudness (read from the clip's own
//           samples at the playback position, so the clip should be imported as Decompress On Load).
//           Without one, it flaps in a speech-like rhythm while Talking is on (the director switches it on while the
//           dialogue text is typing) and closes when it goes off.
public class BirkinFace : MonoBehaviour
{
    [Header("Blend shape names")]
    public string eyeCloseLeft = "eyeClose_L";
    public string eyeCloseRight = "eyeClose_R";
    public string mouthOpen = "mouthOpen";
    public string jawDrop = "jawDrop";

    [Header("Blink")]
    [Tooltip("Seconds between blinks: random between these two values.")]
    public Vector2 blinkInterval = new Vector2(2.5f, 6f);
    [Tooltip("Seconds a blink takes, closing and opening.")]
    public float blinkTime = 0.14f;

    [Header("Mouth")]
    [Tooltip("Blend shape weight (0-100) with the mouth fully open.")]
    public float mouthOpenMax = 65f;
    public float jawDropMax = 40f;
    [Tooltip("Syllables per second of the flapping rhythm.")]
    public float flapRate = 5.5f;
    [Tooltip("How quickly the mouth follows its target (per second).")]
    public float mouthSpeed = 16f;

    [Header("Voice")]
    [Tooltip("The source the director plays voice clips on. Empty = rhythm only.")]
    public AudioSource voice;
    [Tooltip("Loudness (RMS) of the clip, as a fraction of the clip's loudest sample, is multiplied by this to get the mouth opening.")]
    public float voiceGain = 7f;
    // The loudest sample of the clip that is playing (set by the director), so a quiet recording opens the mouth as far as a loud one.
    [System.NonSerialized] public float voicePeak = 1f;

    // The dialogue text is typing: the mouth moves (unless a voice clip is doing it).
    public bool Talking { get; set; }
    // For tests.
    public float MouthOpen01 { get; private set; }
    public float EyesClosed01 { get; private set; }
    public int Blinks { get; private set; }
    public bool UsingVoice { get; private set; }

    struct FaceMesh
    {
        public SkinnedMeshRenderer renderer;
        public int eyeL, eyeR, mouth, jaw;
    }

    readonly List<FaceMesh> meshes = new List<FaceMesh>();
    float nextBlink;
    float blink = -1f;       // seconds into the current blink, negative when not blinking
    float talkClock;
    float[] samples = new float[512];

    void Awake()
    {
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            UnityEngine.Mesh mesh = r.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0) continue;
            var face = new FaceMesh
            {
                renderer = r,
                eyeL = mesh.GetBlendShapeIndex(eyeCloseLeft),
                eyeR = mesh.GetBlendShapeIndex(eyeCloseRight),
                mouth = mesh.GetBlendShapeIndex(mouthOpen),
                jaw = mesh.GetBlendShapeIndex(jawDrop),
            };
            if (face.eyeL >= 0 || face.eyeR >= 0 || face.mouth >= 0 || face.jaw >= 0) meshes.Add(face);
        }
        ScheduleBlink();
    }

    void OnDisable()
    {
        Talking = false;
        MouthOpen01 = 0f;
        EyesClosed01 = 0f;
        blink = -1f;
        Apply();
    }

    void ScheduleBlink()
    {
        nextBlink = Random.Range(blinkInterval.x, blinkInterval.y);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // --- blink ---------------------------------------------------------------------------------------------
        if (blink < 0f)
        {
            nextBlink -= dt;
            if (nextBlink <= 0f)
            {
                blink = 0f;
                Blinks++;
            }
        }
        else
        {
            blink += dt;
            float half = Mathf.Max(0.02f, blinkTime * 0.5f);
            // closes over the first half, opens over the second
            EyesClosed01 = Mathf.Clamp01(1f - Mathf.Abs(blink - half) / half);
            if (blink >= half * 2f)
            {
                blink = -1f;
                EyesClosed01 = 0f;
                ScheduleBlink();
            }
        }

        // --- mouth ---------------------------------------------------------------------------------------------
        float target = 0f;
        UsingVoice = voice != null && voice.isPlaying && voice.clip != null && VoiceLevel(out target);
        if (!UsingVoice)
        {
            target = 0f;
            if (Talking || (voice != null && voice.isPlaying))
            {
                talkClock += dt;
                // one open-and-close per syllable, each a different size
                float syllable = Mathf.Abs(Mathf.Sin(talkClock * flapRate * Mathf.PI));
                target = syllable * Mathf.Lerp(0.35f, 1f, Mathf.PerlinNoise(talkClock * 3.1f, 7.3f));
            }
        }
        MouthOpen01 = Mathf.MoveTowards(MouthOpen01, target, Mathf.Max(1f, mouthSpeed) * dt);
    }

    // Loudness of the voice clip around the playback position. False when the clip's samples cannot be read
    // (a streamed or compressed-in-memory clip): the rhythm stands in.
    bool VoiceLevel(out float level)
    {
        level = 0f;
        AudioClip clip = voice.clip;
        int channels = Mathf.Max(1, clip.channels);
        int frames = samples.Length / channels;
        int position = Mathf.Clamp(voice.timeSamples, 0, Mathf.Max(0, clip.samples - frames - 1));
        if (clip.samples <= frames || !clip.GetData(samples, position)) return false;

        float sum = 0f;
        int count = frames * channels;
        for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
        level = Mathf.Clamp01(Mathf.Sqrt(sum / count) / Mathf.Max(0.0001f, voicePeak) * voiceGain);
        return true;
    }

    // After the Animator, so nothing overwrites the shapes.
    void LateUpdate()
    {
        Apply();
    }

    void Apply()
    {
        float eyes = EyesClosed01 * 100f;
        foreach (var face in meshes)
        {
            if (face.renderer == null) continue;
            if (face.eyeL >= 0) face.renderer.SetBlendShapeWeight(face.eyeL, eyes);
            if (face.eyeR >= 0) face.renderer.SetBlendShapeWeight(face.eyeR, eyes);
            if (face.mouth >= 0) face.renderer.SetBlendShapeWeight(face.mouth, MouthOpen01 * mouthOpenMax);
            if (face.jaw >= 0) face.renderer.SetBlendShapeWeight(face.jaw, MouthOpen01 * jawDropMax);
        }
    }
}
