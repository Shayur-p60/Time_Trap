using UnityEngine;

// The green "power restore" button on the generator console. One use: after a short delay (the powered lights fade up
// during it) the facility power comes on, which starts Stage 2 for everything that listens to PowerState.
public class GeneratorConsole : Interactable
{
    [Tooltip("Seconds between pressing the button and the power coming on.")]
    public float powerUpDelay = 1.5f;

    bool used;
    float timer = -1f;

    public bool IsStarting => timer >= 0f;

    public override string PromptText
    {
        get
        {
            if (PowerState.IsPowered) return "Power online";
            return IsStarting ? "Restoring power..." : "[E] Restore power";
        }
    }

    public override bool CanInteract => !used && !PowerState.IsPowered;

    public override void Interact()
    {
        if (!CanInteract) return;
        used = true;
        timer = Mathf.Max(0f, powerUpDelay);
        if (PowerLighting.Instance != null) PowerLighting.Instance.BeginPowerUp(timer);
    }

    void Update()
    {
        Tick(Time.deltaTime);
    }

    // Public so the countdown can be stepped without Play mode.
    public void Tick(float deltaTime)
    {
        if (timer < 0f) return;
        timer -= deltaTime;
        if (timer > 0f) return;
        timer = -1f;
        PowerState.SetPowered(true);
    }
}
