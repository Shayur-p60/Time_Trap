using System;
using System.Collections.Generic;
using UnityEngine;

public enum PowerUpType { FlashPulse, SpeedBoost, ZombieFreeze, Compass }

// Static side of the power-ups: the events and state other systems (zombies, objectives) hook into.
// The effects on the player themselves are run by PlayerPowerUps.
public static class PowerUps
{
    // Flash Pulse went off: (position, seconds it lasts).
    public static event Action<Vector3, float> OnFlashPulse;

    // Zombie Freeze started: (seconds it lasts). OnZombieFreezeEnded follows when the time is up.
    public static event Action<float> OnZombieFreeze;
    public static event Action OnZombieFreezeEnded;

    // Time.time at which the current freeze ends.
    public static float FreezeEndTime { get; private set; }
    public static bool ZombiesFrozen => Application.isPlaying && Time.time < FreezeEndTime;

    // What the Compass points at. The objectives task fills this; anything collected should be removed (or destroyed
    // or deactivated: those entries are skipped).
    public static readonly List<Transform> CompassTargets = new List<Transform>();

    public static void RaiseFlashPulse(Vector3 position, float duration)
    {
        OnFlashPulse?.Invoke(position, duration);
    }

    public static void RaiseZombieFreeze(float duration)
    {
        FreezeEndTime = Time.time + duration;
        OnZombieFreeze?.Invoke(duration);
    }

    public static void RaiseZombieFreezeEnded()
    {
        FreezeEndTime = 0f;
        OnZombieFreezeEnded?.Invoke();
    }

    // The nearest live compass target to a position, or null.
    public static Transform NearestCompassTarget(Vector3 from)
    {
        Transform best = null;
        float bestSqr = float.MaxValue;
        foreach (var t in CompassTargets)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            float sqr = (t.position - from).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }
        return best;
    }

    public static string DisplayName(PowerUpType type)
    {
        switch (type)
        {
            case PowerUpType.FlashPulse: return "Flash Pulse";
            case PowerUpType.SpeedBoost: return "Speed Boost";
            case PowerUpType.ZombieFreeze: return "Zombie Freeze";
            default: return "Compass";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        OnFlashPulse = null;
        OnZombieFreeze = null;
        OnZombieFreezeEnded = null;
        FreezeEndTime = 0f;
        CompassTargets.Clear();
    }
}
