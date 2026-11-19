using System.Runtime.InteropServices;
using RocketSlam.Rocket;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketSlam.World
{
    /// <summary>
    /// Low-poly sea for the droneship levels, flat shaded with the plain URP Lit material (every triangle has its own
    /// three vertices and one face normal; no custom shader).
    /// <para>Near patch: a square grid (default 100 × 100 cells of 7 m = 700 m) around the ship, displaced every
    /// frame on the CPU by the level's <see cref="WaveField"/> (the same function that moves the ship). Two Burst
    /// jobs (heights per grid point, then six vertices per cell) run between Update and LateUpdate. The waves fade
    /// out towards the patch edge (square fade, exactly 0 on the border).</para>
    /// <para>Far ring: static square rings, each 10 % larger than the last, out to 30 km (beyond the far clip, deep in
    /// the fog), sharing the patch's border vertices (no cracks), with a small random facet tilt so the distant sea
    /// keeps the same faceted glitter instead of turning into a mirror.</para>
    /// </summary>
    public class OceanSurface : MonoBehaviour
    {
        [SerializeField] SeaState sea;
        [SerializeField] Material material;
        [SerializeField] float patchHalf = 350f;
        [SerializeField] int cells = 100;
        [Tooltip("Fraction of the patch half-size where the wave fade starts.")]
        [SerializeField] float fadeStart = 0.45f;
        [SerializeField] float farRadius = 30000f;

        [StructLayout(LayoutKind.Sequential)]
        struct OceanVertex
        {
            public float3 position;
            public float3 normal;
        }

        Mesh nearMesh, farMesh;
        NativeArray<float> heights;
        NativeArray<OceanVertex> vertices;
        NativeArray<Wave> waveParams;
        JobHandle handle;
        bool scheduled;
        WaveField field;

        public WaveField Field => field ??= new WaveField(sea);
        public Mesh NearMesh => nearMesh;

        public void Setup(SeaState s, Material mat, float half = 350f, int cellCount = 100)
        {
            sea = s;
            material = mat;
            patchHalf = half;
            cells = cellCount;
            field = null;
        }

        void Awake()
        {
            Init();
            Evaluate(LevelClock.Render);
        }

        /// <summary>Allocate the job buffers and build the meshes (Awake; EditMode tests call it directly).</summary>
        public void Init()
        {
            if (heights.IsCreated) return;
            field = new WaveField(sea);
            int n1 = cells + 1;
            heights = new NativeArray<float>(n1 * n1, Allocator.Persistent);
            vertices = new NativeArray<OceanVertex>(cells * cells * 6, Allocator.Persistent);
            // The job evaluates exactly the WaveField's waves with Wave.Height: one formula for the ship and the sea.
            waveParams = new NativeArray<Wave>(field.Waves, Allocator.Persistent);

            nearMesh = BuildNearMesh();
            farMesh = BuildFarMesh();
            AddPart("OceanNear", nearMesh);
            AddPart("OceanFar", farMesh);
        }

        /// <summary>Displace the near patch for level time t right now (synchronously).</summary>
        public void Evaluate(float t)
        {
            Schedule(t);
            Complete();
        }

        void AddPart(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        };

        Mesh BuildNearMesh()
        {
            var mesh = new Mesh { name = "OceanNear" };
            int count = cells * cells * 6;
            mesh.SetVertexBufferParams(count, Layout);
            mesh.SetIndexBufferParams(count, IndexFormat.UInt32);
            var idx = new NativeArray<int>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) idx[i] = i;
            mesh.SetIndexBufferData(idx, 0, 0, count, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            idx.Dispose();
            mesh.subMeshCount = 1;
            float amp = Mathf.Max(1f, Field.MaxHeight);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, count) { bounds = new Bounds(Vector3.zero, new Vector3(2f * patchHalf, 2f * amp, 2f * patchHalf)) },
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f * patchHalf, 2f * amp, 2f * patchHalf));
            mesh.MarkDynamic();
            return mesh;
        }

        /// <summary>Static square rings from the patch border out to <see cref="farRadius"/>.</summary>
        Mesh BuildFarMesh()
        {
            int perim = 4 * cells;
            // Perimeter direction for vertex j on a unit square (half-size 1), counter-clockwise from (-1, -1).
            var unit = new Vector2[perim];
            for (int j = 0; j < perim; j++)
            {
                int side = j / cells;
                float f = (float)(j % cells) / cells * 2f - 1f;
                unit[j] = side == 0 ? new Vector2(f, -1f) : side == 1 ? new Vector2(1f, f) : side == 2 ? new Vector2(-f, 1f) : new Vector2(-1f, -f);
            }
            var rings = new System.Collections.Generic.List<float> { patchHalf };
            while (rings[rings.Count - 1] < farRadius) rings.Add(rings[rings.Count - 1] * 1.1f);

            // Flat geometry (a dead-flat horizon), but each quad gets a slightly tilted normal from a smooth pattern in
            // "ring space" (features scale with the distance, so they keep about the same size on screen): the distant
            // sea keeps a soft faceted glitter. One normal per quad (both triangles) avoids sliver streaks.
            float ph = sea.seed * 0.7f;
            int quads = (rings.Count - 1) * perim;
            var pos = new Vector3[quads * 6];
            var nrm = new Vector3[quads * 6];
            var tri = new int[quads * 6];
            int v = 0;
            for (int k = 0; k < rings.Count - 1; k++)
            {
                float tilt = 0.06f * Mathf.Clamp01(k / 5f);
                for (int j = 0; j < perim; j++)
                {
                    int j1 = (j + 1) % perim;
                    float ang = 2f * Mathf.PI * (j + 0.5f) / perim; // whole numbers of waves around the ring: no seam
                    float tx = 0.6f * Mathf.Sin(29f * ang + 0.9f * k + ph) + 0.4f * Mathf.Sin(53f * ang - 1.3f * k + 2f * ph);
                    float tz = 0.6f * Mathf.Sin(37f * ang - 0.7f * k + 3f * ph) + 0.4f * Mathf.Sin(61f * ang + 1.1f * k + ph);
                    Vector3 n = new Vector3(tilt * tx, 1f, tilt * tz).normalized;
                    Vector3 a = P(unit[j], rings[k]);
                    Vector3 b = P(unit[j1], rings[k]);
                    Vector3 c = P(unit[j1], rings[k + 1]);
                    Vector3 d = P(unit[j], rings[k + 1]);
                    Tri(a, d, c, n);
                    Tri(a, c, b, n);
                }
            }
            void Tri(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n)
            {
                if (Vector3.Cross(p1 - p0, p2 - p0).y < 0f) (p1, p2) = (p2, p1); // clockwise from above = facing up
                pos[v] = p0; nrm[v] = n; tri[v] = v; v++;
                pos[v] = p1; nrm[v] = n; tri[v] = v; v++;
                pos[v] = p2; nrm[v] = n; tri[v] = v; v++;
            }
            var mesh = new Mesh { name = "OceanFar", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.triangles = tri;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        static Vector3 P(Vector2 unit, float half) => new Vector3(unit.x * half, 0f, unit.y * half);

        // Same clock as the interpolated droneship (see LevelClock.Render), so the hull sits on the waves drawn.
        void Update() => Schedule(LevelClock.Render);

        void LateUpdate() => Complete();

        void Schedule(float t)
        {
            if (scheduled) Complete();
            Vector3 o = transform.position;
            var hj = new HeightJob
            {
                waves = waveParams,
                n1 = cells + 1,
                cell = 2f * patchHalf / cells,
                half = patchHalf,
                fadeStart = fadeStart,
                t = t,
                origin = new float2(o.x, o.z),
                heights = heights,
            };
            var vj = new VertexJob
            {
                heights = heights,
                cells = cells,
                cell = 2f * patchHalf / cells,
                half = patchHalf,
                vertices = vertices,
            };
            handle = vj.Schedule(cells * cells, 64, hj.Schedule((cells + 1) * (cells + 1), 128));
            scheduled = true;
        }

        void Complete()
        {
            if (!scheduled) return;
            handle.Complete();
            scheduled = false;
            nearMesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers);
        }

        void OnDestroy() => Release();

        /// <summary>Free the job buffers and meshes (OnDestroy; EditMode tests call it directly).</summary>
        public void Release()
        {
            handle.Complete();
            scheduled = false;
            if (heights.IsCreated) heights.Dispose();
            if (vertices.IsCreated) vertices.Dispose();
            if (waveParams.IsCreated) waveParams.Dispose();
            if (nearMesh != null) DestroyAsset(nearMesh);
            if (farMesh != null) DestroyAsset(farMesh);
            nearMesh = farMesh = null;
        }

        static void DestroyAsset(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        /// <summary>Surface height of the near patch at a grid point (tests): faded wave height.</summary>
        public float PatchHeight(int ix, int iz) => heights[iz * (cells + 1) + ix];

        public int Cells => cells;

        /// <summary>World (x, z) of a near-patch grid point (y = 0).</summary>
        public Vector3 PatchPoint(int ix, int iz)
        {
            float cell = 2f * patchHalf / cells;
            Vector3 o = transform.position;
            return new Vector3(o.x - patchHalf + ix * cell, 0f, o.z - patchHalf + iz * cell);
        }

        /// <summary>Wave fade factor at a grid point (1 in the middle of the patch, 0 on its border).</summary>
        public float PatchFade(int ix, int iz)
        {
            float cell = 2f * patchHalf / cells;
            float x = -patchHalf + ix * cell, z = -patchHalf + iz * cell;
            float edge = Mathf.Sqrt(x * x + z * z) / patchHalf;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, 1f, edge));
        }

        [BurstCompile]
        struct HeightJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Wave> waves;
            public int n1;
            public float cell, half, fadeStart, t;
            public float2 origin;
            [WriteOnly] public NativeArray<float> heights;

            public void Execute(int i)
            {
                int ix = i % n1, iz = i / n1;
                float x = -half + ix * cell, z = -half + iz * cell;
                float wx = origin.x + x, wz = origin.y + z;
                float h = 0f;
                for (int w = 0; w < waves.Length; w++)
                    h += waves[w].Height(wx, wz, t);
                float edge = math.sqrt(x * x + z * z) / half; // round fade; 0 on (and beyond) the square border
                float f = 1f - math.smoothstep(fadeStart, 1f, edge);
                heights[i] = h * f;
            }
        }

        [BurstCompile]
        struct VertexJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> heights;
            public int cells;
            public float cell, half;
            [NativeDisableParallelForRestriction, WriteOnly] public NativeArray<OceanVertex> vertices;

            float3 Point(int ix, int iz) => new float3(-half + ix * cell, heights[iz * (cells + 1) + ix], -half + iz * cell);

            public void Execute(int q)
            {
                int ix = q % cells, iz = q / cells;
                float3 p00 = Point(ix, iz), p10 = Point(ix + 1, iz), p01 = Point(ix, iz + 1), p11 = Point(ix + 1, iz + 1);
                int v = q * 6;
                // Alternate the diagonal (checkerboard) for the classic low-poly sea pattern. All clockwise from above.
                if (((ix + iz) & 1) == 0)
                {
                    Tri(v, p00, p01, p11);
                    Tri(v + 3, p00, p11, p10);
                }
                else
                {
                    Tri(v, p00, p01, p10);
                    Tri(v + 3, p01, p11, p10);
                }
            }

            void Tri(int v, float3 a, float3 b, float3 c)
            {
                float3 n = math.normalize(math.cross(b - a, c - a));
                vertices[v] = new OceanVertex { position = a, normal = n };
                vertices[v + 1] = new OceanVertex { position = b, normal = n };
                vertices[v + 2] = new OceanVertex { position = c, normal = n };
            }
        }
    }
}
