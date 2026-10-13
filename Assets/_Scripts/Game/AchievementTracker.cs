using System.Collections.Generic;
using TimeTrap.UI;
using UnityEngine;

// Watches the game for the achievements' conditions and unlocks them (Achievements holds the list and the save).
// One per scene, on Map/Gameplay/Achievements. It also owns the unlock pop-up (AchievementToast).
//
//   End of a run   the escape achievements, Patient Zero, and the personal bests (GameManager.OnRunEnded).
//   Kills          Erased From Time (saved across runs), Headhunter, Giant Slayer; each body is remembered for
//                  Rest in Pieces until it has dissolved.
//   Power-ups      Frozen in Time and Blinded count the zombies one use affects that were on to the player (not
//                  ones only wandering); Fully Stocked needs Flash Pulse,
//                  Speed Boost and Zombie Freeze in one run (the spawner places one of each every run).
//   Falls          Watch Your Step: any death that is not a zombie's.
//   Crouching      Rest in Pieces: three crouch-downs within two seconds beside a fresh body.
// Nothing unlocks while the game is paused: a request made then waits for the resume. After the run has ended the
// unlock still happens, but the pop-up leaves it to the end screen's list.
[DefaultExecutionOrder(-175)] // before GameMenus builds the achievements screen
public class AchievementTracker : MonoBehaviour
{
    public static AchievementTracker Instance { get; private set; }

    [Tooltip("The biohazard icon (white on transparent). Tinted amber, cyan, gold or grey where it is shown.")]
    public Sprite icon;
    [Tooltip("Played on the UI mixer group with the pop-up. Empty = GameAudio's objective sound (UI_Collect).")]
    public Sfx chime = new Sfx();

    [Header("Rest in Pieces")]
    public int crouchesNeeded = 3;
    [Tooltip("Seconds the crouches have to fit in.")]
    public float crouchWindow = 2f;
    [Tooltip("How close to the body the player has to be (metres).")]
    public float corpseDistance = 1.5f;

#if UNITY_EDITOR
    [Header("Testing (editor only: use the component's context menu)")]
    public string unlockId = "escape";
#endif

    public AchievementToast Toast { get; private set; }

    // Set when a run ends: which saved records it beat.
    public bool NewBestTime { get; private set; }
    public bool NewBestKills { get; private set; }
    public bool NewBestAccuracy { get; private set; }
    // Zombies the last Zombie Freeze and the last Flash Pulse affected.
    public int LastFreezeCount { get; private set; }
    public int LastFlashCount { get; private set; }
    // Requests waiting for the game to be resumed.
    public int HeldCount => held.Count;
    public int FreshCorpses { get { Prune(); return corpses.Count; } }
    public int CrouchesCounted => crouchTimes.Count;

    GameManager manager;
    PlayerMovementController movement;
    readonly List<string> held = new List<string>();
    readonly List<ZombieAI> corpses = new List<ZombieAI>();
    readonly List<float> crouchTimes = new List<float>();
    bool wasCrouching;
    // the zombies that were on to the player at the end of the last frame (see HandleFlashPulse)
    readonly HashSet<ZombieAI> wasAware = new HashSet<ZombieAI>();
    // the zombies a Flash Pulse stunned while they were on to the player; dropped when the stun ends
    readonly HashSet<ZombieAI> stunnedMidHunt = new HashSet<ZombieAI>();

