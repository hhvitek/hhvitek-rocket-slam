using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using RocketSlam.Sound;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>Effects and sound in the real Level01 scene: they react to the flight and never log errors.</summary>
    public class EffectsAudioTests
    {
        const float SimSpeed = 4f;
        LandingSession session;
        RocketController rocket;
        RocketEffects effects;
        GameAudio audio;
        CameraDirector cameras;

        [UnitySetUp]
        public IEnumerator LoadLevel()
        {
            SaveGame.UseMemory(); // never the developer's editor save
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            effects = Object.FindAnyObjectByType<RocketEffects>();
            audio = Object.FindAnyObjectByType<GameAudio>();
            cameras = Object.FindAnyObjectByType<CameraDirector>();
            Assert.IsNotNull(effects, "scene has RocketEffects");
            Assert.IsNotNull(audio, "scene has GameAudio");
            Assert.IsNotNull(audio.Mixer, "GameAudio routes into Main.mixer");
            Assert.IsTrue(audio.Mixer.GetFloat(GameAudio.MasterVolumeParameter, out _), "MasterVolume is exposed");
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        /// <summary>The Easy-mode "naive human" (same rules as the player autocapture bot) lands with flame, dust,
        /// rumble and engine sound all reacting, and nothing logs an error or exception.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator EasyFlightWithEffectsAndAudioLandsCleanly()
        {
            var controls = Object.FindAnyObjectByType<PlayerControls>();
            controls.Mode = ControlMode.Easy;
            EasyAssist assist = controls.Assist;
            controls.enabled = false;
            assist.enabled = true;
            session.Restart();
            Time.timeScale = SimSpeed;

            float fireNowAt = -1f, start = Time.time;
            float maxFlame = 0f, maxDust = 0f, maxRumble = 0f, maxEngineVol = 0f, maxWindVol = 0f, maxRumbleHigh = 0f;
            int shotsBefore = audio.OneShotCount;
            while (!session.Evaluator.IsFinished && Time.time - start < 150f)
            {
                if (assist.Cue.kind == CueKind.FireNow && fireNowAt < 0f) fireNowAt = Time.time;
                float h = rocket.Altitude;
                bool held;
                if (fireNowAt < 0f || Time.time - fireNowAt < 0.5f) held = false;
                else if (h > 30f) held = true;
                else held = rocket.Velocity.y < -2f || (assist.ThrustHeld && rocket.Velocity.y < -1.5f);
                assist.ThrustHeld = held;

                yield return null;
                maxFlame = Mathf.Max(maxFlame, effects.Flame.Level);
                maxDust = Mathf.Max(maxDust, effects.Dust.Intensity);
                maxRumble = Mathf.Max(maxRumble, cameras.Shake.RumbleLevel);
                if (rocket.Altitude > 100f) maxRumbleHigh = Mathf.Max(maxRumbleHigh, cameras.Shake.RumbleLevel);
                maxEngineVol = Mathf.Max(maxEngineVol, audio.EngineHighSource.volume + audio.EngineLowSource.volume);
                maxWindVol = Mathf.Max(maxWindVol, audio.WindSource.volume);
            }
            assist.ThrustHeld = false;
            for (int i = 0; i < 10; i++) yield return null;

            TestContext.Out.WriteLine($"[EffectsAudioTests] {session.Evaluator.State} flame {maxFlame:0.00} dust {maxDust:0.00} rumble {maxRumble:0.00} " +
                      $"engine vol {maxEngineVol:0.00} wind vol {maxWindVol:0.00} one-shots {audio.OneShotCount - shotsBefore}");
            Assert.AreEqual(LandingState.Success, session.Evaluator.State, session.Evaluator.Describe());
            Assert.Greater(maxFlame, 0.3f, "flame shown while burning");
            Assert.Greater(maxDust, 0.1f, "ground dust during the final approach");
            Assert.Greater(maxRumble, 0f, "rumble near the ground");
            Assert.AreEqual(0f, maxRumbleHigh, "no shake during the high descent");
            Assert.Greater(maxEngineVol, 0.2f, "engine sound while burning");
            Assert.Greater(maxWindVol, 0.1f, "wind while falling fast");
            Assert.GreaterOrEqual(audio.OneShotCount - shotsBefore, 3, "leg clunk/latch, touchdown thud, result sound");
            Assert.AreEqual(0f, effects.Flame.Level, "flame off after touchdown");
            Assert.IsFalse(effects.Flame.FlameLight.enabled);
            Assert.AreEqual(0, effects.Explosion.PlayCount);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Free fall into the ground: the explosion particles, flash light and camera kick fire; restart
        /// clears them.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator CrashTriggersExplosionEffects()
        {
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            Time.timeScale = 6f;
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 60f)
                yield return null;
            Assert.IsTrue(rocket.Exploded);
            Time.timeScale = 1f;
            yield return null;
            yield return null;

            ExplosionEffect fx = effects.Explosion;
            Assert.AreEqual(1, fx.PlayCount);
            Assert.IsTrue(fx.IsPlaying);
            Assert.IsTrue(fx.Flash.enabled, "flash light on");
            int particles = 0;
            foreach (ParticleSystem ps in fx.Systems) particles += ps.particleCount;
            Assert.Greater(particles, 50, "fireball, sparks, smoke and debris spawned");
            Assert.Less(Vector3.Distance(fx.transform.position, rocket.Position), 30f, "at the wreck");
            Assert.Greater(cameras.Shake.Trauma, 0.1f, "camera kick");
            foreach (Renderer r in rocket.GetComponentsInChildren<Renderer>())
                Assert.IsFalse(r.enabled, "booster hidden: " + r.name);

            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(fx.Flash.enabled, "flash fades out");

            session.Restart();
            yield return null;
            Assert.IsFalse(fx.IsPlaying, "restart clears the explosion");
            Assert.AreEqual(0f, cameras.Shake.Trauma);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Pause pauses the whole listener (engine loops and SFX one-shots such as the explosion) while UI
        /// clicks stay audible; restart stops SFX tails and leaves the listener running.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator PauseSilencesSfxAndRestartStopsIt()
        {
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
            session.DismissIntro();
            yield return null;
            Assert.IsFalse(AudioListener.pause, "running game: listener live");
            Time.timeScale = 6f;
            float start = Time.time;
            while (!session.Evaluator.IsFinished && Time.time - start < 60f)
                yield return null;
            Assert.IsTrue(rocket.Exploded);
            Time.timeScale = 1f;
            yield return null;
            bool hadSfx = audio.AnySfxPlaying;

            session.SetPaused(true);
            yield return null;
            Assert.IsTrue(AudioListener.pause, "pause pauses SFX one-shots too");
            Assert.IsTrue(audio.UiSource.ignoreListenerPause, "UI sounds still play while paused");
            session.SetPaused(false);
            yield return null;
            Assert.IsFalse(AudioListener.pause, "resume");

            session.Restart();
            yield return null;
            Assert.IsFalse(audio.AnySfxPlaying, $"restart stops SFX (explosion was playing: {hadSfx})");
            Assert.IsFalse(AudioListener.pause);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
