using UnityEngine;

// Goes on the player camera. Each frame it looks about 2.5 m ahead from the screen centre for an Interactable,
// shows its prompt on the HUD and calls Interact() when E is pressed.
public class PlayerInteractor : MonoBehaviour
{
    public static PlayerInteractor Current { get; private set; }

    [Tooltip("How far the player can reach (metres).")]
    public float range = 2.5f;
    [Tooltip("Layers that hold interactable colliders. Left empty, the 'Interactable' layer is used.")]
    public LayerMask interactableMask;
    [Tooltip("Layers that block reaching (walls, closed doors). Left empty, Default and Environment are used.")]
    public LayerMask blockingMask;
    public KeyCode interactKey = KeyCode.E;

    public Interactable Target { get; private set; }
    // The player object that moves (the CharacterController).
    public Transform PlayerRoot { get; private set; }

    Camera cam;

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
        Target = FindTarget();
        GameHUD.Prompt(Target != null ? Target.PromptText : "");

        if (Target != null && Target.CanInteract && Input.GetKeyDown(interactKey))
            Target.Interact();
    }

    // The interactable under the screen centre, or null. Something solid in front of it (a wall, a closed door) hides it.
    public Interactable FindTarget()
    {
        Ray ray = cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(transform.position, transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, range, interactableMask, QueryTriggerInteraction.Collide))
            return null;

        var found = hit.collider.GetComponentInParent<Interactable>();
        if (found == null || !found.isActiveAndEnabled) return null;

        int blockers = blockingMask.value & ~interactableMask.value;
        if (blockers != 0 && Physics.Raycast(ray, out RaycastHit wall, range, blockers, QueryTriggerInteraction.Ignore)
            && wall.distance < hit.distance - 0.05f
            && wall.collider.GetComponentInParent<Interactable>() != found)
            return null;

        return found;
    }
}
