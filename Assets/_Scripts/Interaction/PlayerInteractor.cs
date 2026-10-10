using UnityEngine;

// Goes on the player camera. Each frame it looks about 2.5 m ahead from the screen centre for an Interactable,
// shows its prompt on the HUD, calls Interact() when E is pressed and Hold() while it stays down.
// Aim is forgiving: a thin sphere is swept along the view line, so the crosshair only has to be near the target.
// When several interactables are within reach of the sweep, the one closest to the screen centre wins.
public class PlayerInteractor : MonoBehaviour
{
    public static PlayerInteractor Current { get; private set; }

    [Tooltip("How far the player can reach (metres).")]
    public float range = 2.5f;
    [Tooltip("Radius of the sweep along the view line (metres). 0 = exact aim only.")]
    public float aimRadius = 0.15f;
    [Tooltip("Layers that hold interactable colliders. Left empty, the 'Interactable' layer is used.")]
    public LayerMask interactableMask;
    [Tooltip("Layers that block reaching (walls, closed doors). Left empty, Default and Environment are used.")]
    public LayerMask blockingMask;
    public KeyCode interactKey = KeyCode.E;

    public Interactable Target { get; private set; }
    // The player object that moves (the CharacterController).
    public Transform PlayerRoot { get; private set; }

    Camera cam;
    static readonly RaycastHit[] sweepHits = new RaycastHit[24];

    void Reset()
    {
        ApplyDefaultMasks();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        var controller = GetComponentInParent<CharacterController>();
        PlayerRoot = controller != null ? controller.transform : transform.root;
        ApplyDefaultMasks();
    }

    void ApplyDefaultMasks()
    {
        if (interactableMask.value == 0) interactableMask = LayerMask.GetMask(Interactable.LayerName);
        if (blockingMask.value == 0) blockingMask = LayerMask.GetMask("Default", "Environment");
    }

    void OnEnable()
    {
        Current = this;
    }

    void OnDisable()
    {
        if (Current == this) Current = null;
        GameHUD.Prompt("");
    }

    void Update()
    {
        if (GameManager.InputBlocked) return;

        Target = FindTarget();
        GameHUD.Prompt(Target != null ? Target.PromptText : "");

        if (Target != null && Target.CanInteract && Input.GetKeyDown(interactKey))
            Target.Interact();
        // hold-to-use things (the data download): looking away or stepping out of reach drops the target, which pauses them
        if (Target != null && Target.CanInteract && Input.GetKey(interactKey))
            Target.Hold(Time.deltaTime);
    }

    // The interactable the player is aiming at, or null. Something solid in front of it (a wall, a closed door) hides it.
    public Interactable FindTarget()
    {
        Ray ray = cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position, transform.forward);

        // dead centre first: nothing can be closer to the screen centre than what the view line itself hits
        if (Physics.Raycast(ray, out RaycastHit direct, range, interactableMask, QueryTriggerInteraction.Collide))
        {
            var found = Usable(direct.collider);
            if (found != null && !Blocked(ray.origin, direct.point, found)) return found;
        }
        if (aimRadius <= 0f) return null;

        int count = Physics.SphereCastNonAlloc(ray, aimRadius, sweepHits, range, interactableMask, QueryTriggerInteraction.Collide);
        Interactable best = null;
        float bestOffCentre = float.MaxValue;
        float bestAlong = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var found = Usable(sweepHits[i].collider);
            if (found == null) continue;

            // a collider the sweep starts inside reports no contact point: use its nearest point instead
            Vector3 point = sweepHits[i].distance > 0f ? sweepHits[i].point : sweepHits[i].collider.ClosestPoint(ray.origin);
            Vector3 to = point - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            if (along < 0f) continue;
            float offCentre = Vector3.Cross(ray.direction, to).magnitude;
            if (offCentre > bestOffCentre + 0.001f) continue;
            if (Mathf.Abs(offCentre - bestOffCentre) <= 0.001f && along >= bestAlong) continue;
            if (Blocked(ray.origin, point, found)) continue;

            best = found;
            bestOffCentre = offCentre;
            bestAlong = along;
        }
        return best;
    }

    static Interactable Usable(Collider collider)
    {
        var found = collider.GetComponentInParent<Interactable>();
        return found != null && found.isActiveAndEnabled ? found : null;
    }

    // True when something solid that is not part of the interactable itself lies between the eye and the point.
    bool Blocked(Vector3 eye, Vector3 point, Interactable target)
    {
        int blockers = blockingMask.value & ~interactableMask.value;
        if (blockers == 0) return false;
        if (!Physics.Linecast(eye, point, out RaycastHit wall, blockers, QueryTriggerInteraction.Ignore)) return false;
        if (wall.distance >= (point - eye).magnitude - 0.05f) return false;
        return wall.collider.GetComponentInParent<Interactable>() != target;
    }
}
