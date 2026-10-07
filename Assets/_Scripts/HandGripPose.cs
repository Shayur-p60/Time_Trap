using System;
using UnityEngine;

// Goes on the arms' Animator object. Holds the right hand's fingers in a fixed grip, whatever the body animation
// does with them, so the hand closes around the flashlight.
//
// The grip is set with the humanoid finger muscles (curl and spread per finger). The muscle values are turned into
// finger-bone rotations once (and again whenever a value is changed in the Inspector); after that the rotations are
// simply written to the finger bones every frame, after the Animator has run. Only the finger bones are touched.
[DefaultExecutionOrder(60)]
[RequireComponent(typeof(Animator))]
public class HandGripPose : MonoBehaviour
{
    [Serializable]
    public class Finger
    {
        [Tooltip("Curl of the knuckle, middle and tip joints: 1 = straight, -1 = fully curled.")]
        [Range(-1f, 1f)] public float curl1 = -0.6f;
        [Range(-1f, 1f)] public float curl2 = -0.7f;
        [Range(-1f, 1f)] public float curl3 = -0.7f;
        [Tooltip("Sideways spread: 0 = neutral.")]
        [Range(-1f, 1f)] public float spread;
    }

    [Header("Right hand grip (humanoid finger muscles)")]
    public Finger thumb = new Finger { curl1 = -0.2f, curl2 = -0.3f, curl3 = -0.3f, spread = -0.3f };
    public Finger index = new Finger();
    public Finger middle = new Finger();
    public Finger ring = new Finger();
    public Finger little = new Finger();

    [Tooltip("0 = leave the animation's fingers alone, 1 = full grip.")]
    [Range(0f, 1f)] public float weight = 1f;

    static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
    static readonly HumanBodyBones[] FingerBones =
    {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };

    Animator animator;
    Transform[] bones;
    Quaternion[] gripRotations;
    bool built;
    bool rebuild = true;

    // How many finger bones the avatar has mapped (15 when every joint is there).
    public int MappedFingerBones { get; private set; }
    public bool Built => built;

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void OnValidate()
    {
        rebuild = true;
    }

    // After the Animator (and the hand IK) has posed the arm this frame.
    void LateUpdate()
    {
        if (animator == null || !animator.isHuman) return;
        if (rebuild) Build();
        if (!built || weight <= 0f) return;

        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            bones[i].localRotation = weight >= 1f ? gripRotations[i] : Quaternion.Slerp(bones[i].localRotation, gripRotations[i], weight);
        }
    }

    // Works out the finger-bone rotations for the grip: poses the rig once through the humanoid muscles, reads the
    // finger bones, then puts every bone (and the rig itself) back exactly as it was.
    void Build()
    {
        rebuild = false;
        built = false;

        bones = new Transform[FingerBones.Length];
        gripRotations = new Quaternion[FingerBones.Length];
        MappedFingerBones = 0;
        for (int i = 0; i < FingerBones.Length; i++)
        {
            bones[i] = animator.GetBoneTransform(FingerBones[i]);
            if (bones[i] != null) MappedFingerBones++;
        }
        if (MappedFingerBones == 0 || animator.avatar == null) return;

        // remember everything SetHumanPose is going to move
        Transform root = transform;
        Vector3 rootPosition = root.localPosition;
        Quaternion rootRotation = root.localRotation;
        var all = new Transform[(int)HumanBodyBones.LastBone];
        var positions = new Vector3[all.Length];
        var rotations = new Quaternion[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            all[i] = animator.GetBoneTransform((HumanBodyBones)i);
            if (all[i] == null) continue;
            positions[i] = all[i].localPosition;
            rotations[i] = all[i].localRotation;
        }

        var handler = new HumanPoseHandler(animator.avatar, root);
        var pose = new HumanPose();
        handler.GetHumanPose(ref pose);

        Finger[] fingers = { thumb, index, middle, ring, little };
        string[] muscleNames = HumanTrait.MuscleName;
        for (int f = 0; f < fingers.Length; f++)
        {
            SetMuscle(ref pose, muscleNames, "Right " + FingerNames[f] + " 1 Stretched", fingers[f].curl1);
            SetMuscle(ref pose, muscleNames, "Right " + FingerNames[f] + " 2 Stretched", fingers[f].curl2);
            SetMuscle(ref pose, muscleNames, "Right " + FingerNames[f] + " 3 Stretched", fingers[f].curl3);
            SetMuscle(ref pose, muscleNames, "Right " + FingerNames[f] + " Spread", fingers[f].spread);
        }
        handler.SetHumanPose(ref pose);

        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) gripRotations[i] = bones[i].localRotation;

        // put the whole rig back
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            all[i].localPosition = positions[i];
            all[i].localRotation = rotations[i];
        }
        root.localPosition = rootPosition;
        root.localRotation = rootRotation;
        handler.Dispose();

        built = true;
    }

    static void SetMuscle(ref HumanPose pose, string[] names, string name, float value)
    {
        int i = Array.IndexOf(names, name);
        if (i >= 0 && i < pose.muscles.Length) pose.muscles[i] = value;
    }
}
