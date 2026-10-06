using System;
using UnityEngine;

// The single switch for the facility power state.
//   Not powered = Stage 1 (game start: emergency lighting, alarms).
//   Powered     = Stage 2 (power restored).
// The editor preview menus (Time Trap/Lighting/Preview Stage 1 and 2) and, later, the generator script all call SetPowered,
// and anything that reacts to power (for example GeneratorRoomAlarm) listens to OnPowerChanged.
public static class PowerState
{
    const string SessionKey = "TimeTrap.PowerState.IsPowered";

#if !UNITY_EDITOR
    static bool powered;
#endif

    // Raised every time SetPowered is called (even with the same value, so a preview can force a refresh).
    public static event Action<bool> OnPowerChanged;

    public static bool IsPowered
    {
        get
        {
#if UNITY_EDITOR
            // kept in the editor session so the Stage 1 / Stage 2 preview survives script recompiles
            return UnityEditor.SessionState.GetBool(SessionKey, false);
#else
            return powered;
#endif
        }
    }

    public static void SetPowered(bool value)
    {
#if UNITY_EDITOR
        UnityEditor.SessionState.SetBool(SessionKey, value);
#else
        powered = value;
#endif
        OnPowerChanged?.Invoke(value);
    }

    // The game always starts without power (Stage 1), and listeners from a previous play session are dropped.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        OnPowerChanged = null;
#if UNITY_EDITOR
        UnityEditor.SessionState.SetBool(SessionKey, false);
#else
        powered = false;
#endif
    }
}
