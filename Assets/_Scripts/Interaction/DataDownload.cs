using UnityEngine;

// The research data (the data processor in the testing lab). Hold E to download: a bar on the HUD fills over
// Download Time. Letting go, looking away or stepping out of reach pauses it and the progress is kept. It needs power,
// and it is loud: a noise pulse goes out every Noise Interval seconds of downloading.
public class DataDownload : Interactable
{
    [Tooltip("Seconds of holding E for the whole download.")]
    [Min(0.1f)] public float downloadTime = 5f;
    [Tooltip("Seconds of downloading between noise pulses.")]
    [Min(0.1f)] public float noiseInterval = 1.5f;
    [Tooltip("How far each pulse carries (metres).")]
    public float noiseRadius = 12f;

    float progress;   // seconds downloaded so far
    float noiseTimer; // seconds downloaded since the last pulse (kept across pauses)
    int lastHoldFrame = -10;
    bool barShown;

    public bool IsComplete { get; private set; }
    public float Progress01 => Mathf.Clamp01(progress / downloadTime);
    public bool IsDownloading => !IsComplete && Time.frameCount - lastHoldFrame <= 1;
    public int NoisePulses { get; private set; }

    public override string PromptText
    {
        get
        {
            if (IsComplete) return "Data downloaded";
            if (!PowerState.IsPowered) return "No power";
            int percent = Mathf.FloorToInt(Progress01 * 100f);
            if (IsDownloading) return "Downloading... " + percent + "%";
            return progress > 0f ? "[E] Hold to resume download (" + percent + "%)" : "[E] Hold to download data";
        }
    }

    public override bool CanInteract => !IsComplete && PowerState.IsPowered;

    // The press itself does nothing: Hold, called every frame E stays down, does the work.
    public override void Interact() { }

    public override void Hold(float deltaTime)
    {
        if (!CanInteract || deltaTime <= 0f) return;
        lastHoldFrame = Time.frameCount;

        deltaTime = Mathf.Min(deltaTime, downloadTime - progress);
        progress += deltaTime;
        noiseTimer += deltaTime;
        while (noiseTimer >= noiseInterval)
        {
            noiseTimer -= noiseInterval;
            NoisePulses++;
            NoiseSystem.MakeNoise(transform.position, noiseRadius);
        }

        if (progress >= downloadTime)
        {
            progress = downloadTime;
            IsComplete = true;
            ShowBar(false);
            if (GameProgress.Instance != null) GameProgress.Instance.Complete(Objective.Data);
            else Debug.LogWarning("[DataDownload] Finished, but there is no GameProgress in the scene to record it.");
            return;
        }
        ShowBar(true);
    }

    void Update()
    {
        if (barShown && !IsDownloading) ShowBar(false);
    }

    void ShowBar(bool show)
    {
        barShown = show;
        GameHUD.Progress(show ? Progress01 : -1f);
    }

    void OnEnable()
    {
        GameProgress.OnProgressReset += HandleReset;
    }

    void OnDisable()
    {
        GameProgress.OnProgressReset -= HandleReset;
        if (barShown) ShowBar(false);
    }

    void HandleReset()
    {
        progress = 0f;
        noiseTimer = 0f;
        NoisePulses = 0;
        IsComplete = false;
        lastHoldFrame = -10;
        if (barShown) ShowBar(false);
    }
}
