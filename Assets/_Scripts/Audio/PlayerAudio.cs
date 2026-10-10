using UnityEngine;

// The player's own sounds, all 2D through the SFX group (GameAudio's voices). Goes on the FirstPersonController.
//
//   Footsteps  one per HeadBob step (the same distance-walked stride), so they speed up with sprint and Speed Boost.
//              Louder when sprinting, quieter when walking, silent when crouching, none in the air.
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

    [Header("Footsteps")]
    public Sfx[] footsteps = new Sfx[0];
    [Range(0f, 1f)] public float walkVolume = 0.4f;
    [Range(0f, 1f)] public float sprintVolume = 0.7f;
    public float sprintPitch = 1.08f;
    [Tooltip("Seconds off the ground before a step counts as airborne (stairs and small ledges stay audible).")]
    public float groundedGrace = 0.1f;

    [Header("Pistol")]
    public Sfx gunshot = new Sfx();
    public Sfx reload = new Sfx();
    public Sfx emptyClick = new Sfx();

    [Header("Torch")]
    public Sfx torchClick = new Sfx();

    // For tests: counts as holding the crouch key (the key itself cannot be pressed from a script).
    [System.NonSerialized] public bool scriptedCrouch;

    // For tests.
    public int FootstepCount { get; private set; }
    // Steps that made no sound because the player was crouching or in the air.
    public int SilentSteps { get; private set; }

    int lastStep;
    int lastFootstep = -1;
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
        if (headBob != null) lastStep = Mathf.FloorToInt(headBob.StepPhase);
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
    }

    // After HeadBob (execution order -100) has moved its step counter on.
    void LateUpdate()
    {
        if (headBob == null || controller == null) return;
        if (controller.isGrounded) lastGroundedTime = Time.time;

        // whole numbers of the step counter are footfalls
        int step = Mathf.FloorToInt(headBob.StepPhase);
        if (step == lastStep) return;
        lastStep = step;
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
        audio.Play2D(sprinting ? "Player/FootstepSprint" : "Player/Footstep", Sfx.Pick(footsteps, ref lastFootstep), audio.sfxGroup,
            false, sprinting ? sprintVolume : walkVolume, sprinting ? sprintPitch : 1f);
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
