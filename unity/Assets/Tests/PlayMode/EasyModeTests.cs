using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>
    /// Easy mode (the default) driven only through the real keyboard bindings, like a first-time player:
    /// the start screen, the one-key engine, the burn cue and the never-climb thrust cap.
    /// </summary>
    public class EasyModeTests : InputTestFixture
    {
        /// <summary>Tests never read or write the developer's editor save.</summary>
        [SetUp]
        public void UseMemorySave() => SaveGame.UseMemory();

        const float SimSpeed = 4f;
        Keyboard keyboard;
        LandingSession session;
        RocketController rocket;
        PlayerControls controls;
        EasyAssist assist;

        /// <summary>Load the level and close the start screen with Space (the only key a naive player uses).</summary>
        IEnumerator LoadAndStartWithSpace()
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            spaceDown = false;
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            controls = Object.FindAnyObjectByType<PlayerControls>();
            assist = controls.Assist;

            Assert.AreEqual(ControlMode.Easy, controls.Mode, "Easy is the default");
            Assert.IsTrue(session.IntroVisible, "start screen first");
            Assert.AreEqual(0f, Time.timeScale, "game waits on the start screen");

            Press(keyboard.spaceKey);
            yield return null;
            Assert.IsFalse(session.IntroVisible, "any key starts");
            Assert.AreEqual(1f, Time.timeScale);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(rocket.EngineOn, "the key that closed the start screen does not fire the engine");
            Release(keyboard.spaceKey);
            yield return null;
        }

        bool spaceDown;

        /// <summary>Press or release Space (tracked here: reading the control mid-frame trips the input
        /// system's value cache in the test fixture).</summary>
        void Hold(bool on)
        {
            if (on == spaceDown) return;
            spaceDown = on;
            if (on) Press(keyboard.spaceKey);
            else Release(keyboard.spaceKey);
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        /// <summary>Waits for FIRE NOW, reacts after 0.5 s (or a slow 1.5 s), holds Space down to 30 m, then holds only while the
        /// fall is faster than 2 m/s. Must land.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator NaiveHumanLandsByFollowingTheCue([Values(0.5f, 1.5f)] float reactionSeconds)
        {
            yield return LoadAndStartWithSpace();
            Time.timeScale = SimSpeed;
            float fireNowAt = -1f, start = Time.time;
            bool sawFireIn = false, sawTouchdownCue = false;
            float fireInAltitude = 0f;
            while (!session.Evaluator.IsFinished && Time.time - start < 150f)
            {
                CueKind cue = assist.Cue.kind;
                if (cue == CueKind.FireIn && !sawFireIn) { sawFireIn = true; fireInAltitude = rocket.Altitude; }
                if (cue == CueKind.Touchdown) sawTouchdownCue = true;
                if (cue == CueKind.FireNow && fireNowAt < 0f)
                {
                    fireNowAt = Time.time;
                    Debug.Log($"[EasyModeTests] FIRE NOW at h={rocket.Altitude:0} vy={rocket.Velocity.y:0.0}");
                }
                bool held;
                if (fireNowAt < 0f || Time.time - fireNowAt < reactionSeconds) held = false;
                else if (rocket.Altitude > 30f) held = true;
                else held = rocket.Velocity.y < -2f || (spaceDown && rocket.Velocity.y < -1.5f);
                Hold(held);
                yield return null;
            }
            Hold(false);
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[EasyModeTests] naive human (reaction {reactionSeconds} s): {e.State} {e.Reason} - {e.Describe()}; fuel {rocket.FuelKg:0} kg; " +
                      $"off target {session.HorizontalToTarget.magnitude:0.0} m; flight {Time.time - start:0} s");
            Assert.IsTrue(sawFireIn, "cue counts down first");
            Assert.Greater(fireInAltitude, 2000f, "FIRE IN shown from the start");
            Assert.Greater(fireNowAt, 0f, "cue said FIRE NOW");
            Assert.IsTrue(sawTouchdownCue, "touchdown readout in the last metres");
            Assert.AreEqual(LandingState.Success, e.State, e.Describe());
            Assert.Less(session.HorizontalToTarget.magnitude, 15f, "assist steered onto the pad");
        }

        /// <summary>Holding Space from the very first second must never make the rocket go up.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator HoldingSpaceFromTheStartNeverClimbs()
        {
            yield return LoadAndStartWithSpace();
            Time.timeScale = SimSpeed;
            Hold(true);
            float start = Time.time, maxVy = float.MinValue, lowest = float.MaxValue, maxRise = 0f;
            float fuel0 = rocket.FuelKg;
            while (!session.Evaluator.IsFinished && Time.time - start < 140f)
            {
                yield return new WaitForFixedUpdate();
                if (session.Evaluator.State != LandingState.Flying) break; // contact bounces are not climbing
                maxVy = Mathf.Max(maxVy, rocket.Velocity.y);
                float h = rocket.Position.y;
                lowest = Mathf.Min(lowest, h);
                maxRise = Mathf.Max(maxRise, h - lowest);
            }
            Hold(false);
            Debug.Log($"[EasyModeTests] hold from start: max vy {maxVy:0.00} m/s, max rise {maxRise:0.00} m, " +
                      $"fuel used {fuel0 - rocket.FuelKg:0} kg, end {session.Evaluator.State} {session.Evaluator.Describe()}");
            Assert.Greater(fuel0 - rocket.FuelKg, 1000f, "engine actually fired");
            Assert.LessOrEqual(maxVy, 0.5f, "never moves up");
            Assert.Less(maxRise, 0.5f, "never climbs");
        }

        /// <summary>No input at all: free fall into the ground (legs deploy by themselves, but that doesn't help).</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator NoInputCrashes()
        {
            yield return LoadAndStartWithSpace();
            Time.timeScale = 6f;
            bool legsAuto = false;
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 60f)
            {
                if (rocket.Legs != LegsState.Stowed) legsAuto = true;
                yield return null;
            }
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[EasyModeTests] no input: {e.State} {e.Reason} - {e.Describe()}");
            Assert.IsTrue(legsAuto, "legs deploy automatically");
            Assert.AreEqual(LandingState.Failure, e.State);
            Assert.AreEqual(FailureReason.HardImpact, e.Reason);
            Assert.IsTrue(rocket.Exploded);
        }

        [UnityTest]
        public IEnumerator MTogglesManualMode()
        {
            yield return LoadAndStartWithSpace();
            Assert.IsTrue(assist.enabled);
            Assert.IsTrue(rocket.FullThrottleRange);
            var help = Object.FindAnyObjectByType<RocketSlam.Hud.HudController>().GetComponent<UIDocument>()
                .rootVisualElement.Q<Label>("help");
            yield return null;
            Assert.AreEqual(DisplayStyle.None, help.resolvedStyle.display, "the Manual key line is hidden in Easy mode");
            PressAndRelease(keyboard.mKey);
            yield return null;
            Assert.AreEqual(ControlMode.Manual, controls.Mode);
            yield return null;
            yield return null;
            Assert.AreEqual(DisplayStyle.Flex, help.resolvedStyle.display, "and shown in Manual mode");
            Assert.IsFalse(assist.enabled, "assist off in Manual");
            Assert.IsFalse(rocket.FullThrottleRange, "40 % minimum back in Manual");
            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.IsTrue(rocket.EngineOn, "Space toggles the engine in Manual");
            Assert.GreaterOrEqual(rocket.Throttle, rocket.Config.minThrottle);
            PressAndRelease(keyboard.mKey);
            yield return null;
            Assert.AreEqual(ControlMode.Easy, controls.Mode);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(rocket.EngineOn, "Easy: engine runs only while Space is held");
        }
    }
}
