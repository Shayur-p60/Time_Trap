using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // Every menu screen: the main menu (over the fly-through), the pause menu, and the Settings, How to Play, Credits and
    // Achievements panels that open from either. One per scene, on Map/Gameplay/GameMenus; it builds its own canvas
    // (MenuKit) above the HUD and the game manager's screens.
    //
    //   Main menu   shown while GameManager.State is Menu. Play: whoosh, the MenuCameraRig glides into the player's
    //               camera, the menu fades out as the HUD fades in, then GameManager.StartGame.
    //   Pause       GameManager.Pause / Resume show and hide it. Esc (P as well in the editor) goes back one level:
    //               out of a confirmation, out of a sub-panel, then out of the pause menu.
    //   Settings    reads and writes GameSettings; the same panel from both menus.
    // All of it runs on unscaled time, since the game is frozen under the pause menu.
    [DefaultExecutionOrder(-170)]
    public class GameMenus : MonoBehaviour
    {
        public enum Panel { None, Main, Pause, Settings, HowToPlay, Credits, Achievements }

        public static GameMenus Instance { get; private set; }

        [Header("Sounds (UI mixer group)")]
        [Tooltip("The soft tick when a button is hovered or takes the keyboard focus.")]
        public Sfx hoverTick = new Sfx();
        [Tooltip("Empty = GameAudio's UI click.")]
        public Sfx click = new Sfx();
        [Tooltip("Play was pressed: the portal whoosh.")]
        public Sfx playWhoosh = new Sfx();
        [Tooltip("Played on the SFX group when the SFX slider is let go.")]
        public Sfx sfxSample = new Sfx();

        [Header("Content")]
        [Tooltip("Assets/_Audio/CREDITS.md.")]
        public TextAsset credits;
        [Tooltip("The symbol of the brightness test. Empty = a plain disc.")]
        public Sprite brightnessSymbol;
        [Tooltip("Optional serif for the titles. Empty = the TextMeshPro default, bold.")]
        public TMP_FontAsset titleFont;
        public string studio = "Dead Reckoning Games";
        public string version = "v1.0";

        [Header("Scene")]
        public MenuCameraRig cameraRig;

        [Header("Timing (seconds)")]
        public float menuFadeTime = 0.4f;
        public float pauseFadeIn = 0.12f;
        public float pauseFadeOut = 0.3f;
        public float titleGlitchTime = 0.5f;
        public float creditsSpeed = 70f;

        public Panel Current { get; private set; } = Panel.None;
        public bool ConfirmOpen => confirmRoot != null && confirmRoot.gameObject.activeSelf;
        // True from the Play click until the game has started.
        public bool Starting { get; private set; }

        // For tests.
        public Button PlayButton => playItem.Button;
        public MenuSelector DifficultySelector => difficultySelector;
        public string Tagline => tagline != null ? tagline.text : "";
        public string AchievementCounter => achievementCounter != null ? achievementCounter.text : "";
        public Button ResumeButton => resumeItem.Button;
        public Button RestartButton => restartItem.Button;
        public Button MainMenuButton => mainMenuEntry.Button;
        public Button PauseQuitButton => pauseQuitItem.Button;
        public Button ConfirmYesButton => confirmYes.Button;
        public Button ConfirmNoButton => confirmNo.Button;
        public string PauseStripText => stripTime.text + " | " + stripDifficulty.text + " | " + stripObjectives.text;
        public bool PauseDimVisible => pauseDim != null && pauseDim.gameObject.activeSelf;
        public float MainAlpha => mainGroup != null && mainRoot.gameObject.activeSelf ? mainGroup.alpha : 0f;

        GameManager manager;
        Transform canvas;

        // main
        RectTransform mainRoot;
        CanvasGroup mainGroup;
        TextMeshProUGUI title, titleRed, titleCyan, tagline, achievementCounter;
        MenuEntry playItem, quitItem;
        MenuSelector difficultySelector;

        // pause
        RectTransform pauseDim, pauseRoot;
        CanvasGroup pauseDimGroup, pauseGroup;
        MenuEntry resumeItem, restartItem, mainMenuEntry, pauseQuitItem;
        TextMeshProUGUI stripTime, stripDifficulty, stripObjectives;
        readonly List<Image> stripHearts = new List<Image>();
        Coroutine pauseFade;

        // settings
        RectTransform settingsRoot;
        CanvasGroup settingsGroup;
        readonly RectTransform[] tabRoots = new RectTransform[3];
        readonly MenuEntry[] tabItems = new MenuEntry[3];
        readonly List<Selectable>[] tabControls = { new List<Selectable>(), new List<Selectable>(), new List<Selectable>() };
        RectTransform tabUnderline;
        int tab;
        MenuEntry settingsBack, settingsDefaults, settingsProgress;
        Slider masterSlider, musicSlider, sfxSlider, zombiesSlider, sensitivitySlider, brightnessSlider, fovSlider;
        MenuSelector invertSelector, fullscreenSelector, resolutionSelector, headBobSelector;
        readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        Graphic brightnessMark;

        // the rest
        RectTransform howToRoot, creditsRoot, achievementsRoot, confirmRoot;
        TextMeshProUGUI howToText, achievementsHeader, confirmText;
        ScrollRect achievementsScroll;
        RectTransform achievementsContent;
        Image[] cardIcons;
        TextMeshProUGUI[] cardNames, cardDescriptions, cardPoints;
        MenuSelector goldenSelector;
        TextMeshProUGUI goldenLabel;
        MenuEntry howToBack, creditsBack, achievementsBack, confirmYes, confirmNo;
        RectTransform creditsContent;
        float creditsViewHeight, creditsHeight;
        Action confirmAction;
        GameObject confirmReturn;
        CanvasGroup confirmUnder;

        Panel returnTo = Panel.None;
        Component defaultSelection;

        static readonly string[] OffOn = { "OFF", "ON" };

        // The controls, as shown in How to Play and Settings.
        static readonly KeyValuePair<string, string[]>[] Controls =
        {
            new KeyValuePair<string, string[]>("Move", new[] { "W", "A", "S", "D", "Arrows" }),
            new KeyValuePair<string, string[]>("Look", new[] { "Mouse" }),
            new KeyValuePair<string, string[]>("Sprint", new[] { "Left Shift" }),
            new KeyValuePair<string, string[]>("Crouch", new[] { "Left Ctrl" }),
            new KeyValuePair<string, string[]>("Interact / hold to download", new[] { "E" }),
            new KeyValuePair<string, string[]>("Torch on / off", new[] { "F" }),
            new KeyValuePair<string, string[]>("Switch torch / pistol", new[] { "Q" }),
            new KeyValuePair<string, string[]>("Fire", new[] { "Left mouse" }),
            new KeyValuePair<string, string[]>("Reload", new[] { "R" }),
            new KeyValuePair<string, string[]>("Pause", new[] { "Esc" }),
        };

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;

            MenuKit.TitleFont = titleFont;
            MenuKit.Hover = PlayHover;
            MenuKit.Click = PlayClick;

            var go = new GameObject("MenuCanvas");
            go.transform.SetParent(transform, false);
            MenuKit.MakeCanvas(go, 200, true); // GameHUD is 100, GameManagerUI 150
            canvas = go.transform;

            BuildMain();
            BuildPause();
            BuildSettings();
            BuildHowToPlay();
            BuildCredits();
            BuildAchievements();
            BuildConfirm();

            foreach (var root in new[] { mainRoot, pauseDim, pauseRoot, settingsRoot, howToRoot, creditsRoot, achievementsRoot, confirmRoot })
                root.gameObject.SetActive(false);
        }

        void Start()
        {
            manager = GameManager.Instance;
            if (cameraRig == null) cameraRig = FindAnyObjectByType<MenuCameraRig>();
            if (manager != null && manager.State == GameState.Menu) ShowMain(true);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            MenuKit.Hover = null;
            MenuKit.Click = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewPlaySession()
        {
            Instance = null;
            MenuKit.Hover = null;
            MenuKit.Click = null;
        }

        void Update()
        {
            if (Current == Panel.None) return;

            // GameManager opened the pause menu with this same key press: not a "back" as well
            if (GameManager.PausePressed && (manager == null || Time.frameCount != manager.PauseFrame)) Back();

            if (Current == Panel.Credits) ScrollCredits(Time.unscaledDeltaTime);
            if (Current == Panel.Achievements) ScrollAchievements(Time.unscaledDeltaTime);

            // a click on empty space drops the selection: put it back, so the keyboard always has somewhere to start
            var events = EventSystem.current;
            if (events != null && !Starting && defaultSelection != null && defaultSelection.gameObject.activeInHierarchy)
            {
                GameObject selected = events.currentSelectedGameObject;
                if (selected == null || !selected.activeInHierarchy) MenuKit.Select(defaultSelection);
            }
        }

        // ------------------------------------------------------------------ sounds

        void Sound(string role, Sfx sfx, bool onSfxGroup = false)
        {
            var audio = GameAudio.Instance;
            if (audio == null || sfx == null) return;
            audio.Play2D(role, sfx, onSfxGroup ? audio.sfxGroup : audio.uiGroup, true);
        }

        void PlayHover()
        {
            Sound("UI/Hover", hoverTick);
        }

        void PlayClick()
        {
            if (click != null && click.HasClip) Sound("UI/Click", click);
            else GameAudio.UiClick();
        }

        // ------------------------------------------------------------------ which panel is showing

        void Show(Panel panel, Component select)
        {
            CloseConfirm(false);
            Current = panel;
            mainRoot.gameObject.SetActive(panel == Panel.Main);
            pauseRoot.gameObject.SetActive(panel == Panel.Pause);
            settingsRoot.gameObject.SetActive(panel == Panel.Settings);
            howToRoot.gameObject.SetActive(panel == Panel.HowToPlay);
            creditsRoot.gameObject.SetActive(panel == Panel.Credits);
            achievementsRoot.gameObject.SetActive(panel == Panel.Achievements);
            defaultSelection = select;
            MenuKit.Select(select);
        }

        // The main menu. firstTime: the scene has just loaded, so the title glitches in.
        public void ShowMain(bool firstTime)
        {
            returnTo = Panel.Main;
            Starting = false;
            mainGroup.alpha = 1f;
            mainGroup.interactable = true;
            mainGroup.blocksRaycasts = true;
            difficultySelector.SetIndex(Difficulty.Index, false);
            RefreshMain();
            Show(Panel.Main, playItem);
            if (firstTime && titleGlitchTime > 0f) StartCoroutine(GlitchTitleIn());
        }

        // Called by GameManager.Pause.
        public void ShowPause()
        {
            returnTo = Panel.Pause;
            if (pauseFade != null) StopCoroutine(pauseFade);
            pauseDim.gameObject.SetActive(true);
            pauseFade = StartCoroutine(FadeGroup(pauseDimGroup, 1f, pauseFadeIn, false));
            RefreshPauseStrip();
            Show(Panel.Pause, resumeItem);
        }

        // Called by GameManager.Resume (and when a run ends under the menu): the panels go at once, the dark layer
        // fades back to the game.
        public void HidePause(bool immediate = false)
        {
            Show(Panel.None, null);
            if (pauseFade != null) StopCoroutine(pauseFade);
            pauseFade = null;
            if (!pauseDim.gameObject.activeSelf) return;
            if (immediate || pauseFadeOut <= 0f)
            {
                pauseDimGroup.alpha = 0f;
                pauseDim.gameObject.SetActive(false);
                return;
            }
            pauseDimGroup.blocksRaycasts = false;
            pauseFade = StartCoroutine(FadeGroup(pauseDimGroup, 0f, pauseFadeOut, true));
        }

        IEnumerator FadeGroup(CanvasGroup group, float to, float seconds, bool thenHide)
        {
            if (to > 0f) group.blocksRaycasts = true;
            float from = group.alpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            group.alpha = to;
            if (thenHide) group.gameObject.SetActive(false);
        }

        void OpenSub(Panel panel)
        {
            switch (panel)
            {
                case Panel.Settings:
                    RefreshSettings();
                    SelectTab(tab, false);
                    Show(panel, tabItems[tab]);
                    break;
                case Panel.HowToPlay:
                    howToText.text = BuildTips();
                    Show(panel, howToBack);
                    break;
                case Panel.Credits:
                    Show(panel, creditsBack);
                    creditsViewHeight = ((RectTransform)creditsContent.parent).rect.height;
                    creditsContent.anchoredPosition = new Vector2(0f, -creditsViewHeight);
                    break;
                case Panel.Achievements:
                    RefreshAchievements();
                    achievementsScroll.verticalNormalizedPosition = 1f;
                    Show(panel, achievementsBack);
                    break;
            }
        }

        // One level back: out of the confirmation, out of a sub-panel, or out of the pause menu.
        public void Back()
        {
            if (Starting) return;
            if (ConfirmOpen)
            {
                PlayClick();
                CloseConfirm(true);
                return;
            }
            switch (Current)
            {
                case Panel.Pause:
                    if (manager != null) manager.Resume();
                    break;
                case Panel.Settings:
                case Panel.HowToPlay:
                case Panel.Credits:
                case Panel.Achievements:
                    PlayClick();
                    CloseSub();
                    break;
            }
        }

        void CloseSub()
        {
            Panel from = Current;
            if (returnTo == Panel.Pause)
            {
                RefreshPauseStrip();
                Show(Panel.Pause, from == Panel.Settings ? settingsItemOnPause : from == Panel.HowToPlay ? howToItemOnPause : resumeItem);
                return;
            }
            RefreshMain();
            Show(Panel.Main, from == Panel.Settings ? settingsItemOnMain : from == Panel.HowToPlay ? howToItemOnMain
                : from == Panel.Credits ? creditsItemOnMain : from == Panel.Achievements ? achievementsItemOnMain : playItem);
        }

        MenuEntry settingsItemOnMain, howToItemOnMain, creditsItemOnMain, achievementsItemOnMain, settingsItemOnPause, howToItemOnPause;

        // ------------------------------------------------------------------ main menu

        void BuildMain()
        {
            mainRoot = MenuKit.Fill("Main", canvas);
            mainGroup = MenuKit.Group(mainRoot);
            MenuKit.AddImage(MenuKit.Fill("Tint", mainRoot), null, new Color(0f, 0f, 0f, 0.25f));

            // the two colour ghosts of the title only show while it glitches in
            titleRed = MakeTitle("TitleRed", new Color(1f, 0.15f, 0.2f, 0.75f));
            titleCyan = MakeTitle("TitleCyan", new Color(MenuKit.Cyan.r, MenuKit.Cyan.g, MenuKit.Cyan.b, 0.75f));
            title = MakeTitle("Title", new Color(0.96f, 0.94f, 0.9f, 1f));
            titleRed.gameObject.SetActive(false);
            titleCyan.gameObject.SetActive(false);

            tagline = MenuKit.MakeText("Tagline", mainRoot, new Vector2(0f, 228f), new Vector2(1400f, 40f), "", 27f, MenuKit.Cyan);
            tagline.characterSpacing = 3f;

            MenuKit.MakePanel("Plate", mainRoot, new Vector2(0f, -88f), new Vector2(640f, 480f), 0.62f).GetComponent<Image>().raycastTarget = false;

            var size = new Vector2(460f, 56f);
            playItem = MenuKit.MakeButton(mainRoot, "PLAY", new Vector2(0f, 98f), size, Play, 44f);
            difficultySelector = MenuKit.MakeSelector(mainRoot, "Difficulty", new Vector2(0f, 42f), 300f, new[] { "EASY", "NORMAL", "HARD" },
                Difficulty.Index, null, ChooseDifficulty, 26f);
            difficultySelector.GetComponent<MenuEntry>().normalColour = MenuKit.Cyan;
            difficultySelector.valueLabel.color = MenuKit.Cyan;
            howToItemOnMain = MenuKit.MakeButton(mainRoot, "HOW TO PLAY", new Vector2(0f, -22f), size, () => OpenSub(Panel.HowToPlay));
            achievementsItemOnMain = MenuKit.MakeButton(mainRoot, "ACHIEVEMENTS", new Vector2(0f, -84f), size, () => OpenSub(Panel.Achievements));
            achievementCounter = MenuKit.MakeText("Counter", mainRoot, new Vector2(262f, -84f), new Vector2(110f, 40f), "", 24f, MenuKit.Dim, TextAlignmentOptions.Left);
            settingsItemOnMain = MenuKit.MakeButton(mainRoot, "SETTINGS", new Vector2(0f, -146f), size, () => OpenSub(Panel.Settings));
            creditsItemOnMain = MenuKit.MakeButton(mainRoot, "CREDITS", new Vector2(0f, -208f), size, () => OpenSub(Panel.Credits));
            quitItem = MenuKit.MakeButton(mainRoot, "QUIT", new Vector2(0f, -270f), size, Quit);

            MenuKit.LinkVertical(new Selectable[]
            {
                playItem.Control, difficultySelector, howToItemOnMain.Control, achievementsItemOnMain.Control,
                settingsItemOnMain.Control, creditsItemOnMain.Control, quitItem.Control
            });

            var left = MenuKit.MakeText("Studio", mainRoot, Vector2.zero, new Vector2(600f, 36f), studio, 24f, MenuKit.Dim, TextAlignmentOptions.Left);
            Corner(left, new Vector2(0f, 0f), new Vector2(44f, 30f));
            var right = MenuKit.MakeText("Version", mainRoot, Vector2.zero, new Vector2(300f, 36f), version, 24f, MenuKit.Dim, TextAlignmentOptions.Right);
            Corner(right, new Vector2(1f, 0f), new Vector2(-44f, 30f));
        }

        TextMeshProUGUI MakeTitle(string name, Color colour)
        {
            var label = MenuKit.MakeText(name, mainRoot, new Vector2(0f, 330f), new Vector2(1600f, 190f), "TIME TRAP", 156f, colour, TextAlignmentOptions.Center, true);
            if (titleFont != null) label.font = titleFont;
            label.characterSpacing = 14f;
            return label;
        }

        static void Corner(TextMeshProUGUI label, Vector2 corner, Vector2 offset)
        {
            var rt = (RectTransform)label.transform;
            rt.anchorMin = corner;
            rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = offset;
        }

        // The title arrives split into its colours and jittering, then settles.
        IEnumerator GlitchTitleIn()
        {
            var home = ((RectTransform)title.transform).anchoredPosition;
            titleRed.gameObject.SetActive(true);
            titleCyan.gameObject.SetActive(true);
            float next = 0f;
            Vector2 jitter = Vector2.zero;
            float split = 0f;
            for (float t = 0f; t < titleGlitchTime; t += Time.unscaledDeltaTime)
            {
                float strength = 1f - t / titleGlitchTime;
                if (t >= next)
                {
                    // a new offset about 25 times a second, so it reads as jumps, not a wobble
                    next = t + 0.04f;
                    jitter = new Vector2(UnityEngine.Random.Range(-18f, 18f), UnityEngine.Random.Range(-5f, 5f)) * strength;
                    split = UnityEngine.Random.Range(8f, 26f) * strength;
                }
                ((RectTransform)title.transform).anchoredPosition = home + jitter;
                ((RectTransform)titleRed.transform).anchoredPosition = home + jitter + new Vector2(-split, 0f);
                ((RectTransform)titleCyan.transform).anchoredPosition = home + jitter + new Vector2(split, 0f);
                title.alpha = UnityEngine.Random.value < 0.15f * strength ? 0.4f : 1f;
                yield return null;
            }
            ((RectTransform)title.transform).anchoredPosition = home;
            title.alpha = 1f;
            titleRed.gameObject.SetActive(false);
            titleCyan.gameObject.SetActive(false);
        }

        // The tagline's time and the achievement counter.
        public void RefreshMain()
        {
            float seconds = Difficulty.Current != null ? Difficulty.Current.timerSeconds : (manager != null ? manager.startSeconds : 300f);
            tagline.text = "Containment breach · " + Clock(seconds) + " until portal collapse";
            achievementCounter.text = Achievements.UnlockedCount + " / " + Achievements.Count;
        }

        // mm:ss
        static string Clock(float seconds)
        {
            int whole = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (whole / 60).ToString("00") + ":" + (whole % 60).ToString("00");
        }

        void ChooseDifficulty(int index)
        {
            Difficulty.Index = index;
            if (manager != null) manager.SelectDifficulty();
            RefreshMain();
        }

        void Play()
        {
            if (Starting || manager == null || manager.State != GameState.Menu) return;
            Starting = true;
            Sound("UI/PlayWhoosh", playWhoosh);
            mainGroup.interactable = false;
            mainGroup.blocksRaycasts = false;
            defaultSelection = null;
            MenuKit.Select(null);
            manager.BeginStart();
            if (cameraRig != null && cameraRig.InMenu) cameraRig.BeginGlide(() => StartCoroutine(FinishStart()));
            else StartCoroutine(FinishStart());
        }

        // The camera is where the player's is: the menu fades out as the HUD fades in, then the game takes over.
        IEnumerator FinishStart()
        {
            manager.FadeHud(1f, menuFadeTime);
            for (float t = 0f; t < menuFadeTime; t += Time.unscaledDeltaTime)
            {
                mainGroup.alpha = 1f - t / menuFadeTime;
                yield return null;
            }
            mainGroup.alpha = 0f;
            Show(Panel.None, null);
            Starting = false;
            manager.StartGame();
        }

        void Quit()
        {
            if (manager != null) manager.Quit();
        }

        // ------------------------------------------------------------------ pause menu

        void BuildPause()
        {
            // the dark layer stays up behind Settings and How to Play as well
            pauseDim = MenuKit.Fill("PauseDim", canvas);
            pauseDimGroup = MenuKit.Group(pauseDim);
            pauseDimGroup.alpha = 0f;
            MenuKit.AddImage(pauseDim, null, new Color(0.03f, 0.03f, 0.04f, 0.66f), true);

            pauseRoot = MenuKit.Fill("Pause", canvas);
            pauseGroup = MenuKit.Group(pauseRoot);
            MenuKit.MakeHeading("Title", pauseRoot, new Vector2(0f, 360f), "PAUSED", 84f);

            var size = new Vector2(460f, 58f);
            const string sure = "Are you sure?\n<size=75%>Progress in this run will be lost.</size>";
            resumeItem = MenuKit.MakeButton(pauseRoot, "RESUME", new Vector2(0f, 228f), size, () => manager.Resume(), 42f);
            restartItem = MenuKit.MakeButton(pauseRoot, "RESTART", new Vector2(0f, 160f), size, () => Confirm(sure, () => manager.Restart()));
            settingsItemOnPause = MenuKit.MakeButton(pauseRoot, "SETTINGS", new Vector2(0f, 96f), size, () => OpenSub(Panel.Settings));
            howToItemOnPause = MenuKit.MakeButton(pauseRoot, "HOW TO PLAY", new Vector2(0f, 32f), size, () => OpenSub(Panel.HowToPlay));
            mainMenuEntry = MenuKit.MakeButton(pauseRoot, "MAIN MENU", new Vector2(0f, -32f), size, () => Confirm(sure, () => manager.ReturnToMenu()));
            pauseQuitItem = MenuKit.MakeButton(pauseRoot, "QUIT", new Vector2(0f, -96f), size, () => Confirm(sure, () => manager.Quit()));
            MenuKit.LinkVertical(new Selectable[]
            {
                resumeItem.Control, restartItem.Control, settingsItemOnPause.Control, howToItemOnPause.Control, mainMenuEntry.Control, pauseQuitItem.Control
            });

            // bottom strip: time left, lives, difficulty, objectives
            var strip = MenuKit.MakePanel("Strip", pauseRoot, Vector2.zero, new Vector2(1560f, 168f));
            strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 0f);
            strip.anchoredPosition = new Vector2(0f, 118f);
            strip.GetComponent<Image>().raycastTarget = false;

            Caption(strip, "TIME LEFT", -600f);
            stripTime = MenuKit.MakeText("Time", strip, new Vector2(-600f, -16f), new Vector2(260f, 70f), "", 54f, MenuKit.Text, TextAlignmentOptions.Center, true);

            Caption(strip, "LIVES", -300f);
            Sprite heart = GameManagerUI.MakeHeartSprite();
            for (int i = 0; i < GameManagerUI.MaxLifeIcons; i++)
            {
                var icon = MenuKit.AddImage(MenuKit.Rect("Life" + (i + 1), strip, new Vector2(-300f, -16f), new Vector2(38f, 38f)), heart, Color.white);
                stripHearts.Add(icon);
            }

            Caption(strip, "DIFFICULTY", 0f);
            stripDifficulty = MenuKit.MakeText("Difficulty", strip, new Vector2(0f, -16f), new Vector2(300f, 70f), "", 40f, MenuKit.Cyan, TextAlignmentOptions.Center, true);

            Caption(strip, "OBJECTIVES", 460f);
            stripObjectives = MenuKit.MakeText("Objectives", strip, new Vector2(470f, -20f), new Vector2(520f, 110f), "", 23f, MenuKit.Text, TextAlignmentOptions.Left);
            stripObjectives.lineSpacing = 6f;
        }

        static void Caption(Transform parent, string text, float x)
        {
            var label = MenuKit.MakeText(text, parent, new Vector2(x, 54f), new Vector2(400f, 30f), text, 20f, MenuKit.Amber, TextAlignmentOptions.Center, true);
            label.characterSpacing = 8f;
        }

        void RefreshPauseStrip()
        {
            if (manager == null) return;
            stripTime.text = GameManager.FormatTime(manager.DisplaySeconds);
            stripTime.color = manager.IsLowTime ? MenuKit.Red : MenuKit.Text;
            stripDifficulty.text = Difficulty.Name.ToUpperInvariant();

            int max = Mathf.Min(manager.startLives, stripHearts.Count);
            for (int i = 0; i < stripHearts.Count; i++)
            {
                stripHearts[i].gameObject.SetActive(i < max);
                stripHearts[i].color = i < manager.Lives ? manager.lifeColour : manager.lifeLostColour;
                stripHearts[i].rectTransform.anchoredPosition = new Vector2(-300f + (i - (max - 1) * 0.5f) * 46f, -16f);
            }

            var progress = GameProgress.Instance;
            var sb = new StringBuilder();
            if (progress != null)
            {
                ObjectiveLine(sb, ObjectiveHUD.Label(Objective.Power), progress.HasPower);
                ObjectiveLine(sb, ObjectiveHUD.Label(Objective.Pathogen), progress.HasPathogen);
                ObjectiveLine(sb, ObjectiveHUD.Label(Objective.Data), progress.HasData);
                if (progress.AllObjectivesComplete) ObjectiveLine(sb, "Escape through Gate B", progress.HasEscaped);
            }
            stripObjectives.text = sb.ToString();
        }

        // Ticked and dimmed when done, as on the HUD.
        static void ObjectiveLine(StringBuilder sb, string label, bool done)
        {
            if (sb.Length > 0) sb.Append('\n');
            if (done) sb.Append("<color=#73E680><mspace=0.55em>[x]</mspace></color> <color=").Append(MenuKit.Hex(MenuKit.Dim)).Append(">").Append(label).Append("</color>");
            else sb.Append("<mspace=0.55em>[ ]</mspace> ").Append(label);
        }

        // ------------------------------------------------------------------ confirmation

        void BuildConfirm()
        {
            confirmRoot = MenuKit.Fill("Confirm", canvas);
            MenuKit.AddImage(confirmRoot, null, new Color(0f, 0f, 0f, 0.6f), true);
            var panel = MenuKit.MakePanel("Panel", confirmRoot, Vector2.zero, new Vector2(820f, 330f), 1f);
            confirmText = MenuKit.MakeText("Text", panel, new Vector2(0f, 62f), new Vector2(740f, 150f), "", 38f, MenuKit.Text);
            confirmText.textWrappingMode = TextWrappingModes.Normal;
            confirmYes = MenuKit.MakeButton(panel, "YES", new Vector2(-170f, -100f), new Vector2(240f, 58f), ConfirmYes);
            confirmNo = MenuKit.MakeButton(panel, "NO", new Vector2(170f, -100f), new Vector2(240f, 58f), () => CloseConfirm(true));
            MenuKit.LinkHorizontal(new[] { confirmYes.Control, confirmNo.Control });
        }

        // Asks before doing something that cannot be undone. No is the default.
        void Confirm(string message, Action yes)
        {
            confirmAction = yes;
            confirmText.text = message;
            confirmReturn = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            confirmUnder = Current == Panel.Pause ? pauseGroup : Current == Panel.Settings ? settingsGroup : null;
            if (confirmUnder != null) confirmUnder.interactable = false;
            confirmRoot.gameObject.SetActive(true);
            defaultSelection = confirmNo;
            MenuKit.Select(confirmNo);
        }

        void ConfirmYes()
        {
            Action action = confirmAction;
            CloseConfirm(true);
            action?.Invoke();
        }

        void CloseConfirm(bool reselect)
        {
            if (confirmRoot == null || !confirmRoot.gameObject.activeSelf) return;
            confirmRoot.gameObject.SetActive(false);
            confirmAction = null;
            if (confirmUnder != null) confirmUnder.interactable = true;
            confirmUnder = null;
            if (!reselect || confirmReturn == null) return;
            defaultSelection = confirmReturn.transform;
            MenuKit.Select(confirmReturn.transform);
        }

        // ------------------------------------------------------------------ settings

        const float RowLabelX = -330f, RowLabelWidth = 440f, ControlX = 170f, ControlWidth = 420f;

        void BuildSettings()
        {
            settingsRoot = MenuKit.Fill("Settings", canvas);
            settingsGroup = MenuKit.Group(settingsRoot);
            var panel = MenuKit.MakePanel("Panel", settingsRoot, Vector2.zero, new Vector2(1340f, 900f));
            MenuKit.MakeHeading("Title", panel, new Vector2(0f, 385f), "SETTINGS", 58f);

            string[] names = { "AUDIO", "CONTROLS", "DISPLAY" };
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                tabItems[i] = MenuKit.MakeButton(panel, names[i], new Vector2((i - 1) * 300f, 300f), new Vector2(260f, 52f), () => SelectTab(index, true), 32f);
                tabRoots[i] = MenuKit.Rect(names[i], panel, Vector2.zero, Vector2.zero);
            }
            tabUnderline = MenuKit.Rect("Underline", panel, new Vector2(0f, 270f), new Vector2(200f, 3f));
            MenuKit.AddImage(tabUnderline, null, MenuKit.Amber);
            MenuKit.AddImage(MenuKit.Rect("Rule", panel, new Vector2(0f, 268f), new Vector2(1220f, 1f)), null, MenuKit.Track);

            BuildAudioTab(tabRoots[0], tabControls[0]);
            BuildControlsTab(tabRoots[1], tabControls[1]);
            BuildDisplayTab(tabRoots[2], tabControls[2]);

            var size = new Vector2(380f, 52f);
            settingsBack = MenuKit.MakeButton(panel, "BACK", new Vector2(-430f, -392f), size, CloseSub, 30f);
            settingsDefaults = MenuKit.MakeButton(panel, "RESET TO DEFAULTS", new Vector2(0f, -392f), size, ResetDefaults, 30f);
            settingsProgress = MenuKit.MakeButton(panel, "RESET PROGRESS", new Vector2(430f, -392f), size,
                () => Confirm("Are you sure?\n<size=75%>This clears every achievement and record.</size>", ResetProgress), 30f);
        }

        static string Percent(float value)
        {
            return Mathf.RoundToInt(value * 100f) + "%";
        }

        Slider Row(Transform parent, List<Selectable> controls, string label, float y, string key, float min, float max, Func<float, string> format)
        {
            var rowLabel = MenuKit.MakeRowLabel(parent, label, new Vector2(RowLabelX, y), RowLabelWidth);
            var slider = MenuKit.MakeSlider(parent, label, new Vector2(ControlX, y), ControlWidth, min, max, GameSettings.Get(key), rowLabel, format,
                v => GameSettings.Set(key, v));
            controls.Add(slider);
            return slider;
        }

        MenuSelector ToggleRow(Transform parent, List<Selectable> controls, string label, float y, string key)
        {
            var rowLabel = MenuKit.MakeRowLabel(parent, label, new Vector2(RowLabelX, y), RowLabelWidth);
            var selector = MenuKit.MakeSelector(parent, label, new Vector2(ControlX, y), ControlWidth, OffOn, GameSettings.IsOn(key) ? 1 : 0, rowLabel,
                i => GameSettings.Set(key, i));
            controls.Add(selector);
            return selector;
        }

        void BuildAudioTab(Transform parent, List<Selectable> controls)
        {
            masterSlider = Row(parent, controls, "Master volume", 180f, GameSettings.Master, 0f, 1f, Percent);
            musicSlider = Row(parent, controls, "Music", 100f, GameSettings.Music, 0f, 1f, Percent);
            sfxSlider = Row(parent, controls, "Sound effects", 20f, GameSettings.Sfx, 0f, 1f, Percent);
            zombiesSlider = Row(parent, controls, "Zombies", -60f, GameSettings.Zombies, 0f, 1f, Percent);
            // a sample on letting go, so the level can be judged by ear
            sfxSlider.gameObject.AddComponent<SliderRelease>().onRelease = () => Sound("UI/SfxSample", sfxSample, true);
        }

        void BuildControlsTab(Transform parent, List<Selectable> controls)
        {
            sensitivitySlider = Row(parent, controls, "Mouse sensitivity", 190f, GameSettings.Sensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x");
            invertSelector = ToggleRow(parent, controls, "Invert Y", 118f, GameSettings.InvertY);

            MenuKit.MakeText("Caption", parent, new Vector2(0f, 46f), new Vector2(1200f, 30f), "CONTROLS  <size=80%><color=" + MenuKit.Hex(MenuKit.Dim) + ">(fixed)</color></size>",
                22f, MenuKit.Amber, TextAlignmentOptions.Center, true).characterSpacing = 8f;
            // two columns of five
            BuildControlsTable(parent, 0, 5, new Vector2(-610f, -8f), 50f, 300f, 23f);
            BuildControlsTable(parent, 5, 10, new Vector2(100f, -8f), 50f, 270f, 23f);
        }

        void BuildDisplayTab(Transform parent, List<Selectable> controls)
        {
            brightnessSlider = Row(parent, controls, "Brightness", 196f, GameSettings.Brightness, 0f, 1f, Percent);
            brightnessSlider.onValueChanged.AddListener(v => RefreshBrightnessTest());
            fullscreenSelector = ToggleRow(parent, controls, "Fullscreen", 124f, GameSettings.Fullscreen);

            foreach (Resolution r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                // nothing smaller than the menus can be read at
                if (size.x >= 1024 && size.y >= 600 && !resolutions.Contains(size)) resolutions.Add(size);
            }
            Vector2Int now = GameSettings.Resolution;
            // the size the game is running at, if the list has nothing (the editor's Game view)
            if (!resolutions.Contains(now) && (resolutions.Count == 0 || (now.x >= 1024 && now.y >= 600))) resolutions.Add(now);
            resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var names = new List<string>();
            foreach (var r in resolutions) names.Add(r.x + " x " + r.y);
            var resolutionLabel = MenuKit.MakeRowLabel(parent, "Resolution", new Vector2(RowLabelX, 52f), RowLabelWidth);
            resolutionSelector = MenuKit.MakeSelector(parent, "Resolution", new Vector2(ControlX, 52f), ControlWidth, names, Mathf.Max(0, resolutions.IndexOf(now)), resolutionLabel,
                i => GameSettings.SetResolution(resolutions[i].x, resolutions[i].y));
            resolutionSelector.wrap = false;
            controls.Add(resolutionSelector);

            fovSlider = Row(parent, controls, "Field of view", -20f, GameSettings.Fov, GameSettings.MinFov, GameSettings.MaxFov, v => Mathf.RoundToInt(v) + "°");
            fovSlider.wholeNumbers = true;
            headBobSelector = ToggleRow(parent, controls, "Head bob", -92f, GameSettings.HeadBobOn);

            // the Time Lord reward: the row is only there once it is unlocked (RefreshSettings)
            goldenLabel = MenuKit.MakeRowLabel(parent, "Golden beam", new Vector2(RowLabelX, -164f), RowLabelWidth);
            goldenSelector = MenuKit.MakeSelector(parent, "Golden beam", new Vector2(ControlX, -164f), ControlWidth, OffOn, Achievements.GoldenBeamEnabled ? 1 : 0, goldenLabel,
                i => Achievements.GoldenBeamEnabled = i == 1);
            goldenSelector.GetComponent<MenuEntry>().highlightColour = MenuKit.Gold;

            // brightness test: a symbol on black that should only just show
            MenuKit.MakeText("TestCaption", parent, new Vector2(RowLabelX, -264f), new Vector2(RowLabelWidth, 80f),
                "<size=24>Brightness test</size>\n<size=20><color=" + MenuKit.Hex(MenuKit.Dim) + ">Adjust until the symbol is barely visible.</color></size>",
                24f, MenuKit.Text, TextAlignmentOptions.Left);
            var box = MenuKit.Rect("BrightnessTest", parent, new Vector2(ControlX - ControlWidth * 0.5f + 150f, -264f), new Vector2(300f, 108f));
            MenuKit.AddImage(box, null, Color.black);
            var mark = MenuKit.Rect("Symbol", box, Vector2.zero, new Vector2(84f, 84f));
            var image = MenuKit.AddImage(mark, brightnessSymbol != null ? brightnessSymbol : MenuKit.PanelSprite, Color.white);
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            brightnessMark = image;
            RefreshBrightnessTest();
        }

        // The menus are drawn over the finished frame, so the exposure does not reach them: the symbol's grey is
        // worked out from the same number of stops instead.
        void RefreshBrightnessTest()
        {
            if (brightnessMark == null) return;
            float stops = GameSettings.Instance != null ? GameSettings.Instance.BrightnessStops : 0f;
            float grey = Mathf.Clamp01(0.035f * Mathf.Pow(2f, stops * 1.6f));
            brightnessMark.color = new Color(grey, grey, grey, 1f);
        }

        public void SelectTab(int index, bool focusFirst)
        {
            tab = Mathf.Clamp(index, 0, 2);
            for (int i = 0; i < 3; i++)
            {
                tabRoots[i].gameObject.SetActive(i == tab);
                tabItems[i].normalColour = i == tab ? MenuKit.Amber : MenuKit.Text;
                tabItems[i].label.color = tabItems[i].Highlighted || i == tab ? MenuKit.Amber : MenuKit.Text;
            }
            tabUnderline.anchoredPosition = new Vector2((tab - 1) * 300f, 270f);

            // down from the tabs into the tab's controls, down again to the bottom row
            var controls = tabControls[tab];
            var column = new List<Selectable> { tabItems[tab].Control };
            column.AddRange(controls);
            column.Add(settingsBack.Control);
            MenuKit.LinkVertical(column, false);
            var tabs = new[] { tabItems[0].Control, tabItems[1].Control, tabItems[2].Control };
            var bottom = new[] { settingsBack.Control, settingsDefaults.Control, settingsProgress.Control };
            MenuKit.LinkHorizontal(tabs);
            MenuKit.LinkHorizontal(bottom);
            Selectable first = controls.Count > 0 ? controls[0] : settingsBack.Control;
            Selectable last = controls.Count > 0 ? controls[controls.Count - 1] : tabItems[tab].Control;
            foreach (var t in tabs) SetVertical(t, null, first);
            foreach (var b in bottom) SetVertical(b, last, null);

            if (focusFirst && Current == Panel.Settings)
            {
                defaultSelection = tabItems[tab];
                MenuKit.Select(tabItems[tab]);
            }
        }

        static void SetVertical(Selectable control, Selectable up, Selectable down)
        {
            Navigation nav = control.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = up;
            nav.selectOnDown = down;
            control.navigation = nav;
        }

        // Puts every control on its saved value (opening the panel, and after Reset to defaults).
        void RefreshSettings()
        {
            masterSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Master));
            musicSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Music));
            sfxSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Sfx));
            zombiesSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Zombies));
            sensitivitySlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Sensitivity));
            brightnessSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Brightness));
            fovSlider.SetValueWithoutNotify(GameSettings.Get(GameSettings.Fov));
            foreach (var slider in new[] { masterSlider, musicSlider, sfxSlider, zombiesSlider, sensitivitySlider, brightnessSlider, fovSlider })
            {
                // the value text follows onValueChanged, which SetValueWithoutNotify skips
                float value = slider.value;
                slider.onValueChanged.Invoke(value);
            }
            invertSelector.SetIndex(GameSettings.IsOn(GameSettings.InvertY) ? 1 : 0, false);
            fullscreenSelector.SetIndex(GameSettings.IsOn(GameSettings.Fullscreen) ? 1 : 0, false);
            headBobSelector.SetIndex(GameSettings.IsOn(GameSettings.HeadBobOn) ? 1 : 0, false);

            bool golden = Achievements.GoldenBeamAvailable;
            goldenLabel.gameObject.SetActive(golden);
            goldenSelector.gameObject.SetActive(golden);
            goldenSelector.SetIndex(Achievements.GoldenBeamEnabled ? 1 : 0, false);
            var display = tabControls[2];
            display.Remove(goldenSelector);
            if (golden) display.Insert(display.IndexOf(headBobSelector) + 1, goldenSelector);
            int resolution = resolutions.IndexOf(GameSettings.Resolution);
            if (resolution >= 0) resolutionSelector.SetIndex(resolution, false);
            RefreshBrightnessTest();
        }

        // Achievements, records and the golden beam go; the panel and the main menu's counter follow.
        void ResetProgress()
        {
            GameSettings.ResetProgress();
            RefreshSettings();
            SelectTab(tab, false);
            RefreshMain();
        }

        void ResetDefaults()
        {
            GameSettings.ResetToDefaults();
            RefreshSettings();
        }

        // ------------------------------------------------------------------ how to play

        void BuildHowToPlay()
        {
            howToRoot = MenuKit.Fill("HowToPlay", canvas);
            var panel = MenuKit.MakePanel("Panel", howToRoot, Vector2.zero, new Vector2(1700f, 920f));
            MenuKit.MakeHeading("Title", panel, new Vector2(0f, 395f), "HOW TO PLAY", 58f);

            SubHeading(panel, "CONTROLS", -440f);
            BuildControlsTable(panel, 0, Controls.Length, new Vector2(-790f, 262f), 56f, 400f, 26f);

            MenuKit.AddImage(MenuKit.Rect("Divider", panel, new Vector2(0f, -10f), new Vector2(1f, 640f)), null, MenuKit.Track);

            SubHeading(panel, "OBJECTIVE AND TIPS", 420f);
            howToText = MenuKit.MakeText("Tips", panel, new Vector2(420f, -22f), new Vector2(740f, 600f), "", 25f, MenuKit.Text, TextAlignmentOptions.TopLeft);
            howToText.textWrappingMode = TextWrappingModes.Normal;
            howToText.lineSpacing = 8f;
            howToText.paragraphSpacing = 26f;

            howToBack = MenuKit.MakeButton(panel, "BACK", new Vector2(0f, -405f), new Vector2(300f, 54f), CloseSub, 32f);
        }

        static void SubHeading(Transform parent, string text, float x)
        {
            MenuKit.MakeText(text, parent, new Vector2(x, 318f), new Vector2(760f, 36f), text, 26f, MenuKit.Cyan, TextAlignmentOptions.Center, true).characterSpacing = 8f;
        }

        // Rows of "action   [key] [key]". The keys start Key Offset to the right of the row's left edge.
        static void BuildControlsTable(Transform parent, int from, int to, Vector2 topLeft, float rowHeight, float keyOffset, float fontSize)
        {
            for (int i = from; i < to; i++)
            {
                float y = topLeft.y - (i - from) * rowHeight;
                MenuKit.MakeText("Action " + i, parent, new Vector2(topLeft.x + keyOffset * 0.5f, y), new Vector2(keyOffset, rowHeight), Controls[i].Key,
                    fontSize, MenuKit.Text, TextAlignmentOptions.Left);
                float x = topLeft.x + keyOffset;
                foreach (string key in Controls[i].Value)
                {
                    var cap = MenuKit.MakeKeyCap(parent, key, new Vector2(x, y), fontSize - 3f);
                    x += cap.sizeDelta.x + 8f;
                }
            }
        }

        // The right-hand column. Its numbers are the selected difficulty's.
        string BuildTips()
        {
            DifficultySettings d = Difficulty.Current;
            float timer = d != null ? d.timerSeconds : (manager != null ? manager.startSeconds : 300f);
            int lives = d != null ? d.lives : (manager != null ? manager.startLives : 3);
            float penalty = d != null ? d.penaltySeconds : (manager != null ? manager.catchPenaltySeconds : 10f);
            string amber = "<color=" + MenuKit.Hex(MenuKit.Amber) + "><b>";
            const string end = "</b></color>  ";

            return amber + "GOAL" + end + "Restore power, take the pathogen sample and download the research data, in any order. Then reach the portal at Gate B before the "
                + GameManager.FormatTime(timer) + " timer runs out.\n"
                + amber + "LIVES" + end + "You have " + lives + ". Each catch or fall costs a life and " + Mathf.RoundToInt(penalty)
                + " seconds, and sends you back to your last checkpoint.\n"
                + amber + "ZOMBIES" + end + "They see your torch from further away and hear you sprint. Turn the torch off to sneak. Headshots do triple damage.\n"
                + amber + "STAMINA" + end + "Sprinting drains it. When it is empty you slow down and pant loudly enough for zombies to hear.\n"
                + amber + "POWER-UPS" + end + "Flash Pulse stuns, Speed Boost gives unlimited sprint, Zombie Freeze stops zombies in place. Chests can hold a compass.\n"
                + "<color=" + MenuKit.Hex(MenuKit.Dim) + "><size=85%>Difficulty: " + Difficulty.Name + "</size></color>";
        }

        // ------------------------------------------------------------------ credits

        void BuildCredits()
        {
            creditsRoot = MenuKit.Fill("Credits", canvas);
            MenuKit.AddImage(creditsRoot, null, MenuKit.Panel(0.94f), true);

            var view = MenuKit.Fill("View", creditsRoot);
            view.offsetMin = new Vector2(0f, 110f);
            view.offsetMax = new Vector2(0f, -40f);
            view.gameObject.AddComponent<RectMask2D>();

            var label = MenuKit.MakeText("Text", view, Vector2.zero, new Vector2(1300f, 100f), BuildCreditsText(), 26f, MenuKit.Text, TextAlignmentOptions.Top);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.lineSpacing = 10f;
            creditsContent = (RectTransform)label.transform;
            creditsContent.anchorMin = creditsContent.anchorMax = new Vector2(0.5f, 1f);
            creditsContent.pivot = new Vector2(0.5f, 1f);
            creditsHeight = label.GetPreferredValues(label.text, 1300f, 0f).y;
            creditsContent.sizeDelta = new Vector2(1300f, creditsHeight);

            creditsBack = MenuKit.MakeButton(creditsRoot, "BACK", Vector2.zero, new Vector2(300f, 54f), CloseSub, 32f);
            var back = (RectTransform)creditsBack.transform;
            back.anchorMin = back.anchorMax = new Vector2(0.5f, 0f);
            back.anchoredPosition = new Vector2(0f, 56f);
        }

        // Rolls up from below the view; starts again once the last line has left the top.
        void ScrollCredits(float deltaTime)
        {
            creditsViewHeight = ((RectTransform)creditsContent.parent).rect.height;
            float y = creditsContent.anchoredPosition.y + creditsSpeed * deltaTime;
            if (y > creditsHeight + 40f || y < -creditsViewHeight) y = -creditsViewHeight;
            creditsContent.anchoredPosition = new Vector2(0f, y);
        }

        // The studio and the team, then CREDITS.md with its markdown taken out: headings become headings, a table row
        // becomes one line, and the table header and ruler rows are dropped.
        string BuildCreditsText()
        {
            string amber = MenuKit.Hex(MenuKit.Amber), cyan = MenuKit.Hex(MenuKit.Cyan), dim = MenuKit.Hex(MenuKit.Dim);
            var sb = new StringBuilder();
            sb.Append("<size=66><b><color=").Append(amber).Append('>').Append(studio).Append("</color></b></size>\n");
            sb.Append("<size=26><color=").Append(dim).Append(">presents</color></size>\n\n");
            sb.Append("<size=110><b>TIME TRAP</b></size>\n\n\n");
            sb.Append("<size=44><b><color=").Append(amber).Append(">THE TEAM</color></b></size>\n\n");
            sb.Append("[TODO: group member names]\n\n\n");
            if (credits == null) return sb.ToString();

            string[] lines = credits.text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("|"))
                {
                    // the header row is the one with a ruler (| --- | --- |) under it
                    bool ruler = line.Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0;
                    bool header = i + 1 < lines.Length && lines[i + 1].Replace("|", "").Replace("-", "").Replace(":", "").Trim().Length == 0 && lines[i + 1].Contains("-");
                    if (ruler || header) continue;
                    var cells = new List<string>();
                    foreach (string cell in line.Split('|'))
                    {
                        string cleaned = Clean(cell);
                        if (cleaned.Length > 0) cells.Add(cleaned);
                    }
                    if (cells.Count == 0) continue;
                    sb.Append("<b>").Append(cells[0]).Append("</b>");
                    if (cells.Count > 1)
                    {
                        sb.Append("\n<size=80%><color=").Append(dim).Append('>');
                        for (int c = 1; c < cells.Count; c++) sb.Append(c > 1 ? "  ·  " : "").Append(cells[c]);
                        sb.Append("</color></size>");
                    }
                    sb.Append("\n\n");
                    continue;
                }

                if (line.StartsWith("### ")) sb.Append("\n<size=32><b><color=").Append(cyan).Append('>').Append(Clean(line.Substring(4)).ToUpperInvariant()).Append("</color></b></size>\n\n");
                else if (line.StartsWith("## ")) sb.Append("\n\n<size=44><b><color=").Append(amber).Append('>').Append(Clean(line.Substring(3)).ToUpperInvariant()).Append("</color></b></size>\n\n");
                else if (line.StartsWith("# ")) continue; // the file's own title
                else if (line.Length > 0) sb.Append("<color=").Append(dim).Append('>').Append(Clean(line)).Append("</color>\n");
            }
            sb.Append("\n\n\n<size=40><b>Thank you for playing.</b></size>\n");
            return sb.ToString();
        }

        // Markdown symbols out; angle brackets too, so nothing in the file is read as a TextMeshPro tag.
        static string Clean(string text)
        {
            return text.Replace("**", "").Replace("`", "").Replace("<", "(").Replace(">", ")").Trim();
        }

        // ------------------------------------------------------------------ achievements

        const float CardWidth = 724f, CardHeight = 124f, CardGap = 14f, AchievementsViewHeight = 580f;

        void BuildAchievements()
        {
            achievementsRoot = MenuKit.Fill("Achievements", canvas);
            var panel = MenuKit.MakePanel("Panel", achievementsRoot, Vector2.zero, new Vector2(1620f, 920f));
            MenuKit.MakeHeading("Title", panel, new Vector2(0f, 395f), "ACHIEVEMENTS", 58f);
            achievementsHeader = MenuKit.MakeText("Header", panel, new Vector2(0f, 328f), new Vector2(1400f, 40f), "", 28f, MenuKit.Cyan);
            achievementsHeader.characterSpacing = 3f;

            // the cards scroll inside this window: wheel, drag, or the up and down keys
            var view = MenuKit.Rect("View", panel, new Vector2(-8f, -8f), new Vector2(CardWidth * 2f + CardGap, AchievementsViewHeight));
            MenuKit.AddImage(view, null, Color.clear, true);
            view.gameObject.AddComponent<RectMask2D>();
            achievementsContent = MenuKit.Rect("Content", view, Vector2.zero, Vector2.zero);
            achievementsContent.anchorMin = new Vector2(0f, 1f);
            achievementsContent.anchorMax = new Vector2(1f, 1f);
            achievementsContent.pivot = new Vector2(0.5f, 1f);
            int rows = (Achievements.Count + 1) / 2;
            achievementsContent.sizeDelta = new Vector2(0f, rows * (CardHeight + CardGap) - CardGap);
            achievementsContent.anchoredPosition = Vector2.zero;

            achievementsScroll = view.gameObject.AddComponent<ScrollRect>();
            achievementsScroll.viewport = view;
            achievementsScroll.content = achievementsContent;
            achievementsScroll.horizontal = false;
            achievementsScroll.movementType = ScrollRect.MovementType.Clamped;
            achievementsScroll.scrollSensitivity = 45f;

            var bar = MenuKit.Rect("Scrollbar", panel, new Vector2(CardWidth + CardGap * 0.5f + 16f, -8f), new Vector2(8f, AchievementsViewHeight));
            MenuKit.AddImage(bar, MenuKit.PanelSprite, MenuKit.Track, true).pixelsPerUnitMultiplier = 6f;
            var slide = MenuKit.Fill("Sliding Area", bar);
            var handle = MenuKit.Fill("Handle", slide);
            var handleImage = MenuKit.AddImage(handle, MenuKit.PanelSprite, MenuKit.Amber, true);
            handleImage.pixelsPerUnitMultiplier = 6f;
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.transition = Selectable.Transition.None;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            // the keyboard stays on Back; the arrow keys scroll the list themselves
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            achievementsScroll.verticalScrollbar = scrollbar;

            int count = Achievements.Count;
            cardIcons = new Image[count];
            cardNames = new TextMeshProUGUI[count];
            cardDescriptions = new TextMeshProUGUI[count];
            cardPoints = new TextMeshProUGUI[count];
            for (int i = 0; i < count; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * (CardWidth + CardGap) * 0.5f;
                float y = -(i / 2) * (CardHeight + CardGap) - CardHeight * 0.5f;
                var card = MenuKit.Rect("Card " + Achievements.All[i].id, achievementsContent, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(CardWidth, CardHeight));
                MenuKit.AddImage(card, MenuKit.PanelSprite, new Color(1f, 1f, 1f, 0.045f));
                var disc = MenuKit.Rect("Disc", card, new Vector2(-CardWidth * 0.5f + 62f, 0f), new Vector2(88f, 88f));
                MenuKit.AddImage(disc, MenuKit.PillSprite, new Color(0f, 0f, 0f, 0.45f));
                cardIcons[i] = MenuKit.AddImage(MenuKit.Rect("Icon", disc, Vector2.zero, new Vector2(58f, 58f)), null, Color.white);
                cardIcons[i].preserveAspect = true;
                cardNames[i] = MenuKit.MakeText("Name", card, new Vector2(-12f, 24f), new Vector2(470f, 40f), "", 29f, MenuKit.Text, TextAlignmentOptions.Left, true);
                cardDescriptions[i] = MenuKit.MakeText("Description", card, new Vector2(-12f, -22f), new Vector2(470f, 58f), "", 21f, MenuKit.Dim, TextAlignmentOptions.TopLeft);
                cardDescriptions[i].textWrappingMode = TextWrappingModes.Normal;
                cardPoints[i] = MenuKit.MakeText("Points", card, new Vector2(CardWidth * 0.5f - 76f, 0f), new Vector2(120f, 44f), "", 27f, MenuKit.Amber, TextAlignmentOptions.Right, true);
            }

            achievementsBack = MenuKit.MakeButton(panel, "BACK", new Vector2(0f, -405f), new Vector2(300f, 54f), CloseSub, 32f);
            achievementsBack.Control.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        // The header and every card, from what is unlocked now. A locked card is grey; a hidden one says nothing
        // about itself until it is unlocked.
        public void RefreshAchievements()
        {
            achievementsHeader.text = Achievements.UnlockedCount + " of " + Achievements.Count + " achievements  \u00B7  "
                + Achievements.UnlockedPoints + " / " + Achievements.TotalPoints + " TP";
            Color grey = new Color(0.42f, 0.44f, 0.48f, 1f);
            for (int i = 0; i < Achievements.Count; i++)
            {
                AchievementDef def = Achievements.All[i];
                bool unlocked = Achievements.IsUnlocked(def.id);
                bool secret = def.hidden && !unlocked;
                Color tint = Achievements.Tint(def, unlocked);
                cardIcons[i].sprite = Achievements.Icon != null ? Achievements.Icon : brightnessSymbol;
                cardIcons[i].enabled = cardIcons[i].sprite != null;
                cardIcons[i].color = tint;
                cardNames[i].text = secret ? "???" : def.name;
                cardNames[i].color = unlocked ? MenuKit.Text : grey;
                cardDescriptions[i].text = secret ? "Secret achievement: keep playing to unlock" : def.description;
                cardDescriptions[i].color = unlocked ? MenuKit.Dim : new Color(grey.r, grey.g, grey.b, 0.8f);
                cardPoints[i].text = def.points + " TP";
                cardPoints[i].color = unlocked ? tint : grey;
            }
        }

        // Up and down (arrows or W / S) move the list, since there is only Back to select.
        void ScrollAchievements(float deltaTime)
        {
            float hidden = achievementsContent.rect.height - AchievementsViewHeight;
            if (hidden <= 0f) return;
            float move = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(move) < 0.1f) return;
            achievementsScroll.verticalNormalizedPosition = Mathf.Clamp01(achievementsScroll.verticalNormalizedPosition + move * deltaTime * 900f / hidden);
        }

        // For tests.
        public string AchievementsHeader => achievementsHeader != null ? achievementsHeader.text : "";
        public string CardText(int index) => cardNames[index].text + " / " + cardDescriptions[index].text + " / " + cardPoints[index].text;
        public Color CardTint(int index) => cardIcons[index].color;
        public bool GoldenToggleVisible => goldenSelector != null && goldenSelector.gameObject.activeSelf;
        public MenuSelector GoldenSelector => goldenSelector;
    }
}
