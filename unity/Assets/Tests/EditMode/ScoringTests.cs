using NUnit.Framework;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEditor;

namespace RocketSlam.Tests.EditMode
{
    public class ScoringTests
    {
        static readonly StarThresholds Stars = new StarThresholds(650, 790);

        static ScoreInput Landing(float fuelFrac = 0.4f, float distance = 0f, float vertical = 1.5f, float horizontal = 0f) => new ScoreInput
        {
            landed = true,
            fuelLeftKg = 10000f * fuelFrac,
            startFuelKg = 10000f,
            distanceFromCentreM = distance,
            touchdownVerticalSpeed = vertical,
            touchdownHorizontalSpeed = horizontal,
            maxVerticalSpeed = 2.5f,
            maxHorizontalSpeed = 1.5f,
        };

        [Test]
        public void FailureScoresNothing()
        {
            ScoreInput s = Landing(0.9f, 0f, 0.2f);
            s.landed = false;
            ScoreBreakdown b = Scoring.Compute(s, Stars);
            Assert.AreEqual(0, b.total);
            Assert.AreEqual(0, b.fuel + b.precision + b.softness);
            Assert.AreEqual(0, b.stars);
        }

        [Test]
        public void PerfectLandingScoresMaximum()
        {
            ScoreBreakdown b = Scoring.Compute(Landing(Scoring.FullFuelFraction, Scoring.PerfectRadius, Scoring.PerfectSpeed, 0f), Stars);
            Assert.AreEqual(Scoring.MaxFuel, b.fuel);
            Assert.AreEqual(Scoring.MaxPrecision, b.precision);
            Assert.AreEqual(Scoring.MaxSoftness, b.softness);
            Assert.AreEqual(Scoring.MaxTotal, b.total);
            Assert.AreEqual(1000, Scoring.MaxTotal);
            Assert.AreEqual(3, b.stars);
            // more fuel than the full-score fraction does not overflow
            Assert.AreEqual(Scoring.MaxFuel, Scoring.Compute(Landing(1f), Stars).fuel);
        }

        [Test]
        public void NaNAndInfinityNeverBreakTheScore([Values(float.NaN, float.PositiveInfinity, float.NegativeInfinity)] float bad)
        {
            for (int field = 0; field < 6; field++)
            {
                ScoreInput s = Landing();
                switch (field)
                {
                    case 0: s.fuelLeftKg = bad; break;
                    case 1: s.startFuelKg = bad; break;
                    case 2: s.distanceFromCentreM = bad; break;
                    case 3: s.touchdownVerticalSpeed = bad; break;
                    case 4: s.touchdownHorizontalSpeed = bad; break;
                    case 5: s.maxVerticalSpeed = bad; break;
                }
                ScoreBreakdown b = Scoring.Compute(s, Stars);
                foreach (int part in new[] { b.fuel, b.precision, b.softness })
                    Assert.That(part, Is.InRange(0, Scoring.MaxSoftness + Scoring.MaxFuel), $"field {field}");
                Assert.That(b.total, Is.InRange(0, Scoring.MaxTotal), $"field {field}");
                Assert.AreEqual(b.fuel + b.precision + b.softness, b.total);
                Assert.That(b.stars, Is.InRange(1, 3));
            }
        }

        [Test]
        public void ComponentsAreLinearAndClamped()
        {
            Assert.AreEqual(200, Scoring.Compute(Landing(Scoring.FullFuelFraction * 0.5f), Stars).fuel);
            Assert.AreEqual(0, Scoring.Compute(Landing(0f), Stars).fuel);
            float mid = (Scoring.PerfectRadius + Scoring.ZeroRadius) * 0.5f;
            Assert.AreEqual(150, Scoring.Compute(Landing(distance: mid), Stars).precision);
            Assert.AreEqual(0, Scoring.Compute(Landing(distance: Scoring.ZeroRadius + 5f), Stars).precision);
            // vertical 1.5 of limit 2.5 (perfect 0.5): half of the vertical part; no sideways speed = full horizontal part
            Assert.AreEqual(Mathf(300f * (0.75f * 0.5f + 0.25f)), Scoring.Compute(Landing(vertical: 1.5f), Stars).softness);
            Assert.AreEqual(Mathf(300f * 0.75f), Scoring.Compute(Landing(vertical: 0.5f, horizontal: 1.5f), Stars).softness);
            Assert.AreEqual(0, Scoring.Compute(Landing(vertical: 2.5f, horizontal: 1.5f), Stars).softness);
            Assert.AreEqual(Scoring.Compute(Landing(vertical: 1f), Stars).softness, Scoring.Compute(Landing(vertical: -1f), Stars).softness, "sign does not matter");
        }

        static int Mathf(float v) => UnityEngine.Mathf.RoundToInt(v);

        [Test]
        public void TotalIsTheSumAndBetterLandingsScoreMore()
        {
            ScoreBreakdown a = Scoring.Compute(Landing(0.35f, 3f, 2f, 0.5f), Stars);
            ScoreBreakdown b = Scoring.Compute(Landing(0.5f, 1f, 1f, 0.1f), Stars);
            Assert.AreEqual(a.fuel + a.precision + a.softness, a.total);
            Assert.Greater(b.total, a.total);
        }

        [Test]
        public void StarThresholds()
        {
            Assert.AreEqual(0, Scoring.Stars(false, 999, Stars));
            Assert.AreEqual(1, Scoring.Stars(true, 0, Stars), "landing at all = one star");
            Assert.AreEqual(1, Scoring.Stars(true, 649, Stars));
            Assert.AreEqual(2, Scoring.Stars(true, 650, Stars));
            Assert.AreEqual(2, Scoring.Stars(true, 789, Stars));
            Assert.AreEqual(3, Scoring.Stars(true, 790, Stars));
        }

        /// <summary>The shipped level table: thresholds ordered and reachable (the PlayMode bots in CampaignLevelsTests check
        /// which skill gets which star count).</summary>
        [Test]
        public void LevelThresholdsAreSane([NUnit.Framework.Range(1, 5)] int n)
        {
            var level = AssetDatabase.LoadAssetAtPath<LevelConfig>($"Assets/Settings/Levels/Level{n:00}.asset");
            Assert.IsNotNull(level);
            Assert.Greater(level.twoStarScore, 0);
            Assert.Greater(level.threeStarScore, level.twoStarScore);
            Assert.Less(level.threeStarScore, Scoring.MaxTotal);
            Assert.IsNotEmpty(level.conditions);
        }
    }
}
