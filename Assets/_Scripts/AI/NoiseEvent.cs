using System;
using UnityEngine;

/// <summary>
/// Lightweight global bus for "something made a sound here" events.
/// Anything can shout: NoiseEvent.Emit(transform.position, 8f);
/// Zombies subscribe in OnEnable and decide for themselves whether they were
/// close enough to care.
/// </summary>
public static class NoiseEvent
{
    public struct Data
    {
        public Vector3 position;
        public float radius;
        public Transform source;
    }

    public static event Action<Data> OnNoise;

    public static void Emit(Vector3 position, float radius, Transform source = null)
    {
        OnNoise?.Invoke(new Data
        {
            position = position,
            radius = radius,
            source = source
        });
    }
}
