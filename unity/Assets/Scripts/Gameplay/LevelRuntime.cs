using RocketSlam.Rocket;
using RocketSlam.World;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// Per-step level conditions for the rocket, before anyone flies it: the wind at the stage and the landing-surface
    /// reference (deck height under the stage, deck velocity there, deck normal), so altitude, the burn cue, the Easy
    /// assist and the autopilot all work relative to a moving deck. Runs after <see cref="ShipMotion"/> and before
    /// the autopilot / assist / rocket.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class LevelRuntime : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [SerializeField] LandingSite site;
        [SerializeField] LevelConfig level;

        WindField wind;

        public WindField WindField => wind ??= new WindField(level != null ? level.wind : default);
        /// <summary>Wind at the stage now (m/s, horizontal).</summary>
        public Vector3 CurrentWind { get; private set; }
        public bool HasWind => !WindField.IsCalm;
        public LandingSite Site => site;

        public void Setup(RocketController r, LandingSite s, LevelConfig l)
        {
            rocket = r;
            site = s;
            level = l;
            wind = null;
        }

        void FixedUpdate() => Apply(LevelClock.Physics);

        /// <summary>Push the conditions at level time t (<see cref="LevelClock"/>) into the rocket (also called right
        /// after a restart).</summary>
        public void Apply(float t)
        {
            if (rocket == null || site == null) return;
            CurrentWind = WindField.At(t);
            rocket.Wind = CurrentWind;
            Vector3 p = rocket.Position;
            Vector3 surface = site.SurfacePointUnder(p.x, p.z);
            rocket.ReferenceHeight = surface.y;
            rocket.ReferenceVelocity = site.PointVelocity(surface);
            rocket.ReferenceUp = site.SurfaceUp;
        }
    }
}
