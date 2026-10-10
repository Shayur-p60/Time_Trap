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
    [SerializeField] private float moveSpeed = 5.5f;
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
    private float currentIdleWeight = 1f;
    private float currentWalkWeight = 0f;
    private float currentRunWeight = 0f;

    public float CurrentGroundSpeed { get; private set; }
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
        if (lockCursor)
        {
            SetCursorLocked(true);
        }

        InitializePlayables();
    }

    private void Update()
    {
        HandleCursorToggle();
        HandleMouseLook();
        HandleCrouch();
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
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

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
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.forward * vertical + transform.right * horizontal);
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        bool isCrouchHeld = Input.GetKey(crouchKey);
        bool isSprinting = !isCrouchHeld && Input.GetKey(sprintKey);
        float targetSpeed = isCrouchHeld ? crouchSpeed : (isSprinting ? sprintSpeed : moveSpeed);

        Vector3 horizontalMove = moveDirection * (targetSpeed * Time.deltaTime);
        Vector3 verticalMove = Vector3.up * (verticalVelocity * Time.deltaTime);

        characterController.Move(horizontalMove + verticalMove);

        // Update ground velocity for animation crossfading
        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);
        CurrentGroundSpeed = horizontalVelocity.magnitude;
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

        if (idleClip == null && walkClip == null && runClip == null)
        {
            return;
        }

        playableGraph = PlayableGraph.Create("PlayerMovementPlayableGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        mixerPlayable = AnimationMixerPlayable.Create(playableGraph, 3);

        if (idleClip != null)
        {
            var idlePlayable = AnimationClipPlayable.Create(playableGraph, idleClip);
            playableGraph.Connect(idlePlayable, 0, mixerPlayable, 0);
        }

        if (walkClip != null)
        {
            var walkPlayable = AnimationClipPlayable.Create(playableGraph, walkClip);
            playableGraph.Connect(walkPlayable, 0, mixerPlayable, 1);
        }

        if (runClip != null)
        {
            var runPlayable = AnimationClipPlayable.Create(playableGraph, runClip);
            playableGraph.Connect(runPlayable, 0, mixerPlayable, 2);
        }

        // Set initial weights
        currentIdleWeight = 1f;
        currentWalkWeight = 0f;
        currentRunWeight = 0f;
        mixerPlayable.SetInputWeight(0, 1f);
        mixerPlayable.SetInputWeight(1, 0f);
        mixerPlayable.SetInputWeight(2, 0f);

        var playableOutput = AnimationPlayableOutput.Create(playableGraph, "Animation", animator);
        playableOutput.SetSourcePlayable(mixerPlayable);

        playableGraph.Play();
    }

    private void UpdateAnimationBlend()
    {
        if (!playableGraph.IsValid()) return;

        float targetIdle = 0f;
        float targetWalk = 0f;
        float targetRun = 0f;

        // Calculate target weights based on ground speed
        if (CurrentGroundSpeed <= 0.1f)
        {
            targetIdle = 1f;
            targetWalk = 0f;
            targetRun = 0f;
        }
        else if (CurrentGroundSpeed <= moveSpeed)
        {
            float t = Mathf.Clamp01(CurrentGroundSpeed / Mathf.Max(moveSpeed, 0.01f));
            targetIdle = 1f - t;
            targetWalk = t;
            targetRun = 0f;
        }
        else
        {
            float speedRange = Mathf.Max(sprintSpeed - moveSpeed, 0.01f);
            float t = Mathf.Clamp01((CurrentGroundSpeed - moveSpeed) / speedRange);
            targetIdle = 0f;
            targetWalk = 1f - t;
            targetRun = t;
        }

        // Smoothly interpolate weights
        float step = animationCrossfadeSpeed * Time.deltaTime;
        currentIdleWeight = Mathf.MoveTowards(currentIdleWeight, targetIdle, step);
        currentWalkWeight = Mathf.MoveTowards(currentWalkWeight, targetWalk, step);
        currentRunWeight = Mathf.MoveTowards(currentRunWeight, targetRun, step);

        float totalWeight = currentIdleWeight + currentWalkWeight + currentRunWeight;
        if (totalWeight > 0.0001f)
        {
            mixerPlayable.SetInputWeight(0, currentIdleWeight / totalWeight);
            mixerPlayable.SetInputWeight(1, currentWalkWeight / totalWeight);
            mixerPlayable.SetInputWeight(2, currentRunWeight / totalWeight);
        }
    }
    #endregion
}
