using UnityEngine;

namespace RocketSlam.Effects
{
    /// <summary>
    /// Dust/steam blown radially across the surface under a burning engine close to the ground. A world-space
    /// particle system (configured by the scene builder: flat circle emitter, radial speed, drag) is moved to the
    /// point where the exhaust hits the surface; emission and speed follow <see cref="EffectsMath.GroundDust"/>.
    /// </summary>
    public class GroundDust : MonoBehaviour
    {
        [SerializeField] ParticleSystem dust;
        [SerializeField] float maxRate = 45f;
        [SerializeField] float minSpeed = 10f;
        [SerializeField] float maxSpeed = 40f;

        /// <summary>Intensity last applied, 0..1.</summary>
        public float Intensity { get; private set; }
        public ParticleSystem Particles => dust;

        public void Setup(ParticleSystem system, float puffsPerSecond)
        {
            dust = system;
            maxRate = puffsPerSecond;
        }

        /// <summary>Raycast from the nozzle along the exhaust and emit where it meets the surface.</summary>
        public void Apply(float thrustFraction, Vector3 nozzleWorld, Vector3 exhaustDirWorld)
        {
            Intensity = 0f;
            if (thrustFraction > 0f && exhaustDirWorld.y < -0.2f &&
                Physics.Raycast(nozzleWorld, exhaustDirWorld, out RaycastHit hit, EffectsMath.DustMaxHeight * 1.5f))
            {
                float height = nozzleWorld.y - hit.point.y;
                Intensity = EffectsMath.GroundDust(thrustFraction, height);
                if (Intensity > 0f) transform.position = hit.point + Vector3.up * 0.6f;
            }

            if (dust == null) return;
            ParticleSystem.EmissionModule emission = dust.emission;
            emission.rateOverTime = maxRate * Intensity;
            if (Intensity <= 0f) return;
            ParticleSystem.MainModule main = dust.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(Mathf.Lerp(minSpeed, maxSpeed, Intensity) * 0.6f,
                                                              Mathf.Lerp(minSpeed, maxSpeed, Intensity));
        }

        public void Clear()
        {
            Intensity = 0f;
            if (dust == null) return;
            ParticleSystem.EmissionModule emission = dust.emission;
            emission.rateOverTime = 0f;
            dust.Clear(true);
        }
    }
}
