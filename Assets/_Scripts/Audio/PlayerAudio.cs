using UnityEngine;

// The player's own sounds, all 2D through the SFX group (GameAudio's voices). Goes on the FirstPersonController.
//
//   Footsteps  driven straight from the HeadBob phase: one step at each dip of the camera, at every speed, so the
//              sound and the bob cannot drift apart. Walking and sprinting have their own clip sets; the rhythm
//              changes with the bob. Silent when crouching, none in the air, never the same clip twice running.
//   Panting    a loop whose volume follows PlayerStamina: it comes in once a sprint has taken stamina below Pant
//              Start, is loudest at empty, and fades away as stamina comes back. While the player is exhausted the
//              panting gives them away: a small noise pulse (NoiseSystem, kind Player) every Pant Noise Interval.
//   Pistol     gunshot on Pistol.OnFired, reload on OnReloadStarted (cut short if the reload is cancelled), a click on
//              OnDryFire (trigger pulled with an empty magazine).
//   Torch      a click on F, on and off, whichever light is showing (the torch or the pistol's weapon light).
public class PlayerAudio : MonoBehaviour
{
    [Header("References (found on this object and its children when empty)")]
    public HeadBob headBob;
    public CharacterController controller;
    public PlayerMovementController movement;
    public Pistol pistol;
    public FlashlightController torch;
    public PlayerStamina stamina;

    [Header("Footsteps")]
    [Tooltip("Walking steps.")]
    public Sfx[] footsteps = new Sfx[0];
    [Tooltip("Sprinting steps.")]
    public Sfx[] runFootsteps = new Sfx[0];
    [Range(0f, 1f)] public float walkVolume = 0.21f;
    [Range(0f, 1f)] public float runVolume = 0.3f;
    [Tooltip("Each step's volume varies by up to this fraction either way.")]
    [Range(0f, 0.5f)] public float stepVolumeVariation = 0.1f;
    [Tooltip("Where in a step of the HeadBob phase the foot lands: 0.75 is the lowest point of the camera's dip.")]
    [Range(0f, 1f)] public float stepPhaseOffset = 0.75f;
    [Tooltip("Seconds off the ground before a step counts as airborne (stairs and small ledges stay audible).")]
    public float groundedGrace = 0.1f;

    [Header("Pistol")]
    public Sfx gunshot = new Sfx();
    public Sfx reload = new Sfx();
    public Sfx emptyClick = new Sfx();

    [Header("Torch")]
    public Sfx torchClick = new Sfx();

    [Header("Panting (the loop itself is GameAudio's Panting)")]
    [Tooltip("Panting starts once a sprint has taken stamina below this.")]
    [Range(0f, 1f)] public float pantStart = 0.8f;
    [Tooltip("Volume when it starts (x) and at empty stamina (y).")]
    public Vector2 pantVolume = new Vector2(0.1f, 0.4f);
    [Tooltip("Seconds the volume takes to follow, so it never jumps.")]
    public float pantFade = 0.5f;
    [Tooltip("Optional. A short gasp at the moment stamina runs out.")]
    public Sfx gasp = new Sfx();
    [Tooltip("While exhausted, a noise pulse this often (seconds)...")]
    public float pantNoiseInterval = 1.5f;
    [Tooltip("...that zombies hear within this distance (metres).")]
    public float pantNoiseRadius = 5f;

    // For tests: counts as holding the crouch key (the key itself cannot be pressed from a script).
    [System.NonSerialized] public bool scriptedCrouch;

    // For tests.
    public int WalkSteps { get; private set; }
    public int RunSteps { get; private set; }
    // How late the last step was fired after the exact dip, in seconds (it can only be late by part of a frame),
    // and the largest and the sum of those since the start.
    public float LastStepLateness { get; private set; }
    public float MaxStepLateness { get; private set; }
    public float TotalStepLateness { get; private set; }
    // The panting volume asked for right now (0-1).
    public float PantTarget { get; private set; }
    public int PantNoisePulses { get; private set; }
    public int FootstepCount { get; private set; }
    // Steps that made no sound because the player was crouching or in the air.
    public int SilentSteps { get; private set; }

    int lastStep;
    int lastFootstep = -1;
    int lastRunFootstep = -1;
    bool panting;
    float pantNoiseTimer;
    float lastGroundedTime = -999f;
    SfxVoice reloadVoice;
    float reloadStarted;

    void Awake()
    {
        if (headBob == null) headBob = GetComponentInChildren<HeadBob>(true);
        if (controller == null) controller = GetComponent<CharacterController>();
        if (movement == null) movement = GetComponent<PlayerMovementController>();
        if (pistol == null) pistol = GetComponentInChildren<Pistol>(true);
        if (torch == null) torch = GetComponentInChildren<FlashlightController>(true);
        if (stamina == null) stamina = GetComponent<PlayerStamina>();
    }

    void OnEnable()
    {
        if (pistol != null)
        {
            pistol.OnFired += HandleFired;
            pistol.OnDryFire += HandleDryFire;
            pistol.OnReloadStarted += HandleReloadStarted;
            pistol.OnReloadFinished += HandleReloadFinished;
        }
        if (torch != null) torch.OnToggled += HandleTorchToggled;
        if (stamina != null) stamina.OnExhausted += HandleExhausted;
        if (headBob != null) lastStep = StepIndex(headBob.StepPhase);
    }

