using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>Drives the real input bindings with a virtual keyboard.</summary>
    public class ControlsTests : InputTestFixture
    {
        /// <summary>Tests never read or write the developer's editor save.</summary>
        [SetUp]
        public void UseMemorySave() => SaveGame.UseMemory();

        Keyboard keyboard;
        LandingSession session;
        RocketController rocket;

        /// <summary>These tests cover the realistic controls: switch to Manual and skip the start screen.</summary>
        void UseManualMode()
        {
            Object.FindAnyObjectByType<PlayerControls>().Mode = ControlMode.Manual;
            session.DismissIntro();
        }

        /// <summary>Engine off, high speed: holding a steer key (WASD or arrows) must tilt the rocket and build
        /// sideways velocity towards the camera-relative direction.</summary>
        [UnityTest]
        public IEnumerator SteeringWorksWithTheEngineOff()
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            UseManualMode();
            var cameras = Object.FindAnyObjectByType<RocketSlam.Cameras.CameraDirector>();

            foreach (var key in new[] { keyboard.dKey, keyboard.rightArrowKey, keyboard.wKey, keyboard.upArrowKey })
            {
                session.Restart();
                yield return null;
                Transform cam = cameras.ActiveCamera.transform;
                Vector3 right = cam.right; right.y = 0f; right.Normalize();
                Vector3 away = cam.forward + cam.up; away.y = 0f; away.Normalize();
                bool sideways = key == keyboard.dKey || key == keyboard.rightArrowKey;
                Vector3 expected = sideways ? right : away;
                Assert.IsFalse(rocket.EngineOn);

                Press(key);
                float t0 = Time.time;
                float tiltAtHalfSecond = -1f;
                while (Time.time - t0 < 3f)
                {
                    yield return new WaitForFixedUpdate();
                    if (tiltAtHalfSecond < 0f && Time.time - t0 >= 0.5f) tiltAtHalfSecond = rocket.TiltDeg;
                }
                Release(key);
                yield return null;

                Vector3 lean = rocket.Up; lean.y = 0f;
                Vector3 vel = rocket.Velocity; vel.y = 0f;
                Debug.Log($"[ControlsTests] {key.name}: tilt@0.5s={tiltAtHalfSecond:0.0}° tilt@3s={rocket.TiltDeg:0.0}° lateral={Vector3.Dot(vel, expected):0.0} m/s");
                Assert.Greater(tiltAtHalfSecond, 1f, $"{key.name}: visible response within 0.5 s");
                Assert.Greater(rocket.TiltDeg, 5f, $"{key.name}: tilts");
                Assert.Greater(Vector3.Dot(lean.normalized, expected), 0.9f, $"{key.name}: leans the right way");
                Assert.Greater(Vector3.Dot(vel, expected), 2f, $"{key.name}: drifts the right way");
                Assert.Less(rocket.TiltDeg, 45f, $"{key.name}: no flipping");
            }
        }

        [UnityTest]
        public IEnumerator KeyboardFliesTheRocket()
        {
            // Set up inside the test: InputTestFixture.Setup must run first (it isolates the input system).
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            UseManualMode();

            Assert.IsFalse(rocket.EngineOn);
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
            Assert.IsTrue(rocket.EngineOn, "Space ignites");

            Press(keyboard.leftCtrlKey);
            for (int i = 0; i < 30; i++) yield return null;
            Release(keyboard.leftCtrlKey);
            yield return null;
            Assert.Less(rocket.Throttle, 1f, "Ctrl lowers throttle");
            Assert.GreaterOrEqual(rocket.Throttle, rocket.Config.minThrottle);
            float lowered = rocket.Throttle;
            Press(keyboard.leftShiftKey);
            for (int i = 0; i < 10; i++) yield return null;
            Release(keyboard.leftShiftKey);
            yield return null;
            Assert.Greater(rocket.Throttle, lowered, "Shift raises throttle");
            PressAndRelease(keyboard.xKey);
            yield return null;
            Assert.AreEqual(rocket.Config.minThrottle, rocket.Throttle, 1e-5f, "X = minimum");
            PressAndRelease(keyboard.zKey);
            yield return null;
            Assert.AreEqual(1f, rocket.Throttle, 1e-5f, "Z = full");

            Press(keyboard.rightArrowKey);
            yield return null;
            // Camera-relative: the rocket must tilt towards the camera's screen-right.
            Camera cam = Object.FindAnyObjectByType<RocketSlam.Cameras.CameraDirector>().ActiveCamera;
            Vector3 camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
            Vector3 tiltWorld = rocket.Rotation * new Vector3(rocket.Gimbal.x, 0f, rocket.Gimbal.y);
            Assert.Greater(tiltWorld.magnitude, 0.9f, "Right arrow deflects the gimbal");
            Assert.Greater(Vector3.Dot(tiltWorld.normalized, camRight), 0.95f, "Right arrow tilts towards screen-right");
            Release(keyboard.rightArrowKey);
            yield return null;
            Assert.AreEqual(0f, rocket.Gimbal.magnitude, 1e-4f);

            // One key per frame: two queued keyboard state events in one update would overwrite each other.
            Press(keyboard.rightArrowKey);
            yield return null;
            Press(keyboard.upArrowKey);
            yield return null;
            PlayerControls controls = Object.FindAnyObjectByType<PlayerControls>();
            Assert.AreEqual(1f, controls.GimbalInput.x, 1e-4f, "digital composite: diagonal gives full deflection per axis");
            Assert.AreEqual(1f, controls.GimbalInput.y, 1e-4f);
            Assert.Greater(rocket.Gimbal.magnitude, 1f, "not normalised to a unit circle");
            Release(keyboard.rightArrowKey);
            yield return null;
            Release(keyboard.upArrowKey);
            yield return null;

            PressAndRelease(keyboard.gKey);
            yield return null;
            Assert.AreNotEqual(LegsState.Stowed, rocket.Legs, "G deploys legs");

            PressAndRelease(keyboard.spaceKey);
            yield return null;
            Assert.IsFalse(rocket.EngineOn, "Space cuts the engine");

            PressAndRelease(keyboard.escapeKey);
            yield return null;
            Assert.IsTrue(session.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);

            PressAndRelease(keyboard.rKey);
            yield return null;
            Assert.IsFalse(session.IsPaused);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.AreEqual(LegsState.Stowed, rocket.Legs, "R restarts the attempt");
            Assert.AreEqual(LandingState.Flying, session.Evaluator.State);
        }
    }
}
