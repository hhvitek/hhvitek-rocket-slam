using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketSlam.World
{
    /// <summary>
    /// Draws the mass props (trees, bushes, rocks, fences, lamps, road tiles) with GPU instancing instead of thousands
    /// of GameObjects. The scene builder places them (seeded RNG) and stores one compact record per instance;
    /// at load they are bucketed into spatial chunks with world bounds, so whole chunks are frustum- and
    /// shadow-culled by Unity and small props are dropped by distance. No colliders (props are decoration).
    /// </summary>
    public class InstancedScenery : MonoBehaviour
    {
        [Serializable]
        public struct Item
        {
            public Vector3 p;
            /// <summary>x = yaw in degrees, y = uniform scale.</summary>
            public Vector2 rs;
        }

        [Serializable]
        public class Kind
        {
            public string name;
            public Mesh mesh;
            public Material material;
            [Tooltip("Mesh pose inside the model (FBX axis conversion), applied before the instance transform.")]
            public Matrix4x4 meshLocal = Matrix4x4.identity;
            public bool castShadows = true;
            [Tooltip("Chunks farther than this from the camera are not drawn (0 = never culled by distance), m.")]
            public float cullDistance;
            [Tooltip("Added to the cull distance per metre of camera height (high views see far props), m/m.")]
            public float cullPerMetreHeight;
            [Tooltip("Thinning (forests): full density up to this distance from the camera, then the drawn share falls " +
                     "as distance⁻¹ down to MinDensity (0 = never thinned), m.")]
            public float thinDistance;
            public List<Item> items = new List<Item>();
        }

        class Chunk
        {
            public Kind kind;
            public Matrix4x4[] matrices;
            public Bounds bounds;
            public RenderParams rp;
        }

        const float ChunkSize = 400f;
        /// <summary>Shadow casters use smaller chunks: a chunk only casts shadows while it is within the shadow
        /// distance (plus <see cref="ShadowMargin"/>), and smaller chunks fall out of the shadow cascades sooner.</summary>
        const float ShadowChunkSize = 150f;
        const float ShadowMargin = 60f;
        /// <summary>Props never cast shadows from farther than this (a tree's shadow at 1 km is a pixel or two), m.</summary>
        const float MaxCasterDistance = 1000f;
        /// <summary>Least share of a thinned chunk that is still drawn.</summary>
        const float MinDensity = 0.4f;
        const int MaxPerDraw = 1023;

        [SerializeField] List<Kind> kinds = new List<Kind>();
        [Tooltip("When set, the instances of each kind (matched by name) come from the generated launch-site layout at load.")]
        [SerializeField] LaunchSite site;

        readonly List<Chunk> chunks = new List<Chunk>();

        public IReadOnlyList<Kind> Kinds => kinds;
        public int InstanceCount { get; private set; }
        /// <summary>Instances submitted in the last frame (tests / benchmark log).</summary>
        public int DrawnLastFrame { get; private set; }

        public void Setup(List<Kind> k, LaunchSite launchSite = null)
        {
            kinds = k;
            site = launchSite;
        }

        /// <summary>Generation time of the layout at load (0 when cached / not generated), ms.</summary>
        public long LayoutMs { get; private set; }

        void Awake()
        {
            if (site != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LaunchSiteLayout.Result layout = site.Layout;
                LayoutMs = sw.ElapsedMilliseconds;
                foreach (Kind k in kinds)
                {
                    PropKind generated = layout.Find(k.name);
                    k.items = generated != null ? generated.items : new List<Item>();
                }
            }
            BuildChunks();
        }

        void BuildChunks()
        {
            chunks.Clear();
            InstanceCount = 0;
            foreach (Kind kind in kinds)
            {
                if (kind.mesh == null || kind.material == null || kind.items.Count == 0) continue;
                var buckets = new Dictionary<Vector2Int, List<Matrix4x4>>();
                Bounds local = kind.mesh.bounds;
                float size = kind.castShadows ? ShadowChunkSize : ChunkSize;
                foreach (Item it in kind.items)
                {
                    var key = new Vector2Int(Mathf.FloorToInt(it.p.x / size), Mathf.FloorToInt(it.p.z / size));
                    if (!buckets.TryGetValue(key, out List<Matrix4x4> list)) buckets[key] = list = new List<Matrix4x4>();
                    list.Add(Matrix4x4.TRS(it.p, Quaternion.Euler(0f, it.rs.x, 0f), Vector3.one * it.rs.y) * kind.meshLocal);
                }
                foreach (List<Matrix4x4> list in buckets.Values)
                {
                    // Random order inside a chunk: any prefix of it is an evenly spread subset (thinning draws a prefix).
                    if (kind.thinDistance > 0f) Shuffle(list, StableHash(kind.name) ^ list.Count);
                    for (int start = 0; start < list.Count; start += MaxPerDraw)
                    {
                        int count = Mathf.Min(MaxPerDraw, list.Count - start);
                        var m = list.GetRange(start, count).ToArray();
                        Bounds b = TransformBounds(m[0], local);
                        for (int i = 1; i < m.Length; i++) b.Encapsulate(TransformBounds(m[i], local));
                        chunks.Add(new Chunk
                        {
                            kind = kind,
                            matrices = m,
                            bounds = b,
                            rp = new RenderParams(kind.material)
                            {
                                worldBounds = b,
                                shadowCastingMode = kind.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                                receiveShadows = true,
                                layer = gameObject.layer,
                                lightProbeUsage = LightProbeUsage.BlendProbes,
                            },
                        });
                        InstanceCount += count;
                    }
                }
            }
        }

        static int StableHash(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                foreach (char ch in s) h = (h ^ ch) * 16777619;
                return h;
            }
        }

        static void Shuffle(List<Matrix4x4> list, int seed)
        {
            var rng = new System.Random(seed);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            Vector3 c = m.MultiplyPoint3x4(b.center);
            Vector3 e = b.extents;
            Vector3 ax = m.GetColumn(0) * e.x, ay = m.GetColumn(1) * e.y, az = m.GetColumn(2) * e.z;
            var ext = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, ext * 2f);
        }

        readonly Plane[] planes = new Plane[6];

        /// <summary>Benchmark experiments only (<c>-bench-props</c>): no prop shadows / scale every cull distance.</summary>
        public static bool DebugNoShadows;
        public static float DebugCullScale = 1f;
        public static string[] DebugHideKinds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            DebugNoShadows = false;
            DebugCullScale = 1f;
            DebugHideKinds = null;
        }

        static bool Hidden(Kind k)
        {
            if (DebugHideKinds == null) return false;
            foreach (string s in DebugHideKinds)
                if (k.name.Contains(s)) return true;
            return false;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            float shadowRange = Mathf.Min((urp != null ? urp.shadowDistance : 1000f) + ShadowMargin, MaxCasterDistance);
            float shadowRangeSq = DebugNoShadows ? -1f : shadowRange * shadowRange;
            int drawn = 0;
            foreach (Chunk c in chunks)
            {
                if (DebugHideKinds != null && Hidden(c.kind)) continue;
                if (c.kind.cullDistance > 0f)
                {
                    float limit = (c.kind.cullDistance + c.kind.cullPerMetreHeight * Mathf.Max(0f, eye.y)) * DebugCullScale;
                    if (c.bounds.SqrDistance(eye) > limit * limit) continue;
                }
                // Chunks outside the view are not submitted (saves the per-frame matrix upload; Unity would cull them
                // later anyway), unless they cast shadows into it: shadow casters within the shadow distance.
                // Casters beyond it are drawn without the shadow passes (their shadows would not be drawn anyway, but
                // their whole chunk would still be rendered into every cascade it overlaps).
                bool inView = cam == null || GeometryUtility.TestPlanesAABB(planes, c.bounds);
                float sqrDistance = c.bounds.SqrDistance(eye);
                bool casts = c.kind.castShadows && sqrDistance <= shadowRangeSq;
                if (!inView && !casts) continue;
                int count = c.matrices.Length;
                if (c.kind.thinDistance > 0f && sqrDistance > c.kind.thinDistance * c.kind.thinDistance)
                {
                    float share = Mathf.Max(MinDensity, c.kind.thinDistance / Mathf.Sqrt(sqrDistance));
                    count = Mathf.Max(1, Mathf.CeilToInt(count * share));
                }
                if (c.kind.castShadows && !casts)
                {
                    RenderParams rp = c.rp;
                    rp.shadowCastingMode = ShadowCastingMode.Off;
                    Graphics.RenderMeshInstanced(rp, c.kind.mesh, 0, c.matrices, count);
                }
                else Graphics.RenderMeshInstanced(c.rp, c.kind.mesh, 0, c.matrices, count);
                drawn += count;
            }
            DrawnLastFrame = drawn;
        }
    }
}
