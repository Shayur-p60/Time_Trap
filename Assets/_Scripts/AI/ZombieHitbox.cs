using UnityEngine;

// A part of a zombie that takes more (or less) damage than the body: the head. Sits on its own collider, so the
// pistol's GetComponentInParent<IDamageable> finds it before the ZombieHealth on the root.
public class ZombieHitbox : MonoBehaviour, IDamageable
{
    public ZombieHealth health;
    public float damageMultiplier = 3f;
    public bool isHead = true;

    public void TakeHit(float damage, Vector3 point)
    {
        if (health == null) health = GetComponentInParent<ZombieHealth>();
        if (health != null) health.ApplyHit(damage * damageMultiplier, point, isHead);
    }
}
