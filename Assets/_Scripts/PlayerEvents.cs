using System;

// Things that happen to the player that the rest of the game cares about.
// The game manager (later) subscribes here; for now TempRespawnHandler does.
public static class PlayerEvents
{
    // The player died. cause: "Fall" (the Bioreactors pit), "OutOfBounds" (fell out of the map), ...
    public static event Action<string> OnPlayerKilled;

    // Number of listeners, for the restart checks (GameManager.StaticListenerReport).
    public static int KilledListenerCount => OnPlayerKilled != null ? OnPlayerKilled.GetInvocationList().Length : 0;

    // False when the death does not count: the game manager allows one death at a time, and the next only once the
    // respawn from the last has finished (GameManager.DeathAllowed). Nothing is raised then.
    public static bool RaisePlayerKilled(string cause)
    {
        if (!GameManager.DeathAllowed) return false;
        OnPlayerKilled?.Invoke(cause);
        return true;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        OnPlayerKilled = null;
    }
}
