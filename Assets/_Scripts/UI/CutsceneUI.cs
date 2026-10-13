using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // Everything the intro cutscene draws on the screen. CutsceneDirector creates one and tells it what to show; like
    // the menus it is built in code (MenuKit), on its own canvas above them.
    //
    //   Layers       black (the fades), cyan static (the transition out of the void) and white (the portal flash).
    //   Typewriter   beat 1: monospaced lines typed onto the black screen, a key clack on every character.
    //   Dialogue     a box along the bottom, 80% of the screen wide: charcoal, a thin cyan border with a few pieces
    //                of it knocked out of place, the speaker's name in amber, white text typed out with a soft blip
    //                every two or three characters, and a pulsing [E] key-cap once the line is complete.
    //   Skip         "Hold SPACE to skip" at the bottom right, with a ring that fills while the key is held.
    //   Subtitle     the radio line after the arrival.
    // All of it runs on real (unscaled) time. Nothing here reads the keyboard: the director does.
    public class CutsceneUI : MonoBehaviour
    {
        // Sound slots, filled in by the director. Empty = GameAudio's UI click, pitched and turned down.
        [System.NonSerialized] public Sfx blip;
        [System.NonSerialized] public Sfx keyClack;
        [System.NonSerialized] public float blipFallbackVolume = 0.12f;
        [System.NonSerialized] public float blipFallbackPitch = 1.9f;
        [System.NonSerialized] public float clackFallbackVolume = 0.3f;
        [System.NonSerialized] public float clackFallbackPitch = 1.25f;

        public const float BoxWidth = 1536f, BoxHeight = 220f, BoxBottom = 96f;

        // For the director and for tests.
        public float Black => black.Value;
        public float White => white.Value;
        public float Noise => noise.Value;
        public bool Typing => typewriterTyping;
        public string TypewriterText => typewriterLabel != null ? typewriterLabel.text : "";
        public bool TypewriterVisible => typewriterRoot != null && typewriterRoot.gameObject.activeSelf;
        public bool DialogueVisible => dialogueRoot != null && dialogueRoot.gameObject.activeSelf;
        public bool LineTyping => DialogueVisible && lineTyping;
        public string ShownSpeaker => nameLabel != null ? nameLabel.text : "";
        public string ShownLine => bodyLabel != null ? bodyLabel.text : "";
        public int ShownCharacters => bodyLabel != null ? bodyLabel.maxVisibleCharacters : 0;
        public bool PromptVisible => promptGroup != null && promptGroup.gameObject.activeSelf;
        public bool SkipVisible => skipRoot != null && skipRoot.gameObject.activeSelf;
        public float SkipFill => skipFill != null ? skipFill.fillAmount : 0f;
        public string ShownSubtitle => subtitleRoot != null && subtitleRoot.gameObject.activeSelf ? subtitleLabel.text : "";
        // Seconds the last line took from its first character to its last (for tests).
        public float LastLineSeconds { get; private set; }
        public int Blips { get; private set; }
        public int Clacks { get; private set; }
        public RectTransform DialogueBox => box;
        public RectTransform SkipPrompt => skipRoot;

        // A full-screen layer whose opacity moves to a target.
        class Layer
        {
            public Graphic graphic;
            public float Value;
            float target;
            float rate;

            public void Set(float value)
            {
                Value = target = Mathf.Clamp01(value);
                Show();
            }

            public void Fade(float to, float seconds)
            {
                target = Mathf.Clamp01(to);
                if (seconds <= 0f) Set(to);
                else rate = 1f / seconds;
            }

            public void Tick(float dt)
            {
                if (Mathf.Approximately(Value, target)) return;
                Value = Mathf.MoveTowards(Value, target, rate * dt);
                Show();
            }

            void Show()
            {
                Color c = graphic.color;
                c.a = Value;
                graphic.color = c;
                graphic.gameObject.SetActive(Value > 0.001f);
            }
        }

        readonly Layer black = new Layer(), white = new Layer(), noise = new Layer();
        RawImage noiseImage;
        Texture2D noiseTexture;
        // the cyan static: up, held, down again
        float noisePeak, noiseIn, noiseHold, noiseOut, noiseAge = -1f;

        RectTransform typewriterRoot;
        CanvasGroup typewriterGroup;
        TextMeshProUGUI typewriterLabel;
        bool typewriterTyping;
        float typewriterSpeed = 22f;
        float typewriterShown;
        int typewriterTotal;
        float typewriterFade;

        RectTransform dialogueRoot, box;
        TextMeshProUGUI nameLabel, bodyLabel;
        CanvasGroup promptGroup;
        readonly RectTransform[] shards = new RectTransform[6];
        readonly float[] shardTimers = new float[6];
        bool lineTyping;
        float lineSpeed = 40f;
        float lineShown;
        int lineTotal;
        int nextBlip;
        bool lineBlips = true;
        bool lineDriven;
        float lineStarted;

        RectTransform skipRoot;
        CanvasGroup skipGroup;
        Image skipFill;

        RectTransform subtitleRoot;
        CanvasGroup subtitleGroup;
        TextMeshProUGUI subtitleLabel;
        float subtitleLeft;

        void Awake()
        {
            // the menus are 200: the fade to black has to cover them
            MenuKit.MakeCanvas(gameObject, 300, false);
            Transform canvas = transform;

            BuildTypewriterBackdrop(canvas);
            BuildDialogue(canvas);
            BuildSubtitle(canvas);

            noiseTexture = MakeNoiseTexture();
            var noiseRect = MenuKit.Fill("Noise", canvas);
            noiseImage = noiseRect.gameObject.AddComponent<RawImage>();
            noiseImage.texture = noiseTexture;
            noiseImage.raycastTarget = false;
            noiseImage.color = new Color(MenuKit.Cyan.r, MenuKit.Cyan.g, MenuKit.Cyan.b, 0f);
            noise.graphic = noiseImage;
            noise.Set(0f);

            white.graphic = MenuKit.AddImage(MenuKit.Fill("White", canvas), null, new Color(1f, 1f, 1f, 0f));
            white.Set(0f);

            BuildSkip(canvas);
        }

        void OnDestroy()
        {
            if (noiseTexture != null) Destroy(noiseTexture);
        }

        // ------------------------------------------------------------------ building

        // The black layer sits under the typewriter text and the dialogue box, over everything else in the game.
        void BuildTypewriterBackdrop(Transform canvas)
        {
            black.graphic = MenuKit.AddImage(MenuKit.Fill("Black", canvas), null, new Color(0f, 0f, 0f, 0f));
            black.Set(0f);

            typewriterRoot = MenuKit.Fill("Typewriter", canvas);
            typewriterGroup = MenuKit.Group(typewriterRoot);
            typewriterLabel = MenuKit.MakeText("Text", typewriterRoot, new Vector2(0f, 30f), new Vector2(1180f, 320f), "", 36f,
                new Color(0.78f, 0.95f, 0.9f, 1f), TextAlignmentOptions.TopLeft);
            typewriterLabel.lineSpacing = 34f;
            typewriterRoot.gameObject.SetActive(false);
        }

        void BuildDialogue(Transform canvas)
        {
            dialogueRoot = MenuKit.Fill("Dialogue", canvas);

            box = MenuKit.Rect("Box", dialogueRoot, new Vector2(0.5f, 0f), new Vector2(0f, BoxBottom + BoxHeight * 0.5f), new Vector2(BoxWidth, BoxHeight));
            // 92% on screen: the project renders in linear colour, where a dark panel needs a higher alpha to look it
            MenuKit.AddImage(box, null, MenuKit.Panel(0.975f));

            // the border: four thin cyan lines
            Color cyan = MenuKit.Cyan;
            Edge("Top", new Vector2(0f, BoxHeight * 0.5f), new Vector2(BoxWidth, 2f), cyan);
            Edge("Bottom", new Vector2(0f, -BoxHeight * 0.5f), new Vector2(BoxWidth, 2f), cyan);
            Edge("Left", new Vector2(-BoxWidth * 0.5f, 0f), new Vector2(2f, BoxHeight), cyan);
            Edge("Right", new Vector2(BoxWidth * 0.5f, 0f), new Vector2(2f, BoxHeight), cyan);
            // corner ticks
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    Edge("Tick", new Vector2(sx * (BoxWidth * 0.5f - 14f), sy * (BoxHeight * 0.5f + 7f)), new Vector2(28f, 3f), cyan);
                    Edge("Tick", new Vector2(sx * (BoxWidth * 0.5f + 7f), sy * (BoxHeight * 0.5f - 14f)), new Vector2(3f, 28f), cyan);
                }
            // the glitch: short pieces of the top and bottom lines, knocked out of place, that jump now and then
            for (int i = 0; i < shards.Length; i++)
            {
                shards[i] = Edge("Shard" + i, Vector2.zero, new Vector2(80f, 3f), new Color(cyan.r, cyan.g, cyan.b, 0.85f));
                PlaceShard(i);
            }

            // the name tag sits on the top line, at the left
            var tag = MenuKit.Rect("NameTag", box, new Vector2(-BoxWidth * 0.5f + 230f, BoxHeight * 0.5f), new Vector2(400f, 40f));
            MenuKit.AddImage(tag, null, MenuKit.Panel(1f));
            nameLabel = MenuKit.MakeText("Name", tag, Vector2.zero, new Vector2(400f, 40f), "", 23f, MenuKit.Amber, TextAlignmentOptions.Center, true);
            nameLabel.characterSpacing = 6f;

            bodyLabel = MenuKit.MakeText("Body", box, new Vector2(0f, -10f), new Vector2(BoxWidth - 96f, BoxHeight - 76f), "", 32f, Color.white, TextAlignmentOptions.TopLeft);
            bodyLabel.textWrappingMode = TextWrappingModes.Normal;
            bodyLabel.lineSpacing = 8f;

            // [E] at the bottom right, once the line is complete
            var prompt = MenuKit.Rect("Prompt", box, new Vector2(BoxWidth * 0.5f - 62f, -BoxHeight * 0.5f + 36f), new Vector2(60f, 44f));
            promptGroup = MenuKit.Group(prompt);
            var cap = MenuKit.MakeKeyCap(prompt, "E", Vector2.zero, 24f);
            cap.anchoredPosition = Vector2.zero;
            prompt.gameObject.SetActive(false);

            dialogueRoot.gameObject.SetActive(false);
        }

        RectTransform Edge(string name, Vector2 position, Vector2 size, Color colour)
        {
            var rt = MenuKit.Rect(name, box, position, size);
            MenuKit.AddImage(rt, null, colour);
            return rt;
        }

        // A new place, length and offset for one of the border's loose pieces.
        void PlaceShard(int i)
        {
            bool top = i % 2 == 0;
            float length = Random.Range(36f, 170f);
            float x = Random.Range(-BoxWidth * 0.5f + length, BoxWidth * 0.5f - length);
            float off = Random.Range(3f, 8f) * (Random.value < 0.5f ? -1f : 1f);
            shards[i].sizeDelta = new Vector2(length, Random.Range(2f, 4f));
            shards[i].anchoredPosition = new Vector2(x + Random.Range(-10f, 10f), (top ? 1f : -1f) * BoxHeight * 0.5f + off);
            // mostly still, with the odd quick burst
            shardTimers[i] = Random.value < 0.3f ? Random.Range(0.04f, 0.12f) : Random.Range(0.6f, 2.6f);
        }

        void BuildSkip(Transform canvas)
        {
            skipRoot = MenuKit.Rect("Skip", canvas, new Vector2(1f, 0f), new Vector2(-36f, 44f), new Vector2(10f, 10f));
            skipGroup = MenuKit.Group(skipRoot);

            Sprite ring = MakeRingSprite();
            var back = MenuKit.AddImage(MenuKit.Rect("Ring", skipRoot, new Vector2(-22f, 0f), new Vector2(44f, 44f)), ring, new Color(1f, 1f, 1f, 0.22f));
            back.type = Image.Type.Simple;
            skipFill = MenuKit.AddImage(MenuKit.Rect("Fill", skipRoot, new Vector2(-22f, 0f), new Vector2(44f, 44f)), ring, MenuKit.Cyan);
            skipFill.type = Image.Type.Filled;
            skipFill.fillMethod = Image.FillMethod.Radial360;
            skipFill.fillOrigin = (int)Image.Origin360.Top;
            skipFill.fillClockwise = true;
            skipFill.fillAmount = 0f;

            // right to left: ring, "to skip", [SPACE], "Hold". The two words are aligned toward the key-cap, so the
            // gaps on either side of it are the same whatever the font makes of their width.
            MenuKit.MakeText("ToSkip", skipRoot, new Vector2(-72f, 0f), new Vector2(100f, 36f), "to skip", 22f, MenuKit.Text, TextAlignmentOptions.Left);
            var cap = MenuKit.MakeKeyCap(skipRoot, "SPACE", new Vector2(-220f, 0f), 20f);
            MenuKit.MakeText("Hold", skipRoot, new Vector2(cap.anchoredPosition.x - cap.sizeDelta.x * 0.5f - 12f - 35f, 0f), new Vector2(70f, 36f), "Hold", 22f, MenuKit.Text, TextAlignmentOptions.Right);
            skipRoot.gameObject.SetActive(false);
        }

        void BuildSubtitle(Transform canvas)
        {
            subtitleRoot = MenuKit.Rect("Subtitle", canvas, new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(1100f, 64f));
            subtitleGroup = MenuKit.Group(subtitleRoot);
            MenuKit.AddImage(subtitleRoot, MenuKit.PanelSprite, MenuKit.Panel(0.9f));
            subtitleLabel = MenuKit.MakeText("Text", subtitleRoot, Vector2.zero, new Vector2(1060f, 60f), "", 28f, Color.white);
            subtitleRoot.gameObject.SetActive(false);
        }

        // A white ring on clear: the skip timer.
        static Sprite MakeRingSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            float centre = size * 0.5f, outer = size * 0.5f - 1f, inner = outer - 7f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - centre, dy = y + 0.5f - centre;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), MenuKit.Centre, 100f);
        }

        // Static: random bright and dark blocks. Shown at a different offset every frame.
        static Texture2D MakeNoiseTexture()
        {
            const int width = 192, height = 108;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // some rows are brighter: torn scanlines
                float row = Random.value < 0.12f ? 1f : Random.Range(0.25f, 0.7f);
                for (int x = 0; x < width; x++)
                {
                    float v = Random.value * row;
                    byte b = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(v * 1.5f));
                    pixels[y * width + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ layers

        public void SetBlack(float alpha) { black.Set(alpha); }
        public void FadeBlack(float to, float seconds) { black.Fade(to, seconds); }
        public void SetWhite(float alpha) { white.Set(alpha); }
        public void FadeWhite(float to, float seconds) { white.Fade(to, seconds); }

        // The cyan static: up to Peak over In seconds, held, and down again.
        public void NoiseBurst(float peak, float inSeconds, float holdSeconds, float outSeconds)
        {
            noisePeak = Mathf.Clamp01(peak);
            noiseIn = Mathf.Max(0.01f, inSeconds);
            noiseHold = Mathf.Max(0f, holdSeconds);
            noiseOut = Mathf.Max(0.01f, outSeconds);
            noiseAge = 0f;
        }

        // ------------------------------------------------------------------ typewriter

        public void BeginTypewriter(float charactersPerSecond)
        {
            typewriterSpeed = Mathf.Max(1f, charactersPerSecond);
            typewriterLabel.text = "";
            typewriterLabel.maxVisibleCharacters = 0;
            typewriterShown = 0f;
            typewriterTotal = 0;
            typewriterTyping = false;
            typewriterFade = 0f;
            typewriterGroup.alpha = 1f;
            typewriterRoot.gameObject.SetActive(true);
        }

        // Adds a line under the ones already there and types it out.
        public void TypeLine(string line)
        {
            if (!typewriterRoot.gameObject.activeSelf) BeginTypewriter(typewriterSpeed);
            typewriterShown = typewriterTotal;
            string text = typewriterLabel.text;
            typewriterLabel.text = (text.Length > 0 ? text + "\n" : "") + "<mspace=0.6em>" + line + "</mspace>";
            typewriterLabel.ForceMeshUpdate();
            typewriterTotal = typewriterLabel.textInfo.characterCount;
            typewriterTyping = true;
        }

        // The whole block fades out over a number of seconds.
        public void FadeTypewriter(float seconds)
        {
            if (seconds <= 0f) ClearTypewriter();
            else typewriterFade = 1f / seconds;
        }

        public void ClearTypewriter()
        {
            typewriterTyping = false;
            typewriterFade = 0f;
            typewriterRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ dialogue

        // Shows the box with a new line and starts typing it, at a number of characters per second with a blip every
        // few characters. Over Seconds (a voiced line): the whole line is spread over that long instead, the length of
        // the voice clip, and there are no blips.
        public void ShowLine(string speaker, string text, float charactersPerSecond, float overSeconds = 0f)
        {
            nameLabel.text = speaker;
            bodyLabel.text = text;
            bodyLabel.maxVisibleCharacters = 0;
            dialogueRoot.gameObject.SetActive(true);
            bodyLabel.ForceMeshUpdate();
            lineTotal = bodyLabel.textInfo.characterCount;
            lineBlips = overSeconds <= 0f;
            lineSpeed = lineBlips ? Mathf.Max(1f, charactersPerSecond) : Mathf.Max(1f, lineTotal / overSeconds);
            lineStarted = Time.unscaledTime;
            lineDriven = false;
            LastLineSeconds = 0f;
            lineShown = 0f;
            lineTyping = lineTotal > 0;
            nextBlip = 1;
            promptGroup.gameObject.SetActive(!lineTyping);
        }

        // A voiced line: how far through the spoken part of the clip the voice is (0-1). The director calls this every
        // frame while the clip plays, and the text follows it exactly, so a slow frame cannot leave the text behind
        // the voice. In a frame without a call the text carries on at its own speed.
        public void SetLineProgress(float fraction)
        {
            if (!lineTyping) return;
            lineShown = Mathf.Max(lineShown, lineTotal * Mathf.Clamp01(fraction));
            lineDriven = true;
        }

        // The rest of the line appears at once.
        public void CompleteLine()
        {
            if (!lineTyping) return;
            lineTyping = false;
            LastLineSeconds = Time.unscaledTime - lineStarted;
            lineShown = lineTotal;
            bodyLabel.maxVisibleCharacters = lineTotal;
            promptGroup.gameObject.SetActive(true);
        }

        public void HideDialogue()
        {
            lineTyping = false;
            dialogueRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ skip prompt, subtitle

        // Fill: 0-1, how long the key has been held.
        public void SetSkip(bool visible, float fill)
        {
            if (skipRoot.gameObject.activeSelf != visible) skipRoot.gameObject.SetActive(visible);
            if (!visible) return;
            skipFill.fillAmount = Mathf.Clamp01(fill);
            // quiet until the key is touched
            skipGroup.alpha = fill > 0f ? 1f : 0.6f;
        }

        public void Subtitle(string speaker, string text, float seconds)
        {
            subtitleLabel.text = (string.IsNullOrEmpty(speaker) ? "" : "<color=" + MenuKit.Hex(MenuKit.Amber) + "><b>" + speaker + "</b></color>   ") + text;
            subtitleLeft = Mathf.Max(0.5f, seconds);
            subtitleGroup.alpha = 0f;
            subtitleRoot.gameObject.SetActive(true);
        }

        // Everything off except the subtitle (the director uses this when the cutscene ends or is skipped).
        public void HideAll()
        {
            ClearTypewriter();
            HideDialogue();
            SetSkip(false, 0f);
            noiseAge = -1f;
            noise.Set(0f);
            black.Set(0f);
            white.Set(0f);
        }

        // ------------------------------------------------------------------ every frame (real time)

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            black.Tick(dt);
            white.Tick(dt);
            UpdateNoise(dt);
            UpdateTypewriter(dt);
            UpdateDialogue(dt);
            UpdateSubtitle(dt);
        }

        void UpdateNoise(float dt)
        {
            if (noiseAge < 0f) return;
            noiseAge += dt;
            float level;
            if (noiseAge < noiseIn) level = noiseAge / noiseIn;
            else if (noiseAge < noiseIn + noiseHold) level = 1f;
            else level = 1f - (noiseAge - noiseIn - noiseHold) / noiseOut;
            if (level <= 0f)
            {
                noiseAge = -1f;
                noise.Set(0f);
                return;
            }
            noise.Set(level * noisePeak);
            // a new piece of the static every frame, in blocks of different sizes
            float zoom = Random.Range(0.35f, 1f);
            noiseImage.uvRect = new Rect(Random.value, Random.value, zoom, zoom * Random.Range(0.4f, 1f));
        }

        void UpdateTypewriter(float dt)
        {
            if (!typewriterRoot.gameObject.activeSelf) return;

            if (typewriterTyping)
            {
                int before = Mathf.FloorToInt(typewriterShown);
                typewriterShown = Mathf.Min(typewriterTotal, typewriterShown + typewriterSpeed * dt);
                int now = Mathf.FloorToInt(typewriterShown);
                typewriterLabel.maxVisibleCharacters = now;
                // a clack for each character that is not a space
                for (int i = before; i < now; i++)
                    if (!IsSpace(typewriterLabel, i)) Clack();
                if (now >= typewriterTotal) typewriterTyping = false;
            }

            if (typewriterFade > 0f)
            {
                typewriterGroup.alpha = Mathf.MoveTowards(typewriterGroup.alpha, 0f, typewriterFade * dt);
                if (typewriterGroup.alpha <= 0f) ClearTypewriter();
            }
        }

        void UpdateDialogue(float dt)
        {
            if (!dialogueRoot.gameObject.activeSelf) return;

            if (lineTyping)
            {
                int before = bodyLabel.maxVisibleCharacters;
                // a voiced line is placed by the voice (SetLineProgress); anything else types at its own speed
                if (!lineDriven) lineShown = Mathf.Min(lineTotal, lineShown + lineSpeed * dt);
                lineDriven = false;
                // a hair under a whole number still counts as that character (the last one must not wait a frame)
                int now = Mathf.Min(lineTotal, Mathf.FloorToInt(lineShown + 0.001f));
                bodyLabel.maxVisibleCharacters = now;
                for (int i = before; i < now && lineBlips; i++)
                {
                    if (IsSpace(bodyLabel, i)) continue;
                    if (--nextBlip > 0) continue;
                    nextBlip = Random.Range(2, 4); // every two or three characters
                    Blip();
                }
                if (now >= lineTotal)
                {
                    lineTyping = false;
                    LastLineSeconds = Time.unscaledTime - lineStarted;
                    promptGroup.gameObject.SetActive(true);
                }
            }

            if (promptGroup.gameObject.activeSelf)
                promptGroup.alpha = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));

            for (int i = 0; i < shards.Length; i++)
            {
                shardTimers[i] -= dt;
                if (shardTimers[i] <= 0f) PlaceShard(i);
            }
        }

        void UpdateSubtitle(float dt)
        {
            if (!subtitleRoot.gameObject.activeSelf) return;
            subtitleLeft -= dt;
            // in over a quarter of a second, out over the last half
            float target = subtitleLeft > 0.5f ? 1f : Mathf.Clamp01(subtitleLeft / 0.5f);
            subtitleGroup.alpha = subtitleLeft > 0.5f ? Mathf.MoveTowards(subtitleGroup.alpha, target, dt * 4f) : target;
            if (subtitleLeft <= 0f) subtitleRoot.gameObject.SetActive(false);
        }

        static bool IsSpace(TextMeshProUGUI label, int index)
        {
            TMP_TextInfo info = label.textInfo;
            return index < 0 || index >= info.characterCount || char.IsWhiteSpace(info.characterInfo[index].character);
        }

        // ------------------------------------------------------------------ sounds

        void Blip()
        {
            Blips++;
            TypeSound("Cutscene/Blip", blip, blipFallbackVolume, blipFallbackPitch);
        }

        void Clack()
        {
            Clacks++;
            TypeSound("Cutscene/KeyClack", keyClack, clackFallbackVolume, clackFallbackPitch);
        }

        // The slot's own sound, or the UI click standing in for it: quieter, and at another pitch.
        static void TypeSound(string role, Sfx own, float fallbackVolume, float fallbackPitch)
        {
            var audio = GameAudio.Instance;
            if (audio == null) return;
            if (own != null && own.HasClip) audio.Play2D(role, own, audio.uiGroup, true);
            else audio.Play2D(role, audio.uiClick, audio.uiGroup, true, fallbackVolume, fallbackPitch);
        }
    }
}
