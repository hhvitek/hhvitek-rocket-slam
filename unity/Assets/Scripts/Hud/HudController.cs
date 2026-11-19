using RocketSlam.Cameras;
using RocketSlam.Gameplay;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.UIElements;

namespace RocketSlam.Hud
{
    /// <summary>
    /// Fills the UI Toolkit HUD (<c>Assets/UI/Hud.uxml</c>) from the rocket and session. Runs in LateUpdate
    /// after the camera has moved so the screen-space pad marker and predicted-impact reticle line up.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(UIDocument))]
    public class HudController : MonoBehaviour
    {
        [SerializeField] LandingSession session;
        [SerializeField] CameraDirector cameras;
        [SerializeField] PlayerControls controls;

        VisualElement root;
        Label alt, vspeed, hspeed, tilt, legs, engineState, throttle, fuel, targetDist;
        Label camName, padLabel, steerSource;
        VisualElement steerDot;
        VisualElement throttleFill, fuelFill, needle, padMarker, padArrow, impactMarker;
        /// <summary>Seconds the results screen (<c>LevelMenus</c>) waits after an explosion.</summary>
        public const float ResultDelayAfterExplosion = 1.5f;
        VisualElement hudRoot, cue, intro, introEasy, introManual, easyFuelFill;
        Label easyAlt, easyFall, easyFuel, cueMain, cueSub, cueSpeed, modePill, introMode, introStart;
        Label introLevel, introBriefing, introLevels, windSpeed;
        VisualElement windPanel, windArrow;
        int introLevelsFor = -1, introLevelsHints = -1;
        readonly HintLabels hints = new HintLabels(menuContext: false);

        public void Setup(LandingSession s, CameraDirector c, PlayerControls p)
        {
            session = s;
            cameras = c;
            controls = p;
        }

        /// <summary>The text the burn cue currently shows (tests, screenshots).</summary>
        public string CueText => cue != null && cue.resolvedStyle.display != DisplayStyle.None ? cueMain.text : string.Empty;

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            alt = root.Q<Label>("alt");
            vspeed = root.Q<Label>("vspeed");
            hspeed = root.Q<Label>("hspeed");
            tilt = root.Q<Label>("tilt");
            legs = root.Q<Label>("legs");
            engineState = root.Q<Label>("engine-state");
            throttle = root.Q<Label>("throttle");
            fuel = root.Q<Label>("fuel");
            targetDist = root.Q<Label>("target-dist");
            camName = root.Q<Label>("cam-name");
            padLabel = root.Q<Label>("pad-marker-label");
            steerSource = root.Q<Label>("steer-source");
            steerDot = root.Q("steer-dot");
            throttleFill = root.Q("throttle-fill");
            fuelFill = root.Q("fuel-fill");
            needle = root.Q("needle");
            padMarker = root.Q("pad-marker");
            padArrow = root.Q("pad-arrow");
            impactMarker = root.Q("impact-marker");
            hudRoot = root.Q("hud-root");
            cue = root.Q("cue");
            cueMain = root.Q<Label>("cue-main");
            cueSub = root.Q<Label>("cue-sub");
            cueSpeed = root.Q<Label>("cue-speed");
            easyAlt = root.Q<Label>("easy-alt");
            easyFall = root.Q<Label>("easy-fall");
            easyFuel = root.Q<Label>("easy-fuel");
            easyFuelFill = root.Q("easy-fuel-fill");
            modePill = root.Q<Label>("mode-pill");
            intro = root.Q("intro");
            introEasy = root.Q("intro-easy");
            introManual = root.Q("intro-manual");
            introMode = root.Q<Label>("intro-mode");
            introStart = root.Q<Label>("intro-start");
            introLevel = root.Q<Label>("intro-level");
            introBriefing = root.Q<Label>("intro-briefing");
            introLevels = root.Q<Label>("intro-levels");
            windPanel = root.Q("wind-panel");
            windArrow = root.Q("wind-arrow");
            windSpeed = root.Q<Label>("wind-speed");
        }

