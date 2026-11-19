using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// Pure physics formulas used by <see cref="RocketController"/>. No Unity objects, so EditMode tests can
    /// check the numbers directly.
    /// </summary>
    public static class RocketPhysics
    {
        /// <summary>Clamp a throttle setpoint to what a running engine can do: [minThrottle, 1].</summary>
        public static float ClampThrottle(RocketConfig c, float throttle) => ClampThrottle(throttle, c.minThrottle);

        /// <summary>Clamp a throttle setpoint to [minThrottle, 1] for an explicit minimum (Easy mode uses 0).</summary>
        public static float ClampThrottle(float throttle, float minThrottle) => Mathf.Clamp(throttle, Mathf.Clamp01(minThrottle), 1f);

        /// <summary>Thrust in newtons. Zero when the engine is off; otherwise the throttle is clamped to [min, 1].</summary>
        public static float Thrust(RocketConfig c, bool engineOn, float throttle) => Thrust(c, engineOn, throttle, c.minThrottle);

        public static float Thrust(RocketConfig c, bool engineOn, float throttle, float minThrottle)
            => engineOn ? c.maxThrustN * ClampThrottle(throttle, minThrottle) : 0f;

        /// <summary>Propellant flow in kg/s, proportional to the (clamped) throttle; zero when off.</summary>
        public static float BurnRate(RocketConfig c, bool engineOn, float throttle) => BurnRate(c, engineOn, throttle, c.minThrottle);

        public static float BurnRate(RocketConfig c, bool engineOn, float throttle, float minThrottle)
            => engineOn ? c.burnRateAtFullKgPerS * ClampThrottle(throttle, minThrottle) : 0f;

        /// <summary>Total mass in kg (dry + remaining propellant).</summary>
        public static float Mass(RocketConfig c, float fuelKg) => c.dryMassKg + Mathf.Max(0f, fuelKg);

        /// <summary>Drag force ½ρv²·Cd·A, pointing against the velocity.</summary>
        public static Vector3 Drag(RocketConfig c, Vector3 velocity)
        {
            float k = 0.5f * c.airDensity * c.dragCoefficient * c.dragAreaM2;
            return -k * velocity.magnitude * velocity;
        }

        /// <summary>
        /// End-on (axial) drag of the near-vertical stage: ½ρ·Cd·A·|v_y|·v_y on the vertical airspeed only. The
        /// horizontal airspeed is handled once, by <see cref="SideWindForce"/> on the body side area; the two together
        /// make the stage's whole aerodynamic drag (wind is horizontal, so the vertical airspeed is the vertical velocity).
        /// </summary>
        public static Vector3 AxialDrag(RocketConfig c, Vector3 airspeed)
        {
            float k = 0.5f * c.airDensity * c.dragCoefficient * c.dragAreaM2;
            return new Vector3(0f, -k * Mathf.Abs(airspeed.y) * airspeed.y, 0f);
        }

        /// <summary>
        /// Horizontal crossflow force of the wind on the body side: ½ρ·Cd_side·A_side·|Δ|·Δ with Δ = horizontal
        /// (wind − velocity) and A_side = 2·radius·height (the end-on <see cref="Drag"/> uses only the small frontal
        /// area, which badly underestimates a crosswind on a standing stage). Zero in still air for a stage that does
        /// not move sideways.
        /// </summary>
        public static Vector3 SideWindForce(RocketConfig c, Vector3 velocity, Vector3 wind)
        {
            Vector3 d = wind - velocity;
            d.y = 0f;
            float area = 2f * c.bodyRadius * c.bodyHeight;
            return 0.5f * c.airDensity * c.sideDragCoefficient * area * d.magnitude * d;
        }

        /// <summary>Height of <paramref name="point"/> above the plane through <paramref name="surfacePoint"/> with normal
        /// <paramref name="up"/>, measured vertically (the plane's height under the point itself). Used for the altitude
        /// of each foot over a tilted deck.</summary>
        public static float SurfaceClearance(Vector3 point, Vector3 surfacePoint, Vector3 up)
        {
            float surface = surfacePoint.y;
            if (up.y > 0.5f) surface -= (up.x * (point.x - surfacePoint.x) + up.z * (point.z - surfacePoint.z)) / up.y;
            return point.y - surface;
        }

        /// <summary>Speed at which drag equals weight.</summary>
        public static float TerminalVelocity(RocketConfig c, float massKg, float gravity)
            => Mathf.Sqrt(2f * massKg * gravity / (c.airDensity * c.dragCoefficient * c.dragAreaM2));

        /// <summary>
        /// Thrust direction in rocket-local space for a gimbal input in [-1, 1]² (x = right, y = forward).
        /// The nozzle deflects so that positive input tilts the rocket towards +x / +z: the thrust at the
        /// engine (below the centre of mass) gets a sideways component towards -x / -z.
        /// Each axis deflects by exactly input·gimbalMax degrees in its own plane.
        /// </summary>
        public static Vector3 ThrustDirectionLocal(RocketConfig c, Vector2 gimbal)
        {
            float gx = Mathf.Clamp(gimbal.x, -1f, 1f) * c.gimbalMaxDeg * Mathf.Deg2Rad;
            float gz = Mathf.Clamp(gimbal.y, -1f, 1f) * c.gimbalMaxDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Tan(gx), 1f, -Mathf.Tan(gz)).normalized;
        }

        public static float DynamicPressure(RocketConfig c, Vector3 velocity) => 0.5f * c.airDensity * velocity.sqrMagnitude;

        /// <summary>
        /// Rocket-local control torque from grid fins (∝ dynamic pressure) plus RCS for a steering input in
        /// [-1, 1]² (same axes as the gimbal: positive x tilts the nose towards local +x, positive y towards +z).
        /// </summary>
        public static Vector3 AeroControlTorqueLocal(RocketConfig c, float dynamicPressure, Vector2 input)
        {
            float k = c.gridFinTorquePerPa * dynamicPressure + c.rcsTorqueNm;
            float gx = Mathf.Clamp(input.x, -1f, 1f), gz = Mathf.Clamp(input.y, -1f, 1f);
            // rotation about -z tips the nose to +x; rotation about +x tips it to +z
            return new Vector3(gz, 0f, -gx) * k;
        }

        /// <summary>
        /// Body lift q·A·Cl·sin(2α), perpendicular to the velocity, while falling engine-first. Direction:
        /// towards the side the nose leans when <see cref="RocketConfig.liftTowardsNose"/>, else the physical
        /// crossflow direction (opposite).
        /// </summary>
        public static Vector3 BodyLift(RocketConfig c, Vector3 up, Vector3 velocity)
        {
            float speed = velocity.magnitude;
            if (speed < 1f) return Vector3.zero;
            Vector3 dir = velocity / speed;
            float cosA = Vector3.Dot(-up, dir);          // 1 = perfectly engine-first
            if (cosA <= 0f) return Vector3.zero;
            Vector3 lean = up - Vector3.Dot(up, dir) * dir; // nose direction perpendicular to the flow
            float sinA = lean.magnitude;
            if (sinA < 1e-5f) return Vector3.zero;
            float magnitude = DynamicPressure(c, velocity) * c.dragAreaM2 * c.bodyLiftCoefficient * 2f * sinA * cosA;
            return (c.liftTowardsNose ? 1f : -1f) * magnitude * (lean / sinA);
        }

        /// <summary>Principal inertia of a solid cylinder about the given centre of mass height (x, y = axial, z).</summary>
        public static Vector3 InertiaTensor(RocketConfig c, float massKg)
        {
            float r = c.bodyRadius, h = c.bodyHeight;
            float offset = h * 0.5f - c.centerOfMassHeight;
            float transverse = massKg * ((3f * r * r + h * h) / 12f + offset * offset);
            float axial = 0.5f * massKg * r * r;
            return new Vector3(transverse, axial, transverse);
        }

        /// <summary>
        /// Weathervane torque that turns the rocket's bottom (-up) into the airflow while it falls engine-first.
        /// Zero when moving top-first or slower than 1 m/s.
        /// </summary>
        public static Vector3 AeroStabilityTorque(RocketConfig c, Vector3 up, Vector3 velocity)
        {
            float speed = velocity.magnitude;
            if (speed < 1f) return Vector3.zero;
            Vector3 dir = velocity / speed;
            Vector3 down = -up;
            if (Vector3.Dot(down, dir) <= 0f) return Vector3.zero;
            float q = 0.5f * c.airDensity * speed * speed;
            return q * c.dragAreaM2 * c.aeroStabilityArmM * Vector3.Cross(down, dir);
        }
    }
}
