using UnityEngine;

// Base class for everything the player can use with E. PlayerInteractor raycasts against the "Interactable" layer,
// finds the nearest Interactable on (or above) the collider it hits, shows PromptText and calls Interact() on E.
public abstract class Interactable : MonoBehaviour
{
    public const string LayerName = "Interactable";

    // Full text of the on-screen prompt, for example "[E] Open door" or "Locked – needs power". Empty = show nothing.
    public virtual string PromptText => "";

    // False = the prompt is shown as information only and E does nothing.
    public virtual bool CanInteract => true;

    public abstract void Interact();

    // Called every frame E is held on this (after Interact on the first frame). For hold-to-use things.
    public virtual void Hold(float deltaTime) { }
}
