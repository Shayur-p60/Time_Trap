using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

// Keeps the facility populated with zombies, by stage. One per scene; nothing is spawned until Play.
//   Stage 1  a few slow wanderers.
//   Stage 2  more of them; the generator starting up makes a noise that draws the nearest ones in.
//   Stage 3  the most, with keener senses, runners placed toward the escape, one brute, and regular pushes toward
//            the player.
// Rules a spawn never breaks: not within Min Player Distance of the player, not where the player can see it, on the
// NavMesh, and never above the stage's Max Alive. When no spawn point passes, the spawner waits and tries again.
// For the first Opening Grace Time of a run nothing spawns within Opening Grace Distance of the player.
// Runners are placed on or beside the walking route from the player to Gate B, never close to the gate itself.
// The difficulty (GameManager.ApplyDifficulty) replaces the stages' Max Alive, multiplies sight and speed, and says
// from which stage a brute may spawn. While the main menu is up nothing spawns at all; the run (and its opening
// grace) starts when the menu closes.
// Each zombie is the Zombie Prefab (the brain) plus one random body from its role's list, so adding a new character
// is one more entry in Bodies.
public class ZombieSpawner : MonoBehaviour
{
    [Serializable]
    public class Role
    {
        public string name = "Regular";
        [Tooltip("Body prefabs for this role: a humanoid model with an Animator using the shared zombie controller. One is picked at random.")]
        public GameObject[] bodies = new GameObject[0];
        [Tooltip("Wander speed: random between these two values (m/s).")]
        public Vector2 wanderSpeed = new Vector2(0.5f, 0.6f);
        [Tooltip("Chase speed: random between these two values (m/s).")]
        public Vector2 chaseSpeed = new Vector2(3.75f, 4.05f);
        [Tooltip("Uniform scale: random between these two values.")]
        public Vector2 scale = new Vector2(0.95f, 1.05f);
        [Tooltip("Optional. One is picked at random and multiplied into the body's base colour. Empty = no tint.")]
        public Color[] tints = new Color[0];
        [Range(0f, 1f)] public float screamChance = 1f;
        public float health = 75f;

        [Header("Optional: 0 keeps the Zombie prefab's value")]
        [Tooltip("Damage multiplier of a head shot.")]
        public float headshotMultiplier = 0f;
        [Tooltip("Seconds between the start of the attack and the strike.")]
        public float windUpTime = 0f;
        [Tooltip("Seconds it is held up by a shot.")]
        public float staggerTime = 0f;
        [Tooltip("Pitch of its whole voice relative to other zombies (0.85 = 15% lower).")]
        public float voicePitch = 0f;
        [Tooltip("Radius and height of the body collider. The NavMeshAgent keeps its own radius, so the zombie still fits wherever the NavMesh goes.")]
        public float bodyRadius = 0f;
        public float bodyHeight = 0f;
        [Tooltip("Radius of the head hitbox.")]
        public float headRadius = 0f;
        [Tooltip("Never spawns closer to the escape point than this (metres).")]
        public float minEscapeDistance = 0f;
    }

    [Serializable]
    public class StageSettings
    {
        [Tooltip("Never more than this many alive at once.")]
        public int maxAlive = 4;
        [Tooltip("Spawned straight away when the stage starts (as far as the rules allow).")]
        public int startAlive = 3;
        [Tooltip("Seconds between spawns once the stage is running.")]
        public float spawnInterval = 12f;
        [Tooltip("How many of the alive zombies may be runners.")]
        public int maxRunners = 0;
        [Tooltip("How many of the alive zombies may be brutes. They count toward Max Alive like any other.")]
        public int maxBrutes = 0;
        [Tooltip("Multiplier on every zombie's sight range.")]
        public float sightMultiplier = 1f;
        [Tooltip("How many zombies at most react to one noise.")]
        public int noiseResponders = 2;
        public float investigateSpeed = 1.4f;
        [Tooltip("Every this many seconds one calm zombie is sent to look at a random spot near the player. 0 = never.")]
        public float huntInterval = 0f;
    }

    [Serializable]
    public class SpawnPoint
    {
        public string label = "";
        public Vector3 position;
        [Tooltip("Not used before this stage.")]
        public GameStage fromStage = GameStage.Stage1;
    }

