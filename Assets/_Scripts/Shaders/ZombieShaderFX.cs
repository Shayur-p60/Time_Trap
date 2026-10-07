using UnityEngine;

// Drives the TimeTrap/ZombieSurface shader on one zombie. Sits on the Zombie prefab beside ZombieAI.
//   death   OnDeathStarted -> Dissolve Delay seconds later, _Dissolve runs 0 -> 1 over Dissolve Time (this replaces
//           the sink into the floor; the zombie is removed by ZombieAI when the dissolve ends). On the Zombie prefab
//           the delay is the death animation + 0.5 s, so a zombie is gone about 6 s after it dies.
//   damage  ZombieHealth.OnDamaged -> _HitFlash jumps to 1 and fades out
//   freeze  ZombieAI.IsFrozen -> _Freeze fades in and out
//   stun    ZombieAI state Stunned -> _Stun fades in and out
// The values go to the renderers through a MaterialPropertyBlock, so every zombie shares the same materials.
[RequireComponent(typeof(ZombieAI))]
public class ZombieShaderFX : MonoBehaviour
{
    [Header("Dissolve")]
    [Tooltip("Seconds the body takes to dissolve.")]
    public float dissolveTime = 2.5f;
    [Tooltip("Seconds after death before the dissolve starts. Negative = when the body used to start sinking (after the death animation and the corpse linger).")]
    public float dissolveDelay = -1f;

    [Header("Hit flash")]
    [Tooltip("Seconds a hit flash lasts.")]
    public float hitFlashTime = 0.15f;

    [Header("Freeze and stun")]
    [Tooltip("Seconds the icy look takes to fade in or out.")]
    public float freezeFadeTime = 0.35f;
    [Tooltip("Seconds the stun flicker takes to fade in or out.")]
    public float stunFadeTime = 0.2f;

    // current values (0-1), for other scripts and tests
    public float Dissolve { get; private set; }
    public float HitFlash { get; private set; }
    public float Freeze { get; private set; }
    public float Stun { get; private set; }

    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    static readonly int FreezeId = Shader.PropertyToID("_Freeze");
    static readonly int StunId = Shader.PropertyToID("_Stun");
    static readonly int TintId = Shader.PropertyToID("_Tint");

    ZombieAI ai;
    ZombieHealth health;
    Renderer[] renderers = new Renderer[0];
    MaterialPropertyBlock block;
    Color tint = Color.white;
    bool dying;
    float deadTime;
    float dissolveStart;
    bool dirty = true;

    void Awake()
    {
        ai = GetComponent<ZombieAI>();
        health = GetComponent<ZombieHealth>();
        block = new MaterialPropertyBlock();
        // the dissolve replaces the sink
        ai.sinkOnDeath = false;
    }

    void OnEnable()
    {
        ai.OnDeathStarted += HandleDeathStarted;
        if (health != null) health.OnDamaged += HandleDamaged;
    }

    void OnDisable()
    {
        ai.OnDeathStarted -= HandleDeathStarted;
        if (health != null) health.OnDamaged -= HandleDamaged;
    }

    // Finds the body's renderers. The spawner adds the body after this object is created, so this runs again
    // (from Update) until there is something to find. Call it yourself after swapping the body.
    public void Bind()
    {
        renderers = GetComponentsInChildren<Renderer>();
        dirty = true;
    }

    // Multiplied into the body's colour (the spawner's runner tint).
    public void SetTint(Color colour)
    {
        tint = colour;
        Bind();
        Apply();
    }

    void HandleDamaged(float damage, Vector3 point, bool head)
    {
        HitFlash = 1f;
        dirty = true;
    }

    void HandleDeathStarted(ZombieAI zombie)
    {
        dying = true;
        deadTime = 0f;
        dissolveStart = dissolveDelay >= 0f ? dissolveDelay : ai.deathAnimationTime + ai.corpseLinger;
        // ZombieAI removes the body at death animation + linger + sink time: make that the end of the dissolve
        ai.corpseLinger = Mathf.Max(0f, dissolveStart - ai.deathAnimationTime);
        ai.sinkTime = dissolveTime;
    }

    void Update()
    {
        if (renderers.Length == 0) Bind();
        float dt = Time.deltaTime;

        if (dying)
        {
            deadTime += dt;
            float dissolve = dissolveTime > 0f ? Mathf.Clamp01((deadTime - dissolveStart) / dissolveTime) : 1f;
            if (dissolve != Dissolve) { Dissolve = dissolve; dirty = true; }
        }

        if (HitFlash > 0f)
        {
            HitFlash = Mathf.MoveTowards(HitFlash, 0f, dt / Mathf.Max(0.01f, hitFlashTime));
            dirty = true;
        }

        float freezeTarget = !dying && ai.IsFrozen ? 1f : 0f;
        float freeze = Mathf.MoveTowards(Freeze, freezeTarget, dt / Mathf.Max(0.01f, freezeFadeTime));
        if (freeze != Freeze) { Freeze = freeze; dirty = true; }

        float stunTarget = ai.CurrentState == ZombieAI.State.Stunned ? 1f : 0f;
        float stun = Mathf.MoveTowards(Stun, stunTarget, dt / Mathf.Max(0.01f, stunFadeTime));
        if (stun != Stun) { Stun = stun; dirty = true; }

        if (dirty) Apply();
    }

    // Writes the current values to every renderer of the body.
    void Apply()
    {
        dirty = false;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(DissolveId, Dissolve);
            block.SetFloat(HitFlashId, HitFlash);
            block.SetFloat(FreezeId, Freeze);
            block.SetFloat(StunId, Stun);
            block.SetColor(TintId, tint);
            r.SetPropertyBlock(block);
        }
    }
}
