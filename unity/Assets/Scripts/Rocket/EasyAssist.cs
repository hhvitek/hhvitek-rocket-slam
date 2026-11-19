using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// Easy mode (the default): the player only holds one key. While <see cref="ThrustHeld"/> the engine ramps up
    /// (<c>easyThrottleRampTime</c>), capped by <see cref="EasyFlight.ThrottleCap"/> so the stage can never climb;
    /// releasing ramps it down and shuts it off (except in the last metres, where a touchdown guard keeps the fall
    /// gentle once the engine has been fired). Steering is automatic (same guidance as the autopilot, towards the
    /// pad, plus an optional nudge), the legs deploy by themselves, and <see cref="Cue"/> tells the player when to fire.
    /// Runs before <see cref="RocketController"/> so its commands apply in the same physics step.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public class EasyAssist : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [Tooltip("World position of the landing target (pad centre).")]
        public Vector3 target;
        public float maxTiltDeg = 8f;

        float command; // ramped 0..1 player throttle request
        bool burnEarned;
        bool lit; // the player has fired the engine during this attempt (arms the touchdown guard)

        /// <summary>The thrust key is held (set every frame by PlayerControls or a test driver).</summary>
        public bool ThrustHeld { get; set; }
        /// <summary>Optional steering nudge as a world-horizontal direction (camera-relative arrows), magnitude ≤ ~1.4.</summary>
        public Vector3 Nudge { get; set; }
        /// <summary>The burn cue for the HUD, refreshed every physics step.</summary>
        public BurnCue Cue { get; private set; }
        public RocketController Rocket => rocket;

        public void Bind(RocketController r, Vector3 targetPosition)
        {
            if (rocket != null) rocket.WasReset -= ResetState;
            rocket = r;
            if (isActiveAndEnabled && rocket != null) rocket.WasReset += ResetState;
            target = targetPosition;
            ResetState();
        }

        /// <summary>Forget the ramp and cue history (new attempt).</summary>
        public void ResetState()
        {
            command = 0f;
            burnEarned = false;
            lit = false;
            ThrustHeld = false;
            Nudge = Vector3.zero;
            Cue = default;
        }

        void OnEnable()
        {
            ResetState();
            if (rocket == null) return;
            rocket.FullThrottleRange = true;
            rocket.WasReset += ResetState;
        }

        void OnDisable()
        {
            if (rocket == null) return;
            rocket.WasReset -= ResetState;
            rocket.FullThrottleRange = false;
            rocket.SetGimbal(Vector2.zero);
        }

        void FixedUpdate()
        {
            if (rocket == null) return;
            RocketConfig c = rocket.Config;
            float dt = Time.fixedDeltaTime;
            float g = -Physics.gravity.y;
            float h = rocket.Altitude;
            Vector3 v = rocket.Velocity;
            // Vertical speed relative to the (possibly heaving) deck under the stage; the cap also keeps the world
            // fall speed at its floor, so the stage never climbs even while the deck rises under it.
            float vRel = rocket.RelativeVelocity.y;
            float m = rocket.Mass;

            if (rocket.ControlsLocked)
            {
                command = 0f;
                Cue = default;
                return;
            }

            if (rocket.Legs == LegsState.Stowed && h < c.easyLegsAltitude) rocket.DeployLegs();

            // ---- vertical: ramped command, capped so the fall never slows below the floor speed ----
            command = Mathf.MoveTowards(command, ThrustHeld ? 1f : 0f, dt / Mathf.Max(0.01f, c.easyThrottleRampTime));
            float dragUp = RocketPhysics.AxialDrag(c, rocket.Airspeed).y; // same vertical drag the rocket feels
            float upDot = Vector3.Dot(rocket.Up, Vector3.up);
            float cap = Mathf.Min(EasyFlight.ThrottleCap(c, m, h, vRel, upDot, dragUp, g),
                                  EasyFlight.NoClimbCap(c, m, v.y, upDot, dragUp, g));
            // Touchdown guard: after the player has fired once, the last metres never free-fall (see GuardThrottle).
            if (ThrustHeld) lit = true;
            float guard = lit ? EasyFlight.GuardThrottle(c, m, h, vRel, upDot, dragUp, g) : 0f;
            float throttle = Mathf.Min(Mathf.Max(command, guard), cap);
            if (throttle > 0.005f && rocket.FuelKg > 0f)
            {
                rocket.SetThrottle(throttle);
                if (!rocket.EngineOn) rocket.SetEngine(true);
            }
            else if (rocket.EngineOn)
            {
                rocket.SetEngine(false);
            }

            // ---- lateral: automatic guidance to the pad, plus the player's nudge ----
            Vector3 nudge = Vector3.ClampMagnitude(new Vector3(Nudge.x, 0f, Nudge.z), 1f) * c.easyNudgeAccel;
            rocket.SetGimbal(SuicideBurnAutopilot.LateralGimbal(rocket, target, nudge, maxTiltDeg));

            // ---- cue ----
            BurnCue cue = EasyFlight.Evaluate(c, m, h, vRel, ThrustHeld, burnEarned, g);
            if (vRel < 0f && h <= cue.fireAltitude) burnEarned = true;
            Cue = cue;
        }
    }
}
