using UnityEngine;

// Optional slow spin and bob for a pickup's visual. Switched on or off per PickupDefinition (Idle Spin Bob).
public class PickupIdle : MonoBehaviour
{
    [Tooltip("What moves: the pickup's 'Visual' child. Filled in by the pickup.")]
    public Transform target;
    [Tooltip("Degrees per second.")]
    public float spinSpeed = 45f;
    [Tooltip("Bob height (metres, each way).")]
    public float bobHeight = 0.06f;
    [Tooltip("Bobs per second.")]
    public float bobSpeed = 0.5f;

    Transform captured;
    Vector3 basePosition;
    float phase;

    void OnEnable()
    {
        // different pickups do not bob in step
        phase = Random.value * Mathf.PI * 2f;
    }

    void Update()
    {
        if (target == null) return;
        if (captured != target)
        {
            captured = target;
            basePosition = target.localPosition;
        }
        target.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        target.localPosition = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f + phase) * bobHeight);
    }
}
