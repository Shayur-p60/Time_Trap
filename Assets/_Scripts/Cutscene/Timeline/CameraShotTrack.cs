using UnityEngine.Timeline;

// A Timeline track of camera moves for the intro cutscene. Bind it to the CutsceneCameraRig; each clip on it is a
// Camera Shot (a move from one pose to another).
[TrackColor(0.25f, 0.8f, 0.9f)]
[TrackClipType(typeof(CameraShotClip))]
[TrackBindingType(typeof(CutsceneCameraRig))]
public class CameraShotTrack : TrackAsset
{
}
