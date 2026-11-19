using RocketSlam.Cameras;
using RocketSlam.Gameplay;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Effects
{
    /// <summary>
    /// Drives the visual effects from the flight state each frame: engine flame and light, ground dust under a
    /// low burning engine, camera rumble near the ground, and the explosion (plus a camera kick) on a hard impact.
    /// Everything is cleared on restart.
    /// </summary>
    public class RocketEffects : MonoBehaviour
    {
        [SerializeField] LandingSession session;
        [SerializeField] CameraDirector cameras;
        [SerializeField] EngineFlame flame;
        [SerializeField] GroundDust dust;
        [SerializeField] ExplosionEffect explosion;

        RocketController rocket;

        public EngineFlame Flame => flame;
        public GroundDust Dust => dust;
        public ExplosionEffect Explosion => explosion;
        /// <summary>Thrust fraction 0..1 last shown.</summary>
        public float ThrustFraction { get; private set; }

        public void Setup(LandingSession s, CameraDirector c, EngineFlame f, GroundDust d, ExplosionEffect e)
        {
            session = s;
            cameras = c;
            flame = f;
            dust = d;
            explosion = e;
        }

        void OnEnable()
        {
            if (session == null) return;
            rocket = session.Rocket;
            session.Exploded += OnExploded;
            session.Restarted += OnRestarted;
        }

        void OnDisable()
        {
            if (session == null) return;
            session.Exploded -= OnExploded;
            session.Restarted -= OnRestarted;
        }

        void LateUpdate()
        {
            if (rocket == null) return;
            ThrustFraction = EffectsMath.ThrustFraction(rocket.CurrentThrust, rocket.Config.maxThrustN);
            Vector3 localDir = rocket.ThrustDirectionLocal;
            Transform t = rocket.transform;
            Vector3 nozzle = t.TransformPoint(0f, rocket.NozzleExitY, 0f);
            Vector3 exhaust = -(t.rotation * localDir);
            // The rocket is on the Ignore Raycast layer, so this finds the pad or ground under the flame.
            float groundDistance = ThrustFraction > 0f && Physics.Raycast(nozzle, exhaust, out RaycastHit hit, EffectsMath.MaxFlameLength * 1.5f)
                ? hit.distance : float.PositiveInfinity;
            if (flame != null) flame.Apply(ThrustFraction, localDir, Time.time, rocket.Velocity.magnitude, groundDistance);
            if (dust != null) dust.Apply(ThrustFraction, nozzle, exhaust);

            if (cameras != null)
                cameras.Shake.SetRumble(EffectsMath.Rumble(ThrustFraction, Mathf.Max(0f, rocket.Altitude)));
        }

        void OnExploded(Vector3 at)
        {
            if (explosion != null) explosion.Play(at);
            if (cameras != null && cameras.ActiveCamera != null)
                cameras.Shake.AddTrauma(EffectsMath.ExplosionTrauma(Vector3.Distance(cameras.ActiveCamera.transform.position, at)));
        }

        void OnRestarted()
        {
            if (flame != null) flame.Clear();
            if (dust != null) dust.Clear();
            if (explosion != null) explosion.Clear();
            if (cameras != null) cameras.Shake.Reset();
        }
    }
}
