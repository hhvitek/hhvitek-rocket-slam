using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>Automated flights in the real Level01 scene with the real rocket component.</summary>
    public class FlightTests
    {
        const float SimSpeed = 6f;
        LandingSession session;
        RocketController rocket;

        [UnitySetUp]
        public IEnumerator LoadLevel()
        {
            SaveGame.UseMemory(); // never the developer's editor save
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null; // Start() -> Restart()
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            Time.timeScale = SimSpeed;
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        IEnumerator FlyUntilFinished(float maxSimSeconds)
        {
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < maxSimSeconds)
                yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AutopilotLandsFromLevelOneStart()
        {
            Assert.AreEqual(session.Level.startAltitude, rocket.Altitude, 60f); // a few frames of fall already
            Assert.AreEqual(session.Level.fuelKg, rocket.FuelKg, 1f);
            var autopilot = Object.FindAnyObjectByType<SuicideBurnAutopilot>(FindObjectsInactive.Include);
            autopilot.Bind(rocket, session.TargetPosition);
            autopilot.logTelemetry = true;
            autopilot.enabled = true;

            yield return FlyUntilFinished(120f);

            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[FlightTests] autopilot: {e.State} {e.Reason} - {e.Describe()}; fuel left {rocket.FuelKg:0} kg; " +
                      $"distance from target {session.HorizontalToTarget.magnitude:0.0} m");
            Assert.AreEqual(LandingState.Success, e.State, e.Describe());
            Assert.Greater(rocket.FuelKg, 0f);
            Assert.Less(session.HorizontalToTarget.magnitude, 15f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FreeFallWithoutBurnExplodesOnImpact()
        {
            yield return FlyUntilFinished(60f);

            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[FlightTests] free fall: {e.State} {e.Reason} - {e.Describe()}");
            Assert.AreEqual(LandingState.Failure, e.State);
            Assert.AreEqual(FailureReason.HardImpact, e.Reason);
            Assert.Greater(e.TouchdownVerticalSpeed, 150f, "falls near terminal velocity");
            Assert.IsTrue(rocket.Exploded);

            session.TogglePause();
            Assert.IsFalse(session.IsPaused, "pause is ignored once the outcome is final");
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator RestartAfterCrashRestoresStartState()
        {
            yield return FlyUntilFinished(60f);
            Assert.IsTrue(session.Evaluator.IsFinished);

            session.Restart();
            Time.timeScale = 1f;
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(LandingState.Flying, session.Evaluator.State);
            Assert.IsFalse(rocket.Exploded);
            Assert.IsFalse(rocket.ControlsLocked);
            Assert.IsFalse(rocket.EngineOn);
            Assert.AreEqual(LegsState.Stowed, rocket.Legs);
            Assert.AreEqual(session.Level.fuelKg, rocket.FuelKg, 1f);
            Assert.AreEqual(session.Level.startAltitude, rocket.Altitude, 10f);
            Assert.AreEqual(session.Level.startVerticalSpeed, rocket.Velocity.y, 1f);
            Assert.Less(rocket.TiltDeg, 0.1f);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator EngineBurnsFuelAndCannotThrottleBelowMinimum()
        {
            rocket.SetEngine(true);
            rocket.SetThrottle(0.1f);
            Assert.AreEqual(rocket.Config.minThrottle, rocket.Throttle, 1e-5f);
            float fuel = rocket.FuelKg;
            float vy = rocket.Velocity.y;
            Time.timeScale = 1f;
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();

            float expectedBurn = rocket.Config.burnRateAtFullKgPerS * rocket.Config.minThrottle * 50 * Time.fixedDeltaTime;
            Assert.AreEqual(expectedBurn, fuel - rocket.FuelKg, expectedBurn * 0.05f);
            Assert.Greater(rocket.Velocity.y, vy, "even minimum thrust plus drag slows the fall");
        }
    }
}
