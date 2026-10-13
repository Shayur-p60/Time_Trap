using UnityEngine;

// The portal the player steps out of at the end of the intro: a smaller copy of the chamber's portal ring, with a
// TimeTrap/TimePortal disc in it, standing right behind the player's start point. Map/Gameplay/ArrivalPortal, saved
// inactive: only the cutscene director switches it on (Open), so Play Again and Restart never see it.
// It has no collider and is not part of the NavMesh: it is light and geometry only.
//   Open       shown at once, fully open.
//   Collapse   _Activate runs 1 -> 0 over Collapse Time with a screen glitch, the glow dies with it, and the whole
//              object is switched off.
// Real (unscaled) time.
public class ArrivalPortal : MonoBehaviour
{
    [Tooltip("The disc drawn with the TimeTrap/TimePortal shader.")]
    public Renderer surface;
    public Light glow;
    [Tooltip("Seconds the portal takes to collapse.")]
    public float collapseTime = 0.7f;
    [Tooltip("Seconds of ScreenFX glitch as it collapses (0 = none).")]
    public float glitchTime = 0.4f;

    public bool IsOpen { get; private set; }
    public bool Collapsing { get; private set; }
    // 1 = open, 0 = gone.
    public float Activation { get; private set; }

    static readonly int ActivateId = Shader.PropertyToID("_Activate");
    MaterialPropertyBlock block;
    float glowIntensity;
    bool glowRead;
    Vector3 scale;
    bool scaleRead;

    public void Open()
    {
        if (!scaleRead)
        {
            scale = transform.localScale;
            scaleRead = true;
        }
        if (glow != null && !glowRead)
        {
            glowIntensity = glow.intensity;
            glowRead = true;
        }
        transform.localScale = scale;
        gameObject.SetActive(true);
        IsOpen = true;
        Collapsing = false;
        Activation = 1f;
        Apply();
    }

    public void Collapse()
    {
        if (!IsOpen || Collapsing) return;
        Collapsing = true;
        if (glitchTime > 0f && ScreenFX.Instance != null) ScreenFX.Instance.TriggerGlitch(glitchTime);
    }

    void Update()
    {
        if (!Collapsing) return;
        Activation = collapseTime > 0f ? Mathf.MoveTowards(Activation, 0f, Time.unscaledDeltaTime / collapseTime) : 0f;
        Apply();
        if (Activation > 0f) return;

        Collapsing = false;
        IsOpen = false;
        if (scaleRead) transform.localScale = scale;
        gameObject.SetActive(false);
    }

    void Apply()
    {
        if (surface != null)
        {
            if (block == null) block = new MaterialPropertyBlock();
            surface.GetPropertyBlock(block);
            block.SetFloat(ActivateId, Activation);
            surface.SetPropertyBlock(block);
        }
        if (glow != null && glowRead) glow.intensity = glowIntensity * Activation;
        // the ring folds in on itself over the second half
        if (scaleRead) transform.localScale = scale * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Activation * 2f));
    }
}
