using UnityEngine;

// The way out at Gate B. Off (visual hidden, trigger disabled) until GameProgress reaches Stage 3, then it glows and
// walking into its trigger raises GameProgress.OnPlayerEscaped. This object stays active so it can hear the stage
// change; only the Visual child is switched. When it opens, the portal surface (TimeTrap/TimePortal shader) grows in
// from nothing: _Activate runs 0 -> 1 over Activate Time, and the glow light comes up with it.
[RequireComponent(typeof(Collider))]
public class EscapePortal : MonoBehaviour
{
    [Tooltip("The portal surface and point light. Inactive until Stage 3.")]
    public GameObject visual;
    [Tooltip("The disc drawn with the TimeTrap/TimePortal shader. Its _Activate property is driven from here.")]
    public Renderer portalSurface;
    [Tooltip("Seconds the portal takes to grow in when it opens.")]
    public float activateTime = 2.5f;
    [Tooltip("Optional. Pulses while the portal is on.")]
    public Light glow;
    public float pulseSpeed = 2f;
    [Range(0f, 1f)] public float pulseAmount = 0.25f;

    public bool IsOpen { get; private set; }
    // 0 = closed, 1 = fully grown in.
    public float Activation { get; private set; }

    static readonly int ActivateId = Shader.PropertyToID("_Activate");
    MaterialPropertyBlock block;

    Collider zone;
    Transform player;
    float glowIntensity;
    bool glowRead;

    void Awake()
    {
        zone = GetComponent<Collider>();
        zone.isTrigger = true;
    }

    void OnEnable()
    {
        GameProgress.OnStageChanged += HandleStageChanged;
        GameProgress.OnProgressReset += HandleReset;
    }

    void OnDisable()
    {
        GameProgress.OnStageChanged -= HandleStageChanged;
        GameProgress.OnProgressReset -= HandleReset;
    }

    void Start()
    {
        SetOpen(GameProgress.Instance != null && GameProgress.Instance.Stage == GameStage.Stage3);
    }

    void HandleStageChanged(GameStage stage)
    {
        SetOpen(stage == GameStage.Stage3);
    }

    void HandleReset()
    {
        SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        if (zone == null) zone = GetComponent<Collider>();
        IsOpen = open;
        zone.enabled = open;
        if (visual != null) visual.SetActive(open);
        // always starts from nothing, so it grows in each time it opens
        Activation = 0f;
        ApplyActivation();
    }

    // Main menu only: shows the portal fully open without opening the way out (the trigger stays off). Preview(false)
    // puts it back to what the stage says.
    public void Preview(bool show)
    {
        if (IsOpen) return;
        if (visual != null) visual.SetActive(show);
        Activation = show ? 1f : 0f;
        ApplyActivation();
    }

    void ApplyActivation()
    {
        if (portalSurface == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        portalSurface.GetPropertyBlock(block);
        block.SetFloat(ActivateId, Activation);
        portalSurface.SetPropertyBlock(block);
    }

    // True if the point is inside the trigger volume.
    public bool Contains(Vector3 point)
    {
        return zone != null && zone.enabled && (zone.ClosestPoint(point) - point).sqrMagnitude < 1e-6f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other is CharacterController && other.CompareTag("Player")) Enter();
    }

    void Update()
    {
        if (!IsOpen) return;

        if (Activation < 1f)
        {
            Activation = activateTime > 0f ? Mathf.MoveTowards(Activation, 1f, Time.deltaTime / activateTime) : 1f;
            ApplyActivation();
        }

        if (glow != null)
        {
            if (!glowRead)
            {
                glowIntensity = glow.intensity;
                glowRead = true;
            }
            glow.intensity = glowIntensity * Activation * (1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed));
        }

        // as in DeathZone: a position check as well, so a teleport into the portal still counts
        if (player == null)
        {
            var found = GameObject.FindGameObjectWithTag("Player");
            if (found == null) return;
            player = found.transform;
        }
        if (Contains(player.position + Vector3.up * 0.9f)) Enter();
    }

    void Enter()
    {
        if (!IsOpen || GameProgress.Instance == null) return;
        GameProgress.Instance.RaisePlayerEscaped();
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.7f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
