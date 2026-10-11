using System;

// Infection: the last life went to a zombie. Fall: the last life went to a fall.
public enum RunOutcome { InProgress, Won, PortalFailure, Infection, Fall }

// The numbers of one run. The game manager keeps them up to date while the run is going and shows them on the end
// screen; the achievements system can read them from GameManager.Instance.Stats, or take them from
// GameManager.OnRunEnded. A new run (after Restart) starts with a new, empty RunStats.
[Serializable]
public class RunStats
{
    public const int ObjectiveCount = 3;

    public RunOutcome outcome = RunOutcome.InProgress;
    // "Easy", "Normal" or "Hard".
    public string difficulty = "";

    // Seconds on the countdown now (or when the run ended).
    public float timeRemaining;
    // Seconds of play so far. Not the same as "start time minus time remaining": penalties take time off the clock.
    public float timeSurvived;

    public int livesLeft;
    // Deaths by cause: caught by a zombie, and falls (the Bioreactors pit, out of bounds).
    public int catches;
    public int falls;
    public int Deaths => catches + falls;

    public int zombiesKilled;
    public int shotsFired;
    // Shots that hit a zombie (anything IDamageable).
    public int shotsHit;

    // Times the player ran their stamina down to nothing.
    public int timesExhausted;

    public int objectivesCompleted;
    public bool escaped;

    // 0..1. No shots fired counts as 0.
    public float Accuracy => shotsFired > 0 ? (float)shotsHit / shotsFired : 0f;

    public bool Ended => outcome != RunOutcome.InProgress;
}
