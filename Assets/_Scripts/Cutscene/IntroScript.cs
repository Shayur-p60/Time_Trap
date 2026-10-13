using System;
using UnityEngine;
using UnityEngine.Timeline;

// Every word of the intro cutscene, and what plays with each line. One asset (Assets/_Data/Cutscene/IntroScript),
// so the text can be edited without touching code. CutsceneDirector reads it.
//
//   Typewriter   beat 1: the lines typed on the black screen, one after another.
//   Void lines   beat 2: Dr Birkin in the void.
//   Chamber lines  beat 3: Dr Birkin in the portal chamber. The walk into the portal follows the last one.
//   Radio line   beat 5: the subtitle after the arrival.
// A dialogue line waits for the player (E, left click or Enter). Its two Timeline slots start when the line does:
//   Shot     a camera move (CameraShotTrack). A line with the same Shot as the one before carries on in that shot,
//            so the push-in is not restarted; a different Shot is a cut. Empty = the camera stays as it is.
//   Action   what happens during the line: Birkin's animation (BirkinAnimTrack) and, on the last line, the portal
//            powering up (PortalPowerTrack). Empty = nothing changes.
[CreateAssetMenu(fileName = "IntroScript", menuName = "Time Trap/Intro Script")]
public class IntroScript : ScriptableObject
{
    [Serializable]
    public class Line
    {
        [TextArea(2, 5)] public string text;
        [Tooltip("Optional. With a clip, the text types out over the spoken part of the clip and Birkin's mouth follows its volume; without one the text types at Characters Per Second with blips, and his mouth moves while it types.")]
        public AudioClip voice;
        [Tooltip("Camera move for this line (a Timeline with a Camera Shot track). Same as the line before = no cut.")]
        public TimelineAsset shot;
        [Tooltip("Birkin's animation and anything else that happens during the line (a Timeline).")]
        public TimelineAsset action;
    }

    [Header("Beat 1: typewriter")]
    public string[] typewriterLines = new string[0];
    [Tooltip("Characters typed per second.")]
    public float typewriterSpeed = 22f;
    [Tooltip("Seconds a finished line is held before the next one starts.")]
    public float lineHold = 0.9f;
    [Tooltip("Seconds the last line is held before it fades.")]
    public float lastLineHold = 1.6f;
    [Tooltip("Seconds the text takes to fade out at the end.")]
    public float lastLineFade = 2.5f;

    [Header("Dialogue")]
    public string speaker = "DR. BIRKIN · AI GUIDE";
    [Tooltip("Characters typed per second in the dialogue box.")]
    public float charactersPerSecond = 40f;

    [Header("Beat 2: the void")]
    public Line[] voidLines = new Line[0];

    [Header("Beat 3: the portal chamber")]
    public Line[] chamberLines = new Line[0];

    [Header("Beat 5: arrival")]
    public string radioSpeaker = "DR. BIRKIN";
    public string radioLine = "You're through. The clock's running.";
    [Tooltip("Optional. Played as a radio voice (the Radio mixer group), with a crackle before and after.")]
    public AudioClip radioVoice;
    [Tooltip("Seconds the subtitle stays up.")]
    public float radioSeconds = 4f;
}
