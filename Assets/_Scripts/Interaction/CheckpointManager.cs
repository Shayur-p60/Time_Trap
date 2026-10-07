using System.Collections;
using UnityEngine;

// Remembers where the player was at the start and each time an objective was completed, and puts them back there
// after a death (DeathZone calls RespawnAtCheckpoint). No lives or timer here: that is the game manager's job.
public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    [Tooltip("Seconds for each fade (out, then in).")]
    public float fadeTime = 0.5f;

    public bool HasCheckpoint { get; private set; }
    public Vector3 CheckpointPosition { get; private set; }
    public float CheckpointYaw { get; private set; }
    // "Start", or the objective that set it.
    public string CheckpointLabel { get; private set; } = "";
    public int CheckpointCount { get; private set; }
    public bool IsRespawning { get; private set; }
    public int RespawnCount { get; private set; }

    Transform player;
    Vector3 startPosition;
    float startYaw;
    bool hasStart;
    // an objective completed in mid-air (or mid-respawn) is recorded once the player is on the ground
    bool pending;
    string pendingLabel;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        GameProgress.OnObjectiveCompleted += HandleObjectiveCompleted;
        GameProgress.OnProgressReset += HandleReset;
    }

    void OnDisable()
    {
        GameProgress.OnObjectiveCompleted -= HandleObjectiveCompleted;
        GameProgress.OnProgressReset -= HandleReset;
        if (IsRespawning)
        {
            IsRespawning = false;
            GameHUD.Fade(0f);
        }
    }

    void Start()
    {
        Transform p = FindPlayer();
        if (p == null) return;
        startPosition = p.position;
        startYaw = p.eulerAngles.y;
        hasStart = true;
        RecordCheckpoint(startPosition, startYaw, "Start");
    }

    void Update()
    {
        if (pending) TryRecordPending();
    }

    Transform FindPlayer()
    {
        if (player != null) return player;
        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) player = tagged.transform;
        return player;
    }

    void HandleObjectiveCompleted(Objective objective)
    {
        pending = true;
        pendingLabel = objective.ToString();
        TryRecordPending();
    }

    void TryRecordPending()
    {
        Transform p = FindPlayer();
        if (p == null || IsRespawning) return;
        var controller = p.GetComponent<CharacterController>();
        if (controller != null && controller.enabled && !controller.isGrounded) return;
        pending = false;
        RecordCheckpoint(p.position, p.eulerAngles.y, pendingLabel);
    }

    public void RecordCheckpoint(Vector3 position, float yaw, string label)
    {
        CheckpointPosition = position;
        CheckpointYaw = yaw;
        CheckpointLabel = label;
        HasCheckpoint = true;
        CheckpointCount++;
    }

    void HandleReset()
    {
        pending = false;
        if (hasStart) RecordCheckpoint(startPosition, startYaw, "Start");
    }

    // Fades out, puts the player on the latest checkpoint and fades back in. False if there is nothing to respawn to
    // or a respawn is already running.
    public bool RespawnAtCheckpoint()
    {
        if (!HasCheckpoint || IsRespawning || FindPlayer() == null) return false;
        StartCoroutine(Respawn());
        return true;
    }

    IEnumerator Respawn()
    {
        IsRespawning = true;

        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            GameHUD.Fade(t / fadeTime);
            yield return null;
        }
        GameHUD.Fade(1f);

        TeleportToCheckpoint();
        RespawnCount++;

        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            GameHUD.Fade(1f - t / fadeTime);
            yield return null;
        }
        GameHUD.Fade(0f);

        IsRespawning = false;
    }

    // The move on its own, without the fade.
    public void TeleportToCheckpoint()
    {
        Transform p = FindPlayer();
        if (p == null || !HasCheckpoint) return;

        // the CharacterController must be off while the transform is moved, or it snaps back
        var controller = p.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        p.SetPositionAndRotation(CheckpointPosition, Quaternion.Euler(0f, CheckpointYaw, 0f));
        Physics.SyncTransforms();
        if (controller != null) controller.enabled = true;

        var movement = p.GetComponent<PlayerMovementController>();
        if (movement != null) movement.ResetFall();
    }
}
