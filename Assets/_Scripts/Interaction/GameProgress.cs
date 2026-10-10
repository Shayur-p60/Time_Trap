using System;
using UnityEngine;

public enum GameStage { Stage1 = 1, Stage2 = 2, Stage3 = 3 }

public enum Objective { Power, Pathogen, Data }

// Tracks what the player has done and which stage the game is in. One per scene.
//   Stage 1: no power (game start).
//   Stage 2: power restored. Follows PowerState, so the generator console needs no extra wiring.
//   Stage 3: power, the pathogen sample and the research data are all in hand (any order). The escape portal opens.
// Progress is latched: once the power has been restored HasPower stays true and the stage never goes back, even if
// PowerState is switched off later. Only ResetProgress clears it.
// The events are static, so listeners can subscribe before or after this object wakes up.
[DefaultExecutionOrder(-50)]
public class GameProgress : MonoBehaviour
{
    public static GameProgress Instance { get; private set; }

    public static event Action<GameStage> OnStageChanged;
    public static event Action<Objective> OnObjectiveCompleted;
    public static event Action OnPlayerEscaped;
    // Raised by ResetProgress, so the sample, the download and the checkpoints can go back to their start state.
    public static event Action OnProgressReset;

    public bool HasPower { get; private set; }
    public bool HasPathogen { get; private set; }
    public bool HasData { get; private set; }
    public bool HasEscaped { get; private set; }
    public GameStage Stage { get; private set; } = GameStage.Stage1;

    public bool AllObjectivesComplete => HasPower && HasPathogen && HasData;

    public bool IsComplete(Objective objective)
    {
        switch (objective)
        {
            case Objective.Power: return HasPower;
            case Objective.Pathogen: return HasPathogen;
            default: return HasData;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        PowerState.OnPowerChanged += HandlePowerChanged;
        if (PowerState.IsPowered) HasPower = true;
        UpdateStage();
    }

    void OnDisable()
    {
        PowerState.OnPowerChanged -= HandlePowerChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static GameStage StageFor(bool power, bool pathogen, bool data)
    {
        if (!power) return GameStage.Stage1;
        return pathogen && data ? GameStage.Stage3 : GameStage.Stage2;
    }

    // Marks an objective as done. False if it already was. Power goes through PowerState, so the lights follow.
    public bool Complete(Objective objective)
    {
        if (IsComplete(objective)) return false;
        if (objective == Objective.Power)
        {
            PowerState.SetPowered(true); // comes back through HandlePowerChanged
            return HasPower;
        }

        if (objective == Objective.Pathogen) HasPathogen = true;
        else HasData = true;
        OnObjectiveCompleted?.Invoke(objective);
        UpdateStage();
        return true;
    }

    void HandlePowerChanged(bool powered)
    {
        // latched: losing power later does not undo the objective
        if (!powered || HasPower) return;
        HasPower = true;
        OnObjectiveCompleted?.Invoke(Objective.Power);
        UpdateStage();
    }

    void UpdateStage()
    {
        GameStage stage = StageFor(HasPower, HasPathogen, HasData);
        if (stage <= Stage) return; // forwards only
        Stage = stage;
        OnStageChanged?.Invoke(stage);
    }

    // Called by the escape portal. Raises once per run.
    public void RaisePlayerEscaped()
    {
        if (HasEscaped) return;
        HasEscaped = true;
        Debug.Log("[GameProgress] Player escaped through Gate B.");
        OnPlayerEscaped?.Invoke();
    }

    // Back to the start of a run: Stage 1, power off, nothing collected.
    public static void ResetProgress()
    {
        var progress = Instance;
        bool stageChanged = false;
        if (progress != null)
        {
            progress.HasPower = false;
            progress.HasPathogen = false;
            progress.HasData = false;
            progress.HasEscaped = false;
            stageChanged = progress.Stage != GameStage.Stage1;
            progress.Stage = GameStage.Stage1;
        }
        PowerState.SetPowered(false);
        if (stageChanged) OnStageChanged?.Invoke(GameStage.Stage1);
        OnProgressReset?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        Instance = null;
        OnStageChanged = null;
        OnObjectiveCompleted = null;
        OnPlayerEscaped = null;
        OnProgressReset = null;
    }
}
