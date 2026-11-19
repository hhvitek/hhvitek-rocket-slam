using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace RocketSlam.World
{
    /// <summary>A prop type of the launch site (one instanced mesh / material), with its placed instances.</summary>
    public sealed class PropKind
    {
        public string key, model, suffix;
        public bool shadows;
        public float cull;
        /// <summary>Extra cull distance per metre of camera height (see <see cref="InstancedScenery.Kind"/>).</summary>
        public float cullPerMetre;
        /// <summary>Forest thinning distance (see <see cref="InstancedScenery.Kind.thinDistance"/>), 0 = off.</summary>
        public float thin;
        public readonly List<InstancedScenery.Item> items = new List<InstancedScenery.Item>();
    }

    /// <summary>A building of the launch site (solid GameObject placed by the scene builder).</summary>
    public struct BuildingSpot
    {
        public string model;
        public Vector2 position;
        public float yaw;
    }

    /// <summary>
    /// Level 1 / 2 launch-site layout, generated from a seed at load (runtime, so the 50k+ prop instances are not
    /// serialised into the scenes): the ground paint shapes (fields, forest floors, roads, apron) for
    /// <see cref="GroundTerrain"/>, the instanced props (trees, bushes, rocks, fences, lamps, road tiles) for
    /// <see cref="InstancedScenery"/> and the building spots (placed as GameObjects by the editor scene builder, which
    /// runs the same generator). Deterministic: the same seed and building sizes give the same layout in the editor
    /// and in the player. Pad at the world origin; x = east, z = north. Nothing within ~60 m of the pad centre except
    /// the apron, its markers and lights; nothing tall near the descent path.
    /// </summary>
    public static class LaunchSiteLayout
    {
        public const float ApronRadius = 135f;
        public const float PadClearRadius = 60f;
        const float FenceHalf = 300f;
        static readonly Rect SiteRect = Rect.MinMaxRect(-1000f, -700f, 760f, 520f);
        const float TreeShadowRadius = 2600f;

        class Layout
        {
            public readonly List<PaintShape> paints = new List<PaintShape>();
            public readonly List<(Vector2 a, Vector2 b)> roads = new List<(Vector2, Vector2)>();
            public readonly List<(Vector2 c, float r)> blocked = new List<(Vector2, float)>();
            public readonly List<(Vector2 c, float r)> forests = new List<(Vector2, float)>();
            public readonly List<BuildingSpot> buildings = new List<BuildingSpot>();
            public Func<string, float> buildingRadius = _ => 10f;

            public float RoadDistance(Vector2 p)
            {
                float best = float.MaxValue;
                foreach ((Vector2 a, Vector2 b) in roads)
                {
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                    best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
                }
                return best;
            }

            /// <summary>True when a prop of radius <paramref name="clearance"/> at p touches no road, building or the pad zone.</summary>
            public bool Free(Vector2 p, float clearance)
            {
                if (p.magnitude < PadClearRadius + clearance) return false;
                if (RoadDistance(p) < 7f + clearance) return false;
                foreach ((Vector2 c, float r) in blocked)
                    if (Vector2.Distance(p, c) < r + clearance) return false;
                return true;
            }
        }


        public sealed class Result
        {
            public readonly List<PaintShape> paints;
            public readonly List<PropKind> kinds;
            public readonly List<BuildingSpot> buildings;
            public long milliseconds;

            public Result(List<PaintShape> p, List<PropKind> k, List<BuildingSpot> b)
            {
                paints = p;
                kinds = k;
                buildings = b;
            }

            public PropKind Find(string key) => kinds.Find(k => k.key == key);
        }

        static readonly Dictionary<int, Result> cache = new Dictionary<int, Result>();

        /// <summary>The layout for <paramref name="seed"/>; generated once per seed and process, then cached.</summary>
        public static Result Get(int seed, Func<string, float> buildingRadius)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(seed, out Result r)) cache[seed] = r = Generate(seed, buildingRadius);
                return r;
            }
        }

        /// <summary>Generate the whole layout (no caching). <paramref name="buildingRadius"/> gives each building
        /// model's footprint radius (props keep clear of it).</summary>
        public static Result Generate(int seed, Func<string, float> buildingRadius)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var shape = new TerrainShape();
            var rng = new Random(seed);
            var L = new Layout { buildingRadius = buildingRadius ?? (_ => 10f) };
            var kinds = new List<PropKind>();
            PropKind K(string model, bool shadows = true, float cull = 0f, string suffix = "")
            {
                string key = model + suffix;
                PropKind k = kinds.Find(x => x.key == key);
                if (k == null) kinds.Add(k = new PropKind { key = key, model = model, shadows = shadows, cull = cull, suffix = suffix });
                return k;
            }

            // Paint order = priority (later wins): forest floors and fields, site lawn, apron, hardstands, roads.
            PlaceForests(L, shape, rng, K);
            PlaceFields(L, shape, rng, K);
            PlaceScrub(L, rng);
            L.paints.Add(PaintShape.Circle(GroundTerrain.Gravel, Vector2.zero, ApronRadius, 4f, 3f));
            L.paints.Add(PaintShape.Circle(GroundTerrain.Asphalt, Vector2.zero, 31f, 2f));

            BuildRoads(L, K);
            PlaceSite(L, K);
            PlaceFences(L, rng, K);
            PlaceGroves(L, shape, rng, K);
            PlaceSmallProps(L, shape, rng, K);
            return new Result(L.paints, kinds, L.buildings) { milliseconds = sw.ElapsedMilliseconds };
        }

        public static bool IsFoliage(string model) => model.Contains("Conifer") || model.Contains("Broadleaf");

        static void Add(PropKind k, Vector3 p, float yaw, float scale = 1f)
            => k.items.Add(new InstancedScenery.Item { p = p, rs = new Vector2(yaw, scale) });

        static float Range(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static Vector3 OnGround(TerrainShape shape, Vector2 p, float sink = 0f) => new Vector3(p.x, shape.Height(p.x, p.y) - sink, p.y);

        static float YawTowards(Vector2 from, Vector2 to) => Mathf.Atan2(to.x - from.x, to.y - from.y) * Mathf.Rad2Deg;


        // ---- roads ----------------------------------------------------------------------------------

        /// <summary>
        /// Road network: tile meshes (10 m wide, 20 m long) in the flat zone, painted asphalt with gravel shoulders
        /// along the full length (roads continue over the hills to the edge of the map as paint only). Tile runs are
        /// laid out by count so every run meets its junction piece exactly.
        /// </summary>
        static void BuildRoads(Layout L, Func<string, bool, float, string, PropKind> K)
        {
            PropKind straight = K("Scenery_Road_Straight", false, 0f, "");
            PropKind tee = K("Scenery_Road_T", false, 0f, "");

            Vector2 Run(Vector2 start, Vector2 dir, int tiles)
            {
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                for (int i = 0; i < tiles; i++)
                {
                    Vector2 c = start + dir * (10f + 20f * i);
                    Add(straight, new Vector3(c.x, 0f, c.y), yaw);
                }
                return start + dir * (20f * tiles);
            }
            void Paint(Vector2 a, Vector2 b)
            {
                L.roads.Add((a, b));
                L.paints.Add(PaintShape.Segment(GroundTerrain.Gravel, a, b, 8.5f, 5f, 0.55f));
                L.paints.Add(PaintShape.Segment(GroundTerrain.Asphalt, a, b, 5.2f, 2.5f));
            }
            void Tee(Vector2 c, float yaw) => Add(tee, new Vector3(c.x, 0f, c.y), yaw); // yaw 0: through along Z, branch to -X

            const float Edge = 9800f;
            // A: pad → west to road C.
            Vector2 aEnd = Run(new Vector2(-35f, 0f), Vector2.left, 28);                    // (-595, 0)
            Paint(new Vector2(-30f, 0f), aEnd + Vector2.left * 5f);
            Tee(new Vector2(-600f, 0f), 180f);
            // C: north–south through the complex, out over the hills both ways.
            Vector2 cn = Run(new Vector2(-600f, 5f), Vector2.up, 70);                       // (-600, 1405)
            Vector2 cs = Run(new Vector2(-600f, -5f), Vector2.down, 44);                    // (-600, -885)
            Tee(new Vector2(-600f, -890f), 180f);
            Vector2 cs2 = Run(new Vector2(-600f, -895f), Vector2.down, 25);                 // (-600, -1395)
            Paint(new Vector2(-600f, -Edge), new Vector2(-600f, Edge));
            // D: east–west south of the site.
            Run(new Vector2(-595f, -890f), Vector2.right, 29);                              // → (-15, -890)
            Tee(new Vector2(-10f, -890f), 90f);
            Run(new Vector2(-5f, -890f), Vector2.right, 66);                                // → (1315, -890)
            Run(new Vector2(-605f, -890f), Vector2.left, 40);                               // → (-1405, -890)
            Paint(new Vector2(-Edge, -890f), new Vector2(Edge, -890f));
            // B: from road D north to the pad, with the tank-farm spur.
            Run(new Vector2(-10f, -885f), Vector2.up, 20);                                  // → (-10, -485)
            Tee(new Vector2(-10f, -480f), 180f);
            Run(new Vector2(-10f, -475f), Vector2.up, 22);                                  // → (-10, -35)
            Paint(new Vector2(-10f, -890f), new Vector2(-10f, -30f));
            Vector2 spur = Run(new Vector2(-5f, -480f), Vector2.right, 19);                 // → (375, -480)
            Paint(new Vector2(-10f, -480f), spur);
            // Farm tracks (paint only): gravel lanes out to the fields.
            L.paints.Add(PaintShape.Segment(GroundTerrain.Gravel, new Vector2(1315f, -890f), new Vector2(2400f, 1900f), 3.5f, 3f, 0.8f));
            L.paints.Add(PaintShape.Segment(GroundTerrain.Gravel, new Vector2(-600f, 1405f), new Vector2(-2600f, 2300f), 3.5f, 3f, 0.8f));
            L.paints.Add(PaintShape.Segment(GroundTerrain.Gravel, new Vector2(-1405f, -890f), new Vector2(-2200f, -3100f), 3.5f, 3f, 0.8f));
            _ = cn; _ = cs; _ = cs2;
        }

        // ---- launch site ----------------------------------------------------------------------------

        static void Building(Layout L, string model, Vector2 at, float yaw)
        {
            L.buildings.Add(new BuildingSpot { model = model, position = at, yaw = yaw });
            L.blocked.Add((at, L.buildingRadius(model) + 4f));
        }

        static void PlaceSite(Layout L, Func<string, bool, float, string, PropKind> K)
        {
            void Hard(int layer, Vector2 c, Vector2 size, float angle = 0f)
            {
                L.paints.Add(PaintShape.Rectangle(layer, c, size, angle, 3f));
                L.blocked.Add((c, 0.5f * size.magnitude * 0.8f));
            }

            // Main complex north-west of the pad, along road A and road C (in the chase view's background).
            Hard(GroundTerrain.Asphalt, new Vector2(-430f, 30f), new Vector2(70f, 46f));
            Building(L, "Scenery_Hangar", new Vector2(-430f, 76f), 180f);
            Hard(GroundTerrain.Asphalt, new Vector2(-622f, 185f), new Vector2(34f, 150f));
            Building(L, "Scenery_Warehouse", new Vector2(-656f, 140f), 90f);
            Building(L, "Scenery_Warehouse", new Vector2(-656f, 232f), 90f);
            Hard(GroundTerrain.Asphalt, new Vector2(-520f, -26f), new Vector2(54f, 36f));
            Building(L, "Scenery_Office", new Vector2(-520f, -56f), 0f);
            Hard(GroundTerrain.Asphalt, new Vector2(-638f, -62f), new Vector2(62f, 44f));
            Building(L, "Scenery_Office", new Vector2(-680f, -62f), 90f);
            Building(L, "Scenery_WaterTower", new Vector2(-545f, 190f), 20f);
            // Tracking station west of road C.
            Hard(GroundTerrain.Gravel, new Vector2(-845f, -275f), new Vector2(90f, 80f), 15f);
            Building(L, "Scenery_RadarDish", new Vector2(-825f, -255f), YawTowards(new Vector2(-825f, -255f), Vector2.zero));
            Building(L, "Scenery_RadarDish", new Vector2(-868f, -300f), YawTowards(new Vector2(-868f, -300f), Vector2.zero));
            Building(L, "Scenery_Bunker", new Vector2(-880f, -240f), 105f);
            // Blockhouses watching the pad, and the range tower.
            Building(L, "Scenery_Bunker", new Vector2(-230f, -170f), YawTowards(new Vector2(-230f, -170f), Vector2.zero));
            Building(L, "Scenery_Bunker", new Vector2(185f, 225f), YawTowards(new Vector2(185f, 225f), Vector2.zero));
            Hard(GroundTerrain.Gravel, new Vector2(385f, 325f), new Vector2(30f, 30f));
            Building(L, "Scenery_ObsTower", new Vector2(385f, 325f), -40f);
            // Tank farm at the end of the spur road (south-east, behind the pad in the pad-side view).
            Hard(GroundTerrain.Gravel, new Vector2(480f, -475f), new Vector2(160f, 100f));
            for (int i = 0; i < 4; i++)
                Building(L, "Scenery_TankSphere", new Vector2(428f + 32f * i, -446f), 0f);
            for (int i = 0; i < 3; i++)
                Building(L, "Scenery_TankPair", new Vector2(432f + 44f * i, -502f), 0f);

            // Lamps: apron ring (arms toward the pad), along roads A and B, at the hardstands.
            PropKind lamp = K("Scenery_LampPost", true, 1800f, "");
            for (int i = 0; i < 12; i++)
            {
                float a = (15f + 30f * i) * Mathf.Deg2Rad;
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (ApronRadius + 12f);
                Add(lamp, new Vector3(p.x, 0f, p.y), YawTowards(p, Vector2.zero));
            }
            for (float x = -170f; x >= -570f; x -= 40f)
            {
                Add(lamp, new Vector3(x, 0f, -8.5f), 0f);
                Add(lamp, new Vector3(x + 20f, 0f, 8.5f), 180f);
            }
            for (float z = -170f; z >= -860f; z -= 40f)
            {
                Add(lamp, new Vector3(-18.5f, 0f, z), 90f);
                Add(lamp, new Vector3(-1.5f, 0f, z - 20f), -90f);
            }
            foreach (Vector2 p in new[] { new Vector2(-466f, 10f), new Vector2(-394f, 10f), new Vector2(-546f, -10f), new Vector2(-494f, -10f),
                                          new Vector2(405f, -430f), new Vector2(555f, -430f), new Vector2(405f, -520f), new Vector2(555f, -520f) })
                Add(lamp, new Vector3(p.x, 0f, p.y), 0f);
        }

        static void PlaceFences(Layout L, Random rng, Func<string, bool, float, string, PropKind> K)
        {
            PropKind fence = K("Scenery_Fence", false, 250f, "");
            PropKind bush = K("Scenery_Bush", false, 700f, "");
            void Line(Vector2 a, Vector2 b, bool outsideIsLeft)
            {
                Vector2 d = b - a;
                int n = Mathf.RoundToInt(d.magnitude / 10f);
                Vector2 dir = d.normalized;
                // Segment runs along its local X; outside faces local +Z.
                float yaw = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
                Vector2 normal = new Vector2(-dir.y, dir.x) * (outsideIsLeft ? 1f : -1f);
                if (!outsideIsLeft) yaw += 180f;
                for (int i = 0; i < n; i++)
                {
                    Vector2 c = a + dir * (5f + 10f * i);
                    if (L.RoadDistance(c) < 14f) continue; // gate
                    Add(fence, new Vector3(c.x, 0f, c.y), yaw);
                    if (rng.NextDouble() < 0.22)
                    {
                        Vector2 bp = c + normal * Range(rng, -4f, 4f) + dir * Range(rng, -4f, 4f);
                        if (L.RoadDistance(bp) > 9f) Add(bush, new Vector3(bp.x, -0.1f, bp.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.5f));
                    }
                }
            }
            void Loop(Rect r)
            {
                // Counter-clockwise seen from above (x right, z up): outside is to the right of travel.
                Line(new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), false);
                Line(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), false);
                Line(new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), false);
                Line(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), false);
            }
            Loop(Rect.MinMaxRect(-FenceHalf, -FenceHalf, FenceHalf, FenceHalf));
            Loop(SiteRect);
        }

        // ---- nature ---------------------------------------------------------------------------------

        const float FarTreeCull = 3500f;
        const float FarTreeCullPerMetre = 2.5f;
        const float TreeThinDistance = 900f;

        static readonly string[] Conifers = { "Scenery_Conifer_A", "Scenery_Conifer_B" };
        static readonly string[] Broadleaves = { "Scenery_Broadleaf_A", "Scenery_Broadleaf_B" };

        static PropKind TreeKind(Func<string, bool, float, string, PropKind> K, string model, Vector2 p)
        {
            bool near = p.magnitude < TreeShadowRadius;
            if (near)
            {
                PropKind k = K(model, true, 0f, "");
                k.thin = TreeThinDistance;
                return k;
            }
            // Far trees: from low down only the nearer ones are drawn (beyond that a tree is a few pixels and the dark
            // forest floor reads as forest); the higher the camera, the farther they show.
            PropKind far = K(model, false, FarTreeCull, "_far");
            far.cullPerMetre = FarTreeCullPerMetre;
            far.thin = TreeThinDistance;
            return far;
        }

        static void Tree(Func<string, bool, float, string, PropKind> K, TerrainShape shape, Random rng, Vector2 p, float coniferShare)
        {
            bool conifer = rng.NextDouble() < coniferShare;
            string[] set = conifer ? Conifers : Broadleaves;
            string model = set[rng.NextDouble() < 0.6 ? 0 : 1];
            Add(TreeKind(K, model, p), OnGround(shape, p, 0.4f), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.3f));
        }

        /// <summary>Forests from 1.1 km out to the ridges: each one a cluster of overlapping lobes on a dark
        /// forest-floor paint (the canopy tone that makes a forest read as one dark mass from 3 km), filled with trees
        /// (denser and smaller near the site, sparser and bigger far out, where single trees are below a pixel).</summary>
        static void PlaceForests(Layout L, TerrainShape shape, Random rng, Func<string, bool, float, string, PropKind> K)
        {
            int placed = 0, tries = 0, trees = 0;
            while (placed < 30 && tries++ < 4000)
            {
                float ang = Range(rng, 0f, Mathf.PI * 2f);
                float dist = Mathf.Lerp(1150f, 7600f, Mathf.Pow((float)rng.NextDouble(), 0.8f));
                var c = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                if (Mathf.Abs(c.x) > 8500f || Mathf.Abs(c.y) > 8500f) continue;
                bool near = dist < 2600f;
                float radius = Range(rng, 70f, near ? 150f : 240f);
                // Lobes: the main disc plus 1-4 offset discs, so the outline is irregular instead of round.
                var lobes = new List<(Vector2 c, float r)> { (c, radius) };
                int extra = rng.Next(1, near ? 3 : 5);
                for (int i = 0; i < extra; i++)
                {
                    float la = Range(rng, 0f, Mathf.PI * 2f);
                    float lr = radius * Range(rng, 0.45f, 0.85f);
                    lobes.Add((c + new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * (radius * Range(rng, 0.6f, 1.1f)), lr));
                }
                float reach = 0f;
                foreach ((Vector2 lc, float lr) in lobes) reach = Mathf.Max(reach, Vector2.Distance(lc, c) + lr);
                if (Expand(SiteRect, reach + 120f).Contains(c) || c.magnitude - reach < 1000f) continue;
                bool clash = false;
                foreach ((Vector2 fc, float fr) in L.forests) if (Vector2.Distance(fc, c) < fr + reach + 90f) { clash = true; break; }
                if (clash || NearRoadLines(c, reach + 25f)) continue;
                L.forests.Add((c, reach));

                // Far forests are mostly conifers: the cheapest tree meshes, and darker en masse.
                float coniferShare = dist > 4500f ? 0.9f : near ? Range(rng, 0.2f, 0.9f) : Range(rng, 0.7f, 0.95f);
                float spacing = near ? 40f : 75f; // m² per tree
                float scaleMin = near ? 0.85f : 1.25f, scaleMax = near ? 1.35f : 1.9f;
                float seedX = Range(rng, 0f, 100f), seedZ = Range(rng, 0f, 100f);
                for (int li = 0; li < lobes.Count; li++)
                {
                    (Vector2 lc, float lr) = lobes[li];
                    L.paints.Add(PaintShape.Circle(GroundTerrain.Forest, lc, lr + 4f, 22f, 26f));
                    int count = Mathf.RoundToInt(Mathf.PI * lr * lr / spacing);
                    for (int i = 0; i < count; i++)
                    {
                        float a = Range(rng, 0f, Mathf.PI * 2f);
                        var p = lc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * lr * Mathf.Sqrt((float)rng.NextDouble());
                        // Ragged edge; skip spots already covered by an earlier lobe (no double density in overlaps).
                        float edge = lr * (0.8f + 0.35f * Mathf.PerlinNoise(seedX + p.x / 70f, seedZ + p.y / 70f));
                        if (Vector2.Distance(p, lc) > edge) continue;
                        bool covered = false;
                        for (int lj = 0; lj < li; lj++) if (Vector2.Distance(p, lobes[lj].c) < lobes[lj].r * 0.9f) { covered = true; break; }
                        if (covered) continue;
                        bool conifer = rng.NextDouble() < coniferShare;
                        string model = (conifer ? Conifers : Broadleaves)[rng.NextDouble() < 0.6 ? 0 : 1];
                        Add(TreeKind(K, model, p), OnGround(shape, p, 0.4f), Range(rng, 0f, 360f), Range(rng, scaleMin, scaleMax));
                        trees++;
                    }
                }
                placed++;
            }
            _ = trees;
        }

        // Road centrelines are known before BuildRoads runs (forests and fields are painted first).
        static readonly (Vector2 a, Vector2 b)[] RoadLines =
        {
            (new Vector2(-600f, -9800f), new Vector2(-600f, 9800f)),
            (new Vector2(-9800f, -890f), new Vector2(9800f, -890f)),
            (new Vector2(-600f, 0f), new Vector2(0f, 0f)),
            (new Vector2(-10f, -890f), new Vector2(-10f, 0f)),
            (new Vector2(-10f, -480f), new Vector2(380f, -480f)),
            (new Vector2(1315f, -890f), new Vector2(2400f, 1900f)),
            (new Vector2(-600f, 1405f), new Vector2(-2600f, 2300f)),
            (new Vector2(-1405f, -890f), new Vector2(-2200f, -3100f)),
        };

        static bool NearRoadLines(Vector2 p, float dist)
        {
            foreach ((Vector2 a, Vector2 b) in RoadLines)
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector2.Distance(p, a + ab * t) < dist) return true;
            }
            return false;
        }

        static float RectEdgeDistance(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, p.y - r.yMax);
            if (dx <= 0f && dz <= 0f) return -Mathf.Max(dx, dz); // inside: distance to the nearest edge
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dz, 0f)).magnitude;
        }

        static Rect Expand(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);

        /// <summary>
        /// Patchwork farmland on a slightly rotated grid: bands of varying depth split into fields of varying width
        /// (pasture, green crop, ripe grain, stubble, ploughed soil, young crop), separated by narrow grass margins,
        /// some with hedgerows. Colours stay close to the base ground so it reads as farmland, not pasted squares.
        /// </summary>
        static void PlaceFields(Layout L, TerrainShape shape, Random rng, Func<string, bool, float, string, PropKind> K)
        {
            const float angle = 9f, extent = 7700f;
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            PropKind bush = K("Scenery_Bush", false, 700f, "");
            int fields = 0;
            for (float gz = -extent; gz < extent;)
            {
                float ch = Range(rng, 110f, 240f);
                for (float gx = -extent; gx < extent;)
                {
                    float cw = Range(rng, 80f, 320f);
                    Vector3 w = rot * new Vector3(gx + cw * 0.5f, 0f, gz + ch * 0.5f);
                    var c = new Vector2(w.x, w.z);
                    float lx = gx, lw = cw;
                    gx += cw;
                    if (c.magnitude < 820f || Mathf.Abs(c.x) > 8400f || Mathf.Abs(c.y) > 8400f) continue;
                    if (Expand(SiteRect, 140f).Contains(c)) continue;
                    if (shape.SlopeDeg(c.x, c.y, 40f) > 10f) continue;
                    bool inForest = false;
                    foreach ((Vector2 fc, float fr) in L.forests) if (Vector2.Distance(fc, c) < fr + Mathf.Max(lw, ch) * 0.5f + 20f) { inForest = true; break; }
                    if (inForest || rng.NextDouble() > 0.8) continue;

                    var size = new Vector2(lw - Range(rng, 7f, 16f), ch - Range(rng, 7f, 16f));
                    double kind = rng.NextDouble();
                    if (kind < 0.2) L.paints.Add(PaintShape.Rectangle(GroundTerrain.Grass, c, size, angle, 9f, 2f));             // pasture
                    else if (kind < 0.36)
                    {
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Grass, c, size, angle, 9f, 2f));                         // green crop
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Forest, c, size, angle, 9f, 2f, Range(rng, 0.2f, 0.35f)));
                    }
                    else if (kind < 0.56) L.paints.Add(PaintShape.Rectangle(GroundTerrain.Meadow, c, size, angle, 9f, 2f, Range(rng, 0.8f, 1f))); // grain
                    else if (kind < 0.7)
                    {
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Meadow, c, size, angle, 9f, 2f, 0.75f));                 // stubble
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Soil, c, size, angle, 9f, 2f, 0.25f));
                    }
                    else if (kind < 0.86) L.paints.Add(PaintShape.Rectangle(GroundTerrain.Soil, c, size, angle, 9f, 2f, Range(rng, 0.75f, 0.92f))); // plough
                    else
                    {
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Soil, c, size, angle, 9f, 2f, 0.6f));                    // young crop
                        L.paints.Add(PaintShape.Rectangle(GroundTerrain.Grass, c, size, angle, 9f, 2f, 0.45f));
                    }
                    fields++;

                    // Hedgerow along the field's north or east edge: trees with bushes between.
                    if (rng.NextDouble() < 0.2 && c.magnitude < 6500f)
                    {
                        bool north = rng.NextDouble() < 0.5;
                        Vector3 e0 = rot * (north ? new Vector3(lx, 0f, gz + ch) : new Vector3(lx + lw, 0f, gz));
                        Vector3 e1 = rot * new Vector3(lx + lw, 0f, gz + ch);
                        float len = north ? lw : ch;
                        var a = new Vector2(e0.x, e0.z);
                        var b = new Vector2(e1.x, e1.z);
                        L.paints.Add(PaintShape.Segment(GroundTerrain.Forest, a, b, 3f, 6f, 0.6f));
                        for (float t = 0f; t <= 1f; t += Range(rng, 6f, 11f) / len)
                        {
                            Vector2 p = Vector2.Lerp(a, b, t) + new Vector2(Range(rng, -2f, 2f), Range(rng, -2f, 2f));
                            if (NearRoadLines(p, 12f)) continue;
                            if (rng.NextDouble() < 0.7) Tree(K, shape, rng, p, 0.15f);
                            else Add(bush, OnGround(shape, p, 0.2f), Range(rng, 0f, 360f), Range(rng, 1.2f, 2f));
                        }
                    }
                }
                gz += ch;
            }
            _ = fields;
        }

        /// <summary>Rough ground around the site (inside the field ring): irregular patches of dry grass, dark scrub,
        /// bare earth and lush grass at several sizes, plus a few dirt tracks, so the flat zone is not one green sheet
        /// at 300-1000 m.</summary>
        static void PlaceScrub(Layout L, Random rng)
        {
            Rect area = Expand(SiteRect, 320f);
            for (int i = 0; i < 420; i++)
            {
                var c = new Vector2(Range(rng, area.xMin, area.xMax), Range(rng, area.yMin, area.yMax));
                float r = Mathf.Lerp(12f, 90f, Mathf.Pow((float)rng.NextDouble(), 1.6f));
                if (c.magnitude < ApronRadius + r + 15f) continue;
                double kind = rng.NextDouble();
                int layer = kind < 0.42 ? GroundTerrain.Meadow : kind < 0.68 ? GroundTerrain.Forest : kind < 0.86 ? GroundTerrain.Soil : GroundTerrain.Grass;
                float strength = layer == GroundTerrain.Meadow ? Range(rng, 0.4f, 0.75f)
                    : layer == GroundTerrain.Forest ? Range(rng, 0.2f, 0.4f)
                    : layer == GroundTerrain.Soil ? Range(rng, 0.2f, 0.45f) : Range(rng, 0.4f, 0.7f);
                L.paints.Add(PaintShape.Circle(layer, c, r, r * 0.5f, r * 0.6f, strength));
            }
            // Dirt tracks between the site and the fields.
            for (int i = 0; i < 7; i++)
            {
                var a = new Vector2(Range(rng, SiteRect.xMin, SiteRect.xMax), Range(rng, SiteRect.yMin, SiteRect.yMax));
                if (a.magnitude < 400f) continue;
                float ang = Range(rng, 0f, Mathf.PI * 2f);
                Vector2 b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Range(rng, 250f, 700f);
                if (b.magnitude < 400f) continue;
                L.paints.Add(PaintShape.Segment(GroundTerrain.Soil, a, b, 2f, 3f, 0.55f));
            }
        }

        /// <summary>Groves (about 15–200 trees) between the pad fence and the site edge, for scale at 300 m and 50 m.</summary>
        static void PlaceGroves(Layout L, TerrainShape shape, Random rng, Func<string, bool, float, string, PropKind> K)
        {
            int placed = 0, tries = 0;
            while (placed < 24 && tries++ < 3000)
            {
                float ang = Range(rng, 0f, Mathf.PI * 2f);
                var c = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Range(rng, 380f, 1500f);
                float radius = Range(rng, 22f, 55f);
                if (Mathf.Abs(c.x) < FenceHalf + radius + 20f && Mathf.Abs(c.y) < FenceHalf + radius + 20f) continue;
                if (!L.Free(c, radius + 8f)) continue;
                if (RectEdgeDistance(SiteRect, c) < radius + 12f) continue; // keep clear of the site fence
                int count = Mathf.RoundToInt(Mathf.PI * radius * radius / Range(rng, 45f, 90f));
                float coniferShare = Range(rng, 0f, 1f);
                for (int i = 0; i < count; i++)
                {
                    float a = Range(rng, 0f, Mathf.PI * 2f);
                    var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * Mathf.Sqrt((float)rng.NextDouble());
                    if (!L.Free(p, 3f)) continue;
                    Tree(K, shape, rng, p, coniferShare);
                }
                L.blocked.Add((c, radius));
                L.paints.Add(PaintShape.Circle(GroundTerrain.Forest, c, radius + 4f, 14f, 10f, 0.55f));
                placed++;
            }
        }

        static void PlaceSmallProps(Layout L, TerrainShape shape, Random rng, Func<string, bool, float, string, PropKind> K)
        {
            PropKind bush = K("Scenery_Bush", false, 700f, "");
            PropKind rocks = K("Scenery_Rocks", false, 1200f, "");
            // Apron edge: rocks and scrub where the gravel meets the grass.
            for (int i = 0; i < 70; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, ApronRadius + 4f, ApronRadius + 50f);
                if (!L.Free(p, 3f)) continue;
                if (rng.NextDouble() < 0.4) Add(rocks, new Vector3(p.x, -0.15f, p.y), Range(rng, 0f, 360f), Range(rng, 0.6f, 1.4f));
                else Add(bush, new Vector3(p.x, -0.1f, p.y), Range(rng, 0f, 360f), Range(rng, 0.8f, 1.6f));
            }
            // Along the road edges in the flat zone.
            foreach ((Vector2 a, Vector2 b) in L.roads)
            {
                float len = Vector2.Distance(a, b);
                Vector2 dir = (b - a) / len, n = new Vector2(-dir.y, dir.x);
                for (float s = 0f; s < len; s += Range(rng, 15f, 40f))
                {
                    Vector2 p = a + dir * s + n * (rng.NextDouble() < 0.5 ? -1f : 1f) * Range(rng, 11f, 18f);
                    if (p.magnitude > 1600f || !L.Free(p, 1.5f)) continue;
                    Add(bush, new Vector3(p.x, -0.1f, p.y), Range(rng, 0f, 360f), Range(rng, 0.9f, 1.8f));
                }
            }
            // Scattered over the grass, and rock fields on the steeper hills.
            for (int i = 0; i < 600; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, 200f, 2400f);
                if (!L.Free(p, 2f)) continue;
                if (rng.NextDouble() < 0.3) Add(rocks, OnGround(shape, p, 0.2f), Range(rng, 0f, 360f), Range(rng, 0.7f, 1.8f));
                else Add(bush, OnGround(shape, p, 0.15f), Range(rng, 0f, 360f), Range(rng, 0.9f, 2.2f));
            }
            for (int i = 0; i < 2500; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Range(rng, 2000f, 7000f);
                if (shape.SlopeDeg(p.x, p.y) < 13f) continue;
                Add(rocks, OnGround(shape, p, 0.3f), Range(rng, 0f, 360f), Range(rng, 1.5f, 4f));
            }
        }
    }
}
