using System.Text;
using UnityEngine;

// Shows GameProgress on the HUD: the objective list in the top-left corner (done items ticked and dimmed), a short
// centre-screen message on each completion and "ESCAPED" at the end. Goes on the same object as GameProgress.
public class ObjectiveHUD : MonoBehaviour
{
    [Tooltip("Seconds a completion message stays on screen.")]
    public float bannerSeconds = 3f;
    public Color doneColour = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color tickColour = new Color(0.45f, 0.9f, 0.5f, 1f);
    public Color headingColour = new Color(1f, 0.7f, 0.1f, 1f);

    string lastBanner = "";

    void OnEnable()
    {
        GameProgress.OnObjectiveCompleted += HandleObjectiveCompleted;
        GameProgress.OnStageChanged += HandleStageChanged;
        GameProgress.OnPlayerEscaped += HandleEscaped;
        GameProgress.OnProgressReset += Refresh;
    }

    void OnDisable()
    {
        GameProgress.OnObjectiveCompleted -= HandleObjectiveCompleted;
        GameProgress.OnStageChanged -= HandleStageChanged;
        GameProgress.OnPlayerEscaped -= HandleEscaped;
        GameProgress.OnProgressReset -= Refresh;
    }

    void Start()
    {
        Refresh();
    }

    public static string Label(Objective objective)
    {
        switch (objective)
        {
            case Objective.Power: return "Restore power";
            case Objective.Pathogen: return "Retrieve pathogen sample";
            default: return "Download research data";
        }
    }

    public static string CompletionMessage(Objective objective)
    {
        switch (objective)
        {
            case Objective.Power: return "POWER RESTORED";
            case Objective.Pathogen: return "PATHOGEN SAMPLE SECURED";
            default: return "RESEARCH DATA DOWNLOADED";
        }
    }

    void HandleObjectiveCompleted(Objective objective)
    {
        lastBanner = CompletionMessage(objective);
        GameHUD.Banner(lastBanner, bannerSeconds);
        Refresh();
    }

    void HandleStageChanged(GameStage stage)
    {
        // the last objective and Stage 3 arrive together: one message carries both
        if (stage == GameStage.Stage3)
            GameHUD.Banner(lastBanner + "\n<size=60%>Escape through Gate B</size>", bannerSeconds + 1.5f);
        Refresh();
    }

    void HandleEscaped()
    {
        GameHUD.Banner("ESCAPED", 6f);
        Refresh();
    }

    public void Refresh()
    {
        GameHUD.Objectives(BuildText(GameProgress.Instance));
    }

    // The list as rich text, one objective per line.
    public string BuildText(GameProgress progress)
    {
        if (progress == null) return "";
        var sb = new StringBuilder();
        sb.Append("<size=75%><color=#").Append(ColorUtility.ToHtmlStringRGB(headingColour)).Append(">OBJECTIVES</color></size>");
        AppendLine(sb, Label(Objective.Power), progress.HasPower);
        AppendLine(sb, Label(Objective.Pathogen), progress.HasPathogen);
        AppendLine(sb, Label(Objective.Data), progress.HasData);
        if (progress.AllObjectivesComplete) AppendLine(sb, "Escape through Gate B", progress.HasEscaped);
        return sb.ToString();
    }

    void AppendLine(StringBuilder sb, string label, bool done)
    {
        // the box is monospaced, so the labels line up whether it is ticked or not
        sb.Append('\n');
        if (done)
        {
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tickColour)).Append("><mspace=0.55em>[x]</mspace></color> ");
            sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(doneColour)).Append("><s>").Append(label).Append("</s></color>");
        }
        else
        {
            sb.Append("<mspace=0.55em>[ ]</mspace> ").Append(label);
        }
    }
}
