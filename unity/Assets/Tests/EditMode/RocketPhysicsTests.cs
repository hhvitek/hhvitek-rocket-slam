using NUnit.Framework;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    public class RocketPhysicsTests
    {
        const float G = 9.81f;
        RocketConfig config;

        [SetUp]
        public void SetUp() => config = ScriptableObject.CreateInstance<RocketConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(config);

        [Test]
        public void ThrustIsZeroWhenEngineOff()
        {
            Assert.AreEqual(0f, RocketPhysics.Thrust(config, false, 1f));
            Assert.AreEqual(0f, RocketPhysics.BurnRate(config, false, 1f));
        }

        [TestCase(1f, 850000f)]
        [TestCase(0.7f, 595000f)]
        [TestCase(0.4f, 340000f)]
        [TestCase(0.1f, 340000f)] // below minimum -> clamped to 40 %
        [TestCase(0f, 340000f)]
        [TestCase(1.5f, 850000f)] // above maximum -> clamped to 100 %
        public void ThrustScalesWithClampedThrottle(float throttle, float expected)
        {
            Assert.AreEqual(expected, RocketPhysics.Thrust(config, true, throttle), 1f);
        }

        [TestCase(1f, 280f)]
        [TestCase(0.5f, 140f)]
        [TestCase(0.2f, 112f)] // clamped to 40 %
        public void BurnRateIsProportionalToClampedThrottle(float throttle, float expected)
        {
            Assert.AreEqual(expected, RocketPhysics.BurnRate(config, true, throttle), 0.01f);
        }

        [Test]
        public void ClampThrottleKeepsRunningEngineBetweenMinAndFull()
        {
            Assert.AreEqual(0.4f, RocketPhysics.ClampThrottle(config, -3f), 1e-6f);
            Assert.AreEqual(0.4f, RocketPhysics.ClampThrottle(config, 0.39f), 1e-6f);
            Assert.AreEqual(0.55f, RocketPhysics.ClampThrottle(config, 0.55f), 1e-6f);
            Assert.AreEqual(1f, RocketPhysics.ClampThrottle(config, 7f), 1e-6f);
        }

        [Test]
        public void MassIsDryPlusFuelAndNeverBelowDry()
        {
            Assert.AreEqual(32000f, RocketPhysics.Mass(config, 10000f));
            Assert.AreEqual(22000f, RocketPhysics.Mass(config, 0f));
            Assert.AreEqual(22000f, RocketPhysics.Mass(config, -5f));
        }

        [Test]
        public void MinimumThrottleExceedsWeightSoTheStageCannotHover()
        {
            float minThrust = RocketPhysics.Thrust(config, true, 0f);
            float heaviest = RocketPhysics.Mass(config, 10000f) * G;
            Assert.Greater(minThrust, heaviest);
        }

        [Test]
        public void FullThrustBrakesAroundEighteenMetresPerSecondSquared()
        {
            float full = RocketPhysics.Thrust(config, true, 1f);
            float atStart = full / RocketPhysics.Mass(config, 10000f) - G;
            float nearlyEmpty = full / RocketPhysics.Mass(config, 6000f) - G;
            Assert.That(atStart, Is.InRange(15f, 18f));
            Assert.That(nearlyEmpty, Is.InRange(18f, 22f));
        }

        [Test]
        public void StoppingFrom250AtFullThrustWithoutDragMatchesSpecSanityCheck()
        {
            // Spec: ~14 s, ~1.7 km, ~4 t of propellant (rough numbers with a constant 18 m/s²).
            float v = 250f, fuel = 10000f, distance = 0f, t = 0f;
            const float dt = 0.01f;
            while (v > 0f)
            {
                float m = RocketPhysics.Mass(config, fuel);
                float a = RocketPhysics.Thrust(config, true, 1f) / m - G;
                distance += v * dt;
                v -= a * dt;
                fuel -= RocketPhysics.BurnRate(config, true, 1f) * dt;
                t += dt;
            }
            Assert.That(t, Is.InRange(11f, 15f), "burn time");
            Assert.That(distance, Is.InRange(1400f, 1900f), "stopping distance");
            Assert.That(10000f - fuel, Is.InRange(3000f, 4200f), "propellant used");
        }

        [Test]
        public void DragIsHalfRhoVSquaredCdAAgainstVelocity()
        {
            var v = new Vector3(0f, -225f, 0f);
            Vector3 drag = RocketPhysics.Drag(config, v);
            float expected = 0.5f * 1.225f * 225f * 225f * 0.95f * 10.75f;
            Assert.AreEqual(expected, drag.magnitude, expected * 1e-4f);
            Assert.Greater(drag.y, 0f, "drag must oppose a downward velocity");
            Assert.AreEqual(Vector3.zero, RocketPhysics.Drag(config, Vector3.zero));

            Vector3 sideways = RocketPhysics.Drag(config, new Vector3(10f, 0f, 0f));
            Assert.Less(sideways.x, 0f);
            Assert.AreEqual(4f, RocketPhysics.Drag(config, new Vector3(20f, 0f, 0f)).magnitude / sideways.magnitude, 1e-3f,
                "drag grows with v squared");
        }

        [Test]
        public void StartSpeedIsCloseToTerminalVelocity()
        {
            float terminal = RocketPhysics.TerminalVelocity(config, RocketPhysics.Mass(config, 10000f), G);
            Assert.That(terminal, Is.InRange(200f, 250f));
        }

        [Test]
        public void GimbalCentredThrustsAlongTheAxis()
        {
            Vector3 dir = RocketPhysics.ThrustDirectionLocal(config, Vector2.zero);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.up, dir), 1e-3f);
        }

        [Test]
        public void GimbalDeflectsFiveDegreesPerAxisTowardsTorqueThatTiltsTheRocketToTheInput()
        {
            Vector3 right = RocketPhysics.ThrustDirectionLocal(config, new Vector2(1f, 0f));
            Assert.AreEqual(5f, Vector3.Angle(Vector3.up, right), 1e-3f);
            Assert.Less(right.x, 0f, "push the engine left so the nose tilts right");
            Assert.AreEqual(0f, right.z, 1e-6f);

            Vector3 forward = RocketPhysics.ThrustDirectionLocal(config, new Vector2(0f, 1f));
            Assert.AreEqual(5f, Vector3.Angle(Vector3.up, forward), 1e-3f);
            Assert.Less(forward.z, 0f);

            Vector3 both = RocketPhysics.ThrustDirectionLocal(config, new Vector2(1f, 1f));
            Assert.AreEqual(5f, Mathf.Atan2(-both.x, both.y) * Mathf.Rad2Deg, 1e-3f, "x-plane deflection");
            Assert.AreEqual(5f, Mathf.Atan2(-both.z, both.y) * Mathf.Rad2Deg, 1e-3f, "z-plane deflection");

            Vector3 over = RocketPhysics.ThrustDirectionLocal(config, new Vector2(3f, 0f));
            Assert.AreEqual(5f, Vector3.Angle(Vector3.up, over), 1e-3f, "input is clamped to the gimbal limit");
        }

        [Test]
        public void GimbalThrustBelowCentreOfMassTorquesTheRocketTowardsTheInput()
        {
            // Force at the engine (origin), centre of mass above it.
            Vector3 force = RocketPhysics.ThrustDirectionLocal(config, new Vector2(1f, 0f)) * 850000f;
            Vector3 lever = Vector3.zero - new Vector3(0f, config.centerOfMassHeight, 0f);
            Vector3 torque = Vector3.Cross(lever, force);
            // A small rotation by this torque must move the nose (+y) towards +x.
            Vector3 noseMotion = Vector3.Cross(torque, Vector3.up);
            Assert.Greater(noseMotion.x, 0f);
        }

        [Test]
        public void GridFinsAndRcsTorqueScalesWithDynamicPressureAndTipsTheNoseTowardsTheInput()
        {
            Assert.AreEqual(Vector3.zero, RocketPhysics.AeroControlTorqueLocal(config, 30000f, Vector2.zero));
            float atRest = RocketPhysics.AeroControlTorqueLocal(config, 0f, new Vector2(1f, 0f)).magnitude;
            Assert.AreEqual(config.rcsTorqueNm, atRest, 1f, "RCS alone at zero speed");

            float q = RocketPhysics.DynamicPressure(config, new Vector3(0f, -225f, 0f));
            Assert.AreEqual(0.5f * 1.225f * 225f * 225f, q, 1f);
            Vector3 full = RocketPhysics.AeroControlTorqueLocal(config, q, new Vector2(1f, 0f));
            Assert.AreEqual(config.gridFinTorquePerPa * q + config.rcsTorqueNm, full.magnitude, 10f);
            Assert.Greater(Vector3.Cross(full, Vector3.up).x, 0f, "+x input tips the nose to +x");
            Vector3 fwd = RocketPhysics.AeroControlTorqueLocal(config, q, new Vector2(0f, 1f));
            Assert.Greater(Vector3.Cross(fwd, Vector3.up).z, 0f, "+y input tips the nose to +z");
        }

        [Test]
        public void BodyLiftIsQACl2SinCosPerpendicularToFlowAndTowardsTheNose()
        {
            Vector3 fall = new Vector3(0f, -200f, 0f);
            Assert.AreEqual(Vector3.zero, RocketPhysics.BodyLift(config, Vector3.up, fall), "no lift without angle of attack");

            Vector3 up45 = new Vector3(1f, 1f, 0f).normalized; // nose leaning to +x by 45 deg
            Vector3 lift = RocketPhysics.BodyLift(config, up45, fall);
            float q = 0.5f * 1.225f * 200f * 200f;
            Assert.AreEqual(q * config.dragAreaM2 * config.bodyLiftCoefficient, lift.magnitude, 1f, "sin(2α) = 1 at 45°");
            Assert.AreEqual(0f, Vector3.Dot(lift, fall.normalized), 1e-2f, "perpendicular to the flow");
            Assert.Greater(lift.x, 0f, "gameplay mode: lift towards the leaning nose");

            config.liftTowardsNose = false;
            Assert.Less(RocketPhysics.BodyLift(config, up45, fall).x, 0f, "physical mode: crossflow direction");
            Assert.AreEqual(Vector3.zero, RocketPhysics.BodyLift(config, up45, -fall), "only while falling engine-first");
        }

        [Test]
        public void EngineOffSteeringGivesUsefulButBoundedDriftAtStartSpeed()
        {
            // Full fin input against weathervane stability at 225 m/s: equilibrium angle of attack and the
            // resulting sideways acceleration at full mass.
            Vector3 v = new Vector3(0f, -225f, 0f);
            float q = RocketPhysics.DynamicPressure(config, v);
            float fin = config.gridFinTorquePerPa * q + config.rcsTorqueNm;
            float stabilityPerSin = q * config.dragAreaM2 * config.aeroStabilityArmM;
            float alpha = Mathf.Asin(Mathf.Clamp01(fin / stabilityPerSin)) * Mathf.Rad2Deg;
            Assert.That(alpha, Is.InRange(15f, 35f), "trim angle of attack");
            Vector3 up = Quaternion.Euler(0f, 0f, -alpha) * Vector3.up;
            float accel = RocketPhysics.BodyLift(config, up, v).magnitude / RocketPhysics.Mass(config, 10000f);
            Assert.That(accel, Is.InRange(2f, 8f), "sideways m/s² -> tens of m/s over a ~10 s fall");
        }

        [Test]
        public void InertiaIsPositiveAndTransverseExceedsAxial()
        {
            Vector3 inertia = RocketPhysics.InertiaTensor(config, 32000f);
            Assert.Greater(inertia.y, 0f);
            Assert.Greater(inertia.x, inertia.y * 10f);
            Assert.AreEqual(inertia.x, inertia.z);
        }

        [Test]
        public void AeroStabilityRestoresTiltWhileFallingAndIsZeroWhenAligned()
        {
            Vector3 fall = new Vector3(0f, -200f, 0f);
            Assert.AreEqual(Vector3.zero, RocketPhysics.AeroStabilityTorque(config, Vector3.up, fall));

            Vector3 tiltedRight = Quaternion.Euler(0f, 0f, -10f) * Vector3.up; // nose towards +x
            Assert.Greater(tiltedRight.x, 0f);
            Vector3 torque = RocketPhysics.AeroStabilityTorque(config, tiltedRight, fall);
            Vector3 noseMotion = Vector3.Cross(torque, tiltedRight);
            Assert.Less(noseMotion.x, 0f, "torque turns the nose back towards vertical");

            Assert.AreEqual(Vector3.zero, RocketPhysics.AeroStabilityTorque(config, tiltedRight, -fall), "no torque when climbing");
            Assert.AreEqual(Vector3.zero, RocketPhysics.AeroStabilityTorque(config, tiltedRight, new Vector3(0f, -0.5f, 0f)));
        }
    }
}
