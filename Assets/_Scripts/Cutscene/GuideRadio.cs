using System.Collections;
using UnityEngine;

// Dr Birkin's radio lines during the run. They play like the arrival's "You're through" line: CutsceneDirector's radio
// queue (the Radio mixer group, a crackle before and after, the bottom subtitle, the music stepping back), so two
// lines never overlap. Goes on Map/Gameplay/GuideRadio, whose trigger collider sits across the way out of Gate B.
//
//   Elevator   the first time the player walks through the trigger after the arrival. Once per run: not on the way
//              back, not after a respawn, not on a later pass.
//   Armory     Armory Delay seconds after the power is restored (after the power-on sting). Once per run.
// A scene reload (Restart, Play Again, Main Menu) starts a new run, so both can play again.
[RequireComponent(typeof(Collider))]
public class GuideRadio : MonoBehaviour
{
    public string speaker = "DR. BIRKIN";

    [Header("Leaving Gate B")]
    public AudioClip elevatorVoice;
    [TextArea(2, 4)] public string elevatorLine = "The elevator won't work without power, find the door to the stairwell";

    [Header("Power restored")]
    public AudioClip armoryVoice;
    [TextArea(2, 6)] public string armoryLine = "Happiness can be found, even in the darkest of times, if one only remembers to turn on the light. You might need some protection. You should have passed the armory on your way here. You\u2019ll find something useful inside.";
    [Tooltip("Seconds after the power comes on (game time, so the pause menu holds it).")]
    public float armoryDelay = 1.5f;

    [Tooltip("Seconds a line's subtitle stays up when it has no voice clip.")]
    public float subtitleSeconds = 5f;

    // For tests.
    public bool ElevatorPlayed { get; private set; }
    public bool ArmoryPlayed { get; private set; }

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    // After GameManager.ResetAllStatics on a restart (it clears the event), as every listener of it does.
    void OnEnable()
    {
        PowerState.OnPowerChanged += HandlePower;
    }

    void OnDisable()
    {
        PowerState.OnPowerChanged -= HandlePower;
        StopAllCoroutines();
    }

    void OnTriggerEnter(Collider other)
    {
        if (ElevatorPlayed || !(other is CharacterController) || !other.CompareTag("Player")) return;
        // the arrival walk is still the cutscene: the run has not started
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        ElevatorPlayed = Say("Elevator", elevatorVoice, elevatorLine);
    }

    void HandlePower(bool powered)
    {
        if (!powered || ArmoryPlayed || !Application.isPlaying) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        ArmoryPlayed = true;
        StartCoroutine(ArmoryAfterDelay());
    }

    IEnumerator ArmoryAfterDelay()
    {
        yield return new WaitForSeconds(armoryDelay);
        Say("Armory", armoryVoice, armoryLine);
    }

    bool Say(string which, AudioClip clip, string line)
    {
        var director = CutsceneDirector.Instance;
        bool queued = director != null && director.QueueRadio(clip, speaker, line, subtitleSeconds);
        Debug.Log("[GuideRadio] " + which + " line " + (queued ? "queued" : "not played (no radio, or the run is over)"));
        return queued;
    }
}
