using System.Collections;
using NUnit.Framework;
using RocketSlam.Effects;
using RocketSlam.Gameplay;
using RocketSlam.Menus;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>
    /// M5 game loop from the main menu: Play → level 1 → landing → results (score, stars, telemetry) → Next → level 2 →
    /// pause → main menu; level select locks; settings reach the mixer and the save. Uses an in-memory save.
    /// (Named to sort with the other input fixtures, before the plain fixtures.)
    /// </summary>
    public class CampaignLoopTests : InputTestFixture
    {
        Keyboard keyboard;

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            SettingsApplier.ApplyQuality(QualityPreset.Medium);
            SaveGame.UseMemory(); // leave nothing pointing at a file
        }

        IEnumerator LoadMenu(SaveData save = null)
        {
            SaveGame.UseMemory(save ?? new SaveData());
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync(LevelLoader.MenuScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        IEnumerator Key(KeyControl key)
        {
            PressAndRelease(key);
            yield return null;
            yield return null;
        }

        static IEnumerator WaitForScene(string name, float timeout = 30f)
        {
            float t0 = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
            yield return null;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator PlayFromMenuLandResultsNextAndBackToMenu()
        {
            yield return LoadMenu();
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.IsNotNull(menu, "MainMenu scene has the menu");
            Assert.AreEqual(0, SceneManager.GetActiveScene().buildIndex, "the menu is the first scene of the build");
            Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen);
            Assert.AreEqual(0, menu.MainList.Selected, "Play is selected");
            Assert.AreEqual(1, menu.ContinueLevel);

            yield return Key(keyboard.enterKey);
            yield return WaitForScene("Level01");
            var session = Object.FindAnyObjectByType<LandingSession>();
            var tracker = Object.FindAnyObjectByType<AttemptTracker>();
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            Assert.IsTrue(session.IntroVisible, "the level starts on its start screen");

            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            var autopilot = Object.FindAnyObjectByType<SuicideBurnAutopilot>(FindObjectsInactive.Include);
            autopilot.Bind(session.Rocket, session.TargetPosition);
            autopilot.enabled = true;
            Time.timeScale = 6f;
            float start = Time.time;
            while (!menus.ResultsVisible && Time.time - start < 160f) yield return null;
            Time.timeScale = 1f;
            Assert.IsTrue(menus.ResultsVisible, "results screen appears");
            Assert.AreEqual(LandingState.Success, session.Evaluator.State, session.Evaluator.Describe());

            AttemptResult r = tracker.LastResult;
            Assert.IsNotNull(r);
            Assert.IsTrue(r.landed);
            Assert.Greater(r.score.total, 500);
            Assert.AreEqual(r.score.fuel + r.score.precision + r.score.softness, r.score.total);
            Assert.GreaterOrEqual(r.score.stars, 1);
            Assert.IsTrue(r.record.newBestScore, "first landing is a new best");
            Assert.AreEqual(2, r.record.unlockedLevel);
            Assert.IsTrue(r.nextUnlocked);
            Assert.Greater(tracker.Telemetry.Count, 100, "~10 Hz over a ~40 s flight");
            Assert.Greater(tracker.Telemetry.MaxAltitude(), 2500f);
            Assert.Greater(tracker.Telemetry.MaxFallSpeed(), 150f);
            Assert.AreEqual(tracker.Telemetry.Count, menus.Graph.SampleCount, "the graph shows the recorded telemetry");
            Assert.AreEqual("LANDED!", menus.TitleText);
            Assert.IsTrue(SaveGame.Current.IsLanded(1));
            Assert.AreEqual(r.score.total, SaveGame.Current.Level(1).bestScore);

            menus.FinishAnimation();
            yield return null;
            yield return null;
            Assert.AreEqual(r.score.stars, menus.FilledStars, "stars shown");
            Assert.AreEqual(r.score.total.ToString(), menus.TotalText);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(menus.AnimationSettled, "the results stop updating once the animation is done");

            yield return Key(keyboard.nKey);
            yield return WaitForScene("Level02");
            session = Object.FindAnyObjectByType<LandingSession>();
            menus = Object.FindAnyObjectByType<LevelMenus>();
            Assert.IsTrue(session.IntroVisible);
            yield return Key(keyboard.spaceKey);
            Assert.IsFalse(session.IntroVisible);
            yield return Key(keyboard.escapeKey);
            Assert.IsTrue(session.IsPaused);
            Assert.AreEqual(LevelScreen.Pause, menus.ActiveScreen);
            Assert.AreEqual(0, menus.PauseList.Selected, "Resume selected");
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.downArrowKey);
            Assert.AreEqual(3, menus.PauseList.Selected, "Main menu selected");
            yield return Key(keyboard.enterKey);
            yield return WaitForScene(LevelLoader.MenuScene);
            Assert.AreEqual(1f, Time.timeScale, "leaving a paused level unpauses time");
            menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.AreEqual(2, menu.ContinueLevel, "Play now continues at level 2");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PauseMenuResumeAndRestart()
        {
            SaveGame.UseMemory();
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            var session = Object.FindAnyObjectByType<LandingSession>();
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            yield return Key(keyboard.spaceKey);
            for (int i = 0; i < 30; i++) yield return null;
            float altBefore = session.Rocket.Altitude;
            yield return Key(keyboard.escapeKey);
            Assert.IsTrue(session.IsPaused);
            Assert.AreEqual(LevelScreen.Pause, menus.ActiveScreen);
            yield return Key(keyboard.escapeKey);
            Assert.IsFalse(session.IsPaused, "Esc resumes");
            Assert.AreEqual(LevelScreen.None, menus.ActiveScreen);
            yield return Key(keyboard.escapeKey);
            Assert.IsTrue(session.IsPaused, "and pauses again");
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.enterKey);
            Assert.IsFalse(session.IsPaused, "Restart unpauses");
            Assert.Greater(session.Rocket.Altitude, altBefore, "Restart puts the stage back at the start altitude");

            // Settings from pause, Esc back to the pause menu
            yield return Key(keyboard.escapeKey);
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.enterKey);
            Assert.AreEqual(LevelScreen.PauseSettings, menus.ActiveScreen);
            Assert.IsTrue(menus.Settings.Visible);
            yield return Key(keyboard.escapeKey);
            Assert.AreEqual(LevelScreen.Pause, menus.ActiveScreen);
            Assert.IsFalse(menus.Settings.Visible);
            Assert.IsTrue(session.IsPaused);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator LevelSelectRespectsLocks()
        {
            var save = new SaveData();
            save.Record(1, LevelLoader.LevelCount, true, 700, 2, 1.5f);
            yield return LoadMenu(save);
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            yield return Key(keyboard.downArrowKey);
            Assert.AreEqual(1, menu.MainList.Selected);
            yield return Key(keyboard.enterKey);
            Assert.AreEqual(MainMenuScreen.Levels, menu.ActiveScreen);
            Assert.AreEqual(1, menu.LevelList.Selected, "level select opens on the level Play would continue (2)");

            yield return Key(keyboard.rightArrowKey);
            Assert.AreEqual(2, menu.LevelList.Selected);
            yield return Key(keyboard.enterKey);
            Assert.AreEqual(LevelLoader.MenuScene, SceneManager.GetActiveScene().name, "level 3 is locked");
            Assert.AreEqual(0, menu.RequestedLevel);
            Assert.IsFalse(menu.StartLevel(5));

            yield return Key(keyboard.escapeKey);
            Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen, "Esc goes back");
            yield return Key(keyboard.upArrowKey);
            Assert.AreEqual(0, menu.MainList.Selected);
            yield return Key(keyboard.enterKey); // Play
            yield return WaitForScene("Level02");

            // On a level's start screen the number keys only reach unlocked levels.
            var session = Object.FindAnyObjectByType<LandingSession>();
            yield return Key(keyboard.digit4Key);
            Assert.AreEqual("Level02", SceneManager.GetActiveScene().name, "key 4: level 4 is locked");
            Assert.IsTrue(session.IntroVisible, "a locked level key does not start the flight");
            yield return Key(keyboard.digit1Key);
            yield return WaitForScene("Level01");
            yield return Key(keyboard.escapeKey);
            yield return WaitForScene(LevelLoader.MenuScene);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SettingsReachTheMixerQualityAndSave()
        {
            yield return LoadMenu();
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.downArrowKey);
            yield return Key(keyboard.enterKey);
            Assert.AreEqual(MainMenuScreen.Settings, menu.ActiveScreen);
            Assert.IsTrue(menu.Settings.Visible);
            Assert.IsNotNull(SettingsApplier.Mixer, "the menu registered the mixer");

            float before = SaveGame.Settings.masterVolume;
            for (int i = 0; i < 4; i++) yield return Key(keyboard.leftArrowKey); // volume row: -5 % per step
            float v = SaveGame.Settings.masterVolume;
            Assert.AreEqual(before - 0.2f, v, 1e-3f);
            Assert.IsTrue(SettingsApplier.Mixer.GetFloat(SettingsApplier.MasterVolumeParameter, out float db));
            Assert.AreEqual(EffectsMath.LinearToDecibels(v), db, 0.05f, "the volume reached the mixer");
            Assert.AreEqual(v * 100f, menu.Settings.VolumeSlider.value, 1e-3f, "slider follows");

            menu.Settings.VolumeSlider.value = 25f; // mouse drag path
            yield return null;
            Assert.AreEqual(0.25f, SaveGame.Settings.masterVolume, 1e-3f);
            SettingsApplier.Mixer.GetFloat(SettingsApplier.MasterVolumeParameter, out db);
            Assert.AreEqual(EffectsMath.LinearToDecibels(0.25f), db, 0.05f);

            yield return Key(keyboard.downArrowKey); // quality
            yield return Key(keyboard.leftArrowKey);
            Assert.AreEqual((int)QualityPreset.Low, SaveGame.Settings.quality);
            Assert.AreEqual(QualityPreset.Low, SettingsApplier.CurrentQuality(), "the quality level switched");
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Assert.Less(urp.renderScale, 1f, "Low renders at a lower resolution");
            yield return Key(keyboard.rightArrowKey);
            yield return Key(keyboard.rightArrowKey);
            Assert.AreEqual(QualityPreset.High, SettingsApplier.CurrentQuality());
            urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Assert.AreEqual(4, urp.msaaSampleCount, "High uses 4x MSAA");

            yield return Key(keyboard.downArrowKey); // easy assist
            yield return Key(keyboard.enterKey);
            Assert.IsFalse(SaveGame.Settings.easyAssist);
            yield return Key(keyboard.downArrowKey); // camera
            yield return Key(keyboard.rightArrowKey);
            Assert.AreEqual(1, SaveGame.Settings.cameraView);

            yield return Key(keyboard.escapeKey);
            Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen);
            Assert.IsFalse(menu.Settings.Visible);

            // The settings carry into a level: Manual mode, engine camera.
            yield return Key(keyboard.upArrowKey);
            yield return Key(keyboard.upArrowKey);
            yield return Key(keyboard.enterKey);
            yield return WaitForScene("Level01");
            Assert.AreEqual(ControlMode.Manual, Object.FindAnyObjectByType<PlayerControls>().Mode);
            Assert.AreEqual(RocketSlam.Cameras.CameraView.Engine, Object.FindAnyObjectByType<RocketSlam.Cameras.CameraDirector>().View);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator CrashShowsFailureWithZeroScore()
        {
            SaveGame.UseMemory();
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            var session = Object.FindAnyObjectByType<LandingSession>();
            var tracker = Object.FindAnyObjectByType<AttemptTracker>();
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            yield return Key(keyboard.spaceKey);
            Time.timeScale = 6f;
            float start = Time.time;
            while (!menus.ResultsVisible && Time.time - start < 60f) yield return null;
            Time.timeScale = 1f;
            Assert.IsTrue(menus.ResultsVisible);
            Assert.AreEqual("CRASHED", menus.TitleText);
            Assert.AreEqual(0, tracker.LastResult.score.total);
            Assert.AreEqual(0, tracker.LastResult.score.stars);
            Assert.IsFalse(tracker.LastResult.nextUnlocked, "a crash on level 1 unlocks nothing");
            Assert.AreEqual(1, SaveGame.Current.Level(1).attempts);
            Assert.IsFalse(SaveGame.Current.IsLanded(1));
            Assert.Greater(tracker.Telemetry.Count, 20);

            menus.FinishAnimation(); // past the input delay
            yield return null;
            yield return Key(keyboard.nKey);
            Assert.AreEqual("Level01", SceneManager.GetActiveScene().name, "N does nothing while the next level is locked");
            yield return Key(keyboard.rKey);
            Assert.IsFalse(menus.ResultsVisible, "R retries");
            Assert.AreEqual(LandingState.Flying, session.Evaluator.State);
            Assert.Less(tracker.Telemetry.Count, 5, "telemetry restarts with the attempt");
        }

        /// <summary>R restarts at once after an explosion (no 1.5 s dead time); Esc skips straight to the results,
        /// whose telemetry ends at the impact speed (not a fall speed of 0).</summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator KeysWorkRightAfterAnExplosion()
        {
            SaveGame.UseMemory();
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            var session = Object.FindAnyObjectByType<LandingSession>();
            var tracker = Object.FindAnyObjectByType<AttemptTracker>();
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            yield return Key(keyboard.spaceKey);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Time.timeScale = 6f;
                float start = Time.time;
                while (!session.Evaluator.IsFinished && Time.time - start < 60f) yield return null;
                Time.timeScale = 1f;
                Assert.IsTrue(session.Rocket.Exploded);
                Assert.IsFalse(menus.ResultsVisible, "results wait for the fireball");
                if (attempt == 0)
                {
                    yield return Key(keyboard.rKey);
                    Assert.AreEqual(LandingState.Flying, session.Evaluator.State, "R restarted during the explosion delay");
                    Assert.IsFalse(menus.ResultsVisible);
                    Assert.IsFalse(session.Rocket.Exploded);
                }
                else
                {
                    yield return Key(keyboard.escapeKey);
                    Assert.IsTrue(menus.ResultsVisible, "Esc shows the results at once");
                    Assert.AreEqual("CRASHED", menus.TitleText);
                    TelemetrySample last = tracker.Telemetry[tracker.Telemetry.Count - 1];
                    Assert.AreEqual(0f, last.altitude);
                    Assert.Less(last.verticalSpeed, -20f, "the graph ends at the impact speed");
                    Assert.AreEqual(-session.Evaluator.TouchdownVerticalSpeed, last.verticalSpeed, 1e-3f);
                }
            }
        }

        /// <summary>Settings changed in the menu reach the save file on disk (a temporary file, never the editor's).</summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator SettingsAreWrittenToTheSaveFile()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rocketslam_play_" + System.Guid.NewGuid().ToString("N"));
            string file = System.IO.Path.Combine(dir, "save.json");
            try
            {
                SaveGame.UseFile(file);
                Assert.AreEqual(file, SaveGame.FilePath);
                Assert.IsFalse(System.IO.File.Exists(file), "starts without a file");
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(LevelLoader.MenuScene, LoadSceneMode.Single);
                yield return null;
                yield return null;
                var menu = Object.FindAnyObjectByType<MainMenuController>();
                yield return Key(keyboard.downArrowKey);
                yield return Key(keyboard.downArrowKey);
                yield return Key(keyboard.enterKey);
                Assert.AreEqual(MainMenuScreen.Settings, menu.ActiveScreen);
                for (int i = 0; i < 2; i++) yield return Key(keyboard.leftArrowKey); // volume 80 -> 70 %
                yield return Key(keyboard.downArrowKey);
                yield return Key(keyboard.leftArrowKey); // quality Medium -> Low
                yield return Key(keyboard.downArrowKey);
                yield return Key(keyboard.enterKey); // Easy assist off
                yield return Key(keyboard.escapeKey);
                Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen);

                Assert.IsTrue(System.IO.File.Exists(file), "closing the settings wrote the save file");
                SaveData onDisk = SaveStore.Load(file, LevelLoader.LevelCount, out SaveLoadStatus status);
                Assert.AreEqual(SaveLoadStatus.Loaded, status);
                Assert.AreEqual(0.7f, onDisk.settings.masterVolume, 1e-3f);
                Assert.AreEqual((int)QualityPreset.Low, onDisk.settings.quality);
                Assert.IsFalse(onDisk.settings.easyAssist);

                // A fresh load of the same file (next game start) sees the same settings.
                SaveGame.UseFile(file);
                Assert.AreEqual(0.7f, SaveGame.Settings.masterVolume, 1e-3f);
                Assert.IsFalse(SaveGame.Settings.easyAssist);
            }
            finally
            {
                SaveGame.UseMemory();
                if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
            }
        }
    }
}
