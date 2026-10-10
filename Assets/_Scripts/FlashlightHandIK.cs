using UnityEngine;

// Goes on the arms' Animator object. Uses humanoid IK on the hands, on top of the body animation.
//
// Torch (Pistol Weight 0):
//   * The flashlight animation (idle, lower, raise) poses the arm. The IK is only a light touch on top of it:
//     Torch View Offset shifts the animated hand as a whole (the animation holds it beside the head, out of the
//     frame), Torch Position Weight pulls it a little towards Target, and Torch Rotation Weight turns the hand a
//     little so the lens (the Lens marker) points along the target's forward. The target's rotation follows the
//     beam pivot, so the lens leans the way the beam points. The animation's own movement of the hand stays.
//   * Both fade out as the arm is lowered (Torch Lowered), so the Lower and Raise clips play untouched.
//   * Bones listed under Hidden Bones are scaled to zero (the left arm), so only the right arm shows.
// Pistol (Pistol Weight 1):
//   * The right hand goes to Pistol Target instead, turned so the gun's barrel (Pistol Aim, the MuzzlePoint) points
//     along that target's forward.
//   * The left hand goes to Left Hand Target and the hidden bones are shown again. That marker sits under Pistol
//     Target, beside where the grip ends up. It must not be a child of the right hand bone: during the IK pass the
//     bones still hold the animation's pose, not last frame's IK result, so a marker on the hand reads the wrong place.
// Lowered (0..1) drops the hands down and tips them forward: used to bring the gun up and down in draw / holster.
// PlayerWeaponState drives Pistol Weight, Left Hand Weight, Lowered, Torch Lowered and Hide Bones.
// Needs "IK Pass" ticked on the Animator Controller layer, or OnAnimatorIK is never called.
[DefaultExecutionOrder(50)]
[RequireComponent(typeof(Animator))]
public class FlashlightHandIK : MonoBehaviour
{
    [Header("Hand target")]
    [Tooltip("Where the right hand is pulled towards: RightHandHoldTarget under the camera.")]
    public Transform target;
    [Tooltip("The target takes this object's rotation each frame (FlashlightBeamPivot), so hand and beam move together.")]
    public Transform followRotation;
    [Range(0f, 1f)] public float weight = 1f;

    [Header("Torch: light touch on top of the flashlight animation")]
    [Tooltip("Added to where the animation puts the hand, in view space (x right, y up, z forward; metres), to frame the torch in the view.")]
    public Vector3 torchViewOffset;
    [Tooltip("How strongly the torch hand is then pulled to Target: 0 = the animation's hand position plus the offset, 1 = pinned to the target.")]
    [Range(0f, 1f)] public float torchPositionWeight = 0.2f;
    [Tooltip("How much of the lens's aiming error is taken out: 0 = the animation's own hand rotation, 1 = the lens always points exactly along the target's forward.")]
    [Range(0f, 1f)] public float torchRotationWeight = 0.6f;
    [Tooltip("How strongly the elbow is pulled to the Elbow Hint while the torch is held.")]
    [Range(0f, 1f)] public float torchElbowHintWeight = 0.3f;

    [Header("Flashlight alignment")]
    [Tooltip("A transform on the flashlight model whose forward (blue axis) is the way the lens points.")]
    public Transform lens;
    [Tooltip("Aim the hand by the lens: it is turned so the lens points along the target's forward. Off: the hand is turned towards the target's rotation.")]
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
    // 0 = torch arm up, 1 = lowered by the Lower clip: fades the torch touch out.
    public float TorchLowered { get; set; }
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
    float lastRotationWeight;                  // and how strongly: the measuring below needs the hand to have reached it
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

        // --- right hand, torch: start from what the animation does with the hand. The hand is shifted into the
        // frame and pulled a little to the target; its rotation is kept, and the lens is swung part of the way onto
        // the target's forward (the shortest turn, so the animation's roll of the wrist is kept). All of it fades
        // out as the Lower clip takes the arm down.
        float raised = 1f - Mathf.Clamp01(TorchLowered);
        float positionWeight = raised;
        float rotationWeight = raised;
        float elbowWeight = torchElbowHintWeight * raised;
        Transform view = target.parent;
        Vector3 position = animator.GetIKPosition(AvatarIKGoal.RightHand);
        if (view != null) position += view.TransformVector(torchViewOffset);
        position = Vector3.Lerp(position, target.position, torchPositionWeight);
        Quaternion animated = animator.GetIKRotation(AvatarIKGoal.RightHand);
        Quaternion goal = animated;
        if (alignLens && lens != null)
        {
            if (lensCalibrated)
            {
                Quaternion aimed = Quaternion.FromToRotation(animated * lensFromGoal * Vector3.forward, target.forward) * animated;
                goal = Quaternion.Slerp(animated, aimed, torchRotationWeight);
            }
        }
        else goal = Quaternion.Slerp(animated, target.rotation, torchRotationWeight);

        // --- pistol: both fully on the pistol target
        if (pistol > 0f)
        {
            Quaternion pistolGoal = pistolTarget.rotation;
            if (pistolAim != null && aimCalibrated) pistolGoal = pistolTarget.rotation * Quaternion.Inverse(aimFromGoal);
            goal = Quaternion.Slerp(goal, pistolGoal, pistol);
            position = Vector3.Lerp(position, pistolTarget.position, pistol);
            positionWeight = Mathf.Lerp(positionWeight, 1f, pistol);
            rotationWeight = Mathf.Lerp(rotationWeight, 1f, pistol);
            elbowWeight = Mathf.Lerp(elbowWeight, elbowHintWeight, pistol);
        }

        // --- lowered: down and tipped forward, in view space
        float lowered = Mathf.Clamp01(Lowered);
        if (lowered > 0f && view != null)
        {
            position += view.TransformVector(loweredOffset) * lowered;
            goal = Quaternion.AngleAxis(loweredTilt * lowered, view.right) * goal;
        }

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight * positionWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight * rotationWeight);
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
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, elbowWeight * weight);
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
        lastRotationWeight = weight * rotationWeight;
        rigRotationAtIK = transform.rotation;
        goalSet = true;
    }

    // After the Animator (and its IK) has posed the arms, and after the beam pivot has turned.
    void LateUpdate()
    {
        for (int i = 0; i < hiddenBones.Length; i++)
            if (hiddenBones[i] != null) hiddenBones[i].localScale = HideBones ? Vector3.zero : hiddenBoneScales[i];

        // measure how the lens and the gun barrel sit relative to the rotation the hand was asked for (constant for a
        // given model), ignoring anything that turned the whole rig after the IK ran (ArmSway). Only on frames where
        // the hand was turned all the way to that rotation (not while the Lower / Raise clips have the arm).
        if (goalSet && lastRotationWeight >= 1f && extraRotation == Vector3.zero)
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
