using NUnit.Framework;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    public class EasyFlightTests
    {
        const float G = 9.81f;
        RocketConfig config;
        float mass;

        [SetUp]
        public void Make()
        {
            config = ScriptableObject.CreateInstance<RocketConfig>();
            mass = config.dryMassKg + 10000f;
        }

        [TearDown]
        public void Destroy() => Object.DestroyImmediate(config);

        [Test]
        public void ThrottleCapIsZeroWhenMovingUpAndFullWhenFallingFast()
        {
            Assert.AreEqual(0f, EasyFlight.ThrottleCap(config, mass, 500f, 0.5f, 1f, 0f, G));
            Assert.Less(EasyFlight.ThrottleCap(config, mass, 2f, 0f, 1f, 0f, G), mass * G / config.maxThrustN, "stopped near the ground: below hover, so it keeps descending");
            Assert.AreEqual(1f, EasyFlight.ThrottleCap(config, mass, 1500f, -150f, 1f, 0f, G));
        }

        [Test]
        public void ThrottleCapHoversAtTheFloorSpeed()
        {
            float h = 1000f;
            float vFloor = -EasyFlight.FloorSpeed(config, h);
            Assert.AreEqual(config.easyMaxFloorSpeed, -vFloor, 1e-4f);
            float cap = EasyFlight.ThrottleCap(config, mass, h, vFloor, 1f, 0f, G);
            Assert.AreEqual(mass * G / config.maxThrustN, cap, 1e-4f, "exactly cancels gravity");
            Assert.Less(cap, config.minThrottle, "Easy needs the deep throttle range to hold the floor");
        }

        [Test]
        public void TouchdownGuardHoldsTheGuardSpeedOnlyNearTheGround()
        {
            float hover = mass * G / config.maxThrustN;
            float low = config.easyGuardAltitude * 0.5f;
            Assert.AreEqual(0f, EasyFlight.GuardThrottle(config, mass, config.easyGuardAltitude + 1f, -10f, 1f, 0f, G), "off higher up");
            Assert.AreEqual(hover, EasyFlight.GuardThrottle(config, mass, low, -config.easyGuardSpeed, 1f, 0f, G), 1e-4f, "hovers at the guard speed");
            Assert.Greater(EasyFlight.GuardThrottle(config, mass, low, -config.easyGuardSpeed - 1f, 1f, 0f, G), hover, "brakes a faster fall");
            Assert.Less(EasyFlight.GuardThrottle(config, mass, low, -0.5f, 1f, 0f, G), hover, "lets a slow fall speed up");
            Assert.Less(config.easyGuardSpeed, config.landing.maxVerticalSpeed * 0.7f, "comfortable margin under the landing limit");
        }

        [Test]
        public void FloorSpeedShrinksTowardsTheGroundButStaysDownward()
        {
            Assert.AreEqual(config.easyMinDescentSpeed, EasyFlight.FloorSpeed(config, 0f), 1e-5f);
            Assert.Greater(EasyFlight.FloorSpeed(config, 0f), 0f);
            Assert.Less(EasyFlight.FloorSpeed(config, 20f), EasyFlight.FloorSpeed(config, 60f));
        }

        [Test]
        public void FireAltitudeGrowsWithSpeed()
        {
            float slow = EasyFlight.FireAltitude(config, mass, 60f, G);
            float fast = EasyFlight.FireAltitude(config, mass, 225f, G);
            Assert.Greater(fast, slow);
            Assert.That(fast, Is.InRange(1200f, 2500f), "from the level start the cue fires well below 3 km");
            Assert.AreEqual(0f, EasyFlight.FireAltitude(config, mass, 0f, G));
        }

        [Test]
        public void CueSequence()
        {
            Assert.AreEqual(CueKind.FireIn, EasyFlight.Evaluate(config, mass, 3000f, -225f, false, false, G).kind);
            BurnCue c = EasyFlight.Evaluate(config, mass, 3000f, -225f, false, false, G);
            Assert.AreEqual(3000f - c.fireAltitude, c.distance, 1e-3f);
            Assert.AreEqual(CueKind.FireNow, EasyFlight.Evaluate(config, mass, 1000f, -225f, false, false, G).kind);
            Assert.AreEqual(CueKind.KeepHolding, EasyFlight.Evaluate(config, mass, 1000f, -225f, true, true, G).kind);
            Assert.AreEqual(CueKind.TooEarly, EasyFlight.Evaluate(config, mass, 3000f, -225f, true, false, G).kind);
            Assert.AreEqual(CueKind.Touchdown, EasyFlight.Evaluate(config, mass, 50f, -3f, true, true, G).kind);
            Assert.AreEqual(CueKind.FireNow, EasyFlight.Evaluate(config, mass, 50f, -60f, false, true, G).kind, "late: fire beats touchdown");
        }

        [Test]
        public void ExplicitMinimumThrottleOverload()
        {
            Assert.AreEqual(0f, RocketPhysics.Thrust(config, true, 0f, 0f));
            Assert.AreEqual(config.maxThrustN * 0.2f, RocketPhysics.Thrust(config, true, 0.2f, 0f), 1e-2f);
            Assert.AreEqual(config.maxThrustN * config.minThrottle, RocketPhysics.Thrust(config, true, 0.2f), 1e-2f);
        }
    }
}
