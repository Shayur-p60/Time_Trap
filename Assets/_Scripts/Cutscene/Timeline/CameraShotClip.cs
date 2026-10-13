using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// One camera move on a Camera Shot track: from the start pose to the end pose over the length of the clip, eased by
// Ease. The poses are local to the camera rig's Shot Space (the void stage). Stretch the clip in the Timeline window
// to change how long the move takes; start a new clip (or a new Timeline) for a cut.
[Serializable]
public class CameraShotClip : PlayableAsset, ITimelineClipAsset
{
    public Vector3 startPosition;
    public Vector3 startEuler;
    public Vector3 endPosition;
    public Vector3 endEuler;
    public float startFieldOfView = 35f;
    public float endFieldOfView = 35f;
    [Tooltip("0 to 1 across the clip: how far along the move the camera is.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CameraShotBehaviour>.Create(graph);
        playable.GetBehaviour().clip = this;
        return playable;
    }
}

public class CameraShotBehaviour : PlayableBehaviour
{
    public CameraShotClip clip;

    CutsceneCameraRig rig;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        // the game only: scrubbing the Timeline in the editor must not move the saved camera
        if (!Application.isPlaying || clip == null) return;
        rig = playerData as CutsceneCameraRig;
        double duration = playable.GetDuration();
        Apply(duration > 0.0 ? Mathf.Clamp01((float)(playable.GetTime() / duration)) : 1f);
    }

    // The clip ended: the last frame drawn was a moment short of the end pose.
    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (!Application.isPlaying || clip == null || rig == null) return;
        if (playable.GetTime() >= playable.GetDuration() - 0.05) Apply(1f);
        rig = null;
    }

    void Apply(float t)
    {
        if (rig == null) return;
        float k = clip.ease != null && clip.ease.length > 0 ? clip.ease.Evaluate(t) : t;
        rig.SetShot(
            Vector3.LerpUnclamped(clip.startPosition, clip.endPosition, k),
            Quaternion.SlerpUnclamped(Quaternion.Euler(clip.startEuler), Quaternion.Euler(clip.endEuler), k),
            Mathf.LerpUnclamped(clip.startFieldOfView, clip.endFieldOfView, k));
    }
}
