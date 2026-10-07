using UnityEngine;

// The way out at Gate B. Off (visual hidden, trigger disabled) until GameProgress reaches Stage 3, then it glows and
// walking into its trigger raises GameProgress.OnPlayerEscaped. This object stays active so it can hear the stage
// change; only the Visual child is switched. The glow is a placeholder until the portal shader exists.
[RequireComponent(typeof(Collider))]
public class EscapePortal : MonoBehaviour
{
    [Tooltip("The glowing placeholder (emissive disc and point light). Inactive until Stage 3.")]
    public GameObject visual;
    [Tooltip("Optional. Pulses while the portal is on.")]
    public Light glow;
    public float pulseSpeed = 2f;
    [Range(0f, 1f)] public float pulseAmount = 0.25f;

    public bool IsOpen { get; private set; }

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

        if (glow != null)
        {
            if (!glowRead)
            {
                glowIntensity = glow.intensity;
                glowRead = true;
            }
            glow.intensity = glowIntensity * (1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed));
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
