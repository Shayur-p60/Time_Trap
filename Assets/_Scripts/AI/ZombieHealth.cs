using UnityEngine;

/// <summary>
/// Damage sink for a zombie. Keeps health out of the FSM so the FSM only has
/// to care about one thing: someone told me I'm dead.
/// </summary>
[RequireComponent(typeof(ZombieAI))]
public class ZombieHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;
    [Tooltip("Multiplier applied when the hit came from a HeadHitbox.")]
    public float headshotMultiplier = 3f;

    [Header("Noise")]
    [Tooltip("Getting hit is loud. Nearby zombies will come and look.")]
    public float painNoiseRadius = 14f;

    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;

    private ZombieAI ai;

    private void Awake()
    {
        ai = GetComponent<ZombieAI>();
        CurrentHealth = maxHealth;
    }

    public void TakeDamage(int amount, bool headshot = false)
    {
        if (IsDead) return;

        int applied = headshot
            ? Mathf.RoundToInt(amount * headshotMultiplier)
            : amount;

        CurrentHealth -= applied;

        // A wounded zombie shrieks; that pulls its friends in.
        if (painNoiseRadius > 0f)
            NoiseEvent.Emit(transform.position, painNoiseRadius, transform);

        if (CurrentHealth <= 0)
        {
            CurrentHealth = 0;
            ai.Kill();
        }
    }
}
