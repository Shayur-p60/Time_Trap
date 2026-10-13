using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// One clip on a Portal Power track: over the length of the clip the portal's Power goes from From to To (0 = idle,
// 1 = open), eased by Ease. CutscenePortal turns that number into the surface opening, the ring's strips
// brightening, the glow swelling, the hum and the flickering lamps.
[Serializable]
public class PortalPowerClip : PlayableAsset, ITimelineClipAsset
{
    [Range(0f, 1f)] public float from;
    [Range(0f, 1f)] public float to = 1f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<PortalPowerBehaviour>.Create(graph);
        playable.GetBehaviour().clip = this;
        return playable;
    }
}

public class PortalPowerBehaviour : PlayableBehaviour
{
    public PortalPowerClip clip;

    CutscenePortal portal;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!Application.isPlaying || clip == null) return;
        portal = playerData as CutscenePortal;
        double duration = playable.GetDuration();
        Apply(duration > 0.0 ? Mathf.Clamp01((float)(playable.GetTime() / duration)) : 1f);
    }

    // The clip ended: the last frame drawn was a moment short of the final value.
    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (!Application.isPlaying || clip == null || portal == null) return;
        if (playable.GetTime() >= playable.GetDuration() - 0.05) Apply(1f);
        portal = null;
    }

    void Apply(float t)
    {
        if (portal == null) return;
        float k = clip.ease != null && clip.ease.length > 0 ? clip.ease.Evaluate(t) : t;
        portal.Power = Mathf.LerpUnclamped(clip.from, clip.to, k);
    }
}
