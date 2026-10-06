using UnityEngine;

// Anything the pistol can hurt. The pistol calls TakeHit on the first IDamageable found on (or above) the collider it hits.
public interface IDamageable
{
    void TakeHit(float damage, Vector3 point);
}
