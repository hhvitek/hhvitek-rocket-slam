using UnityEngine;

namespace RocketSlam.Gameplay
{
    public enum LandingState { Flying, Settling, Success, Failure }

    public enum FailureReason { None, HardImpact, TippedOver, NoLegs, OffPad, OutOfFuel, TooTilted }

    /// <summary>One physics-step snapshot of everything the landing rules look at.</summary>
    public struct FlightSample
    {
        /// <summary>World velocity before the latest physics step (= impact velocity on the touchdown step).</summary>
        public Vector3 velocity;
        /// <summary>Velocity of the landing surface under the stage (moving deck; zero for a ground pad). Touchdown
        /// speeds are measured relative to it.</summary>
        public Vector3 surfaceVelocity;
        /// <summary>Angle between the rocket axis and the landing-surface normal (deck up; world up on the ground), degrees.</summary>
        public float tiltDeg;
        public float fuelKg;
        /// <summary>Legs fully deployed and locked.</summary>
        public bool legsDeployed;
        /// <summary>Any collider other than a leg foot touches something (body hit the surface).</summary>
        public bool bodyContact;
        public int legsTouchingPad;
        /// <summary>Leg feet touching anything that is not the pad.</summary>
        public int legsTouchingGround;
        /// <summary>Leg feet whose position lies inside the pad footprint.</summary>
        public int legsOverPad;
        public int legCount;

        public bool AnyContact => bodyContact || legsTouchingPad > 0 || legsTouchingGround > 0;
    }

    /// <summary>
    /// Pure landing rules (no Unity objects). Feed one <see cref="FlightSample"/> per physics step.
    /// <para>Rules, in order of precedence at the first contact (touchdown):</para>
    /// <list type="number">
    /// <item>vertical speed &gt; max or horizontal speed &gt; max → HardImpact (explosion). Both are measured relative to
    /// the landing surface (<see cref="FlightSample.surfaceVelocity"/>): what breaks legs is the closing speed with a
    /// heaving deck, not the speed over the sea floor;</item>
    /// <item>legs not deployed and locked → NoLegs;</item>
    /// <item>body touches the surface → TippedOver;</item>
    /// <item>any foot touches something other than the pad → OffPad;</item>
    /// <item>tilt &gt; max → TooTilted. Tilt is measured from the deck normal, not world up: on a rolling deck the
    /// legs touch evenly when the stage is square to the deck. The deck tilts at most ~3° on level 4, so the 6° limit
    /// stays meaningful and a stage standing on a rolling deck is not "tipping";</item>
    /// <item>otherwise Settling: within settleTime the stage must not tip (tilt &gt; tipOver or body contact →
    /// TippedOver) nor touch the ground outside the pad (OffPad); after settleTime all feet must be over the
    /// pad → Success, else OffPad.</item>
    /// </list>
    /// OutOfFuel: if the propellant ran out while airborne, <see cref="FuelExhausted"/> is set and the flight
    /// continues; a failed touchdown then reports OutOfFuel as its reason (a hard impact still sets
    /// <see cref="ImpactTooHard"/> so the explosion plays). A gentle touchdown on an empty tank still counts.
    /// </summary>
    public class LandingEvaluator
    {
        readonly LandingCriteria criteria;

        public LandingCriteria Criteria => criteria;
        public LandingState State { get; private set; }
        public FailureReason Reason { get; private set; }
        public float SettleTimer { get; private set; }
        public float TouchdownVerticalSpeed { get; private set; }
        public float TouchdownHorizontalSpeed { get; private set; }
        public float TouchdownTiltDeg { get; private set; }
        /// <summary>Propellant ran out before touchdown.</summary>
        public bool FuelExhausted { get; private set; }
        /// <summary>Touchdown speed exceeded the limits (explosion), whatever the reported reason.</summary>
        public bool ImpactTooHard { get; private set; }
        public bool IsFinished => State == LandingState.Success || State == LandingState.Failure;

        public LandingEvaluator(LandingCriteria criteria)
        {
            this.criteria = criteria;
            Reset();
        }

