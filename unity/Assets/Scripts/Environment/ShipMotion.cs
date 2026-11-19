using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>
    /// Moves the droneship (kinematic Rigidbody) with the sea: every physics step the hull pose for the end of the
    /// step comes from <see cref="WaveField.HullPose"/> (the same wave function that displaces the visible ocean) and is
    /// applied with MovePosition / MoveRotation, so PhysX carries a landed stage along through friction and contact.
    /// The ship never moves horizontally on average (no surge / sway / yaw drift), only heaves, pitches and rolls.
    /// Runs before everything else in the physics step so the level and the assists see the deck where it is.
    /// </summary>
    [DefaultExecutionOrder(-320)]
    [RequireComponent(typeof(Rigidbody))]
    public class ShipMotion : MonoBehaviour
    {
        [SerializeField] SeaState sea;
        [Tooltip("Deck top above the mean sea level, m.")]
        [SerializeField] float freeboard = 3f;
        [SerializeField] float halfLength = 45f;
        [SerializeField] float halfWidth = 25f;
        [Tooltip("Deck centre (x, z) and heading; taken from the transform at build time.")]
        [SerializeField] Vector2 centreXZ;
        [SerializeField] float yawDeg;

        WaveField field;
        Rigidbody rb;
        Pose current, next;
        bool hasPose;
        float stepDt = 0.02f;

        public WaveField Field => field ??= new WaveField(sea);
        public SeaState Sea => sea;
        public float Freeboard => freeboard;
        /// <summary>Hull pose at the start of the current physics step (where the deck is now).</summary>
        public Pose Current => hasPose ? current : PoseAt(LevelClock.Physics);

        public void Setup(SeaState s, float deckFreeboard, float shipHalfLength, float shipHalfWidth)
        {
            sea = s;
            freeboard = deckFreeboard;
            halfLength = shipHalfLength;
            halfWidth = shipHalfWidth;
            centreXZ = new Vector2(transform.position.x, transform.position.z);
            yawDeg = transform.eulerAngles.y;
            field = null;
            hasPose = false;
            Pose p = PoseAt(0f);
            transform.SetPositionAndRotation(p.position, p.rotation);
        }

        /// <summary>Deck pose (root transform: deck centre at deck height) at time t.</summary>
        public Pose PoseAt(float t)
            => Field.HullPose(new Vector3(centreXZ.x, freeboard, centreXZ.y), yawDeg, halfLength, halfWidth, t);

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            field = new WaveField(sea);
            Step(LevelClock.Physics, Time.fixedDeltaTime, teleport: true);
        }

        void FixedUpdate() => Step(LevelClock.Physics, Time.fixedDeltaTime, teleport: false);

        /// <summary>Jump to the pose for the current <see cref="LevelClock"/> time (after a restart reset the clock).</summary>
        public void Snap()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            Step(LevelClock.Physics, Time.fixedDeltaTime, teleport: true);
        }

        void Step(float t, float dt, bool teleport)
        {
            stepDt = Mathf.Max(1e-4f, dt);
            current = PoseAt(t);
            next = PoseAt(t + stepDt);
            hasPose = true;
            if (teleport)
            {
                transform.SetPositionAndRotation(current.position, current.rotation);
                rb.position = current.position;
                rb.rotation = current.rotation;
            }
            rb.MovePosition(next.position);
            rb.MoveRotation(next.rotation);
        }

        /// <summary>Velocity of a point fixed to the hull (given in world space now) over the current step, m/s.</summary>
        public Vector3 PointVelocity(Vector3 worldPoint)
        {
            Pose a = Current;
            Pose b = hasPose ? next : PoseAt(LevelClock.Physics + stepDt);
            Vector3 local = Quaternion.Inverse(a.rotation) * (worldPoint - a.position);
            Vector3 pb = b.position + b.rotation * local;
            return (pb - worldPoint) / stepDt;
        }
    }
}
