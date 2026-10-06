using UnityEngine;

// Floating icon above a pickup: always faces the camera and bobs gently. Added by the pickup when its definition has an icon.
public class PickupIcon : MonoBehaviour
{
    [Tooltip("Bob height (metres, each way).")]
    public float bobHeight = 0.05f;
    [Tooltip("Bobs per second.")]
    public float bobSpeed = 0.6f;

    Vector3 basePosition;
    Camera cam;

    void Start()
    {
        basePosition = transform.localPosition;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
        transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobHeight);
    }
}
