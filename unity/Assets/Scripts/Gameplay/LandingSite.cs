using RocketSlam.World;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// The landing target of a level: the ground pad or the droneship deck. Answers where the target is now, the
    /// surface normal, the height of the surface under a point (altitude reference), how fast the surface moves at a
    /// point (deck-relative landing speeds) and whether a point lies over the landing area (all four feet must).
    /// A ground pad is static; a deck follows its <see cref="ShipMotion"/>.
    /// </summary>
    public class LandingSite : MonoBehaviour
    {
        [Tooltip("Top centre of the landing area in this object's local space.")]
        [SerializeField] Vector3 localTarget;
        [Tooltip("Half size of the landing area (local x, local z), m.")]
        [SerializeField] Vector2 halfExtents = new Vector2(20f, 20f);
        [SerializeField] ShipMotion motion;
        [SerializeField] string siteName = "PAD";

        public void Setup(Vector3 target, Vector2 half, ShipMotion ship, string label)
        {
            localTarget = target;
            halfExtents = half;
            motion = ship;
            siteName = label;
        }

        public bool IsMoving => motion != null;
        public ShipMotion Motion => motion;
        public Vector2 HalfExtents => halfExtents;
        /// <summary>Short label for the HUD marker ("PAD", "SHIP").</summary>
        public string Label => siteName;

        /// <summary>Pose of the site's frame now (physics time for a ship).</summary>
        public Pose CurrentPose => motion != null ? motion.Current : new Pose(transform.position, transform.rotation);

        /// <summary>World position of the top centre of the landing area.</summary>
        public Vector3 TargetPoint
        {
            get
            {
                Pose p = CurrentPose;
                return p.position + p.rotation * localTarget;
            }
        }

        public Vector3 SurfaceUp => CurrentPose.rotation * Vector3.up;

        /// <summary>The point of the landing surface under world (x, z); outside the area the nearest edge point's
        /// plane is used (so the altitude reference never extrapolates a tilted deck kilometres away).</summary>
        public Vector3 SurfacePointUnder(float x, float z)
        {
            Pose p = CurrentPose;
            Vector3 local = Quaternion.Inverse(p.rotation) * (new Vector3(x, 0f, z) - p.position) - localTarget;
            var clamped = new Vector3(Mathf.Clamp(local.x, -halfExtents.x, halfExtents.x), 0f, Mathf.Clamp(local.z, -halfExtents.y, halfExtents.y));
            Vector3 point = p.position + p.rotation * (localTarget + clamped);
            Vector3 n = p.rotation * Vector3.up;
            // Move along the plane from the clamped point to the requested (x, z) when inside; keep its height outside.
            if (Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.z) <= halfExtents.y && Mathf.Abs(n.y) > 1e-3f)
            {
                float y = point.y - (n.x * (x - point.x) + n.z * (z - point.z)) / n.y;
                return new Vector3(x, y, z);
            }
            return point;
        }

        /// <summary>Velocity of the surface at a world point (zero for a ground pad), m/s.</summary>
        public Vector3 PointVelocity(Vector3 worldPoint) => motion != null ? motion.PointVelocity(worldPoint) : Vector3.zero;

        /// <summary>True when the world point lies within the landing area footprint (in the surface's own frame).</summary>
        public bool IsOver(Vector3 worldPoint)
        {
            Pose p = CurrentPose;
            Vector3 local = Quaternion.Inverse(p.rotation) * (worldPoint - p.position) - localTarget;
            return Mathf.Abs(local.x) <= halfExtents.x && Mathf.Abs(local.z) <= halfExtents.y;
        }
    }
}
