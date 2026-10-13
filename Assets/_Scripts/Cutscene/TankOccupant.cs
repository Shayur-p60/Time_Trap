using UnityEngine;

// A body floating in one of the specimen tanks of the portal chamber (intro cutscene). Goes on a zombie body prefab
// placed at a TankOccupant point. It is scenery: no ZombieAI, senses, health, hitboxes or colliders, and nothing here
// touches the spawner, the run statistics or the achievements.
//   pose   the body's Animator plays State Name from Pose Time at Animator Speed: nearly frozen, drifting through its
//          idle very slowly.
//   float  the body bobs up and down around where it was placed, and turns a few degrees each way.
public class TankOccupant : MonoBehaviour
{
    [Header("Pose")]
    [Tooltip("Found on the children when empty.")]
    public Animator animator;
    [Tooltip("The Animator state to hold. Empty = the controller's default state.")]
    public string stateName = "";
    [Tooltip("Where in the state the pose is taken from (0-1).")]
    [Range(0f, 1f)] public float poseTime;
    [Tooltip("Animator speed: 0 holds the pose exactly, 0.05 lets it drift.")]
    public float animatorSpeed = 0.05f;

    [Header("Float")]
    [Tooltip("Metres up and down.")]
    public float bobAmount = 0.06f;
    [Tooltip("Bobs per second.")]
    public float bobSpeed = 0.22f;
    [Tooltip("Degrees it turns each way.")]
    public float turnAmount = 6f;
    [Tooltip("Where in the bob this body starts (0-1). Negative = random.")]
    public float phase = -1f;

    Vector3 restPosition;
    Quaternion restRotation;

    void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        if (phase < 0f) phase = Random.value;
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
    }

    void OnEnable()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        animator.applyRootMotion = false;
        animator.speed = animatorSpeed;
        if (string.IsNullOrEmpty(stateName))
        {
            animator.Play(0, 0, poseTime);
            return;
        }
        int state = Animator.StringToHash(stateName);
        if (animator.HasState(0, state)) animator.Play(state, 0, poseTime);
    }

    void Update()
    {
        float t = (Time.time * bobSpeed + phase) * Mathf.PI * 2f;
        transform.localPosition = restPosition + Vector3.up * (Mathf.Sin(t) * bobAmount);
        transform.localRotation = restRotation * Quaternion.Euler(0f, Mathf.Sin(t * 0.37f + phase * 5f) * turnAmount, 0f);
    }
}