    [Header("What to spawn")]
    [Tooltip("The zombie without a body: NavMeshAgent, ZombieAI, ZombieSenses, ZombieHealth, collider.")]
    public GameObject zombiePrefab;
    public Role regular = new Role();
    public Role runner = new Role
    {
        name = "Runner",
        wanderSpeed = new Vector2(0.9f, 1.1f),
        chaseSpeed = new Vector2(4.9f, 5.1f),
        scale = new Vector2(0.97f, 1.03f),
        screamChance = 0.4f
    };

    public Role brute = new Role
    {
        name = "Brute",
        wanderSpeed = new Vector2(0.45f, 0.55f),
        chaseSpeed = new Vector2(3.1f, 3.3f),
        scale = new Vector2(1f, 1f),
        health = 190f,
        headshotMultiplier = 4f,
        windUpTime = 1f,
        staggerTime = 0.25f,
        voicePitch = 0.85f,
        bodyRadius = 0.42f,
        // lower than the head, or the collider swallows head shots when it leans back
        bodyHeight = 1.55f,
        headRadius = 0.2f,
        minEscapeDistance = 20f
    };

    [Header("Stages")]
    public StageSettings stage1 = new StageSettings { maxAlive = 4, startAlive = 3, spawnInterval = 15f, noiseResponders = 2 };
    public StageSettings stage2 = new StageSettings { maxAlive = 6, startAlive = 5, spawnInterval = 10f, noiseResponders = 3 };
    public StageSettings stage3 = new StageSettings
    {
        maxAlive = 10, startAlive = 8, spawnInterval = 5f, maxRunners = 3, maxBrutes = 1, sightMultiplier = 1.3f,
        noiseResponders = 4, investigateSpeed = 2.2f, huntInterval = 15f
    };

    [Header("Where")]
    public SpawnPoint[] spawnPoints = new SpawnPoint[0];
    [Tooltip("Gate B. Runners are placed along the walking route from the player to here.")]
    public Transform escapePoint;

    [Header("Rules")]
    [Tooltip("Never spawn closer to the player than this (metres).")]
    public float minPlayerDistance = 15f;
    [Tooltip("For this many seconds after a run starts (or is reset)...")]
    public float openingGraceTime = 20f;
    [Tooltip("...nothing spawns closer to the player than this.")]
    public float openingGraceDistance = 50f;
    [Tooltip("Runners never spawn closer to the escape point than this.")]
    public float runnerEscapeExclusion = 20f;
    [Tooltip("Runners spawn at most this far to the side of the route from the player to the escape point.")]
    public float runnerRouteWidth = 1.5f;
    [Tooltip("Never spawn closer to another zombie than this.")]
    public float minZombieSpacing = 2.5f;
    [Tooltip("What hides a spawn point from the player. Left empty: everything except Zombie, Player, Ignore Raycast, TransparentFX, UI and Interactable.")]
    public LayerMask viewBlockMask;
    [Tooltip("Seconds between attempts while no spawn point passes the rules, and between the spawns of a stage's opening group.")]
    public float retryInterval = 1f;

    [Header("Brute")]
    [Tooltip("After a brute is killed, no new brute spawns for this many seconds. Its slot can go to a regular zombie meanwhile.")]
    public float bruteRespawnDelay = 60f;

    [Header("Power on (Stage 2)")]
    [Tooltip("How far the generator starting up carries. 0 = silent.")]
    public float powerOnNoiseRadius = 35f;
    public int powerOnNoisePulses = 3;
    public float powerOnNoiseInterval = 2f;

    [Header("Recycling")]
    [Tooltip("A zombie that has only wandered for Recycle Calm Time, is further than this from the player and out of sight is removed, so a new one can spawn where it matters. 0 = never.")]
    public float recycleDistance = 45f;
    public float recycleCalmTime = 25f;
    [Tooltip("...and only if a free spawn point is at least this much nearer to the player than the zombie is.")]
    public float recycleGain = 15f;
    [Tooltip("At most one zombie is recycled per this many seconds. Its replacement spawns at the nearest free point.")]
    public float recycleCooldown = 10f;

    [Header("Hunting (stages with a Hunt Interval)")]
    [Tooltip("The zombie is sent to a random NavMesh point within this distance of the player.")]
    public float huntRadius = 10f;

    public bool spawningEnabled = true;

