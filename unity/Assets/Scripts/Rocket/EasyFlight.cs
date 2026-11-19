using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>What the big centred burn cue tells the player in Easy mode.</summary>
    public enum CueKind
    {
        None,
        /// <summary>Falling, engine off, braking not needed yet: "FIRE IN 640 m".</summary>
        FireIn,
        /// <summary>Braking needed now and the engine is off: flashing "FIRE NOW - HOLD SPACE".</summary>
        FireNow,
        /// <summary>Engine on during the main braking: "KEEP HOLDING".</summary>
        KeepHolding,
        /// <summary>Engine on far too early: "TOO EARLY - release to save fuel".</summary>
        TooEarly,
        /// <summary>Last metres: "TOUCHDOWN: keep under 2.5 m/s" with the fall speed readout.</summary>
        Touchdown,
    }

    public struct BurnCue
    {
        public CueKind kind;
        /// <summary>For <see cref="CueKind.FireIn"/>: metres of fall left before FIRE NOW.</summary>
        public float distance;
        /// <summary>Altitude at which braking at full throttle (with margin) has to start.</summary>
        public float fireAltitude;
    }

    /// <summary>
    /// Pure Easy-mode maths (no Unity objects, EditMode-testable): the descent-speed floor that stops the stage
    /// from ever climbing, the matching thrust cap, and the burn cue.
    /// </summary>
    public static class EasyFlight
    {
        /// <summary>Slowest allowed fall speed (positive, m/s) at altitude <paramref name="h"/>:
        /// max(minDescent, min(h·perMetre, maxFloor)). The engine is throttled back so the fall never gets slower.</summary>
        public static float FloorSpeed(RocketConfig c, float h)
            => Mathf.Max(c.easyMinDescentSpeed, Mathf.Min(Mathf.Max(0f, h) * c.easyFloorPerMetre, c.easyMaxFloorSpeed));

        /// <summary>
        /// Highest throttle (0..1) allowed now: the thrust that drives the vertical speed towards -FloorSpeed with
        /// gain <c>easyFloorGain</c>, accounting for gravity, vertical drag and tilt. 1 when falling fast, about
        /// hover when at the floor speed, 0 when moving up.
        /// </summary>
        public static float ThrottleCap(RocketConfig c, float massKg, float altitude, float verticalSpeed, float upDotY, float dragUpN, float gravity)
        {
            float vFloor = -FloorSpeed(c, altitude);
            float accelUp = gravity + c.easyFloorGain * (vFloor - verticalSpeed);
            float thrustN = (massKg * accelUp - dragUpN) / Mathf.Max(0.3f, upDotY);
            return Mathf.Clamp01(thrustN / c.maxThrustN);
        }

        /// <summary>Highest throttle that keeps the world vertical speed at or below zero (never climbs, even when the
        /// floor is held relative to a deck that rises faster than the floor speed).</summary>
        public static float NoClimbCap(RocketConfig c, float massKg, float verticalSpeed, float upDotY, float dragUpN, float gravity)
        {
            float accelUp = gravity + c.easyFloorGain * (0f - verticalSpeed);
            float thrustN = (massKg * accelUp - dragUpN) / Mathf.Max(0.3f, upDotY);
            return Mathf.Clamp01(thrustN / c.maxThrustN);
        }

        /// <summary>
        /// Lowest throttle (0..1) the touchdown guard holds below <c>easyGuardAltitude</c>: the thrust that drives the
        /// fall speed relative to the deck towards <c>easyGuardSpeed</c>. 0 above the guard altitude. Released key in the
        /// last metres = gentle settle at the guard speed instead of free fall (a pulsing player re-presses only after
        /// the fall is already fast, and the engine ramp adds more lag on top).
        /// </summary>
        public static float GuardThrottle(RocketConfig c, float massKg, float altitude, float relVerticalSpeed, float upDotY, float dragUpN, float gravity)
        {
            if (altitude > c.easyGuardAltitude) return 0f;
            float accelUp = gravity + c.easyGuardGain * (-c.easyGuardSpeed - relVerticalSpeed);
            float thrustN = (massKg * accelUp - dragUpN) / Mathf.Max(0.3f, upDotY);
            return Mathf.Clamp01(thrustN / c.maxThrustN);
        }

        /// <summary>Full-throttle braking deceleration (net of gravity) used by the cue. Half of the current drag is
        /// counted, because drag fades as the stage slows.</summary>
        public static float BrakingDeceleration(RocketConfig c, float massKg, float speed, float gravity)
        {
            float dragK = 0.5f * c.airDensity * c.dragCoefficient * c.dragAreaM2;
            return Mathf.Max(0.5f, c.maxThrustN / massKg - gravity + 0.5f * dragK * speed * speed / massKg);
        }

        /// <summary>
        /// Altitude at which the player must start braking: the main burn (down to the floor speed) has to end at
        /// <c>cueBurnEndAltitude</c>, and close to the ground the stage must stop above 0, both with
        /// <c>cueMargin</c> of the available deceleration.
        /// </summary>
        public static float FireAltitude(RocketConfig c, float massKg, float fallSpeed, float gravity)
        {
            float a = c.cueMargin * BrakingDeceleration(c, massKg, fallSpeed, gravity);
            float v2 = fallSpeed * fallSpeed;
            float floor = c.easyMaxFloorSpeed;
            float main = fallSpeed > floor ? c.cueBurnEndAltitude + (v2 - floor * floor) / (2f * a) : 0f;
            float touchdown = c.landing.maxVerticalSpeed * 0.6f;
            float last = fallSpeed > touchdown ? (v2 - touchdown * touchdown) / (2f * a) : 0f;
            return Mathf.Max(main, last);
        }

        /// <summary>
        /// The cue for the current state. <paramref name="burnEarned"/> = the cue already asked to fire during this
        /// attempt (after that, holding is never called "too early").
        /// </summary>
        public static BurnCue Evaluate(RocketConfig c, float massKg, float altitude, float verticalSpeed, bool thrustHeld, bool burnEarned, float gravity)
        {
            float fall = Mathf.Max(0f, -verticalSpeed);
            float fireAlt = FireAltitude(c, massKg, fall, gravity);
            var cue = new BurnCue { fireAltitude = fireAlt };
            bool needBurn = altitude <= fireAlt;

            if (altitude <= c.cueFinalAltitude)
                cue.kind = !thrustHeld && needBurn ? CueKind.FireNow : CueKind.Touchdown;
            else if (thrustHeld)
                cue.kind = !burnEarned && altitude > fireAlt + 300f && fall > c.easyMaxFloorSpeed + 3f ? CueKind.TooEarly : CueKind.KeepHolding;
            else if (needBurn)
                cue.kind = CueKind.FireNow;
            else
            {
                cue.kind = CueKind.FireIn;
                cue.distance = altitude - fireAlt;
            }
            return cue;
        }
    }
}
