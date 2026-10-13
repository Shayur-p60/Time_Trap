using System;
using System.Collections.Generic;
using UnityEngine;

// One achievement: what it is called, how it is earned and what it is worth.
public class AchievementDef
{
    public readonly string id;
    public readonly string name;
    public readonly string description;
    // Time Points.
    public readonly int points;
    // Shown as "???" on the achievements screen until it is unlocked.
    public readonly bool hidden;

    public AchievementDef(string id, string name, string description, int points, bool hidden = false)
    {
        this.id = id;
        this.name = name;
        this.description = description;
        this.points = points;
        this.hidden = hidden;
    }
}

// The achievements and everything about them that is saved: which are unlocked, the kills made across all runs, the
// personal bests and the golden-beam switch. All of it lives in PlayerPrefs under "tt.progress.", and only
// ResetAll (Settings -> Reset progress) clears it. GameManager.ResetAllStatics runs on every scene reload and only
// calls ResetForNewRun here, which forgets what was unlocked "this run" and nothing else.
//
// The conditions themselves are watched by AchievementTracker in the scene; this class is the list and the save.
public static class Achievements
{
    public const string Escape = "escape", AgainstClock = "against_clock", LastSecond = "last_second", Untouchable = "untouchable";
    public const string Pacifist = "pacifist", NeverTired = "never_tired", Nightmare = "nightmare", GiantSlayer = "giant_slayer";
    public const string Headhunter = "headhunter", Erased = "erased", Frozen = "frozen", Blinded = "blinded", FullyStocked = "fully_stocked";
    public const string WatchStep = "watch_step", PatientZero = "patient_zero", RestInPieces = "rest_in_pieces", TimeLord = "time_lord";

    public const int ErasedKills = 50, HeadhunterKills = 10, CrowdSize = 3;
    public const float AgainstClockSeconds = 90f, LastSecondSeconds = 10f;

    public static readonly AchievementDef[] All =
    {
        new AchievementDef(Escape, "Containment Successful", "Escape through the portal.", 30),
        new AchievementDef(AgainstClock, "Against the Clock", "Escape with 1:30 or more left.", 80),
        new AchievementDef(LastSecond, "Last Second", "Escape with under 10 seconds left.", 50),
        new AchievementDef(Untouchable, "Untouchable", "Escape without being caught or dying from a fall.", 100),
        new AchievementDef(Pacifist, "Pacifist", "Escape without firing a shot.", 80),
        new AchievementDef(NeverTired, "Never Tired", "Escape without becoming exhausted.", 60),
        new AchievementDef(Nightmare, "Nightmare", "Escape on Hard.", 120),
        new AchievementDef(GiantSlayer, "Giant Slayer", "Kill a Bloater brute.", 60),
        new AchievementDef(Headhunter, "Headhunter", "10 headshot kills in one run.", 50),
        new AchievementDef(Erased, "Erased From Time", "50 zombie kills in total, across all runs.", 60),
        new AchievementDef(Frozen, "Frozen in Time", "Freeze 3 or more zombies that are hunting you with one Zombie Freeze.", 40),
        new AchievementDef(Blinded, "Blinded", "Stun 3 or more zombies that are hunting you with one Flash Pulse.", 40),
        new AchievementDef(FullyStocked, "Fully Stocked", "Pick up all three power-up types in one run.", 40),
        new AchievementDef(WatchStep, "Watch Your Step", "Die from a fall.", 15, true),
        new AchievementDef(PatientZero, "Patient Zero", "Lose to infection.", 15),
        new AchievementDef(RestInPieces, "Rest in Pieces", "Crouch three times in two seconds over a zombie you just killed.", 20, true),
        new AchievementDef(TimeLord, "Time Lord", "Unlock every other achievement.", 140),
    };

    const string Prefix = "tt.progress.";
    const string UnlockKey = Prefix + "ach.", KillsKey = Prefix + "kills", GoldenKey = Prefix + "goldenBeam";
    const string BestTimeKey = Prefix + "bestTime", BestKillsKey = Prefix + "bestKills", BestAccuracyKey = Prefix + "bestAccuracy";

    // An achievement has just been unlocked (and saved).
    public static event Action<AchievementDef> OnUnlocked;
    // Saved progress was wiped.
    public static event Action OnReset;

    // The biohazard icon every achievement is drawn with, tinted by Tint. Set by the AchievementTracker.
    public static Sprite Icon;

    // Unlocked since the scene loaded, in order: the end screen lists these.
    public static readonly List<AchievementDef> UnlockedThisRun = new List<AchievementDef>();

    public static int Count => All.Length;

    public static int TotalPoints
    {
        get
        {
            int total = 0;
            foreach (var a in All) total += a.points;
            return total;
        }
    }

