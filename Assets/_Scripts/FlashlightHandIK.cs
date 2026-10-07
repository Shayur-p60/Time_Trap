using UnityEngine;

// Goes on the arms' Animator object. Uses humanoid IK to hold the hands on targets under the camera, so the hand
// and whatever it holds show in the view whatever the body animation is doing.
//
// Torch (Pistol Weight 0):
//   * The right hand goes to Target; the target's rotation follows the beam pivot, so torch and beam turn together.
//   * The hand is turned so that the flashlight's lens (the Lens marker) points along the target's forward, whatever
//     way the model sits in the hand: the difference is measured every frame and taken out.
//   * Bones listed under Hidden Bones are scaled to zero (the left arm), so only the right arm shows.
// Pistol (Pistol Weight 1):
//   * The right hand goes to Pistol Target instead, turned so the gun's barrel (Pistol Aim, the MuzzlePoint) points
//     along that target's forward.
//   * The left hand goes to Left Hand Target and the hidden bones are shown again. That marker sits under Pistol
//     Target, beside where the grip ends up. It must not be a child of the right hand bone: during the IK pass the
//     bones still hold the animation's pose, not last frame's IK result, so a marker on the hand reads the wrong place.
// Lowered (0..1) drops the hands down and tips them forward: used while sprinting and during draw / holster.
// PlayerWeaponState drives Pistol Weight, Left Hand Weight, Lowered and Hide Bones.
// Needs "IK Pass" ticked on the Animator Controller layer, or OnAnimatorIK is never called.
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(Animator))]
public class FlashlightHandIK : MonoBehaviour
{
    [Header("Hand target")]
    [Tooltip("Where the right hand goes: RightHandHoldTarget under the camera.")]
    public Transform target;
    [Tooltip("The target takes this object's rotation each frame (FlashlightBeamPivot), so hand and beam move together.")]
    public Transform followRotation;
    [Range(0f, 1f)] public float weight = 1f;

    [Header("Flashlight alignment")]
    [Tooltip("A transform on the flashlight model whose forward (blue axis) is the way the lens points.")]
    public Transform lens;
    [Tooltip("Turn the hand so the lens points along the target's forward. Off: the hand simply takes the target's rotation.")]
    public bool alignLens = true;
    [Tooltip("Extra twist of the hand on top of the alignment (degrees).")]
    public Vector3 extraRotation;

    [Header("Elbow (optional)")]
    [Tooltip("Pulls the elbow towards this point, to keep it down and out of the view.")]
    public Transform elbowHint;
    [Range(0f, 1f)] public float elbowHintWeight = 0.6f;

    [Header("Hidden bones")]
    [Tooltip("Scaled to zero while Hide Bones is on (the left upper arm in the torch state).")]
    public Transform[] hiddenBones = new Transform[0];

    [Header("Pistol hold")]
    [Tooltip("Where the right hand goes while the pistol is drawn: PistolHoldTarget under the camera.")]
    public Transform pistolTarget;
    [Tooltip("A transform on the gun whose forward (blue axis) is the barrel: MuzzlePoint.")]
    public Transform pistolAim;
    [Tooltip("Optional: where the right elbow is pulled while the pistol is drawn. Left empty, the torch elbow hint is used.")]
    public Transform pistolElbowHint;
    [Tooltip("Where the left hand goes while the pistol is drawn: a marker under Pistol Target, beside the gun's grip (not on the hand bone).")]
    public Transform leftHandTarget;
    [Tooltip("Twist of the left hand relative to its marker (degrees).")]
    public Vector3 leftHandRotation;
    [Tooltip("Optional: pulls the left elbow towards this point.")]
    public Transform leftElbowHint;
    [Range(0f, 1f)] public float leftElbowHintWeight = 0.6f;

    [Header("Lowering (sprint, draw, holster)")]
    [Tooltip("How far the right hand drops when fully lowered, in view space (x right, y up, z forward; metres).")]
    public Vector3 loweredOffset = new Vector3(0.04f, -0.35f, -0.1f);
    [Tooltip("How far the hand tips forward when fully lowered (degrees).")]
    public float loweredTilt = 35f;

    // Set by PlayerWeaponState every frame.
    public float PistolWeight { get; set; }
    public float LeftHandWeight { get; set; }
    public float Lowered { get; set; }
    public bool HideBones { get; set; } = true;

    Animator animator;
    Transform handBone;
    Transform leftHandBone;
    Vector3[] hiddenBoneScales;

    // marker rotation = commanded hand rotation * this (constant for a given model in the hand)
    Quaternion lensFromGoal = Quaternion.identity;
    Quaternion aimFromGoal = Quaternion.identity;
    bool lensCalibrated, aimCalibrated;

    Quaternion lastGoal = Quaternion.identity; // the rotation asked for, before Extra Rotation
    Quaternion rigRotationAtIK = Quaternion.identity;
    bool goalSet;

