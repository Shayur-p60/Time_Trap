using UnityEngine.Timeline;

// A Timeline track that powers the chamber's portal up (or down) in the intro cutscene. Bind it to the
// CutscenePortal; each clip on it moves the portal's Power from one value to another.
[TrackColor(0.2f, 0.9f, 0.75f)]
[TrackClipType(typeof(PortalPowerClip))]
[TrackBindingType(typeof(CutscenePortal))]
public class PortalPowerTrack : TrackAsset
{
}
