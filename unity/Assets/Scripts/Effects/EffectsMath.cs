using UnityEngine;

namespace RocketSlam.Effects
{
    /// <summary>
    /// Pure mappings from flight state to effect and sound parameters (flame size, ground dust, camera shake,
    /// engine and wind sound). Kept free of scene state so EditMode tests can pin them down.
    /// </summary>
    public static class EffectsMath
    {
        public const float MinFlameLength = 10f;
        public const float MaxFlameLength = 32f;
        /// <summary>The engine raises dust/steam from the surface below this height (nozzle to ground), m.</summary>
        public const float DustMaxHeight = 60f;
        /// <summary>Camera rumble only when the burning engine is closer to the ground than this, m.</summary>
        public const float RumbleMaxHeight = 50f;

        /// <summary>Current thrust as a fraction of the engine's full thrust, 0..1.</summary>
        public static float ThrustFraction(float thrustN, float maxThrustN)
            => maxThrustN > 0f ? Mathf.Clamp01(thrustN / maxThrustN) : 0f;

        /// <summary>Visible flame length behind the nozzle exit, m (0 with the engine off).</summary>
        public static float FlameLength(float thrustFraction)
            => thrustFraction <= 0f ? 0f : Mathf.Lerp(MinFlameLength, MaxFlameLength, Mathf.Clamp01(thrustFraction));

        /// <summary>Flame brightness multiplier 0..1 (a deep-throttled engine still glows clearly).</summary>
        public static float FlameBrightness(float thrustFraction)
            => thrustFraction <= 0f ? 0f : Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(thrustFraction));

        /// <summary>Plume width multiplier: a retro-burn into fast oncoming air balloons the exhaust (×1 at rest,
        /// ×2.4 at terminal velocity).</summary>
        public static float PlumeExpansion(float airspeed) => 1f + 1.4f * Mathf.Clamp01(airspeed / 220f);

        /// <summary>Flame length limited by the surface below the nozzle (<paramref name="groundDistance"/> along the
        /// exhaust, m): at most 85 % of the gap, never shorter than 1.5 m.</summary>
        public static float ClampFlameToGround(float length, float groundDistance)
            => float.IsInfinity(groundDistance) ? length : Mathf.Min(length, Mathf.Max(1.5f, 0.85f * groundDistance));

        /// <summary>Plume width factor near the ground: the outer fire shrinks to 45 % when the nozzle is at the pad,
        /// full size from 25 m up (the dust takes over the spreading look).</summary>
        public static float PlumeGroundScale(float groundDistance)
            => float.IsInfinity(groundDistance) ? 1f : Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(groundDistance / 25f));

        /// <summary>Ground dust/steam intensity 0..1: grows with thrust and as the nozzle nears the surface. Even a
        /// deep-throttled engine (final hover) blows plenty of dust.</summary>
        public static float GroundDust(float thrustFraction, float nozzleHeight, float maxHeight = DustMaxHeight)
        {
            if (thrustFraction <= 0f || nozzleHeight >= maxHeight || maxHeight <= 0f) return 0f;
            float near = 1f - Mathf.Max(0f, nozzleHeight) / maxHeight;
            return (0.35f + 0.65f * Mathf.Clamp01(thrustFraction)) * Mathf.Pow(near, 1.3f);
        }

        /// <summary>Engine rumble 0..1 for the camera: zero above <see cref="RumbleMaxHeight"/> (no shake during
        /// the fast descent), quadratic towards the ground.</summary>
        public static float Rumble(float thrustFraction, float height, float maxHeight = RumbleMaxHeight)
        {
            if (thrustFraction <= 0f || height >= maxHeight || maxHeight <= 0f) return 0f;
            float near = 1f - Mathf.Max(0f, height) / maxHeight;
            return Mathf.Clamp01(thrustFraction) * near * near;
        }

        /// <summary>Linear trauma decay (trauma model: shake amplitude = trauma²).</summary>
        public static float DecayTrauma(float trauma, float dt, float decayPerSecond)
            => Mathf.Max(0f, trauma - decayPerSecond * Mathf.Max(0f, dt));

        public static float ShakeAmount(float trauma) => Mathf.Clamp01(trauma) * Mathf.Clamp01(trauma);

        /// <summary>Shake trauma an explosion adds for a camera at <paramref name="distance"/> m.</summary>
        public static float ExplosionTrauma(float distance) => Mathf.Clamp01(1.3f - distance / 250f);

        /// <summary>Weight of the high (full-power) engine loop vs the low loop, 0..1.</summary>
        public static float EngineHighWeight(float thrustFraction) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(thrustFraction));

        public static float EngineVolume(float thrustFraction)
            => thrustFraction <= 0f ? 0f : Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(thrustFraction));

        public static float EnginePitch(float thrustFraction) => Mathf.Lerp(0.82f, 1.12f, Mathf.Clamp01(thrustFraction));

        /// <summary>Wind loudness 0..1 by airspeed: silent below 8 m/s, full at ~230 m/s (terminal velocity).</summary>
        public static float WindVolume(float airspeed)
        {
            float t = Mathf.Clamp01((airspeed - 8f) / 220f);
            return Mathf.Sqrt(t);
        }

        public static float WindPitch(float airspeed) => Mathf.Lerp(0.6f, 1.35f, Mathf.Clamp01(airspeed / 230f));

        /// <summary>Linear 0..1 volume to mixer decibels (-80 dB = silent).</summary>
        public static float LinearToDecibels(float linear)
            => linear <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));
    }
}
