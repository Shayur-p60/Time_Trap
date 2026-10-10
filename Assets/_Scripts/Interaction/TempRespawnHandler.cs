using System.Collections;
using UnityEngine;

// TEMPORARY: stands in for the game manager until it exists. Listens to PlayerEvents.OnPlayerKilled and, whatever
// the cause, fades to black, puts the player on the respawn point and fades back in. Delete this (and take over the
// event) when the game manager handles death.
public class TempRespawnHandler : MonoBehaviour
{
    public Transform respawnPoint;
    [Tooltip("Seconds for each fade (out, then in).")]
    public float fadeTime = 0.5f;

    public bool IsRespawning { get; private set; }
    public string LastCause { get; private set; }
    public int RespawnCount { get; private set; }

    void OnEnable()
    {
        PlayerEvents.OnPlayerKilled += HandleKilled;
    }

    void OnDisable()
    {
        PlayerEvents.OnPlayerKilled -= HandleKilled;
        if (IsRespawning)
        {
            IsRespawning = false;
            GameHUD.Fade(0f);
        }
    }

    void HandleKilled(string cause)
    {
        // fallback only: with a CheckpointManager in the scene, DeathZone respawns the player at the latest checkpoint
        if (CheckpointManager.Instance != null) return;
        if (IsRespawning || respawnPoint == null) return;
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;
        LastCause = cause;
        StartCoroutine(Respawn(player.transform));
    }

    IEnumerator Respawn(Transform player)
    {
        IsRespawning = true;

        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            GameHUD.Fade(t / fadeTime);
            yield return null;
        }
        GameHUD.Fade(1f);

        // the CharacterController must be off while the transform is moved, or it snaps back
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.SetPositionAndRotation(respawnPoint.position, Quaternion.Euler(0f, respawnPoint.eulerAngles.y, 0f));
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;

        var movement = player.GetComponent<PlayerMovementController>();
        if (movement != null) movement.ResetFall();
        RespawnCount++;

        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            GameHUD.Fade(1f - t / fadeTime);
            yield return null;
        }
        GameHUD.Fade(0f);

        IsRespawning = false;
    }
}