    const int ThreePowerUps = (1 << (int)PowerUpType.FlashPulse) | (1 << (int)PowerUpType.SpeedBoost) | (1 << (int)PowerUpType.ZombieFreeze);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }
        Instance = this;
        Achievements.Icon = icon;

        var go = new GameObject("AchievementToast");
        go.transform.SetParent(transform, false);
        Toast = go.AddComponent<AchievementToast>();
        Toast.Build(this);
    }

    void OnEnable()
    {
        PlayerEvents.OnPlayerKilled += HandlePlayerKilled;
        ZombieAI.OnAnyDeathStarted += HandleZombieDied;
        PowerUps.OnZombieFreeze += HandleFreeze;
        PowerUps.OnFlashPulse += HandleFlashPulse;
        PowerUps.OnPowerUpUsed += HandlePowerUpUsed;
    }

    void OnDisable()
    {
        PlayerEvents.OnPlayerKilled -= HandlePlayerKilled;
        ZombieAI.OnAnyDeathStarted -= HandleZombieDied;
        PowerUps.OnZombieFreeze -= HandleFreeze;
        PowerUps.OnFlashPulse -= HandleFlashPulse;
        PowerUps.OnPowerUpUsed -= HandlePowerUpUsed;
        if (manager != null) manager.OnRunEnded -= HandleRunEnded;
    }

    void Start()
    {
        manager = GameManager.Instance;
        if (manager != null) manager.OnRunEnded += HandleRunEnded;
        movement = FindAnyObjectByType<PlayerMovementController>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetForNewPlaySession()
    {
        Instance = null;
    }

    bool Paused => manager != null && manager.State == GameState.Paused;
    bool Playing => manager == null || manager.State == GameState.Playing;

    // Unlocks an achievement, or holds the request until the game is resumed.
    public void Request(string id)
    {
        if (Achievements.IsUnlocked(id) || held.Contains(id)) return;
        if (Paused) held.Add(id);
        else Achievements.Unlock(id);
    }

    void Update()
    {
        if (held.Count > 0 && !Paused)
        {
            var waiting = new List<string>(held);
            held.Clear();
            foreach (string id in waiting) Achievements.Unlock(id);
        }

        // Rest in Pieces counts the moments the crouch key goes down, not the frames it is held
        bool crouching = movement != null && !GameManager.InputBlocked && movement.IsCrouching;
        if (crouching && !wasCrouching) RegisterCrouch();
        wasCrouching = crouching;
    }

    void LateUpdate()
    {
        wasAware.Clear();
        foreach (var z in ZombieAI.All)
            if (IsAware(z)) wasAware.Add(z);
        stunnedMidHunt.RemoveWhere(z => z == null || z.IsDead || z.CurrentState != ZombieAI.State.Stunned);
    }

    // ------------------------------------------------------------------ the end of a run

    void HandleRunEnded(RunStats stats)
    {
        if (stats == null) return;
        if (stats.escaped)
        {
            Request(Achievements.Escape);
            if (stats.timeRemaining >= Achievements.AgainstClockSeconds) Request(Achievements.AgainstClock);
            if (stats.timeRemaining < Achievements.LastSecondSeconds) Request(Achievements.LastSecond);
            if (stats.catches == 0 && stats.falls == 0) Request(Achievements.Untouchable);
            if (stats.shotsFired == 0) Request(Achievements.Pacifist);
            if (stats.timesExhausted == 0) Request(Achievements.NeverTired);
            if (stats.difficulty == "Hard") Request(Achievements.Nightmare);
        }
        else if (stats.outcome == RunOutcome.Infection) Request(Achievements.PatientZero);

        Achievements.SubmitRun(stats, out bool time, out bool kills, out bool accuracy);
        NewBestTime = time;
        NewBestKills = kills;
        NewBestAccuracy = accuracy;
    }

    // ------------------------------------------------------------------ kills

    // GameManager hears the same event first, so the run's counts already include this kill.
    void HandleZombieDied(ZombieAI zombie)
    {
        if (zombie == null || !Playing) return;
        if (Achievements.AddKill() >= Achievements.ErasedKills) Request(Achievements.Erased);
        if (zombie.IsBrute) Request(Achievements.GiantSlayer);
        if (manager != null && manager.Stats.headshotKills >= Achievements.HeadhunterKills) Request(Achievements.Headhunter);
        corpses.Add(zombie);
    }

    // Bodies that are gone, or have finished dissolving, no longer count.
    void Prune()
    {
        for (int i = corpses.Count - 1; i >= 0; i--)
        {
            ZombieAI z = corpses[i];
            if (z == null || !z.IsDead || Dissolved(z)) corpses.RemoveAt(i);
        }
    }

    static bool Dissolved(ZombieAI zombie)
    {
        var fx = zombie.GetComponent<ZombieShaderFX>();
        return fx != null && fx.Dissolve >= 1f;
    }

    public bool NearFreshCorpse(Vector3 position)
    {
        Prune();
        foreach (var z in corpses)
            if (Vector3.Distance(z.transform.position, position) <= corpseDistance) return true;
        return false;
    }

    // The crouch key went down. True if it counted (the player is beside a fresh body). The tests call this too,
    // since a key press cannot be faked.
    public bool RegisterCrouch()
    {
        Transform player = PlayerCatch.Player;
        if (player == null || !Playing || !NearFreshCorpse(player.position)) return false;

        float now = Time.time;
        crouchTimes.Add(now);
        crouchTimes.RemoveAll(t => now - t > crouchWindow);
        if (crouchTimes.Count >= crouchesNeeded) Request(Achievements.RestInPieces);
        return true;
    }

    // ------------------------------------------------------------------ power-ups

    // A zombie that is on to the player: chasing, attacking, alerted, investigating a noise, searching, or at a door
    // on its way. One that is only wandering does not count towards Frozen in Time or Blinded, so those take a real
    // crowd and not just three zombies somewhere on the map. (One a Flash Pulse stunned mid-hunt still counts for a
    // freeze: see StunnedMidHunt.)
    public static bool IsAware(ZombieAI zombie)
    {
        if (zombie == null || zombie.IsDead) return false;
        switch (zombie.CurrentState)
        {
            case ZombieAI.State.Chase:
            case ZombieAI.State.Attack:
            case ZombieAI.State.Alert:
            case ZombieAI.State.Investigate:
            case ZombieAI.State.Search:
            case ZombieAI.State.Door:
                return true;
            default:
                return false;
        }
    }

    // Stunned by a Flash Pulse while it was on to the player, and still stunned.
    bool StunnedMidHunt(ZombieAI zombie)
    {
        return zombie != null && !zombie.IsDead && zombie.CurrentState == ZombieAI.State.Stunned && stunnedMidHunt.Contains(zombie);
    }

    // Zombie Freeze stops every living zombie; the ones that were on to the player are the ones it counts, and so
    // are the ones a Flash Pulse stunned in the middle of hunting.
    void HandleFreeze(float duration)
    {
        if (!Playing) return;
        int frozen = 0;
        foreach (var z in ZombieAI.All)
            if (IsAware(z) || StunnedMidHunt(z)) frozen++;
        LastFreezeCount = frozen;
        if (manager != null) manager.Stats.mostFrozen = Mathf.Max(manager.Stats.mostFrozen, LastFreezeCount);
        if (LastFreezeCount >= Achievements.CrowdSize) Request(Achievements.Frozen);
    }

    // Flash Pulse stuns the zombies within their Stun Radius of it (ZombieAI.HandleFlashPulse uses the same rule).
    // Each zombie is asked from its own handler order, so one this pulse has just stunned is counted by what it was
    // doing before: a zombie's handler may have run first and put it in Stunned already.
    void HandleFlashPulse(Vector3 position, float duration)
    {
        if (!Playing) return;
        int count = 0;
        foreach (var z in ZombieAI.All)
        {
            if (z == null || z.IsDead || Vector3.Distance(position, z.transform.position) > z.stunRadius) continue;
            if (!IsAware(z) && !(z.CurrentState == ZombieAI.State.Stunned && wasAware.Contains(z))) continue;
            count++;
            // remembered while the stun lasts: a freeze on the same crowd still counts it
            stunnedMidHunt.Add(z);
        }
        LastFlashCount = count;
        if (manager != null) manager.Stats.mostStunned = Mathf.Max(manager.Stats.mostStunned, count);
        if (count >= Achievements.CrowdSize) Request(Achievements.Blinded);
    }

    void HandlePowerUpUsed(PowerUpType type)
    {
        if (!Playing || manager == null) return;
        if ((manager.Stats.powerUpTypes & ThreePowerUps) == ThreePowerUps) Request(Achievements.FullyStocked);
    }

    // ------------------------------------------------------------------ falls

    void HandlePlayerKilled(string cause)
    {
        // the death may just have ended the run (GameManager hears it first): it still counts
        if (cause != "Zombie") Request(Achievements.WatchStep);
    }

    // ------------------------------------------------------------------ editor helpers

#if UNITY_EDITOR
    [ContextMenu("Achievements: Unlock all")]
    void EditorUnlockAll()
    {
        foreach (var a in Achievements.All) Request(a.id);
    }

    [ContextMenu("Achievements: Reset all")]
    void EditorResetAll()
    {
        Achievements.ResetAll();
    }

    [ContextMenu("Achievements: Unlock by ID (Unlock Id field)")]
    void EditorUnlockById()
    {
        if (Achievements.Find(unlockId) == null) Debug.LogWarning("[Achievements] No achievement with the id '" + unlockId + "'.", this);
        else Request(unlockId);
    }
#endif
}
