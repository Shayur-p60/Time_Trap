using UnityEngine;

// The pathogen sample (the vaccine case in the Birkin lab). E takes it: the case disappears and GameProgress records it.
public class PathogenSample : Interactable
{
    [Tooltip("What disappears when the sample is taken. Left empty, this object.")]
    public GameObject visual;

    public bool Taken { get; private set; }

    public override string PromptText => Taken ? "" : "[E] Take pathogen sample";

    public override bool CanInteract => !Taken;

    // Awake / OnDestroy rather than OnEnable / OnDisable: the object is inactive once taken and must still hear the reset.
    void Awake()
    {
        GameProgress.OnProgressReset += HandleReset;
        PickupHighlight.Ensure(gameObject);
    }

    void OnDestroy()
    {
        GameProgress.OnProgressReset -= HandleReset;
    }

    public override void Interact()
    {
        if (Taken) return;
        Taken = true;
        PickupHighlight.Collect(gameObject);
        if (GameProgress.Instance != null) GameProgress.Instance.Complete(Objective.Pathogen);
        else Debug.LogWarning("[PathogenSample] Taken, but there is no GameProgress in the scene to record it.");
        (visual != null ? visual : gameObject).SetActive(false);
    }

    void HandleReset()
    {
        Taken = false;
        (visual != null ? visual : gameObject).SetActive(true);
        PickupHighlight.Restore(gameObject);
    }
}
