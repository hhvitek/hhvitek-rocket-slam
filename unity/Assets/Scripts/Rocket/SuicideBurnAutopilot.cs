using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// Simple landing autopilot driving the real <see cref="RocketController"/> through its public commands
    /// (same interface as the player). Used by PlayMode tests and screenshot automation to prove the level is
    /// winnable; later it can become the beginner "assistant".
    /// <para>Vertical: free-fall until a full-throttle stop needs ~80 % of the remaining height, then track a
    /// descent-speed profile v(h) = -max(1, min(sqrt(2·a·h), 0.5·h + 1)). Because even minimum throttle beats
    /// gravity, the engine is switched off whenever the required throttle drops well below the minimum.</para>
    /// <para>Lateral: PD on horizontal position/velocity → desired tilt (≤ 8°) → PD attitude controller mapped
    /// onto the two gimbal axes.</para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SuicideBurnAutopilot : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [Tooltip("World position of the landing target (pad centre).")]
        public Vector3 target;
        [Tooltip("Fraction of max deceleration assumed when deciding when to ignite (lower = earlier).")]
        public float ignitionMargin = 0.8f;
        [Tooltip("Reference deceleration of the descent profile, m/s².")]
        public float profileDeceleration = 9f;
        public float legDeployAltitude = 400f;
        public float maxTiltDeg = 8f;
        [Tooltip("Log a telemetry line every half second (tests / tuning).")]
        public bool logTelemetry;

        float nextLog;

        bool burnStarted;

        public void Bind(RocketController r, Vector3 targetPosition)
        {
            rocket = r;
            target = targetPosition;
            burnStarted = false;
        }

        void OnEnable() => burnStarted = false;

        void FixedUpdate()
        {
            if (rocket == null || rocket.ControlsLocked) return;
            RocketConfig c = rocket.Config;
            float g = -Physics.gravity.y;
            float h = rocket.Altitude;
            Vector3 v = rocket.RelativeVelocity; // relative to the deck under the stage (= world on a ground pad)
            float m = rocket.Mass;

            if (rocket.Legs == LegsState.Stowed && h < legDeployAltitude) rocket.DeployLegs();

            // ---- vertical ----
            float maxNetDecel = c.maxThrustN / m - g;
            if (!burnStarted && v.y < 0f)
            {
                float stopDistance = v.y * v.y / (2f * maxNetDecel * ignitionMargin);
                if (h <= stopDistance) burnStarted = true;
            }

            float throttleCmd = 0f;
            if (burnStarted)
            {
                float hp = Mathf.Max(0f, h);
                float vTarget = -Mathf.Max(1f, Mathf.Min(Mathf.Sqrt(2f * profileDeceleration * hp), 0.5f * hp + 1f));
                float accelCmd = g + 2.5f * (vTarget - v.y); // upward acceleration the thrust must provide
                throttleCmd = m * accelCmd / c.maxThrustN;
                // thrust is not purely vertical when tilted
                throttleCmd /= Mathf.Max(0.5f, Vector3.Dot(rocket.Up, Vector3.up));
            }

            if (throttleCmd >= c.minThrottle * 0.75f && rocket.FuelKg > 0f)
            {
                rocket.SetThrottle(throttleCmd);
                if (!rocket.EngineOn) rocket.SetEngine(true);
            }
            else if (rocket.EngineOn)
            {
                rocket.SetEngine(false);
            }

            // ---- lateral ----
            Vector2 gimbal = LateralGimbal(rocket, target, Vector3.zero, maxTiltDeg);

            if (logTelemetry && Time.time >= nextLog)
            {
                nextLog = Time.time + 0.5f;
                Vector3 errH = target - rocket.Position;
                Vector3 up = rocket.Up;
                Debug.Log($"[Autopilot] t={Time.time:0.0} h={h:0.0} vy={v.y:0.0} vh=({v.x:0.0},{v.z:0.0}) err=({errH.x:0.0},{errH.z:0.0}) " +
                          $"tilt={rocket.TiltDeg:0.0} up=({up.x:0.000},{up.z:0.000}) eng={rocket.EngineOn} thr={rocket.Throttle:0.00} g=({gimbal.x:0.00},{gimbal.y:0.00}) fuel={rocket.FuelKg:0}");
            }
            rocket.SetGimbal(gimbal);
        }

        /// <summary>
        /// Lateral guidance shared with the Easy-mode assist: PD on horizontal position/velocity (plus an optional
        /// extra horizontal acceleration request, e.g. the player's nudge) → desired tilt → PD attitude controller
        /// mapped onto the two steering axes (gimbal + grid fins + RCS). Returns the steering input in [-1, 1]².
        /// </summary>
        public static Vector2 LateralGimbal(RocketController rocket, Vector3 target, Vector3 extraAccelH, float maxTiltDeg)
        {
            RocketConfig c = rocket.Config;
            float g = -Physics.gravity.y;
            float h = rocket.Altitude;
            Vector3 v = rocket.RelativeVelocity;
            float m = rocket.Mass;

            Vector3 errH = target - rocket.Position; errH.y = 0f;
            Vector3 velH = v; velH.y = 0f;
            // Outer loop must stay slower than the attitude loop, which is weak at low throttle.
            bool final = h < 60f;
            float kp = final ? 0.03f : 0.08f;
            float kd = final ? 0.45f : 0.5f;
            float tiltLimit = final ? Mathf.Min(4f, maxTiltDeg) : maxTiltDeg;
            float thrustAccel = Mathf.Max(g, rocket.CurrentThrust / m);
            // Feed the wind forward: request the acceleration that cancels its push (a steady crosswind would
            // otherwise leave a standing offset of a/kp, ~10 m in the final phase).
            extraAccelH -= rocket.WindForce / m;
            extraAccelH.y = 0f;
            Vector3 accelH = Vector3.ClampMagnitude(kp * errH - kd * velH + extraAccelH, thrustAccel * Mathf.Tan(tiltLimit * Mathf.Deg2Rad));
            Vector3 desiredUp = (Vector3.up * thrustAccel + accelH).normalized;

            Vector3 up = rocket.Up;
            Vector3 err = Vector3.Cross(up, desiredUp);           // axis * sin(angle)
            Vector3 w = rocket.Body.angularVelocity;
            Vector3 alphaCmd = 4f * err - 3f * w;
            Vector3 inertia = RocketPhysics.InertiaTensor(c, m);
            Vector3 torque = alphaCmd * inertia.x;
            torque -= Vector3.Dot(torque, up) * up;                // gimbal cannot roll the stage

            // Input axes: +x tips the nose to local +x (torque about local -z), +y tips it to +z (torque about +x).
            Vector3 local = Quaternion.Inverse(rocket.Rotation) * torque;
            float authority = Mathf.Max(1f, rocket.ControlTorqueAuthority);
            return new Vector2(Mathf.Clamp(-local.z / authority, -1f, 1f), Mathf.Clamp(local.x / authority, -1f, 1f));
        }
    }
}
