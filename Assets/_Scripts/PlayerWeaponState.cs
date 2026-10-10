using UnityEngine;

// Goes on the FirstPersonController. Decides what the player's hands are doing and tells the other scripts:
//
//   Torch (default, and whenever the pistol is holstered)
//       The right hand holds the torch (flashlight idle animation); the left arm is hidden. Walk and sprint allowed.
//       When a sprint starts the Lower clip takes the arm out of view; when it stops the Raise clip brings it back.
//   Pistol
//       Both hands on the gun, pistol animation set, weapon light under the barrel. No sprint: walk speed only.
//   Drawing
//       The Lower clip takes the torch down, the torch and the gun swap out of view, then the gun comes up.
//   Holstering
//       The gun goes down, the gun and the torch swap out of view, then the Raise clip brings the torch up.
//   The torch's part of a draw or holster lasts as long as the Lower / Raise clip played at Transition Clip Speed,
//   the gun's part Gun Move Time. A sprint plays the clips at their normal speed.
//   No firing during either.
//
// Picking the pistol up draws it straight away (the normal draw). After that:
// Controls: Q draws or holsters (once the player has the pistol). Holding sprint while moving with the pistol out
// holsters it first, then the sprint starts. F (FlashlightController) toggles the light in every state, at once,
// and never moves the arm or holsters the gun; left click and R (Pistol) fire and reload while the pistol is out.
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
    public HandGripPose handGrip;

    [Header("Models")]
    [Tooltip("The torch in the right hand. Hidden while the gun is out.")]
    public GameObject torchModel;
    [Tooltip("WeaponModelSlot on the right hand bone. Whatever gun model sits under it is shown while the gun is out.")]
    public GameObject weaponModelSlot;
    [Tooltip("WeaponLightMount under the gun's barrel: the torch light sits here while the gun is out.")]
    public Transform weaponLightMount;

    [Header("Timing")]
    public KeyCode drawKey = KeyCode.Q;
    [Tooltip("Seconds the gun takes to come up once the torch is down (draw), and to go down before the torch comes up (holster). The torch's own lowering and raising take the length of the Lower and Raise clips.")]
    public float gunMoveTime = 0.2f;
    [Tooltip("Speed of the Lower and Raise clips during a draw or a holster (1 = as authored). Sprinting always plays them at normal speed.")]
    public float transitionClipSpeed = 1.6f;
    [Tooltip("How far the arm is lowered while sprinting (1 = the whole Lower clip).")]
    [Range(0f, 1f)] public float sprintLowerAmount = 1f;
    [Tooltip("Seconds the torch arm takes to go down or come up when the movement script has no flashlight clips.")]
    public float fallbackLowerTime = 0.2f;

    [Header("Grip")]
    [Tooltip("How much of Hand Grip Pose's fixed finger grip is used while the torch is held: 0 = the flashlight animation's own fingers. The gun always gets the full grip.")]
    [Range(0f, 1f)] public float torchGripWeight;

    public State Current { get; private set; } = State.Torch;
    // 0 at the start of a draw or holster, 1 at the end.
    public float TransitionProgress { get; private set; }
    // 0 = torch pose, 1 = pistol pose (animation set, light position). Changes while the gun is coming up or going down.
    public float PistolBlend { get; private set; }
    // 0 = hands up, 1 = whatever is in the hands is fully lowered (draw / holster, or sprinting).
    public float Lowered => GunVisible ? gunLower : torchLower;
    // 0 = torch arm up, 1 = at the end of the Lower clip.
    public float TorchLower => torchLower;
    // The gun model is the one showing (from the swap in a draw to the swap in a holster).
    public bool GunVisible { get; private set; }
    // The pistol is in the hands (drawn, or on its way in or out): crosshair and ammo are shown.
    public bool IsPistolOut => Current != State.Torch;
    // Fully drawn: the only time a shot or a reload is allowed.
    public bool CanFire => Current == State.Pistol;

    // Seconds the torch takes to go fully down / come fully up: the clip lengths.
    public float TorchLowerTime => movement != null && movement.HasFlashlightClips ? movement.FlashlightLowerLength : fallbackLowerTime;
    public float TorchRaiseTime => movement != null && movement.HasFlashlightClips ? movement.FlashlightRaiseLength : fallbackLowerTime;

    float torchLower;    // 0 = torch arm up, 1 = down
    bool torchLowering;  // on the way down or held down (Lower clip), as opposed to coming up (Raise clip)
    float gunLower;      // 0 = gun up, 1 = gun down out of view
    bool hadPistol;

    void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovementController>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (pistol == null) pistol = GetComponentInChildren<Pistol>(true);
        if (handIK == null) handIK = GetComponentInChildren<FlashlightHandIK>(true);
        if (beamPivot == null) beamPivot = GetComponentInChildren<FlashlightBeamPivot>(true);
        if (handGrip == null) handGrip = GetComponentInChildren<HandGripPose>(true);
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

        if (Input.GetKeyDown(drawKey) && !GameManager.InputBlocked) ToggleDraw();

        bool wantsSprint = movement != null && movement.WantsSprint;
        // sprint with the gun out: put it away first
        if (Current == State.Pistol && wantsSprint) Holster();
        // lost the pistol somehow while it was out: straight back to the torch, which comes up from below
        if (!hasPistol && Current != State.Torch)
        {
            if (GunVisible) SwapToTorch();
            Current = State.Torch;
        }

        float gunStep = Time.deltaTime / Mathf.Max(0.01f, gunMoveTime);
        switch (Current)
        {
            case State.Torch:
                // lowered while sprinting, up otherwise
                MoveTorch(movement != null && movement.IsSprinting ? sprintLowerAmount : 0f, 1f);
                break;

            case State.Drawing:
                if (!GunVisible)
                {
                    // torch down (from wherever it is: a draw out of a sprint has less far to go), then swap
                    MoveTorch(1f, transitionClipSpeed);
                    if (torchLower >= 1f)
                    {
                        GunVisible = true;
                        gunLower = 1f;
                    }
                }
                else
                {
                    gunLower = Mathf.MoveTowards(gunLower, 0f, gunStep);
                    if (gunLower <= 0f) Current = State.Pistol;
                }
                break;

            case State.Holstering:
                if (GunVisible)
                {
                    gunLower = Mathf.MoveTowards(gunLower, 1f, gunStep);
                    if (gunLower >= 1f) SwapToTorch();
                }
                else if (wantsSprint)
                {
                    // a holster that ends in a sprint stays down instead of coming up first
                    Current = State.Torch;
                }
                else
                {
                    MoveTorch(0f, transitionClipSpeed);
                    if (torchLower <= 0f) Current = State.Torch;
                }
                break;
        }

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
        Current = State.Drawing;
        return true;
    }

    public bool Holster()
    {
        if (Current != State.Pistol) return false;
        Current = State.Holstering;
        return true;
    }

    // Moves the torch arm towards a lowered amount at the speed of the clip that takes it there, times Speed.
    void MoveTorch(float target, float speed)
    {
        speed = Mathf.Max(0.01f, speed);
        if (target > torchLower)
        {
            torchLowering = true;
            torchLower = Mathf.Min(target, torchLower + speed * Time.deltaTime / Mathf.Max(0.01f, TorchLowerTime));
        }
        else if (target < torchLower)
        {
            torchLowering = false;
            torchLower = Mathf.Max(target, torchLower - speed * Time.deltaTime / Mathf.Max(0.01f, TorchRaiseTime));
        }
    }

    // The gun is down out of view: the torch takes its place, lowered, at the start of the Raise clip.
    void SwapToTorch()
    {
        GunVisible = false;
        gunLower = 0f;
        torchLower = 1f;
        torchLowering = false;
    }

    // Pushes the current state out to the movement, the hand IK, the grip, the beam and the models.
    void Apply()
    {
        float clipSpeed = Mathf.Max(0.01f, transitionClipSpeed);
        float lowerTime = TorchLowerTime / clipSpeed, raiseTime = TorchRaiseTime / clipSpeed, gunTime = Mathf.Max(0.01f, gunMoveTime);
        switch (Current)
        {
            case State.Torch:
            case State.Pistol:
                TransitionProgress = 1f;
                break;
            case State.Drawing:
                TransitionProgress = (GunVisible ? lowerTime + (1f - gunLower) * gunTime : torchLower * lowerTime) / (lowerTime + gunTime);
                break;
            case State.Holstering:
                TransitionProgress = (GunVisible ? gunLower * gunTime : gunTime + (1f - torchLower) * raiseTime) / (gunTime + raiseTime);
                break;
        }

        // the animation set and the light follow the gun up and down; the hands themselves are pinned to the gun by
        // the IK for as long as it shows
        float gunUp = GunVisible ? 1f - gunLower : 0f;
        PistolBlend = gunUp * gunUp * (3f - 2f * gunUp);

        bool clips = movement != null && movement.HasFlashlightClips;
        if (movement != null)
        {
            movement.SprintAllowed = Current == State.Torch;
            movement.PistolBlend = PistolBlend;
            movement.TorchLower = torchLower;
            movement.TorchLowering = torchLowering;
        }

        if (handIK != null)
        {
            handIK.PistolWeight = GunVisible ? 1f : 0f;
            handIK.LeftHandWeight = GunVisible ? 1f : 0f;
            // without the clips the IK drops the torch hand itself, the way it does the gun
            handIK.Lowered = GunVisible ? gunLower : clips ? 0f : torchLower;
            handIK.TorchLowered = clips ? torchLower : 0f;
            handIK.HideBones = !GunVisible;
        }

        if (handGrip != null) handGrip.Blend = GunVisible ? 1f : torchGripWeight;

        if (beamPivot != null)
        {
            beamPivot.mount = weaponLightMount;
            beamPivot.mountWeight = weaponLightMount != null ? PistolBlend : 0f;
        }

        if (torchModel != null && torchModel.activeSelf == GunVisible) torchModel.SetActive(!GunVisible);
        if (weaponModelSlot != null && weaponModelSlot.activeSelf != GunVisible) weaponModelSlot.SetActive(GunVisible);
    }
}
