using System;
using RocketSlam.Effects;
using UnityEngine;

namespace RocketSlam.Cameras
{
    /// <summary>
    /// Perlin-noise camera shake with two sources: <b>trauma</b> (explosions; added in kicks, decays linearly,
    /// amplitude = trauma²) and a continuous <b>rumble</b> level set every frame (engine near the ground).
    /// Applied once by <see cref="CameraDirector"/> to the final, already blended pose.
    /// </summary>
    [Serializable]
    public class CameraShake
    {
        [Tooltip("Rotation at full trauma, degrees (pitch/yaw; roll is half).")]
        public float maxAngleDeg = 5f;
        [Tooltip("Position offset at full trauma, m (camera-local).")]
        public float maxOffset = 1.2f;
        public float frequency = 12f;
        [Tooltip("Trauma lost per second.")]
        public float decayPerSecond = 0.7f;
        [Tooltip("Rotation at full engine rumble, degrees.")]
        public float rumbleAngleDeg = 0.35f;
        public float rumbleOffset = 0.12f;
        public float rumbleFrequency = 22f;

        float trauma, rumble, time;

        public float Trauma => trauma;
        public float RumbleLevel => rumble;

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, amount));
        public void SetRumble(float level) => rumble = Mathf.Clamp01(level);

        public void Reset()
        {
            trauma = rumble = 0f;
        }

        /// <summary>Advance by <paramref name="dt"/> and return the shake to apply in camera space.</summary>
        public void Step(float dt, out Vector3 localOffset, out Quaternion localRotation)
        {
            time += dt;
            trauma = EffectsMath.DecayTrauma(trauma, dt, decayPerSecond);
            float k = EffectsMath.ShakeAmount(trauma);
            if (k <= 0f && rumble <= 0f)
            {
                localOffset = Vector3.zero;
                localRotation = Quaternion.identity;
                return;
            }

            float a = time * frequency, b = time * rumbleFrequency;
            float angK = k * maxAngleDeg, offK = k * maxOffset;
            float angR = rumble * rumbleAngleDeg, offR = rumble * rumbleOffset;
            localRotation = Quaternion.Euler(
                angK * Noise(1.3f, a) + angR * Noise(11.7f, b),
                angK * Noise(2.9f, a) + angR * Noise(13.1f, b),
                0.5f * (angK * Noise(4.1f, a) + angR * Noise(17.3f, b)));
            localOffset = new Vector3(
                offK * Noise(5.7f, a) + offR * Noise(19.9f, b),
                offK * Noise(7.3f, a) + offR * Noise(23.3f, b),
                offK * Noise(9.1f, a) * 0.5f);
        }

        static float Noise(float seed, float t) => Mathf.PerlinNoise(seed, t) * 2f - 1f;
    }
}