    // For tests.
    public int SpawnCount { get; private set; }
    public int RecycleCount { get; private set; }
    public int RunnerRouteSpawns { get; private set; }
    public Vector3 LastSpawnPosition { get; private set; }
    // Time.time at which the current run started.
    public float RunStartTime { get; private set; }
    public bool InOpeningGrace => Time.time - RunStartTime < openingGraceTime;
    // The distance from the player inside which nothing may spawn right now.
    public float CurrentMinPlayerDistance => InOpeningGrace ? Mathf.Max(minPlayerDistance, openingGraceDistance) : minPlayerDistance;
    // Seconds until a brute may spawn again (0 = now).
    public float BruteReadyIn => Mathf.Max(0f, nextBruteTime - Time.time);
    public int HuntCount { get; private set; }
    public Vector3 LastHuntPoint { get; private set; }
    public string LastSkipReason { get; private set; } = "";
    public GameStage Stage => GameProgress.Instance != null ? GameProgress.Instance.Stage : GameStage.Stage1;
    public StageSettings Current => SettingsFor(Stage);
    // Null: no difficulty, the stage settings are used as they are.
    public DifficultySettings DifficultyInUse { get; private set; }
    public int CurrentMaxAlive => MaxAlive(Stage);
    public int CurrentMaxBrutes => MaxBrutes(Stage);
    public float SightMultiplier => DifficultyInUse != null ? DifficultyInUse.sightMultiplier : 1f;
    public float SpeedMultiplier => DifficultyInUse != null ? DifficultyInUse.speedMultiplier : 1f;

    public int MaxAlive(GameStage stage)
    {
        return DifficultyInUse != null ? DifficultyInUse.CapFor(stage) : SettingsFor(stage).maxAlive;
    }

    public int MaxBrutes(GameStage stage)
    {
        int own = SettingsFor(stage).maxBrutes;
        if (DifficultyInUse == null) return own;
        return DifficultyInUse.BruteIn(stage) ? Mathf.Max(1, own) : 0;
    }

    // Called by the game manager before a run starts. Zombies already alive take the new multipliers.
    public void ApplyDifficulty(DifficultySettings difficulty)
    {
        float oldSpeed = SpeedMultiplier;
        DifficultyInUse = difficulty;
        float change = SpeedMultiplier / Mathf.Max(0.01f, oldSpeed);
        foreach (var z in ZombieAI.All)
        {
            if (z == null || z.IsDead) continue;
            z.wanderSpeed *= change;
            z.chaseSpeed *= change;
            ApplyStage(z, Current);
        }
    }

    float spawnTimer;
    float huntTimer;
    float recycleTimer;
    float nextRecycleTime;
    float nextBruteTime;
    // replacements for recycled zombies: these spawn at the nearest free point instead of a random one
    int nearSpawnsOwed;
    // zombies still owed to the current stage's opening group
    int openingGroup;
    bool heldForMenu;
    Camera viewCamera;
    MaterialPropertyBlock block;
    readonly List<SpawnPoint> valid = new List<SpawnPoint>();
    readonly List<float> weights = new List<float>();
    readonly List<Vector3> route = new List<Vector3>();
    readonly List<Vector3> routeSpots = new List<Vector3>();
    NavMeshPath routePath;

    static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");

    public StageSettings SettingsFor(GameStage stage)
    {
        switch (stage)
        {
            case GameStage.Stage3: return stage3;
            case GameStage.Stage2: return stage2;
            default: return stage1;
        }
    }

    public int AliveRunners
    {
        get
        {
            int n = 0;
            foreach (var z in ZombieAI.All)
                if (z != null && !z.IsDead && z.IsRunner) n++;
            return n;
        }
    }

    public int AliveBrutes
    {
        get
        {
            int n = 0;
            foreach (var z in ZombieAI.All)
                if (z != null && !z.IsDead && z.IsBrute) n++;
            return n;
        }
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (viewBlockMask.value == 0)
            viewBlockMask = ~LayerMask.GetMask("Zombie", "Player", "Ignore Raycast", "TransparentFX", "UI", "Interactable");
    }

    void OnEnable()
    {
        GameProgress.OnStageChanged += HandleStageChanged;
        GameProgress.OnProgressReset += HandleProgressReset;
        ZombieAI.OnAnyDeathStarted += HandleZombieDeath;
    }

