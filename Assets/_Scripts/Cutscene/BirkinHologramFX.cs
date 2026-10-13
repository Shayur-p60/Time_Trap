using UnityEngine;
using UnityEngine.Rendering.Universal;

// Drives the TimeTrap/Hologram shader on Dr Birkin. Goes on the DR_Birkin object.
//   reveal    Appear: _Reveal runs 0 -> 1 over Reveal Time (he materialises from the feet up), with a big glitch.
//             Disappear: the same backwards.
//   glitch    SmallGlitch between dialogue lines, BigGlitch when he appears or disappears: _GlitchAmount jumps up and
//             fades out. Now and then a faint one fires by itself.
//   flicker   _Flicker sits at Base Flicker and rises with the glitch.
// The values go to the renderers through a MaterialPropertyBlock, like ZombieShaderFX, so the hologram materials are
// never changed. Everything runs on real (unscaled) time.
//
// It also switches on the "Hologram Depth" renderer feature (the depth pre-pass of the shader) while he exists, and
// off again when he is disabled, so the feature costs nothing during play.
public class BirkinHologramFX : MonoBehaviour
{
    [Tooltip("The Render Objects feature on PC_Renderer that draws the shader's HologramDepth pass. On only while this object is.")]
    public ScriptableRendererFeature depthFeature;

    [Header("Reveal")]
    [Tooltip("Seconds he takes to materialise (and to go).")]
    public float revealTime = 1.6f;
    [Tooltip("His height in metres: the reveal line travels this far up from his feet.")]
    public float figureHeight = 1.9f;

    [Header("Glitch")]
    [Range(0f, 1f)] public float smallGlitch = 0.4f;
    public float smallGlitchTime = 0.3f;
    [Range(0f, 1f)] public float bigGlitch = 1f;
    public float bigGlitchTime = 0.8f;
    [Tooltip("A faint glitch by itself every so many seconds: random between these two values. 0 = never.")]
    public Vector2 idleGlitchInterval = new Vector2(5f, 11f);
    [Range(0f, 1f)] public float idleGlitch = 0.22f;

    [Header("Flicker")]
    [Range(0f, 1f)] public float baseFlicker = 0.15f;
    [Tooltip("Extra flicker at a full glitch.")]
    [Range(0f, 1f)] public float glitchFlicker = 0.6f;

    // Current values (0-1), for other scripts and tests.
    public float Reveal { get; private set; } = 1f;
    public float Glitch { get; private set; }
    public float Flicker { get; private set; }
    public int GlitchCount { get; private set; }

    static readonly int RevealId = Shader.PropertyToID("_Reveal");
    static readonly int GlitchId = Shader.PropertyToID("_GlitchAmount");
    static readonly int FlickerId = Shader.PropertyToID("_Flicker");
    static readonly int RevealMinYId = Shader.PropertyToID("_RevealMinY");
    static readonly int RevealHeightId = Shader.PropertyToID("_RevealHeight");

    Renderer[] renderers = new Renderer[0];
    MaterialPropertyBlock block;
    float revealTarget = 1f;
    float glitchFade = 1f;
    float nextIdleGlitch;

    void Awake()
    {
        block = new MaterialPropertyBlock();
        renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnEnable()
    {
        if (depthFeature != null && !depthFeature.isActive) depthFeature.SetActive(true);
        ScheduleIdleGlitch();
        Apply();
    }

    void OnDisable()
    {
        if (depthFeature != null && depthFeature.isActive) depthFeature.SetActive(false);
        Glitch = 0f;
    }

    void ScheduleIdleGlitch()
    {
        nextIdleGlitch = idleGlitchInterval.y > 0f ? Random.Range(idleGlitchInterval.x, idleGlitchInterval.y) : float.MaxValue;
    }

    // He materialises from the feet up, glitching hard.
    public void Appear()
    {
        Reveal = 0f;
        revealTarget = 1f;
        BigGlitch();
    }

    // He goes the way he came.
    public void Disappear()
    {
        revealTarget = 0f;
        BigGlitch();
    }

    // Shown or hidden at once, with no reveal.
    public void SetVisible(bool visible)
    {
        Reveal = revealTarget = visible ? 1f : 0f;
        Apply();
    }

    public void SmallGlitch()
    {
        StartGlitch(smallGlitch, smallGlitchTime);
    }

    public void BigGlitch()
    {
        StartGlitch(bigGlitch, bigGlitchTime);
    }

    void StartGlitch(float amount, float seconds)
    {
        if (amount < Glitch) return;
        Glitch = amount;
        glitchFade = amount / Mathf.Max(0.01f, seconds);
        GlitchCount++;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        Reveal = Mathf.MoveTowards(Reveal, revealTarget, dt / Mathf.Max(0.01f, revealTime));
        Glitch = Mathf.MoveTowards(Glitch, 0f, glitchFade * dt);

        nextIdleGlitch -= dt;
        if (nextIdleGlitch <= 0f)
        {
            ScheduleIdleGlitch();
            if (Reveal >= 1f) StartGlitch(idleGlitch, smallGlitchTime);
        }

        Flicker = Mathf.Clamp01(baseFlicker + glitchFlicker * Glitch);
        Apply();
    }

    // Writes the current values to every renderer of the body.
    void Apply()
    {
        if (block == null) return;
        float feet = transform.position.y;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(RevealId, Reveal);
            block.SetFloat(GlitchId, Glitch);
            block.SetFloat(FlickerId, Flicker);
            block.SetFloat(RevealMinYId, feet);
            block.SetFloat(RevealHeightId, Mathf.Max(0.1f, figureHeight));
            r.SetPropertyBlock(block);
        }
    }
}
