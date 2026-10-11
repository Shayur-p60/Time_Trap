using UnityEngine;

// One difficulty level: the numbers the game manager and the zombie spawner take when Play is pressed. Three assets
// (Easy, Normal, Hard) sit in Assets/_Data/Difficulty and are listed on the GameManager. Normal holds the values the
// game was tuned with.
[CreateAssetMenu(fileName = "Difficulty", menuName = "Time Trap/Difficulty Settings")]
public class DifficultySettings : ScriptableObject
{
    public string displayName = "Normal";

    [Header("Run")]
    [Tooltip("Seconds on the clock at the start of a run.")]
    public float timerSeconds = 300f;
    public int lives = 3;
    [Tooltip("Seconds a catch or a fall takes off the clock.")]
    public float penaltySeconds = 10f;

    [Header("Zombies")]
    [Tooltip("Most zombies alive at once in Stage 1, 2 and 3.")]
    public int capStage1 = 4;
    public int capStage2 = 6;
    public int capStage3 = 10;
    [Tooltip("Multiplier on every zombie's sight range. Stacks with the Stage 3 multiplier.")]
    public float sightMultiplier = 1f;
    [Tooltip("Multiplier on wander, investigate and chase speed.")]
    public float speedMultiplier = 1f;
    [Tooltip("First stage a brute may spawn in. 0 = never.")]
    [Range(0, 3)] public int bruteFromStage = 3;

    public int CapFor(GameStage stage)
    {
        switch (stage)
        {
            case GameStage.Stage3: return capStage3;
            case GameStage.Stage2: return capStage2;
            default: return capStage1;
        }
    }

    public bool BruteIn(GameStage stage)
    {
        return bruteFromStage > 0 && (int)stage >= bruteFromStage;
    }
}

// The difficulty the player picked, for anything that wants to show or use it (HUD, pause strip, end screen,
// achievements). The choice is kept in PlayerPrefs; the asset behind it is set by the GameManager when the scene
// loads. Neither is touched by GameManager.ResetAllStatics, so Play Again keeps the same difficulty.
public static class Difficulty
{
    public const int Easy = 0, Normal = 1, Hard = 2;
    public const int Count = 3;
    const string PrefKey = "tt.difficulty";
    static readonly string[] names = { "Easy", "Normal", "Hard" };

    // Null in a scene whose GameManager has no difficulty assets: the inspector values are used as they are.
    public static DifficultySettings Current { get; private set; }

    public static int Index
    {
        get { return Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, Normal), 0, Count - 1); }
        set
        {
            PlayerPrefs.SetInt(PrefKey, Mathf.Clamp(value, 0, Count - 1));
            PlayerPrefs.Save();
        }
    }

    public static string Name => Current != null && !string.IsNullOrEmpty(Current.displayName) ? Current.displayName : names[Index];
    public static bool IsHard => Index == Hard;

    // Picks the asset for the saved choice out of the GameManager's list (Easy, Normal, Hard, in that order).
    public static DifficultySettings Select(DifficultySettings[] list)
    {
        Current = list != null && Index < list.Length ? list[Index] : null;
        return Current;
    }
}
