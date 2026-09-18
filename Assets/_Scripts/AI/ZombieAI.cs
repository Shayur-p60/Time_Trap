using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Five-state zombie finite state machine.
///
///   Patrol      wander or follow waypoints, unaware
///   Investigate heard something, go look at where it came from
///   Chase       player is visible, pursue directly
///   Search      lost the player, sweep around their last known position
///   Dead        terminal state, no transitions out
///
/// Requires ZombieSenses on the same GameObject.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(ZombieSenses))]
public class ZombieAI : MonoBehaviour
{
    public enum State { Patrol, Investigate, Chase, Search, Dead }

    [Header("Current State (read only at runtime)")]
    [SerializeField] private State currentState = State.Patrol;
    public State CurrentState => currentState;

    [Header("Speeds")]
    public float patrolSpeed = 1.1f;
    public float investigateSpeed = 2.0f;
    public float chaseSpeed = 3.6f;
    public float searchSpeed = 1.8f;

    [Header("Patrol")]
    public Transform[] waypoints;
    [Tooltip("Used when no waypoints are assigned.")]
    public float wanderRadius = 12f;
    public float waypointPause = 2.5f;

    [Header("Investigate")]
    [Tooltip("How loud a noise has to be, relative to distance, to interest this zombie.")]
    public float hearingSensitivity = 1f;
    [Tooltip("Seconds spent looking around at the noise source.")]
    public float investigateLookTime = 3f;
    [Tooltip("Abandon the investigation if it takes longer than this.")]
    public float investigateTimeout = 12f;

    [Header("Chase")]
    public float attackRange = 1.9f;
    public float attackCooldown = 1.4f;
    public int attackDamage = 12;
    [Tooltip("Seconds of no line of sight before dropping to Search.")]
    public float loseSightDelay = 2.5f;
    [Tooltip("Re-path to the player at most this often, to save CPU.")]
    public float repathInterval = 0.2f;

    [Header("Search")]
    [Tooltip("How many spots around the last known position to check.")]
    public int searchPoints = 3;
    public float searchRadius = 8f;
    public float searchPointPause = 2f;
    [Tooltip("Give up and return to Patrol after this long.")]
    public float searchTimeout = 15f;

    [Header("Death")]
    public float corpseLingerTime = 12f;
    [Tooltip("Leave empty to just disable the object instead of destroying it.")]
    public bool destroyOnDespawn = true;

    // --- cached refs -------------------------------------------------------
    private NavMeshAgent agent;
    private ZombieSenses senses;
    private Animator animator;
    private Collider bodyCollider;

    // --- shared timers -----------------------------------------------------
    private float stateEnterTime;
    private float pauseTimer;
    private float lastAttackTime = -999f;
    private float lastRepathTime = -999f;

    // --- patrol ------------------------------------------------------------
    private int waypointIndex = -1;
    private Vector3 spawnPoint;

    // --- investigate / search ---------------------------------------------
    private Vector3 targetPoint;
    private bool reachedTargetPoint;
    private int searchPointsVisited;

    // --- animator hashes ---------------------------------------------------
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DieHash = Animator.StringToHash("Die");
    private static readonly int AlertHash = Animator.StringToHash("Alert");

