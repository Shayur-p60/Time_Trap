using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A zombie's behaviour. Needs a NavMeshAgent (agent type Zombie), ZombieSenses and ZombieHealth on the same object, and
// a humanoid body with an Animator somewhere below it (the spawner adds a random one; see BindBody).
//
//   Wander       slow, aimless, around where it spawned
//   Investigate  heard something: walk to where it came from
//   Alert        just spotted the player: scream
//   Chase        run at the player (or at where they were last seen)
//   Attack       wind-up, then the strike. The strike only catches a player who is still in reach, so backing off or
//                shooting the zombie during the wind-up gets them out of it
//   Search       lost the player: check a few spots around the last known position, then back to Wander
//   Door         the way is blocked by a closed door: walk up to it, open it (Normal doors) or bang on it (locked ones)
//   Staggered    was shot
//   Stunned      Flash Pulse went off nearby
//   Dead         death animation, then the body sinks and is removed
//
// Zombie Freeze (PowerUps.ZombiesFrozen) is not a state: everything simply stops, and carries on where it was.
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(ZombieSenses))]
[RequireComponent(typeof(ZombieHealth))]
public class ZombieAI : MonoBehaviour
{
    public enum State { Wander, Investigate, Alert, Chase, Attack, Search, Door, Staggered, Stunned, Dead }

    // Every zombie in the scene, dead ones included until their body is removed.
    public static readonly List<ZombieAI> All = new List<ZombieAI>();
    // Any zombie has started dying.
    public static event System.Action<ZombieAI> OnAnyDeathStarted;

    // This zombie has started dying. A dissolve effect can hook in here (and switch Sink On Death off).
    public event System.Action<ZombieAI> OnDeathStarted;

    [Header("Speeds (m/s)")]
    public float wanderSpeed = 0.55f;
    public float investigateSpeed = 1.4f;
    public float searchSpeed = 1.2f;
    public float chaseSpeed = 3.9f;
    [Tooltip("The speed the run animation is made for. Faster zombies (runners) play it faster by Chase Speed / this.")]
    public float runAnimationSpeed = 3.9f;

    [Header("Wander")]
    public float wanderRadius = 10f;
    [Tooltip("Seconds standing still between walks: random between these two values.")]
    public Vector2 wanderPause = new Vector2(2f, 6f);

    [Header("Hearing")]
    [Tooltip("Multiplier on every noise radius for this zombie.")]
    public float hearingSensitivity = 1f;

    [Header("Alert")]
    [Tooltip("Chance of screaming when it first spots the player.")]
    [Range(0f, 1f)] public float screamChance = 1f;
    [Tooltip("Seconds the scream holds it in place before it starts chasing.")]
    public float alertTime = 1.2f;
    public float screamCooldown = 15f;

    [Header("Chase")]
    [Tooltip("Seconds without seeing the player before it gives up the chase and searches.")]
    public float loseSightDelay = 3f;
    public float repathInterval = 0.2f;

    [Header("Attack")]
    [Tooltip("Starts the wind-up when the player is this close.")]
    public float attackRange = 1.7f;
    [Tooltip("Seconds between the start of the attack and the strike.")]
    public float windUpTime = 0.8f;
    [Tooltip("During the wind-up it keeps closing in at this fraction of its chase speed.")]
    [Range(0f, 1f)] public float windUpMoveFactor = 0.4f;
    [Tooltip("The strike catches the player if they are still within this distance...")]
    public float catchRange = 2f;
    [Tooltip("...and within this angle of straight ahead.")]
    public float catchAngle = 75f;
    [Tooltip("Seconds it stands there after a strike before chasing again.")]
    public float recoverTime = 0.9f;

    [Header("Search")]
    public int searchPoints = 3;
    public float searchRadius = 6f;
    public float searchPause = 2f;
    public float searchTimeout = 18f;

