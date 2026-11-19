using NUnit.Framework;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using RocketSlam.World;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    /// <summary>M4b: the one wave function (sea + ship), the deck pose, deck-relative landing rules and the wind.</summary>
    public class SeaAndWindTests
    {
        static readonly SeaState Rough = new SeaState { swellAmplitude = 1.1f, swellPeriod = 10f, swellDirection = 60f, chopAmplitude = 0.35f, seed = 4 };

        [Test]
        public void WaveFieldIsDeterministic()
        {
            var a = new WaveField(Rough);
            var b = new WaveField(Rough);
            var other = new WaveField(new SeaState { swellAmplitude = 1.1f, swellPeriod = 10f, swellDirection = 60f, chopAmplitude = 0.35f, seed = 5 });
            bool differs = false;
            for (int i = 0; i < 200; i++)
            {
                float x = i * 37.3f - 3000f, z = i * -11.9f + 500f, t = i * 0.731f;
                Assert.AreEqual(a.Height(x, z, t), b.Height(x, z, t), "same sea state, same heights");
                Assert.LessOrEqual(Mathf.Abs(a.Height(x, z, t)), a.MaxHeight + 1e-4f);
                if (Mathf.Abs(a.Height(x, z, t) - other.Height(x, z, t)) > 0.01f) differs = true;
            }
            Assert.IsTrue(differs, "another seed gives another sea");
            Assert.IsTrue(new WaveField(default(SeaState)).IsFlat, "calm level: no waves");
            Assert.AreEqual(0f, new WaveField(default(SeaState)).Height(10f, 20f, 3f));
        }

        [Test]
        public void WavesTravelWithDeepWaterSpeed()
        {
            Wave w = WaveField.Make(1f, 10f, 90f, 0f); // towards east
            float c = w.omega / w.k;                     // phase speed g·T/2π ≈ 15.6 m/s
            Assert.AreEqual(9.81f * 10f / (2f * Mathf.PI), c, 0.01f);
            var f = new WaveField(new[] { w });
            Assert.AreEqual(f.Height(0f, 0f, 0f), f.Height(c * 3f, 0f, 3f), 1e-3f, "crest moved c·t east");
        }

        /// <summary>The deck pose IS the wave function: the plane fitted through the wave heights at the 5 × 3 hull
        /// stations (heave = mean, pitch / roll = least-squares slopes), at any time.</summary>
        [Test]
        public void HullPoseComesFromTheWaveFunction()
        {
            var f = new WaveField(Rough);
            const float L = 45f, W = 25f, freeboard = 3f;
            for (int i = 0; i < 50; i++)
            {
                float t = i * 0.37f;
                Pose p = f.HullPose(new Vector3(0f, freeboard, 0f), 0f, L, W, t);
                float mean = 0f, sx = 0f, sz = 0f, xx = 0f, zz = 0f;
                foreach (float fx in WaveField.HullStationsX)
                foreach (float fz in WaveField.HullStationsZ)
                {
                    float h = f.Height(fx * L, fz * W, t);
                    mean += h / 15f; sx += fx * L * h; sz += fz * W * h; xx += fx * L * fx * L; zz += fz * W * fz * W;
                }
                Assert.AreEqual(freeboard + mean, p.position.y, 1e-4f, "heave = mean wave height under the hull");
                Vector3 bow = p.position + p.rotation * new Vector3(L, 0f, 0f);
                Vector3 stern = p.position + p.rotation * new Vector3(-L, 0f, 0f);
                Vector3 left = p.position + p.rotation * new Vector3(0f, 0f, W);
                Vector3 right = p.position + p.rotation * new Vector3(0f, 0f, -W);
                Assert.AreEqual(2f * L * sx / xx, bow.y - stern.y, 0.02f, "pitch = fitted slope along the hull");
                Assert.AreEqual(2f * W * sz / zz, left.y - right.y, 0.02f, "roll = fitted slope across the hull");
                Assert.Less(Vector3.Angle(p.rotation * Vector3.up, Vector3.up), 5f, "deck stays within a few degrees");
            }
            // A swell much longer than the ship is followed almost exactly at the bow and the stern.
            var swell = new WaveField(new[] { WaveField.Make(1f, 20f, 90f, 0.3f) }); // λ ≈ 624 m along the hull
            for (int i = 0; i < 20; i++)
            {
                float t = i * 0.7f;
                Pose p = swell.HullPose(new Vector3(0f, freeboard, 0f), 0f, L, W, t);
                Vector3 bow = p.position + p.rotation * new Vector3(L, 0f, 0f);
                Assert.AreEqual(freeboard + swell.Height(L, 0f, t), bow.y, 0.25f, "bow rides the long swell");
            }
        }

        [Test]
        public void ShipMotionPoseAndPointVelocityMatchTheWaveFunction()
        {
            var go = new GameObject("Ship");
            try
            {
                go.AddComponent<Rigidbody>();
                ShipMotion ship = go.AddComponent<ShipMotion>();
                ship.Setup(Rough, 3f, 45f, 25f);
                var f = new WaveField(Rough);
                for (int i = 0; i < 20; i++)
                {
                    float t = 1f + i * 0.9f;
                    Pose a = ship.PoseAt(t), b = f.HullPose(new Vector3(0f, 3f, 0f), 0f, 45f, 25f, t);
                    Assert.AreEqual(b.position.y, a.position.y, 1e-5f);
                    Assert.Less(Quaternion.Angle(a.rotation, b.rotation), 1e-3f);
                }
                // Deck point velocity: displacement of a deck point over one step, divided by the step.
                Pose p0 = ship.PoseAt(0f), p1 = ship.PoseAt(0.02f);
                Vector3 local = new Vector3(30f, 0f, -10f);
                Vector3 expected = ((p1.position + p1.rotation * local) - (p0.position + p0.rotation * local)) / 0.02f;
                Vector3 v = ship.PointVelocity(p0.position + p0.rotation * local);
                Assert.Less((v - expected).magnitude, 1e-3f);
                Assert.Less(v.magnitude, 3f, "a deck moves at walking pace, not more");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LandingSiteMeasuresInTheDeckFrame()
        {
            var go = new GameObject("Site");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(4f, 0f, 0f));
                LandingSite site = go.AddComponent<LandingSite>();
                site.Setup(Vector3.zero, new Vector2(45f, 24.5f), null, "SHIP");
                Assert.IsTrue(site.IsOver(new Vector3(44f, 3f, 0f)));
                Assert.IsFalse(site.IsOver(new Vector3(46f, 3f, 0f)));
                Assert.IsFalse(site.IsOver(new Vector3(0f, 3f, 26f)));
                Vector3 under = site.SurfacePointUnder(0f, 10f);
                Assert.AreEqual(3f - 10f * Mathf.Tan(4f * Mathf.Deg2Rad), under.y, 1e-3f, "height on the tilted deck plane");
                Vector3 far = site.SurfacePointUnder(0f, 2000f);
                Assert.AreEqual(site.SurfacePointUnder(0f, 24.5f).y, far.y, 0.05f, "no extrapolation of the tilt far away");
                Assert.AreEqual(Vector3.zero, site.PointVelocity(Vector3.zero), "a site without ship motion is static");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---- deck-relative landing rules ----------------------------------------------------------------

        static FlightSample Touch(Vector3 rocketVelocity, Vector3 deckVelocity, float tilt = 1f) => new FlightSample
        {
            velocity = rocketVelocity,
            surfaceVelocity = deckVelocity,
            tiltDeg = tilt,
            fuelKg = 1000f,
            legsDeployed = true,
            legsTouchingPad = 2,
            legsOverPad = 4,
            legCount = 4,
        };

        [Test]
        public void TouchdownSpeedIsMeasuredRelativeToTheDeck()
        {
            var rules = new LandingCriteria();
            var e = new LandingEvaluator(rules);
            // 2 m/s down onto a deck rising at 1 m/s: 3 m/s closing speed → hard impact.
            Assert.AreEqual(LandingState.Failure, e.Tick(Touch(new Vector3(0f, -2f, 0f), new Vector3(0f, 1f, 0f)), 0.02f));
            Assert.IsTrue(e.ImpactTooHard);
            Assert.AreEqual(3f, e.TouchdownVerticalSpeed, 1e-4f);

            // 3 m/s down onto a deck falling away at 1 m/s: 2 m/s → fine.
            e.Reset();
            Assert.AreEqual(LandingState.Settling, e.Tick(Touch(new Vector3(0f, -3f, 0f), new Vector3(0f, -1f, 0f)), 0.02f));
            Assert.AreEqual(2f, e.TouchdownVerticalSpeed, 1e-4f);

            // Sideways: the deck slides at 1 m/s, the stage matches it → 0 horizontal.
            e.Reset();
            e.Tick(Touch(new Vector3(1f, -1f, 0.2f), new Vector3(1f, 0f, 0.2f)), 0.02f);
            Assert.AreEqual(LandingState.Settling, e.State);
            Assert.AreEqual(0f, e.TouchdownHorizontalSpeed, 1e-4f);

            // Ground pad: surface velocity zero → world speeds, exactly as before.
            e.Reset();
            e.Tick(Touch(new Vector3(0f, -2.4f, 0f), Vector3.zero), 0.02f);
            Assert.AreEqual(LandingState.Settling, e.State);
        }

        [Test]
        public void TiltLimitIsRelativeToTheDeck()
        {
            var e = new LandingEvaluator(new LandingCriteria());
            // The caller passes the angle to the deck normal: square to a deck rolled 3° is 0°, fine.
            e.Tick(Touch(new Vector3(0f, -1f, 0f), Vector3.zero, tilt: 0.5f), 0.02f);
            Assert.AreEqual(LandingState.Settling, e.State);
            e.Reset();
            e.Tick(Touch(new Vector3(0f, -1f, 0f), Vector3.zero, tilt: 7f), 0.02f);
            Assert.AreEqual(FailureReason.TooTilted, e.Reason);
        }

        // ---- wind ---------------------------------------------------------------------------------------

        [Test]
        public void WindFieldIsDeterministicSteadyPlusBoundedGusts()
        {
            var s = new WindSettings { steady = new Vector2(6f, -2f), gustStrength = 3f, gustFrequency = 0.1f, seed = 7 };
            var a = new WindField(s);
            var b = new WindField(s);
            Vector3 steady = new Vector3(6f, 0f, -2f);
            Vector3 mean = Vector3.zero;
            int n = 0;
            for (float t = 0f; t < 600f; t += 0.25f, n++)
            {
                Vector3 w = a.At(t);
                Assert.AreEqual(w, b.At(t));
                Assert.AreEqual(0f, w.y);
                Assert.LessOrEqual((w - steady).magnitude, 3f * 1.2f, "gusts stay near their strength");
                mean += w;
            }
            mean /= n;
            Assert.Less((mean - steady).magnitude, 1.5f, "on average the steady wind");
            Assert.AreEqual(Vector3.zero, new WindField(default).At(12f), "calm level");
            Assert.IsTrue(new WindField(default).IsCalm);
        }

        /// <summary>The visible ocean patch (Burst job) and the ship use one wave formula on one clock: the patch height
        /// at a grid point equals WaveField.Height there (times the edge fade).</summary>
        [Test]
        public void OceanPatchMatchesTheWaveField()
        {
            var go = new GameObject("OceanTest");
            go.transform.position = new Vector3(120f, 0f, -340f);
            var ocean = go.AddComponent<OceanSurface>();
            try
            {
                ocean.Setup(Rough, null, 350f, 50);
                ocean.Init();
                foreach (float t in new[] { 0f, 3.7f, 41.25f })
                {
                    ocean.Evaluate(t);
                    int checkedFull = 0;
                    for (int iz = 0; iz <= ocean.Cells; iz += 3)
                    for (int ix = 0; ix <= ocean.Cells; ix += 3)
                    {
                        Vector3 p = ocean.PatchPoint(ix, iz);
                        float fade = ocean.PatchFade(ix, iz);
                        Assert.AreEqual(fade * ocean.Field.Height(p.x, p.z, t), ocean.PatchHeight(ix, iz), 2e-3f, $"grid ({ix},{iz}) t={t}");
                        if (fade > 0.999f) checkedFull++;
                    }
                    Assert.Greater(checkedFull, 20, "plenty of points with the full wave height");
                }
            }
            finally
            {
                ocean.Release();
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClearanceIsMeasuredToTheTiltedSurfaceUnderEachPoint()
        {
            Vector3 deck = new Vector3(0f, 3f, 0f);
            Vector3 up = Quaternion.Euler(0f, 0f, 3f) * Vector3.up; // normal leans to -x: the +x side is higher
            float h = RocketPhysics.SurfaceClearance(new Vector3(0f, 5f, 0f), deck, up);
            Assert.AreEqual(2f, h, 1e-4f, "right above the anchor");
            float footHigh = RocketPhysics.SurfaceClearance(new Vector3(9f, 5f, 0f), deck, up);
            float footLow = RocketPhysics.SurfaceClearance(new Vector3(-9f, 5f, 0f), deck, up);
            Assert.AreEqual(2f - 9f * Mathf.Tan(3f * Mathf.Deg2Rad), footHigh, 1e-3f, "foot over the raised side is closer");
            Assert.AreEqual(2f + 9f * Mathf.Tan(3f * Mathf.Deg2Rad), footLow, 1e-3f);
            Assert.AreEqual(2f, RocketPhysics.SurfaceClearance(new Vector3(30f, 5f, -7f), deck, Vector3.up), 1e-5f, "flat pad");
        }

        [Test]
        public void WindIsCountedOnceAxialDragPlusSideForce()
        {
            var c = ScriptableObject.CreateInstance<RocketConfig>();
            try
            {
                Vector3 v = new Vector3(0f, -100f, 0f), wind = new Vector3(8f, 0f, 0f);
                Vector3 axial = RocketPhysics.AxialDrag(c, v - wind);
                Assert.AreEqual(0f, axial.x); Assert.AreEqual(0f, axial.z);
                Assert.AreEqual(RocketPhysics.AxialDrag(c, v), axial, "end-on drag doesn't see a horizontal wind");
                Assert.AreEqual(-RocketPhysics.Drag(c, v).y, -axial.y, 1e-2f, "straight fall: same as the full drag");
                Assert.Greater(RocketPhysics.SideWindForce(c, v, wind).x, 0f, "the crosswind acts only through the side force");
            }
            finally { Object.DestroyImmediate(c); }
        }

        [Test]
        public void SideWindPushesDownwindAndVanishesWhenMovingWithTheAir()
        {
            var c = ScriptableObject.CreateInstance<RocketConfig>();
            try
            {
                Vector3 wind = new Vector3(8f, 0f, 0f);
                Vector3 f = RocketPhysics.SideWindForce(c, Vector3.zero, wind);
                float area = 2f * c.bodyRadius * c.bodyHeight;
                Assert.AreEqual(0.5f * c.airDensity * c.sideDragCoefficient * area * 64f, f.x, 1f, "½ρ·Cd·A·v²");
                Assert.AreEqual(0f, f.y);
                Assert.AreEqual(0f, f.z, 1e-3f);
                Assert.AreEqual(Vector3.zero, RocketPhysics.SideWindForce(c, wind, wind), "drifting with the wind: no force");
                Assert.AreEqual(Vector3.zero, RocketPhysics.SideWindForce(c, new Vector3(0f, -50f, 0f), Vector3.zero), "falling straight in still air");
                // An 8 m/s crosswind on the 30 t stage: noticeable (> 0.1 m/s²), but far below what the assist can lean against.
                float a = f.magnitude / RocketPhysics.Mass(c, 8000f);
                Assert.Greater(a, 0.1f);
                Assert.Less(a, 1f);
            }
            finally
            {
                Object.DestroyImmediate(c);
            }
        }
    }
}
