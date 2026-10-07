using UnityEngine;

// Goes on the FirstPersonController. Decides what the player's hands are doing and tells the other scripts:
//
//   Torch (default, and whenever the pistol is holstered)
//       The right hand holds the torch; the left arm is hidden. Walk and sprint allowed.
//       While sprinting the arm is lowered out of view and comes back up when the sprint stops.
//   Pistol
//       Both hands on the gun, pistol animation set, weapon light under the barrel. No sprint: walk speed only.
//   Drawing / Holstering
//       About 0.35 s: the hands drop out of view, the torch and the gun swap at the lowest point, and they come
//       back up. No firing meanwhile.
//
// Picking the pistol up draws it straight away (the normal draw). After that:
// Controls: Q draws or holsters (once the player has the pistol). Holding sprint while moving with the pistol out
// holsters it first, then the sprint starts. F (FlashlightController) toggles the light in every state and never
// holsters the gun; left click and R (Pistol) fire and reload while the pistol is out.
[DefaultExecutionOrder(-10)] // before PlayerMovementController, so Sprint Allowed is right in the same frame
public class PlayerWeaponState : MonoBehaviour
{
    public enum State { Torch, Drawing, Pistol, Holstering }

    [Header("References (found on this object and its children when empty)")]
    public PlayerMovementController movement;
    public PlayerInventory inventory;
    public Pistol pistol;
    public FlashlightHandIK handIK;
    public FlashlightBeamPivot beamPivot;

    [Header("Models")]
    [Tooltip("The torch in the right hand. Hidden while the gun is out.")]
    public GameObject torchModel;
    [Tooltip("WeaponModelSlot on the right hand bone. Whatever gun model sits under it is shown while the gun is out.")]
    public GameObject weaponModelSlot;
    [Tooltip("WeaponLightMount under the gun's barrel: the torch light sits here while the gun is out.")]
    public Transform weaponLightMount;

    [Header("Timing")]
    public KeyCode drawKey = KeyCode.Q;
    [Tooltip("Seconds a draw or a holster takes (lower, swap, raise).")]
    public float drawTime = 0.35f;
    [Tooltip("Seconds the arm takes to drop out of view when a sprint starts, and to come back when it stops.")]
    public float sprintLowerTime = 0.2f;
    [Tooltip("How far the arm is lowered while sprinting (1 = fully, as in Lowered Offset on the hand IK).")]
    [Range(0f, 1f)] public float sprintLowerAmount = 1f;

    public State Current { get; private set; } = State.Torch;
    // 0 at the start of a draw or holster, 1 at the end.
    public float TransitionProgress { get; private set; }
    // 0 = torch pose, 1 = pistol pose (animation set, hand targets, light position).
    public float PistolBlend { get; private set; }
    // 0 = hands up, 1 = hands fully lowered (draw / holster dip, or sprinting).
    public float Lowered { get; private set; }
    // The gun model is the one showing (from the middle of a draw to the middle of a holster).
    public bool GunVisible { get; private set; }
    // The pistol is in the hands (drawn, or on its way in or out): crosshair and ammo are shown.
    public bool IsPistolOut => Current != State.Torch;
    // Fully drawn: the only time a shot or a reload is allowed.
    public bool CanFire => Current == State.Pistol;

    float sprintLower;
    bool hadPistol;

    void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovementController>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (pistol == null) pistol = GetComponentInChildren<Pistol>(true);
        if (handIK == null) handIK = GetComponentInChildren<FlashlightHandIK>(true);
        if (beamPivot == null) beamPivot = GetComponentInChildren<FlashlightBeamPivot>(true);
    }

    void Start()
    {
        hadPistol = inventory != null && inventory.hasPistol;
        Apply();
    }

    void Update()
    {
        bool hasPistol = inventory != null && inventory.hasPistol;

        // just picked the pistol up: draw it
        if (hasPistol && !hadPistol) Draw();
        hadPistol = hasPistol;

        if (Input.GetKeyDown(drawKey)) ToggleDraw();

        // sprint with the gun out: put it away first
        if (Current == State.Pistol && movement != null && movement.WantsSprint) Holster();
        // lost the pistol somehow while it was out
        if (!hasPistol && Current != State.Torch) SetState(State.Torch);

        // --- draw / holster progress
        if (Current == State.Drawing || Current == State.Holstering)
        {
            TransitionProgress += Time.deltaTime / Mathf.Max(0.01f, drawTime);
            if (TransitionProgress >= 1f) SetState(Current == State.Drawing ? State.Pistol : State.Torch);
        }

        // --- sprint lowering (torch state; a holster that ends in a sprint stays down instead of coming up first)
        bool sprinting = movement != null &&
                         ((Current == State.Torch && movement.IsSprinting) || (Current == State.Holstering && movement.WantsSprint));
        float lowerRate = Time.deltaTime / Mathf.Max(0.01f, sprintLowerTime);
        sprintLower = Mathf.MoveTowards(sprintLower, sprinting ? sprintLowerAmount : 0f, lowerRate);

        Apply();
    }

    // Q: draw when holstered, holster when drawn. Ignored without the pistol and while already drawing or holstering.
    public bool ToggleDraw()
    {
        return Current == State.Torch ? Draw() : Holster();
    }

    public bool Draw()
    {
        if (Current != State.Torch || inventory == null || !inventory.hasPistol) return false;
        SetState(State.Drawing);
        return true;
    }

    public bool Holster()
    {
        if (Current != State.Pistol) return false;
        SetState(State.Holstering);
        return true;
    }

    void SetState(State next)
    {
        Current = next;
        TransitionProgress = next == State.Drawing || next == State.Holstering ? 0f : 1f;
    }

    // Pushes the current state out to the movement, the hand IK, the beam and the models.
    void Apply()
    {
        float t = Mathf.Clamp01(TransitionProgress);
        float eased = t * t * (3f - 2f * t);
        float dip = 0f;

        switch (Current)
        {
            case State.Torch: PistolBlend = 0f; GunVisible = false; break;
            case State.Pistol: PistolBlend = 1f; GunVisible = true; break;
            case State.Drawing: PistolBlend = eased; GunVisible = t >= 0.5f; dip = Mathf.Sin(t * Mathf.PI); break;
            case State.Holstering: PistolBlend = 1f - eased; GunVisible = t < 0.5f; dip = Mathf.Sin(t * Mathf.PI); break;
        }
        Lowered = Mathf.Max(dip, sprintLower);

        if (movement != null)
        {
            movement.SprintAllowed = Current == State.Torch;
            movement.PistolBlend = PistolBlend;
        }

        if (handIK != null)
        {
            handIK.PistolWeight = PistolBlend;
            handIK.LeftHandWeight = GunVisible ? PistolBlend : 0f;
            handIK.Lowered = Lowered;
            handIK.HideBones = !GunVisible;
        }

        if (beamPivot != null)
        {
            beamPivot.mount = weaponLightMount;
            beamPivot.mountWeight = weaponLightMount != null ? PistolBlend : 0f;
        }

        if (torchModel != null && torchModel.activeSelf == GunVisible) torchModel.SetActive(!GunVisible);
        if (weaponModelSlot != null && weaponModelSlot.activeSelf != GunVisible) weaponModelSlot.SetActive(GunVisible);
    }
}
