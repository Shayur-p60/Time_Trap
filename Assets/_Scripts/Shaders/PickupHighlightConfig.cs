using UnityEngine;

// Holds the highlight material for the scene (Map/Gameplay/PickupHighlightConfig). Pickups are created at runtime
// and cannot carry a material reference of their own, so PickupHighlight reads it from here.
[DefaultExecutionOrder(-200)]   // before the pickups are spawned
public class PickupHighlightConfig : MonoBehaviour
{
    [Tooltip("The TimeTrap/PickupHighlight material.")]
    public Material material;

    void Awake()
    {
        PickupHighlight.SharedMaterial = material;
    }
}
