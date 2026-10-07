using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The small screen-space HUD used by the interaction system: interaction prompt, short messages, crosshair, ammo counter,
// compass arrow and the full-screen fade. The prompt and the messages sit on dark rounded panels with outlined text,
// and the canvas is drawn over the finished frame (after bloom), so they stay readable over bright, glowing things. It builds itself the first time something needs it in Play mode, so no scene
// setup is required. To restyle it, put a GameHUD component in the scene and assign a font.
[DefaultExecutionOrder(-100)]
public class GameHUD : MonoBehaviour
{
    [Tooltip("Optional. Leave empty to use the TextMeshPro default font.")]
    public TMP_FontAsset font;
    public Color textColour = Color.white;
    [Tooltip("Colour of the key at the start of a prompt, for example the [E] in \"[E] Open door\".")]
    public Color keyColour = new Color(1f, 0.7f, 0.1f, 1f);
    [Tooltip("Panel behind the prompt and the messages, so they stay readable over bright or glowing things.")]
    public Color panelColour = new Color(0f, 0f, 0f, 0.7f);
    public Color crosshairColour = new Color(1f, 1f, 1f, 0.8f);

    static GameHUD instance;

    // A line of text on a dark rounded panel that resizes to fit it and hides itself when empty.
    class TextPanel
    {
        public RectTransform rect;
        public TextMeshProUGUI label;
        public Vector2 padding = new Vector2(44f, 22f);
        string shown = "";

        public string Shown => shown;

        public void Set(string text)
        {
            text = text ?? "";
            if (text == shown) return;
            shown = text;
            bool empty = text.Length == 0;
            rect.gameObject.SetActive(!empty);
            label.text = text;
            if (empty) return;
            Vector2 size = label.GetPreferredValues(text);
            rect.sizeDelta = new Vector2(Mathf.Ceil(size.x), Mathf.Ceil(size.y)) + padding;
        }
    }

    TextPanel promptPanel;
    TextPanel messagePanel;
    TextMeshProUGUI ammoLabel;
    TextMeshProUGUI compassLabel;
    GameObject crosshair;
    RectTransform compassArrow;
    Image fadeImage;
    float messageHideTime;

    // ------------------------------------------------------------------ static access

    static GameHUD Get(bool create)
    {
        if (instance == null && create && Application.isPlaying)
        {
            var go = new GameObject("GameHUD");
            instance = go.AddComponent<GameHUD>();
        }
        return instance;
    }

    public static void Prompt(string text)
    {
        bool empty = string.IsNullOrEmpty(text);
        var hud = Get(!empty);
        if (hud == null || hud.promptPanel == null) return;
        hud.promptPanel.Set(empty ? "" : hud.HighlightKey(text));
    }

    // What the prompt and message panels are showing right now (rich text), for tests.
    public static string ShownPrompt => instance != null && instance.promptPanel != null ? instance.promptPanel.Shown : "";
    public static string ShownMessage => instance != null && instance.messagePanel != null ? instance.messagePanel.Shown : "";

    // "[E] Open door" -> the key in the highlight colour, the rest in the normal text colour.
    string HighlightKey(string text)
    {
        if (text.Length < 3 || text[0] != '[') return text;
        int close = text.IndexOf(']');
        if (close < 0) return text;
        return "<color=#" + ColorUtility.ToHtmlStringRGB(keyColour) + ">" + text.Substring(0, close + 1) + "</color>" + text.Substring(close + 1);
    }

    public static void Message(string text, float seconds = 2.5f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var hud = Get(true);
        if (hud == null || hud.messagePanel == null) return;
        hud.messagePanel.Set(text);
        hud.messageHideTime = Time.unscaledTime + seconds;
    }

    public static void Crosshair(bool visible)
    {
        var hud = Get(visible);
        if (hud != null && hud.crosshair != null) hud.crosshair.SetActive(visible);
    }

    // Null or empty hides the counter.
    public static void Ammo(string text)
    {
        var hud = Get(!string.IsNullOrEmpty(text));
        if (hud != null && hud.ammoLabel != null) hud.ammoLabel.text = text ?? "";
    }

    // 0 = clear, 1 = black.
    public static void Fade(float alpha)
    {
        var hud = Get(alpha > 0f);
        if (hud == null || hud.fadeImage == null) return;
        var c = hud.fadeImage.color;
        c.a = Mathf.Clamp01(alpha);
        hud.fadeImage.color = c;
        hud.fadeImage.enabled = c.a > 0f;
    }

    // angle: degrees to the target, clockwise from straight ahead. hasTarget false shows the label only.
    public static void Compass(bool visible, bool hasTarget = false, float angle = 0f, string label = "")
    {
        var hud = Get(visible);
        if (hud == null || hud.compassArrow == null) return;
        hud.compassArrow.gameObject.SetActive(visible && hasTarget);
        hud.compassLabel.gameObject.SetActive(visible);
        if (!visible) return;
        hud.compassArrow.localRotation = Quaternion.Euler(0f, 0f, -angle);
        hud.compassLabel.text = label ?? "";
    }

