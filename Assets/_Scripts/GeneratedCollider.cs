using UnityEngine;

// Marker added by the editor tool "Time Trap/Colliders/Generate". It records which
// colliders the tool created so that Generate / Clear Generated can remove exactly
// those and never touch colliders that existed before.
[DisallowMultipleComponent]
[AddComponentMenu("")]
public class GeneratedCollider : MonoBehaviour
{
    [Tooltip("Rule that produced the colliders: Structural, Prop, Door, GapFill or Ramp.")]
    public string rule;

    [Tooltip("Colliders on this object that were added by the generator.")]
    public Collider[] colliders = new Collider[0];

    [Tooltip("True when the generator created this whole GameObject (for example a ramp).")]
    public bool ownsGameObject;
}
