using RocketSlam.Cameras;
using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RocketSlam.Gameplay
{
    /// <summary>How the player flies.</summary>
    public enum ControlMode
    {
        /// <summary>Default: hold one key to fire, the <see cref="EasyAssist"/> steers, limits thrust and cues the burn.</summary>
        Easy,
        /// <summary>The realistic controls: engine on/off, throttle (40 % minimum), camera-relative steering, legs.</summary>
        Manual,
    }

    /// <summary>
    /// Routes the "Rocket" action map of <c>Assets/Input/RocketControls.inputactions</c> to the rocket (Manual) or
    /// the Easy assist, the session (start screen, pause, restart) and the camera director. M toggles the mode; the
    /// starting mode comes from the settings (Easy assist on/off). On the start screen: any key starts, 1-5 pick an
    /// unlocked level, Esc goes back to the main menu. While paused or after the result, <c>LevelMenus</c> owns the keys.
    /// Disable it to let an autopilot fly (this also turns the Easy assist off and dismisses the start screen).
    /// </summary>
    public class PlayerControls : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;
        [SerializeField] RocketController rocket;
        [SerializeField] LandingSession session;
        [SerializeField] CameraDirector cameras;
        [SerializeField] EasyAssist assist;
        [SerializeField] ControlMode mode = ControlMode.Easy;

        InputActionMap map;
        bool waitForThrustRelease;

        /// <summary>Raw gimbal stick/arrows value (camera-relative: x = screen right, y = away from camera).</summary>
        public Vector2 GimbalInput { get; private set; }
        InputAction thrust, engine, throttle, throttleWheel, throttleMax, throttleMin, gimbal, legs, cameraSwitch, pause, restart, modeSwitch;

        public EasyAssist Assist => assist;
        /// <summary>The Restart action (R / gamepad Select) fired this frame; the pause menu and results use it too.</summary>
        public bool RestartPressedThisFrame => isActiveAndEnabled && restart != null && restart.WasPressedThisFrame();

        public ControlMode Mode
        {
            get => mode;
            set
            {
                mode = value;
                ApplyMode();
            }
        }

        public void Setup(InputActionAsset asset, RocketController r, LandingSession s, CameraDirector c, EasyAssist a)
        {
            actions = asset;
            rocket = r;
            session = s;
            cameras = c;
            assist = a;
        }

        void Awake()
        {
            mode = SaveGame.Settings.easyAssist ? ControlMode.Easy : ControlMode.Manual;
            map = actions.FindActionMap("Rocket", throwIfNotFound: true);
            thrust = map.FindAction("Thrust", true);
            engine = map.FindAction("Engine", true);
            throttle = map.FindAction("Throttle", true);
            throttleWheel = map.FindAction("ThrottleWheel", true);
            throttleMax = map.FindAction("ThrottleMax", true);
            throttleMin = map.FindAction("ThrottleMin", true);
            gimbal = map.FindAction("Gimbal", true);
            legs = map.FindAction("Legs", true);
            cameraSwitch = map.FindAction("Camera", true);
            pause = map.FindAction("Pause", true);
            restart = map.FindAction("Restart", true);
            modeSwitch = map.FindAction("Mode", true);
        }

        void OnEnable()
        {
            map?.Enable();
            ApplyMode();
        }

        void OnDisable()
        {
            map?.Disable();
            if (assist != null) assist.enabled = false;
            if (rocket != null) rocket.SetGimbal(Vector2.zero);
            if (session != null) session.DismissIntro(); // nobody could press a key any more
        }

        void ApplyMode()
        {
            if (assist != null) assist.enabled = isActiveAndEnabled && mode == ControlMode.Easy;
            if (rocket == null) return;
            rocket.SetGimbal(Vector2.zero);
            if (mode == ControlMode.Easy) waitForThrustRelease = thrust != null && thrust.IsPressed();
        }

        void Update()
        {
            if (session.IntroVisible)
            {
                UpdateIntro();
                return;
            }
            // Pause menu and results screen: LevelMenus handles every key.
            if (session.Evaluator.IsFinished || session.IsPaused) return;
            if (restart.WasPressedThisFrame())
            {
                Restart();
                return;
            }
            if (pause.WasPressedThisFrame())
            {
                session.SetPaused(true);
                return;
            }

            if (cameraSwitch.WasPressedThisFrame() && cameras != null) cameras.Toggle();
            if (modeSwitch.WasPressedThisFrame()) Mode = mode == ControlMode.Easy ? ControlMode.Manual : ControlMode.Easy;
            if (legs.WasPressedThisFrame()) rocket.DeployLegs();
            GimbalInput = gimbal.ReadValue<Vector2>();

            if (mode == ControlMode.Easy)
                UpdateEasy();
            else
                UpdateManual();
        }

        void UpdateIntro()
        {
            int picked = LevelKeyPressed();
            if (picked > 0)
            {
                // 1-5: another unlocked level; the current one (or a locked one) keeps this start screen.
                if (picked != LevelLoader.CurrentLevel && SaveGame.Current.IsUnlocked(picked)) LevelLoader.Load(picked);
                return;
            }
            Keyboard k = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if ((k != null && k.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame))
            {
                LevelLoader.LoadMenu();
                return;
            }
            if (modeSwitch.WasPressedThisFrame()) Mode = mode == ControlMode.Easy ? ControlMode.Manual : ControlMode.Easy;
            else if (AnyKeyPressed()) StartGame();
        }

        /// <summary>New attempt in the same level (R, pause menu, results screen).</summary>
        public void Restart()
        {
            session.Restart();
            if (cameras != null) cameras.Snap();
            GimbalInput = Vector2.zero;
        }

        /// <summary>1-5 on the main row or the numpad: that level number, else 0.</summary>
        static int LevelKeyPressed()
        {
            Keyboard k = Keyboard.current;
            if (k == null) return 0;
            for (int i = 1; i <= LevelLoader.LevelCount; i++)
            {
                if (k[(Key)((int)Key.Digit1 + i - 1)].wasPressedThisFrame) return i;
                if (k[(Key)((int)Key.Numpad1 + i - 1)].wasPressedThisFrame) return i;
            }
            return 0;
        }

        void StartGame()
        {
            session.DismissIntro();
            // The key that closed the start screen (often Space) must not fire the engine until it is let go.
            waitForThrustRelease = true;
        }

        void UpdateEasy()
        {
            bool held = thrust.IsPressed();
            if (waitForThrustRelease && !held) waitForThrustRelease = false;
            assist.ThrustHeld = held && !waitForThrustRelease;
            assist.Nudge = CameraRelative(GimbalInput);
        }

        void UpdateManual()
        {
            if (engine.WasPressedThisFrame()) rocket.ToggleEngine();
            rocket.AdjustThrottle(throttle.ReadValue<float>(), Time.deltaTime);
            float wheel = throttleWheel.ReadValue<float>();
            if (Mathf.Abs(wheel) > 0.01f) rocket.SetThrottle(rocket.Throttle + Mathf.Sign(wheel) * 0.1f); // 10 % per notch
            if (throttleMax.WasPressedThisFrame()) rocket.SetThrottle(1f);
            if (throttleMin.WasPressedThisFrame()) rocket.SetThrottle(0f); // clamps to the 40 % minimum
            rocket.SetGimbalWorld(CameraRelative(GimbalInput));
        }

        static bool AnyKeyPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            Gamepad pad = Gamepad.current;
            if (pad != null)
                foreach (InputControl control in pad.allControls)
                    if (control is ButtonControl button && !button.synthetic && button.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Map stick input onto the horizontal plane using the active camera's heading.</summary>
        Vector3 CameraRelative(Vector2 input)
        {
            Transform cam = cameras != null && cameras.ActiveCamera != null ? cameras.ActiveCamera.transform : null;
            if (cam == null) return new Vector3(input.x, 0f, input.y);
            Vector3 right = cam.right; right.y = 0f;
            Vector3 away = cam.forward + cam.up; away.y = 0f; // screen-up projected; works even looking straight down
            if (right.sqrMagnitude < 1e-6f || away.sqrMagnitude < 1e-6f) return new Vector3(input.x, 0f, input.y);
            return right.normalized * input.x + away.normalized * input.y;
        }
    }
}
