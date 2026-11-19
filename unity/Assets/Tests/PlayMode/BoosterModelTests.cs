using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    /// <summary>The Booster.fbx rig in Level01: node names, leg geometry after deployment, grid fins.
    /// An InputTestFixture so Level01's input actions never bind to real devices (keeps later input tests isolated).</summary>
    public class BoosterModelTests : InputTestFixture
    {
        /// <summary>Tests never read or write the developer's editor save.</summary>
        [SetUp]
        public void UseMemorySave() => SaveGame.UseMemory();

        // Rig contract from Assets/Models/Booster_rig.json (rocket-local, metres).
        const float FootRadius = 10.01f;
        const float FootCentreY = -2.00f;
        const float FootContactY = -2.20f;
        const float Tolerance = 0.1f;

        LandingSession session;
        RocketController rocket;

        /// <summary>Called at the start of each test, after InputTestFixture has set up its isolated input system.</summary>
        IEnumerator LoadLevel()
        {
            InputSystem.AddDevice<Keyboard>(); // same device setup as the other input-fixture tests
            yield return SceneManager.LoadSceneAsync("Level01", LoadSceneMode.Single);
            yield return null;
            session = Object.FindAnyObjectByType<LandingSession>();
            rocket = session.Rocket;
            Object.FindAnyObjectByType<PlayerControls>().enabled = false;
        }

        [UnityTest]
        public IEnumerator ModelNodesExistWithExpectedNames()
        {
            yield return LoadLevel();
            Transform model = rocket.transform.Find("BoosterModel");
            Assert.IsNotNull(model, "BoosterModel child");
            Assert.AreEqual(4, rocket.LegCount);
            for (int i = 0; i < 4; i++)
            {
                Transform leg = model.Find($"Leg_{i}");
                Assert.IsNotNull(leg, $"Leg_{i}");
                Assert.AreSame(leg, rocket.GetLeg(i).transform, $"leg {i} component sits on Leg_{i}");
                Assert.IsNotNull(leg.Find($"Foot_{i}"), $"Foot_{i} re-parented under Leg_{i}");
                Assert.IsNotNull(model.Find($"GridFin_{i}"), $"GridFin_{i}");
            }
            Assert.IsNotNull(rocket.GridFins);
            Assert.AreEqual(4, rocket.GridFins.Count);
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator DeployedFeetMatchRigGeometry()
        {
            yield return LoadLevel();
            rocket.DeployLegs();
            float start = Time.time;
            while (rocket.Legs != LegsState.Deployed && Time.time - start < 5f) yield return new WaitForFixedUpdate();
            Assert.AreEqual(LegsState.Deployed, rocket.Legs);
            yield return null;

            for (int i = 0; i < rocket.LegCount; i++)
            {
                RocketLeg leg = rocket.GetLeg(i);
                Vector3 local = rocket.transform.InverseTransformPoint(leg.FootPosition);
                float r = new Vector2(local.x, local.z).magnitude;
                float az = Mathf.Repeat(Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg, 360f);
                Assert.AreEqual(FootRadius, r, Tolerance, $"foot {i} radius");
                Assert.AreEqual(FootCentreY, local.y, Tolerance, $"foot {i} height");
                Assert.AreEqual(45f + 90f * i, az, 1f, $"foot {i} azimuth");
                // Foot pads stay level and their collider bottom is the touchdown plane.
                Assert.Less(Vector3.Angle(leg.Foot.up, rocket.transform.up), 0.5f, $"foot {i} level");
                Assert.IsTrue(leg.FootCollider.enabled);
                // Collider bounds follow the physics pose, the transform is interpolated: compare offsets only.
                Bounds b = leg.FootCollider.bounds;
                Assert.AreEqual(FootContactY - FootCentreY, b.min.y - b.center.y, Tolerance, $"foot {i} contact plane");
                Vector3 colliderLocal = Quaternion.Inverse(rocket.Rotation) * (b.center - rocket.Position); // physics pose
                Assert.Less(Vector3.Distance(colliderLocal, local), Tolerance, $"foot {i} collider sits on the foot model");
            }
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator GridFinsDeployDuringDescent()
        {
            yield return LoadLevel();
            Assert.Less(rocket.Velocity.y, -100f, "level starts falling fast");
            float start = Time.time;
            while (rocket.FinDeployProgress < 1f && Time.time - start < 3f) yield return null;
            Assert.AreEqual(1f, rocket.FinDeployProgress, 1e-4f);
            for (int i = 0; i < rocket.GridFins.Count; i++)
            {
                Transform fin = rocket.GridFins.GetFin(i);
                Vector3 outward = new Vector3(fin.localPosition.x, 0f, fin.localPosition.z).normalized;
                // Deployed fin's up axis points outward (±18° steering twist does not change that).
                Assert.Greater(Vector3.Dot(fin.localRotation * Vector3.up, outward), 0.95f, $"fin {i} deployed outward");
            }
        }
    }
}
