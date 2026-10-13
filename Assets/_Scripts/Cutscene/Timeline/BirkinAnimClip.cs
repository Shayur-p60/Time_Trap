using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

// One clip on a Birkin Animation track: when the Timeline reaches it, Birkin cross-fades to Animation (a state of
// Birkin.controller). Only the start of the clip matters; its length is just how it shows in the Timeline window.
[Serializable]
public class BirkinAnimClip : PlayableAsset, ITimelineClipAsset
{
    public BirkinAnim animation = BirkinAnim.Talk1;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<BirkinAnimBehaviour>.Create(graph);
        playable.GetBehaviour().animation = animation;
        return playable;
    }
}

public class BirkinAnimBehaviour : PlayableBehaviour
{
    public BirkinAnim animation;

    bool pending;

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        pending = true;
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        if (!pending || !Application.isPlaying) return;
        pending = false;
        var actor = playerData as BirkinActor;
        if (actor != null) actor.Play(animation);
    }
}
