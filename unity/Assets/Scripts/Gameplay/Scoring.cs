using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>What a finished attempt is scored from.</summary>
    public struct ScoreInput
    {
        public bool landed;
        public float fuelLeftKg;
        public float startFuelKg;
        /// <summary>Horizontal distance of the engine centre from the target centre after touchdown, m.</summary>
        public float distanceFromCentreM;
        /// <summary>Touchdown speeds relative to the landing surface (positive), m/s.</summary>
        public float touchdownVerticalSpeed;
        public float touchdownHorizontalSpeed;
        /// <summary>The landing limits (<see cref="LandingCriteria"/>).</summary>
        public float maxVerticalSpeed;
        public float maxHorizontalSpeed;
    }

    /// <summary>Per-level score needed for the second and third star (the first star = landed).</summary>
    public struct StarThresholds
    {
        public int twoStars;
        public int threeStars;

        public StarThresholds(int two, int three)
        {
            twoStars = two;
            threeStars = three;
        }
    }

    public struct ScoreBreakdown
    {
        public bool landed;
        public int fuel;
        public int precision;
        public int softness;
        public int total;
        public int stars;
    }

    /// <summary>
    /// Points for a landing (pure maths, EditMode-tested), 0-1000 in total:
    /// <list type="bullet">
    /// <item>FUEL 0-400: linear in the fraction of the level's fuel left, full at <see cref="FullFuelFraction"/>
    /// (a perfect suicide burn leaves 50-62 %, a first-time Easy landing 30-43 %).</item>
    /// <item>PRECISION 0-300: full within <see cref="PerfectRadius"/> of the target centre, linear down to 0 at
    /// <see cref="ZeroRadius"/>.</item>
    /// <item>SOFTNESS 0-300: 75 % vertical touchdown speed (full at <see cref="PerfectSpeed"/>, 0 at the limit),
    /// 25 % horizontal speed (full at 0, 0 at the limit).</item>
    /// </list>
    /// A failed attempt scores 0 and 0 stars. Stars: 1 = landed, 2 = total ≥ the level's two-star score,
    /// 3 = total ≥ its three-star score (see the level table in <c>SceneBuilder</c>).
    /// </summary>
    public static class Scoring
    {
        public const int MaxFuel = 400;
        public const int MaxPrecision = 300;
        public const int MaxSoftness = 300;
        public const int MaxTotal = MaxFuel + MaxPrecision + MaxSoftness;
        public const float FullFuelFraction = 0.65f;
        public const float PerfectRadius = 0.5f;
        public const float ZeroRadius = 12f;
        public const float PerfectSpeed = 0.5f;
        const float VerticalWeight = 0.75f;

        public static ScoreBreakdown Compute(in ScoreInput s, StarThresholds stars)
        {
            var b = new ScoreBreakdown { landed = s.landed };
            if (!s.landed) return b;
            // NaN / Infinity in any input (a broken physics frame) must not turn into int.MinValue points.
            float startFuel = Finite(s.startFuelKg, 0f);
            float fuelFrac = startFuel > 0f ? Mathf.Max(0f, Finite(s.fuelLeftKg, 0f)) / startFuel : 0f;
            b.fuel = Mathf.RoundToInt(MaxFuel * Mathf.Clamp01(fuelFrac / FullFuelFraction));
            float d = Mathf.Max(0f, Finite(s.distanceFromCentreM, ZeroRadius));
            b.precision = Mathf.RoundToInt(MaxPrecision * Mathf.Clamp01(1f - (d - PerfectRadius) / (ZeroRadius - PerfectRadius)));
            float vLimit = Mathf.Max(PerfectSpeed + 0.01f, Finite(s.maxVerticalSpeed, 0f));
            float vertical = Mathf.Clamp01((vLimit - Mathf.Abs(Finite(s.touchdownVerticalSpeed, vLimit))) / (vLimit - PerfectSpeed));
            float hLimit = Finite(s.maxHorizontalSpeed, 0f);
            float horizontal = hLimit > 0f ? Mathf.Clamp01(1f - Mathf.Abs(Finite(s.touchdownHorizontalSpeed, hLimit)) / hLimit) : 1f;
            b.softness = Mathf.RoundToInt(MaxSoftness * (VerticalWeight * vertical + (1f - VerticalWeight) * horizontal));
            b.total = b.fuel + b.precision + b.softness;
            b.stars = Stars(true, b.total, stars);
            return b;
        }

        /// <summary><paramref name="v"/>, or <paramref name="fallback"/> when it is NaN or infinite.</summary>
        static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

        public static int Stars(bool landed, int total, StarThresholds t)
        {
            if (!landed) return 0;
            if (total >= t.threeStars) return 3;
            if (total >= t.twoStars) return 2;
            return 1;
        }
    }
}
