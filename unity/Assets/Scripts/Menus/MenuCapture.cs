using System;
using System.Collections;
using System.IO;
using RocketSlam.Gameplay;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RocketSlam.Menus
{
    /// <summary>
    /// Automated screenshots of the whole game loop for a built player:
    /// <c>RocketSlam.x86_64 -autocapture-m5 &lt;folder&gt;</c>. Uses an in-memory save seeded with some progress, then:
    /// main menu, level select, settings, level 1 (autopilot) with the pause menu on the way down, results of the
    /// landing, a retry without input (crash) and its results. Writes <c>m5_*.png</c> and quits.
    /// </summary>
    public class MenuCapture : MonoBehaviour
    {
        string folder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autocapture-m5");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("MenuCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<MenuCapture>().folder = Path.GetFullPath(args[i + 1]);
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            Application.runInBackground = true;
            var seed = new SaveData();
            seed.Record(1, LevelLoader.LevelCount, true, 742, 2, 1.5f);
            seed.Record(2, LevelLoader.LevelCount, true, 655, 2, 1.4f);
            seed.Record(2, LevelLoader.LevelCount, false, 0, 0, 0f);
            SaveGame.UseMemory(seed);
            Debug.Log($"[MenuCapture] folder={folder}");

            while (SceneManager.GetActiveScene().name != LevelLoader.MenuScene) yield return null;
            yield return Wait(5f);
            var menu = FindAnyObjectByType<MainMenuController>();
            menu.Show(MainMenuScreen.Main); // refresh with the seeded save
            yield return Wait(0.5f);
            yield return Shot("m5_1_main_menu");

            menu.Show(MainMenuScreen.Levels);
            menu.LevelList.Select(2, playSound: false);
            yield return Wait(0.8f);
            yield return Shot("m5_2_level_select");

            menu.Show(MainMenuScreen.Settings);
            menu.Settings.List.Select(1, playSound: false);
            yield return Wait(0.8f);
            yield return Shot("m5_3_settings");
            menu.Settings.Close();

            menu.StartLevel(1);
            while (SceneManager.GetActiveScene().name != LevelLoader.SceneName(1)) yield return null;
            yield return Wait(4f);
            var session = FindAnyObjectByType<LandingSession>();
            var controls = FindAnyObjectByType<PlayerControls>();
            var menus = FindAnyObjectByType<LevelMenus>();
            var autopilot = FindAnyObjectByType<SuicideBurnAutopilot>(FindObjectsInactive.Include);
            controls.enabled = false; // dismisses the start screen
            autopilot.Bind(session.Rocket, session.TargetPosition);
            autopilot.enabled = true;

            while (session.Rocket.Altitude > 1200f) yield return null;
            session.SetPaused(true);
            yield return Wait(0.6f);
            yield return Shot("m5_4_pause");
            menus.Resume();

            while (!menus.ResultsVisible) yield return null;
            yield return Wait(3.2f);
            yield return Shot("m5_5_results_success");
            Debug.Log($"[MenuCapture] success: {menus.TitleText} score {menus.TotalText} stars {menus.FilledStars} graph {menus.Graph.SampleCount}");

            autopilot.enabled = false;
            menus.Retry();
            float t0 = Time.unscaledTime;
            while (!menus.ResultsVisible && Time.unscaledTime - t0 < 60f) yield return null;
            yield return Wait(2.2f);
            yield return Shot("m5_6_results_failure");
            Debug.Log($"[MenuCapture] failure: {menus.TitleText} score {menus.TotalText}");
            yield return Wait(0.5f);
            Application.Quit(0);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        IEnumerator Shot(string name)
        {
            string path = Path.Combine(folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[MenuCapture] {path}");
            yield return null;
            yield return null;
        }

        void Update()
        {
            if (Time.unscaledTime > 300f) Application.Quit(2);
        }
    }
}
