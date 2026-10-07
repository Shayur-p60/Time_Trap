using UnityEngine;

// "Can I see the player", kept out of ZombieAI so sight can be tuned per zombie.
// How far a zombie sees depends on the light the player is in:
//   power off, torch off   Sight Range Dark   (short: the player can slip past in the dark)
//   power off, torch on    Sight Range Torch  (the torch gives the player away)
//   power on               Sight Range Lit
// Inside Proximity Range the player is noticed whatever way the zombie faces. Walls and closed doors block sight.
public class ZombieSenses : MonoBehaviour
{
    [Header("Sight")]
    public float sightRangeDark = 5f;
    public float sightRangeTorch = 11f;
    public float sightRangeLit = 14f;
    [Range(0f, 360f)] public float fieldOfView = 110f;
    [Tooltip("Detected at this distance regardless of facing direction.")]
    public float proximityRange = 2.2f;
    [Tooltip("Eye height above the pivot, used when Eye is empty.")]
    public float eyeHeight = 1.6f;
    [Tooltip("Optional. The head bone; ZombieAI fills it in.")]
    public Transform eye;
    [Tooltip("What blocks sight. Left empty: everything except Zombie, Player, Ignore Raycast, TransparentFX, UI and Interactable.")]
    public LayerMask obstructionMask;
    [Tooltip("Seconds between sight checks.")]
    public float checkInterval = 0.12f;

    [Header("Debug")]
    public bool drawGizmos = true;

    // The spawner raises this in Stage 3.
    public float SightMultiplier { get; set; } = 1f;

    public Transform Player => PlayerCatch.Player;
    public bool CanSeePlayer { get; private set; }
    public bool HasLastKnownPosition { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public float TimeSinceLastSeen => Time.time - lastSeenTime;

    float lastSeenTime = -999f;
    float nextCheck;
    FlashlightController torch;
    CharacterController playerController;

    public float CurrentSightRange
    {
        get
        {
            float range;
            if (PowerState.IsPowered) range = sightRangeLit;
            else range = TorchOn ? sightRangeTorch : sightRangeDark;
            return range * SightMultiplier;
        }
    }

    bool TorchOn
    {
        get
        {
            if (torch == null && Player != null) torch = Player.GetComponentInChildren<FlashlightController>();
            return torch != null && torch.isActiveAndEnabled && torch.IsOn;
        }
    }

    public Vector3 EyePosition => eye != null ? eye.position : transform.position + Vector3.up * eyeHeight;

    // The middle of the player's body.
    public Vector3 PlayerCentre
    {
        get
        {
            Transform p = Player;
            if (p == null) return transform.position;
            if (playerController == null) playerController = p.GetComponent<CharacterController>();
            return playerController != null ? playerController.bounds.center : p.position + Vector3.up;
        }
    }

    void Awake()
    {
        if (obstructionMask.value == 0)
            obstructionMask = ~LayerMask.GetMask("Zombie", "Player", "Ignore Raycast", "TransparentFX", "UI", "Interactable");
        // spread the checks of zombies spawned in the same frame
        nextCheck = Time.time + Random.value * checkInterval;
    }

    void OnDisable()
    {
        CanSeePlayer = false;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + checkInterval;
        CheckNow();
    }

    // Runs the sight check immediately (Update does it every Check Interval).
    public bool CheckNow()
    {
        CanSeePlayer = CheckSight();
        if (CanSeePlayer)
        {
            lastSeenTime = Time.time;
            LastKnownPosition = Player.position;
            HasLastKnownPosition = true;
        }
        return CanSeePlayer;
    }

    bool CheckSight()
    {
        if (Player == null || PlayerCatch.PlayerOutOfPlay) return false;

        Vector3 from = EyePosition;
        Vector3 target = PlayerCentre;
        Vector3 toPlayer = target - from;
        float distance = toPlayer.magnitude;
        if (distance > CurrentSightRange) return false;

        if (distance > proximityRange)
        {
            Vector3 flat = toPlayer;
            flat.y = 0f;
            if (Vector3.Angle(transform.forward, flat) > fieldOfView * 0.5f) return false;
        }
        return HasClearLine(target);
    }

    // True when nothing solid is between the eyes and the point.
    public bool HasClearLine(Vector3 point)
    {
        return !Physics.Linecast(EyePosition, point, obstructionMask, QueryTriggerInteraction.Ignore);
    }

    // The zombie was told where the player is (it was shot, for example) without seeing them.
    public void NotePlayerPosition(Vector3 position)
    {
        LastKnownPosition = position;
        HasLastKnownPosition = true;
    }

    // Call after a respawn or a stun so stale memory doesn't linger.
    public void ForgetPlayer()
    {
        lastSeenTime = -999f;
        CanSeePlayer = false;
        HasLastKnownPosition = false;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Vector3 from = EyePosition;
        float range = Application.isPlaying ? CurrentSightRange : sightRangeLit;

        Gizmos.color = CanSeePlayer ? Color.red : Color.yellow;
        Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * transform.forward;
        Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * transform.forward;
        Gizmos.DrawRay(from, left * range);
        Gizmos.DrawRay(from, right * range);

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, proximityRange);
    }
}
