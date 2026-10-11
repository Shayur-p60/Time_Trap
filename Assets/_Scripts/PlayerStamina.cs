using System;
using UnityEngine;

// The player's stamina, 0 to 1. Goes on the FirstPersonController.
//
//   Drain     only while actually sprinting (the sprint is on and the player is moving): full to empty in Drain Time.
//             Shift held while standing still, or while running into a wall, costs nothing. Speed Boost: no drain.
//   Recharge  starts Recharge Delay after the sprint stops: empty to full in Recharge Time.
//   Exhausted at 0 the sprint stops (PlayerMovementController.SprintBlocked) and the player is back at walk speed.
//             Sprint cannot start again until stamina is above Resume Threshold.
//   HUD       GameHUD.Stamina: a thin bar, shown only while stamina is not full, red while exhausted.
//   Reset     full again after a checkpoint respawn; a Restart reloads the scene, so it starts full.
// It runs on game time, so it stands still while the game is paused or on an end screen.
[DefaultExecutionOrder(-20)] // before PlayerWeaponState and PlayerMovementController, so Sprint Blocked is right in the same frame
public class PlayerStamina : MonoBehaviour
{
    [Tooltip("Seconds of sprinting from full to empty.")]
    public float drainTime = 5f;
    [Tooltip("Seconds after the sprint stops before stamina starts to come back.")]
    public float rechargeDelay = 1.5f;
    [Tooltip("Seconds from empty to full.")]
    public float rechargeTime = 4f;
    [Tooltip("After running out, sprint is blocked until stamina is back above this.")]
    [Range(0f, 1f)] public float resumeThreshold = 0.25f;
    [Tooltip("Below this ground speed (m/s) the player is not really running and nothing is drained.")]
    public float minMoveSpeed = 0.5f;

    // 0 = empty, 1 = full.
    public float Stamina01 { get; private set; } = 1f;
    // Ran out: no sprint until stamina is back above Resume Threshold.
    public bool Exhausted { get; private set; }
    // Sprinting and moving this frame (whether or not Speed Boost is paying for it).
    public bool Sprinting { get; private set; }
    public int TimesExhausted { get; private set; }

    // Stamina has just hit 0.
    public event Action OnExhausted;

    PlayerMovementController movement;
    PlayerPowerUps powerUps;
    float sinceSprint = 999f;

    void Awake()
    {
        movement = GetComponent<PlayerMovementController>();
        powerUps = GetComponent<PlayerPowerUps>();
    }

    void OnEnable()
    {
        CheckpointManager.OnPlayerRespawned += HandleRespawned;
    }

    void OnDisable()
    {
        CheckpointManager.OnPlayerRespawned -= HandleRespawned;
        if (movement != null) movement.SprintBlocked = false;
        GameHUD.Stamina(-1f, false);
    }

    void HandleRespawned(Vector3 position)
    {
        Refill();
    }

    // Back to full, and no longer exhausted.
    public void Refill()
    {
        SetStamina(1f);
    }

    // Sets stamina directly (respawn, tests). Raising it above Resume Threshold ends the exhaustion.
    public void SetStamina(float value)
    {
        Stamina01 = Mathf.Clamp01(value);
        if (Stamina01 > resumeThreshold) Exhausted = false;
        if (movement != null) movement.SprintBlocked = Exhausted;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (movement == null) return;

        if (dt > 0f)
        {
            Sprinting = movement.IsSprinting && movement.CurrentGroundSpeed > minMoveSpeed;
            if (Sprinting)
            {
                sinceSprint = 0f;
                bool free = powerUps != null && powerUps.SpeedBoostActive;
                if (!free)
                {
                    Stamina01 -= dt / Mathf.Max(0.01f, drainTime);
                    if (Stamina01 <= 0f)
                    {
                        Stamina01 = 0f;
                        if (!Exhausted) Exhaust();
                    }
                }
            }
            else
            {
                sinceSprint += dt;
                if (sinceSprint >= rechargeDelay) Stamina01 = Mathf.Min(1f, Stamina01 + dt / Mathf.Max(0.01f, rechargeTime));
            }

            if (Exhausted && Stamina01 > resumeThreshold) Exhausted = false;
            movement.SprintBlocked = Exhausted;
        }

        bool show = Stamina01 < 1f && !GameManager.IsGameOver;
        GameHUD.Stamina(show ? Stamina01 : -1f, Exhausted);
    }

    void Exhaust()
    {
        Exhausted = true;
        Sprinting = false;
        TimesExhausted++;
        var manager = GameManager.Instance;
        if (manager != null && manager.State == GameState.Playing) manager.Stats.timesExhausted++;
        OnExhausted?.Invoke();
    }
}
