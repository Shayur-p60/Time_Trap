using UnityEngine;

// The seven states of Birkin.controller. The state names are these names.
public enum BirkinAnim { Idle, Talk1, Talk2, HeadTap, Sad, Point, PortalGesture }

// Dr Birkin's body in the intro cutscene. Goes on the DR_Birkin object, beside its Animator (Humanoid avatar,
// Birkin.controller).
//
//   Animation  Play cross-fades to one of the seven states over Crossfade seconds. The cutscene's Timelines call it
//              (BirkinAnimTrack). The one-shot states go back to Idle by themselves; the talk loops are sent back by
//              the director when a line has finished typing.
//   Spine      the model is a Rigify rig, exported with its control bones. The Humanoid avatar drives the control
//              chain (torso, spine_fk, ORG-spine), but the mesh is skinned to the DEF-spine bones, which hang on a
//              separate branch and would stay behind. After every animation update each DEF-spine bone is put on its
//              ORG-spine twin, so the torso and the head follow.
//   Look       the head (and a little of the chest) turns to follow Look Target, through the Animator's look-at IK.
[RequireComponent(typeof(Animator))]
public class BirkinActor : MonoBehaviour
{
    [Tooltip("Seconds every change of animation is blended over.")]
    public float crossfade = 0.3f;
    [Tooltip("A clip shorter than this is treated as missing (a bad download), and Fallback plays instead.")]
    public float minClipLength = 0.5f;
    public BirkinAnim fallback = BirkinAnim.Talk2;

    [Header("Spine (found by name when empty)")]
    [Tooltip("ORG-spine ... ORG-spine.006: the bones the avatar moves.")]
    public Transform[] spineSources = new Transform[0];
    [Tooltip("DEF-spine ... DEF-spine.006: the bones the mesh is skinned to. Same order.")]
    public Transform[] spineTargets = new Transform[0];

    [Header("Look")]
    public Transform lookTarget;
    [Range(0f, 1f)] public float lookWeight;
    [Tooltip("How quickly the look turns on and off (per second).")]
    public float lookEase = 2f;
    [Range(0f, 1f)] public float bodyWeight = 0.12f;
    [Range(0f, 1f)] public float headWeight = 0.9f;

    public BirkinAnim Current { get; private set; } = BirkinAnim.Idle;
    public bool IsTalkLoop => Current == BirkinAnim.Talk1 || Current == BirkinAnim.Talk2;
    // 0-1: how much of the look is applied right now.
    public float LookBlend { get; private set; }
    // How many times Play has changed the animation (for tests).
    public int PlayCount { get; private set; }

    static readonly string[] SpineNames = { "spine", "spine.001", "spine.002", "spine.003", "spine.004", "spine.005", "spine.006" };

    Animator animator;
    Matrix4x4[] spineOffsets = new Matrix4x4[0];

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        if (spineSources.Length == 0 || spineTargets.Length != spineSources.Length) FindSpine();

        // how each DEF bone sits on its ORG twin in the rest pose (they coincide, but this keeps any offset)
        spineOffsets = new Matrix4x4[spineSources.Length];
        for (int i = 0; i < spineSources.Length; i++)
        {
            if (spineSources[i] == null || spineTargets[i] == null) continue;
            spineOffsets[i] = spineSources[i].worldToLocalMatrix * spineTargets[i].localToWorldMatrix;
        }
    }

    public void FindSpine()
    {
        spineSources = new Transform[SpineNames.Length];
        spineTargets = new Transform[SpineNames.Length];
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            for (int i = 0; i < SpineNames.Length; i++)
            {
                if (t.name == "ORG-" + SpineNames[i]) spineSources[i] = t;
                else if (t.name == "DEF-" + SpineNames[i]) spineTargets[i] = t;
            }
        }
    }

    // Cross-fades to an animation. Instant: no blend (he has just been moved somewhere else).
    public void Play(BirkinAnim anim, bool instant = false)
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (!HasClip(anim)) anim = fallback;
        Current = anim;
        PlayCount++;
        if (!isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
        if (instant) animator.Play(anim.ToString(), 0, 0f);
        else animator.CrossFadeInFixedTime(anim.ToString(), Mathf.Max(0f, crossfade), 0);
    }

    // The controller has a usable clip for this state.
    public bool HasClip(BirkinAnim anim)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return true;
        string clipName = "Birkin_" + anim;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            if (clip != null && clip.name == clipName) return clip.length >= minClipLength;
        return false;
    }

    // Stands him at a point, turned to face another one.
    public void Place(Vector3 position, Vector3 faceTowards)
    {
        Vector3 forward = faceTowards - position;
        forward.y = 0f;
        Quaternion rotation = forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward) : transform.rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    void Update()
    {
        float target = lookTarget != null ? lookWeight : 0f;
        LookBlend = Mathf.MoveTowards(LookBlend, target, Mathf.Max(0.01f, lookEase) * Time.unscaledDeltaTime);
    }

    // The controller's layer has IK Pass on, so the Animator calls this.
    void OnAnimatorIK(int layer)
    {
        if (animator == null || lookTarget == null || LookBlend <= 0f)
        {
            if (animator != null) animator.SetLookAtWeight(0f);
            return;
        }
        animator.SetLookAtWeight(LookBlend, bodyWeight, headWeight, 0f, 0.6f);
        animator.SetLookAtPosition(lookTarget.position);
    }

    // After the Animator has written the control bones: bring the skinned spine along. Parents first.
    void LateUpdate()
    {
        for (int i = 0; i < spineSources.Length; i++)
        {
            Transform from = spineSources[i], to = spineTargets[i];
            if (from == null || to == null) continue;
            Matrix4x4 pose = from.localToWorldMatrix * spineOffsets[i];
            to.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }
    }
}
