using UnityEngine;

namespace RocketSlam.Effects
{
    /// <summary>
    /// Engine exhaust at the nozzle exit: a white-hot stretched core, an orange outer plume, a glow at the nozzle
    /// and a flickering point light. The particle systems are configured by the scene builder (local simulation
    /// space, flame along local -Y); this component only modulates them with thrust every frame.
    /// </summary>
    public class EngineFlame : MonoBehaviour
    {
        [SerializeField] ParticleSystem core;
        [SerializeField] ParticleSystem plume;
        [SerializeField] ParticleSystem glow;
        [SerializeField] Light flameLight;
        [SerializeField] float coreLifetime = 0.1f;
        [SerializeField] float plumeLifetime = 0.2f;
        [SerializeField] float coreRate = 150f;
        [SerializeField] float plumeRate = 80f;
        [SerializeField] float glowRate = 40f;
        [SerializeField] float lightIntensity = 6f;

        float noiseSeed;
        float plumeSizeMin = 1f, plumeSizeMax = 1f;

        public ParticleSystem Core => core;
        public ParticleSystem Plume => plume;
        public Light FlameLight => flameLight;
        /// <summary>Thrust fraction last applied (0 = off).</summary>
        public float Level { get; private set; }

        public void Setup(ParticleSystem coreSystem, ParticleSystem plumeSystem, ParticleSystem glowSystem, Light light)
        {
            core = coreSystem;
            plume = plumeSystem;
            glow = glowSystem;
            flameLight = light;
        }

        void Awake()
        {
            noiseSeed = Random.value * 100f;
            if (plume != null)
            {
                ParticleSystem.MinMaxCurve size = plume.main.startSize;
                plumeSizeMin = size.mode == ParticleSystemCurveMode.TwoConstants ? size.constantMin : size.constant;
                plumeSizeMax = size.mode == ParticleSystemCurveMode.TwoConstants ? size.constantMax : size.constant;
            }
        }

        /// <summary>Apply engine state: <paramref name="thrustFraction"/> 0..1 (0 = off) and the thrust direction
        /// in the rocket's local frame (the flame points the opposite way). Retro-burning into a fast airflow
        /// (<paramref name="airspeed"/>, m/s) makes the plume balloon around the engine. The flame never reaches past
        /// <paramref name="groundDistance"/> (nozzle to the surface along the exhaust, m): close to the pad it spreads
        /// as dust instead of piling up as a dark blob.</summary>
        public void Apply(float thrustFraction, Vector3 localThrustDir, float time, float airspeed = 0f, float groundDistance = float.PositiveInfinity)
        {
            Level = thrustFraction;
            bool on = thrustFraction > 0f;
            transform.localRotation = Quaternion.FromToRotation(Vector3.up, localThrustDir);

            float flicker = 0.9f + 0.2f * Mathf.PerlinNoise(noiseSeed, time * 18f);
            float length = EffectsMath.ClampFlameToGround(EffectsMath.FlameLength(thrustFraction) * flicker, groundDistance);
            float bright = EffectsMath.FlameBrightness(thrustFraction);

            SetSystem(core, on, coreRate * (0.5f + 0.5f * thrustFraction), length * 0.55f / coreLifetime, bright);
            SetSystem(plume, on, plumeRate * (0.4f + 0.6f * thrustFraction), length / plumeLifetime, bright);
            SetSystem(glow, on, glowRate, 0f, bright);
            if (on && plume != null)
            {
                ParticleSystem.MainModule main = plume.main;
                float k = EffectsMath.PlumeExpansion(airspeed) * EffectsMath.PlumeGroundScale(groundDistance);
                main.startSize = new ParticleSystem.MinMaxCurve(plumeSizeMin * k, plumeSizeMax * k);
            }

            if (flameLight != null)
            {
                flameLight.enabled = on;
                if (on)
                {
                    flameLight.intensity = lightIntensity * bright * (0.8f + 0.4f * Mathf.PerlinNoise(noiseSeed + 7f, time * 25f));
                    flameLight.range = 25f + 35f * thrustFraction;
                    flameLight.transform.localPosition = new Vector3(0f, -0.35f * length, 0f);
                }
            }
        }

        static void SetSystem(ParticleSystem ps, bool on, float rate, float speed, float brightness)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = on ? rate : 0f;
            if (!on) return;
            ParticleSystem.MainModule main = ps.main;
            if (speed > 0f) main.startSpeed = speed;
            main.startColor = new Color(1f, 1f, 1f, brightness);
        }

        /// <summary>Remove all live particles (restart).</summary>
        public void Clear()
        {
            Apply(0f, Vector3.up, 0f);
            if (core != null) core.Clear(true);
            if (plume != null) plume.Clear(true);
            if (glow != null) glow.Clear(true);
        }
    }
}
