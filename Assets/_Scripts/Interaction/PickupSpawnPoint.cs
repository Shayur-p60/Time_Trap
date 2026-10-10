using UnityEngine;

public enum StageReachable { Stage1, Stage2Only }

// Marks a place where the PickupSpawner may put a pickup. Sit it on the floor or table surface.
public class PickupSpawnPoint : MonoBehaviour
{
    [Tooltip("Stage1: the player can get here before the power is on. Stage2Only: behind a power-locked door.")]
    public StageReachable stageReachable = StageReachable.Stage1;

    // What "Preview Layout" on the PickupSpawner put here (editor only, not saved).
    [System.NonSerialized] public string previewLabel;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = stageReachable == StageReachable.Stage1 ? new Color(0.3f, 1f, 0.4f, 0.9f) : new Color(1f, 0.6f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.15f, 0.15f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.6f);

        string text = name + "  [" + stageReachable + "]";
        if (!string.IsNullOrEmpty(previewLabel)) text += "\n" + previewLabel;
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.75f, text);
    }
#endif
}
