using System.Collections;
using NUnit.Framework;
using RocketSlam.Gameplay;
using RocketSlam.Menus;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using RocketSlam.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>
    /// M4b: every level (ground pad, crosswind, droneship, rough sea, night) is winnable by a first-time Easy player
    /// holding only Space when the cue says so, and by the autopilot; a landing on the droneship's containers fails.
    /// (Named to sort before the plain fixtures: a level whose input actions were enabled outside an InputTestFixture
    /// leaves stale state monitors that break later fixtures.)
    /// </summary>
    public class CampaignLevelsTests : InputTestFixture
    {
        const float SimSpeed = 4f;
        const float SkilledDelay = 1.2f;
        Keyboard keyboard;
        LandingSession session;
        RocketController rocket;
        bool spaceDown;

        [TearDown]
        public void RestoreTime()
        {
            Time.timeScale = 1f;
            SaveGame.UseMemory(); // leave nothing pointing at a file
        }

        /// <summary>A fresh in-memory save with every level unlocked (tests never write the editor's save file).</summary>
        static void AllUnlocked()
        {
            var save = new SaveData { unlockedLevels = LevelLoader.LevelCount };
            SaveGame.UseMemory(save);
        }

        AttemptResult Result => Object.FindAnyObjectByType<AttemptTracker>().LastResult;

        IEnumerator Load(int level)
        {
            AllUnlocked();
            keyboard = InputSystem.AddDevice<Keyboard>();
            spaceDown = false;
            yield return SceneManager.LoadSceneAsync(LevelLoader.SceneName(level), LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            Assert.AreEqual(level, session.Level.number);
            Assert.AreEqual(level, LevelLoader.CurrentLevel);
        }

        void Hold(bool on)
        {
            if (on == spaceDown) return;
            spaceDown = on;
            if (on) Press(keyboard.spaceKey);
            else Release(keyboard.spaceKey);
        }

        /// <summary>Same naive player as <c>EasyModeTests</c>: waits for FIRE NOW, reacts after 0.5 s, holds Space to
        /// 30 m, then only while the HUD fall speed (relative to the deck) is above 2 m/s.</summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator NaiveEasyPlayerLandsOnEveryLevel([Values(1, 2, 3, 4, 5)] int level)
        {
            yield return Load(level);
            PlayerControls controls = Object.FindAnyObjectByType<PlayerControls>();
            EasyAssist assist = controls.Assist;
            Assert.AreEqual(ControlMode.Easy, controls.Mode);
            Assert.IsTrue(session.IntroVisible);
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
            Assert.IsFalse(session.IntroVisible);

            Time.timeScale = SimSpeed;
            float fireNowAt = -1f, start = Time.time, maxWind = 0f, maxDeckSpeed = 0f;
            while (!session.Evaluator.IsFinished && Time.time - start < 200f)
            {
                if (assist.Cue.kind == CueKind.FireNow && fireNowAt < 0f) fireNowAt = Time.time;
                float fall = rocket.RelativeVelocity.y;
                bool held;
                if (fireNowAt < 0f || Time.time - fireNowAt < 0.5f) held = false;
                else if (rocket.Altitude > 30f) held = true;
                else held = fall < -2f || (spaceDown && fall < -1.5f);
                Hold(held);
                maxWind = Mathf.Max(maxWind, rocket.Wind.magnitude);
                maxDeckSpeed = Mathf.Max(maxDeckSpeed, rocket.ReferenceVelocity.magnitude);
                yield return null;
            }
            Hold(false);
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[CampaignLevelsTests] level {level} naive Easy: {e.State} {e.Reason} - {e.Describe()}; fuel {rocket.FuelKg:0} / {session.Level.fuelKg:0} kg; " +
                      $"off target {session.HorizontalToTarget.magnitude:0.0} m; max wind {maxWind:0.0} m/s; max deck speed {maxDeckSpeed:0.00} m/s; flight {Time.time - start:0} s");
            Assert.Greater(fireNowAt, 0f, "cue said FIRE NOW");
            Assert.AreEqual(LandingState.Success, e.State, e.Describe());
            AttemptResult r = Result;
            Debug.Log($"[CampaignLevelsTests] level {level} naive Easy score {r.score.total} (fuel {r.score.fuel}, precision {r.score.precision}, " +
                      $"softness {r.score.softness}) {r.score.stars} stars; thresholds {session.Level.twoStarScore}/{session.Level.threeStarScore}");
            Assert.AreEqual(2, r.score.stars, "a first-time Easy landing earns two stars");
            if (session.Level.wind.steady.sqrMagnitude > 1f) Assert.Greater(maxWind, 4f, "this level has wind");
            if (session.Level.sea.swellAmplitude > 0.5f) Assert.Greater(maxDeckSpeed, 0.1f, "this level's deck moves");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator AutopilotLandsOnEveryLevel([Values(1, 2, 3, 4, 5)] int level)
        {
            yield return Load(level);
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            var autopilot = Object.FindAnyObjectByType<SuicideBurnAutopilot>(FindObjectsInactive.Include);
            autopilot.Bind(rocket, session.TargetPosition);
            autopilot.enabled = true;
            Time.timeScale = 6f;
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 150f) yield return null;
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[CampaignLevelsTests] level {level} autopilot: {e.State} {e.Reason} - {e.Describe()}; fuel {rocket.FuelKg:0} kg; " +
                      $"off target {session.HorizontalToTarget.magnitude:0.0} m");
            Assert.AreEqual(LandingState.Success, e.State, e.Describe());
            Assert.Less(session.HorizontalToTarget.magnitude, 15f);
            AttemptResult r = Result;
            Debug.Log($"[CampaignLevelsTests] level {level} autopilot score {r.score.total} (fuel {r.score.fuel}, precision {r.score.precision}, " +
                      $"softness {r.score.softness}) {r.score.stars} stars");
            Assert.AreEqual(3, r.score.stars, "a near-optimal (suicide burn) landing earns three stars");
            float fuelLeft = rocket.FuelKg / session.Level.fuelKg;
            Debug.Log($"[CampaignLevelsTests] level {level} autopilot fuel margin {fuelLeft * 100f:0.0} % of {session.Level.fuelKg:0} kg");
            Assert.Greater(fuelLeft, 0.15f, "the level leaves a fuel margin for a good landing");

            // Results screen: on the last level there is no Next button, and it cannot be selected either.
            var menus = Object.FindAnyObjectByType<LevelMenus>();
            Time.timeScale = 1f;
            for (float t0 = Time.unscaledTime; !menus.ResultsVisible && Time.unscaledTime - t0 < 5f;) yield return null;
            Assert.IsTrue(menus.ResultsVisible);
            menus.FinishAnimation();
            yield return null;
            if (level == LevelLoader.LevelCount)
            {
                Assert.IsFalse(menus.NextAvailable);
                Assert.AreEqual(0, menus.ResultsList.Selected, "Retry selected on the last level");
                menus.ResultsList.Step(1);
                Assert.AreEqual(2, menus.ResultsList.Selected, "hidden Next is skipped: Retry -> Menu");
                menus.ResultsList.Step(1);
                Assert.AreEqual(0, menus.ResultsList.Selected, "wraps to Retry");
                menus.ResultsList.Select(1, playSound: false);
                Assert.AreEqual(0, menus.ResultsList.Selected, "Next cannot be selected directly");
            }
            else Assert.AreEqual(1, menus.ResultsList.Selected, "Next selected after a landing");
        }

        /// <summary>A skilled Easy player: lets the stage fall a moment longer after FIRE NOW appears (fires 1.2 s later: a
        /// later, harder burn wastes less fuel; the cue keeps a 30 % braking margin) and holds Space all the way down to the
        /// pad (softest Easy touchdown). This is what three stars ask of an Easy player.</summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator SkilledEasyPlayerEarnsThreeStars([Values(1, 2, 3, 4, 5)] int level)
        {
            yield return Load(level);
            EasyAssist assist = Object.FindAnyObjectByType<PlayerControls>().Assist;
            Press(keyboard.enterKey);
            yield return null;
            Release(keyboard.enterKey);
            yield return null;
            Assert.IsFalse(session.IntroVisible);
            Time.timeScale = SimSpeed;
            float fireNowAt = -1f, start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 200f)
            {
                if (assist.Cue.kind == CueKind.FireNow && fireNowAt < 0f) fireNowAt = Time.time;
                Hold(fireNowAt >= 0f && Time.time - fireNowAt >= SkilledDelay);
                yield return null;
            }
            Hold(false);
            LandingEvaluator e = session.Evaluator;
            AttemptResult r = Result;
            Debug.Log($"[CampaignLevelsTests] level {level} skilled Easy: {e.Describe()}; fuel {rocket.FuelKg:0} kg; score {r?.score.total} " +
                      $"(fuel {r?.score.fuel}, precision {r?.score.precision}, softness {r?.score.softness}) {r?.score.stars} stars");
            Assert.AreEqual(LandingState.Success, e.State, e.Describe());
            Assert.AreEqual(3, r.score.stars, "a skilled Easy landing earns three stars");
        }

        /// <summary>The rocket stays on the heaving level-4 deck through the whole settle check (friction carries it),
        /// moving with the ship.</summary>
        [UnityTest, Timeout(240000)]
        public IEnumerator LandedStageRidesTheMovingDeck()
        {
            yield return Load(4);
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            var autopilot = Object.FindAnyObjectByType<SuicideBurnAutopilot>(FindObjectsInactive.Include);
            autopilot.Bind(rocket, session.TargetPosition);
            autopilot.enabled = true;
            Time.timeScale = 6f;
            float start = Time.time;
            LandingSite site = session.Site;
            Vector3? touchdownLocal = null, touchdownFeet = null;
            while (!session.Evaluator.IsFinished && Time.time - start < 150f)
            {
                if (touchdownLocal == null && session.Evaluator.State == LandingState.Settling)
                {
                    touchdownLocal = DeckLocal(site, rocket.Position);
                    touchdownFeet = DeckLocal(site, FeetCentre(rocket));
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(LandingState.Success, session.Evaluator.State, session.Evaluator.Describe());
            Assert.IsNotNull(touchdownLocal, "settle phase seen");
            // What slides on the deck are the feet, along the deck plane (deck-local x / z); the feet also sink a little
            // along the deck normal as the legs take the load, and the stage's origin sways as it rocks on its legs.
            float settleSlide = InDeckPlane(touchdownFeet.Value, DeckLocal(site, FeetCentre(rocket)));
            float settleSway = Vector3.Distance(touchdownLocal.Value, DeckLocal(site, rocket.Position));
            // 10 more seconds on the moving deck: still upright, on the deck, moving with it.
            float minY = float.MaxValue, maxY = float.MinValue;
            for (float t0 = Time.time; Time.time - t0 < 10f;)
            {
                yield return new WaitForFixedUpdate();
                minY = Mathf.Min(minY, rocket.Position.y);
                maxY = Mathf.Max(maxY, rocket.Position.y);
                Assert.Less(rocket.RelativeTiltDeg, 3f, "stays square to the deck");
                Assert.IsTrue(site.IsOver(rocket.Position), "still on the deck");
                Assert.Less((rocket.Velocity - rocket.ReferenceVelocity).magnitude, 0.5f, "moves with the deck");
            }
            float totalSlide = InDeckPlane(touchdownFeet.Value, DeckLocal(site, FeetCentre(rocket)));
            Debug.Log($"[CampaignLevelsTests] stage on the level-4 deck: heave range {maxY - minY:0.00} m, feet slide on the deck " +
                      $"{settleSlide:0.00} m during the settle check (stage origin {settleSway:0.00} m), {totalSlide:0.00} m after 10 s more");
            Assert.Less(settleSlide, 0.3f, "doesn't slide on the pitching deck during the settle check");
            Assert.Less(totalSlide, 0.5f, "stays put on the deck");
            Assert.Greater(maxY - minY, 0.2f, "the deck (and the stage on it) really heaves");
        }

        /// <summary>Day levels: the ambient light is a nearly neutral gradient (the white booster's shadow side must not
        /// read as blue paint) that still lights the shadows (not black).</summary>
        [UnityTest]
        public IEnumerator DayAmbientIsNeutralAndNotBlack([Values(1, 2, 3, 4)] int level)
        {
            yield return Load(level);
            yield return null;
            Assert.AreEqual(UnityEngine.Rendering.AmbientMode.Trilight, RenderSettings.ambientMode);
            UnityEngine.Rendering.SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
            // Light from straight up (sky), from the sides (what lights a vertical shadow side) and from below.
            var dirs = new[] { Vector3.up, Vector3.right, Vector3.forward, Vector3.left, Vector3.down };
            var colors = new Color[dirs.Length];
            sh.Evaluate(dirs, colors);
            for (int i = 0; i < dirs.Length; i++)
            {
                Color c = colors[i];
                Debug.Log($"[CampaignLevelsTests] level {level} ambient {dirs[i]}: {c.r:0.000} {c.g:0.000} {c.b:0.000}");
                float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                Assert.Greater(lum, 0.15f, "shadows are lit, not black");
                Assert.Less(c.b / Mathf.Max(1e-4f, c.r), 1.3f, "ambient is not strongly blue");
            }
        }

        static float InDeckPlane(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static Vector3 FeetCentre(RocketController r)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < r.LegCount; i++) sum += r.GetLeg(i).FootPosition;
            return sum / r.LegCount;
        }

        static Vector3 DeckLocal(LandingSite site, Vector3 world)
        {
            Pose p = site.CurrentPose;
            return Quaternion.Inverse(p.rotation) * (world - p.position);
        }

        /// <summary>No input on any level: the touchdown guard is armed only by the player's own burn, so a stage that
        /// was never fired still free-falls into the pad / deck and explodes.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator NoInputCrashesOnEveryLevel([Values(1, 2, 3, 4, 5)] int level)
        {
            yield return Load(level);
            Press(keyboard.spaceKey);
            yield return null;
            Release(keyboard.spaceKey);
            yield return null;
            Assert.IsFalse(session.IntroVisible);
            Time.timeScale = 6f;
            float start = Time.time;
            bool engineEverOn = false;
            while (!session.Evaluator.IsFinished && Time.time - start < 60f)
            {
                engineEverOn |= rocket.EngineOn;
                yield return null;
            }
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[CampaignLevelsTests] level {level} no input: {e.State} {e.Reason} - {e.Describe()}");
            Assert.IsFalse(engineEverOn, "the engine (and the touchdown guard) never fired");
            Assert.AreEqual(LandingState.Failure, e.State);
            Assert.AreEqual(FailureReason.HardImpact, e.Reason);
            Assert.IsTrue(rocket.Exploded);
        }

        /// <summary>Touching down with a foot on the stacked containers at the stern of the level-3 ship fails.</summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator LandingOnAContainerFails()
        {
            yield return Load(3);
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            yield return new WaitForFixedUpdate();
            Collider container = session.Site.transform.Find("Obstacles/ContainerStack").GetComponent<Collider>();
            Vector3 top = container.bounds.center + Vector3.up * container.bounds.extents.y;

            // Legs locked out, then the stage placed so foot 0 hangs 5 cm above the container top, sinking slowly.
            rocket.ResetTo(new Vector3(top.x, top.y + 30f, top.z), Vector3.zero, 1000f);
            rocket.SetPreviewState(false, 0f, 1f);
            Physics.SyncTransforms();
            Vector3 foot = rocket.GetLeg(0).FootCollider.bounds.center;
            float footBottom = rocket.GetLeg(0).FootCollider.bounds.min.y;
            Vector3 shift = new Vector3(top.x - foot.x, top.y + 0.05f - footBottom, top.z - foot.z);
            Vector3 p = rocket.Position + shift;
            rocket.transform.position = p;
            rocket.Body.position = p;
            rocket.Body.linearVelocity = new Vector3(0f, -0.5f, 0f);
            Physics.SyncTransforms();

            Time.timeScale = 2f;
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 20f) yield return null;
            LandingEvaluator e = session.Evaluator;
            Debug.Log($"[CampaignLevelsTests] container landing: {e.State} {e.Reason} - {e.Describe()}");
            Assert.AreEqual(LandingState.Failure, e.State, "a container is not the landing deck");
            Assert.That(e.Reason, Is.EqualTo(FailureReason.OffPad).Or.EqualTo(FailureReason.TippedOver));
        }

        [UnityTest]
        public IEnumerator LevelKeysAndNextLevelLoadScenes()
        {
            yield return Load(1);
            Assert.IsTrue(session.IntroVisible);
            PressAndRelease(keyboard.digit3Key);
            yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(3, LevelLoader.CurrentLevel, "key 3 on the start screen picks level 3");
            Assert.AreEqual(2, LevelLoader.Next(1));
            Assert.AreEqual(1, LevelLoader.Next(5), "after the last level: back to the first");
            var s = Object.FindAnyObjectByType<LandingSession>();
            Assert.IsTrue(s.IntroVisible, "the new level shows its start screen");
            Assert.AreEqual("Droneship", s.Level.displayName);
            Assert.IsNotNull(Object.FindAnyObjectByType<OceanSurface>(), "sea level has an ocean");
            Assert.IsNotNull(Object.FindAnyObjectByType<ShipMotion>());
        }
    }
}
