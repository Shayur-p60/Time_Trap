using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Minimal player health so zombie attacks have somewhere to land.
/// Swap or extend this once you have a real damage system.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;
    [Tooltip("Seconds of immunity after a hit, stops stacked zombies one-shotting you.")]
    public float invulnerabilityWindow = 0.5f;

    [Header("Events")]
    public UnityEvent<int, int> onHealthChanged; // (current, max)
    public UnityEvent onDeath;

    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;

    private float lastHitTime = -999f;

    private void Awake()
    {
        CurrentHealth = maxHealth;
    }

    private void Start()
    {
        onHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    public void TakeDamage(int amount)
    {
        if (IsDead) return;
        if (Time.time - lastHitTime < invulnerabilityWindow) return;

        lastHitTime = Time.time;
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        onHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (CurrentHealth <= 0)
            onDeath?.Invoke();
    }

    public void Heal(int amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        onHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }
}