    // ------------------------------------------------------------------ lifecycle

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        Build();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (messagePanel != null && messagePanel.Shown.Length > 0 && Time.unscaledTime >= messageHideTime)
            messagePanel.Set("");
    }

    // ------------------------------------------------------------------ construction

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        var centre = new Vector2(0.5f, 0.5f);

        // crosshair: a dot and four ticks
        var cross = MakeRect("Crosshair", transform, centre, Vector2.zero, new Vector2(40f, 40f));
        MakeBar(cross, Vector2.zero, new Vector2(3f, 3f));
        MakeBar(cross, new Vector2(0f, 11f), new Vector2(2f, 8f));
        MakeBar(cross, new Vector2(0f, -11f), new Vector2(2f, 8f));
        MakeBar(cross, new Vector2(11f, 0f), new Vector2(8f, 2f));
        MakeBar(cross, new Vector2(-11f, 0f), new Vector2(8f, 2f));
        crosshair = cross.gameObject;
        crosshair.SetActive(false);

        // prompt: just under the crosshair. message: lower down. Both on a dark panel.
        Sprite panelSprite = MakePanelSprite();
        promptPanel = MakePanel("Prompt", centre, new Vector2(0f, -74f), 32f, panelSprite);
        messagePanel = MakePanel("Message", new Vector2(0.5f, 0f), new Vector2(0f, 220f), 28f, panelSprite);
        ammoLabel = MakeText("Ammo", transform, new Vector2(1f, 0f), new Vector2(-170f, 70f), new Vector2(300f, 50f), 34f, TextAlignmentOptions.Right);

        // compass: arrow at the top of the screen with a distance label under it
        compassArrow = MakeRect("CompassArrow", transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(56f, 56f));
        var arrowImage = compassArrow.gameObject.AddComponent<Image>();
        arrowImage.sprite = MakeArrowSprite();
        arrowImage.color = new Color(0.75f, 0.45f, 1f, 0.95f);
        arrowImage.raycastTarget = false;
        compassArrow.gameObject.SetActive(false);
        compassLabel = MakeText("CompassLabel", transform, new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(400f, 40f), 24f, TextAlignmentOptions.Center);
        compassLabel.gameObject.SetActive(false);

        // fade: last, so it covers everything else
        var fade = MakeRect("Fade", transform, centre, Vector2.zero, Vector2.zero);
        fade.anchorMin = Vector2.zero;
        fade.anchorMax = Vector2.one;
        fade.offsetMin = Vector2.zero;
        fade.offsetMax = Vector2.zero;
        fadeImage = fade.gameObject.AddComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.raycastTarget = false;
        fadeImage.enabled = false;
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

    void MakeBar(RectTransform parent, Vector2 position, Vector2 size)
    {
        var rt = MakeRect("Bar", parent, new Vector2(0.5f, 0.5f), position, size);
        var image = rt.gameObject.AddComponent<Image>();
        image.color = crosshairColour;
        image.raycastTarget = false;
    }

    TextMeshProUGUI MakeText(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var rt = MakeRect(name, parent, anchor, position, size);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = textColour;
        label.raycastTarget = false;
        label.text = "";
        // thin dark outline, so the letters hold their edge on any background
        label.outlineColor = new Color32(0, 0, 0, 255);
        label.outlineWidth = 0.16f;
        return label;
    }

    TextPanel MakePanel(string name, Vector2 anchor, Vector2 position, float fontSize, Sprite sprite)
    {
        var rt = MakeRect(name, transform, anchor, position, new Vector2(200f, 60f));
        var background = rt.gameObject.AddComponent<Image>();
        background.sprite = sprite;
        background.type = Image.Type.Sliced;
        background.color = panelColour;
        background.raycastTarget = false;

        var label = MakeText("Text", rt, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, fontSize, TextAlignmentOptions.Center);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        rt.gameObject.SetActive(false);
        return new TextPanel { rect = rt, label = label };
    }

    // A white rounded rectangle, 9-sliced so the corners keep their shape at any panel size.
    static Sprite MakePanelSprite()
    {
        const int size = 48;
        const float radius = 14f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // distance outside the rounded outline, in pixels (soft one-pixel edge)
                float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius), 0f);
                float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius), 0f);
                float outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f - outside));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
    }

    // A plain white triangle pointing up.
    static Sprite MakeArrowSprite()
    {
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            float halfWidth = (size - 1 - y) / (float)(size - 1) * (size * 0.5f - 1f);
            for (int x = 0; x < size; x++)
            {
                bool inside = Mathf.Abs(x - (size - 1) * 0.5f) <= halfWidth;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