        void LateUpdate()
        {
            if (session == null || alt == null) return;
            hints.Update(root);
            RocketController r = session.Rocket;
            RocketConfig c = r.Config;
            LandingCriteria rules = c.landing;
            // Speeds and tilt relative to the landing surface (what the landing rules judge; = world on the ground).
            Vector3 v = r.RelativeVelocity;
            float hs = new Vector2(v.x, v.z).magnitude;

            alt.text = $"{Mathf.Max(0f, r.Altitude):0} m";
            vspeed.text = $"{v.y:0.0} m/s";
            Mark(vspeed, Mathf.Max(0f, -v.y), rules.maxVerticalSpeed);
            hspeed.text = $"{hs:0.0} m/s";
            Mark(hspeed, hs, rules.maxHorizontalSpeed);
            tilt.text = $"{r.RelativeTiltDeg:0.0}°";
            Mark(tilt, r.RelativeTiltDeg, rules.maxTiltDeg);

            switch (r.Legs)
            {
                case LegsState.Stowed: legs.text = "STOWED"; break;
                case LegsState.Deploying: legs.text = $"DEPLOYING {r.LegDeployProgress * 100f:0}%"; break;
                default: legs.text = "DEPLOYED"; break;
            }
            legs.EnableInClassList("good", r.Legs == LegsState.Deployed);
            legs.EnableInClassList("warn", r.Legs != LegsState.Deployed);

            engineState.text = r.EngineOn ? "ON" : r.FuelKg <= 0f ? "NO FUEL" : "OFF";
            engineState.EnableInClassList("on", r.EngineOn);
            throttle.text = $"{r.Throttle * 100f:0} %";
            throttleFill.style.width = Length.Percent(r.Throttle * 100f);
            float fuelFrac = r.StartFuelKg > 0f ? r.FuelKg / r.StartFuelKg : 0f;
            fuel.text = $"{r.FuelKg:#,0} kg";
            fuel.EnableInClassList("danger", fuelFrac < 0.1f);
            fuel.EnableInClassList("warn", fuelFrac >= 0.1f && fuelFrac < 0.3f);
            fuelFill.style.width = Length.Percent(fuelFrac * 100f);

            Vector2 toTarget = session.HorizontalToTarget;
            targetDist.text = $"{toTarget.magnitude:0} m";
            Camera cam = cameras != null ? cameras.ActiveCamera : null;
            float yaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            float bearing = Mathf.Atan2(toTarget.x, toTarget.y) * Mathf.Rad2Deg - yaw;
            needle.style.rotate = new Rotate(new Angle(bearing, AngleUnit.Degree));
            UpdateWind(yaw);

            if (cam != null) UpdateWorldMarkers(cam, r);
            if (cam != null) UpdateSteering(cam, r);

            if (cameras != null)
            {
                camName.text = CameraDirector.DisplayName(cameras.View);
                camName.EnableInClassList("visible", Time.unscaledTime - cameras.ViewChangedAt < 1.5f);
            }

            bool easy = controls == null || controls.Mode == ControlMode.Easy;
            hudRoot.EnableInClassList("mode-easy", easy);
            hudRoot.EnableInClassList("mode-manual", !easy);
            modePill.text = easy ? InputHints.Pick("EASY MODE   (M = manual)", "EASY MODE   (X = manual)")
                                 : InputHints.Pick("MANUAL MODE   (M = easy)", "MANUAL MODE   (X = easy)");
            UpdateEasyPanel(r, rules, fuelFrac);

            LandingEvaluator e = session.Evaluator;
            bool finished = e.IsFinished;
            UpdateCue(easy && !finished && !session.IntroVisible, r, rules);
            // Pause menu and results: LevelMenus.

            intro.EnableInClassList("visible", session.IntroVisible);
            if (session.IntroVisible)
            {
                introEasy.style.display = easy ? DisplayStyle.Flex : DisplayStyle.None;
                introManual.style.display = easy ? DisplayStyle.None : DisplayStyle.Flex;
                introMode.text = easy
                    ? "EASY MODE: the rocket steers itself. You only fire the engine."
                    : InputHints.Pick("MANUAL MODE: you fly everything yourself. Press M for easy mode.",
                                      "MANUAL MODE: you fly everything yourself. Press X for easy mode.");
                introStart.EnableInClassList("dim", Mathf.Repeat(Time.unscaledTime, 1.2f) > 0.8f);
                LevelConfig level = session.Level;
                if (level != null)
                {
                    introLevel.text = $"LEVEL {level.number} / {LevelLoader.LevelCount}:  {level.displayName}";
                    introBriefing.text = level.briefing;
                }
                if (introLevelsFor != LevelLoader.CurrentLevel || introLevelsHints != InputHints.Version)
                {
                    introLevelsFor = LevelLoader.CurrentLevel;
                    introLevelsHints = InputHints.Version;
                    int unlocked = 1;
                    for (int n = 2; n <= LevelLoader.LevelCount; n++)
                        if (SaveGame.Current.IsUnlocked(n)) unlocked = n;
                    introLevels.text = InputHints.UsingGamepad ? "B: main menu"
                        : unlocked > 1 ? $"Keys 1 - {unlocked}: choose a level   ·   Esc: main menu" : "Esc: main menu";
                }
            }
        }

