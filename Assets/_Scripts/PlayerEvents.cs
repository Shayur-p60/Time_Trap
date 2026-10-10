using System;

// Things that happen to the player that the rest of the game cares about.
// The game manager (later) subscribes here; for now TempRespawnHandler does.
public static class PlayerEvents
{
    // The player died. cause: "Fall" (the Bioreactors pit), "OutOfBounds" (fell out of the map), ...
    public static event Action<string> OnPlayerKilled;

    public static void RaisePlayerKilled(string cause)
    {
        OnPlayerKilled?.Invoke(cause);
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        OnPlayerKilled = null;
    }
}
