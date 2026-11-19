using RocketSlam.Gameplay;
using UnityEngine;

namespace RocketSlam.Rocket
{
    public enum LegsState { Stowed, Deploying, Deployed }

    /// <summary>Contacts reported by the physics step that just finished.</summary>
    public struct ContactSnapshot
    {
        public bool body;
        public int legsOnPadMask;
        public int legsOnGroundMask;

        public int LegsOnPad => CountBits(legsOnPadMask);
        public int LegsOnGround => CountBits(legsOnGroundMask);

        static int CountBits(int v)
        {
            int n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }
    }

    /// <summary>
    /// The booster: a Rigidbody driven by custom forces in FixedUpdate (thrust at the gimballed engine,
    /// drag, weathervane torque; gravity comes from PhysX). Input comes from <c>PlayerControls</c> or an
    /// autopilot through the public command methods. Origin = centre of the engine plane at the bottom.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public class RocketController : MonoBehaviour
    {
        [SerializeField] RocketConfig config;
        [SerializeField] RocketLeg[] legs = new RocketLeg[0];
        [SerializeField] RocketGridFins gridFins;
        [Tooltip("Local height of the nozzle exit plane; the exhaust effects start here.")]
        [SerializeField] float nozzleExitY = -1.25f;
        [Tooltip("Seconds for the grid fins to swing out (visual only).")]
        [SerializeField] float finDeployTime = 1f;

        Rigidbody rb;
        Renderer[] renderers;
        ContactSnapshot pendingContacts;
        Vector3 pendingVelocity;

        public RocketConfig Config => config;
        public Rigidbody Body => rb != null ? rb : rb = GetComponent<Rigidbody>();

        public bool EngineOn { get; private set; }
        /// <summary>Throttle setpoint, always within [<see cref="MinThrottle"/>, 1]; used whenever the engine runs.</summary>
        public float Throttle { get; private set; } = 1f;
        public Vector2 Gimbal { get; private set; }
        public float FuelKg { get; private set; }
        public float StartFuelKg { get; private set; }
        public LegsState Legs { get; private set; }
        public float LegDeployProgress { get; private set; }
        /// <summary>Grid fins (visual): 0 stowed, 1 deployed. They open once the stage falls or moves fast.</summary>
        public float FinDeployProgress { get; private set; }
        public RocketGridFins GridFins => gridFins;
        public bool ControlsLocked { get; private set; }
        public bool Exploded { get; private set; }
        /// <summary>World Y of the surface altitude is measured from (the pad top / the deck under the stage).</summary>
        public float ReferenceHeight { get; set; }
        /// <summary>Velocity of the landing surface under the stage (moving deck), m/s. Zero for a ground pad.</summary>
        public Vector3 ReferenceVelocity { get; set; }
        /// <summary>Normal of the landing surface (deck up). World up for a ground pad.</summary>
        public Vector3 ReferenceUp { get; set; } = Vector3.up;
        /// <summary>Wind (the air's velocity) at the stage, set every physics step by the level, m/s.</summary>
        public Vector3 Wind { get; set; }
        /// <summary>Horizontal force the wind adds compared with still air (last physics step), N. The assist and the
        /// autopilot feed it forward to hold their course.</summary>
        public Vector3 WindForce { get; private set; }

        /// <summary>Easy mode: the engine may throttle all the way down to 0 % (the assist needs to hover).
        /// Manual mode keeps the realistic deep-throttle limit <see cref="RocketConfig.minThrottle"/>.</summary>
        public bool FullThrottleRange
        {
            get => fullThrottleRange;
            set { fullThrottleRange = value; Throttle = RocketPhysics.ClampThrottle(Throttle, MinThrottle); }
        }
        bool fullThrottleRange;
        public float MinThrottle => fullThrottleRange ? 0f : config.minThrottle;

        public float Mass => RocketPhysics.Mass(config, FuelKg);
        public float CurrentThrust => RocketPhysics.Thrust(config, EngineOn && FuelKg > 0f && !Exploded, Throttle, MinThrottle);
        /// <summary>Local height of the nozzle exit plane.</summary>
        public float NozzleExitY => nozzleExitY;
        /// <summary>Thrust direction in the rocket's local frame (gimbal applied).</summary>
        public Vector3 ThrustDirectionLocal => RocketPhysics.ThrustDirectionLocal(config, Gimbal);
        public Vector3 Velocity => Body.linearVelocity;
        /// <summary>Physics pose (rb.position/rotation), not the interpolated transform. Falls back to the
        /// transform outside play mode (editor screenshot tools).</summary>
        public Vector3 Position => Application.isPlaying ? Body.position : transform.position;
        public Quaternion Rotation => Application.isPlaying ? Body.rotation : transform.rotation;
        public Vector3 Up => Rotation * Vector3.up;
        public float TiltDeg => Vector3.Angle(Up, Vector3.up);
        /// <summary>Tilt relative to the landing surface normal (= <see cref="TiltDeg"/> on a ground pad).</summary>
        public float RelativeTiltDeg => Vector3.Angle(Up, ReferenceUp);
        /// <summary>Velocity relative to the landing surface under the stage.</summary>
        public Vector3 RelativeVelocity => Velocity - ReferenceVelocity;
        /// <summary>Velocity relative to the air.</summary>
        public Vector3 Airspeed => Velocity - Wind;
        public int LegCount => legs.Length;
        public RocketLeg GetLeg(int i) => legs[i];
        public Vector3 CenterOfMassWorld => Position + Rotation * new Vector3(0f, config.centerOfMassHeight, 0f);
        public Vector3 EnginePositionWorld => Position;

        /// <summary>
        /// Clearance of the lowest point (engine or deployed feet) above the landing surface: each point is measured
        /// against the surface plane under that point (through <see cref="ReferenceHeight"/> under the stage, normal
        /// <see cref="ReferenceUp"/>), so on a rolling deck the foot over the raised side counts, not the deck height
        /// under the stage centre. On a flat pad this is simply lowest y − <see cref="ReferenceHeight"/>.
        /// </summary>
        public float Altitude
        {
            get
            {
                Vector3 p = Position;
                float lowest = Clearance(p, p);
                if (Legs != LegsState.Stowed)
                    foreach (RocketLeg leg in legs)
                        if (leg.FootCollider.enabled)
                        {
                            Bounds b = leg.FootCollider.bounds;
                            lowest = Mathf.Min(lowest, Clearance(new Vector3(b.center.x, b.min.y, b.center.z), p));
                        }
                return lowest;
            }
        }

        float Clearance(Vector3 point, Vector3 anchor)
            => RocketPhysics.SurfaceClearance(point, new Vector3(anchor.x, ReferenceHeight, anchor.z), ReferenceUp);

        /// <summary>Contacts from the last finished physics step (refreshed at the start of each FixedUpdate).</summary>
        public ContactSnapshot Contacts { get; private set; }
        /// <summary>Velocity at the start of the last finished physics step, i.e. just before any impact in it.</summary>
        public Vector3 VelocityBeforeLastStep { get; private set; }

        public void SetConfig(RocketConfig c, RocketLeg[] legParts, RocketGridFins fins = null, float nozzleExit = -1.25f)
        {
            config = c;
            legs = legParts;
            gridFins = fins;
            nozzleExitY = nozzleExit;
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            renderers = GetComponentsInChildren<Renderer>(true);
            ConfigureBody();
        }

        void ConfigureBody()
        {
            rb.useGravity = true;
            rb.linearDamping = 0f;
            rb.angularDamping = config.angularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.sleepThreshold = 0f; // keep contact callbacks coming while the stage stands on the pad
            UpdateMassProperties();
        }

        void UpdateMassProperties()
        {
            float m = Mass;
            rb.mass = m;
            rb.centerOfMass = new Vector3(0f, config.centerOfMassHeight, 0f);
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.inertiaTensor = RocketPhysics.InertiaTensor(config, m);
        }

        // ---- commands ---------------------------------------------------------------------------

        public void SetEngine(bool on)
        {
            if (ControlsLocked || Exploded) return;
            EngineOn = on && FuelKg > 0f;
        }

        public void ToggleEngine() => SetEngine(!EngineOn);

        public void CutEngine() => EngineOn = false;

        public void SetThrottle(float throttle) => Throttle = RocketPhysics.ClampThrottle(throttle, MinThrottle);

        /// <summary>Move the throttle setpoint by <paramref name="axis"/>·rate·dt (axis in [-1, 1]).</summary>
        public void AdjustThrottle(float axis, float dt)
        {
            if (ControlsLocked) return;
            SetThrottle(Throttle + axis * config.throttleChangeRate * dt);
        }

        public void SetGimbal(Vector2 g) => Gimbal = ControlsLocked
            ? Vector2.zero
            : new Vector2(Mathf.Clamp(g.x, -1f, 1f), Mathf.Clamp(g.y, -1f, 1f));

        /// <summary>Steering input as a world-horizontal tilt direction (for the HUD indicator).</summary>
        public Vector3 SteerWorld => Rotation * new Vector3(Gimbal.x, 0f, Gimbal.y);

        public float GimbalTorqueAuthority => CurrentThrust * Mathf.Sin(config.gimbalMaxDeg * Mathf.Deg2Rad) * config.centerOfMassHeight;
        public float FinTorqueAuthority => config.gridFinTorquePerPa * RocketPhysics.DynamicPressure(config, Airspeed);
        /// <summary>Total control torque per axis at full input (gimbal + grid fins + RCS), N·m.</summary>
        public float ControlTorqueAuthority => GimbalTorqueAuthority + FinTorqueAuthority + config.rcsTorqueNm;

        /// <summary>
        /// Gimbal command as a world-horizontal tilt direction (x = east, z = north, magnitude up to ~1.41):
        /// the rocket tilts towards it. Converted into the two rocket-local gimbal axes, each clamped to ±1.
        /// </summary>
        public void SetGimbalWorld(Vector3 worldTilt)
        {
            Vector3 local = Quaternion.Inverse(Rotation) * new Vector3(worldTilt.x, 0f, worldTilt.z);
            SetGimbal(new Vector2(local.x, local.z));
        }

        /// <summary>Lowest point above whatever surface is below (pad or ground), via a downward raycast.
        /// The rocket itself is on the Ignore Raycast layer.</summary>
        public float RadarAltitude
        {
            get
            {
                Vector3 p = Position;
                if (Physics.Raycast(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, out RaycastHit hit, 50000f))
                    return Altitude + ReferenceHeight - hit.point.y;
                return Altitude;
            }
        }

        public void DeployLegs()
        {
            if (ControlsLocked || Exploded || Legs != LegsState.Stowed) return;
            Legs = LegsState.Deploying;
        }

        public void LockControls()
        {
            ControlsLocked = true;
            EngineOn = false;
            Gimbal = Vector2.zero;
        }

        /// <summary>Hard-impact visual: freeze the body and hide it (the session shows the fireball).</summary>
        public void Explode()
        {
            Exploded = true;
            LockControls();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            foreach (Renderer r in renderers) r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        /// <summary>Raised at the end of <see cref="ResetTo"/> (a new attempt starts).</summary>
        public event System.Action WasReset;

        /// <summary>Put the stage back into a fresh start state (no scene reload).</summary>
        public void ResetTo(Vector3 position, Vector3 velocity, float fuelKg)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            Exploded = false;
            ControlsLocked = false;
            EngineOn = false;
            Throttle = 1f;
            Gimbal = Vector2.zero;
            FuelKg = StartFuelKg = fuelKg;
            Legs = LegsState.Stowed;
            LegDeployProgress = 0f;
            FinDeployProgress = 0f;
            foreach (Renderer r in renderers) r.enabled = true;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
            foreach (RocketLeg leg in legs) leg.SetDeployment(0f);

            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            rb.position = position;
            rb.rotation = Quaternion.identity;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            UpdateMassProperties();

            WindForce = Vector3.zero;
            pendingContacts = default;
            Contacts = default;
            pendingVelocity = VelocityBeforeLastStep = velocity;
            RefreshVisuals();
            WasReset?.Invoke();
        }

        /// <summary>For editor tools (screenshots): set state without running physics.</summary>
        public void SetPreviewState(bool engineOn, float throttle, float legDeployment, float finDeployment = 1f)
        {
            FinDeployProgress = Mathf.Clamp01(finDeployment);
            EngineOn = engineOn;
            Throttle = RocketPhysics.ClampThrottle(throttle, MinThrottle);
            FuelKg = Mathf.Max(FuelKg, 1f);
            LegDeployProgress = legDeployment;
            Legs = legDeployment <= 0f ? LegsState.Stowed : legDeployment >= 1f ? LegsState.Deployed : LegsState.Deploying;
            foreach (RocketLeg leg in legs) leg.SetDeployment(legDeployment);
            RefreshVisuals();
        }

        // ---- simulation -------------------------------------------------------------------------

        void FixedUpdate()
        {
            // Publish what the previous physics step reported, then start collecting for the next one.
            Contacts = pendingContacts;
            pendingContacts = default;
            VelocityBeforeLastStep = pendingVelocity;
            pendingVelocity = rb.linearVelocity;

            if (Exploded) return;
            float dt = Time.fixedDeltaTime;

            StepLegs(dt);
            ApplyThrust(dt);
            UpdateMassProperties();

            // Aerodynamics act on the airspeed (velocity relative to the wind). Drag is split once into the end-on part
            // (vertical airspeed, frontal area) and the crossflow on the body side (horizontal airspeed, side area),
            // so a crosswind is counted exactly once.
            Vector3 v = rb.linearVelocity;
            Vector3 air = v - Wind;
            Vector3 drag = RocketPhysics.AxialDrag(config, air);
            Vector3 side = RocketPhysics.SideWindForce(config, v, Wind);
            rb.AddForce(drag + side, ForceMode.Force);
            rb.AddForce(RocketPhysics.BodyLift(config, Up, air), ForceMode.Force);
            rb.AddTorque(RocketPhysics.AeroStabilityTorque(config, Up, air), ForceMode.Force);
            // What the wind adds compared with still air (the axial drag does not depend on a horizontal wind).
            WindForce = side - RocketPhysics.SideWindForce(config, v, Vector3.zero);
            // Grid fins + RCS use the same steering input as the gimbal and work with the engine off.
            Vector3 control = RocketPhysics.AeroControlTorqueLocal(config, RocketPhysics.DynamicPressure(config, air), Gimbal);
            rb.AddTorque(Rotation * control, ForceMode.Force);
            ApplyFootGrip(v);
        }

        /// <summary>Damping rate of the sideways motion relative to the surface while the stage stands on the pad, 1/s.</summary>
        const float FootGripRate = 6f;
        /// <summary>Above this sideways speed relative to the surface the grip does not act (a sliding crash stays one), m/s.</summary>
        const float FootGripMaxSpeed = 1.5f;

        /// <summary>
        /// Standing on the pad or deck with the engine off (three or more feet on it), the feet grip the surface like
        /// hold-down clamps: the sideways motion relative to the surface is damped, so wind gusts and a pitching deck
        /// do not walk the landed stage across it.
        /// </summary>
        void ApplyFootGrip(Vector3 v)
        {
            if (EngineOn || Contacts.LegsOnPad < 3) return;
            Vector3 rel = v - ReferenceVelocity;
            rel.y = 0f;
            if (rel.sqrMagnitude > FootGripMaxSpeed * FootGripMaxSpeed) return;
            rb.AddForce(-rel * FootGripRate, ForceMode.Acceleration);
        }

        void StepLegs(float dt)
        {
            if (Legs != LegsState.Deploying) return;
            LegDeployProgress = Mathf.MoveTowards(LegDeployProgress, 1f, dt / Mathf.Max(0.01f, config.legDeployTime));
            foreach (RocketLeg leg in legs) leg.SetDeployment(LegDeployProgress);
            if (LegDeployProgress >= 1f) Legs = LegsState.Deployed;
        }

        void ApplyThrust(float dt)
        {
            if (!EngineOn) return;
            float thrust = RocketPhysics.Thrust(config, true, Throttle, MinThrottle);
            float burn = RocketPhysics.BurnRate(config, true, Throttle, MinThrottle) * dt;
            if (burn >= FuelKg)
            {
                thrust *= FuelKg / burn; // partial last step, then flame-out
                FuelKg = 0f;
                EngineOn = false;
            }
            else
            {
                FuelKg -= burn;
            }
            Vector3 dir = Rotation * RocketPhysics.ThrustDirectionLocal(config, Gimbal);
            rb.AddForceAtPosition(dir * thrust, EnginePositionWorld, ForceMode.Force);
        }

        void OnCollisionEnter(Collision c) => RecordContacts(c);
        void OnCollisionStay(Collision c) => RecordContacts(c);

        void RecordContacts(Collision c)
        {
            LandingSurface surface = c.collider.GetComponentInParent<LandingSurface>();
            bool onPad = surface != null && surface.isPad;
            for (int i = 0; i < c.contactCount; i++)
            {
                Collider mine = c.GetContact(i).thisCollider;
                int leg = LegIndexOf(mine);
                if (leg < 0)
                    pendingContacts.body = true;
                else if (onPad)
                    pendingContacts.legsOnPadMask |= 1 << leg;
                else
                    pendingContacts.legsOnGroundMask |= 1 << leg;
            }
        }

        int LegIndexOf(Collider col)
        {
            for (int i = 0; i < legs.Length; i++)
                if (legs[i].FootCollider == col) return i;
            return -1;
        }

        // ---- visuals ----------------------------------------------------------------------------

        void Update()
        {
            if (!Exploded && (Velocity.y < -2f || Velocity.sqrMagnitude > 50f * 50f))
                FinDeployProgress = Mathf.MoveTowards(FinDeployProgress, 1f, Time.deltaTime / Mathf.Max(0.01f, finDeployTime));
            RefreshVisuals();
        }

        public void RefreshVisuals()
        {
            if (gridFins != null) gridFins.SetPose(FinDeployProgress, Gimbal);
        }
    }
}