    [Header("Doors")]
    [Tooltip("Seconds it stands at a door before opening it.")]
    public float doorOpenDelay = 0.5f;
    [Tooltip("Seconds it bangs on a locked door before giving up.")]
    public float bangTime = 5f;
    public float bangInterval = 1f;
    [Tooltip("How far each bang carries. Marked as zombie noise, so other zombies ignore it.")]
    public float bangNoiseRadius = 12f;
    [Tooltip("Seconds it remembers that a door is locked and leaves it alone.")]
    public float lockedDoorMemory = 30f;

    [Header("Reactions")]
    [Tooltip("Seconds it is held up by a shot. Interrupts an attack wind-up.")]
    public float staggerTime = 0.5f;
    [Tooltip("Flash Pulse stuns zombies within this distance of it.")]
    public float stunRadius = 12f;
    [Tooltip("Seconds a Flash Pulse stun lasts (at least as long as the flash itself).")]
    public float stunTime = 4f;

    [Header("Death")]
    [Tooltip("Seconds given to the death animation.")]
    public float deathAnimationTime = 3f;
    [Tooltip("Seconds the body lies there afterwards.")]
    public float corpseLinger = 4f;
    [Tooltip("The body sinks into the floor before it is removed. Switch off when another effect (a dissolve) replaces it.")]
    public bool sinkOnDeath = true;
    public float sinkTime = 2f;
    public float sinkDepth = 1.5f;

    [Header("Head hitbox (created on the head bone)")]
    public float headRadius = 0.17f;
    [Tooltip("How far above the head bone the middle of the head is.")]
    public float headOffset = 0.1f;

    [Header("State (read only)")]
    [SerializeField] State state = State.Wander;

    public State CurrentState => state;
    public bool IsDead => state == State.Dead;
    public bool IsFrozen { get; private set; }
    // Set by the spawner.
    public bool IsRunner { get; set; }
    // Seconds since it last did anything but wander.
    public float CalmTime { get; private set; }
    public ZombieSenses Senses => senses;
    public ZombieHealth Health => health;
    public Animator Body => animator;
    public Door CurrentDoor => state == State.Door ? door : null;
    public bool IsBanging => state == State.Door && doorPhase == DoorPhase.Bang;

    // For tests.
    public int AttacksStarted { get; private set; }
    public int StrikesLanded { get; private set; }
    public int StrikesMissed { get; private set; }
    public int DoorsOpened { get; private set; }
    public int BangsMade { get; private set; }

    // Can react to a noise right now.
    public bool CanHear
    {
        get
        {
            if (IsFrozen || !isActiveAndEnabled) return false;
            switch (state)
            {
                case State.Wander:
                case State.Investigate:
                case State.Search:
                    return true;
                case State.Door:
                    return doorPhase != DoorPhase.Bang;
                default:
                    return false;
            }
        }
    }

