using UnityEngine;

namespace RocketSlam.Effects
{
    /// <summary>
    /// Hard-impact explosion: flash, fireball, rising smoke column, sparks and tumbling debris (one-shot particle
    /// systems configured by the scene builder) plus a bright flash light that fades out.
    /// </summary>
    public class ExplosionEffect : MonoBehaviour
    {
        [SerializeField] ParticleSystem[] systems = new ParticleSystem[0];
        [SerializeField] Light flash;
        [SerializeField] float flashIntensity = 40f;
        [SerializeField] float flashTime = 1.2f;

        float playedAt = -1f;

        public ParticleSystem[] Systems => systems;
        public Light Flash => flash;
        /// <summary>True from <see cref="Play"/> until every particle has died.</summary>
        public bool IsPlaying
        {
            get
            {
                if (playedAt < 0f) return false;
                foreach (ParticleSystem ps in systems)
                    if (ps != null && ps.IsAlive(true)) return true;
                return false;
            }
        }
        public int PlayCount { get; private set; }

        public void Setup(ParticleSystem[] particleSystems, Light flashLight)
        {
            systems = particleSystems;
            flash = flashLight;
        }

        void Awake()
        {
            if (flash != null) flash.enabled = false;
        }

        public void Play(Vector3 position)
        {
            transform.position = position;
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null) continue;
                ps.Clear(true);
                ps.Play(true);
            }
            playedAt = Time.time;
            PlayCount++;
            if (flash != null)
            {
                flash.enabled = true;
                flash.intensity = flashIntensity;
            }
        }

        public void Clear()
        {
            playedAt = -1f;
            foreach (ParticleSystem ps in systems)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (flash != null) flash.enabled = false;
        }

        void Update()
        {
            if (playedAt < 0f || flash == null || !flash.enabled) return;
            float t = (Time.time - playedAt) / flashTime;
            if (t >= 1f)
            {
                flash.enabled = false;
                return;
            }
            // Fast attack is the Play() spike; then a flickering fall-off.
            float k = (1f - t) * (1f - t);
            flash.intensity = flashIntensity * k * (0.75f + 0.25f * Mathf.PerlinNoise(3.1f, Time.time * 30f));
            flash.range = Mathf.Lerp(160f, 60f, t);
        }
    }
}
