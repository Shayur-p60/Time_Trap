using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Everything the game manager puts on screen: the timer (top centre) with the lives beside it and the penalty popup,
// and the end screen. (The pause menu is one of GameMenus' screens.) Built in code by GameManager, in the same dark-panel style as GameHUD, on its
// own canvas above GameHUD's (so the end screen also covers a respawn fade).
// The menus are deliberately plain. The buttons are ordinary UI Buttons, so they need the scene's EventSystem
// (GameManager checks there is exactly one).
public class GameManagerUI : MonoBehaviour
{
    public Color buttonColour = new Color(0.14f, 0.14f, 0.14f, 0.95f);
    public Color buttonHoverColour = new Color(0.85f, 0.55f, 0.05f, 1f);
    [Tooltip("Seconds the penalty popup stays up.")]
    public float penaltySeconds = 1.6f;

    GameManager owner;
    Sprite panelSprite;

    // Most lives any difficulty gives: this many hearts are built.
    public const int MaxLifeIcons = 5;

    GameObject hudRoot;
    CanvasGroup hudGroup;
    RectTransform livesPanel;
    int livesMax = -1;
    RectTransform timerRect;
    TextMeshProUGUI timerLabel;
    readonly List<Image> lifeIcons = new List<Image>();
    TextMeshProUGUI penaltyLabel;
    Vector2 penaltyHome;
    float penaltyAge = -1f;

    // The win and loss screen (its own class: the sequence, the stats and the unlock list).
    public TimeTrap.UI.EndScreen End { get; private set; }

    public Button EndPlayAgainButton => End.PlayAgainButton;
    public Button EndMainMenuButton => End.MainMenuButton;

    string timerText = "";
    bool timerLow;
    bool timerHeld;
    int livesShown = -1;

    // For tests.
    public string TimerText => timerText;
    public bool TimerIsLow => timerLow;
    public Color TimerColour => timerLabel != null ? timerLabel.color : Color.clear;
    public float TimerScale => timerLabel != null ? timerLabel.transform.localScale.x : 1f;
    public int LivesShown => livesShown;
    public bool HudVisible => hudRoot != null && hudRoot.activeSelf && hudGroup.alpha > 0f;
    public float HudAlpha => hudGroup != null ? hudGroup.alpha : 0f;
    public bool EndVisible => End != null && End.Visible;
    public string EndTitle => End != null ? End.Title : "";
    public string EndText => End != null ? End.Subtitle : "";
    public bool PenaltyVisible => penaltyAge >= 0f;
    public string PenaltyText => penaltyLabel != null ? penaltyLabel.text : "";

    // ------------------------------------------------------------------ what the game manager calls

    public void SetTimer(string text, bool low, bool held)
    {
        if (timerLabel == null) return;
        timerLow = low;
        timerHeld = held;
        if (text == timerText) return;
        timerText = text;
        // fixed-width digits, so the panel does not twitch as they change
        timerLabel.text = "<mspace=0.62em>" + text + "</mspace>";
    }

    public void SetLives(int lives, int max)
    {
        if (lives == livesShown && max == livesMax) return;
        livesShown = lives;
        if (max != livesMax) LayoutLives(max);
        for (int i = 0; i < lifeIcons.Count; i++)
        {
            lifeIcons[i].gameObject.SetActive(i < max);
            lifeIcons[i].color = i < lives ? owner.lifeColour : owner.lifeLostColour;
        }
    }

    // The panel is as wide as the hearts the difficulty gives (three fit the original 148).
    void LayoutLives(int max)
    {
        livesMax = max;
        int shown = Mathf.Clamp(max, 1, lifeIcons.Count);
        float width = shown <= 3 ? 148f : 22f + shown * 42f;
        livesPanel.sizeDelta = new Vector2(width, 64f);
        livesPanel.anchoredPosition = new Vector2(96f + width * 0.5f, -46f);
        for (int i = 0; i < lifeIcons.Count; i++)
            ((RectTransform)lifeIcons[i].transform).anchoredPosition = new Vector2((i - (shown - 1) * 0.5f) * 42f, 0f);
    }

    // 0 hides the timer and the lives (the main menu); the end screen is not part of it.
    public void SetHudAlpha(float alpha)
    {
        if (hudGroup != null) hudGroup.alpha = alpha;
    }

    // "-10s", beside the timer: rises a little and fades out.
    public void ShowPenalty(string text)
    {
        if (penaltyLabel == null) return;
        penaltyLabel.text = text;
        penaltyAge = 0f;
        penaltyLabel.gameObject.SetActive(true);
        ApplyPenalty();
    }

    // No button stays selected from the last click, so Enter or Space cannot press one by accident.
    static void ClearSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowEnd(RunStats stats)
    {
        if (End == null) return;
        ClearSelection();
        hudRoot.SetActive(false);
        End.Show(stats);
    }

    // ------------------------------------------------------------------ every frame

    // Unscaled time throughout: this still runs while the game is frozen.
    void Update()
    {
        if (timerLabel != null)
        {
            if (timerLow && !timerHeld)
            {
                // one beat a second, two in the last ten seconds
                float beat = owner.TimeLeft <= 10f ? 2f : 1f;
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * beat * Mathf.PI * 2f);
                timerLabel.color = Color.Lerp(owner.lowTimeColour, Color.Lerp(owner.lowTimeColour, Color.white, 0.45f), pulse);
                timerLabel.transform.localScale = Vector3.one * (1f + 0.12f * pulse);
            }
            else
            {
                Color colour = timerLow ? owner.lowTimeColour : owner.textColour;
                if (timerHeld) colour.a = 0.45f;
                timerLabel.color = colour;
                timerLabel.transform.localScale = Vector3.one;
            }
        }

