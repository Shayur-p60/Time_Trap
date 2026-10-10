using System;
using UnityEngine;

// The one gate every zombie catch goes through, so a catch happens exactly once however many zombies reach the player
// in the same moment. A catch raises PlayerEvents.OnPlayerKilled("Zombie"); until the game manager exists it also
// sends the player back to the latest checkpoint (Respawn On Catch).
// After any checkpoint respawn the player cannot be caught for Invulnerable Time, and the zombies near the respawn
// point are cleared (ZombieAI.ResetAfterPlayerRespawn).
public static class PlayerCatch
{
    // Seconds after a checkpoint respawn during which the player cannot be caught.
    public static float invulnerableTime = 2f;
    // Zombies within this distance of the respawn point are removed; the rest forget the player.
    public static float clearRadius = 12f;
    // TEMPORARY: the catch itself calls CheckpointManager.RespawnAtCheckpoint. Switch off when the game manager
    // handles lives, the penalty and the respawn from OnPlayerKilled.
    public static bool respawnOnCatch = true;

    // A zombie caught the player: (which one). Raised once per catch, after OnPlayerKilled.
    public static event Action<ZombieAI> OnPlayerCaught;

    public static int CatchCount { get; private set; }
    public static float InvulnerableUntil { get; private set; }

    // a catch has happened and the respawn has not put the player down yet
    static bool pending;
    static float pendingSince;
    static Transform player;

    public static Transform Player
    {
        get
        {
            if (player == null)
            {
                var found = GameObject.FindGameObjectWithTag("Player");
                if (found != null) player = found.transform;
            }
            return player;
        }
    }

    // True between a catch and the end of the respawn fade: zombies neither see nor attack the player.
    public static bool PlayerOutOfPlay
    {
        get
        {
            // safety: nothing respawned the player, so do not stay locked for ever
            if (pending && Time.time - pendingSince > 5f) pending = false;
            if (pending) return true;
            var checkpoints = CheckpointManager.Instance;
            return checkpoints != null && checkpoints.IsRespawning;
        }
    }

    public static bool CanBeCaught => !PlayerOutOfPlay && Time.time >= InvulnerableUntil;

    // A zombie's strike has landed. True if this was the catch; false if the player is already caught, respawning
    // or still invulnerable.
    public static bool TryCatch(ZombieAI by)
    {
        if (!CanBeCaught) return false;
        pending = true;
        pendingSince = Time.time;
        CatchCount++;

        PlayerEvents.RaisePlayerKilled("Zombie");
        OnPlayerCaught?.Invoke(by);

        if (respawnOnCatch)
        {
            var checkpoints = CheckpointManager.Instance;
            // nothing to respawn to: the player stays put, so at least give them the grace period
            if (checkpoints == null || !checkpoints.RespawnAtCheckpoint()) HandleRespawned(Player != null ? Player.position : Vector3.zero);
        }
        return true;
    }

    // Makes the player uncatchable for a while (for the game manager's penalty, for example).
    public static void GrantInvulnerability(float seconds)
    {
        InvulnerableUntil = Mathf.Max(InvulnerableUntil, Time.time + seconds);
    }

    static void HandleRespawned(Vector3 position)
    {
        pending = false;
        GrantInvulnerability(invulnerableTime);
        ZombieAI.ResetAfterPlayerRespawn(position, clearRadius);
    }

    // After SubsystemRegistration, where CheckpointManager drops its listeners.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartPlaySession()
    {
        OnPlayerCaught = null;
        CatchCount = 0;
        InvulnerableUntil = 0f;
        pending = false;
        player = null;
        respawnOnCatch = true;
        CheckpointManager.OnPlayerRespawned += HandleRespawned;
    }
}
