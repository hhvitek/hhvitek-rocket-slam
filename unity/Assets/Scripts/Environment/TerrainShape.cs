using System;
using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>
    /// Analytic ground height around the pad (pad centre = world origin). Perfectly flat (y 0) inside
    /// <see cref="flatRadius"/>, gentle noise hills beyond it, a ring of higher ridges far out that fades back to 0
    /// before the terrain edge. Pure function: the runtime terrain generator and the editor scenery placement use
    /// the same numbers, so props sit on the generated ground.
    /// </summary>
    [Serializable]
    public class TerrainShape
    {
        [Tooltip("Flat (y 0) inside this radius around the pad, m.")]
        public float flatRadius = 1700f;
        [Tooltip("Hills ramp from 0 to full height over this distance beyond flatRadius, m.")]
        public float hillRamp = 1600f;
        public float hillHeight = 120f;
        [Tooltip("Hill noise wavelength, m.")]
        public float hillScale = 1500f;
        [Tooltip("Ridge ring: starts rising at ridgeStart, full at ridgeFull (radial, m).")]
        public float ridgeStart = 5200f;
        public float ridgeFull = 7600f;
        public float ridgeHeight = 520f;
        public float ridgeScale = 2400f;
        [Tooltip("Everything fades back to y 0 between these square distances (max |x|,|z|) from the pad, m.")]
        public float edgeFadeStart = 8600f;
        public float edgeFadeEnd = 9800f;
        public Vector2 noiseOffset = new Vector2(371.3f, 917.9f);

        public float Height(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r <= flatRadius) return 0f;
            float edge = 1f - Smooth(edgeFadeStart, edgeFadeEnd, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
            if (edge <= 0f) return 0f;

            float hillMask = Smooth(flatRadius, flatRadius + hillRamp, r);
            float n = Fbm(x / hillScale, z / hillScale, 3);           // ~0..1
            float hills = hillHeight * hillMask * Mathf.Max(0f, n - 0.28f) / 0.72f;
            // Long gentle undulation everywhere outside the flat zone (fields roll a little).
            hills += 12f * hillMask * Mathf.PerlinNoise(noiseOffset.x + x / 600f, noiseOffset.y + z / 600f);

            float ridgeMask = Smooth(ridgeStart, ridgeFull, r);
            float ridge = 0f;
            if (ridgeMask > 0f)
            {
                float rn = 1f - Mathf.Abs(2f * Mathf.PerlinNoise(noiseOffset.y + x / ridgeScale, noiseOffset.x + z / ridgeScale) - 1f);
                float m = Fbm(x / (ridgeScale * 0.6f) + 13.7f, z / (ridgeScale * 0.6f) + 5.1f, 3);
                ridge = ridgeHeight * ridgeMask * rn * rn * (0.35f + 0.9f * m);
            }
            return (hills + ridge) * edge;
        }

        /// <summary>Terrain slope in degrees from central differences (for painting scree on steep ground).</summary>
        public float SlopeDeg(float x, float z, float step = 8f)
        {
            float dx = Height(x + step, z) - Height(x - step, z);
            float dz = Height(x, z + step) - Height(x, z - step);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz) / (2f * step)) * Mathf.Rad2Deg;
        }

        float Fbm(float x, float z, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(noiseOffset.x + x, noiseOffset.y + z);
                norm += amp;
                x = x * 2.03f + 17.1f;
                z = z * 2.03f + 3.7f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public enum PaintKind { Circle, Rect, Segment }

    /// <summary>
    /// One painted area on the terrain splat map: a circle (centre <see cref="a"/>, radius <see cref="width"/>),
    /// an oriented rectangle (centre a, size <see cref="b"/>, yaw <see cref="angleDeg"/>) or a road-like segment
    /// (a → b, half-width <see cref="width"/>). <see cref="feather"/> softens the edge (m); <see cref="edgeNoise"/>
    /// makes it ragged (m). Coordinates are world x / z.
    /// </summary>
    [Serializable]
    public struct PaintShape
    {
        public PaintKind kind;
        public int layer;
        public Vector2 a;
        public Vector2 b;
        public float width;
        public float angleDeg;
        public float feather;
        public float edgeNoise;
        [Range(0f, 1f)] public float strength;

        public static PaintShape Circle(int layer, Vector2 c, float radius, float feather, float edgeNoise = 0f, float strength = 1f)
            => new PaintShape { kind = PaintKind.Circle, layer = layer, a = c, width = radius, feather = feather, edgeNoise = edgeNoise, strength = strength };

        public static PaintShape Rectangle(int layer, Vector2 c, Vector2 size, float angleDeg, float feather, float edgeNoise = 0f, float strength = 1f)
            => new PaintShape { kind = PaintKind.Rect, layer = layer, a = c, b = size, angleDeg = angleDeg, feather = feather, edgeNoise = edgeNoise, strength = strength };

        public static PaintShape Segment(int layer, Vector2 from, Vector2 to, float halfWidth, float feather, float strength = 1f)
            => new PaintShape { kind = PaintKind.Segment, layer = layer, a = from, b = to, width = halfWidth, feather = feather, strength = strength };

        /// <summary>World-space bounds (x/z) including feather and edge noise.</summary>
        public Rect Bounds()
        {
            float pad = feather + edgeNoise;
            switch (kind)
            {
                case PaintKind.Circle:
                    return new Rect(a.x - width - pad, a.y - width - pad, 2f * (width + pad), 2f * (width + pad));
                case PaintKind.Rect:
                    float rad = 0.5f * b.magnitude + pad;
                    return new Rect(a.x - rad, a.y - rad, 2f * rad, 2f * rad);
                default:
                    float w = width + pad;
                    return Rect.MinMaxRect(Mathf.Min(a.x, b.x) - w, Mathf.Min(a.y, b.y) - w, Mathf.Max(a.x, b.x) + w, Mathf.Max(a.y, b.y) + w);
            }
        }

        /// <summary>Signed distance from the shape edge, m (negative inside).</summary>
        public float Distance(float x, float z)
        {
            switch (kind)
            {
                case PaintKind.Circle:
                {
                    float dx = x - a.x, dz = z - a.y;
                    return Mathf.Sqrt(dx * dx + dz * dz) - width;
                }
                case PaintKind.Rect:
                {
                    float rad = -angleDeg * Mathf.Deg2Rad;
                    float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                    float dx = x - a.x, dz = z - a.y;
                    float lx = Mathf.Abs(c * dx - s * dz) - 0.5f * b.x;
                    float lz = Mathf.Abs(s * dx + c * dz) - 0.5f * b.y;
                    float ox = Mathf.Max(lx, 0f), oz = Mathf.Max(lz, 0f);
                    return Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(lx, lz), 0f);
                }
                default:
                {
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(new Vector2(x, z) - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    Vector2 p = a + ab * t;
                    float dx = x - p.x, dz = z - p.y;
                    return Mathf.Sqrt(dx * dx + dz * dz) - width;
                }
            }
        }

        /// <summary>Coverage 0..1 at a world point.</summary>
        public float Coverage(float x, float z)
        {
            float d = Distance(x, z);
            float f = Mathf.Max(0.01f, feather);
            if (edgeNoise > 0f)
            {
                // The noise moves the edge by at most ±edgeNoise: skip it where the result is already decided.
                if (d - edgeNoise >= 0.5f * f) return 0f;
                if (d + edgeNoise <= -0.5f * f) return strength;
                d += edgeNoise * (Mathf.PerlinNoise(x / 23f + 11.3f, z / 23f + 7.9f) - 0.5f) * 2f;
            }
            return Mathf.Clamp01(0.5f - d / f) * strength;
        }
    }
}
