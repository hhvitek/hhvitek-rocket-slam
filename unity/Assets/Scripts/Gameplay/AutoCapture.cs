using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// Automated check mode for a built player: <c>RocketSlam.x86_64 -autocapture &lt;folder&gt; [-autocapture-crash | -autocapture-easy]</c>.
    /// Default: the autopilot flies (Manual HUD). -autocapture-crash: nobody flies. -autocapture-easy: a scripted
    /// "naive human" holds the one Easy-mode key when the cue says FIRE NOW (start screen, FIRE IN, FIRE NOW,
    /// final approach and result are captured). Screenshots include the HUD; the player quits afterwards.
    /// <c>-autocapture-m4a</c> (with -autocapture): the same Easy bot, HUD hidden, environment shots at 3000 / 1000 /
    /// 300 / 50 m, engine and pad-side cams, landed, then wide establishing views (m4a_*.png).
    /// <c>-benchmark [file]</c>: the Easy bot flies one landing in the chase view at 1920×1080 (no screenshots) and
    /// writes average / 1 % low FPS (overall and per altitude band) to the file, then quits.
    /// Does nothing in normal play.
    /// </summary>
    public class AutoCapture : MonoBehaviour
    {
        [SerializeField] LandingSession session;
        [SerializeField] SuicideBurnAutopilot autopilot;
        [SerializeField] PlayerControls controls;

        string folder;
        string prefix;
        bool easy;
        int stage;
        float finishedAt = -1f;
        float nextLog;
        float fireNowSeenAt = -1f;
        EasyAssist assist;
        // M3 effect shots (m3_*.png): flame, engine cam, dust, landed / explosion frames.
        CameraDirector cameras;
        RocketEffects effects;
        int m3Stage;
        float m3StageAt;
        int lastShotFrame = -1;
        // M4a environment shots, M4b level shots and the benchmark.
        bool m4a, m4b, benchmark;
        int m4bStage;
        float m4bStageAt;
        string benchFile;
        int m4aStage;
        float m4aStageAt;
        float flightStartedAt = -1f;
        readonly List<float> frameTimes = new List<float>();
        readonly List<float> frameAltitudes = new List<float>();
        readonly List<float> frameClock = new List<float>();

        bool EasyShots => !m4a && !m4b && !benchmark;

        public void Setup(LandingSession s, SuicideBurnAutopilot a, PlayerControls c)
        {
            session = s;
            autopilot = a;
            controls = c;
        }

        void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-autocapture");
            int b = Array.IndexOf(args, "-benchmark");
            bool crash = false;
            if (b >= 0)
            {
                benchmark = true;
                easy = true;
                benchFile = Path.GetFullPath(b + 1 < args.Length && !args[b + 1].StartsWith("-") ? args[b + 1] : "benchmark.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(benchFile));
                folder = Path.GetDirectoryName(benchFile);
                prefix = "bench";
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                if (Screen.width != 1920 || Screen.height != 1080)
                    Screen.SetResolution(1920, 1080, Screen.fullScreenMode);
                // Profiling aids: -bench-noshadowprops, -bench-cullscale <x>, -bench-disable <Name>[,<Name>...]
                RocketSlam.World.InstancedScenery.DebugNoShadows = Array.IndexOf(args, "-bench-noshadowprops") >= 0;
                int cs = Array.IndexOf(args, "-bench-cullscale");
                if (cs >= 0 && cs + 1 < args.Length && float.TryParse(args[cs + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float scale))
                    RocketSlam.World.InstancedScenery.DebugCullScale = scale;
                int hk = Array.IndexOf(args, "-bench-hidekinds");
                if (hk >= 0 && hk + 1 < args.Length) RocketSlam.World.InstancedScenery.DebugHideKinds = args[hk + 1].Split(',');
                // -bench-disable <Name>[,<Name>...] deactivates scene objects by name for the run.
                int d = Array.IndexOf(args, "-bench-disable");
                if (d >= 0 && d + 1 < args.Length)
                    foreach (string name in args[d + 1].Split(','))
                        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                            if (t.name == name)
                            {
                                t.gameObject.SetActive(false);
                                Debug.Log($"[Benchmark] disabled {name}");
                            }
            }
            else if (i >= 0 && i + 1 < args.Length)
            {
                folder = Path.GetFullPath(args[i + 1]);
                Directory.CreateDirectory(folder);
                crash = Array.IndexOf(args, "-autocapture-crash") >= 0;
                m4a = Array.IndexOf(args, "-autocapture-m4a") >= 0;
                m4b = Array.IndexOf(args, "-autocapture-m4b") >= 0;
                easy = m4a || m4b || Array.IndexOf(args, "-autocapture-easy") >= 0;
                prefix = m4a ? "m4a" : m4b ? "m4b" : easy ? "easy" : crash ? "crash" : "land";
            }
            else
            {
                enabled = false;
                return;
            }
            Application.runInBackground = true; // keep flying when the window loses focus
            cameras = FindAnyObjectByType<CameraDirector>();
            effects = FindAnyObjectByType<RocketEffects>();
            if (easy)
            {
                // Keep the start screen up for the first shot; the bot takes over after it.
                controls.Mode = ControlMode.Easy;
                assist = controls.Assist;
            }
            else
            {
                controls.Mode = ControlMode.Manual;
                controls.enabled = false;
                if (!crash)
                {
                    autopilot.Bind(session.Rocket, session.TargetPosition);
                    autopilot.enabled = true;
                }
            }
            Debug.Log($"[AutoCapture] mode={prefix} folder={folder}");
        }

        void Update()
        {
            if (easy) UpdateEasy();
            else UpdateAutopilot();
            if (m4a) { if (lastShotFrame != Time.frameCount) UpdateM4a(); }
            else if (m4b) { if (lastShotFrame != Time.frameCount) UpdateM4b(); }
            else if (!benchmark && lastShotFrame != Time.frameCount) UpdateM3();
            if (benchmark) SampleFrame();
            Common();
        }

        void UpdateAutopilot()
        {
            if (session.IntroVisible) session.DismissIntro(); // the session may have shown it after Start() above
            RocketController r = session.Rocket;
            if (stage == 0 && Time.timeSinceLevelLoad > 0.5f)
                Shot("3000");
            else if (stage == 1 && r.Altitude < 1000f)
                Shot("1000");
            else if (stage == 2 && r.Altitude < 150f)
                Shot("150");
        }

        /// <summary>A naive human: waits for FIRE NOW (0.4 s reaction), holds until 30 m, then holds only while
        /// falling faster than 2 m/s.</summary>
        void UpdateEasy()
        {
            RocketController r = session.Rocket;
            if (stage == 0)
            {
                if (Time.unscaledTime > 1.5f)
                {
                    if (EasyShots) Shot("1_start"); // captured at the end of this frame
                    else stage++;
                }
                return;
            }
            if (session.IntroVisible)
            {
                // The benchmark waits longer on the start screen (as a player reading it would), so load-time
                // warm-up (shader compiles, first texture uploads) is not counted as flight frames.
                if (Time.unscaledTime > (benchmark ? 8f : 2.5f))
                {
                    controls.enabled = false;      // dismisses the start screen
                    assist.enabled = true;         // the bot presses the key instead of the keyboard
                    session.Restart();
                    flightStartedAt = Time.unscaledTime;
                    if (m4a || m4b) SetHudVisible(false);
                }
                return;
            }
            if (session.Evaluator.IsFinished) { assist.ThrustHeld = false; return; }

            CueKind cue = assist.Cue.kind;
            if (cue == CueKind.FireNow && fireNowSeenAt < 0f) fireNowSeenAt = Time.time;
            bool held;
            float h = r.Altitude;
            if (fireNowSeenAt < 0f || Time.time - fireNowSeenAt < 0.4f) held = false;
            else if (h > 30f) held = true;
            else held = r.RelativeVelocity.y < -2f || (assist.ThrustHeld && r.RelativeVelocity.y < -1.5f); // the HUD shows deck-relative speed
            assist.ThrustHeld = held;

            if (!EasyShots) return;
            if (stage == 1 && cue == CueKind.FireIn && Time.timeSinceLevelLoad > 1f && assist.Cue.distance < 900f)
                Shot("2_fire_in");
            else if (stage == 2 && fireNowSeenAt >= 0f && Time.time - fireNowSeenAt > 0.3f)
                Shot("3_fire_now");
            else if (stage == 3 && h < 25f)
                Shot("4_final");
        }

        void Common()
        {
            RocketController r = session.Rocket;
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 5f;
                Debug.Log($"[AutoCapture] t={Time.timeSinceLevelLoad:0.0} alt={r.Altitude:0.0} vy={r.Velocity.y:0.0} state={session.Evaluator.State}" +
                          (assist != null ? $" cue={assist.Cue.kind} held={assist.ThrustHeld} thr={r.Throttle:0.00} eng={r.EngineOn} fuel={r.FuelKg:0}" : ""));
            }
            if (session.Evaluator.IsFinished)
            {
                if (finishedAt < 0f)
                {
                    finishedAt = Time.unscaledTime;
                    Debug.Log($"[AutoCapture] result: {session.Evaluator.State} {session.Evaluator.Reason} - {session.Evaluator.Describe()}; fuel {r.FuelKg:0} kg");
                }
                if (benchmark)
                {
                    if (Time.unscaledTime - finishedAt > 2f) FinishBenchmark();
                    return;
                }
                if (m4a || m4b) return; // UpdateM4a / UpdateM4b take the landed shots, then quit
                if (finishedAt >= 0f && Time.unscaledTime - finishedAt > 1.5f && stage < 99)
                {
                    stage = 98;
                    Shot(easy ? "5_result" : "result");
                }
                if (Time.unscaledTime - finishedAt > (session.Rocket.Exploded ? 4.5f : 3f)) Application.Quit(0);
            }
            if (Time.unscaledTime > 240f) Application.Quit(2);
        }

        /// <summary>Effect screenshots for M3, interleaved with the regular shots (one capture per frame).</summary>
        void UpdateM3()
        {
            if (cameras == null || effects == null) return;
            RocketController r = session.Rocket;
            float h = r.Altitude, f = effects.ThrustFraction, now = Time.unscaledTime;
            if (!easy)
            {
                if (!r.Exploded || finishedAt < 0f) return;
                float t = now - finishedAt;
                if (m3Stage == 0 && t > 0.1f) M3Shot("explosion_1");
                else if (m3Stage == 1 && t > 0.5f) M3Shot("explosion_2");
                else if (m3Stage == 2 && t > 1.4f) M3Shot("explosion_3");
                else if (m3Stage == 3 && t > 3.2f) M3Shot("explosion_4");
                return;
            }
            if (session.IntroVisible || fireNowSeenAt < 0f) return;
            switch (m3Stage)
            {
                case 0: if (f > 0.95f && Time.time - fireNowSeenAt > 1.2f) M3Shot("flame_full_chase"); break;
                case 1: if (now - m3StageAt > 1.5f) { cameras.SetView(CameraView.Engine, blend: false); Next(); } break;
                case 2: M3Shot("flame_engine_cam"); break;
                case 3: cameras.SetView(CameraView.Chase, blend: false); Next(); break;
                case 4: if (r.EngineOn && f > 0.1f && f < 0.6f && h < 400f && h > 20f) M3Shot("flame_low_chase"); else if (h < 20f) Next(); break;
                case 5: if (h < 16f && effects.Dust.Intensity > 0.15f) M3Shot("dust_chase"); else if (h < 3f) Next(); break;
                case 6: if (h < 9f) { cameras.SetView(CameraView.PadSide, blend: false); Next(); } break;
                case 7: M3Shot("dust_padside"); break;
                case 8: cameras.SetView(CameraView.Chase, blend: false); Next(); break;
                case 9: if (finishedAt >= 0f && now - finishedAt > 0.8f) M3Shot("landed"); break;
            }
        }

        // ---- M4a environment shots -----------------------------------------------------------------

        void UpdateM4a()
        {
            if (cameras == null || session.IntroVisible || flightStartedAt < 0f) return;
            RocketController r = session.Rocket;
            float h = r.Altitude, now = Time.unscaledTime, t = now - m4aStageAt;
            bool finished = finishedAt >= 0f;
            switch (m4aStage)
            {
                case 0: if (now - flightStartedAt > 0.6f) M4aShot("chase_3000"); break;
                case 1: if (h < 2300f) { cameras.SetView(CameraView.Engine, blend: false); NextM4a(); } break;
                case 2: M4aShot("engine_cam"); break;
                case 3: cameras.SetView(CameraView.Chase, blend: false); cameras.Snap(); NextM4a(); break;
                case 4: if (h < 1000f) M4aShot("chase_1000"); break;
                case 5: if (h < 300f) M4aShot("chase_300"); break;
                case 6: if (h < 90f) { cameras.SetView(CameraView.PadSide, blend: false); NextM4a(); } break;
                case 7: M4aShot("padside"); break;
                case 8: cameras.SetView(CameraView.Chase, blend: false); cameras.Snap(); NextM4a(); break;
                case 9: if (h < 50f) M4aShot("chase_50"); break;
                case 10: if (finished && now - finishedAt > 1.2f) M4aShot("landed"); break;
                case 11: cameras.SetView(CameraView.PadSide, blend: false); NextM4a(); break;
                case 12: if (t > 0.3f) M4aShot("padside_landed"); break;
                case 13:
                    // Establishing views: the director lets go of the camera.
                    cameras.enabled = false;
                    Pose(new Vector3(430f, 95f, -560f), new Vector3(-260f, 0f, 120f), 50f);
                    NextM4a();
                    break;
                case 14: if (t > 0.5f) M4aShot("wide"); break;
                case 15: Pose(new Vector3(2300f, 1300f, -2600f), new Vector3(-250f, 0f, 0f), 50f); NextM4a(); break;
                case 16: if (t > 0.5f) M4aShot("wide_aerial"); break;
                case 17: Pose(new Vector3(-150f, 1.8f, -265f), new Vector3(0f, 22f, 0f), 50f); NextM4a(); break;
                case 18: if (t > 0.5f) M4aShot("ground_level"); break;
                case 19: Pose(new Vector3(-380f, 60f, -60f), new Vector3(-560f, 10f, 150f), 55f); NextM4a(); break;
                case 20: if (t > 0.5f) M4aShot("complex"); break;
                case 21: if (t > 0.5f) Application.Quit(0); break;
            }
        }

        // ---- M4b level shots ------------------------------------------------------------------------

        /// <summary>Per level (run the player with -level N): level{N}_3000 / _300 (HUD on) / _landed; level 4 adds three
        /// deck-motion frames of the landed stage from a fixed camera low over the water, level 5 a night approach
        /// with the HUD.</summary>
        void UpdateM4b()
        {
            if (cameras == null || session.IntroVisible || flightStartedAt < 0f) return;
            RocketController r = session.Rocket;
            int n = session.Level != null ? session.Level.number : LevelLoader.CurrentLevel;
            string L = $"level{n}";
            float h = r.Altitude, now = Time.unscaledTime, t = now - m4bStageAt;
            bool finished = finishedAt >= 0f;
            switch (m4bStage)
            {
                case 0: if (now - flightStartedAt > 0.6f) M4bShot($"{L}_3000"); break;
                case 1: if (n != 1) m4bStage = 4; else if (h < 2000f) { cameras.SetView(CameraView.Engine, blend: false); NextM4b(); } break;
                case 2: if (t > 0.1f) M4bShot($"{L}_engine_cam"); break;
                case 3: cameras.SetView(CameraView.Chase, blend: false); cameras.Snap(); NextM4b(); break;
                case 4: if (n != 5) m4bStage = 7; else if (h < 900f) { SetHudVisible(true); NextM4b(); } break;
                case 5: if (t > 0.1f) M4bShot($"{L}_night_approach"); break;
                case 6: SetHudVisible(false); NextM4b(); break;
                case 7: if (h < 300f) { SetHudVisible(true); NextM4b(); } break;
                case 8: if (t > 0.1f) M4bShot($"{L}_300"); break;
                case 9: SetHudVisible(false); NextM4b(); break;
                case 10: if (finished && now - finishedAt > 1.2f) M4bShot($"{L}_landed"); break;
                case 11:
                    if (n != 4) { if (t > 0.3f) Application.Quit(0); break; }
                    cameras.enabled = false;
                    Vector3 s = session.Site.transform.position;
                    Pose(new Vector3(s.x + 20f, 4f, s.z - 135f), new Vector3(s.x, 12f, s.z), 38f);
                    NextM4b();
                    break;
                case 12: if (t > 0.3f) M4bShot($"{L}_deck_a"); break;
                case 13: if (t > 2.2f) M4bShot($"{L}_deck_b"); break;
                case 14: if (t > 2.2f) M4bShot($"{L}_deck_c"); break;
                case 15: if (t > 0.3f) Application.Quit(0); break;
            }
        }

        void M4bShot(string name)
        {
            string path = Path.Combine(folder, $"{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            RocketController r = session.Rocket;
            Debug.Log($"[AutoCapture] {path} alt={r.Altitude:0.0} vrel={r.RelativeVelocity.y:0.00} tilt={r.RelativeTiltDeg:0.0} wind={r.Wind.magnitude:0.0}");
            lastShotFrame = Time.frameCount;
            NextM4b();
        }

        void NextM4b()
        {
            m4bStage++;
            m4bStageAt = Time.unscaledTime;
        }

        void Pose(Vector3 position, Vector3 lookAt, float fov)
        {
            Camera cam = cameras.ActiveCamera;
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
            cam.fieldOfView = fov;
        }

        void M4aShot(string name)
        {
            string path = Path.Combine(folder, $"m4a_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[AutoCapture] {path} alt={session.Rocket.Altitude:0.0} view={cameras.View}");
            lastShotFrame = Time.frameCount;
            NextM4a();
        }

        void NextM4a()
        {
            m4aStage++;
            m4aStageAt = Time.unscaledTime;
        }

        void SetHudVisible(bool visible)
        {
            foreach (UIDocument doc in FindObjectsByType<UIDocument>())
                if (doc.rootVisualElement != null)
                    doc.rootVisualElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- benchmark ----------------------------------------------------------------------------

        void SampleFrame()
        {
            // From 1 s after the bot took over until the end (incl. landing dust); the first frames after the restart are skipped.
            if (flightStartedAt < 0f || Time.unscaledTime - flightStartedAt < 1f) return;
            frameTimes.Add(Time.unscaledDeltaTime);
            frameAltitudes.Add(session.Rocket.Altitude);
            frameClock.Add(Time.unscaledTime - flightStartedAt);
        }

        void FinishBenchmark()
        {
            if (!enabled) return;
            enabled = false;
            var sb = new StringBuilder();
            sb.AppendLine($"Rocket Slam benchmark {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"resolution {Screen.width}x{Screen.height} {Screen.fullScreenMode}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', " +
                          $"GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), CPU {SystemInfo.processorType}");
            sb.AppendLine($"result {session.Evaluator.State} {session.Evaluator.Reason}");
            sb.AppendLine(Stats("all", 0f, float.MaxValue));
            sb.AppendLine(Stats("alt > 1000 m", 1000f, float.MaxValue));
            sb.AppendLine(Stats("alt 100-1000 m", 100f, 1000f));
            sb.AppendLine(Stats("alt < 100 m", float.MinValue, 100f));
            // Hitches: where the slow frames happen (time since the bot took over, altitude).
            var spikes = new StringBuilder();
            int spikeCount = 0;
            for (int i = 0; i < frameTimes.Count; i++)
            {
                if (frameTimes[i] < 0.025f) continue;
                if (spikeCount++ < 12) spikes.Append($" {1000f * frameTimes[i]:0}ms@{frameClock[i]:0.0}s/{frameAltitudes[i]:0}m");
            }
            sb.AppendLine($"frames > 25 ms: {spikeCount}{spikes}");
            var buckets = new SortedDictionary<int, int>();
            for (int i = 0; i < frameTimes.Count; i++)
                if (frameTimes[i] >= 0.025f) { int k = Mathf.FloorToInt(frameClock[i] / 2f) * 2; buckets[k] = buckets.TryGetValue(k, out int n) ? n + 1 : 1; }
            var bl = new StringBuilder("slow frames per 2 s:");
            foreach (var kv in buckets) bl.Append($" {kv.Key}s={kv.Value}");
            sb.AppendLine(bl.ToString());
            File.WriteAllText(benchFile, sb.ToString());
            Debug.Log("[Benchmark]\n" + sb);
            Application.Quit(0);
        }

        string Stats(string label, float minAlt, float maxAlt)
        {
            var dts = new List<float>();
            for (int i = 0; i < frameTimes.Count; i++)
                if (frameAltitudes[i] >= minAlt && frameAltitudes[i] < maxAlt) dts.Add(frameTimes[i]);
            if (dts.Count == 0) return $"{label}: no frames";
            float total = 0f;
            foreach (float dt in dts) total += dt;
            dts.Sort();
            int worst = Mathf.Max(1, dts.Count / 100);
            float worstSum = 0f;
            for (int i = dts.Count - worst; i < dts.Count; i++) worstSum += dts[i];
            return $"{label}: frames {dts.Count}, avg {dts.Count / total:0.0} FPS ({1000f * total / dts.Count:0.00} ms), " +
                   $"1% low {worst / worstSum:0.0} FPS, median {1f / dts[dts.Count / 2]:0.0} FPS, worst frame {1000f * dts[dts.Count - 1]:0.0} ms";
        }

        void Next()
        {
            m3Stage++;
            m3StageAt = Time.unscaledTime;
        }

        void M3Shot(string name)
        {
            string path = Path.Combine(folder, $"m3_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[AutoCapture] {path} thrust={(effects != null ? effects.ThrustFraction : 0f):0.00} alt={session.Rocket.Altitude:0.0}");
            lastShotFrame = Time.frameCount;
            Next();
        }

        void Shot(string name)
        {
            if (lastShotFrame == Time.frameCount) return; // one capture per frame; retried next frame
            lastShotFrame = Time.frameCount;
            string path = Path.Combine(folder, easy ? $"easy_{name}.png" : $"m1_hud_{prefix}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[AutoCapture] {path}");
            stage++;
        }
    }
}
