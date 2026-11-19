using NUnit.Framework;
using RocketSlam.Gameplay;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    public class LandingEvaluatorTests
    {
        const float Dt = 0.02f;
        LandingCriteria rules;
        LandingEvaluator eval;

        [SetUp]
        public void SetUp()
        {
            rules = new LandingCriteria();
            eval = new LandingEvaluator(rules);
        }

        static FlightSample Airborne(float fuel = 5000f) => new FlightSample
        {
            velocity = new Vector3(0f, -50f, 0f),
            tiltDeg = 1f,
            fuelKg = fuel,
            legsDeployed = true,
            legCount = 4,
            legsOverPad = 4,
        };

        static FlightSample Touchdown(float vy = -2f, float vh = 0.5f, float tilt = 2f) => new FlightSample
        {
            velocity = new Vector3(vh, vy, 0f),
            tiltDeg = tilt,
            fuelKg = 1000f,
            legsDeployed = true,
            legsTouchingPad = 2,
            legsOverPad = 4,
            legCount = 4,
        };

        static FlightSample Resting() => new FlightSample
        {
            tiltDeg = 2f,
            fuelKg = 1000f,
            legsDeployed = true,
            legsTouchingPad = 4,
            legsOverPad = 4,
            legCount = 4,
        };

        void Settle(float seconds, FlightSample s)
        {
            for (float t = 0f; t < seconds; t += Dt) eval.Tick(s, Dt);
        }

        void AssertFailure(FailureReason reason)
        {
            Assert.AreEqual(LandingState.Failure, eval.State);
            Assert.AreEqual(reason, eval.Reason);
            Assert.IsNotEmpty(eval.Describe());
        }

        [Test]
        public void StaysFlyingWithoutContact()
        {
            for (int i = 0; i < 100; i++) eval.Tick(Airborne(), Dt);
            Assert.AreEqual(LandingState.Flying, eval.State);
            Assert.AreEqual(FailureReason.None, eval.Reason);
        }

        [Test]
        public void OutOfFuelWhileAirborneFailsAtImpactWithOutOfFuelReasonAndExplosion()
        {
            eval.Tick(Airborne(fuel: 0f), Dt);
            Assert.AreEqual(LandingState.Flying, eval.State, "keeps falling until impact");
            Assert.IsTrue(eval.FuelExhausted);

            FlightSample impact = Touchdown(vy: -80f);
            impact.fuelKg = 0f;
            eval.Tick(impact, Dt);
            AssertFailure(FailureReason.OutOfFuel);
            Assert.IsTrue(eval.ImpactTooHard, "the impact still explodes");
            StringAssert.Contains("Out of fuel", eval.Describe());
        }

        [Test]
        public void OutOfFuelJustBeforeAGentleTouchdownStillLands()
        {
            eval.Tick(Airborne(fuel: 0f), Dt);
            FlightSample s = Touchdown();
            s.fuelKg = 0f;
            eval.Tick(s, Dt);
            Settle(rules.settleTime + 0.1f, Resting());
            Assert.AreEqual(LandingState.Success, eval.State);
        }

        [Test]
        public void HardImpactWithFuelLeftExplodes()
        {
            eval.Tick(Touchdown(vy: -10f), Dt);
            AssertFailure(FailureReason.HardImpact);
            Assert.IsTrue(eval.ImpactTooHard);
            Assert.IsFalse(eval.FuelExhausted);
        }

        [Test]
        public void ZeroFuelAtTouchdownIsNotOutOfFuel()
        {
            FlightSample s = Touchdown();
            s.fuelKg = 0f;
            eval.Tick(s, Dt);
            Assert.AreEqual(LandingState.Settling, eval.State);
        }

        [Test]
        public void GentleTouchdownStartsSettlingAtExactLimits()
        {
            eval.Tick(Touchdown(vy: -rules.maxVerticalSpeed, vh: rules.maxHorizontalSpeed, tilt: rules.maxTiltDeg), Dt);
            Assert.AreEqual(LandingState.Settling, eval.State);
            Assert.AreEqual(rules.maxVerticalSpeed, eval.TouchdownVerticalSpeed, 1e-5f);
            Assert.AreEqual(rules.maxHorizontalSpeed, eval.TouchdownHorizontalSpeed, 1e-5f);
        }

        [Test]
        public void VerticalSpeedJustOverLimitIsHardImpact()
        {
            eval.Tick(Touchdown(vy: -(rules.maxVerticalSpeed + 0.01f)), Dt);
            AssertFailure(FailureReason.HardImpact);
            StringAssert.Contains("vertical", eval.Describe());
        }

        [Test]
        public void HorizontalSpeedJustOverLimitIsHardImpact()
        {
            eval.Tick(Touchdown(vh: rules.maxHorizontalSpeed + 0.01f), Dt);
            AssertFailure(FailureReason.HardImpact);
            StringAssert.Contains("horizontal", eval.Describe());
        }

        [Test]
        public void HardImpactWinsOverMissingLegs()
        {
            FlightSample s = Touchdown(vy: -200f);
            s.legsDeployed = false;
            s.bodyContact = true;
            s.legsTouchingPad = 0;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.HardImpact);
        }

        [Test]
        public void TouchdownWithoutLegsFails()
        {
            FlightSample s = Touchdown();
            s.legsDeployed = false;
            s.legsTouchingPad = 0;
            s.bodyContact = true;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.NoLegs);
        }

        [Test]
        public void TouchdownWhileLegsStillDeployingCountsAsNoLegs()
        {
            FlightSample s = Touchdown();
            s.legsDeployed = false; // feet already touch but not locked
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.NoLegs);
        }

        [Test]
        public void BodyTouchingWithLegsDeployedIsTippedOver()
        {
            FlightSample s = Touchdown();
            s.bodyContact = true;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.TippedOver);
        }

        [Test]
        public void LegOnGroundOutsidePadIsOffPad()
        {
            FlightSample s = Touchdown();
            s.legsTouchingPad = 0;
            s.legsTouchingGround = 1;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.OffPad);
        }

        [Test]
        public void TiltJustOverLimitAtTouchdownFails()
        {
            eval.Tick(Touchdown(tilt: rules.maxTiltDeg + 0.01f), Dt);
            AssertFailure(FailureReason.TooTilted);
        }

        [Test]
        public void StayingUprightForSettleTimeSucceeds()
        {
            eval.Tick(Touchdown(), Dt);
            Settle(rules.settleTime - 0.1f, Resting());
            Assert.AreEqual(LandingState.Settling, eval.State, "not yet");
            Settle(0.2f, Resting());
            Assert.AreEqual(LandingState.Success, eval.State);
            Assert.AreEqual(FailureReason.None, eval.Reason);
            StringAssert.Contains("Touchdown", eval.Describe());
        }

        [Test]
        public void TippingOverWhileSettlingFails()
        {
            eval.Tick(Touchdown(), Dt);
            Settle(1f, Resting());
            FlightSample s = Resting();
            s.tiltDeg = rules.tipOverTiltDeg + 0.1f;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.TippedOver);
        }

        [Test]
        public void TiltBelowTipOverWhileSettlingIsTolerated()
        {
            eval.Tick(Touchdown(), Dt);
            FlightSample s = Resting();
            s.tiltDeg = rules.tipOverTiltDeg - 0.1f;
            Settle(rules.settleTime + 0.1f, s);
            Assert.AreEqual(LandingState.Success, eval.State);
        }

        [Test]
        public void BodyHittingTheSurfaceWhileSettlingIsTippedOver()
        {
            eval.Tick(Touchdown(), Dt);
            FlightSample s = Resting();
            s.bodyContact = true;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.TippedOver);
        }

        [Test]
        public void SlidingOffThePadWhileSettlingFails()
        {
            eval.Tick(Touchdown(), Dt);
            FlightSample s = Resting();
            s.legsTouchingGround = 1;
            eval.Tick(s, Dt);
            AssertFailure(FailureReason.OffPad);
        }

        [Test]
        public void FootHangingOverThePadEdgeAtTheEndIsOffPad()
        {
            eval.Tick(Touchdown(), Dt);
            FlightSample s = Resting();
            s.legsOverPad = 3;
            Settle(rules.settleTime + 0.1f, s);
            AssertFailure(FailureReason.OffPad);
        }

        [Test]
        public void OutcomeIsStickyAndResetStartsOver()
        {
            eval.Tick(Touchdown(vy: -50f), Dt);
            AssertFailure(FailureReason.HardImpact);
            Settle(5f, Resting());
            AssertFailure(FailureReason.HardImpact);

            eval.Reset();
            Assert.AreEqual(LandingState.Flying, eval.State);
            Assert.AreEqual(FailureReason.None, eval.Reason);
        }
    }
}