    void OnDisable()
    {
        if (pistol != null)
        {
            pistol.OnFired -= HandleFired;
            pistol.OnDryFire -= HandleDryFire;
            pistol.OnReloadStarted -= HandleReloadStarted;
            pistol.OnReloadFinished -= HandleReloadFinished;
        }
        if (torch != null) torch.OnToggled -= HandleTorchToggled;
        if (stamina != null) stamina.OnExhausted -= HandleExhausted;
        panting = false;
        PantTarget = 0f;
        if (GameAudio.Instance != null) GameAudio.Instance.SetPanting(0f, 0.1f);
    }

    // The HeadBob phase counts steps (0 to 2 and round again). The index changes at the moment the phase passes the
    // dip of each step.
    int StepIndex(float phase)
    {
        return Mathf.FloorToInt(Mathf.Repeat(phase + (1f - stepPhaseOffset), 2f));
    }

    void Update()
    {
        UpdatePanting(Time.deltaTime);
    }

    void UpdatePanting(float dt)
    {
        if (stamina == null || dt <= 0f) return;
        float level = stamina.Stamina01;

        if (stamina.Sprinting && level < pantStart) panting = true;
        if (level >= 1f) panting = false;

        // one curve for the way down and the way back up: from Pant Start to empty it rises to its loudest, and above
        // Pant Start (only reached while recovering) it fades to nothing at full stamina
        float target = 0f;
        if (panting)
            target = level < pantStart
                ? Mathf.Lerp(pantVolume.x, pantVolume.y, Mathf.InverseLerp(pantStart, 0f, level))
                : Mathf.Lerp(pantVolume.x, 0f, Mathf.InverseLerp(pantStart, 1f, level));
        PantTarget = target;
        if (GameAudio.Instance != null) GameAudio.Instance.SetPanting(target, pantFade);

        // out of breath: zombies close by can hear it
        if (!stamina.Exhausted)
        {
            pantNoiseTimer = 0f;
            return;
        }
        pantNoiseTimer -= dt;
        if (pantNoiseTimer > 0f) return;
        pantNoiseTimer = Mathf.Max(0.1f, pantNoiseInterval);
        PantNoisePulses++;
        NoiseSystem.MakeNoise(transform.position, pantNoiseRadius, NoiseKind.Player, transform);
    }

    void HandleExhausted()
    {
        Play("Player/Gasp", gasp);
    }

    // After HeadBob (execution order -100) has moved its step counter on.
    void LateUpdate()
    {
        if (headBob == null || controller == null) return;
        if (controller.isGrounded) lastGroundedTime = Time.time;

        float phase = headBob.StepPhase;
        int step = StepIndex(phase);
        if (step == lastStep) return;
        lastStep = step;

        // how far past the dip this frame landed, as time
        float speed = movement != null ? movement.CurrentGroundSpeed : controller.velocity.magnitude;
        float past = Mathf.Repeat(phase + (1f - stepPhaseOffset), 1f);
        LastStepLateness = speed > 0.1f ? past * headBob.CurrentStride / speed : 0f;
        MaxStepLateness = Mathf.Max(MaxStepLateness, LastStepLateness);
        TotalStepLateness += LastStepLateness;
        Footstep();
    }

    void Footstep()
    {
        bool crouching = scriptedCrouch || (movement != null && movement.IsCrouching);
        bool airborne = Time.time - lastGroundedTime > groundedGrace;
        if (crouching || airborne)
        {
            SilentSteps++;
            return;
        }

        FootstepCount++;
        var audio = GameAudio.Instance;
        if (audio == null) return;
        bool sprinting = movement != null && movement.IsSprinting;
        if (sprinting) RunSteps++;
        else WalkSteps++;
        float volume = (sprinting ? runVolume : walkVolume) * (1f + Random.Range(-stepVolumeVariation, stepVolumeVariation));
        Sfx step = sprinting ? Sfx.Pick(runFootsteps, ref lastRunFootstep) : Sfx.Pick(footsteps, ref lastFootstep);
        // no running clips yet: the walking ones stand in
        if (step == null && sprinting) step = Sfx.Pick(footsteps, ref lastFootstep);
        audio.Play2D(sprinting ? "Player/FootstepRun" : "Player/Footstep", step, audio.sfxGroup, false, volume);
    }

    void HandleFired() => Play("Player/Gunshot", gunshot);
    void HandleDryFire() => Play("Player/EmptyClick", emptyClick);
    void HandleTorchToggled(bool on) => Play(on ? "Player/TorchOn" : "Player/TorchOff", torchClick);

    void HandleReloadStarted()
    {
        reloadStarted = Time.time;
        reloadVoice = Play("Player/Reload", reload);
    }

    void HandleReloadFinished()
    {
        // put away mid-reload: the sound stops with it
        bool cancelled = pistol != null && Time.time < reloadStarted + pistol.reloadTime - 0.05f;
        if (cancelled && reloadVoice != null && reloadVoice.Current == reload) reloadVoice.FadeOut(0.05f);
        reloadVoice = null;
    }

    SfxVoice Play(string role, Sfx sfx)
    {
        var audio = GameAudio.Instance;
        return audio != null ? audio.Play2D(role, sfx, audio.sfxGroup) : null;
    }
}