    public static int UnlockedCount
    {
        get
        {
            int n = 0;
            foreach (var a in All) if (IsUnlocked(a.id)) n++;
            return n;
        }
    }

    public static int UnlockedPoints
    {
        get
        {
            int total = 0;
            foreach (var a in All) if (IsUnlocked(a.id)) total += a.points;
            return total;
        }
    }

    public static AchievementDef Find(string id)
    {
        foreach (var a in All) if (a.id == id) return a;
        return null;
    }

    public static bool IsUnlocked(string id)
    {
        return PlayerPrefs.GetInt(UnlockKey + id, 0) == 1;
    }

    // Unlocks and saves it. False if there is no such achievement or it was unlocked already. Time Lord follows by
    // itself once the other sixteen are in.
    public static bool Unlock(string id)
    {
        AchievementDef def = Find(id);
        if (def == null || IsUnlocked(id)) return false;
        PlayerPrefs.SetInt(UnlockKey + id, 1);
        PlayerPrefs.Save();
        UnlockedThisRun.Add(def);
        Debug.Log("[Achievements] Unlocked: " + def.name + " (" + def.points + " TP). " + UnlockedCount + " of " + Count + ".");
        OnUnlocked?.Invoke(def);

        if (id != TimeLord && !IsUnlocked(TimeLord) && UnlockedCount >= Count - 1) Unlock(TimeLord);
        return true;
    }

    // Amber for an ordinary achievement, cyan for a hidden one, gold for Time Lord, grey while locked.
    public static Color Tint(AchievementDef def, bool unlocked)
    {
        if (!unlocked) return new Color(0.42f, 0.44f, 0.48f, 1f);
        if (def.id == TimeLord) return TimeTrap.UI.MenuKit.Gold;
        return def.hidden ? TimeTrap.UI.MenuKit.Cyan : TimeTrap.UI.MenuKit.Amber;
    }

    // ------------------------------------------------------------------ kills across runs, personal bests

    public static int TotalKills => PlayerPrefs.GetInt(KillsKey, 0);

    public static int AddKill()
    {
        int kills = TotalKills + 1;
        PlayerPrefs.SetInt(KillsKey, kills);
        return kills;
    }

    // -1 while there is no record yet.
    public static float BestTimeLeft => PlayerPrefs.GetFloat(BestTimeKey, -1f);
    public static int BestKills => PlayerPrefs.GetInt(BestKillsKey, -1);
    public static float BestAccuracy => PlayerPrefs.GetFloat(BestAccuracyKey, -1f);

    // Fewer shots than this and the accuracy is not a record (one lucky shot is not 100%).
    public const int MinShotsForAccuracy = 5;

    // Compares a finished run with the saved records, saves the ones it beat and says which those were. Time left
    // only counts on an escape.
    public static void SubmitRun(RunStats stats, out bool bestTime, out bool bestKills, out bool bestAccuracy)
    {
        bestTime = stats.escaped && stats.timeRemaining > BestTimeLeft;
        bestKills = stats.zombiesKilled > 0 && stats.zombiesKilled > BestKills;
        bestAccuracy = stats.shotsFired >= MinShotsForAccuracy && stats.Accuracy > BestAccuracy;
        if (bestTime) PlayerPrefs.SetFloat(BestTimeKey, stats.timeRemaining);
        if (bestKills) PlayerPrefs.SetInt(BestKillsKey, stats.zombiesKilled);
        if (bestAccuracy) PlayerPrefs.SetFloat(BestAccuracyKey, stats.Accuracy);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------------ golden beam (the Time Lord reward)

    public static bool GoldenBeamAvailable => IsUnlocked(TimeLord);

    // The switch in Settings -> Display. On by default once Time Lord is unlocked.
    public static bool GoldenBeamEnabled
    {
        get { return PlayerPrefs.GetInt(GoldenKey, 1) == 1; }
        set
        {
            PlayerPrefs.SetInt(GoldenKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool GoldenBeamActive => GoldenBeamAvailable && GoldenBeamEnabled;

    // ------------------------------------------------------------------ resets

    // Settings -> Reset progress: every unlock, the kill count, the records and the golden beam.
    public static void ResetAll()
    {
        foreach (var a in All) PlayerPrefs.DeleteKey(UnlockKey + a.id);
        foreach (string key in new[] { KillsKey, GoldenKey, BestTimeKey, BestKillsKey, BestAccuracyKey }) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();
        UnlockedThisRun.Clear();
        Debug.Log("[Achievements] Progress reset.");
        OnReset?.Invoke();
    }

    // A scene reload (GameManager.ResetAllStatics) or a new play session: listeners and this run's list go; nothing
    // that is saved is touched.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewRun()
    {
        OnUnlocked = null;
        OnReset = null;
        UnlockedThisRun.Clear();
    }
}
