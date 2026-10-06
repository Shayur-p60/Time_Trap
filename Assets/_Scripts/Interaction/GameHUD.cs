using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The small screen-space HUD used by the interaction system: interaction prompt, short messages, crosshair, ammo counter,
// compass arrow and the full-screen fade. It builds itself the first time something needs it in Play mode, so no scene
// setup is required. To restyle it, put a GameHUD component in the scene and assign a font.
[DefaultExecutionOrder(-100)]
public class GameHUD : MonoBehaviour
{
    [Tooltip("Optional. Leave empty to use the TextMeshPro default font.")]
    public TMP_FontAsset font;
    public Color textColour = new Color(0.92f, 0.92f, 0.88f, 1f);
    public Color crosshairColour = new Color(1f, 1f, 1f, 0.8f);

    static GameHUD instance;

    TextMeshProUGUI promptLabel;
    TextMeshProUGUI messageLabel;
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
        if (hud == null || hud.promptLabel == null) return;
        if (hud.promptLabel.text != (text ?? "")) hud.promptLabel.text = text ?? "";
    }

    public static void Message(string text, float seconds = 2.5f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var hud = Get(true);
        if (hud == null || hud.messageLabel == null) return;
        hud.messageLabel.text = text;
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
        if (messageLabel != null && messageLabel.text.Length > 0 && Time.unscaledTime >= messageHideTime)
            messageLabel.text = "";
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

        promptLabel = MakeText("Prompt", centre, new Vector2(0f, -90f), new Vector2(900f, 50f), 30f, TextAlignmentOptions.Center);
        messageLabel = MakeText("Message", new Vector2(0.5f, 0f), new Vector2(0f, 220f), new Vector2(900f, 50f), 28f, TextAlignmentOptions.Center);
        ammoLabel = MakeText("Ammo", new Vector2(1f, 0f), new Vector2(-170f, 70f), new Vector2(300f, 50f), 34f, TextAlignmentOptions.Right);

        // compass: arrow at the top of the screen with a distance label under it
        compassArrow = MakeRect("CompassArrow", transform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(56f, 56f));
        var arrowImage = compassArrow.gameObject.AddComponent<Image>();
        arrowImage.sprite = MakeArrowSprite();
        arrowImage.color = new Color(0.75f, 0.45f, 1f, 0.95f);
        arrowImage.raycastTarget = false;
        compassArrow.gameObject.SetActive(false);
        compassLabel = MakeText("CompassLabel", new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(400f, 40f), 24f, TextAlignmentOptions.Center);
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

    TextMeshProUGUI MakeText(string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
    {
        var rt = MakeRect(name, transform, anchor, position, size);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = textColour;
        label.raycastTarget = false;
        label.text = "";
        return label;
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
