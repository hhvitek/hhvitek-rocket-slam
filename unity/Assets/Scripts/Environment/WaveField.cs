using System;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>One travelling sine wave of the sea surface (deep-water dispersion ω² = g·k).</summary>
    [Serializable]
    public struct Wave
    {
        public float amplitude;
        /// <summary>Wavenumber k = 2π / wavelength, 1/m.</summary>
        public float k;
        /// <summary>Angular frequency, rad/s.</summary>
        public float omega;
        /// <summary>Unit travel direction (x = east, y = north).</summary>
        public Vector2 dir;
        public float phase;

        public float Height(float x, float z, float t) => amplitude * Mathf.Sin(k * (dir.x * x + dir.y * z) - omega * t + phase);
    }

    /// <summary>
    /// The ONE sea-surface function of the droneship levels: a height field h(x, z, t) = Σ Aᵢ·sin(kᵢ·(dᵢ·p) − ωᵢ·t + φᵢ)
    /// built deterministically from a <see cref="SeaState"/> (main swell, two secondary swells, three short wind waves).
    /// <see cref="OceanSurface"/> displaces the visible mesh with it and <see cref="ShipMotion"/> derives the droneship's
    /// heave, pitch and roll from it, so the deck rides exactly on the waves the player sees. Pure C#, EditMode-testable.
    /// </summary>
    public sealed class WaveField
    {
        public const float Gravity = 9.81f;
        readonly Wave[] waves;

        public Wave[] Waves => waves;
        public bool IsFlat => waves.Length == 0;

        public WaveField(Wave[] w) => waves = w ?? Array.Empty<Wave>();

        public WaveField(SeaState s) : this(Build(s)) { }

        static Wave[] Build(SeaState s)
        {
            if (s.swellAmplitude <= 0f && s.chopAmplitude <= 0f) return Array.Empty<Wave>();
            var rng = new System.Random(s.seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float period = Mathf.Max(2f, s.swellPeriod);
            float dir = s.swellDirection;
            return new[]
            {
                Make(s.swellAmplitude, period, dir, R(0f, 6.283f)),
                Make(s.swellAmplitude * 0.4f, period * 0.8f, dir + R(30f, 45f), R(0f, 6.283f)),
                Make(s.swellAmplitude * 0.25f, period * 0.65f, dir - R(45f, 65f), R(0f, 6.283f)),
                // Short wind waves (wavelength 24-40 m: resolved by the ~7 m mesh, averaged out by the 90 m hull).
                Make(s.chopAmplitude, 5.0f, dir + R(-25f, -10f), R(0f, 6.283f)),
                Make(s.chopAmplitude * 0.7f, 4.4f, dir + R(15f, 35f), R(0f, 6.283f)),
                Make(s.chopAmplitude * 0.5f, 3.9f, dir + R(50f, 75f), R(0f, 6.283f)),
            };
        }

        /// <summary>A deep-water wave of the given period travelling towards <paramref name="directionDeg"/>
        /// (clockwise from north).</summary>
        public static Wave Make(float amplitude, float period, float directionDeg, float phase)
        {
            float omega = 2f * Mathf.PI / period;
            float a = directionDeg * Mathf.Deg2Rad;
            return new Wave
            {
                amplitude = amplitude,
                omega = omega,
                k = omega * omega / Gravity,
                dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a)),
                phase = phase,
            };
        }

        /// <summary>Sea surface height above the mean level at world (x, z), time t.</summary>
        public float Height(float x, float z, float t)
        {
            float h = 0f;
            for (int i = 0; i < waves.Length; i++) h += waves[i].Height(x, z, t);
            return h;
        }

        /// <summary>Largest possible |height| (sum of amplitudes), m.</summary>
        public float MaxHeight
        {
            get
            {
                float s = 0f;
                foreach (Wave w in waves) s += Mathf.Abs(w.amplitude);
                return s;
            }
        }

        /// <summary>Hull sample grid: 5 stations along the length × 3 across the beam, as fractions of the half sizes.</summary>
        public static readonly float[] HullStationsX = { -1f, -0.5f, 0f, 0.5f, 1f };
        public static readonly float[] HullStationsZ = { -1f, 0f, 1f };

        /// <summary>
        /// Pose of a floating box hull (deck centre at <paramref name="centre"/> + heave, long axis = local +x turned
        /// by <paramref name="yawDeg"/>) at time t: the plane fitted (least squares) through the wave heights at a
        /// 5 × 3 grid of stations under the hull. heave = mean height, pitch = slope along the length, roll = slope
        /// across the beam. A 90 m hull thus rides the long swell fully and averages out most of the short chop, like a
        /// real barge. <paramref name="centre"/>.y is the deck height above the mean sea level.
        /// </summary>
        public Pose HullPose(Vector3 centre, float yawDeg, float halfLength, float halfWidth, float t)
        {
            HullMotion(centre, yawDeg, halfLength, halfWidth, t, out float heave, out float pitch, out float roll);
            Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
            return new Pose(new Vector3(centre.x, centre.y + heave, centre.z), yaw * Quaternion.Euler(roll, 0f, pitch));
        }

        /// <summary>Heave (m), pitch (deg, bow up = + rotation about local z) and roll (deg, local +z side up = −
        /// rotation about local x) of the fitted hull plane; see <see cref="HullPose"/>.</summary>
        public void HullMotion(Vector3 centre, float yawDeg, float halfLength, float halfWidth, float t,
            out float heave, out float pitchDeg, out float rollDeg)
        {
            Quaternion yaw = Quaternion.Euler(0f, yawDeg, 0f);
            Vector3 fwd = yaw * Vector3.right, side = yaw * Vector3.forward;
            float sum = 0f, sx = 0f, sz = 0f, xx = 0f, zz = 0f;
            int n = 0;
            foreach (float fx in HullStationsX)
            foreach (float fz in HullStationsZ)
            {
                float x = fx * halfLength, z = fz * halfWidth;
                Vector3 p = centre + fwd * x + side * z;
                float h = Height(p.x, p.z, t);
                sum += h; sx += x * h; sz += z * h; xx += x * x; zz += z * z;
                n++;
            }
            heave = sum / n;
            pitchDeg = Mathf.Atan(sx / xx) * Mathf.Rad2Deg;   // stations are symmetric: the slope is Σx·h / Σx²
            rollDeg = -Mathf.Atan(sz / zz) * Mathf.Rad2Deg;
        }
    }
}
