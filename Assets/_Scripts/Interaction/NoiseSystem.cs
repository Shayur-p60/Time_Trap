using System;
using UnityEngine;

// Who made a noise. Zombies ignore Zombie noise (door banging, a door one of them opened), so they never chase each other's sounds.
public enum NoiseKind { World, Player, Zombie }

public struct NoiseInfo
{
    public Vector3 position;
    public float radius;
    public NoiseKind kind;
    // Optional: whoever made it.
    public Transform source;
}

// One place to say "something made a sound here": doors, the elevator, gunshots, footsteps.
// Listeners get the position and the radius the sound carries. ZombieHearing decides which zombies react.
public static class NoiseSystem
{
    public static event Action<Vector3, float> OnNoise;
    // The same noise with its kind and source.
    public static event Action<NoiseInfo> OnNoiseDetailed;

    public static void MakeNoise(Vector3 position, float radius, NoiseKind kind = NoiseKind.World, Transform source = null)
    {
        if (radius <= 0f) return;
        OnNoise?.Invoke(position, radius);
        OnNoiseDetailed?.Invoke(new NoiseInfo { position = position, radius = radius, kind = kind, source = source });
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        OnNoise = null;
        OnNoiseDetailed = null;
    }
}