    void OnDisable()
    {
        GameProgress.OnStageChanged -= HandleStageChanged;
        GameProgress.OnProgressReset -= HandleProgressReset;
        ZombieAI.OnAnyDeathStarted -= HandleZombieDeath;
    }

    // A killed brute stays gone for a while. One that was recycled or cleared after a respawn was not killed, and
    // does not start the delay.
    void HandleZombieDeath(ZombieAI zombie)
    {
        if (zombie != null && zombie.IsBrute) nextBruteTime = Time.time + bruteRespawnDelay;
    }

    void Start()
    {
        RunStartTime = Time.time;
        BeginStage(false);
    }

    void HandleStageChanged(GameStage stage)
    {
        BeginStage(stage == GameStage.Stage2);
    }

    void HandleProgressReset()
    {
        DespawnAll();
        nearSpawnsOwed = 0;
        nextBruteTime = 0f;
        RunStartTime = Time.time;
        BeginStage(false);
    }

    void BeginStage(bool powerJustCameOn)
    {
        StageSettings s = Current;
        ZombieHearing.maxResponders = s.noiseResponders;
        foreach (var z in ZombieAI.All)
            if (z != null && !z.IsDead) ApplyStage(z, s);

        openingGroup = Mathf.Max(0, s.startAlive - ZombieAI.AliveCount);
        spawnTimer = 0f;
        huntTimer = s.huntInterval;
        if (powerJustCameOn && powerOnNoiseRadius > 0f && isActiveAndEnabled) StartCoroutine(PowerOnNoise());
    }

    void ApplyStage(ZombieAI zombie, StageSettings s)
    {
        zombie.Senses.SightMultiplier = s.sightMultiplier * SightMultiplier;
        zombie.investigateSpeed = s.investigateSpeed * SpeedMultiplier;
    }

    // The generator roars into life where the player is standing (at its console).
    IEnumerator PowerOnNoise()
    {
        Transform player = PlayerCatch.Player;
        if (player == null) yield break;
        Vector3 at = player.position;
        for (int i = 0; i < powerOnNoisePulses; i++)
        {
            NoiseSystem.MakeNoise(at, powerOnNoiseRadius);
            yield return new WaitForSeconds(powerOnNoiseInterval);
        }
    }

    void Update()
    {
        if (!spawningEnabled) return;
        // main menu and intro cutscene: an empty facility. The run starts when the menu closes or the intro ends.
        if (GameManager.BeforeRun)
        {
            heldForMenu = true;
            return;
        }
        if (heldForMenu)
        {
            heldForMenu = false;
            RunStartTime = Time.time;
            BeginStage(false);
        }

        float dt = Time.deltaTime;
        StageSettings s = Current;

        if (s.huntInterval > 0f)
        {
            huntTimer -= dt;
            if (huntTimer <= 0f)
            {
                huntTimer = s.huntInterval;
                SendOneHunting();
            }
        }

        if (recycleDistance > 0f)
        {
            recycleTimer -= dt;
            if (recycleTimer <= 0f)
            {
                recycleTimer = 5f;
                RecycleStrays();
            }
        }

        spawnTimer -= dt;
        if (spawnTimer > 0f) return;
        spawnTimer = retryInterval;

        int alive = ZombieAI.AliveCount;
        if (alive >= CurrentMaxAlive)
        {
            openingGroup = 0;
            LastSkipReason = "at the stage's maximum";
            return;
        }
        // frozen zombies stay frozen; nothing new walks in during a freeze or while the player is being respawned
        if (PowerUps.ZombiesFrozen || PlayerCatch.PlayerOutOfPlay)
        {
            LastSkipReason = "frozen or respawning";
            return;
        }

        int maxBrutes = CurrentMaxBrutes;
        bool wantBrute = maxBrutes > 0 && brute.bodies.Length > 0 && AliveBrutes < maxBrutes && Time.time >= nextBruteTime;
        bool wantRunner = s.maxRunners > 0 && runner.bodies.Length > 0 && AliveRunners < s.maxRunners;
        // no place for a brute or a runner right now: a regular zombie instead, rather than nothing
        bool spawned = wantBrute && TrySpawn(brute, false);
        if (!spawned) spawned = wantRunner && TrySpawn(runner, true);
        if (!spawned && !TrySpawn(regular, false)) return;

        if (openingGroup > 0) openingGroup--;
        if (openingGroup <= 0) spawnTimer = s.spawnInterval;
    }

