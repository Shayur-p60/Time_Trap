using UnityEngine.Timeline;

// A Timeline track of Dr Birkin's animations for the intro cutscene. Bind it to the BirkinActor; each clip on it
// starts one of his animations (cross-faded by the actor) at the moment the clip begins.
[TrackColor(0.9f, 0.65f, 0.25f)]
[TrackClipType(typeof(BirkinAnimClip))]
[TrackBindingType(typeof(BirkinActor))]
public class BirkinAnimTrack : TrackAsset
{
}
