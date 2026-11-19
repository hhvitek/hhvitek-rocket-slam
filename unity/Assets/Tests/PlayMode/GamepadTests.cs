using System.Collections;
using NUnit.Framework;
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
    /// The whole game with a (virtual) gamepad only, no keyboard: menu -> level -> fly and land in Easy with the right
    /// trigger -> results -> next level -> pause (Start) / resume (Start, B) -> main menu -> level select and back.
    /// Also Manual mode on the gamepad: A engine, triggers throttle, shoulders full/min, Y legs, X mode.
    /// </summary>
    public class GamepadTests : InputTestFixture
    {
        Gamepad pad;

        [SetUp]
        public void UseMemorySave() => SaveGame.UseMemory();

        [TearDown]
        public void Restore()
        {
            Time.timeScale = 1f;
            InputHints.Set(false);
            SaveGame.UseMemory();
        }

        IEnumerator Button(ButtonControl b)
        {
            PressAndRelease(b);
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
        public IEnumerator GamepadOnlyMenuFlyLandNextPauseAndBack()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync(LevelLoader.MenuScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuController>();
            Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen);

            // Level select and back with B.
            yield return Button(pad.dpad.down);
            Assert.AreEqual(1, menu.MainList.Selected);
            Assert.IsTrue(InputHints.UsingGamepad, "the gamepad is now the active device for hints");
            yield return Button(pad.buttonSouth);
            Assert.AreEqual(MainMenuScreen.Levels, menu.ActiveScreen);
            yield return Button(pad.buttonEast);
            Assert.AreEqual(MainMenuScreen.Main, menu.ActiveScreen, "B goes back");
            yield return Button(pad.dpad.up);
            Assert.AreEqual(0, menu.MainList.Selected);

            // Play -> level 1 start screen, A starts.
            yield return Button(pad.buttonSouth);
            yield return WaitForScene("Level01");
            var session = Object.FindAnyObjectByType<LandingSession>();
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            var controls = Object.FindAnyObjectByType<PlayerControls>();
            RocketController rocket = session.Rocket;
            Assert.IsTrue(session.IntroVisible);
            Assert.AreEqual(ControlMode.Easy, controls.Mode);
            yield return Button(pad.buttonSouth);
            Assert.IsFalse(session.IntroVisible, "A starts the flight");

            // Fly like a naive player, but with the right trigger.
            EasyAssist assist = controls.Assist;
            Time.timeScale = 4f;
            float fireNowAt = -1f, start = Time.time;
            bool held = false, burned = false;
            while (!session.Evaluator.IsFinished && Time.time - start < 200f)
            {
                if (assist.Cue.kind == CueKind.FireNow && fireNowAt < 0f) fireNowAt = Time.time;
                float fall = rocket.RelativeVelocity.y;
                bool want;
                if (fireNowAt < 0f || Time.time - fireNowAt < 0.5f) want = false;
                else if (rocket.Altitude > 30f) want = true;
                else want = fall < -2f || (held && fall < -1.5f);
                if (want != held)
                {
                    held = want;
                    if (want) Press(pad.rightTrigger);
                    else Release(pad.rightTrigger);
                }
                burned |= rocket.EngineOn;
                yield return null;
            }
            if (held) Release(pad.rightTrigger);
            Time.timeScale = 1f;
            Assert.IsTrue(burned, "the right trigger fires the engine");
            Assert.AreEqual(LandingState.Success, session.Evaluator.State, session.Evaluator.Describe());

            // Results -> Next with A.
            for (float t0 = Time.unscaledTime; !menus.ResultsVisible && Time.unscaledTime - t0 < 10f;) yield return null;
            Assert.IsTrue(menus.ResultsVisible);
            menus.FinishAnimation();
            yield return null;
            Assert.AreEqual(1, menus.ResultsList.Selected, "Next selected");
            yield return Button(pad.buttonSouth);
            yield return WaitForScene("Level02");

            session = Object.FindAnyObjectByType<LandingSession>();
            menus = Object.FindAnyObjectByType<LevelMenus>();
            yield return Button(pad.buttonSouth);
            Assert.IsFalse(session.IntroVisible);
            for (int i = 0; i < 20; i++) yield return null;

            // Start pauses; Start resumes even with another item selected (Start is never Submit here).
            yield return Button(pad.startButton);
            Assert.IsTrue(session.IsPaused, "Start pauses");
            Assert.AreEqual(LevelScreen.Pause, menus.ActiveScreen);
            yield return Button(pad.dpad.down);
            Assert.AreEqual(1, menus.PauseList.Selected, "Restart selected");
            float altBefore = session.Rocket.Altitude;
            yield return Button(pad.startButton);
            Assert.IsFalse(session.IsPaused, "Start resumes");
            Assert.AreEqual(LevelScreen.None, menus.ActiveScreen);
            Assert.LessOrEqual(session.Rocket.Altitude, altBefore + 0.01f, "it resumed instead of restarting");

            // B resumes too.
            yield return Button(pad.startButton);
            Assert.IsTrue(session.IsPaused);
            yield return Button(pad.buttonEast);
            Assert.IsFalse(session.IsPaused, "B resumes");

            // Select restarts while flying.
            for (int i = 0; i < 20; i++) yield return null;
            float altFlying = session.Rocket.Altitude;
            yield return Button(pad.selectButton);
            Assert.Greater(session.Rocket.Altitude, altFlying, "Select restarts the attempt");

            // Pause -> Main menu with the d-pad and A.
            yield return Button(pad.startButton);
            Assert.IsTrue(session.IsPaused);
            for (int i = 0; i < 3; i++) yield return Button(pad.dpad.down);
            Assert.AreEqual(3, menus.PauseList.Selected);
            yield return Button(pad.buttonSouth);
            yield return WaitForScene(LevelLoader.MenuScene);
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ManualModeOnTheGamepad()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            var session = Object.FindAnyObjectByType<LandingSession>();
            var controls = Object.FindAnyObjectByType<PlayerControls>();
            RocketController rocket = session.Rocket;
            yield return Button(pad.buttonWest);
            Assert.AreEqual(ControlMode.Manual, controls.Mode, "X on the start screen switches to Manual");
            yield return Button(pad.buttonSouth);
            Assert.IsFalse(session.IntroVisible);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(rocket.EngineOn, "the A that started the level does not fire the engine");

            yield return Button(pad.buttonSouth);
            Assert.IsTrue(rocket.EngineOn, "A toggles the engine");
            yield return Button(pad.rightShoulder);
            Assert.AreEqual(1f, rocket.Throttle, 1e-3f, "RB = full throttle");
            yield return Button(pad.leftShoulder);
            Assert.AreEqual(rocket.Config.minThrottle, rocket.Throttle, 1e-3f, "LB = minimum throttle");
            Press(pad.rightTrigger);
            for (float t0 = Time.time; Time.time - t0 < 0.5f;) yield return null;
            Release(pad.rightTrigger);
            yield return null;
            float raised = rocket.Throttle;
            Assert.Greater(raised, rocket.Config.minThrottle + 0.02f, "RT raises the throttle");
            Press(pad.leftTrigger);
            for (float t0 = Time.time; Time.time - t0 < 0.3f;) yield return null;
            Release(pad.leftTrigger);
            yield return null;
            Assert.Less(rocket.Throttle, raised, "LT lowers it");

            Set(pad.leftStick, new Vector2(1f, 0f));
            for (int i = 0; i < 5; i++) yield return null;
            Assert.Greater(controls.GimbalInput.magnitude, 0.5f, "left stick steers");
            Set(pad.leftStick, Vector2.zero);

            Assert.AreEqual(LegsState.Stowed, rocket.Legs);
            yield return Button(pad.buttonNorth);
            Assert.AreNotEqual(LegsState.Stowed, rocket.Legs, "Y lowers the legs");

            var cameras = Object.FindAnyObjectByType<RocketSlam.Cameras.CameraDirector>();
            var view = cameras.View;
            yield return Button(pad.rightStickButton);
            Assert.AreNotEqual(view, cameras.View, "R3 changes the camera");

            yield return Button(pad.buttonWest);
            Assert.AreEqual(ControlMode.Easy, controls.Mode, "X back to Easy");
        }
    }
}