    // ------------------------------------------------------------------ spawning

    // Spawns one zombie at a point that passes the rules. False (and nothing happens) when no point does.
    public bool TrySpawn(Role role, bool asRunner)
    {
        if (zombiePrefab == null || role == null || role.bodies.Length == 0)
        {
            LastSkipReason = "no prefab or no bodies";
            return false;
        }
        Transform player = PlayerCatch.Player;
        if (player == null)
        {
            LastSkipReason = "no player";
            return false;
        }

        var filter = AgentFilter();
        GameStage stage = Stage;
        bool towardEscape = asRunner && escapePoint != null;
        if (towardEscape)
        {
            BuildEscapeRoute(player.position, filter);
            if (TrySpawnOnRoute(role, player.position, filter)) return true;
        }

        valid.Clear();
        weights.Clear();
        float total = 0f;
        foreach (var point in spawnPoints)
        {
            if (point == null || point.fromStage > stage) continue;
            if (towardEscape && Vector3.Distance(point.position, escapePoint.position) < runnerEscapeExclusion) continue;
            if (role.minEscapeDistance > 0f && escapePoint != null && Vector3.Distance(point.position, escapePoint.position) < role.minEscapeDistance) continue;
            if (!IsAllowed(point.position, player.position)) continue;
            if (!NavMesh.SamplePosition(point.position, out _, 1.5f, filter)) continue;

            float weight;
            if (towardEscape)
                // no free spot on the route itself: the spawn points nearest to it
                weight = 1f / (1f + DistanceToRoute(point.position) / 5f);
            else
                // anywhere, with a lean toward the player's part of the map
                weight = 1f / (1f + Mathf.Max(0f, Vector3.Distance(point.position, player.position) - CurrentMinPlayerDistance) / 25f);
            valid.Add(point);
            weights.Add(weight);
            total += weight;
        }
        if (valid.Count == 0)
        {
            LastSkipReason = "no spawn point is out of sight and far enough";
            return false;
        }

        int chosen = valid.Count - 1;
        if (!asRunner && nearSpawnsOwed > 0)
        {
            // a recycled zombie's replacement: the nearest point, so it is not recycled again
            nearSpawnsOwed--;
            float nearest = float.MaxValue;
            for (int i = 0; i < valid.Count; i++)
            {
                float d = Vector3.Distance(valid[i].position, player.position);
                if (d >= nearest) continue;
                nearest = d;
                chosen = i;
            }
        }
        else
        {
            float pick = Random.value * total;
            for (int i = 0; i < valid.Count; i++)
            {
                pick -= weights[i];
                if (pick > 0f) continue;
                chosen = i;
                break;
            }
        }
        NavMesh.SamplePosition(valid[chosen].position, out NavMeshHit hit, 1.5f, filter);
        Spawn(role, asRunner, hit.position);
        LastSkipReason = "";
        return true;
    }

    // Puts a zombie of a role at a position, whatever the rules say.
    public ZombieAI Spawn(Role role, bool asRunner, Vector3 position)
    {
        var go = Instantiate(zombiePrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        bool asBrute = role == brute;
        go.name = (asBrute ? "Brute " : asRunner ? "Runner " : "Zombie ") + (SpawnCount + 1);
        var zombie = go.GetComponent<ZombieAI>();

        GameObject bodyPrefab = role.bodies[Random.Range(0, role.bodies.Length)];
        if (bodyPrefab != null)
        {
            var body = Instantiate(bodyPrefab, go.transform);
            body.name = bodyPrefab.name;
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            if (role.tints.Length > 0) Tint(body, role.tints[Random.Range(0, role.tints.Length)]);
        }
        go.transform.localScale = Vector3.one * Random.Range(role.scale.x, role.scale.y);

        zombie.IsRunner = asRunner;
        zombie.IsBrute = asBrute;
        ApplyOptional(zombie, role);
        zombie.wanderSpeed = Random.Range(role.wanderSpeed.x, role.wanderSpeed.y) * SpeedMultiplier;
        zombie.chaseSpeed = Random.Range(role.chaseSpeed.x, role.chaseSpeed.y) * SpeedMultiplier;
        zombie.screamChance = role.screamChance;
        zombie.Health.SetMaxHealth(role.health);
        ApplyStage(zombie, Current);
        // the zombie's Start binds the body (animator, head hitbox), after the scale above is in place

        SpawnCount++;
        LastSpawnPosition = position;
        return zombie;
    }

    // The role's optional values: anything left at 0 keeps what the Zombie prefab has.
    void ApplyOptional(ZombieAI zombie, Role role)
    {
        if (role.headshotMultiplier > 0f) zombie.Health.headshotMultiplier = role.headshotMultiplier;
        if (role.windUpTime > 0f) zombie.windUpTime = role.windUpTime;
        if (role.staggerTime > 0f) zombie.staggerTime = role.staggerTime;
        if (role.headRadius > 0f) zombie.headRadius = role.headRadius;
        if (role.voicePitch > 0f)
        {
            var voice = zombie.GetComponent<ZombieAudio>();
            if (voice != null) voice.ScalePitch(role.voicePitch);
        }
        if (role.bodyRadius > 0f || role.bodyHeight > 0f)
        {
            var capsule = zombie.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                if (role.bodyRadius > 0f) capsule.radius = role.bodyRadius;
                if (role.bodyHeight > 0f)
                {
                    capsule.height = role.bodyHeight;
                    capsule.center = new Vector3(0f, role.bodyHeight * 0.5f, 0f);
                }
            }
        }
    }

