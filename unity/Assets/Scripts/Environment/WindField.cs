using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>
    /// Horizontal wind velocity over time: the steady wind plus gusts. Gusts are a sum of three sines with
    /// incommensurate periods (deterministic, smooth, never repeating within a flight), mostly along the steady wind
    /// with a smaller crosswise swing; their peak is about <see cref="WindSettings.gustStrength"/>. Pure C#.
    /// </summary>
    public sealed class WindField
    {
        readonly Vector2 steady;
        readonly Vector2 along, across;
        readonly float strength, freq;
        readonly float p0, p1, p2, p3;

        public WindSettings Settings { get; }

        public WindField(WindSettings s)
        {
            Settings = s;
            steady = s.steady;
            along = steady.sqrMagnitude > 1e-4f ? steady.normalized : Vector2.right;
            across = new Vector2(-along.y, along.x);
            strength = Mathf.Max(0f, s.gustStrength);
            freq = Mathf.Max(0.01f, s.gustFrequency);
            var rng = new System.Random(s.seed);
            p0 = (float)rng.NextDouble() * 6.283f;
            p1 = (float)rng.NextDouble() * 6.283f;
            p2 = (float)rng.NextDouble() * 6.283f;
            p3 = (float)rng.NextDouble() * 6.283f;
        }

        public bool IsCalm => steady.sqrMagnitude < 1e-4f && strength <= 0f;

        /// <summary>Gust factor in about [-1, 1] at time t (along the steady wind).</summary>
        public float Gust(float t)
        {
            float w = 2f * Mathf.PI * freq * t;
            return 0.55f * Mathf.Sin(w + p0) + 0.3f * Mathf.Sin(2.31f * w + p1) + 0.15f * Mathf.Sin(4.17f * w + p2);
        }

        /// <summary>Wind velocity (the air's motion, world x/z, y = 0) at time t, m/s.</summary>
        public Vector3 At(float t)
        {
            if (IsCalm) return Vector3.zero;
            // Gusts mostly blow harder (skewed positive), with a smaller sideways swing.
            float g = Gust(t);
            float gust = strength * (g > 0f ? g : 0.5f * g);
            float side = 0.35f * strength * Mathf.Sin(2f * Mathf.PI * freq * 0.63f * t + p3);
            Vector2 w = steady + along * gust + across * side;
            return new Vector3(w.x, 0f, w.y);
        }
    }
}
