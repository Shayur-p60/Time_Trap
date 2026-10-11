using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // The look of every menu, and the parts they are built from: text, panels, buttons, sliders, selectors and key-caps.
    // Like GameHUD, the menus are built in code, so there is nothing to keep in step in the scene.
    //   Colours   charcoal panels, amber headings and hover, cyan highlights, red for a loss, gold for Time Lord.
    //   Buttons   on hover or keyboard focus the text turns amber, slides right and shows a small marker, with a tick.
    //             Hovering selects, so the mouse and the arrow keys share one highlight.
    public static class MenuKit
    {
        public static readonly Color Charcoal = new Color32(0x17, 0x18, 0x1C, 0xFF);
        public static readonly Color Amber = new Color32(0xE8, 0xA3, 0x3D, 0xFF);
        // the "erased from time" edge of ZombieSurface, brought down to a displayable colour
        public static readonly Color Cyan = new Color(0.4f, 0.91f, 1f, 1f);
        public static readonly Color Red = new Color(1f, 0.22f, 0.18f, 1f);
        public static readonly Color Gold = new Color32(0xFF, 0xC8, 0x3D, 0xFF);
        public static readonly Color Text = new Color(0.9f, 0.9f, 0.92f, 1f);
        public static readonly Color Dim = new Color(0.58f, 0.6f, 0.65f, 1f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);

        // The project renders in linear colour, where a dark panel lets far more of a bright background through than
        // its alpha suggests: 0.96 here looks like about 90% on screen.
        public const float PanelAlpha = 0.96f;
        public const float SlideDistance = 8f;

        // Optional title font (a serif, if the project gets one). Empty = the TextMeshPro default, bold.
        public static TMP_FontAsset TitleFont;
        // Set by GameMenus: the tick on hover and the click.
        public static Action Hover;
        public static Action Click;
        // True while a default selection is being made in code, which should not tick.
        public static bool Silent { get; private set; }

        public static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        static Sprite panel, triangle, vignette;

        public static Color Panel(float alpha = PanelAlpha)
        {
            Color c = Charcoal;
            c.a = alpha;
            return c;
        }

        public static string Hex(Color colour)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(colour);
        }

        public static Sprite PanelSprite
        {
            get
            {
                if (panel == null) panel = GameHUD.MakePanelSprite();
                return panel;
            }
        }

        // A white triangle pointing right: the hover marker and the selector arrows.
        public static Sprite TriangleSprite
        {
            get
            {
                if (triangle != null) return triangle;
                const int size = 32;
                var tex = NewTexture(size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        // inside when the row is no further from the middle than the column allows
                        float edge = (size - 1 - x) * 0.5f - Mathf.Abs(y - (size - 1) * 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(edge + 0.5f)));
                    }
                tex.SetPixels32(pixels);
                tex.Apply();
                triangle = Sprite.Create(tex, new Rect(0f, 0f, size, size), Centre, 100f);
                return triangle;
            }
        }

        // Clear in the middle, black at the corners.
        public static Sprite VignetteSprite
        {
            get
            {
                if (vignette != null) return vignette;
                const int size = 128;
                var tex = NewTexture(size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / 0.7071f;
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.05f, d));
                        pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(255f * a));
                    }
                tex.SetPixels32(pixels);
                tex.Apply();
                vignette = Sprite.Create(tex, new Rect(0f, 0f, size, size), Centre, 100f);
                return vignette;
            }
        }

        static Texture2D NewTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        // ------------------------------------------------------------------ canvas

        // A screen-space canvas scaled from 1920 x 1080, matching width and height equally (so 16:10 loses nothing).
        public static Canvas MakeCanvas(GameObject go, int sortingOrder, bool raycasts)
        {
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (raycasts) go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ------------------------------------------------------------------ parts

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = Centre;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            return Rect(name, parent, Centre, position, size);
        }

        // Covers its parent.
        public static RectTransform Fill(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static CanvasGroup Group(RectTransform rt)
        {
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            return group;
        }

        public static Image AddImage(RectTransform rt, Sprite sprite, Color colour, bool raycast = false)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            if (sprite != null && sprite.border.sqrMagnitude > 0f) image.type = Image.Type.Sliced;
            image.color = colour;
            image.raycastTarget = raycast;
            return image;
        }

        // A rounded charcoal panel.
        public static RectTransform MakePanel(string name, Transform parent, Vector2 position, Vector2 size, float alpha = PanelAlpha)
        {
            var rt = Rect(name, parent, position, size);
            AddImage(rt, PanelSprite, Panel(alpha), true);
            return rt;
        }

        public static TextMeshProUGUI MakeText(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize,
            Color colour, TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool bold = false)
        {
            var rt = Rect(name, parent, position, size);
            var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = colour;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            if (bold) label.fontStyle = FontStyles.Bold;
            label.text = text;
            return label;
        }

        // A screen or panel heading: amber, bold, in the title font when there is one.
        public static TextMeshProUGUI MakeHeading(string name, Transform parent, Vector2 position, string text, float fontSize)
        {
            var label = MakeText(name, parent, position, new Vector2(1600f, fontSize * 1.4f), text, fontSize, Amber, TextAlignmentOptions.Center, true);
            if (TitleFont != null) label.font = TitleFont;
            label.characterSpacing = 6f;
            return label;
        }

        // A text button. The label is centred (or left-aligned) in the given size; the marker sits just left of it.
        public static MenuEntry MakeButton(Transform parent, string text, Vector2 position, Vector2 size, UnityAction action,
            float fontSize = 38f, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var rt = Rect(text, parent, position, size);
            // an invisible graphic, so the whole rectangle takes the pointer
            AddImage(rt, null, Color.clear, true);
            var slide = Fill("Slide", rt);
            var label = MakeText("Text", slide, Vector2.zero, size, text, fontSize, Text, alignment, true);
            ((RectTransform)label.transform).anchorMin = Vector2.zero;
            ((RectTransform)label.transform).anchorMax = Vector2.one;
            ((RectTransform)label.transform).sizeDelta = Vector2.zero;
            label.characterSpacing = 4f;

            var button = rt.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(PlayClick);
            if (action != null) button.onClick.AddListener(action);

            var item = rt.gameObject.AddComponent<MenuEntry>();
            item.label = label;
            item.slide = slide;
            item.marker = MakeMarker(slide, fontSize);
            item.PlaceMarker();
            return item;
        }

        static Image MakeMarker(Transform parent, float fontSize)
        {
            var rt = Rect("Marker", parent, Vector2.zero, Vector2.one * fontSize * 0.42f);
            var image = AddImage(rt, TriangleSprite, Amber);
            image.enabled = false;
            return image;
        }

        static void PlayClick()
        {
            Click?.Invoke();
        }

        // "Label ........ [control]": the row's label on the left, which is what lights up when the control has focus.
        public static TextMeshProUGUI MakeRowLabel(Transform parent, string text, Vector2 position, float width, float fontSize = 30f)
        {
            var label = MakeText(text + "Label", parent, position, new Vector2(width, 48f), text, fontSize, Text, TextAlignmentOptions.Left);
            return label;
        }

        // A slider with its value written beside it. Format turns the value into that text.
        public static Slider MakeSlider(Transform parent, string name, Vector2 position, float width, float min, float max, float value,
            TextMeshProUGUI rowLabel, Func<float, string> format, UnityAction<float> onChanged)
        {
            var rt = Rect(name + "Slider", parent, position, new Vector2(width, 40f));
            AddImage(rt, null, Color.clear, true);

            var track = Rect("Track", rt, Vector2.zero, new Vector2(0f, 8f));
            track.anchorMin = new Vector2(0f, 0.5f);
            track.anchorMax = new Vector2(1f, 0.5f);
            track.sizeDelta = new Vector2(0f, 8f);
            AddImage(track, PanelSprite, Track).pixelsPerUnitMultiplier = 6f;

            var fillArea = Rect("Fill Area", rt, Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 8f);
            var fill = Rect("Fill", fillArea, Vector2.zero, Vector2.zero);
            fill.sizeDelta = Vector2.zero;
            AddImage(fill, PanelSprite, Amber).pixelsPerUnitMultiplier = 6f;

            var handleArea = Rect("Handle Slide Area", rt, Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(9f, 0f);
            handleArea.offsetMax = new Vector2(-9f, 0f);
            var handle = Rect("Handle", handleArea, Vector2.zero, new Vector2(18f, 0f));
            var handleImage = AddImage(handle, PanelSprite, Text);
            handleImage.pixelsPerUnitMultiplier = 3f;

            var slider = rt.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);

            var valueLabel = MakeText("Value", rt, new Vector2(width * 0.5f + 70f, 0f), new Vector2(120f, 40f), format(value), 28f, Dim, TextAlignmentOptions.Left);
            slider.onValueChanged.AddListener(v => valueLabel.text = format(v));
            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);

            var item = rt.gameObject.AddComponent<MenuEntry>();
            item.label = rowLabel;
            item.accent = handleImage;
            return slider;
        }

        // "< VALUE >": left and right (keys or a click on either half) step through the options.
        public static MenuSelector MakeSelector(Transform parent, string name, Vector2 position, float width, IList<string> options, int index,
            TextMeshProUGUI rowLabel, Action<int> onChanged, float fontSize = 30f)
        {
            var rt = Rect(name + "Selector", parent, position, new Vector2(width, 46f));
            AddImage(rt, null, Color.clear, true);
            var value = MakeText("Value", rt, Vector2.zero, new Vector2(width - 70f, 46f), "", fontSize, Text, TextAlignmentOptions.Center, true);
            var left = AddImage(Rect("Left", rt, new Vector2(-width * 0.5f + 16f, 0f), Vector2.one * fontSize * 0.5f), TriangleSprite, Dim);
            left.transform.localScale = new Vector3(-1f, 1f, 1f);
            var right = AddImage(Rect("Right", rt, new Vector2(width * 0.5f - 16f, 0f), Vector2.one * fontSize * 0.5f), TriangleSprite, Dim);

            var selector = rt.gameObject.AddComponent<MenuSelector>();
            selector.transition = Selectable.Transition.None;
            selector.valueLabel = value;
            selector.leftArrow = left;
            selector.rightArrow = right;
            selector.SetOptions(options, index);
            selector.onChanged = onChanged;

            var item = rt.gameObject.AddComponent<MenuEntry>();
            item.label = rowLabel != null ? rowLabel : value;
            item.accents = new Graphic[] { left, right };
            return selector;
        }

        // A key on a keyboard: light text on a small raised panel, as wide as its text.
        public static RectTransform MakeKeyCap(Transform parent, string text, Vector2 leftCentre, float fontSize = 24f)
        {
            float width = Mathf.Max(46f, text.Length * fontSize * 0.6f + 26f);
            var rt = Rect("Key " + text, parent, leftCentre + new Vector2(width * 0.5f, 0f), new Vector2(width, fontSize + 18f));
            AddImage(rt, PanelSprite, new Color(1f, 1f, 1f, 0.1f)).pixelsPerUnitMultiplier = 2.2f;
            var edge = Rect("Edge", rt, new Vector2(0f, -(fontSize + 18f) * 0.5f + 2f), new Vector2(width - 16f, 2f));
            AddImage(edge, null, new Color(1f, 1f, 1f, 0.28f));
            MakeText("Text", rt, new Vector2(0f, 1f), rt.sizeDelta, text, fontSize, Text, TextAlignmentOptions.Center, true);
            return rt;
        }

        // ------------------------------------------------------------------ selection

        // Gives a control the keyboard focus without the hover tick (a screen's default selection).
        public static void Select(Component control)
        {
            if (EventSystem.current == null) return;
            Silent = true;
            EventSystem.current.SetSelectedGameObject(control != null ? control.gameObject : null);
            Silent = false;
        }

        // Up/down neighbours for a column of controls, top to bottom. Left and right stay with the control itself.
        public static void LinkVertical(IList<Selectable> column, bool wrap = true)
        {
            for (int i = 0; i < column.Count; i++)
            {
                if (column[i] == null) continue;
                var nav = new Navigation { mode = Navigation.Mode.Explicit };
                int up = i - 1, down = i + 1;
                if (wrap)
                {
                    up = (up + column.Count) % column.Count;
                    down %= column.Count;
                }
                nav.selectOnUp = up >= 0 ? column[up] : null;
                nav.selectOnDown = down < column.Count ? column[down] : null;
                Navigation old = column[i].navigation;
                if (old.mode == Navigation.Mode.Explicit)
                {
                    nav.selectOnLeft = old.selectOnLeft;
                    nav.selectOnRight = old.selectOnRight;
                }
                column[i].navigation = nav;
            }
        }

        // Left/right neighbours for a row of controls. Up and down are left as they were.
        public static void LinkHorizontal(IList<Selectable> row)
        {
            for (int i = 0; i < row.Count; i++)
            {
                Navigation nav = row[i].navigation;
                if (nav.mode != Navigation.Mode.Explicit) nav = new Navigation { mode = Navigation.Mode.Explicit };
                nav.selectOnLeft = row[(i - 1 + row.Count) % row.Count];
                nav.selectOnRight = row[(i + 1) % row.Count];
                row[i].navigation = nav;
            }
        }
    }
}