    void Tint(GameObject body, Color tint)
    {
        // the zombie shader has its own tint, which ZombieShaderFX sets together with its other values
        var fx = body.GetComponentInParent<ZombieShaderFX>();
        if (fx != null)
        {
            fx.SetTint(tint);
            return;
        }

        if (block == null) block = new MaterialPropertyBlock();
        foreach (var r in body.GetComponentsInChildren<Renderer>())
        {
            var materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null || !materials[i].HasProperty(BaseColourId)) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColourId, materials[i].GetColor(BaseColourId) * tint);
                r.SetPropertyBlock(block, i);
            }
        }
    }

    NavMeshQueryFilter AgentFilter()
    {
        var agent = zombiePrefab != null ? zombiePrefab.GetComponent<NavMeshAgent>() : null;
        return new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = NavMesh.AllAreas };
    }

    // The distance and line-of-sight rules for a position.
    public bool IsAllowed(Vector3 position, Vector3 playerPosition)
    {
        if (Vector3.Distance(position, playerPosition) < CurrentMinPlayerDistance) return false;
        foreach (var z in ZombieAI.All)
            if (z != null && Vector3.Distance(z.transform.position, position) < minZombieSpacing) return false;
        return !InPlayerView(position);
    }

    // True if the player's camera has a clear line to a zombie-sized shape standing at the position.
    public bool InPlayerView(Vector3 position)
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return false;
        Vector3 eye = viewCamera.transform.position;
        for (int i = 0; i < 3; i++)
        {
            Vector3 point = position + Vector3.up * (0.2f + i * 0.8f);
            Vector3 view = viewCamera.WorldToViewportPoint(point);
            if (view.z <= 0f || view.x < -0.05f || view.x > 1.05f || view.y < -0.05f || view.y > 1.05f) continue;
            if (!Physics.Linecast(eye, point, viewBlockMask, QueryTriggerInteraction.Ignore)) return true;
        }
        return false;
    }

    // The walking route from the player to the escape point, as a line of corners. Where a closed door cuts the
    // path short, the rest is taken as the straight line to the escape point.
    void BuildEscapeRoute(Vector3 playerPosition, NavMeshQueryFilter filter)
    {
        route.Clear();
        if (routePath == null) routePath = new NavMeshPath();
        Vector3 from = playerPosition, to = escapePoint.position;
        if (NavMesh.SamplePosition(from, out NavMeshHit a, 2f, filter)) from = a.position;
        if (NavMesh.SamplePosition(to, out NavMeshHit b, 2f, filter)) to = b.position;
        if (NavMesh.CalculatePath(from, to, filter, routePath)) route.AddRange(routePath.corners);
        if (route.Count == 0) route.Add(from);
        if (Vector3.Distance(route[route.Count - 1], to) > 0.5f) route.Add(to);
    }

    float DistanceToRoute(Vector3 point)
    {
        float best = float.MaxValue;
        for (int i = 1; i < route.Count; i++) best = Mathf.Min(best, DistanceToSegment(point, route[i - 1], route[i]));
        return route.Count == 1 ? Vector3.Distance(point, route[0]) : best;
    }

    // Spawns a runner on (or just beside) the route, at a spot that passes every rule. False when there is none.
    bool TrySpawnOnRoute(Role role, Vector3 playerPosition, NavMeshQueryFilter filter)
    {
        routeSpots.Clear();
        for (int i = 1; i < route.Count; i++)
        {
            Vector3 a = route[i - 1], b = route[i];
            float length = Vector3.Distance(a, b);
            Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized;
            for (float d = 1f; d < length; d += 2f)
            {
                Vector3 candidate = Vector3.Lerp(a, b, d / length) + side * Random.Range(-runnerRouteWidth, runnerRouteWidth);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1f, filter)) continue;
                if (Vector3.Distance(hit.position, escapePoint.position) < runnerEscapeExclusion) continue;
                if (!IsAllowed(hit.position, playerPosition)) continue;
                routeSpots.Add(hit.position);
            }
        }
        if (routeSpots.Count == 0) return false;
        Spawn(role, true, routeSpots[Random.Range(0, routeSpots.Count)]);
        RunnerRouteSpawns++;
        LastSkipReason = "";
        return true;
    }

    static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(point, a + ab * t);
    }

    // ------------------------------------------------------------------ pressure

    // Stage 3: the calm zombie nearest the player goes to look at a random spot near the player.
    void SendOneHunting()
    {
        Transform player = PlayerCatch.Player;
        if (player == null || PlayerCatch.PlayerOutOfPlay) return;
        ZombieAI best = null;
        float bestDistance = float.MaxValue;
        foreach (var z in ZombieAI.All)
        {
            if (z == null || z.IsDead || z.CurrentState != ZombieAI.State.Wander || !z.CanHear) continue;
            float d = Vector3.Distance(z.transform.position, player.position);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = z;
        }
        if (best == null) return;

        var filter = AgentFilter();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * huntRadius;
            Vector3 candidate = player.position + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, filter)) continue;
            best.Investigate(hit.position);
            HuntCount++;
            LastHuntPoint = hit.position;
            return;
        }
    }

    // Removes the furthest zombie that is doing nothing useful, when it could come back noticeably nearer.
    void RecycleStrays()
    {
        Transform player = PlayerCatch.Player;
        if (player == null || Time.time < nextRecycleTime) return;
        ZombieAI furthest = null;
        float furthestDistance = 0f;
        foreach (var z in ZombieAI.All)
        {
            if (z == null || z.IsDead || z.transform.parent != transform) continue;
            if (z.CalmTime < recycleCalmTime) continue;
            float distance = Vector3.Distance(z.transform.position, player.position);
            if (distance < recycleDistance || distance <= furthestDistance) continue;
            if (InPlayerView(z.transform.position)) continue;
            furthest = z;
            furthestDistance = distance;
        }
        // with the player far from everything there is nowhere better to put it: leave it be
        if (furthest == null || !CloserSpawnExists(player.position, furthestDistance - recycleGain)) return;

        furthest.Despawn();
        RecycleCount++;
        nearSpawnsOwed++;
        nextRecycleTime = Time.time + recycleCooldown;
        spawnTimer = Mathf.Min(spawnTimer, retryInterval);
    }

    // True if a spawn point that passes the rules is within the given distance of the player.
    bool CloserSpawnExists(Vector3 playerPosition, float within)
    {
        GameStage stage = Stage;
        foreach (var point in spawnPoints)
        {
            if (point == null || point.fromStage > stage) continue;
            if (Vector3.Distance(point.position, playerPosition) > within) continue;
            if (IsAllowed(point.position, playerPosition)) return true;
        }
        return false;
    }

    // Removes every zombie, bodies of dead ones included.
    public void DespawnAll()
    {
        for (int i = ZombieAI.All.Count - 1; i >= 0; i--)
        {
            var z = ZombieAI.All[i];
            if (z == null) continue;
            if (z.IsDead) Destroy(z.gameObject);
            else z.Despawn();
        }
    }

    void OnDrawGizmosSelected()
    {
        foreach (var point in spawnPoints)
        {
            if (point == null) continue;
            Gizmos.color = point.fromStage == GameStage.Stage1 ? Color.green : (point.fromStage == GameStage.Stage2 ? Color.yellow : Color.red);
            Gizmos.DrawWireSphere(point.position + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(point.position, point.position + Vector3.up * 1.8f);
        }
    }
}
