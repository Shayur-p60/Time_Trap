using UnityEngine;

// The portal in the chamber of the intro cutscene. Goes on PortalRig. One number, Power, runs everything:
//   0   idle, during the dialogue: the surface (TimeTrap/TimePortal) is a small disc at Idle Activate, the glow is low
//   1   open: _Activate 1, the cyan strips of the ring at Strip Boost times their brightness, the glow light swollen,
//       the hum at full volume
// On the way up the red lamps flicker. A Timeline's Portal Power track raises it during Birkin's last line; the
// director can also fade it (FadeTo). Real (unscaled) time.
public class CutscenePortal : MonoBehaviour
{
    [Tooltip("The disc drawn with the TimeTrap/TimePortal shader.")]
    public Renderer surface;
    [Tooltip("_Activate while idle. The portal is 'on standby': a small swirling disc.")]
    [Range(0f, 1f)] public float idleActivate = 0.15f;

    [Header("Ring")]
    [Tooltip("Portal_Frame. Its emissive cyan material brightens with the power.")]
    public Renderer frame;
    [Tooltip("The material of the cyan strips on the ring.")]
    public Material stripMaterial;
    [Tooltip("Emission multiplier at full power.")]
    public float stripBoost = 4f;

    [Header("Glow")]
    public Light glow;
    public float glowIdle = 2f;
    public float glowFull = 14f;
    public float pulseSpeed = 2f;
    [Range(0f, 1f)] public float pulseAmount = 0.2f;

    [Header("Lamps")]
    [Tooltip("The red emergency lamps: they flicker while the portal powers up.")]
    public CutsceneLamp[] lamps = new CutsceneLamp[0];
    [Tooltip("Flicker that stays on once the portal is open.")]
    [Range(0f, 1f)] public float openFlicker = 0.2f;

    [Header("Hum")]
    [Tooltip("The power-up hum. Empty = GameAudio's portal hum (World_Portal_Hum).")]
    public AudioClip hum;
    [Range(0f, 1f)] public float humVolume = 0.7f;
    [Tooltip("Pitch at no power (x) and at full power (y).")]
    public Vector2 humPitch = new Vector2(0.75f, 1.1f);

    // 0 = idle, 1 = open.
    public float Power
    {
        get { return power; }
        set
        {
            power = Mathf.Clamp01(value);
            fading = false;
            Apply();
        }
    }

    // _Activate on the surface right now.
    public float Activation { get; private set; }

    static readonly int ActivateId = Shader.PropertyToID("_Activate");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    [SerializeField, Range(0f, 1f)] float power;
    MaterialPropertyBlock block, stripBlock;
    AudioSource humSource;
    int stripIndex = -1;
    Color stripEmission = Color.black;
    bool fading;
    float fadeTarget;
    float fadeRate;

    void Awake()
    {
        block = new MaterialPropertyBlock();
        stripBlock = new MaterialPropertyBlock();
        if (frame != null && stripMaterial != null)
        {
            var materials = frame.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == stripMaterial) stripIndex = i;
            if (stripMaterial.HasProperty(EmissionId)) stripEmission = stripMaterial.GetColor(EmissionId);
        }
    }

    void OnEnable()
    {
        Apply();
    }

    void OnDisable()
    {
        if (humSource != null) humSource.Stop();
    }

    // Moves the power to a value over a number of seconds.
    public void FadeTo(float value, float seconds)
    {
        if (seconds <= 0f)
        {
            Power = value;
            return;
        }
        fading = true;
        fadeTarget = Mathf.Clamp01(value);
        fadeRate = 1f / seconds;
    }

    void Update()
    {
        if (fading)
        {
            power = Mathf.MoveTowards(power, fadeTarget, fadeRate * Time.unscaledDeltaTime);
            if (Mathf.Approximately(power, fadeTarget)) fading = false;
        }
        Apply();
    }

    void Apply()
    {
        if (block == null) return;

        Activation = Mathf.Lerp(idleActivate, 1f, power);
        if (surface != null)
        {
            surface.GetPropertyBlock(block);
            block.SetFloat(ActivateId, Activation);
            surface.SetPropertyBlock(block);
        }

        if (frame != null && stripIndex >= 0)
        {
            frame.GetPropertyBlock(stripBlock, stripIndex);
            stripBlock.SetColor(EmissionId, stripEmission * Mathf.Lerp(1f, stripBoost, power));
            frame.SetPropertyBlock(stripBlock, stripIndex);
        }

        if (glow != null)
            glow.intensity = Mathf.Lerp(glowIdle, glowFull, power) * (1f + pulseAmount * Mathf.Sin(Time.unscaledTime * pulseSpeed));

        // strongest half-way up, a little left once it is open
        float flicker = Mathf.Clamp01(Mathf.Sin(power * Mathf.PI) * 1.3f) + (power >= 0.999f ? openFlicker : 0f);
        foreach (var lamp in lamps)
            if (lamp != null) lamp.flicker = Mathf.Clamp01(flicker);

        UpdateHum();
    }

    void UpdateHum()
    {
        if (!Application.isPlaying) return;
        if (power <= 0.001f)
        {
            if (humSource != null && humSource.isPlaying) humSource.Stop();
            return;
        }

        if (humSource == null)
        {
            var audio = GameAudio.Instance;
            AudioClip clip = hum != null ? hum : (audio != null ? audio.portalHum.clip : null);
            if (clip == null) return;
            humSource = gameObject.AddComponent<AudioSource>();
            // 2D: the listener stays with the player, up in the facility
            Sfx3D.Apply2D(humSource, audio != null ? audio.sfxGroup : null);
            humSource.clip = clip;
            humSource.loop = true;
            GameAudio.Note("Cutscene/PortalHum", humSource, clip);
        }
        humSource.volume = humVolume * power;
        humSource.pitch = Mathf.Lerp(humPitch.x, humPitch.y, power);
        if (!humSource.isPlaying && isActiveAndEnabled) humSource.Play();
    }
}