        public void Reset()
        {
            State = LandingState.Flying;
            Reason = FailureReason.None;
            SettleTimer = 0f;
            FuelExhausted = ImpactTooHard = false;
            TouchdownVerticalSpeed = TouchdownHorizontalSpeed = TouchdownTiltDeg = 0f;
        }

        public LandingState Tick(in FlightSample s, float dt)
        {
            switch (State)
            {
                case LandingState.Flying:
                    TickFlying(s);
                    break;
                case LandingState.Settling:
                    TickSettling(s, dt);
                    break;
            }
            return State;
        }

        void TickFlying(in FlightSample s)
        {
            if (!s.AnyContact)
            {
                if (s.fuelKg <= 0f) FuelExhausted = true;
                return;
            }

            Vector3 rel = s.velocity - s.surfaceVelocity;
            TouchdownVerticalSpeed = Mathf.Max(0f, -rel.y);
            TouchdownHorizontalSpeed = new Vector2(rel.x, rel.z).magnitude;
            TouchdownTiltDeg = s.tiltDeg;

            ImpactTooHard = TouchdownVerticalSpeed > criteria.maxVerticalSpeed || TouchdownHorizontalSpeed > criteria.maxHorizontalSpeed;
            if (ImpactTooHard)
                Fail(FailureReason.HardImpact);
            else if (!s.legsDeployed)
                Fail(FailureReason.NoLegs);
            else if (s.bodyContact)
                Fail(FailureReason.TippedOver);
            else if (s.legsTouchingGround > 0)
                Fail(FailureReason.OffPad);
            else if (s.tiltDeg > criteria.maxTiltDeg)
                Fail(FailureReason.TooTilted);
            else
            {
                State = LandingState.Settling;
                SettleTimer = 0f;
            }
        }

        void TickSettling(in FlightSample s, float dt)
        {
            if (s.bodyContact || s.tiltDeg > criteria.tipOverTiltDeg)
            {
                Fail(FailureReason.TippedOver);
                return;
            }
            if (s.legsTouchingGround > 0)
            {
                Fail(FailureReason.OffPad);
                return;
            }
            SettleTimer += dt;
            if (SettleTimer >= criteria.settleTime)
            {
                if (s.legsOverPad >= s.legCount)
                    State = LandingState.Success;
                else
                    Fail(FailureReason.OffPad);
            }
        }

        void Fail(FailureReason reason)
        {
            State = LandingState.Failure;
            Reason = FuelExhausted ? FailureReason.OutOfFuel : reason;
        }

        /// <summary>Human-readable outcome line for the result panel.</summary>
        public string Describe()
        {
            switch (State)
            {
                case LandingState.Success:
                    return $"Touchdown {TouchdownVerticalSpeed:0.0} m/s vertical, {TouchdownHorizontalSpeed:0.0} m/s horizontal, tilt {TouchdownTiltDeg:0.0}°";
                case LandingState.Failure:
                    return DescribeFailure();
                case LandingState.Settling:
                    return "Touchdown - holding...";
                default:
                    return string.Empty;
            }
        }

        string DescribeFailure()
        {
            switch (Reason)
            {
                case FailureReason.HardImpact:
                    return TouchdownVerticalSpeed > criteria.maxVerticalSpeed
                        ? $"Hard impact: {TouchdownVerticalSpeed:0.0} m/s vertical (max {criteria.maxVerticalSpeed:0.0})"
                        : $"Hard impact: {TouchdownHorizontalSpeed:0.0} m/s horizontal (max {criteria.maxHorizontalSpeed:0.0})";
                case FailureReason.NoLegs: return "Touched down without landing legs";
                case FailureReason.TippedOver: return "Tipped over";
                case FailureReason.OffPad: return "Landed off the pad";
                case FailureReason.OutOfFuel:
                    return ImpactTooHard ? $"Out of fuel - impact at {TouchdownVerticalSpeed:0.0} m/s" : "Out of fuel";
                case FailureReason.TooTilted: return $"Touchdown tilt {TouchdownTiltDeg:0.0}° (max {criteria.maxTiltDeg:0.0}°)";
                default: return "Failure";
            }
        }
    }
}