    // ======================================================================
    #region Unity lifecycle

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        senses = GetComponent<ZombieSenses>();
        animator = GetComponentInChildren<Animator>();
        bodyCollider = GetComponent<Collider>();
        spawnPoint = transform.position;
    }

    private void OnEnable()
    {
        NoiseEvent.OnNoise += HandleNoise;
    }

    private void OnDisable()
    {
        NoiseEvent.OnNoise -= HandleNoise;
    }

    private void Start()
    {
        if (!agent.isOnNavMesh)
        {
            Debug.LogError(
                $"{name} is not on the NavMesh. Move it onto the baked surface " +
                "or it will never move.", this);
        }
        TransitionTo(State.Patrol);
    }

    private void Update()
    {
        if (currentState == State.Dead) return;

        switch (currentState)
        {
            case State.Patrol:      TickPatrol();      break;
            case State.Investigate: TickInvestigate(); break;
            case State.Chase:       TickChase();       break;
            case State.Search:      TickSearch();      break;
        }

        DriveAnimator();
    }

    #endregion

    // ======================================================================
    #region State machine core

    private void TransitionTo(State next)
    {
        if (currentState == State.Dead) return;

        ExitState(currentState);
        currentState = next;
        stateEnterTime = Time.time;
        pauseTimer = 0f;
        EnterState(next);
    }

    private void EnterState(State state)
    {
        switch (state)
        {
            case State.Patrol:
                agent.speed = patrolSpeed;
                agent.isStopped = false;
                SetAlert(false);
                PickNextPatrolPoint();
                break;

            case State.Investigate:
                agent.speed = investigateSpeed;
                agent.isStopped = false;
                SetAlert(true);
                reachedTargetPoint = false;
                SetDestinationSafe(targetPoint);
                break;

            case State.Chase:
                agent.speed = chaseSpeed;
                agent.isStopped = false;
                SetAlert(true);
                lastRepathTime = -999f;
                break;

            case State.Search:
                agent.speed = searchSpeed;
                agent.isStopped = false;
                SetAlert(true);
                searchPointsVisited = 0;
                reachedTargetPoint = false;
                targetPoint = senses.LastKnownPosition;
                SetDestinationSafe(targetPoint);
                break;

            case State.Dead:
                HandleDeathEntry();
                break;
        }
    }

    private void ExitState(State state)
    {
        if (state == State.Chase)
            agent.isStopped = false;
    }

    #endregion

    // ======================================================================
    #region Patrol

    private void TickPatrol()
    {
        if (senses.HasLineOfSight)
        {
            TransitionTo(State.Chase);
            return;
        }

        if (agent.pathPending) return;

        if (AtDestination())
        {
            pauseTimer += Time.deltaTime;
            if (pauseTimer >= waypointPause)
            {
                pauseTimer = 0f;
                PickNextPatrolPoint();
            }
        }
    }

    private void PickNextPatrolPoint()
    {
        if (waypoints != null && waypoints.Length > 0)
        {
            waypointIndex = (waypointIndex + 1) % waypoints.Length;
            if (waypoints[waypointIndex] != null)
                SetDestinationSafe(waypoints[waypointIndex].position);
            return;
        }

        Vector3 candidate = spawnPoint + UnityEngine.Random.insideUnitSphere * wanderRadius;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            SetDestinationSafe(hit.position);
    }

    #endregion

    // ======================================================================
    #region Investigate

    private void HandleNoise(NoiseEvent.Data noise)
    {
        if (currentState == State.Dead) return;
        if (currentState == State.Chase) return; // already has a better target

        float distance = Vector3.Distance(transform.position, noise.position);
        if (distance > noise.radius * hearingSensitivity) return;

        targetPoint = noise.position;

        if (currentState == State.Investigate || currentState == State.Search)
        {
            // Already alert: just redirect to the newer, closer sound.
            reachedTargetPoint = false;
            stateEnterTime = Time.time;
            SetDestinationSafe(targetPoint);
        }
        else
        {
            TransitionTo(State.Investigate);
        }
    }

    private void TickInvestigate()
    {
        if (senses.HasLineOfSight)
        {
            TransitionTo(State.Chase);
            return;
        }

        if (Time.time - stateEnterTime > investigateTimeout)
        {
            TransitionTo(State.Patrol);
            return;
        }

        if (agent.pathPending) return;

        if (!reachedTargetPoint && AtDestination())
            reachedTargetPoint = true;

        if (reachedTargetPoint)
        {
            // Stand and turn on the spot, scanning.
            pauseTimer += Time.deltaTime;
            transform.Rotate(Vector3.up, 60f * Time.deltaTime, Space.World);

            if (pauseTimer >= investigateLookTime)
                TransitionTo(State.Patrol);
        }
    }

    #endregion

    // ======================================================================
    #region Chase

    private void TickChase()
    {
        Transform player = senses.Player;
        if (player == null)
        {
            TransitionTo(State.Patrol);
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= attackRange)
        {
            agent.isStopped = true;
            FaceTarget(player.position);
            TryAttack(player);
            return;
        }

        agent.isStopped = false;

        if (!senses.HasLineOfSight && senses.TimeSinceLastSeen > loseSightDelay)
        {
            TransitionTo(State.Search);
            return;
        }

        if (Time.time - lastRepathTime >= repathInterval)
        {
            lastRepathTime = Time.time;
            Vector3 destination = senses.HasLineOfSight
                ? player.position
                : senses.LastKnownPosition;
            SetDestinationSafe(destination);
        }
    }

    private void TryAttack(Transform player)
    {
        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        if (animator != null) animator.SetTrigger(AttackHash);

        PlayerHealth health = player.GetComponentInParent<PlayerHealth>();
        if (health != null) health.TakeDamage(attackDamage);
    }

    #endregion

    // ======================================================================
    #region Search

    private void TickSearch()
    {
        if (senses.HasLineOfSight)
        {
            TransitionTo(State.Chase);
            return;
        }

        if (Time.time - stateEnterTime > searchTimeout ||
            searchPointsVisited > searchPoints)
        {
            TransitionTo(State.Patrol);
            return;
        }

        if (agent.pathPending) return;

        if (!reachedTargetPoint && AtDestination())
        {
            reachedTargetPoint = true;
            pauseTimer = 0f;
        }

        if (reachedTargetPoint)
        {
            pauseTimer += Time.deltaTime;
            transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);

            if (pauseTimer >= searchPointPause)
            {
                searchPointsVisited++;
                reachedTargetPoint = false;
                pauseTimer = 0f;
                PickSearchPoint();
            }
        }
    }

    private void PickSearchPoint()
    {
        Vector3 origin = senses.LastKnownPosition;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 candidate = origin + UnityEngine.Random.insideUnitSphere * searchRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
            {
                targetPoint = hit.position;
                SetDestinationSafe(targetPoint);
                return;
            }
        }
        // Nowhere reachable to check: give up.
        TransitionTo(State.Patrol);
    }

    #endregion

    // ======================================================================
    #region Dead

    /// <summary>Call from ZombieHealth when HP hits zero.</summary>
    public void Kill()
    {
        if (currentState == State.Dead) return;

        ExitState(currentState);
        currentState = State.Dead;
        stateEnterTime = Time.time;
        EnterState(State.Dead);
    }

    private void HandleDeathEntry()
    {
        if (agent.isOnNavMesh) agent.isStopped = true;
        agent.velocity = Vector3.zero;
        agent.enabled = false;

        if (bodyCollider != null) bodyCollider.enabled = false;
        senses.ForgetPlayer();
        senses.enabled = false;

        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetTrigger(DieHash);
        }

        Component dissolveOnDeath = null;
        foreach (Component component in GetComponentsInChildren<Component>(true))
        {
            if (component.GetType().Name == "DissolveOnDeath")
            {
                dissolveOnDeath = component;
                break;
            }
        }

        if (dissolveOnDeath != null)
        {
            System.Reflection.MethodInfo startDissolve = dissolveOnDeath.GetType().GetMethod(
                "StartDissolve",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null,
                System.Type.EmptyTypes,
                null);
            startDissolve?.Invoke(dissolveOnDeath, null);
        }

        if (corpseLingerTime > 0f)
            Invoke(nameof(Despawn), corpseLingerTime);
    }

    private void Despawn()
    {
        if (destroyOnDespawn) Destroy(gameObject);
        else gameObject.SetActive(false);
    }

    #endregion

    // ======================================================================
    #region Helpers

    private bool AtDestination()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return false;
        }

        return !agent.pathPending &&
               agent.remainingDistance <= agent.stoppingDistance + 0.25f;
    }

    private void SetDestinationSafe(Vector3 position)
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void FaceTarget(Vector3 position)
    {
        Vector3 direction = position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction),
            Time.deltaTime * 6f);
    }

    private void SetAlert(bool alert)
    {
        if (animator != null) animator.SetBool(AlertHash, alert);
    }

    private void DriveAnimator()
    {
        if (animator == null) return;
        float normalised = agent.velocity.magnitude / Mathf.Max(chaseSpeed, 0.01f);
        animator.SetFloat(SpeedHash, normalised, 0.12f, Time.deltaTime);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (!Application.isPlaying) return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(targetPoint, 0.3f);
        Gizmos.DrawLine(transform.position, targetPoint);
    }

    #endregion
}
