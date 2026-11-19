using RocketSlam.Progress;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Cameras
{
    public enum CameraView { Chase, Engine, PadSide }

    /// <summary>
    /// One physical camera driven by three virtual views (C cycles them with a short cross-blend):
    /// <list type="bullet">
    /// <item><b>Chase</b> (default): behind and above the rocket on the pad→rocket line, aimed between rocket
    /// and pad. Height/distance follow an altitude curve; if rocket and pad cannot both fit, the camera rises
    /// until they do (pad kept in the central 70 % of the frame). Offset, yaw and pitch are smoothed separately;
    /// world-up locked, no roll, heading independent of the rocket's own rotation.</item>
    /// <item><b>Engine</b>: on the lower body, looking down past the engine to the pad, wide lens, roll-stabilised.</item>
    /// <item><b>PadSide</b>: fixed on the ground ~200 m from the pad, tracking the rocket with a zoom lens.</item>
    /// </list>
    /// Follows the interpolated rocket transform (visuals), not the physics pose.
    /// </summary>
    public class CameraDirector : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [SerializeField] Camera cam;
        [SerializeField] Transform padTarget;
        [SerializeField] Vector3 padSideOffset = new Vector3(-170f, 6f, 110f);
        [SerializeField] float blendTime = 0.5f;
        [SerializeField] float focusHeight = 18f;
        [SerializeField] float chaseFov = 55f;
        [SerializeField] float engineFov = 80f;
        [SerializeField] CameraShake shake = new CameraShake();

        struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;
            public float fov;

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                position = Vector3.Lerp(a.position, b.position, t),
                rotation = Quaternion.Slerp(a.rotation, b.rotation, t),
                fov = Mathf.Lerp(a.fov, b.fov, t),
            };
        }

        bool chaseReady;
        Vector3 heading;            // horizontal unit vector pad -> rocket (camera sits further along it)
        Vector3 chaseOffset, chaseOffsetVel;
        float chaseYaw, chaseYawVel, chasePitch, chasePitchVel;
        Pose lastChase;
        CameraView previousView;
        float blendStart = -100f;

        public CameraView View { get; private set; } = CameraView.Chase;
        public Camera ActiveCamera => cam;
        /// <summary>Explosion kicks and engine rumble; applied on top of the blended view.</summary>
        public CameraShake Shake => shake ??= new CameraShake();
        /// <summary>Unscaled time of the last view change (HUD shows the camera name for a moment).</summary>
        public float ViewChangedAt { get; private set; } = -100f;

        public static string DisplayName(CameraView v)
        {
            switch (v)
            {
                case CameraView.Engine: return "ENGINE CAM";
                case CameraView.PadSide: return "PAD-SIDE CAM";
                default: return "CHASE CAM";
            }
        }

        public void Setup(RocketController r, Camera camera, Transform target)
        {
            rocket = r;
            cam = camera;
            padTarget = target;
        }

        /// <summary>The default view from the settings (the player can still cycle with C).</summary>
        void Awake() => View = (CameraView)Mathf.Clamp(SaveGame.Settings.cameraView, 0, 2);

        Vector3 PadPoint => padTarget != null ? padTarget.position : Vector3.zero;
        Vector3 padSideAnchor;
        bool padSideAnchored;

        public void Toggle() => SetView((CameraView)(((int)View + 1) % 3), blend: true);

        public void SetView(CameraView view, bool blend)
        {
            if (view == View) return;
            previousView = View;
            View = view;
            blendStart = blend ? Time.unscaledTime : -100f;
            ViewChangedAt = Time.unscaledTime;
        }

        void LateUpdate() => Apply(Time.deltaTime, smooth: true);

        /// <summary>Jump straight to the current view's target pose (restart, editor screenshots).</summary>
        public void Snap()
        {
            chaseReady = false;
            blendStart = -100f;
            Shake.Reset();
            Apply(0f, smooth: false);
        }

        void Apply(float dt, bool smooth)
        {
            if (rocket == null || cam == null) return;
            lastChase = ChasePose(dt, smooth);
            Pose pose = PoseOf(View);
            float t = (Time.unscaledTime - blendStart) / blendTime;
            if (t < 1f)
                pose = Pose.Lerp(PoseOf(previousView), pose, Mathf.SmoothStep(0f, 1f, t));
            if (smooth)
            {
                Shake.Step(dt, out Vector3 offset, out Quaternion wobble);
                pose.position += pose.rotation * offset;
                pose.rotation *= wobble;
            }
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = pose.fov;
        }

        Pose PoseOf(CameraView v)
        {
            switch (v)
            {
                case CameraView.Engine: return EnginePose();
                case CameraView.PadSide: return PadSidePose();
                default: return lastChase;
            }
        }

        // ---- chase ------------------------------------------------------------------------------

        /// <summary>Piecewise-linear altitude curve through (0, a), (200, b), (1500+, c).</summary>
        static float Curve(float h, float a, float b, float c)
            => h < 200f ? Mathf.Lerp(a, b, h / 200f) : Mathf.Lerp(b, c, (h - 200f) / 1300f);

        Pose ChasePose(float dt, bool smooth)
        {
            Vector3 r = rocket.transform.position;
            Vector3 pad = PadPoint;
            Vector3 focus = r + Vector3.up * focusHeight;
            float h = Mathf.Max(0f, r.y - pad.y);

            Vector3 d = r - pad;
            d.y = 0f;
            float dist = d.magnitude;
            if (!chaseReady || !smooth)
                heading = dist > 1f ? d / dist : Vector3.back;
            else if (dist > 1f)
            {
                // Follow the pad->rocket bearing, but barely when the rocket is right above the pad.
                float w = Mathf.Clamp01((dist - 3f) / 40f) * (1f - Mathf.Exp(-dt / 0.8f));
                heading = Vector3.Slerp(heading, d / dist, w);
                heading.y = 0f;
                heading.Normalize();
            }

            float back = Curve(h, 40f, 65f, 110f);
            float above = Curve(h, 12f, 45f, 80f); // above the rocket base
            float halfV = chaseFov * 0.5f * Mathf.Deg2Rad;
            float padLimit = Mathf.Atan(0.7f * Mathf.Tan(halfV)) * Mathf.Rad2Deg;     // central 70 %
            float rocketLimit = Mathf.Atan(0.65f * Mathf.Tan(halfV)) * Mathf.Rad2Deg; // room for the whole 40 m body

            // Rise until rocket and pad fit into the frame together.
            Vector3 pos = r + heading * back + Vector3.up * above;
            for (int i = 0; i < 30 && Vector3.Angle(focus - pos, pad - pos) > padLimit + rocketLimit; i++)
            {
                above = above * 1.15f + 5f;
                pos = r + heading * back + Vector3.up * above;
            }

            Vector3 toRocket = (focus - pos).normalized;
            Vector3 toPad = (pad - pos).normalized;
            Vector3 aim = Vector3.Slerp(toRocket, toPad, 0.3f);
            float padErr = Vector3.Angle(aim, toPad) - padLimit;
            if (padErr > 0f) aim = Vector3.RotateTowards(aim, toPad, padErr * Mathf.Deg2Rad, 0f);
            float rocketErr = Vector3.Angle(aim, toRocket) - rocketLimit;
            if (rocketErr > 0f) aim = Vector3.RotateTowards(aim, toRocket, rocketErr * Mathf.Deg2Rad, 0f);

            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 offset = pos - r;

            if (!chaseReady || !smooth || dt <= 0f)
            {
                chaseOffset = offset;
                chaseYaw = yaw;
                chasePitch = pitch;
                chaseOffsetVel = Vector3.zero;
                chaseYawVel = chasePitchVel = 0f;
                chaseReady = true;
            }
            else
            {
                chaseOffset = Vector3.SmoothDamp(chaseOffset, offset, ref chaseOffsetVel, 0.5f, Mathf.Infinity, dt);
                chaseYaw = Mathf.SmoothDampAngle(chaseYaw, yaw, ref chaseYawVel, 0.5f, Mathf.Infinity, dt);
                chasePitch = Mathf.SmoothDampAngle(chasePitch, pitch, ref chasePitchVel, 1.0f, Mathf.Infinity, dt);
            }

            Vector3 camPos = r + chaseOffset;
            camPos.y = Mathf.Max(camPos.y, rocket.ReferenceHeight + 3f);
            return new Pose { position = camPos, rotation = Quaternion.Euler(chasePitch, chaseYaw, 0f), fov = chaseFov };
        }

        // ---- engine & pad-side ------------------------------------------------------------------

        /// <summary>On the pad side of the lower body (5.5 m out from the axis, 4 m up), looking towards the pad and
        /// down: the body is behind the camera, only the engine section and the flame show at the bottom edge.</summary>
        Pose EnginePose()
        {
            Transform t = rocket.transform;
            Vector3 pos = t.TransformPoint(0f, 4f, 0f) - heading * 5.5f;
            Vector3 toPad = PadPoint - pos;
            Vector3 flat = -heading;
            float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(-toPad.y, new Vector2(toPad.x, toPad.z).magnitude) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, 45f, 89f);
            return new Pose { position = pos, rotation = Quaternion.Euler(pitch, yaw, 0f), fov = engineFov };
        }

        Pose PadSidePose()
        {
            // Anchored to where the pad was at load: a camera on a buoy, not one riding the heaving deck.
            if (!padSideAnchored) { padSideAnchor = PadPoint; padSideAnchored = true; }
            Vector3 pos = padSideAnchor + padSideOffset;
            Vector3 focus = rocket.transform.position + Vector3.up * focusHeight;
            Vector3 dir = focus - pos;
            // Zoom lens: keep about 70 m of scene height in frame.
            float fov = Mathf.Clamp(2f * Mathf.Atan(35f / Mathf.Max(1f, dir.magnitude)) * Mathf.Rad2Deg, 2f, 60f);
            return new Pose { position = pos, rotation = Quaternion.LookRotation(dir, Vector3.up), fov = fov };
        }
    }
}