    public static int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var z in All)
                if (z != null && !z.IsDead) n++;
            return n;
        }
    }

    enum DoorPhase { Approach, Open, WaitOpen, Bang }

    NavMeshAgent agent;
    ZombieSenses senses;
    ZombieHealth health;
    ZombieAudio sfx;
    Animator animator;
    readonly HashSet<int> animatorParameters = new HashSet<int>();
    NavMeshQueryFilter filter;
    NavMeshPath scratchPath;

    float stateTime;
    Vector3 home;
    Vector3 goal;
    float repathTimer;
    float doorCheckTimer;
    bool stopped;

    // wander
    bool wanderWaiting;
    float wanderWait;

    // alert / attack
    float lastScreamTime = -999f;
    bool struck;

    // search
    Vector3 searchOrigin;
    int searchVisited;
    bool searchLooking;
    float searchLookTime;
    float searchTime;
    float lookDirection = 1f;

    // door
    Door door;
    Vector3 doorApproach;
    DoorPhase doorPhase;
    float doorPhaseTime;
    float nextBang;
    State resumeState;
    readonly Dictionary<Door, float> lockedDoors = new Dictionary<Door, float>();

    // stun
    float stunLeft;

    // death
    float deadTime;
    Vector3 deathPosition;

    static Door[] sceneDoors;

    static readonly int SpeedParam = Animator.StringToHash("Speed");
    static readonly int LocomotionRateParam = Animator.StringToHash("LocomotionRate");
    static readonly int VariantParam = Animator.StringToHash("Variant");
    static readonly int AttackParam = Animator.StringToHash("Attack");
    static readonly int ScreamParam = Animator.StringToHash("Scream");
    static readonly int HitParam = Animator.StringToHash("Hit");
    static readonly int DieParam = Animator.StringToHash("Die");
    static readonly int DeathVariantParam = Animator.StringToHash("DeathVariant");
    static readonly int StunnedParam = Animator.StringToHash("Stunned");
    static readonly int DoorBangParam = Animator.StringToHash("DoorBang");

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        senses = GetComponent<ZombieSenses>();
        health = GetComponent<ZombieHealth>();
        sfx = GetComponent<ZombieAudio>();
        scratchPath = new NavMeshPath();
        home = transform.position;
        health.OnDamaged += HandleDamaged;
        health.OnDied += HandleDied;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        PowerUps.OnFlashPulse += HandleFlashPulse;
    }

    void OnDisable()
    {
        All.Remove(this);
        PowerUps.OnFlashPulse -= HandleFlashPulse;
    }

    void Start()
    {
        filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (animator == null) BindBody();
        if (!agent.isOnNavMesh)
            Debug.LogWarning("[ZombieAI] " + name + " is not on the NavMesh (agent type '" + NavMesh.GetSettingsNameFromID(agent.agentTypeID) + "'): it will not move.", this);
        home = transform.position;
        EnterWander();
    }

    // Finds the body's Animator below this object and puts the head hitbox on its head bone. Start does this; call it
    // again after swapping the body.
    public void BindBody()
    {
        animator = GetComponentInChildren<Animator>();
        animatorParameters.Clear();
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        if (animator.runtimeAnimatorController != null)
            foreach (var p in animator.parameters) animatorParameters.Add(p.nameHash);
        // which idle and walk this zombie uses: 0 or 1
        SetFloat(VariantParam, Random.value < 0.5f ? 0f : 1f);
        SetInteger(DeathVariantParam, Random.Range(0, 2));

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return;
        senses.eye = head;
        if (head.GetComponentInChildren<ZombieHitbox>() != null) return;

        var go = new GameObject("HeadHitbox");
        go.layer = gameObject.layer;
        go.transform.SetParent(head, false);
        go.transform.position = head.position + head.up * headOffset;
        var sphere = go.AddComponent<SphereCollider>();
        sphere.radius = headRadius / Mathf.Max(0.0001f, go.transform.lossyScale.x);
        var hitbox = go.AddComponent<ZombieHitbox>();
        hitbox.health = health;
        hitbox.damageMultiplier = health.headshotMultiplier;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (state == State.Dead)
        {
            TickDead(dt);
            return;
        }

        bool frozen = PowerUps.ZombiesFrozen;
        if (frozen != IsFrozen) SetFrozen(frozen);
        if (IsFrozen) return;

        stateTime += dt;
        CalmTime = state == State.Wander ? CalmTime + dt : 0f;

        switch (state)
        {
            case State.Wander: TickWander(dt); break;
            case State.Investigate: TickInvestigate(dt); break;
            case State.Alert: TickAlert(); break;
            case State.Chase: TickChase(dt); break;
            case State.Attack: TickAttack(dt); break;
            case State.Search: TickSearch(dt); break;
            case State.Door: TickDoor(dt); break;
            case State.Staggered: TickStaggered(); break;
            case State.Stunned: TickStunned(dt); break;
        }

        if (state == State.Dead) return;
        float speed = agent.enabled ? agent.velocity.magnitude : 0f;
        SetFloat(SpeedParam, speed, 0.12f, dt);
        if (sfx != null) sfx.Tick(speed, state == State.Wander || state == State.Search || state == State.Investigate, dt);
    }

    void SetFrozen(bool frozen)
    {
        IsFrozen = frozen;
        senses.enabled = !frozen;
        if (animator != null) animator.speed = frozen ? 0f : 1f;
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = frozen || stopped;
            if (frozen) agent.velocity = Vector3.zero;
        }
    }

    // ------------------------------------------------------------------ state changes

    void SetState(State next, float speed)
    {
        if (state == State.Door && next != State.Door) SetBool(DoorBangParam, false);
        if (state == State.Stunned && next != State.Stunned) SetBool(StunnedParam, false);
        state = next;
        stateTime = 0f;
        repathTimer = 0f;
        doorCheckTimer = 0.3f;
        agent.speed = speed;
        float rate = next == State.Chase || next == State.Attack ? Mathf.Max(1f, chaseSpeed / Mathf.Max(0.1f, runAnimationSpeed)) : 1f;
        SetFloat(LocomotionRateParam, rate);
    }

    void Halt(bool halt)
    {
        stopped = halt;
        if (!agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = halt || IsFrozen;
        if (halt) agent.velocity = Vector3.zero;
    }

    float SpeedFor(State s)
    {
        switch (s)
        {
            case State.Chase: return chaseSpeed;
            case State.Investigate: return investigateSpeed;
            case State.Search: return searchSpeed;
            default: return wanderSpeed;
        }
    }

    void EnterWander()
    {
        SetState(State.Wander, wanderSpeed);
        Halt(true);
        wanderWaiting = true;
        wanderWait = Random.Range(wanderPause.x, wanderPause.y) * 0.5f;
    }

    // Sends the zombie to look at a position (a noise, or where the spawner says the player is). Ignored while it is
    // busy with the player, stunned, frozen or dead.
    public void Investigate(Vector3 position)
    {
        if (!CanHear) return;
        goal = position;
        SetState(State.Investigate, investigateSpeed);
        Halt(false);
    }

    void EnterChase()
    {
        SetState(State.Chase, chaseSpeed);
        Halt(false);
    }

    void EnterSearch(Vector3 origin)
    {
        searchOrigin = origin;
        goal = origin;
        searchVisited = 0;
        searchLooking = false;
        searchTime = 0f;
        SetState(State.Search, searchSpeed);
        Halt(false);
    }

    // The player has just come into view.
    void SpotPlayer()
    {
        if (Time.time - lastScreamTime >= screamCooldown && Random.value < screamChance)
        {
            lastScreamTime = Time.time;
            SetState(State.Alert, 0f);
            Halt(true);
            SetTrigger(ScreamParam);
            if (sfx != null) sfx.PlayScream();
            return;
        }
        EnterChase();
    }

    // Forgets the player and goes back to wandering (the player respawned somewhere else).
    public void LosePlayer()
    {
        if (IsDead) return;
        senses.ForgetPlayer();
        if (state == State.Wander || state == State.Stunned || state == State.Staggered) return;
        ResetTrigger(AttackParam);
        EnterWander();
    }

    // Removes the zombie at once: no death, no body.
    public void Despawn()
    {
        All.Remove(this);
        Destroy(gameObject);
    }

    // The player has been put back on a checkpoint: zombies near it are removed, the others forget the player.
    public static void ResetAfterPlayerRespawn(Vector3 point, float clearRadius)
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            var z = All[i];
            if (z == null || z.IsDead) continue;
            if (Vector3.Distance(z.transform.position, point) <= clearRadius) z.Despawn();
            else z.LosePlayer();
        }
    }

    // ------------------------------------------------------------------ wander

    void TickWander(float dt)
    {
        if (senses.CanSeePlayer)
        {
            SpotPlayer();
            return;
        }

        if (wanderWaiting)
        {
            wanderWait -= dt;
            if (wanderWait > 0f) return;
            if (!PickWanderPoint())
            {
                wanderWait = 1f;
                return;
            }
            wanderWaiting = false;
            stateTime = 0f;
            repathTimer = 0f;
            Halt(false);
            return;
        }

        if (Travel(goal, false, dt) || stateTime > 40f)
        {
            wanderWaiting = true;
            wanderWait = Random.Range(wanderPause.x, wanderPause.y);
            Halt(true);
        }
    }

    bool PickWanderPoint()
    {
        return PickReachablePoint(home, wanderRadius, wanderRadius * 2.5f, out goal);
    }

    // A random point around a centre that this zombie can walk to without opening anything.
    bool PickReachablePoint(Vector3 centre, float radius, float maxWalk, out Vector3 point)
    {
        point = centre;
        if (!agent.isOnNavMesh) return false;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = centre + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, filter)) continue;
            if (!NavMesh.CalculatePath(transform.position, hit.position, filter, scratchPath)) continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
            if (PathLength(scratchPath) > maxWalk) continue;
            point = hit.position;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ investigate

    void TickInvestigate(float dt)
    {
        if (senses.CanSeePlayer)
        {
            SpotPlayer();
            return;
        }
        if (Travel(goal, true, dt) || stateTime > 30f) EnterSearch(transform.position);
    }

    // ------------------------------------------------------------------ alert

    void TickAlert()
    {
        Transform player = senses.Player;
        if (player != null) Face(player.position, 6f);
        if (stateTime >= alertTime) EnterChase();
    }

    // ------------------------------------------------------------------ chase

    void TickChase(float dt)
    {
        Transform player = senses.Player;
        if (player == null || PlayerCatch.PlayerOutOfPlay)
        {
            EnterWander();
            return;
        }

        bool see = senses.CanSeePlayer;
        if (!see && senses.TimeSinceLastSeen > loseSightDelay)
        {
            EnterSearch(senses.HasLastKnownPosition ? senses.LastKnownPosition : transform.position);
            return;
        }

        if (see && InReach(player.position, attackRange))
        {
            EnterAttack();
            return;
        }

        // for a moment after losing sight it still knows where the player went (round a corner, through a doorway)
        bool tracking = see || senses.TimeSinceLastSeen < 1f;
        Vector3 target = tracking || !senses.HasLastKnownPosition ? player.position : senses.LastKnownPosition;
        if (Travel(target, true, dt) && !see) EnterSearch(target);
    }

    bool InReach(Vector3 playerPosition, float range)
    {
        Vector3 to = playerPosition - transform.position;
        if (Mathf.Abs(to.y) > 1.6f) return false;
        to.y = 0f;
        return to.sqrMagnitude <= range * range;
    }

    // ------------------------------------------------------------------ attack

    void EnterAttack()
    {
        SetState(State.Attack, chaseSpeed * windUpMoveFactor);
        Halt(windUpMoveFactor <= 0f);
        // drop to the wind-up speed at once: carrying the chase's momentum would eat the player's time to react
        if (agent.velocity.magnitude > agent.speed) agent.velocity = agent.velocity.normalized * agent.speed;
        struck = false;
        AttacksStarted++;
        SetTrigger(AttackParam);
        if (sfx != null) sfx.PlayAttack();
    }

    void TickAttack(float dt)
    {
        Transform player = senses.Player;
        if (player == null || PlayerCatch.PlayerOutOfPlay)
        {
            EnterWander();
            return;
        }

        if (!struck)
        {
            Face(player.position, 8f);
            repathTimer -= dt;
            if (repathTimer <= 0f && !stopped)
            {
                repathTimer = 0.1f;
                SetDestination(player.position);
            }
            if (stateTime < windUpTime) return;

            // the strike
            struck = true;
            Halt(true);
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            bool caught = InReach(player.position, catchRange)
                && Vector3.Angle(transform.forward, to) <= catchAngle
                && senses.HasClearLine(senses.PlayerCentre)
                && PlayerCatch.TryCatch(this);
            if (caught) StrikesLanded++;
            else StrikesMissed++;
            return;
        }

        if (stateTime >= windUpTime + recoverTime) EnterChase();
    }

    // ------------------------------------------------------------------ search

    void TickSearch(float dt)
    {
        if (senses.CanSeePlayer)
        {
            SpotPlayer();
            return;
        }

        searchTime += dt;
        if (searchTime > searchTimeout)
        {
            EnterWander();
            return;
        }

        if (searchLooking)
        {
            searchLookTime += dt;
            // look one way, then the other
            float turn = searchLookTime > searchPause * 0.5f ? -lookDirection : lookDirection;
            transform.Rotate(0f, 70f * turn * dt, 0f, Space.World);
            if (searchLookTime < searchPause) return;

            searchVisited++;
            searchLooking = false;
            if (searchVisited >= searchPoints || !PickReachablePoint(searchOrigin, searchRadius, searchRadius * 3f, out goal))
            {
                EnterWander();
                return;
            }
            stateTime = 0f;
            repathTimer = 0f;
            Halt(false);
            return;
        }

        // the first spot is the last known position itself, which may be behind a door
        if (Travel(goal, searchVisited == 0, dt) || stateTime > 15f)
        {
            searchLooking = true;
            searchLookTime = 0f;
            lookDirection = Random.value < 0.5f ? 1f : -1f;
            Halt(true);
        }
    }

    // ------------------------------------------------------------------ doors

    // Looks for the closed door that is in the way to a target and, if there is one, goes to deal with it.
    bool TryUseDoor(Vector3 target)
    {
        if (!FindDoorToward(target, out Door found, out Vector3 approach)) return false;
        resumeState = state;
        float resumeTime = stateTime;
        door = found;
        doorApproach = approach;
        doorPhase = DoorPhase.Approach;
        doorPhaseTime = 0f;
        SetState(State.Door, SpeedFor(resumeState));
        stateTime = resumeTime;
        Halt(false);
        SetDestination(doorApproach);
        return true;
    }

    bool FindDoorToward(Vector3 target, out Door best, out Vector3 bestApproach)
    {
        best = null;
        bestApproach = Vector3.zero;
        if (!agent.isOnNavMesh) return false;
        if (sceneDoors == null) sceneDoors = FindObjectsByType<Door>(FindObjectsInactive.Exclude);

        Vector3 here = transform.position;
        float direct = Vector3.Distance(here, target);
        float bestCost = float.MaxValue;
        foreach (var d in sceneDoors)
        {
            if (d == null || !d.isActiveAndEnabled || d.IsOpen || d.mode == Door.Mode.External) continue;
            if (lockedDoors.TryGetValue(d, out float until) && Time.time < until) continue;

            Vector3 centre = d.DoorwayPosition;
            float toDoor = Vector3.Distance(here, centre);
            float beyond = Vector3.Distance(centre, target);
            // a door far off the line to the target is not "in the way"
            if (toDoor + beyond > direct * 2f + 10f) continue;

            // the closed door is carved out of the NavMesh: the nearest walkable point is on one side of it, and
            // mirroring that point through the doorway gives the other side
            if (!NavMesh.SamplePosition(centre, out NavMeshHit near, 2.5f, filter)) continue;
            Vector3 across = centre - near.position;
            across.y = 0f;
            Vector3 mirrored = centre + (across.sqrMagnitude > 0.0001f ? across.normalized : Vector3.forward) * 1.3f;

            float walk = WalkTo(near.position);
            Vector3 approach = near.position;
            if (NavMesh.SamplePosition(mirrored, out NavMeshHit far, 1.2f, filter))
            {
                float farWalk = WalkTo(far.position);
                if (farWalk < walk)
                {
                    walk = farWalk;
                    approach = far.position;
                }
            }
            if (walk == float.MaxValue) continue;

            float cost = walk + beyond * 1.2f;
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = d;
            bestApproach = approach;
        }
        return best != null;
    }

    // Walking distance to a point, or float.MaxValue when it cannot be reached.
    float WalkTo(Vector3 point)
    {
        if (!NavMesh.CalculatePath(transform.position, point, filter, scratchPath)) return float.MaxValue;
        return scratchPath.status == NavMeshPathStatus.PathComplete ? PathLength(scratchPath) : float.MaxValue;
    }

    void TickDoor(float dt)
    {
        if (door == null)
        {
            EnterWander();
            return;
        }

        // the player has stepped into view and can be walked to: forget the door
        if (senses.CanSeePlayer && senses.Player != null && WalkTo(senses.Player.position) != float.MaxValue)
        {
            SpotPlayer();
            return;
        }

        doorPhaseTime += dt;
        Vector3 centre = door.DoorwayPosition;
        switch (doorPhase)
        {
            case DoorPhase.Approach:
                if (door.IsOpen)
                {
                    doorPhase = DoorPhase.WaitOpen;
                    doorPhaseTime = 0f;
                    break;
                }
                bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f;
                if (!arrived)
                {
                    if (doorPhaseTime > 12f) EnterWander();
                    break;
                }
                Halt(true);
                doorPhaseTime = 0f;
                if (door.IsLocked)
                {
                    doorPhase = DoorPhase.Bang;
                    nextBang = 0.4f;
                    SetBool(DoorBangParam, true);
                }
                else doorPhase = DoorPhase.Open;
                break;

            case DoorPhase.Open:
                Face(centre, 6f);
                if (doorPhaseTime < doorOpenDelay) break;
                if (!door.IsOpen)
                {
                    door.OpenBy(NoiseKind.Zombie);
                    DoorsOpened++;
                }
                doorPhase = DoorPhase.WaitOpen;
                doorPhaseTime = 0f;
                break;

            case DoorPhase.WaitOpen:
                // the carving goes when the door starts to move; give the NavMesh a moment to close the hole
                if ((door.IsFullyOpen && doorPhaseTime > 0.2f) || doorPhaseTime > 3f) ResumeAfterDoor();
                break;

            case DoorPhase.Bang:
                Face(centre, 6f);
                if (!door.IsLocked)
                {
                    // the power came back while it was banging
                    SetBool(DoorBangParam, false);
                    doorPhase = DoorPhase.Open;
                    doorPhaseTime = 0f;
                    break;
                }
                if (doorPhaseTime >= nextBang)
                {
                    nextBang += bangInterval;
                    BangsMade++;
                    NoiseSystem.MakeNoise(centre, bangNoiseRadius, NoiseKind.Zombie, transform);
                    if (sfx != null) sfx.PlayDoorBang();
                }
                if (doorPhaseTime >= bangTime)
                {
                    lockedDoors[door] = Time.time + lockedDoorMemory;
                    senses.ForgetPlayer();
                    EnterWander();
                }
                break;
        }
    }

    void ResumeAfterDoor()
    {
        float resumeTime = stateTime;
        SetState(resumeState, SpeedFor(resumeState));
        stateTime = resumeTime;
        Halt(false);
    }

    // ------------------------------------------------------------------ reactions

    void HandleDamaged(float damage, Vector3 point, bool head)
    {
        if (IsDead || health.IsDead) return;
        if (sfx != null) sfx.PlayHit();
        // it now knows roughly where the shot came from
        Transform player = senses.Player;
        if (player != null) senses.NotePlayerPosition(player.position);
        if (IsFrozen || state == State.Stunned) return;

        ResetTrigger(AttackParam);
        SetTrigger(HitParam);
        SetState(State.Staggered, 0f);
        Halt(true);
    }

    void TickStaggered()
    {
        if (stateTime < staggerTime) return;
        if (senses.CanSeePlayer) EnterChase();
        else if (senses.HasLastKnownPosition)
        {
            goal = senses.LastKnownPosition;
            SetState(State.Investigate, chaseSpeed * 0.6f);
            Halt(false);
        }
        else EnterWander();
    }

    void HandleFlashPulse(Vector3 position, float duration)
    {
        if (IsDead || Vector3.Distance(position, transform.position) > stunRadius) return;
        Stun(Mathf.Max(duration, stunTime));
    }

    // Holds the zombie helpless for a while; afterwards it has lost the player.
    public void Stun(float seconds)
    {
        if (IsDead) return;
        stunLeft = Mathf.Max(stunLeft, seconds);
        if (state == State.Stunned) return;
        ResetTrigger(AttackParam);
        SetState(State.Stunned, 0f);
        Halt(true);
        SetBool(StunnedParam, true);
    }

    void TickStunned(float dt)
    {
        stunLeft -= dt;
        if (stunLeft > 0f) return;
        stunLeft = 0f;
        senses.ForgetPlayer();
        EnterSearch(transform.position);
    }

    // ------------------------------------------------------------------ death

    void HandleDied()
    {
        if (state == State.Dead) return;
        SetBool(DoorBangParam, false);
        SetBool(StunnedParam, false);
        state = State.Dead;
        deadTime = 0f;
        IsFrozen = false;

        if (agent.enabled)
        {
            if (agent.isOnNavMesh) agent.isStopped = true;
            agent.enabled = false;
        }
        senses.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        deathPosition = transform.position;

        if (animator != null)
        {
            animator.speed = 1f;
            // a head shot throws it backwards, anything else drops it forwards
            SetInteger(DeathVariantParam, health.LastHitWasHead ? 0 : 1);
            SetFloat(SpeedParam, 0f);
            SetTrigger(DieParam);
        }
        if (sfx != null) sfx.PlayDeath();

        OnDeathStarted?.Invoke(this);
        OnAnyDeathStarted?.Invoke(this);
    }

    void TickDead(float dt)
    {
        deadTime += dt;
        float sinkStart = deathAnimationTime + corpseLinger;
        if (deadTime < sinkStart) return;

        float t = sinkTime > 0f ? (deadTime - sinkStart) / sinkTime : 1f;
        if (sinkOnDeath) transform.position = deathPosition + Vector3.down * (sinkDepth * Mathf.Clamp01(t));
        if (t >= 1f) Destroy(gameObject);
    }

    // ------------------------------------------------------------------ movement helpers

    // Walks toward a target, re-pathing every Repath Interval. True once the zombie is as close as its path gets.
    // With Use Doors, a path that stops short makes it look for the closed door in the way (and the state changes to Door).
    bool Travel(Vector3 target, bool useDoors, float dt)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return false;

        repathTimer -= dt;
        if (repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            SetDestination(target);
            return false;
        }
        if (agent.pathPending) return false;

        if (useDoors && agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            doorCheckTimer -= dt;
            if (doorCheckTimer <= 0f)
            {
                doorCheckTimer = 0.75f;
                if (TryUseDoor(target)) return false;
            }
        }
        return agent.remainingDistance <= agent.stoppingDistance + 0.35f;
    }

    void SetDestination(Vector3 position)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, filter)) position = hit.position;
        agent.SetDestination(position);
    }

    // Walking distance to a point along the NavMesh. Where the path stops short (a closed door, a wall), the rest
    // counts double: sound through a wall is muffled.
    public float PathDistanceTo(Vector3 target)
    {
        float straight = Vector3.Distance(transform.position, target);
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return straight;
        Vector3 end = target;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, filter)) end = hit.position;
        if (!NavMesh.CalculatePath(transform.position, end, filter, scratchPath) || scratchPath.corners.Length == 0) return straight * 2f;

        float length = PathLength(scratchPath);
        if (scratchPath.status != NavMeshPathStatus.PathComplete)
        {
            var corners = scratchPath.corners;
            length += Vector3.Distance(corners[corners.Length - 1], end) * 2f;
        }
        return length;
    }

    static float PathLength(NavMeshPath path)
    {
        var corners = path.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }

    void Face(Vector3 position, float turnSpeed)
    {
        Vector3 direction = position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * turnSpeed);
    }

    // ------------------------------------------------------------------ animator helpers

    // The controller may not have every parameter (or there may be no body yet): setting a missing one is skipped.
    void SetFloat(int id, float value)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.SetFloat(id, value);
    }

    void SetFloat(int id, float value, float damp, float dt)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.SetFloat(id, value, damp, dt);
    }

    void SetInteger(int id, int value)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.SetInteger(id, value);
    }

    void SetBool(int id, bool value)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.SetBool(id, value);
    }

    void SetTrigger(int id)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.SetTrigger(id);
    }

    void ResetTrigger(int id)
    {
        if (animator != null && animatorParameters.Contains(id)) animator.ResetTrigger(id);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        if (!Application.isPlaying) return;
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(goal, 0.25f);
        Gizmos.DrawLine(transform.position, goal);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForNewPlaySession()
    {
        All.Clear();
        OnAnyDeathStarted = null;
        sceneDoors = null;
    }
}
