using System;
using UnityEngine;

// A zombie's health. The pistol finds this (or a ZombieHitbox in front of it) through IDamageable.
// With the default 75 health and the pistol's 25 damage a zombie dies to 3 body shots, or 1 shot to the head hitbox
// (x3). ZombieAI listens to OnDamaged and OnDied; this script knows nothing about behaviour.
public class ZombieHealth : MonoBehaviour, IDamageable
{
    public float maxHealth = 75f;
    [Tooltip("Damage multiplier of the head hitbox ZombieAI creates on the head bone.")]
    public float headshotMultiplier = 3f;

    public float CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0f;
    public int HitsTaken { get; private set; }
    public bool LastHitWasHead { get; private set; }

    // (damage applied, where, head shot)
    public event Action<float, Vector3, bool> OnDamaged;
    public event Action OnDied;

    void Awake()
    {
        CurrentHealth = maxHealth;
    }

    // Changes the maximum and refills to it (the spawner calls this on a fresh zombie).
    public void SetMaxHealth(float value)
    {
        maxHealth = Mathf.Max(1f, value);
        CurrentHealth = maxHealth;
    }

    // A body shot.
    public void TakeHit(float damage, Vector3 point)
    {
        ApplyHit(damage, point, false);
    }

    // Damage that is already multiplied (ZombieHitbox calls this for the head).
    public void ApplyHit(float damage, Vector3 point, bool head)
    {
        if (IsDead || damage <= 0f) return;
        HitsTaken++;
        LastHitWasHead = head;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
        OnDamaged?.Invoke(damage, point, head);
        if (CurrentHealth <= 0f) OnDied?.Invoke();
    }

    public void Kill()
    {
        if (IsDead) return;
        CurrentHealth = 0f;
        OnDied?.Invoke();
    }
}
