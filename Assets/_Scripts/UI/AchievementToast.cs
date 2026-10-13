using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // The pop-up for an unlocked achievement, at the bottom centre of the screen between the stamina bar and the
    // message line. Created by the AchievementTracker.
    //   1. A dark disc with the tinted icon pops in (scale 0 -> 1.1 -> 1).
    //   2. It opens sideways into a pill: "Achievement unlocked" over "80 TP - Against the Clock".
    //   3. It holds, closes back to the disc and fades.
    // Several unlocks queue and play one after another. Nothing is shown while the game is paused (the clock of the
    // one on screen stops with it) or once the run has ended: the end screen lists those instead.
    public class AchievementToast : MonoBehaviour
    {
        public float popTime = 0.25f;
        public float expandTime = 0.35f;
        public float holdTime = 4f;
        public float collapseTime = 0.3f;
        public float fadeTime = 0.25f;
        [Tooltip("Height of the disc and the pill (reference pixels).")]
        public float size = 96f;
        [Tooltip("Centre of the pop-up above the bottom edge: clear of the stamina bar (up to 71) and the message line (from 195).")]
        public float bottomOffset = 140f;

        // For tests.
        public int Queued => queue.Count;
        public int Shown { get; private set; }
        public bool Showing => current != null;
        public string CurrentTitle => current != null ? titleLabel.text : "";
        public float Width => pill != null ? pill.sizeDelta.x : 0f;
        public float Alpha => group != null ? group.alpha : 0f;

        AchievementTracker owner;
        readonly Queue<AchievementDef> queue = new Queue<AchievementDef>();
        AchievementDef current;
        float age;
        float fullWidth;

        CanvasGroup group;
        RectTransform pill;
        Image iconImage;
        TextMeshProUGUI captionLabel, titleLabel;

        public void Build(AchievementTracker tracker)
        {
            owner = tracker;
            // above the menus too, so an unlock made from the editor shows on the main menu
            var canvas = MenuKit.MakeCanvas(gameObject, 210, false);
            // by height, like GameHUD: the gaps to the stamina bar and the message line hold at any aspect
            GetComponent<CanvasScaler>().matchWidthOrHeight = 1f;

            pill = MenuKit.Rect("Pill", canvas.transform, new Vector2(0.5f, 0f), new Vector2(0f, bottomOffset), new Vector2(size, size));
            group = MenuKit.Group(pill);
            group.interactable = false;
            group.blocksRaycasts = false;
            var back = MenuKit.AddImage(pill, MenuKit.PillSprite, new Color(0.055f, 0.058f, 0.07f, 0.98f));
            back.type = Image.Type.Sliced;
            // text is cut off at the pill's edge while it opens
            pill.gameObject.AddComponent<RectMask2D>();

            var ring = MenuKit.Rect("Disc", pill, new Vector2(0f, 0.5f), new Vector2(size * 0.5f, 0f), new Vector2(size - 10f, size - 10f));
            MenuKit.AddImage(ring, MenuKit.PillSprite, new Color(1f, 1f, 1f, 0.07f));
            var icon = MenuKit.Rect("Icon", ring, Vector2.zero, new Vector2(size * 0.62f, size * 0.62f));
            iconImage = MenuKit.AddImage(icon, Achievements.Icon, MenuKit.Amber);
            iconImage.preserveAspect = true;
            iconImage.enabled = Achievements.Icon != null;

            captionLabel = MenuKit.MakeText("Caption", pill, Vector2.zero, new Vector2(900f, 30f), "Achievement unlocked", 22f, MenuKit.Dim, TextAlignmentOptions.Left);
            Anchor(captionLabel, size + 6f, 17f);
            titleLabel = MenuKit.MakeText("Title", pill, Vector2.zero, new Vector2(900f, 40f), "", 30f, MenuKit.Text, TextAlignmentOptions.Left, true);
            Anchor(titleLabel, size + 6f, -15f);

            group.alpha = 0f;
            pill.gameObject.SetActive(false);
        }

        static void Anchor(TextMeshProUGUI label, float x, float y)
        {
            var rt = (RectTransform)label.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        void OnEnable()
        {
            Achievements.OnUnlocked += Enqueue;
        }

        void OnDisable()
        {
            Achievements.OnUnlocked -= Enqueue;
        }

        void Enqueue(AchievementDef def)
        {
            // on the win or loss screen the unlock is in that screen's list
            if (GameManager.IsGameOver) return;
            queue.Enqueue(def);
        }

        void Update()
        {
            var manager = GameManager.Instance;
            bool paused = manager != null && manager.State == GameState.Paused;
            if (GameManager.IsGameOver)
            {
                queue.Clear();
                if (current != null) Hide();
                return;
            }
            if (paused)
            {
                // out of sight under the pause menu, and its clock waits
                if (current != null) group.alpha = 0f;
                return;
            }

            if (current == null)
            {
                if (queue.Count == 0) return;
                Begin(queue.Dequeue());
            }

            age += Time.unscaledDeltaTime;
            Apply();
        }

        void Begin(AchievementDef def)
        {
            current = def;
            age = 0f;
            Shown++;
            iconImage.sprite = Achievements.Icon;
            iconImage.enabled = Achievements.Icon != null;
            iconImage.color = Achievements.Tint(def, true);
            titleLabel.text = def.points + " TP – " + def.name;
            float text = Mathf.Max(titleLabel.GetPreferredValues(titleLabel.text).x, captionLabel.GetPreferredValues(captionLabel.text).x);
            fullWidth = size + 6f + text + 44f;
            pill.gameObject.SetActive(true);
            Apply();

            var audio = GameAudio.Instance;
            if (audio == null) return;
            Sfx sound = owner != null && owner.chime != null && owner.chime.HasClip ? owner.chime : audio.objectiveComplete;
            audio.Play2D("UI/Achievement", sound, audio.uiGroup, true);
        }

        // Where the pop-up is at this age: scale, width and opacity.
        void Apply()
        {
            float t = age;
            float scale = 1f, open = 0f, alpha = 1f;

            if (t < popTime)
            {
                // up to 1.1 in the first 70%, back to 1 in the rest
                float k = t / popTime;
                scale = k < 0.7f ? Mathf.SmoothStep(0f, 1.1f, k / 0.7f) : Mathf.Lerp(1.1f, 1f, (k - 0.7f) / 0.3f);
            }
            else if ((t -= popTime) < expandTime) open = Mathf.SmoothStep(0f, 1f, t / expandTime);
            else if ((t -= expandTime) < holdTime) open = 1f;
            else if ((t -= holdTime) < collapseTime) open = 1f - Mathf.SmoothStep(0f, 1f, t / collapseTime);
            else if ((t -= collapseTime) < fadeTime) alpha = 1f - t / fadeTime;
            else
            {
                Hide();
                return;
            }

            pill.localScale = Vector3.one * scale;
            pill.sizeDelta = new Vector2(Mathf.Lerp(size, fullWidth, open), size);
            group.alpha = alpha;
            captionLabel.alpha = open;
            titleLabel.alpha = open;
        }

        void Hide()
        {
            current = null;
            group.alpha = 0f;
            pill.gameObject.SetActive(false);
        }
    }
}
