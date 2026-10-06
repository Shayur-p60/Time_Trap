using System;
using UnityEngine;
using UnityEngine.AI;

// A sliding door. Each leaf slides by its own open offset; the leaf colliders are switched off while the door is open.
// Moving makes noise, and an open door closes itself after a while once the doorway is clear.
//
// Modes:
//   Normal      always usable.
//   PowerLocked shows "Locked – needs power" until PowerState.IsPowered, then behaves as Normal.
//   Decoy       never opens, always shows "Locked".
//   External    only another script opens and closes it (the elevator); E does nothing.
//
// Open(), Close() and SetOpen() ignore the mode, so other scripts can always drive the door.
public class Door : Interactable
{
    public enum Mode { Normal, PowerLocked, Decoy, External }

    [Serializable]
    public class Leaf
    {
        public Transform transform;
        [Tooltip("How far this leaf moves when the door opens, in the space of the leaf's parent (its local position changes by this much).")]
        public Vector3 openOffset;
        [Tooltip("Local position when closed. Recorded by 'Capture Closed Pose'.")]
        public Vector3 closedLocalPosition;
    }

    public Mode mode = Mode.Normal;

    [Header("Leaves")]
    public Leaf[] leaves = new Leaf[0];
    [Tooltip("Colliders that block the doorway. Disabled while the door is open.")]
    public Collider[] leafColliders = new Collider[0];

    [Header("Movement")]
    [Tooltip("Seconds to slide fully open or closed.")]
    [Min(0.01f)] public float moveTime = 0.6f;
    [Tooltip("How far the sound of the door carries (metres).")]
    public float noiseRadius = 10f;

    [Header("Auto close")]
    public bool autoClose = true;
    [Tooltip("Seconds the door stays open before it tries to close.")]
    public float autoCloseDelay = 8f;
    [Tooltip("The door will not close itself while the player or a zombie is within this distance of the doorway.")]
    public float clearRadius = 1.2f;
    [Tooltip("Optional. Centre of the doorway; left empty, the centre of the closed leaf colliders is used.")]
    public Transform doorwayCentre;

    [SerializeField, HideInInspector] bool closedPoseCaptured;
    [SerializeField, HideInInspector] Vector3 doorwayLocal;

    public event Action<Door, bool> OnDoorMoved; // (door, opening)

    bool targetOpen;
    float openAmount; // 0 closed .. 1 open
    float openTimer;
    float nextClearCheck;

    public bool IsOpen => targetOpen;
    public bool IsFullyOpen => openAmount >= 1f;
    public bool IsFullyClosed => openAmount <= 0f;
    public bool IsMoving => openAmount != (targetOpen ? 1f : 0f);
    public bool IsLocked => mode == Mode.Decoy || (mode == Mode.PowerLocked && !PowerState.IsPowered);

    public Vector3 DoorwayPosition
    {
        get
        {
            if (doorwayCentre != null) return doorwayCentre.position;
            return closedPoseCaptured ? transform.TransformPoint(doorwayLocal) : transform.position;
        }
    }

    // ------------------------------------------------------------------ interaction

    public override string PromptText
    {
        get
        {
            switch (mode)
            {
                case Mode.Decoy: return "Locked";
                case Mode.External: return "";
                case Mode.PowerLocked:
                    if (!PowerState.IsPowered) return "Locked – needs power";
                    break;
            }
            return targetOpen ? "[E] Close door" : "[E] Open door";
        }
    }

    public override bool CanInteract => mode == Mode.Normal || (mode == Mode.PowerLocked && PowerState.IsPowered);

    public override void Interact()
    {
        if (CanInteract) Toggle();
    }

    // ------------------------------------------------------------------ control

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
    public void Toggle() => SetOpen(!targetOpen);

    public void SetOpen(bool open)
    {
        EnsureClosedPose();
        if (targetOpen == open) return;
        targetOpen = open;
        openTimer = 0f;
        nextClearCheck = 0f;
        // passable as soon as it starts opening, solid as soon as it starts closing
        SetCollidersEnabled(!open);
        NoiseSystem.MakeNoise(DoorwayPosition, noiseRadius);
        OnDoorMoved?.Invoke(this, open);
    }

    // Jumps straight to open or closed: no slide, no noise.
    public void SetOpenImmediate(bool open)
    {
        EnsureClosedPose();
        targetOpen = open;
        openAmount = open ? 1f : 0f;
        openTimer = 0f;
        SetCollidersEnabled(!open);
        ApplyPose();
    }

    // True when neither the player nor a zombie is standing in the doorway.
    public bool IsDoorwayClear()
    {
        var hits = Physics.OverlapSphere(DoorwayPosition, clearRadius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (hit is CharacterController) return false;
            if (hit.GetComponentInParent<NavMeshAgent>() != null) return false;
        }
        return true;
    }

    // Records the current leaf positions as the closed pose. Call while the door is closed and its colliders are enabled.
    [ContextMenu("Capture Closed Pose")]
    public void CaptureClosedPose()
    {
        foreach (var leaf in leaves)
            if (leaf != null && leaf.transform != null) leaf.closedLocalPosition = leaf.transform.localPosition;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var c in leafColliders)
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            sum += c.bounds.center;
            count++;
        }
        if (count == 0)
            foreach (var leaf in leaves)
            {
                if (leaf == null || leaf.transform == null) continue;
                sum += leaf.transform.position;
                count++;
            }
        doorwayLocal = count > 0 ? transform.InverseTransformPoint(sum / count) : Vector3.zero;
        closedPoseCaptured = true;
    }

    void EnsureClosedPose()
    {
        if (!closedPoseCaptured) CaptureClosedPose();
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        EnsureClosedPose();
    }

    void Update()
    {
        Tick(Time.deltaTime);
    }

    // One step of door movement and auto-close. Public so the door can be stepped without Play mode.
    public void Tick(float deltaTime)
    {
        float target = targetOpen ? 1f : 0f;
        if (openAmount != target)
        {
            openAmount = Mathf.MoveTowards(openAmount, target, deltaTime / Mathf.Max(0.01f, moveTime));
            ApplyPose();
            return;
        }

        if (!targetOpen || !autoClose) return;

        openTimer += deltaTime;
        if (openTimer < autoCloseDelay || openTimer < nextClearCheck) return;
        if (IsDoorwayClear()) Close();
        else nextClearCheck = openTimer + 0.25f;
    }

    void ApplyPose()
    {
        float t = Mathf.SmoothStep(0f, 1f, openAmount);
        foreach (var leaf in leaves)
        {
            if (leaf == null || leaf.transform == null) continue;
            leaf.transform.localPosition = leaf.closedLocalPosition + leaf.openOffset * t;
        }
    }

    void SetCollidersEnabled(bool value)
    {
        foreach (var c in leafColliders)
            if (c != null) c.enabled = value;
    }
}
