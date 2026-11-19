using System;
using System.Collections.Generic;
using System.Diagnostics;
using RocketSlam.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace RocketSlam.World
{
    /// <summary>
    /// Generates the ground at load time: a grid of Unity terrains (pad at the world origin) from the analytic
    /// <see cref="TerrainShape"/>, splat-painted from a noise base (grass / dry meadow, scree on steep slopes)
    /// plus the scene builder's paint list (gravel apron, roads, hardstands, fields, forest floors), and a flat
    /// far-ground ring out to the fog beyond the last tile. Nothing large is stored in the scene or the repo;
    /// the result is deterministic. Terrain colliders are plain ground (<see cref="LandingSurface"/> isPad false).
    /// Layers: 0 grass, 1 dry meadow, 2 gravel, 3 asphalt, 4 forest floor (dark canopy tone), 5 bare soil.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GroundTerrain : MonoBehaviour
    {
        public const int Grass = 0, Meadow = 1, Gravel = 2, Asphalt = 3, Forest = 4, Soil = 5;

        [SerializeField] TerrainShape shape = new TerrainShape();
        [SerializeField] TerrainLayer[] layers = new TerrainLayer[6];
        [SerializeField] Material terrainMaterial;
        [SerializeField] Material farGroundMaterial;
        [SerializeField] List<PaintShape> paints = new List<PaintShape>();
        [Tooltip("When set, the paint shapes come from the generated launch-site layout (not serialised in the scene).")]
        [SerializeField] LaunchSite site;
        [Header("Grid")]
        [SerializeField] int tilesPerSide = 5;
        [SerializeField] float tileSize = 4000f;
        [SerializeField] float maxHeight = 800f;
        [SerializeField] int heightmapResolution = 257;
        [Tooltip("Splat resolution of the centre tile, its 8 neighbours and the rest.")]
        [SerializeField] int centreAlphaResolution = 1024;
        [SerializeField] int ringAlphaResolution = 256;
        [SerializeField] int outerAlphaResolution = 64;
        [Header("Rendering")]
        [SerializeField] float pixelError = 6f;
        [SerializeField] float basemapDistance = 1400f;
        [SerializeField] float farGroundExtent = 40000f;

        readonly List<Terrain> tiles = new List<Terrain>();

        public TerrainShape Shape => shape;
        public IReadOnlyList<Terrain> Tiles => tiles;
        public float Extent => tilesPerSide * tileSize * 0.5f;
        /// <summary>Time the last generation took, ms.</summary>
        public long GenerationMs { get; private set; }

        public void Setup(TerrainShape s, TerrainLayer[] terrainLayers, Material terrain, Material farGround, LaunchSite launchSite)
        {
            site = launchSite;
            paints = new List<PaintShape>();
            shape = s;
            layers = terrainLayers;
            terrainMaterial = terrain;
            farGroundMaterial = farGround;
        }

        void Awake() => Generate();

        /// <summary>Heights and splat weights of one tile, computed off the main thread.</summary>
        class TileData
        {
            public Vector3 origin;
            public int ring, alphaRes;
            public float[,] heights;
            /// <summary>Splat weights, row-major [z, x, layer] (flat: multi-dimensional arrays are slow in Mono).</summary>
            public float[] alpha;
        }

        public void Generate()
        {
            var sw = Stopwatch.StartNew();
            if (site != null) paints = site.Layout.paints;
            foreach (Transform child in transform) Destroy(child.gameObject);
            tiles.Clear();

            float half = Extent;
            int n = tilesPerSide;
            var data = new TileData[n * n];
            for (int tz = 0; tz < n; tz++)
            for (int tx = 0; tx < n; tx++)
            {
                int ring = Mathf.Max(Mathf.Abs(tx - n / 2), Mathf.Abs(tz - n / 2));
                data[tz * n + tx] = new TileData
                {
                    origin = new Vector3(-half + tx * tileSize, 0f, -half + tz * tileSize),
                    ring = ring,
                    alphaRes = ring == 0 ? centreAlphaResolution : ring == 1 ? ringAlphaResolution : outerAlphaResolution,
                };
            }
            // Pure maths (analytic heights, noise, paint shapes): spread over the worker threads. Painting runs in
            // small row bands, biggest tiles first, so the work balances across the cores.
            System.Threading.Tasks.Parallel.For(0, data.Length, i => data[i].heights = Heights(data[i].origin));
            long heightsMs = sw.ElapsedMilliseconds;
            foreach (TileData d in data) d.alpha = new float[d.alphaRes * d.alphaRes * layers.Length];
            var bands = new List<(TileData d, int z0, int z1)>();
            foreach (TileData d in data)
            {
                int bandRows = Mathf.Max(8, 16384 / d.alphaRes);
                for (int z = 0; z < d.alphaRes; z += bandRows) bands.Add((d, z, Mathf.Min(d.alphaRes, z + bandRows)));
            }
            bands.Sort((p, q) => q.d.alphaRes.CompareTo(p.d.alphaRes));
            System.Threading.Tasks.Parallel.For(0, bands.Count, i => Paint(bands[i].d, bands[i].z0, bands[i].z1));
            long computeMs = sw.ElapsedMilliseconds;

            var grid = new Terrain[n, n];
            for (int tz = 0; tz < n; tz++)
            for (int tx = 0; tx < n; tx++)
            {
                grid[tx, tz] = BuildTile($"Terrain_{tx}_{tz}", data[tz * n + tx]);
                tiles.Add(grid[tx, tz]);
            }
            for (int tz = 0; tz < n; tz++)
            for (int tx = 0; tx < n; tx++)
            {
                grid[tx, tz].SetNeighbors(
                    tx > 0 ? grid[tx - 1, tz] : null, tz < n - 1 ? grid[tx, tz + 1] : null,
                    tx < n - 1 ? grid[tx + 1, tz] : null, tz > 0 ? grid[tx, tz - 1] : null);
            }
            BuildFarGround(half);
            GenerationMs = sw.ElapsedMilliseconds;
            Debug.Log($"[GroundTerrain] {n * n} tiles generated in {GenerationMs} ms (heights {heightsMs} ms + paint {computeMs - heightsMs} ms on worker threads)");
        }

        float[,] Heights(Vector3 origin)
        {
            int hr = heightmapResolution;
            var heights = new float[hr, hr];
            float step = tileSize / (hr - 1);
            for (int z = 0; z < hr; z++)
            for (int x = 0; x < hr; x++)
                heights[z, x] = shape.Height(origin.x + x * step, origin.z + z * step) / maxHeight;
            return heights;
        }

        Terrain BuildTile(string tileName, TileData d)
        {
            var td = new TerrainData { name = tileName };
            td.heightmapResolution = heightmapResolution;
            td.size = new Vector3(tileSize, maxHeight, tileSize);
            td.alphamapResolution = d.alphaRes;
            td.baseMapResolution = Mathf.Clamp(d.alphaRes, 128, 1024);
            td.terrainLayers = layers;
            td.SetHeights(0, 0, d.heights);
            var alpha = new float[d.alphaRes, d.alphaRes, layers.Length];
            Buffer.BlockCopy(d.alpha, 0, alpha, 0, d.alpha.Length * sizeof(float));
            td.SetAlphamaps(0, 0, alpha);

            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = tileName;
            go.transform.SetParent(transform, false);
            go.transform.position = d.origin;
            go.isStatic = true;
            go.AddComponent<LandingSurface>().isPad = false;

            Terrain t = go.GetComponent<Terrain>();
            t.materialTemplate = terrainMaterial;
            t.heightmapPixelError = d.ring == 0 ? pixelError : pixelError * 1.5f;
            t.basemapDistance = basemapDistance;
            // Not instanced: runtime-created instanced terrain renders without its per-pixel normal map (black ground).
            t.drawInstanced = false;
            t.shadowCastingMode = ShadowCastingMode.Off;
            t.drawTreesAndFoliage = false;
            t.allowAutoConnect = false;
            t.groupingID = 1;
            t.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return t;
        }

        /// <summary>Splat weights for alphamap rows z0..z1-1 of one tile (thread-safe: reads only shared inputs).</summary>
        void Paint(TileData d, int z0, int z1)
        {
            Vector3 origin = d.origin;
            int res = d.alphaRes, hr = heightmapResolution, nl = layers.Length;
            float[,] heights = d.heights;
            float[] a = d.alpha;
            float cell = tileSize / (hr - 1);
            float texel = tileSize / res;
            bool slopes = d.ring >= 1;
            for (int z = z0; z < z1; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = origin.x + (x + 0.5f) * texel, wz = origin.z + (z + 0.5f) * texel;
                // Large-scale colour variation (lush grass vs dry meadow at two scales) plus fine mottling, kept
                // low-contrast so the painted fields and forests carry the pattern.
                float m = 0.6f * Mathf.PerlinNoise(wx / 1100f + 51.7f, wz / 1100f + 12.3f)
                          + 0.4f * Mathf.PerlinNoise(wx / 260f + 3.1f, wz / 260f + 77.2f);
                float meadow = TerrainShape.Smooth(0.42f, 0.72f, m) * 0.6f;
                meadow = Mathf.Clamp01(meadow + 0.3f * (Mathf.PerlinNoise(wx / 47f + 9.1f, wz / 47f + 4.4f) - 0.5f));
                float rock = 0f;
                if (slopes)
                {
                    // Slope and height from the heightmap just generated (cheap; the analytic shape is slow here).
                    float gx = (wx - origin.x) / cell, gz = (wz - origin.z) / cell;
                    int ix = Mathf.Min(hr - 2, (int)gx), iz = Mathf.Min(hr - 2, (int)gz);
                    float h00 = heights[iz, ix], h10 = heights[iz, ix + 1], h01 = heights[iz + 1, ix], h11 = heights[iz + 1, ix + 1];
                    float dx = 0.5f * (h10 - h00 + h11 - h01) * maxHeight / cell;
                    float dz = 0.5f * (h01 - h00 + h11 - h10) * maxHeight / cell;
                    float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
                    // Scree only on the steepest flanks, never a full cover (reads as rocky grass, not snow).
                    rock = TerrainShape.Smooth(22f, 36f, slope) * 0.6f;
                    // Higher ground is drier.
                    float h = 0.25f * (h00 + h10 + h01 + h11) * maxHeight;
                    meadow = Mathf.Max(meadow, TerrainShape.Smooth(180f, 420f, h) * 0.7f);
                }
                int i = (z * res + x) * nl;
                a[i + Grass] = (1f - meadow) * (1f - rock);
                a[i + Meadow] = meadow * (1f - rock);
                if (nl > Soil)
                {
                    a[i + Gravel] = rock * 0.4f;
                    a[i + Soil] = rock * 0.6f;
                }
                else a[i + Gravel] = rock;
            }

            var tileRect = new Rect(origin.x, origin.z, tileSize, tileSize);
            foreach (PaintShape p in paints)
            {
                if (p.layer >= nl) continue;
                Rect b = p.Bounds();
                if (!b.Overlaps(tileRect)) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - origin.x) / texel));
                int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((b.xMax - origin.x) / texel));
                int pz0 = Mathf.Max(z0, Mathf.FloorToInt((b.yMin - origin.z) / texel));
                int pz1 = Mathf.Min(z1 - 1, Mathf.CeilToInt((b.yMax - origin.z) / texel));
                for (int z = pz0; z <= pz1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float c = p.Coverage(origin.x + (x + 0.5f) * texel, origin.z + (z + 0.5f) * texel);
                    if (c <= 0f) continue;
                    int i = (z * res + x) * nl;
                    for (int l = 0; l < nl; l++) a[i + l] *= 1f - c;
                    a[i + p.layer] += c;
                }
            }
        }

        /// <summary>A flat ring of plain ground from the terrain edge out to the fog, so no edge or void shows.</summary>
        void BuildFarGround(float half)
        {
            if (farGroundMaterial == null) return;
            float e = farGroundExtent;
            var verts = new[]
            {
                new Vector3(-e, 0f, -e), new Vector3(e, 0f, -e), new Vector3(e, 0f, e), new Vector3(-e, 0f, e),
                new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half),
            };
            // Four trapezoids between the outer square (0..3) and the inner square (4..7), wound to face up.
            var tris = new[] { 0, 4, 1, 1, 4, 5, 1, 5, 2, 2, 5, 6, 2, 6, 3, 3, 6, 7, 3, 7, 0, 0, 7, 4 };
            var mesh = new Mesh { name = "FarGround", vertices = verts, triangles = tris };
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            var go = new GameObject("FarGround");
            go.transform.SetParent(transform, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = farGroundMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
