using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;

/// <summary>
/// First-person player movement controller using CharacterController and Unity Playables API
/// for state-machine-free animation crossfading based on ground velocity.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovementController : MonoBehaviour
{
    [Header("Camera & Mouse Look")]
    [Tooltip("First-person camera transform to control vertical pitch.")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("Mouse look sensitivity multiplier.")]
    [SerializeField] private float mouseSensitivity = 2f;
    [Tooltip("Whether to lock and hide the cursor during gameplay.")]
    [SerializeField] private bool lockCursor = true;

    [Header("Movement")]
    [Tooltip("Walking speed in units per second.")]
    [SerializeField] private float moveSpeed = 3.5f;
    [Tooltip("Sprinting speed in units per second.")]
    [SerializeField] private float sprintSpeed = 6.0f;
    [Tooltip("Key to hold for sprinting.")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("CharacterController step offset. Set to 0.4 to smoothly traverse stairs.")]
    [SerializeField] private float stepOffset = 0.4f;

    [Header("Crouching")]
    [Tooltip("Key to hold for crouching.")]
    [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;
    [Tooltip("Target height of the CharacterController when crouching.")]
    [SerializeField] private float crouchHeight = 0.9f;
    [Tooltip("Movement speed while crouching.")]
    [SerializeField] private float crouchSpeed = 2.0f;
    [Tooltip("Speed of transition between standing and crouching.")]
    [SerializeField] private float crouchTransitionSpeed = 10f;

    [Header("Gravity & Physics")]
    [Tooltip("Downward gravitational acceleration (m/s^2).")]
    [SerializeField] private float gravity = -9.81f;
    [Tooltip("Slight downward velocity applied when grounded to stick to slopes and stairs.")]
    [SerializeField] private float groundedGravity = -2f;

    [Header("Animation (Playables API)")]
    [Tooltip("Target Animator component (if null, searches on this GameObject or children).")]
    [SerializeField] private Animator animator;
    [Tooltip("Animation clip played when stationary.")]
    [SerializeField] private AnimationClip idleClip;
    [Tooltip("Animation clip played when walking.")]
    [SerializeField] private AnimationClip walkClip;
    [Tooltip("Animation clip played when sprinting.")]
    [SerializeField] private AnimationClip runClip;
    [Tooltip("Speed of transition between animation states.")]
    [SerializeField] private float animationCrossfadeSpeed = 6f;

    [Header("Pistol animation (blended in by PlayerWeaponState)")]
    [Tooltip("Standing still with the pistol drawn.")]
    [SerializeField] private AnimationClip pistolIdleClip;
    [SerializeField] private AnimationClip pistolWalkForwardClip;
    [SerializeField] private AnimationClip pistolWalkBackwardClip;
    [SerializeField] private AnimationClip pistolStrafeLeftClip;
    [SerializeField] private AnimationClip pistolStrafeRightClip;

    [Header("Flashlight animation (torch state; driven by PlayerWeaponState)")]
    [Tooltip("Looping pose of the arm holding the torch. When set, it replaces idle / walk / run as the torch-state animation.")]
    [SerializeField] private AnimationClip flashlightIdleClip;
    [Tooltip("The torch arm going down out of view (sprint start, drawing the pistol). Plays once.")]
    [SerializeField] private AnimationClip flashlightLowerClip;
    [Tooltip("The torch arm coming back up (sprint stop, after holstering). Plays once.")]
    [SerializeField] private AnimationClip flashlightRaiseClip;

    // Components and state
    private CharacterController characterController;
    private float cameraPitch = 0f;
    private float verticalVelocity = 0f;

    // Default crouching reference values
    private float defaultControllerHeight = 2.0f;
    private Vector3 defaultControllerCenter = new Vector3(0f, 1f, 0f);
    private float defaultCameraLocalY = 0f;

    // Playables API state
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixerPlayable;

    // Mixer inputs: 0 idle, 1 walk, 2 run (torch set), then the pistol set: 3 idle, 4 forward, 5 backward, 6 left, 7 right,
    // then the flashlight set: 8 idle, 9 lower, 10 raise. The flashlight set shares one weight (kept in slot 8) and
    // only one of its three clips shows at a time; lower and raise do not play by themselves, they are set to the
    // time that matches Torch Lower.
    private const int ClipCount = 11;
    private const int PistolFirst = 3;
    private const int FlashlightFirst = 8;
    private const int FlashlightLower = 9;
    private const int FlashlightRaise = 10;
    private readonly float[] currentWeights = new float[ClipCount];
    private readonly float[] targetWeights = new float[ClipCount];

    // The clip playables, kept so a clip imported without Loop Time can be wrapped by hand.
    private readonly AnimationClipPlayable[] clipPlayables = new AnimationClipPlayable[ClipCount];
    private readonly AnimationClip[] playableClips = new AnimationClip[ClipCount];

    // Animator Controller parameters driven from the ground speed, when the controller has them.
    private bool hasSpeedParameter;
    private readonly System.Collections.Generic.List<int> movingParameters = new System.Collections.Generic.List<int>();
    private static readonly int SpeedParameter = Animator.StringToHash("Speed");

    public float CurrentGroundSpeed { get; private set; }
    /// <summary>Horizontal velocity relative to the way the player faces (x = right, z = forward), in m/s.</summary>
    public Vector3 LocalGroundVelocity { get; private set; }
    public float WalkSpeed => moveSpeed;
    public float SprintSpeed => sprintSpeed;

    /// <summary>False while the pistol is drawn: the sprint key is ignored and the player moves at walk speed.</summary>
    public bool SprintAllowed { get; set; } = true;
    /// <summary>True while the player is exhausted (PlayerStamina): the sprint key does nothing, so there is no sprint, no auto-holster and no lowered torch.</summary>
    public bool SprintBlocked { get; set; }
    /// <summary>The player is holding sprint and a movement key (and is not crouching), whether or not sprint is allowed.</summary>
    public bool WantsSprint { get; private set; }
    /// <summary>The player is sprinting right now.</summary>
    public bool IsSprinting => WantsSprint && SprintAllowed;

    /// <summary>0 = torch animation set (idle / walk / run), 1 = pistol set (idle / walk / strafe). Set by PlayerWeaponState.</summary>
    public float PistolBlend { get; set; }

    /// <summary>The flashlight idle, lower and raise clips are all assigned.</summary>
    public bool HasFlashlightClips => flashlightIdleClip != null && flashlightLowerClip != null && flashlightRaiseClip != null;
    /// <summary>Length of the Lower clip in seconds (0 without one).</summary>
    public float FlashlightLowerLength => flashlightLowerClip != null ? flashlightLowerClip.length : 0f;
    /// <summary>Length of the Raise clip in seconds (0 without one).</summary>
    public float FlashlightRaiseLength => flashlightRaiseClip != null ? flashlightRaiseClip.length : 0f;
    /// <summary>0 = torch arm up (idle clip), 1 = fully lowered (end of the Lower clip). Set by PlayerWeaponState.</summary>
    public float TorchLower { get; set; }
    /// <summary>True while the arm is going down or held down (Lower clip), false while it comes up (Raise clip). Set by PlayerWeaponState.</summary>
    public bool TorchLowering { get; set; }

    // For automated tests and scripted sequences: while Scripted Input is on, movement and sprint come from these
    // two values instead of the keyboard. Mouse look, crouch and everything else are untouched.
    [System.NonSerialized] public bool scriptedInput;
    [System.NonSerialized] public Vector2 scriptedMove;   // x = strafe, y = forward, each -1..1
    [System.NonSerialized] public bool scriptedSprint;
    private bool scriptedWalk;
    /// <summary>Settings: a multiplier on Mouse Sensitivity (1 = as set here).</summary>
    public float SensitivityMultiplier { get; set; } = 1f;
    /// <summary>Settings: moving the mouse up looks down.</summary>
    public bool InvertY { get; set; }
    /// <summary>Multiplier on walk, sprint and crouch speed (1 = normal). The Speed Boost power-up sets this.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>Forgets the speed built up while falling. Call after teleporting the player (respawn).</summary>
    public void ResetFall()
    {
        verticalVelocity = 0f;
    }
    public bool IsCrouching => Input.GetKey(crouchKey);

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.stepOffset = stepOffset;
            defaultControllerHeight = characterController.height;
            defaultControllerCenter = characterController.center;
        }

        // Cache initial camera pitch and position if cameraTransform is assigned
        if (cameraTransform != null)
        {
            defaultCameraLocalY = cameraTransform.localPosition.y;

            Vector3 euler = cameraTransform.localEulerAngles;
            cameraPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            cameraPitch = Mathf.Clamp(cameraPitch, -85f, 85f);
        }
    }

    private void Start()
    {
        // with a game manager the cursor is its business (free in the menu, locked while playing)
        if (lockCursor && GameManager.Instance == null)
        {
            SetCursorLocked(true);
        }

        InitializePlayables();
    }

    private void Update()
    {
        // the intro's arrival walks the player out of the portal with Scripted Input: the body moves, the mouse and
        // the crouch key do nothing
        scriptedWalk = scriptedInput && GameManager.InCutscene;

        // in the menu, paused or on an end screen: the view must not turn
        if (GameManager.InputBlocked && !scriptedWalk) return;

        // Esc belongs to the pause menu. Only a scene without a game manager keeps the old cursor toggle.
        if (GameManager.Instance == null) HandleCursorToggle();
        if (!scriptedWalk)
        {
            HandleMouseLook();
            HandleCrouch();
        }
        HandleMovement();
        UpdateAnimationBlend();
    }

    private void OnEnable()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Play();
        }
    }

    private void OnDisable()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Stop();
        }
    }

    private void OnDestroy()
    {
        if (playableGraph.IsValid())
        {
            playableGraph.Destroy();
        }
    }

    #region Mouse Look
    private void HandleMouseLook()
    {
        float sensitivity = mouseSensitivity * SensitivityMultiplier;
        float mouseX = Input.GetAxis("Mouse X") * sensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivity * (InvertY ? -1f : 1f);

        // Rotate player body horizontally (yaw)
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera vertically (pitch) clamped between -85 and 85 degrees
        if (cameraTransform != null)
        {
            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, -85f, 85f);
            cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(false);
        }
        else if (Input.GetMouseButtonDown(0) && lockCursor && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    private void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
    #endregion

    #region Crouching
    private void HandleCrouch()
    {
        bool isCrouchHeld = Input.GetKey(crouchKey);

        float targetHeight = isCrouchHeld ? crouchHeight : defaultControllerHeight;
        float targetCameraY = isCrouchHeld ? (defaultCameraLocalY * 0.5f) : defaultCameraLocalY;
        float t = crouchTransitionSpeed * Time.deltaTime;

        // Smoothly interpolate CharacterController height and center
        if (characterController != null)
        {
            float currentHeight = characterController.height;
            float newHeight = Mathf.Lerp(currentHeight, targetHeight, t);
            characterController.height = newHeight;

            float heightRatio = defaultControllerHeight > 0.001f ? (newHeight / defaultControllerHeight) : 1f;
            characterController.center = new Vector3(
                defaultControllerCenter.x,
                defaultControllerCenter.y * heightRatio,
                defaultControllerCenter.z
            );
        }

        // Smoothly interpolate camera localPosition.y
        if (cameraTransform != null)
        {
            Vector3 camPos = cameraTransform.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, targetCameraY, t);
            cameraTransform.localPosition = camPos;
        }
    }
    #endregion

    #region Movement
    private void HandleMovement()
    {
        // Grounded check and gravity handling
        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = groundedGravity;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        // WASD Input
        float horizontal = scriptedInput ? Mathf.Clamp(scriptedMove.x, -1f, 1f) : Input.GetAxisRaw("Horizontal");
        float vertical = scriptedInput ? Mathf.Clamp(scriptedMove.y, -1f, 1f) : Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.forward * vertical + transform.right * horizontal);
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        bool isCrouchHeld = !scriptedWalk && Input.GetKey(crouchKey);
        bool sprintHeld = scriptedInput ? scriptedSprint : Input.GetKey(sprintKey);
        WantsSprint = !isCrouchHeld && sprintHeld && !SprintBlocked && moveDirection.sqrMagnitude > 0.01f;
        bool isSprinting = WantsSprint && SprintAllowed;
        float targetSpeed = isCrouchHeld ? crouchSpeed : (isSprinting ? sprintSpeed : moveSpeed);
        targetSpeed *= SpeedMultiplier;

        Vector3 horizontalMove = moveDirection * (targetSpeed * Time.deltaTime);
        Vector3 verticalMove = Vector3.up * (verticalVelocity * Time.deltaTime);

        characterController.Move(horizontalMove + verticalMove);

        // Update ground velocity for animation crossfading
        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);
        CurrentGroundSpeed = horizontalVelocity.magnitude;
        LocalGroundVelocity = transform.InverseTransformDirection(horizontalVelocity);
    }
    #endregion

    #region Playables Animation Blending
    private void InitializePlayables()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        if (animator == null)
        {
            return;
        }

        CacheAnimatorParameters();

        if (idleClip == null && walkClip == null && runClip == null)
        {
            return;
        }

        playableGraph = PlayableGraph.Create("PlayerMovementPlayableGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        mixerPlayable = AnimationMixerPlayable.Create(playableGraph, ClipCount);

        AnimationClip[] clips =
        {
            idleClip, walkClip, runClip,
            pistolIdleClip, pistolWalkForwardClip, pistolWalkBackwardClip, pistolStrafeLeftClip, pistolStrafeRightClip,
            flashlightIdleClip, flashlightLowerClip, flashlightRaiseClip
        };
        for (int i = 0; i < ClipCount; i++)
        {
            if (clips[i] == null) continue;
            var clipPlayable = AnimationClipPlayable.Create(playableGraph, clips[i]);
            playableGraph.Connect(clipPlayable, 0, mixerPlayable, i);
            clipPlayables[i] = clipPlayable;
            playableClips[i] = clips[i];
            // lower and raise are held at the time PlayerWeaponState asks for instead of running by themselves
            if (i == FlashlightLower || i == FlashlightRaise) clipPlayable.SetSpeed(0);
        }

        // Set initial weights: idle only (the flashlight idle when there is one)
        int startClip = playableClips[FlashlightFirst] != null ? FlashlightFirst : 0;
        for (int i = 0; i < ClipCount; i++)
        {
            currentWeights[i] = i == startClip ? 1f : 0f;
            mixerPlayable.SetInputWeight(i, currentWeights[i]);
        }

        var playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
        playableOutput.SetSourcePlayable(mixerPlayable);

        playableGraph.Play();
    }

    // Finds the walk parameters the Animator Controller actually has: a float "Speed" and/or a bool
    // "isWalking" / "IsWalking" / "isMoving" / "IsMoving". Parameters it does not have are never set.
    private void CacheAnimatorParameters()
    {
        hasSpeedParameter = false;
        movingParameters.Clear();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (var parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == "Speed")
            {
                hasSpeedParameter = true;
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool &&
                     (parameter.name == "isWalking" || parameter.name == "IsWalking" || parameter.name == "isMoving" || parameter.name == "IsMoving"))
            {
                movingParameters.Add(parameter.nameHash);
            }
        }
    }

    // Each frame: the CharacterController's horizontal speed goes to the controller's walk parameters.
    private void DriveAnimatorParameters()
    {
        if (animator == null) return;
        if (hasSpeedParameter) animator.SetFloat(SpeedParameter, CurrentGroundSpeed);
        for (int i = 0; i < movingParameters.Count; i++)
        {
            animator.SetBool(movingParameters[i], CurrentGroundSpeed > 0.1f);
        }
    }

    // A clip imported without Loop Time (the run) stops on its last frame and stays there. Start it again instead.
    private void LoopNonLoopingClips()
    {
        for (int i = 0; i < clipPlayables.Length; i++)
        {
            if (i == FlashlightLower || i == FlashlightRaise) continue;
            if (playableClips[i] == null || playableClips[i].isLooping || !clipPlayables[i].IsValid()) continue;
            double length = playableClips[i].length;
            if (length > 0.01 && clipPlayables[i].GetTime() >= length)
            {
                clipPlayables[i].SetTime(clipPlayables[i].GetTime() % length);
            }
        }
    }

    private void UpdateAnimationBlend()
    {
        DriveAnimatorParameters();

        if (!playableGraph.IsValid()) return;

        LoopNonLoopingClips();
        int flashlightClip = ScrubFlashlightClips();

        float targetIdle = 0f;
        float targetWalk = 0f;
        float targetRun = 0f;

        // Calculate target weights based on ground speed
        if (CurrentGroundSpeed <= 0.1f)
        {
            targetIdle = 1f;
        }
        else if (CurrentGroundSpeed <= moveSpeed)
        {
            float t = Mathf.Clamp01(CurrentGroundSpeed / Mathf.Max(moveSpeed, 0.01f));
            targetIdle = 1f - t;
            targetWalk = t;
        }
        else
        {
            float speedRange = Mathf.Max(sprintSpeed - moveSpeed, 0.01f);
            float t = Mathf.Clamp01((CurrentGroundSpeed - moveSpeed) / speedRange);
            targetWalk = 1f - t;
            targetRun = t;
        }

        // The pistol set only takes part when its idle clip is there
        float pistol = playableClips[PistolFirst] != null ? Mathf.Clamp01(PistolBlend) : 0f;
        // With a flashlight idle the torch state shows the flashlight set instead of idle / walk / run
        float locomotion = flashlightClip >= 0 ? 0f : 1f - pistol;
        targetWeights[0] = targetIdle * locomotion;
        targetWeights[1] = targetWalk * locomotion;
        targetWeights[2] = targetRun * locomotion;
        targetWeights[FlashlightFirst] = flashlightClip >= 0 ? 1f - pistol : 0f;
        SetPistolTargets(pistol);

        // Smoothly interpolate weights
        float step = animationCrossfadeSpeed * Time.deltaTime;
        float totalWeight = 0f;
        for (int i = 0; i < ClipCount; i++)
        {
            if (playableClips[i] == null || i == FlashlightLower || i == FlashlightRaise) { currentWeights[i] = 0f; continue; }
            currentWeights[i] = Mathf.MoveTowards(currentWeights[i], targetWeights[i], step);
            totalWeight += currentWeights[i];
        }

        if (totalWeight > 0.0001f)
        {
            for (int i = 0; i < ClipCount; i++)
            {
                float weight = currentWeights[i];
                // the flashlight set's weight goes to whichever of its clips is showing; they meet at their ends, so no fade between them
                if (i >= FlashlightFirst) weight = i == flashlightClip ? currentWeights[FlashlightFirst] : 0f;
                mixerPlayable.SetInputWeight(i, weight / totalWeight);
            }
        }
    }

    // Puts the Lower and Raise clips at the time that matches Torch Lower and returns the mixer input of the
    // flashlight clip to show: the idle while the arm is up, Lower on the way down, Raise on the way up.
    // -1 without a flashlight idle clip.
    private int ScrubFlashlightClips()
    {
        if (playableClips[FlashlightFirst] == null) return -1;

        float lower = Mathf.Clamp01(TorchLower);
        if (playableClips[FlashlightLower] != null) clipPlayables[FlashlightLower].SetTime(lower * playableClips[FlashlightLower].length);
        if (playableClips[FlashlightRaise] != null) clipPlayables[FlashlightRaise].SetTime((1f - lower) * playableClips[FlashlightRaise].length);

        if (lower <= 0f) return FlashlightFirst;
        int moving = TorchLowering ? FlashlightLower : FlashlightRaise;
        return playableClips[moving] != null ? moving : FlashlightFirst;
    }

    // Pistol set: idle when still; forward / backward / strafe by the direction of travel relative to facing.
    // A direction whose clip is missing falls back to the pistol idle.
    private void SetPistolTargets(float pistol)
    {
        float moving = CurrentGroundSpeed <= 0.1f ? 0f : Mathf.Clamp01(CurrentGroundSpeed / Mathf.Max(moveSpeed, 0.01f));
        Vector3 local = LocalGroundVelocity;
        float forward = Mathf.Max(0f, local.z);
        float backward = Mathf.Max(0f, -local.z);
        float left = Mathf.Max(0f, -local.x);
        float right = Mathf.Max(0f, local.x);
        float sum = forward + backward + left + right;

        float idle = 1f - moving;
        float[] directions = { forward, backward, left, right };
        for (int i = 0; i < 4; i++)
        {
            float weight = sum > 0.0001f ? moving * directions[i] / sum : 0f;
            if (playableClips[PistolFirst + 1 + i] == null)
            {
                idle += weight;
                weight = 0f;
            }
            targetWeights[PistolFirst + 1 + i] = weight * pistol;
        }
        targetWeights[PistolFirst] = idle * pistol;
    }
    #endregion
}
