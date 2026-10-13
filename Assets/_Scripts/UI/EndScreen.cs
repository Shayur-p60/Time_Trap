using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeTrap.UI
{
    // The win and loss screen. Built by GameManagerUI on its canvas; GameManager.EndRun shows it.
    //
    //   Win    the portal flares and the picture whites out, then settles to dark as the title comes up.
    //   Loss   a time-distortion glitch, a fade to black, then the red title.
    // After the title: the subtitle and difficulty, the run's numbers counting up one row at a time (with NEW BEST
    // beside a beaten record), the achievements unlocked this run, a tip on a loss, and last the two buttons, which
    // cannot be pressed before then. The whole sequence takes about three seconds; any key or click jumps to its end.
    // It runs on unscaled time: the game is frozen underneath.
    public class EndScreen : MonoBehaviour
    {
        public static readonly string[] Tips =
        {
            "Zombies spot your torch from twice as far. Switch it off to sneak past.",
            "Headshots do triple damage: one clean shot drops an ordinary zombie.",
            "Sprinting is loud. Walk when zombies are close, or crouch to move in silence.",
            "Run out of stamina and you slow down and pant. Ease off before it empties.",
            "The data download is noisy. Clear the room before you start it.",
            "Every catch costs a life and time on the clock. Keep your distance and keep moving.",
            "A Flash Pulse stuns every zombie near you for a few seconds. Save it for a crowd.",
            "The Bloater soaks up body shots. Aim for the head, or go around it.",
        };

        const int Rows = 7, MaxChips = 8;
        const float RowTime = 0.2f;

        // For tests.
        public bool Visible => root != null && root.gameObject.activeSelf;
        public bool Finished { get; private set; }
        public string Title => title != null ? title.text : "";
        public string Subtitle => subtitle != null ? subtitle.text : "";
        public string DifficultyLine => difficulty != null ? difficulty.text : "";
        public string Tip => tip != null && tip.gameObject.activeSelf ? tip.text : "";
        public string UnlocksHeading => unlocksRoot != null && unlocksRoot.gameObject.activeSelf ? unlocksTotal.text : "";
        public int UnlocksListed { get; private set; }
        public Button PlayAgainButton => playAgain.Button;
        public Button MainMenuButton => mainMenu.Button;

        // "name = value [NEW BEST]" for each row, as shown.
        public string StatsText
        {
            get
            {
                var sb = new StringBuilder();
                for (int i = 0; i < Rows; i++)
                {
                    if (i > 0) sb.Append(" | ");
                    sb.Append(rowNames[i].text).Append(" = ").Append(i == 1 ? livesShown + " of " + livesMax + " hearts" : rowValues[i].text);
                    if (rowBest[i].gameObject.activeSelf) sb.Append(" [NEW BEST]");
                }
                return sb.ToString();
            }
        }

        GameManager owner;
        RectTransform root;
        Image cover;
        TextMeshProUGUI title, subtitle, difficulty, tip, unlocksTotal;
        CanvasGroup statsGroup, unlocksGroup, buttonsGroup;
        RectTransform unlocksRoot;
        readonly TextMeshProUGUI[] rowNames = new TextMeshProUGUI[Rows];
        readonly TextMeshProUGUI[] rowValues = new TextMeshProUGUI[Rows];
        readonly TextMeshProUGUI[] rowBest = new TextMeshProUGUI[Rows];
        readonly CanvasGroup[] rowGroups = new CanvasGroup[Rows];
        readonly List<Image> hearts = new List<Image>();
        readonly Image[] chipIcons = new Image[MaxChips];
        readonly TextMeshProUGUI[] chipLabels = new TextMeshProUGUI[MaxChips];
        MenuEntry playAgain, mainMenu;
        int livesShown, livesMax;
        bool skip;
        int shownFrame;

        static readonly Color Dark = new Color(0.02f, 0.021f, 0.027f, 0.985f);

        // ------------------------------------------------------------------ construction

        public void Build(GameManager manager, Transform canvas)
        {
            owner = manager;
            root = MenuKit.Fill("End", canvas);
            cover = MenuKit.AddImage(MenuKit.Fill("Cover", root), null, Color.clear, true);

            title = MenuKit.MakeText("Title", root, new Vector2(0f, 408f), new Vector2(1800f, 120f), "", 92f, Color.white, TextAlignmentOptions.Center, true);
            if (MenuKit.TitleFont != null) title.font = MenuKit.TitleFont;
            title.characterSpacing = 6f;
            subtitle = MenuKit.MakeText("Subtitle", root, new Vector2(0f, 330f), new Vector2(1600f, 48f), "", 32f, MenuKit.Text);
            difficulty = MenuKit.MakeText("Difficulty", root, new Vector2(0f, 284f), new Vector2(800f, 36f), "", 22f, MenuKit.Cyan, TextAlignmentOptions.Center, true);
            difficulty.characterSpacing = 8f;

            // ---- the run's numbers
            var stats = MenuKit.MakePanel("Stats", root, new Vector2(0f, 92f), new Vector2(820f, 330f), 0.6f);
            stats.GetComponent<Image>().raycastTarget = false;
            statsGroup = MenuKit.Group(stats);
            string[] names = { "Time left", "Lives left", "Zombies killed", "Shots fired", "Times caught", "Times exhausted", "Power-ups used" };
            Sprite heart = GameManagerUI.MakeHeartSprite();
            for (int i = 0; i < Rows; i++)
            {
                float y = 126f - i * 42f;
                var row = MenuKit.Rect("Row " + names[i], stats, new Vector2(0f, y), new Vector2(740f, 40f));
                rowGroups[i] = MenuKit.Group(row);
                rowNames[i] = MenuKit.MakeText("Name", row, new Vector2(-170f, 0f), new Vector2(400f, 40f), names[i], 28f, MenuKit.Dim, TextAlignmentOptions.Left);
                rowValues[i] = MenuKit.MakeText("Value", row, new Vector2(150f, 0f), new Vector2(440f, 40f), "", 28f, MenuKit.Text, TextAlignmentOptions.Right, true);
                // outside the panel's right edge, so it never crowds the value
                rowBest[i] = MenuKit.MakeText("Best", row, new Vector2(480f, 0f), new Vector2(180f, 40f), "NEW BEST", 20f, MenuKit.Amber, TextAlignmentOptions.Left, true);
                rowBest[i].characterSpacing = 6f;
                rowBest[i].gameObject.SetActive(false);
                if (i != 1) continue;
                for (int h = 0; h < GameManagerUI.MaxLifeIcons; h++)
                    hearts.Add(MenuKit.AddImage(MenuKit.Rect("Life" + (h + 1), row, new Vector2(352f - h * 40f, 0f), new Vector2(32f, 32f)), heart, Color.white));
            }

            // ---- achievements unlocked this run
            unlocksRoot = MenuKit.Rect("Unlocks", root, new Vector2(0f, -196f), new Vector2(1200f, 220f));
            unlocksGroup = MenuKit.Group(unlocksRoot);
            var heading = MenuKit.MakeText("Heading", unlocksRoot, new Vector2(-250f, 92f), new Vector2(600f, 32f), "ACHIEVEMENTS UNLOCKED", 22f, MenuKit.Amber, TextAlignmentOptions.Left, true);
            heading.characterSpacing = 8f;
            unlocksTotal = MenuKit.MakeText("Total", unlocksRoot, new Vector2(250f, 92f), new Vector2(600f, 32f), "", 22f, MenuKit.Dim, TextAlignmentOptions.Right);
            for (int i = 0; i < MaxChips; i++)
            {
                var chip = MenuKit.Rect("Chip" + i, unlocksRoot, new Vector2(i % 2 == 0 ? -275f : 275f, 48f - (i / 2) * 42f), new Vector2(530f, 40f));
                chipIcons[i] = MenuKit.AddImage(MenuKit.Rect("Icon", chip, new Vector2(-247f, 0f), new Vector2(34f, 34f)), Achievements.Icon, MenuKit.Amber);
                chipIcons[i].preserveAspect = true;
                chipLabels[i] = MenuKit.MakeText("Label", chip, new Vector2(22f, 0f), new Vector2(486f, 40f), "", 25f, MenuKit.Text, TextAlignmentOptions.Left);
            }

            tip = MenuKit.MakeText("Tip", root, new Vector2(0f, -396f), new Vector2(1500f, 40f), "", 25f, MenuKit.Dim);

            var buttons = MenuKit.Rect("Buttons", root, new Vector2(0f, -462f), new Vector2(1000f, 60f));
            buttonsGroup = MenuKit.Group(buttons);
            // Play Again: the same difficulty, straight into the run. Main Menu: back to the menu.
            playAgain = MenuKit.MakeButton(buttons, "PLAY AGAIN", new Vector2(-230f, 0f), new Vector2(380f, 58f), owner.Restart);
            mainMenu = MenuKit.MakeButton(buttons, "MAIN MENU", new Vector2(230f, 0f), new Vector2(380f, 58f), owner.ReturnToMenu);
            MenuKit.LinkHorizontal(new[] { playAgain.Control, mainMenu.Control });

            root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ showing

        public void Show(RunStats stats)
        {
            if (root == null) return;
            bool won = stats.outcome == RunOutcome.Won;
            string titleText, subtitleText;
            switch (stats.outcome)
            {
                case RunOutcome.Won: titleText = owner.winTitle; subtitleText = owner.winText; break;
                case RunOutcome.Infection: titleText = owner.infectionTitle; subtitleText = owner.infectionText; break;
                case RunOutcome.Fall: titleText = owner.fallTitle; subtitleText = owner.fallText; break;
                default: titleText = owner.portalFailureTitle; subtitleText = owner.portalFailureText; break;
            }

            title.text = titleText;
            title.color = won ? owner.winColour : owner.lossColour;
            subtitle.text = subtitleText;
            difficulty.text = "DIFFICULTY: " + (string.IsNullOrEmpty(stats.difficulty) ? Difficulty.Name : stats.difficulty).ToUpperInvariant();
            tip.text = Tips[UnityEngine.Random.Range(0, Tips.Length)];
            tip.gameObject.SetActive(!won);

            var tracker = AchievementTracker.Instance;
            rowBest[0].gameObject.SetActive(tracker != null && tracker.NewBestTime);
            rowBest[2].gameObject.SetActive(tracker != null && tracker.NewBestKills);
            rowBest[3].gameObject.SetActive(tracker != null && tracker.NewBestAccuracy);

            title.alpha = 0f;
            subtitle.alpha = 0f;
            difficulty.alpha = 0f;
            tip.alpha = 0f;
            statsGroup.alpha = 0f;
            unlocksGroup.alpha = 0f;
            buttonsGroup.alpha = 0f;
            buttonsGroup.interactable = false;
            buttonsGroup.blocksRaycasts = false;
            for (int i = 0; i < Rows; i++) rowGroups[i].alpha = 0f;
            unlocksRoot.gameObject.SetActive(false);
            cover.color = Color.clear;
            Finished = false;
            skip = false;
            shownFrame = Time.frameCount;

            root.gameObject.SetActive(true);
            MenuKit.Select(null);
            StopAllCoroutines();
            StartCoroutine(Sequence(stats, won));
        }

        // Jumps to the end of the sequence (any key or click does the same).
        public void Skip()
        {
            skip = true;
        }

        void Update()
        {
            if (!Visible || Finished || Time.frameCount <= shownFrame + 1) return;
            if (Input.anyKeyDown) skip = true;
        }

        // Runs body(0..1) over a number of real seconds; at once when skipping.
        IEnumerator Over(float seconds, Action<float> body)
        {
            for (float t = 0f; t < seconds && !skip; t += Time.unscaledDeltaTime)
            {
                body(t / seconds);
                yield return null;
            }
            body(1f);
        }

        IEnumerator Sequence(RunStats stats, bool won)
        {
            // ---- the way in
            if (won)
            {
                var portal = FindAnyObjectByType<EscapePortal>();
                if (portal != null) portal.Flare(0.6f);
                if (ScreenFX.Instance != null) ScreenFX.Instance.TriggerFlash();
                yield return Over(0.35f, k => cover.color = new Color(1f, 1f, 1f, k));
                yield return Over(0.15f, k => { });
                PlayStinger(true);
                yield return Over(0.45f, k =>
                {
                    cover.color = Color.Lerp(Color.white, Dark, k);
                    title.alpha = k;
                });
            }
            else
            {
                if (ScreenFX.Instance != null) ScreenFX.Instance.TriggerGlitch(0.7f);
                yield return Over(0.6f, k => cover.color = new Color(Dark.r, Dark.g, Dark.b, Dark.a * k));
                PlayStinger(false);
                yield return Over(0.3f, k => title.alpha = k);
            }

            yield return Over(0.2f, k =>
            {
                subtitle.alpha = k;
                difficulty.alpha = k;
                statsGroup.alpha = k;
            });

            // ---- the numbers, one row at a time
            livesMax = Mathf.Min(owner.startLives, hearts.Count);
            for (int i = 0; i < Rows; i++)
            {
                int row = i;
                if (!skip) MenuKit.Hover?.Invoke();
                yield return Over(RowTime, k =>
                {
                    rowGroups[row].alpha = Mathf.Clamp01(k * 3f);
                    SetRow(row, stats, k);
                });
            }

            // ---- achievements, tip, buttons
            int listed = FillUnlocks();
            yield return Over(0.25f, k =>
            {
                if (listed > 0) unlocksGroup.alpha = k;
                tip.alpha = k;
                buttonsGroup.alpha = k;
            });

            Finished = true;
            buttonsGroup.interactable = true;
            buttonsGroup.blocksRaycasts = true;
            // the key or click that skipped the sequence is not a press on Play Again
            yield return null;
            MenuKit.Select(playAgain);
        }

        void PlayStinger(bool won)
        {
            GameAudio.EndSting(won);
            var music = FindAnyObjectByType<MusicManager>();
            if (music != null) music.PlayStinger(won);
        }

        // One row at k (0 = just started counting, 1 = its final value).
        void SetRow(int row, RunStats stats, float k)
        {
            switch (row)
            {
                case 0:
                    rowValues[0].text = GameManager.FormatTime(Mathf.Lerp(0f, stats.timeRemaining, k));
                    break;
                case 1:
                    // hearts fill in from the left, one per life left
                    livesShown = Mathf.RoundToInt(Mathf.Lerp(0f, stats.livesLeft, k));
                    rowValues[1].text = "";
                    for (int h = 0; h < hearts.Count; h++)
                    {
                        // laid out right to left: the last heart sits at the right edge
                        int index = livesMax - 1 - h;
                        hearts[h].gameObject.SetActive(h < livesMax);
                        hearts[h].color = index < livesShown ? owner.lifeColour : owner.lifeLostColour;
                    }
                    break;
                case 2:
                    rowValues[2].text = Count(stats.zombiesKilled, k) + "  <size=75%><color=" + MenuKit.Hex(MenuKit.Dim) + ">("
                        + Count(stats.headshotKills, k) + (stats.headshotKills == 1 ? " headshot)" : " headshots)") + "</color></size>";
                    break;
                case 3:
                    rowValues[3].text = Count(stats.shotsFired, k) + "  <size=75%><color=" + MenuKit.Hex(MenuKit.Dim) + ">("
                        + (stats.shotsFired > 0 ? Mathf.RoundToInt(stats.Accuracy * 100f * k) + "% accuracy)" : "no shots)") + "</color></size>";
                    break;
                case 4: rowValues[4].text = Count(stats.catches, k).ToString(); break;
                case 5: rowValues[5].text = Count(stats.timesExhausted, k).ToString(); break;
                default: rowValues[6].text = Count(stats.powerUpsUsed, k).ToString(); break;
            }
        }

        static int Count(int value, float k)
        {
            return Mathf.RoundToInt(Mathf.Lerp(0f, value, k));
        }

        // The achievements unlocked since the scene loaded. Returns how many (0 hides the section).
        int FillUnlocks()
        {
            var unlocked = Achievements.UnlockedThisRun;
            UnlocksListed = unlocked.Count;
            unlocksRoot.gameObject.SetActive(unlocked.Count > 0);
            if (unlocked.Count == 0) return 0;

            for (int i = 0; i < MaxChips; i++)
            {
                bool used = i < unlocked.Count;
                chipIcons[i].transform.parent.gameObject.SetActive(used);
                if (!used) continue;
                // more than fit: the last chip says how many are not shown
                if (i == MaxChips - 1 && unlocked.Count > MaxChips)
                {
                    chipIcons[i].enabled = false;
                    chipLabels[i].text = "+ " + (unlocked.Count - (MaxChips - 1)) + " more";
                    continue;
                }
                AchievementDef def = unlocked[i];
                chipIcons[i].sprite = Achievements.Icon;
                chipIcons[i].enabled = Achievements.Icon != null;
                chipIcons[i].color = Achievements.Tint(def, true);
                chipLabels[i].text = "<b>" + def.points + " TP</b> – " + def.name;
            }
            unlocksTotal.text = Achievements.UnlockedCount + " of " + Achievements.Count + "  ·  " + Achievements.UnlockedPoints + " / " + Achievements.TotalPoints + " TP";
            return unlocked.Count;
        }
    }
}
