using System.Collections.Generic;
using UnityEngine;

// Decides which zombies react to a noise. A zombie hears a noise when the walking distance to it (along the NavMesh,
// so a wall in between counts for what it is) is within the noise's radius; of those, only the nearest Max Responders
// go and look, so one gunshot does not empty the whole wing. Noise made by zombies is ignored.
public static class ZombieHearing
{
    // How many zombies at most react to one noise. The spawner sets this per stage.
    public static int maxResponders = 3;
    // The walking distance may be this much longer than the radius (corridors are never straight).
    public static float pathSlack = 1.25f;

    public static int NoisesHeard { get; private set; }
    public static int LastResponderCount { get; private set; }

    static readonly List<ZombieAI> candidates = new List<ZombieAI>();
    static readonly List<float> distances = new List<float>();

    static void HandleNoise(NoiseInfo noise)
    {
        if (noise.kind == NoiseKind.Zombie) return;

        candidates.Clear();
        distances.Clear();
        foreach (var zombie in ZombieAI.All)
        {
            if (zombie == null || !zombie.CanHear) continue;
            float reach = noise.radius * zombie.hearingSensitivity;
            if (Vector3.Distance(zombie.transform.position, noise.position) > reach) continue;
            float walk = zombie.PathDistanceTo(noise.position);
            if (walk > reach * pathSlack) continue;

            // keep the lists sorted, nearest first
            int at = distances.Count;
            while (at > 0 && distances[at - 1] > walk) at--;
            distances.Insert(at, walk);
            candidates.Insert(at, zombie);
        }

        int count = Mathf.Min(candidates.Count, Mathf.Max(0, maxResponders));
        for (int i = 0; i < count; i++) candidates[i].Investigate(noise.position);
        if (count > 0) NoisesHeard++;
        LastResponderCount = count;
        candidates.Clear();
        distances.Clear();
    }

    // After SubsystemRegistration, where NoiseSystem drops its listeners.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void StartPlaySession()
    {
        maxResponders = 3;
        NoisesHeard = 0;
        LastResponderCount = 0;
        NoiseSystem.OnNoiseDetailed += HandleNoise;
    }
}
