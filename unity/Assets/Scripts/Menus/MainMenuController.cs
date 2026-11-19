using System.Collections.Generic;
using RocketSlam.Gameplay;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using RocketSlam.Sound;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Menus
{
    public enum MainMenuScreen { Main, Levels, Settings }

    /// <summary>
    /// The main menu scene (<c>Assets/UI/MainMenu.uxml</c>): Play (continues at the first unfinished level), Level Select
    /// (5 cards with conditions, fuel, best score and stars; locked until the previous level is landed), Settings and
    /// Quit. Keyboard, gamepad and mouse via <see cref="MenuList"/>.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] VisualTreeAsset settingsTemplate;
        [SerializeField] LevelConfig[] levels;
        [SerializeField] MenuAudio audioOut;

        VisualElement root, mainScreen, levelsScreen;
        Label playSub, progressText;
        MenuList mainList, levelList;
        SettingsPanel settings;
        readonly MenuInput input = new MenuInput();
        readonly HintLabels hints = new HintLabels(menuContext: true);
        readonly List<VisualElement> cards = new List<VisualElement>();

        public MainMenuScreen ActiveScreen { get; private set; }
        public MenuList MainList => mainList;
        public MenuList LevelList => levelList;
        public SettingsPanel Settings => settings;
        /// <summary>The level Play starts.</summary>
        public int ContinueLevel => SaveGame.Current.ContinueLevel(LevelLoader.LevelCount);
        /// <summary>Last level a menu action asked to load (tests), 0 = none.</summary>
        public int RequestedLevel { get; private set; }

        public void Setup(VisualTreeAsset settingsUxml, LevelConfig[] levelConfigs, MenuAudio menuAudio)
        {
            settingsTemplate = settingsUxml;
            levels = levelConfigs;
            audioOut = menuAudio;
        }

        void Play(MenuSound s)
        {
            if (audioOut != null) audioOut.Play(s);
        }

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            mainScreen = root.Q("screen-main");
            levelsScreen = root.Q("screen-levels");
            playSub = root.Q<Label>("play-sub");
            progressText = root.Q<Label>("progress-text");
            var star = new StarElement { Filled = true };
            star.style.flexGrow = 1;
            root.Q("progress-star").Add(star);

            mainList = new MenuList(horizontal: false, Play) { onCancel = () => mainList.Select(3, playSound: false) };
            mainList.Add(root.Q("btn-play"), () => StartLevel(ContinueLevel));
            mainList.Add(root.Q("btn-levels"), () => { Show(MainMenuScreen.Levels); return true; });
            mainList.Add(root.Q("btn-settings"), () => { Show(MainMenuScreen.Settings); return true; });
            mainList.Add(root.Q("btn-quit"), Quit);

            BuildCards();

            VisualElement host = root.Q("settings-host");
            TemplateContainer panel = settingsTemplate.Instantiate();
            panel.style.flexGrow = 1;
            host.Add(panel);
            settings = new SettingsPanel(host, Play);
            settings.Closed += () => Show(MainMenuScreen.Main);

            Show(MainMenuScreen.Main);
        }

        void BuildCards()
        {
            VisualElement holder = root.Q("level-cards");
            holder.Clear();
            cards.Clear();
            levelList = new MenuList(horizontal: true, Play) { onCancel = () => Show(MainMenuScreen.Main) };
            for (int n = 1; n <= LevelLoader.LevelCount; n++)
            {
                int level = n;
                LevelConfig cfg = levels != null && n - 1 < levels.Length ? levels[n - 1] : null;
                var card = new VisualElement { name = $"card-{n}" };
                card.AddToClassList("level-card");
                card.Add(Text($"LEVEL {n}", "card-number"));
                card.Add(Text(cfg != null ? cfg.displayName : $"Level {n}", "card-name"));
                card.Add(Text(cfg != null ? cfg.SiteName.ToUpperInvariant() : "", "card-site"));
                card.Add(Text(cfg != null ? cfg.conditions : "", "card-conditions"));
                card.Add(Text(cfg != null ? $"Fuel  {cfg.fuelKg / 1000f:0} t" : "", "card-fuel"));
                var divider = new VisualElement();
                divider.AddToClassList("card-divider");
                card.Add(divider);
                card.Add(Text("BEST SCORE", "card-best-label"));
                card.Add(Text("-", "card-best", "best"));
                var stars = new VisualElement { name = "stars" };
                stars.AddToClassList("card-stars");
                for (int i = 0; i < 3; i++) stars.Add(new StarElement());
                card.Add(stars);
                var lockLayer = new VisualElement();
                lockLayer.AddToClassList("card-lock");
                var lockIcon = new VisualElement();
                lockIcon.AddToClassList("lock-icon");
                var shackle = new VisualElement();
                shackle.AddToClassList("lock-shackle");
                lockIcon.Add(shackle);
                lockLayer.Add(lockIcon);
                lockLayer.Add(Text("LOCKED", "lock-title"));
                lockLayer.Add(Text($"Land level {n - 1} to unlock", "lock-note"));
                card.Add(lockLayer);
                holder.Add(card);
                cards.Add(card);
                levelList.Add(card, () => StartLevel(level));
            }
        }

        static Label Text(string text, string cls, string name = null)
        {
            var l = new Label(text) { name = name };
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        void RefreshProgress()
        {
            SaveData save = SaveGame.Current;
            int next = ContinueLevel;
            LevelConfig cfg = levels != null && next - 1 < levels.Length ? levels[next - 1] : null;
            bool any = save.IsLanded(1);
            playSub.text = (any ? "Continue:  " : "Start:  ") + $"Level {next}" + (cfg != null ? $"  ·  {cfg.displayName}" : "");
            progressText.text = $"{save.TotalStars()} / {LevelLoader.LevelCount * 3} stars";
            for (int i = 0; i < cards.Count; i++)
            {
                int n = i + 1;
                VisualElement card = cards[i];
                LevelRecord rec = save.Find(n);
                card.EnableInClassList("locked", !save.IsUnlocked(n));
                card.Q<Label>("best").text = rec != null && rec.landed ? $"{rec.bestScore}" : "-";
                int s = rec?.stars ?? 0;
                int k = 0;
                foreach (StarElement star in card.Q("stars").Children())
                    star.Filled = k++ < s;
            }
        }

        public void Show(MainMenuScreen screen)
        {
            ActiveScreen = screen;
            input.Reset();
            RefreshProgress();
            mainScreen.EnableInClassList("open", screen == MainMenuScreen.Main || screen == MainMenuScreen.Settings);
            levelsScreen.EnableInClassList("open", screen == MainMenuScreen.Levels);
            if (screen == MainMenuScreen.Settings) settings.Open();
            if (screen == MainMenuScreen.Levels) levelList.Select(ContinueLevel - 1, playSound: false);
            mainList.Refresh();
            levelList.Refresh();
        }

        /// <summary>Load level n if it is unlocked (false = locked, nothing happens).</summary>
        public bool StartLevel(int n)
        {
            if (!SaveGame.Current.IsUnlocked(n)) return false;
            RequestedLevel = n;
            LevelLoader.Load(n);
            return true;
        }

        bool Quit()
        {
            Debug.Log("[MainMenu] quit");
            Application.Quit();
            return true;
        }

        void Update()
        {
            hints.Update(root);
            MenuCommand cmd = input.Read();
            switch (ActiveScreen)
            {
                case MainMenuScreen.Main: mainList.Handle(cmd); break;
                case MainMenuScreen.Levels: levelList.Handle(cmd); break;
                case MainMenuScreen.Settings: settings.Tick(cmd); break;
            }
        }
    }
}