        void UpdateEasyPanel(RocketController r, LandingCriteria rules, float fuelFrac)
        {
            float fall = Mathf.Max(0f, -r.RelativeVelocity.y);
            easyAlt.text = $"{Mathf.Max(0f, r.Altitude):0} m";
            easyFall.text = SpeedText(fall);
            Mark(easyFall, fall, rules.maxVerticalSpeed);
            easyFuel.text = $"{fuelFrac * 100f:0} %";
            easyFuel.EnableInClassList("danger", fuelFrac < 0.1f);
            easyFuelFill.style.width = Length.Percent(fuelFrac * 100f);
        }

        /// <summary>The big centred burn cue (Easy mode only).</summary>
        void UpdateCue(bool show, RocketController r, LandingCriteria rules)
        {
            EasyAssist assist = controls != null ? controls.Assist : null;
            CueKind kind = show && assist != null && assist.isActiveAndEnabled ? assist.Cue.kind : CueKind.None;
            cue.style.display = kind == CueKind.None ? DisplayStyle.None : DisplayStyle.Flex;
            cueMain.RemoveFromClassList("fire-now");
            cueMain.RemoveFromClassList("blink");
            cueMain.RemoveFromClassList("holding");
            cueMain.RemoveFromClassList("early");
            cueMain.RemoveFromClassList("touchdown");
            float fall = Mathf.Max(0f, -r.RelativeVelocity.y);
            cueSpeed.text = "";
            switch (kind)
            {
                case CueKind.FireIn:
                    float d = assist.Cue.distance;
                    cueMain.text = $"FIRE IN {(d >= 1000f ? $"{d / 1000f:0.0} km" : $"{d:0} m")}";
                    cueSub.text = InputHints.Pick("then HOLD SPACE", "then HOLD A / RT");
                    break;
                case CueKind.FireNow:
                    cueMain.text = InputHints.Pick("FIRE NOW - HOLD SPACE", "FIRE NOW - HOLD A / RT");
                    cueMain.AddToClassList("fire-now");
                    cueMain.EnableInClassList("blink", Mathf.Repeat(Time.unscaledTime, 0.5f) > 0.25f);
                    cueSub.text = "";
                    break;
                case CueKind.KeepHolding:
                    cueMain.text = InputHints.Pick("KEEP HOLDING SPACE", "KEEP HOLDING A / RT");
                    cueMain.AddToClassList("holding");
                    cueSub.text = "the engine never pushes you up";
                    break;
                case CueKind.TooEarly:
                    cueMain.text = "TOO EARLY - RELEASE";
                    cueMain.AddToClassList("early");
                    cueSub.text = "save fuel, wait for FIRE NOW";
                    break;
                case CueKind.Touchdown:
                    // Green = slow enough to land; amber = the engine is still easing the fall as planned
                    // (the allowed fall speed shrinks towards the ground); red = falling too fast.
                    bool ok = fall <= rules.maxVerticalSpeed;
                    bool onTrack = !ok && assist.ThrustHeld && fall <= EasyFlight.FloorSpeed(r.Config, r.Altitude) * 1.25f + 1f;
                    cueMain.text = $"TOUCHDOWN: keep under {rules.maxVerticalSpeed:0.0} m/s";
                    cueMain.AddToClassList("touchdown");
                    cueSpeed.text = SpeedText(fall);
                    cueSpeed.EnableInClassList("good", ok);
                    cueSpeed.EnableInClassList("warn", onTrack);
                    cueSpeed.EnableInClassList("danger", !ok && !onTrack);
                    cueSub.text = ok ? InputHints.Pick("good - hold SPACE if it creeps up", "good - hold A / RT if it creeps up")
                                : onTrack ? InputHints.Pick("slowing down - keep holding SPACE", "slowing down - keep holding A / RT")
                                : InputHints.Pick("too fast! HOLD SPACE", "too fast! HOLD A / RT");
                    break;
            }
            cueSub.style.display = cueSub.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            cueSpeed.style.display = cueSpeed.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string SpeedText(float speed) => speed >= 10f ? $"{speed:0} m/s" : $"{speed:0.0} m/s";

        /// <summary>One plain-language hint on what to do differently next time.</summary>
        public static string Tip(LandingEvaluator e, bool easy)
        {
            if (e.State == LandingState.Success) return "Well done!";
            switch (e.Reason)
            {
                case FailureReason.HardImpact:
                    if (e.TouchdownVerticalSpeed > 30f)
                        return easy ? $"You fell too fast. Hold {InputHints.Fire} as soon as FIRE NOW appears." : "Fire the engine earlier to brake.";
                    if (e.TouchdownVerticalSpeed > e.Criteria.maxVerticalSpeed)
                        return easy ? $"Almost! Keep holding {InputHints.Fire} all the way down." : "Brake a bit more just before touchdown.";
                    return easy ? InputHints.Pick("Too much sideways speed. Let go of the arrows near the ground.", "Too much sideways speed. Let go of the stick near the ground.") : "Kill sideways speed before touchdown.";
                case FailureReason.OutOfFuel:
                    return easy ? $"You fired too early. Wait for FIRE NOW, then hold {InputHints.Fire}." : "You ran out of fuel. Burn later and shorter.";
                case FailureReason.NoLegs: return InputHints.Pick("Press G", "Press Y") + " to lower the landing legs before touchdown.";
                case FailureReason.OffPad: return easy ? InputHints.Pick("Do not push the arrows; the rocket aims for the pad by itself.", "Do not push the stick; the rocket aims for the pad by itself.") : "Steer over the yellow target before touching down.";
                case FailureReason.TooTilted:
                case FailureReason.TippedOver: return "Land upright: keep the rocket straight in the last seconds.";
                default: return "";
            }
        }

        /// <summary>Pad marker (clamped to the screen edge with an arrow when off-screen) and the
        /// ballistic impact reticle.</summary>
        void UpdateWorldMarkers(Camera cam, RocketController r)
        {
            float w = root.layout.width, h = root.layout.height;
            if (float.IsNaN(w) || w <= 0f) return;

            Vector3 pad = session.TargetPosition;
            Vector3 vp = cam.WorldToViewportPoint(pad);
            bool behind = vp.z < 0f;
            Vector2 p = new Vector2(vp.x, vp.y);
            if (behind) p = Vector2.one - p;
            const float margin = 0.06f;
            bool onScreen = !session.Evaluator.IsFinished && !behind && p.x > margin && p.x < 1f - margin && p.y > margin && p.y < 1f - margin;
            float range = Vector3.Distance(r.transform.position, pad);
            string label = session.Site.Label;
            padLabel.text = range >= 1000f ? $"{label} {range / 1000f:0.0} km" : $"{label} {range:0} m";
            padMarker.style.display = onScreen ? DisplayStyle.Flex : DisplayStyle.None;
            padArrow.style.display = onScreen || session.Evaluator.IsFinished ? DisplayStyle.None : DisplayStyle.Flex;
            if (onScreen)
            {
                Place(padMarker, p, w, h);
            }
            else
            {
                Vector2 dir = p - new Vector2(0.5f, 0.5f);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                float scale = (0.5f - margin) / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
                Vector2 edge = new Vector2(0.5f, 0.5f) + dir * scale;
                Place(padArrow, edge, w, h);
                // chevron points right at 0 deg (its base rotation is 45 deg); screen y grows downwards
                float angle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
                padArrow.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            }

            // Predicted impact: current deck-relative velocity, gravity only (no drag, no future burn).
            Vector3 pos = r.transform.position;
            Vector3 vel = r.RelativeVelocity; // relative to the (moving) deck: where the stage meets the ship
            float g = -Physics.gravity.y;
            float drop = pos.y - pad.y;
            bool show = !session.Evaluator.IsFinished && drop > 2f && (vel.y < -0.5f || drop > 0f);
            if (show)
            {
                float t = (vel.y + Mathf.Sqrt(vel.y * vel.y + 2f * g * drop)) / g;
                Vector3 impact = new Vector3(pos.x + vel.x * t, pad.y, pos.z + vel.z * t);
                Vector3 ivp = cam.WorldToViewportPoint(impact);
                show = ivp.z > 0f && ivp.x > 0f && ivp.x < 1f && ivp.y > 0f && ivp.y < 1f;
                if (show)
                {
                    Place(impactMarker, ivp, w, h);
                    impactMarker.EnableInClassList("on-pad", session.Site.IsOver(impact));
                }
            }
            impactMarker.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Wind indicator (levels with wind): arrow = where the wind blows to, relative to the camera heading.</summary>
        void UpdateWind(float cameraYaw)
        {
            LevelRuntime rt = session.Runtime;
            bool show = rt != null && rt.HasWind;
            windPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            Vector3 w = rt.CurrentWind;
            float speed = new Vector2(w.x, w.z).magnitude;
            windSpeed.text = $"{speed:0} m/s";
            float bearing = Mathf.Atan2(w.x, w.z) * Mathf.Rad2Deg - cameraYaw;
            windArrow.style.rotate = new Rotate(new Angle(bearing, AngleUnit.Degree));
        }

        /// <summary>Steering indicator: the commanded tilt direction in screen terms, plus which effectors act.</summary>
        void UpdateSteering(Camera cam, RocketController r)
        {
            Vector3 steer = r.SteerWorld;
            Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
            Vector3 away = cam.transform.forward + cam.transform.up; away.y = 0f; away.Normalize();
            float x = Mathf.Clamp(Vector3.Dot(steer, right), -1f, 1f);
            float y = Mathf.Clamp(Vector3.Dot(steer, away), -1f, 1f);
            steerDot.style.left = Length.Percent(50f + 42f * x);
            steerDot.style.top = Length.Percent(50f - 42f * y);
            steerDot.EnableInClassList("active", steer.sqrMagnitude > 0.01f);

            float gimbal = r.GimbalTorqueAuthority, fins = r.FinTorqueAuthority;
            string text = fins > r.Config.rcsTorqueNm ? "FINS" : "RCS";
            if (gimbal > 0f) text = "GIMBAL + " + text;
            steerSource.text = text;
        }

        static void Place(VisualElement el, Vector2 viewport, float w, float h)
        {
            el.style.left = viewport.x * w;
            el.style.top = (1f - viewport.y) * h;
        }

        /// <summary>Green when within the touchdown limit, amber up to 4x, red beyond.</summary>
        static void Mark(VisualElement el, float value, float limit)
        {
            el.EnableInClassList("good", value <= limit);
            el.EnableInClassList("warn", value > limit && value <= limit * 4f);
            el.EnableInClassList("danger", value > limit * 4f);
        }
    }
}
