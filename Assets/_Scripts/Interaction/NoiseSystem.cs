using System;
using UnityEngine;

// One place to say "something made a sound here": doors, the elevator, gunshots.
// Listeners get the position and the radius the sound carries.
// It also forwards to the existing NoiseEvent bus, which ZombieAI already listens to, so zombies hear these noises today.
public static class NoiseSystem
{
    public static event Action<Vector3, float> OnNoise;

    public static void MakeNoise(Vector3 position, float radius)
    {
        if (radius <= 0f) return;
        OnNoise?.Invoke(position, radius);
        NoiseEvent.Emit(position, radius);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        OnNoise = null;
    }
}