    // How many times Unity has asked for IK. Stays 0 if no layer has IK Pass.
    public int IKCalls { get; private set; }
    // Distance between the hand bone and the torch target right now (metres).
    public float HandDistanceToTarget => handBone != null && target != null ? Vector3.Distance(handBone.position, target.position) : 0f;
    // Distance between the hand bone and the pistol target right now (metres).
    public float HandDistanceToPistolTarget => handBone != null && pistolTarget != null ? Vector3.Distance(handBone.position, pistolTarget.position) : 0f;
    // Distance between the left hand bone and its marker on the gun right now (metres).
    public float LeftHandDistanceToTarget => leftHandBone != null && leftHandTarget != null ? Vector3.Distance(leftHandBone.position, leftHandTarget.position) : 0f;
    // Angle between the lens direction and the target's forward right now (degrees).
    public float LensAngleToTarget => lens != null && target != null ? Vector3.Angle(lens.forward, target.forward) : 0f;
    // Angle between the gun barrel and the pistol target's forward right now (degrees).
    public float AimAngleToTarget => pistolAim != null && pistolTarget != null ? Vector3.Angle(pistolAim.forward, pistolTarget.forward) : 0f;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator != null && animator.isHuman)
        {
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }

        hiddenBoneScales = new Vector3[hiddenBones.Length];
        for (int i = 0; i < hiddenBones.Length; i++)
            hiddenBoneScales[i] = hiddenBones[i] != null && hiddenBones[i].localScale != Vector3.zero ? hiddenBones[i].localScale : Vector3.one;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || target == null) return;
        IKCalls++;

        float pistol = pistolTarget != null ? Mathf.Clamp01(PistolWeight) : 0f;

        // --- right hand: torch goal, pistol goal, and the blend between them
        Quaternion goal = target.rotation;
        if (alignLens && lens != null && lensCalibrated) goal = target.rotation * Quaternion.Inverse(lensFromGoal);
        Vector3 position = target.position;

        if (pistol > 0f)
        {
            Quaternion pistolGoal = pistolTarget.rotation;
            if (pistolAim != null && aimCalibrated) pistolGoal = pistolTarget.rotation * Quaternion.Inverse(aimFromGoal);
            goal = Quaternion.Slerp(goal, pistolGoal, pistol);
            position = Vector3.Lerp(position, pistolTarget.position, pistol);
        }

        // --- lowered: down and tipped forward, in view space
        float lowered = Mathf.Clamp01(Lowered);
        Transform view = target.parent;
        if (lowered > 0f && view != null)
        {
            position += view.TransformVector(loweredOffset) * lowered;
            goal = Quaternion.AngleAxis(loweredTilt * lowered, view.right) * goal;
        }

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, position);
        animator.SetIKRotation(AvatarIKGoal.RightHand, goal * Quaternion.Euler(extraRotation));

        Transform hint = elbowHint;
        Vector3 hintPosition = hint != null ? hint.position : Vector3.zero;
        if (pistolElbowHint != null && pistol > 0f)
        {
            hintPosition = hint != null ? Vector3.Lerp(hintPosition, pistolElbowHint.position, pistol) : pistolElbowHint.position;
            hint = pistolElbowHint;
        }
        if (hint != null)
        {
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, elbowHintWeight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, hintPosition);
        }

        // --- left hand: beside the gun's grip, lowered together with the right hand
        float left = leftHandTarget != null ? Mathf.Clamp01(LeftHandWeight) * weight : 0f;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, left);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, left);
        if (left > 0f)
        {
            Vector3 leftPosition = leftHandTarget.position;
            Quaternion leftGoal = leftHandTarget.rotation * Quaternion.Euler(leftHandRotation);
            if (lowered > 0f && view != null)
            {
                leftPosition += view.TransformVector(loweredOffset) * lowered;
                leftGoal = Quaternion.AngleAxis(loweredTilt * lowered, view.right) * leftGoal;
            }
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftPosition);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftGoal);
            if (leftElbowHint != null)
            {
                animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, leftElbowHintWeight * left);
                animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftElbowHint.position);
            }
        }

        lastGoal = goal;
        rigRotationAtIK = transform.rotation;
        goalSet = true;
    }

    // After the Animator (and its IK) has posed the arms, and after the beam pivot has turned.
    void LateUpdate()
    {
        for (int i = 0; i < hiddenBones.Length; i++)
            if (hiddenBones[i] != null) hiddenBones[i].localScale = HideBones ? Vector3.zero : hiddenBoneScales[i];

        // measure how the lens and the gun barrel sit relative to the rotation the hand was asked for (constant for a
        // given model), ignoring anything that turned the whole rig after the IK ran (ArmSway)
        if (goalSet && weight >= 1f && extraRotation == Vector3.zero)
        {
            Quaternion undoRig = rigRotationAtIK * Quaternion.Inverse(transform.rotation);
            Quaternion inverseGoal = Quaternion.Inverse(lastGoal);
            if (alignLens && lens != null)
            {
                Quaternion measured = inverseGoal * (undoRig * lens.rotation);
                lensFromGoal = lensCalibrated ? Quaternion.Slerp(lensFromGoal, measured, 0.5f) : measured;
                lensCalibrated = true;
            }
            if (pistolAim != null)
            {
                Quaternion measured = inverseGoal * (undoRig * pistolAim.rotation);
                aimFromGoal = aimCalibrated ? Quaternion.Slerp(aimFromGoal, measured, 0.5f) : measured;
                aimCalibrated = true;
            }
        }
        goalSet = false;

        if (target != null && followRotation != null) target.rotation = followRotation.rotation;
    }
}
