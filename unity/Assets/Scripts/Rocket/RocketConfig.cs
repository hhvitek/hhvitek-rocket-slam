using RocketSlam.Gameplay;
using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// Every tunable number of the booster. Edited as the asset <c>Assets/Settings/RocketConfig.asset</c>;
    /// field initialisers are the defaults used when SceneBuilder creates the asset and in tests.
    /// </summary>
    [CreateAssetMenu(menuName = "Rocket Slam/Rocket Config", fileName = "RocketConfig")]
    public class RocketConfig : ScriptableObject
    {
        [Header("Mass")]
        [Tooltip("Empty stage mass in kg.")]
        public float dryMassKg = 22000f;

        [Header("Engine")]
        [Tooltip("Thrust at 100 % throttle, in newtons.")]
        public float maxThrustN = 850000f;
        [Tooltip("Lowest throttle the running engine can do (0..1). Even this exceeds the weight, so the stage cannot hover.")]
        [Range(0f, 1f)] public float minThrottle = 0.4f;
        [Tooltip("Propellant flow at 100 % throttle, kg/s. Scales linearly with throttle.")]
        public float burnRateAtFullKgPerS = 280f;
        [Tooltip("Throttle change per second while the throttle input is held fully (fraction of 100 %).")]
        public float throttleChangeRate = 0.6f;
        [Tooltip("Gimbal deflection limit on each of the two axes, degrees.")]
        public float gimbalMaxDeg = 5f;

        [Header("Geometry (1 unit = 1 m; rocket origin is the engine plane at the bottom)")]
        public float bodyHeight = 40f;
        public float bodyRadius = 1.85f;
        [Tooltip("Centre of mass height above the engine plane. Low because engine + remaining propellant sit at the bottom.")]
        public float centerOfMassHeight = 14f;

        [Header("Aerodynamics")]
        [Tooltip("Air density, kg/m³ (constant, sea level).")]
        public float airDensity = 1.225f;
        [Tooltip("Drag coefficient. With the reference area below, terminal velocity at full mass is ~225 m/s.")]
        public float dragCoefficient = 0.95f;
        [Tooltip("Drag reference area, m² (end-on cross-section of a 3.7 m cylinder).")]
        public float dragAreaM2 = 10.75f;
        [Tooltip("Crossflow drag coefficient of the body side (cylinder in crosswind), used for the horizontal wind force on the side area (2·radius·height).")]
        public float sideDragCoefficient = 0.8f;
        [Tooltip("Weathervane stability: restoring torque = q·A·arm·sin(angle of attack), only while falling engine-first. Stands in for the centre of pressure sitting above the centre of mass (and for grid fins later).")]
        public float aeroStabilityArmM = 8f;
        [Tooltip("Rigidbody angular damping (1/s). Makes the gimbal feel like a rate command instead of pure acceleration, which keeps the stage controllable for a human.")]
        public float angularDamping = 1.0f;

        [Header("Attitude control without the engine")]
        [Tooltip("Grid-fin control torque per unit dynamic pressure, N·m/Pa (≈ fin area × lift coeff. × arm to CoM, e.g. 4 fins × ~1 m² × 0.4 × 24 m). At full deflection and 225 m/s (q ≈ 31 kPa) ≈ 1.2 MN·m, a bit more than the gimbal at full thrust.")]
        public float gridFinTorquePerPa = 38f;
        [Tooltip("Cold-gas RCS torque at full input, N·m. Weak, but keeps the stage steerable at low speed with the engine off.")]
        public float rcsTorqueNm = 150000f;
        [Tooltip("Body lift at angle of attack: F = q·A·Cl·sin(2α), A = drag reference area.")]
        public float bodyLiftCoefficient = 0.5f;
        [Tooltip("Gameplay simplification: lift pushes towards the side the nose leans (so leaning = moving that way in every phase). False = physical crossflow direction (towards the side the engine points).")]
        public bool liftTowardsNose = true;

        [Header("Landing legs")]
        public float legHingeHeight = 2f;
        public float legHingeRadius = 1.95f;
        [Tooltip("Hinge to foot centre along the stowed leg (informational; the geometry comes from Booster.fbx).")]
        public float legLength = 9f;
        [Tooltip("Hinge rotation from stowed (pointing up along the body) to deployed, degrees.")]
        public float legDeployAngleDeg = 111.94f;
        [Tooltip("Seconds from pressing deploy until the legs are locked.")]
        public float legDeployTime = 1.5f;

        [Header("Easy mode (default): hold one key, the assist does the rest")]
        [Tooltip("Seconds for the Easy-mode engine to ramp from 0 to 100 % (and back) when the key is pressed/released.")]
        public float easyThrottleRampTime = 0.3f;
        [Tooltip("The engine never brakes the fall below this speed (m/s) high up; this keeps 'hold from the start' from being a free win (fuel runs out).")]
        public float easyMaxFloorSpeed = 20f;
        [Tooltip("Lower down the slowest allowed descent is altitude × this (1/s): the stage eases towards the ground instead of hovering.")]
        public float easyFloorPerMetre = 0.25f;
        [Tooltip("Slowest allowed descent near the ground, m/s. Positive: the stage can never climb in Easy mode.")]
        public float easyMinDescentSpeed = 1f;
        [Tooltip("Gain of the thrust cap that holds the descent-speed floor, 1/s.")]
        public float easyFloorGain = 3f;
        [Tooltip("Touchdown guard: once the engine has been lit, below this altitude (m) the Easy assist keeps enough thrust that the fall (relative to the deck) settles at easyGuardSpeed even when the key is released. Pulsing the key in the last metres then can't drop the stage into a hard impact.")]
        public float easyGuardAltitude = 15f;
        [Tooltip("Touchdown guard: fastest fall (m/s, relative to the landing surface) the guard lets build up near the ground; well under the landing limit.")]
        public float easyGuardSpeed = 1.5f;
        [Tooltip("Touchdown guard: controller gain, 1/s (higher = catches a released stage faster).")]
        public float easyGuardGain = 5f;
        [Tooltip("Burn cue: fraction of the full-throttle deceleration assumed (lower = earlier FIRE NOW, more slack for human reaction).")]
        public float cueMargin = 0.7f;
        [Tooltip("Burn cue: the main burn should be done (fall speed at the floor) by this altitude, m.")]
        public float cueBurnEndAltitude = 80f;
        [Tooltip("Burn cue: below this altitude the cue switches to the touchdown speed readout, m.")]
        public float cueFinalAltitude = 100f;
        [Tooltip("Easy mode deploys the legs automatically below this altitude, m.")]
        public float easyLegsAltitude = 300f;
        [Tooltip("Horizontal acceleration requested by a full nudge (arrows/WASD) in Easy mode, m/s².")]
        public float easyNudgeAccel = 3f;

        [Header("Landing rules")]
        public LandingCriteria landing = new LandingCriteria();
    }
}
