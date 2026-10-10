using System.Collections;
using UnityEngine;

// One of these goes on the control column inside each elevator car; the two are linked through Other Car.
// Using the column (with power) closes this car's door, fades the screen, moves the player to the same spot and
// facing relative to the other car's pivot, then opens the other car's door.
//
// Each car's door is a Door in External mode and is driven from here: with power on it opens while the player is
// near the car from outside (or standing in it) and closes when they leave. Without power it stays closed.
public class ElevatorTeleport : Interactable
{
    [Header("This car")]
    [Tooltip("The car's pivot: floor centre of the door threshold.")]
    public Transform carPivot;
    [Tooltip("This car's door (Door in External mode).")]
    public Door door;
    public ElevatorTeleport otherCar;

    [Header("Car interior (in the pivot's local space)")]
    public Vector3 interiorCentre = new Vector3(0f, 1.25f, -1.25f);
    public Vector3 interiorSize = new Vector3(2.5f, 2.5f, 2.5f);

    [Header("Door")]
    [Tooltip("The door opens when the player comes within this distance of the pivot (metres, measured on the floor).")]
    public float approachRadius = 3.5f;
    [Tooltip("Extra distance the player must move away before the door closes again.")]
    public float leaveMargin = 1f;
    [Tooltip("The player must be within this height of the pivot to count as being on this floor.")]
    public float floorTolerance = 2f;

    [Header("Ride")]
    public float fadeOutTime = 0.6f;
    public float darkTime = 0.3f;
    public float fadeInTime = 0.6f;
    [Tooltip("How far the sound of the elevator carries at each end (metres).")]
    public float noiseRadius = 15f;

    static bool riding;
    Transform player;

    public static bool IsRiding => riding;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        riding = false;
    }

    // ------------------------------------------------------------------ interaction

    public override string PromptText
    {
        get
        {
            if (riding) return "";
            return PowerState.IsPowered ? "[E] Use elevator" : "No power";
        }
    }

    public override bool CanInteract => !riding && PowerState.IsPowered && otherCar != null && carPivot != null;

    public override void Interact()
    {
        if (!CanInteract) return;
        Transform p = FindPlayer();
        if (p == null || !IsInside(p.position)) return;
        StartCoroutine(Ride(p));
    }

    // ------------------------------------------------------------------ geometry

    public bool IsInside(Vector3 worldPosition)
    {
        if (carPivot == null) return false;
        Vector3 local = Quaternion.Inverse(carPivot.rotation) * (worldPosition - carPivot.position) - interiorCentre;
        Vector3 half = interiorSize * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    // Where a point in (or near) this car ends up in the other car: same offset from the pivot, turned with the pivot.
    public Vector3 MapPosition(Vector3 worldPosition)
    {
        Quaternion delta = otherCar.carPivot.rotation * Quaternion.Inverse(carPivot.rotation);
        return otherCar.carPivot.position + delta * (worldPosition - carPivot.position);
    }

    // The player only ever turns about the vertical axis, so only the yaw is carried over.
    public Quaternion MapRotation(Quaternion worldRotation)
    {
        Quaternion delta = otherCar.carPivot.rotation * Quaternion.Inverse(carPivot.rotation);
        Vector3 forward = delta * (worldRotation * Vector3.forward);
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f) return worldRotation;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    Transform FindPlayer()
    {
        if (player != null) return player;
        if (PlayerInteractor.Current != null) player = PlayerInteractor.Current.PlayerRoot;
        if (player == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) player = tagged.transform;
        }
        return player;
    }

    // ------------------------------------------------------------------ door

    void Start()
    {
        // this script owns the door: no auto-close timer of its own
        if (door != null) door.autoClose = false;
    }

    void Update()
    {
        if (riding || door == null || carPivot == null) return;

        bool want = false;
        if (PowerState.IsPowered)
        {
            Transform p = FindPlayer();
            if (p != null)
            {
                Vector3 offset = p.position - carPivot.position;
                bool sameFloor = Mathf.Abs(offset.y) <= floorTolerance;
                offset.y = 0f;
                float limit = door.IsOpen ? approachRadius + leaveMargin : approachRadius;
                want = IsInside(p.position) || (sameFloor && offset.magnitude <= limit);
            }
        }
        door.SetOpen(want);
    }

    // ------------------------------------------------------------------ ride

    IEnumerator Ride(Transform p)
    {
        riding = true;
        NoiseSystem.MakeNoise(carPivot.position, noiseRadius);

        if (door != null)
        {
            door.SetOpen(false);
            while (!door.IsFullyClosed) yield return null;
        }

        // stepped out while the door was closing: no ride
        if (!IsInside(p.position))
        {
            riding = false;
            yield break;
        }

        yield return FadeTo(0f, 1f, fadeOutTime);

        Vector3 position = MapPosition(p.position);
        Quaternion rotation = MapRotation(p.rotation);
        var controller = p.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        p.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;

        if (darkTime > 0f) yield return new WaitForSeconds(darkTime);
        NoiseSystem.MakeNoise(otherCar.carPivot.position, noiseRadius);

        yield return FadeTo(1f, 0f, fadeInTime);

        if (otherCar.door != null) otherCar.door.SetOpen(true);
        riding = false;
    }

    static IEnumerator FadeTo(float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            GameHUD.Fade(Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        GameHUD.Fade(to);
    }

    void OnDisable()
    {
        // never leave the screen black or the elevator stuck if this is switched off mid-ride
        if (!riding) return;
        riding = false;
        GameHUD.Fade(0f);
    }

    void OnDrawGizmosSelected()
    {
        if (carPivot == null) return;
        Gizmos.matrix = Matrix4x4.TRS(carPivot.position, carPivot.rotation, Vector3.one);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(interiorCentre, interiorSize);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(carPivot.position, approachRadius);
    }
}