        if (penaltyAge >= 0f)
        {
            penaltyAge += Time.unscaledDeltaTime;
            if (penaltyAge >= penaltySeconds)
            {
                penaltyAge = -1f;
                penaltyLabel.gameObject.SetActive(false);
            }
            else ApplyPenalty();
        }
    }

    void ApplyPenalty()
    {
        float t = Mathf.Clamp01(penaltyAge / Mathf.Max(0.01f, penaltySeconds));
        Color colour = owner.lowTimeColour;
        // full for the first half, then fading
        colour.a = Mathf.Clamp01(2f - 2f * t);
        penaltyLabel.color = colour;
        ((RectTransform)penaltyLabel.transform).anchoredPosition = penaltyHome + new Vector2(0f, 14f * t);
    }

    // ------------------------------------------------------------------ construction

    public void Build(GameManager manager)
    {
        owner = manager;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150; // GameHUD is 100

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        // for the menu buttons
        gameObject.AddComponent<GraphicRaycaster>();

        panelSprite = GameHUD.MakePanelSprite();
        var top = new Vector2(0.5f, 1f);
        var centre = new Vector2(0.5f, 0.5f);

        // ---- HUD: [-10s] [ 4:59 ] [hearts]
        hudRoot = MakeFill("HUD", transform).gameObject;
        hudGroup = hudRoot.AddComponent<CanvasGroup>();
        hudGroup.interactable = false;
        hudGroup.blocksRaycasts = false;

        timerRect = MakePanel("Timer", hudRoot.transform, top, new Vector2(0f, -46f), new Vector2(176f, 64f), owner.panelColour);
        timerLabel = MakeText("Text", timerRect, centre, Vector2.zero, new Vector2(176f, 64f), 42f, TextAlignmentOptions.Center);

        livesPanel = MakePanel("Lives", hudRoot.transform, top, new Vector2(170f, -46f), new Vector2(148f, 64f), owner.panelColour);
        Sprite heart = MakeHeartSprite();
        for (int i = 0; i < Mathf.Max(owner.startLives, MaxLifeIcons); i++)
        {
            var icon = MakeRect("Life" + (i + 1), livesPanel, centre, new Vector2((i - 1) * 42f, 0f), new Vector2(34f, 34f));
            var image = icon.gameObject.AddComponent<Image>();
            image.sprite = heart;
            image.raycastTarget = false;
            lifeIcons.Add(image);
        }

        penaltyHome = new Vector2(-182f, -46f);
        penaltyLabel = MakeText("Penalty", hudRoot.transform, top, penaltyHome, new Vector2(170f, 56f), 38f, TextAlignmentOptions.Right);
        penaltyLabel.gameObject.SetActive(false);

        // ---- end screen
        End = gameObject.AddComponent<TimeTrap.UI.EndScreen>();
        End.Build(owner, transform);
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    // Covers its parent.
    static RectTransform MakeFill(string name, Transform parent)
    {
        var rt = MakeRect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image AddImage(RectTransform rt, Sprite sprite, Color colour)
    {
        var image = rt.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        if (sprite != null) image.type = Image.Type.Sliced;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    // A dark rounded panel, as in GameHUD.
    RectTransform MakePanel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color colour)
    {
        var rt = MakeRect(name, parent, anchor, position, size);
        AddImage(rt, panelSprite, colour);
        return rt;
    }

    TextMeshProUGUI MakeText(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var rt = MakeRect(name, parent, anchor, position, size);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (owner.font != null) label.font = owner.font;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = owner.textColour;
        label.raycastTarget = false;
        label.text = "";
        // thin dark outline, so the letters hold their edge on any background
        label.outlineColor = new Color32(0, 0, 0, 255);
        label.outlineWidth = 0.16f;
        return label;
    }

    // A UI Button on a rounded panel. The Button tints the (white) panel: normal, hover, pressed.
    Button MakeButton(Transform parent, string text, Vector2 position, UnityAction action)
    {
        var rt = MakePanel(text + "Button", parent, new Vector2(0.5f, 0.5f), position, new Vector2(300f, 68f), Color.white);
        var image = rt.GetComponent<Image>();
        image.raycastTarget = true;
        MakeText("Text", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 68f), 34f, TextAlignmentOptions.Center).text = text;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colours = button.colors;
        colours.normalColor = buttonColour;
        colours.selectedColor = buttonColour;
        colours.highlightedColor = buttonHoverColour;
        colours.pressedColor = Color.Lerp(buttonHoverColour, Color.black, 0.3f);
        colours.colorMultiplier = 1f;
        colours.fadeDuration = 0.05f;
        button.colors = colours;
        button.onClick.AddListener(GameAudio.UiClick);
        button.onClick.AddListener(action);
        return button;
    }

    // A white heart with a soft edge, for the life icons.
    public static Sprite MakeHeartSprite()
    {
        const int size = 64;
        const int samples = 3;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        // the curve (x^2 + y^2 - 1)^3 = x^2 y^3, which fits in about -1.2..1.2 by -1..1.25
                        float x = ((px + (sx + 0.5f) / samples) / size - 0.5f) * 2.6f;
                        float y = ((py + (sy + 0.5f) / samples) / size - 0.5f) * 2.6f + 0.12f;
                        float a = x * x + y * y - 1f;
                        if (a * a * a - x * x * y * y * y <= 0f) inside++;
                    }
                byte alpha = (byte)Mathf.RoundToInt(255f * inside / (samples * samples));
                pixels[py * size + px] = new Color32(255, 255, 255, alpha);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
