using NUnit.Framework;
using RocketSlam.Effects;
using RocketSlam.World;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    /// <summary>Ground shape, splat painting primitives, flame clamp, and the built Level01 scenery layout.</summary>
    public class EnvironmentTests
    {
        [Test]
        public void GroundIsFlatAroundThePadAndFadesAtTheEdge()
        {
            var shape = new TerrainShape();
            for (float r = 0f; r <= shape.flatRadius; r += 50f)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * Mathf.PI / 4f;
                Assert.AreEqual(0f, shape.Height(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r), $"flat at r {r}");
            }
            float max = 0f;
            for (float x = -9000f; x <= 9000f; x += 250f)
            for (float z = -9000f; z <= 9000f; z += 250f)
            {
                float h = shape.Height(x, z);
                Assert.GreaterOrEqual(h, 0f, "never below the pad level");
                max = Mathf.Max(max, h);
            }
            Assert.Greater(max, 150f, "hills / ridges exist far out");
            Assert.AreEqual(0f, shape.Height(9900f, 0f), "back to 0 at the terrain edge (meets the far ground)");
            Assert.AreEqual(0f, shape.Height(-9900f, 9900f));
        }

        [Test]
        public void PaintShapesCoverTheirInsideAndFadeOutside()
        {
            PaintShape c = PaintShape.Circle(2, Vector2.zero, 100f, 10f);
            Assert.AreEqual(1f, c.Coverage(0f, 0f), 1e-4f);
            Assert.AreEqual(0.5f, c.Coverage(100f, 0f), 1e-3f, "half at the edge");
            Assert.AreEqual(0f, c.Coverage(120f, 0f));

            PaintShape r = PaintShape.Rectangle(1, new Vector2(500f, 0f), new Vector2(200f, 50f), 90f, 2f);
            Assert.AreEqual(1f, r.Coverage(500f, 90f), 1e-4f, "rotated 90°: long side along z");
            Assert.AreEqual(0f, r.Coverage(590f, 0f));

            PaintShape s = PaintShape.Segment(3, new Vector2(0f, 0f), new Vector2(0f, 1000f), 5f, 2f);
            Assert.AreEqual(1f, s.Coverage(2f, 500f), 1e-4f);
            Assert.AreEqual(0f, s.Coverage(10f, 500f));
            Assert.IsTrue(s.Bounds().Contains(new Vector2(0f, 1000f)));
        }

        [Test]
        public void FlameNeverReachesThroughTheGround()
        {
            Assert.AreEqual(20f, EffectsMath.ClampFlameToGround(20f, float.PositiveInfinity));
            Assert.AreEqual(8.5f, EffectsMath.ClampFlameToGround(20f, 10f), 1e-4f);
            Assert.AreEqual(1.5f, EffectsMath.ClampFlameToGround(20f, 0.2f), 1e-4f, "minimum stub at the pad");
            Assert.AreEqual(1f, EffectsMath.PlumeGroundScale(float.PositiveInfinity));
            Assert.Less(EffectsMath.PlumeGroundScale(0f), 0.5f);
        }

        /// <summary>The built level keeps the landing zone clear: no props inside 60 m of the pad except the road
        /// leading onto the apron, no buildings inside 150 m, and a dense enough world to read from 3 km. The props are
        /// generated at load from the scene's <see cref="LaunchSite"/> (not stored in the scene).</summary>
        [Test]
        public void Level01SceneryKeepsThePadClear()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity");
            var props = Object.FindAnyObjectByType<InstancedScenery>();
            Assert.IsNotNull(props, "InstancedScenery in Level01");
            Assert.IsNotNull(Object.FindAnyObjectByType<GroundTerrain>(), "GroundTerrain in Level01");
            var site = Object.FindAnyObjectByType<LaunchSite>();
            Assert.IsNotNull(site, "LaunchSite in Level01");
            LaunchSiteLayout.Result layout = LaunchSiteLayout.Generate(site.Seed, site.Radius);
            int trees = 0, total = 0;
            foreach (InstancedScenery.Kind k in props.Kinds)
            {
                Assert.IsNotNull(k.mesh, k.name);
                Assert.AreEqual(0, k.items.Count, $"{k.name}: instances are not serialised in the scene");
                PropKind generated = layout.Find(k.name);
                Assert.IsNotNull(generated, $"{k.name} is generated at load");
                bool road = k.name.StartsWith("Scenery_Road");
                foreach (InstancedScenery.Item it in generated.items)
                {
                    float r = new Vector2(it.p.x, it.p.z).magnitude;
                    Assert.Greater(r, road ? 30f : 60f, $"{k.name} at {it.p} too close to the pad");
                    total++;
                }
                if (k.name.Contains("Conifer") || k.name.Contains("Broadleaf")) trees += generated.items.Count;
            }
            Assert.Greater(trees, 2000, "forests");
            Assert.Greater(total, 4000);
            Assert.AreEqual(layout.buildings.Count, GameObject.Find("Buildings").transform.childCount, "same buildings as the generator");
            foreach (BoxCollider b in GameObject.Find("Buildings").GetComponentsInChildren<BoxCollider>())
                Assert.Greater(new Vector2(b.transform.position.x, b.transform.position.z).magnitude, 150f, b.name);
        }

        [Test]
        public void LaunchSiteLayoutIsDeterministic()
        {
            LaunchSiteLayout.Result a = LaunchSiteLayout.Generate(4242, _ => 20f);
            LaunchSiteLayout.Result b = LaunchSiteLayout.Generate(4242, _ => 20f);
            Assert.AreEqual(a.paints.Count, b.paints.Count);
            Assert.AreEqual(a.kinds.Count, b.kinds.Count);
            for (int i = 0; i < a.kinds.Count; i++)
            {
                Assert.AreEqual(a.kinds[i].key, b.kinds[i].key);
                Assert.AreEqual(a.kinds[i].items.Count, b.kinds[i].items.Count);
                for (int j = 0; j < a.kinds[i].items.Count; j += 97)
                    Assert.AreEqual(a.kinds[i].items[j].p, b.kinds[i].items[j].p);
            }
            Assert.Less(a.milliseconds, 2000, "cheap enough to run at load");
        }

        /// <summary>Scenes stay small in git: no per-instance data serialised (M4a's Level01 was 6.6 MB).</summary>
        [Test]
        public void LevelScenesAreSmall()
        {
            for (int n = 1; n <= 5; n++)
            {
                string path = $"Assets/Scenes/Level{n:00}.unity";
                Assert.IsTrue(System.IO.File.Exists(path), path);
                long size = new System.IO.FileInfo(path).Length;
                Assert.Less(size, 1024 * 1024, $"{path} is {size / 1024} KB");
            }
        }
    }
}
