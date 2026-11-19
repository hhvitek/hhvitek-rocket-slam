using System.Collections.Generic;
using RocketSlam.Gameplay;
using RocketSlam.Hud;
using RocketSlam.Progress;
using RocketSlam.Sound;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    public enum LevelScreen { None, Pause, PauseSettings, Results }

    /// <summary>
    /// In-level menus (<c>Assets/UI/LevelMenus.uxml</c>): the pause menu (Resume, Restart, Settings, Main menu) and the
    /// results screen (outcome and reason, score breakdown, stars with a pop-in animation, NEW BEST, telemetry graph;
    /// Retry R, Next level N / Enter when unlocked, Menu Esc). Runs after <see cref="PlayerControls"/>, which leaves all
    /// keys to this component while paused or finished.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(UIDocument))]
    public class LevelMenus : MonoBehaviour
    {
        [SerializeField] LandingSession session;
        [SerializeField] AttemptTracker tracker;
        [SerializeField] PlayerControls controls;
        [SerializeField] GameAudio audioOut;
        [SerializeField] VisualTreeAsset settingsTemplate;
        [Tooltip("The flight HUD: hidden while the results are up.")]
        [SerializeField] UIDocument hud;

        /// <summary>Seconds the results wait after an explosion (the fireball is seen first).</summary>
        public const float ResultDelayAfterExplosion = HudController.ResultDelayAfterExplosion;
        /// <summary>Keys are ignored this long after the results appear (no accidental skip).</summary>
        public const float ResultInputDelay = 0.4f;

        VisualElement root, resultsScreen, resultsBox, pauseScreen, settingsHost, nextButton;
        Label title, reason, tip, levelLine, pauseLevel, total, bestLine, newBest;
        Label legendAlt, legendFall, legendThrust;
        readonly Label[] values = new Label[3], details = new Label[3];
        readonly VisualElement[] fills = new VisualElement[3];
        readonly List<StarElement> stars = new List<StarElement>();
        TelemetryGraph graph;
        MenuList pauseList, resultsList;
        SettingsPanel settings;
        readonly MenuInput input = new MenuInput();
        readonly HintLabels hints = new HintLabels(menuContext: true);
        int openedFrame = -1;
        float shownAt, finishedAt = -1f;
        AttemptResult shown;
        /// <summary>The results animation reached its end state: nothing is written per frame any more.</summary>
        bool animationSettled;
        int shownTotal = -1;

        /// <summary>Length of the results intro animation (count-up, bars, graph, stars), s.</summary>
        const float AnimationLength = 2.6f;

        public LevelScreen ActiveScreen => Screen;
        LevelScreen Screen { get; set; }
        public bool ResultsVisible => Screen == LevelScreen.Results;
        public AttemptResult ShownResult => shown;
        public TelemetryGraph Graph => graph;
        public MenuList PauseList => pauseList;
        public MenuList ResultsList => resultsList;
        public SettingsPanel Settings => settings;
        public string TitleText => title?.text;
        public string TotalText => total?.text;
        public bool NextAvailable => shown != null && shown.nextUnlocked;
        public int FilledStars
        {
            get
            {
                int n = 0;
                foreach (StarElement s in stars) if (s.Filled) n++;
                return n;
            }
        }

        public void Setup(LandingSession s, AttemptTracker t, PlayerControls c, GameAudio a, VisualTreeAsset settingsUxml, UIDocument hudDocument)
        {
            hud = hudDocument;
            session = s;
            tracker = t;
            controls = c;
            audioOut = a;
            settingsTemplate = settingsUxml;
        }

        void Play(MenuSound s)
        {
            if (audioOut != null) audioOut.PlayMenu(s);
        }

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            resultsScreen = root.Q("results");
            resultsBox = root.Q("results-box");
            pauseScreen = root.Q("pause-menu");
            settingsHost = root.Q("settings-host");
            title = root.Q<Label>("results-title");
            reason = root.Q<Label>("results-reason");
            tip = root.Q<Label>("results-tip");
            levelLine = root.Q<Label>("results-level");
            pauseLevel = root.Q<Label>("pause-level");
            total = root.Q<Label>("score-total");
            bestLine = root.Q<Label>("best-line");
            newBest = root.Q<Label>("new-best");
            legendAlt = root.Q<Label>("legend-alt");
            legendFall = root.Q<Label>("legend-fall");
            legendThrust = root.Q<Label>("legend-thrust");
            string[] keys = { "fuel", "precision", "softness" };
            for (int i = 0; i < 3; i++)
            {
                values[i] = root.Q<Label>($"score-{keys[i]}-value");
                details[i] = root.Q<Label>($"score-{keys[i]}-detail");
                fills[i] = root.Q($"score-{keys[i]}-fill");
            }
            VisualElement starRow = root.Q("stars");
            starRow.Clear();
            stars.Clear();
            for (int i = 0; i < 3; i++)
            {
                var s = new StarElement();
                starRow.Add(s);
                stars.Add(s);
            }
            graph = new TelemetryGraph();
            root.Q("graph-host").Add(graph);

            resultsList = new MenuList(horizontal: true, Play) { onCancel = GoToMenu };
            resultsList.Add(root.Q("btn-retry"), () => { Retry(); return true; });
            nextButton = root.Q("btn-next");
            resultsList.Add(nextButton, NextLevel);
            resultsList.Add(root.Q("btn-menu"), () => { GoToMenu(); return true; });

            pauseList = new MenuList(horizontal: false, Play) { onCancel = Resume };
            pauseList.Add(root.Q("btn-resume"), () => { Resume(); return true; });
            pauseList.Add(root.Q("btn-restart"), () => { Retry(); return true; });
            pauseList.Add(root.Q("btn-pause-settings"), () => { OpenSettings(); return true; });
            pauseList.Add(root.Q("btn-main-menu"), () => { GoToMenu(); return true; });

            TemplateContainer panel = settingsTemplate.Instantiate();
            panel.style.flexGrow = 1;
            settingsHost.Add(panel);
            settings = new SettingsPanel(settingsHost, Play);
            settings.Closed += () =>
            {
                if (Screen == LevelScreen.PauseSettings) Show(LevelScreen.Pause); // Back / Esc inside the settings
            };

            if (tracker != null) tracker.ResultReady += OnResult;
            Show(LevelScreen.None);
        }

        void OnDisable()
        {
            if (tracker != null) tracker.ResultReady -= OnResult;
        }

        void OnResult(AttemptResult r)
        {
            finishedAt = Time.unscaledTime;
        }

        void Show(LevelScreen screen)
        {
            LevelScreen previous = Screen;
            Screen = screen;
            if (previous == LevelScreen.PauseSettings && screen != LevelScreen.PauseSettings) settings.Close();
            openedFrame = Time.frameCount;
            input.Reset();
            resultsScreen.EnableInClassList("open", screen == LevelScreen.Results);
            if (hud != null && hud.rootVisualElement != null)
                hud.rootVisualElement.style.visibility = screen == LevelScreen.Results ? Visibility.Hidden : Visibility.Visible;
            pauseScreen.EnableInClassList("open", screen == LevelScreen.Pause || screen == LevelScreen.PauseSettings);
            if (screen == LevelScreen.Pause)
            {
                pauseList.Select(0, playSound: false);
                pauseList.Refresh();
                if (session.Level != null) pauseLevel.text = $"LEVEL {session.Level.number}  ·  {session.Level.displayName}";
            }
            if (screen == LevelScreen.PauseSettings) settings.Open();
        }

        void Update()
        {
            if (session == null) return;
            hints.Update(root);
            bool finished = session.Evaluator.IsFinished;

            // Open / close in response to the game state.
            if (finished && tracker != null && tracker.LastResult != null && Screen != LevelScreen.Results)
            {
                if (finishedAt < 0f) finishedAt = Time.unscaledTime;
                bool exploded = tracker.LastResult.exploded;
                if (!exploded || Time.unscaledTime - finishedAt >= ResultDelayAfterExplosion) ShowResults(tracker.LastResult);
            }
            else if (!finished && Screen == LevelScreen.Results)
            {
                Show(LevelScreen.None); // restarted elsewhere
                finishedAt = -1f;
            }
            if (!finished) finishedAt = -1f;
            bool pausedNow = session.IsPaused && !session.IntroVisible && !finished;
            if (pausedNow && Screen == LevelScreen.None) Show(LevelScreen.Pause);
            else if (!pausedNow && (Screen == LevelScreen.Pause || Screen == LevelScreen.PauseSettings)) Show(LevelScreen.None);

            if (Screen == LevelScreen.Results) AnimateResults();
            Keyboard k = Keyboard.current;
            if (Screen == LevelScreen.None)
            {
                // Between the explosion and the results (ResultDelayAfterExplosion): R / Select restarts at once,
                // Esc / B / Enter / A skip the wait and show the results now.
                if (finished && tracker != null && tracker.LastResult != null)
                {
                    MenuCommand early = input.Read();
                    if (RestartPressed(k)) { Play(MenuSound.Confirm); Retry(); }
                    else if (early.cancel || early.submit) ShowResults(tracker.LastResult);
                }
                return;
            }
            if (Time.frameCount == openedFrame) return;

            MenuCommand cmd = input.Read();
            switch (Screen)
            {
                case LevelScreen.Pause:
                    if (RestartPressed(k)) { Play(MenuSound.Confirm); Retry(); return; }
                    if (cmd.start) { Play(MenuSound.Back); Resume(); return; } // Start toggles pause, never Submit
                    pauseList.Handle(cmd);
                    break;
                case LevelScreen.PauseSettings:
                    if (cmd.start) { Play(MenuSound.Back); Resume(); return; }
                    settings.Tick(cmd);
                    break;
                case LevelScreen.Results:
                    if (Time.unscaledTime - shownAt < ResultInputDelay) return;
                    if (RestartPressed(k)) { Play(MenuSound.Confirm); Retry(); return; }
                    if (k != null && k.nKey.wasPressedThisFrame)
                    {
                        Play(NextLevel() ? MenuSound.Confirm : MenuSound.Error);
                        return;
                    }
                    resultsList.Handle(cmd);
                    break;
            }
        }

        bool RestartPressed(Keyboard k)
            => (controls != null && controls.RestartPressedThisFrame) || (k != null && k.rKey.wasPressedThisFrame);

        // ---- actions ------------------------------------------------------------------------------

        public void Resume()
        {
            Show(LevelScreen.None);
            session.SetPaused(false);
        }

        public void Retry()
        {
            Show(LevelScreen.None);
            finishedAt = -1f;
            if (controls != null) controls.Restart();
            else session.Restart();
        }

        public bool NextLevel()
        {
            if (shown == null || !shown.nextUnlocked) return false;
            LevelLoader.Load(shown.nextLevel);
            return true;
        }

        public void GoToMenu() => LevelLoader.LoadMenu();

        public void OpenSettings() => Show(LevelScreen.PauseSettings);

        // ---- results ------------------------------------------------------------------------------

        void ShowResults(AttemptResult r)
        {
            shown = r;
            Show(LevelScreen.Results);
            shownAt = Time.unscaledTime;
            animationSettled = false;
            shownTotal = -1;
            bool easy = controls == null || controls.Mode == ControlMode.Easy;
            title.text = r.landed ? "LANDED!" : r.exploded ? "CRASHED" : "FAILED";
            title.EnableInClassList("good", r.landed);
            title.EnableInClassList("danger", !r.landed);
            reason.text = r.reasonText;
            string tipText = r.landed ? SuccessTip(r, easy) : HudController.Tip(session.Evaluator, easy);
            tip.text = tipText;
            tip.style.display = string.IsNullOrEmpty(tipText) ? DisplayStyle.None : DisplayStyle.Flex;
            levelLine.text = $"LEVEL {r.level}  ·  {r.levelName}";

            ScoreBreakdown s = r.score;
            SetRow(0, s.fuel, Scoring.MaxFuel, r.landed ? $"{r.fuelLeftFraction * 100f:0} % of the tank" : "");
            SetRow(1, s.precision, Scoring.MaxPrecision, r.landed ? $"{r.distanceFromCentre:0.0} m off centre" : "");
            SetRow(2, s.softness, Scoring.MaxSoftness, r.landed ? $"touchdown {r.touchdownVerticalSpeed:0.0} m/s" : "");
            total.text = "0";
            shownTotal = 0;
            newBest.EnableInClassList("visible", false);
            bestLine.text = r.bestScore > 0 ? $"Best on this level: {r.bestScore}" : "No landing on this level yet";
            foreach (StarElement st in stars)
            {
                st.Filled = false;
                st.style.scale = new Scale(Vector3.one);
            }

            graph.SetData(tracker.Telemetry.Samples);
            graph.Reveal = 0f;
            legendAlt.text = $"ALTITUDE  0-{Mathf.Ceil(graph.MaxAltitude / 100f) * 100f:#,0} m";
            legendFall.text = $"FALL SPEED  0-{Mathf.Ceil(graph.MaxFall):0} m/s";
            legendThrust.text = "THRUST  0-100 %";

            nextButton.EnableInClassList("disabled", !r.nextUnlocked);
            nextButton.style.display = r.nextLevel > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            resultsList.Select(r.nextUnlocked && r.landed ? 1 : 0, playSound: false);
            resultsList.Refresh();
            resultsBox.AddToClassList("hidden-anim");
            resultsBox.schedule.Execute(() => resultsBox.RemoveFromClassList("hidden-anim")).StartingIn(30);
        }

        static string SuccessTip(AttemptResult r, bool easyMode)
        {
            if (r.nextLevel == 0) return r.score.stars >= 3 ? "Perfect finish of the campaign!" : "Campaign complete! Go for three stars on every level.";
            if (r.record.unlockedLevel > 0) return $"Level {r.record.unlockedLevel} unlocked!";
            if (r.score.stars < 3)
                return easyMode ? $"For 3 stars: fire a moment after FIRE NOW, then hold {InputHints.Fire} all the way down."
                                : "For 3 stars: burn later and harder, touch down softer and closer to the centre.";
            return "Excellent landing!";
        }

        void SetRow(int i, int points, int max, string detail)
        {
            values[i].text = $"{points}";
            details[i].text = detail;
            fills[i].style.width = Length.Percent(0f);
            fills[i].userData = max > 0 ? (float)points / max : 0f;
        }

        /// <summary>Count-up of the score, bars filling, stars popping in one after the other, graph drawing left to right.</summary>
        void AnimateResults()
        {
            if (shown == null || animationSettled) return;
            float t = Time.unscaledTime - shownAt;
            if (t >= AnimationLength) animationSettled = true; // this last pass writes the final state
            float k = Mathf.Clamp01((t - 0.2f) / 1.0f);
            float ease = 1f - (1f - k) * (1f - k);
            int totalNow = Mathf.RoundToInt(shown.score.total * ease);
            if (totalNow != shownTotal)
            {
                shownTotal = totalNow;
                total.text = totalNow.ToString();
            }
            for (int i = 0; i < 3; i++)
                fills[i].style.width = Length.Percent(100f * ease * (fills[i].userData is float f ? f : 0f));
            graph.Reveal = Mathf.Clamp01((t - 0.1f) / 1.4f);
            for (int i = 0; i < stars.Count; i++)
            {
                bool on = i < shown.score.stars;
                float st = t - (1.2f + 0.3f * i);
                if (!on || st < 0f)
                {
                    stars[i].Filled = false;
                    stars[i].style.scale = new Scale(Vector3.one);
                    continue;
                }
                stars[i].Filled = true;
                // pop: 0 -> 1.35 -> 1 over 0.35 s
                float p = Mathf.Clamp01(st / 0.35f);
                float scale = p < 0.6f ? Mathf.Lerp(0.2f, 1.35f, p / 0.6f) : Mathf.Lerp(1.35f, 1f, (p - 0.6f) / 0.4f);
                stars[i].style.scale = new Scale(new Vector3(scale, scale, 1f));
            }
            bool best = shown.landed && shown.record.newBestScore && t > 1.2f + 0.3f * shown.score.stars;
            newBest.EnableInClassList("visible", best);
        }

        /// <summary>Skip the intro animation (screenshots, tests).</summary>
        public void FinishAnimation()
        {
            shownAt = Time.unscaledTime - 10f;
            animationSettled = false;
        }

        /// <summary>True once the results animation has finished and stopped updating the UI.</summary>
        public bool AnimationSettled => animationSettled;
    }
}
