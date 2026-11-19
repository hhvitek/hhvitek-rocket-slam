using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// Visual-only grid fins (their control torque lives in <see cref="RocketPhysics.AeroControlTorqueLocal"/>).
    /// Each fin hinges at the top of the body and swings 90° out; once deployed it twists about its radial
    /// axis with the steering input. Lives on the rocket root; set up by SceneBuilder from the model's <c>GridFin_i</c> nodes.
    /// </summary>
    public class RocketGridFins : MonoBehaviour
    {
        [SerializeField] Transform[] fins = new Transform[0];
        [SerializeField] Vector3[] outward = new Vector3[0];
        [SerializeField] float deployAngleDeg = 90f;
        [SerializeField] float maxDeflectionDeg = 18f;

        public int Count => fins.Length;
        public Transform GetFin(int i) => fins[i];

        public void Setup(Transform[] finTransforms, float angleDeg)
        {
            fins = finTransforms;
            deployAngleDeg = angleDeg;
            outward = new Vector3[fins.Length];
            for (int i = 0; i < fins.Length; i++)
            {
                Vector3 p = fins[i].localPosition;
                outward[i] = new Vector3(p.x, 0f, p.z).normalized;
            }
        }

        /// <summary>0 = stowed along the body, 1 = deployed; <paramref name="steer"/> is the gimbal/steering input.</summary>
        public void SetPose(float t, Vector2 steer)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            for (int i = 0; i < fins.Length; i++)
            {
                Vector3 o = outward[i];
                Quaternion deploy = Quaternion.AngleAxis(deployAngleDeg * s, RocketLeg.DeployAxisFor(o));
                // Fins on the ±z sides push the top along x (input.x); fins on ±x push along z (input.y).
                float input = Mathf.Abs(o.z) > Mathf.Abs(o.x) ? steer.x * Mathf.Sign(o.z) : -steer.y * Mathf.Sign(o.x);
                Quaternion twist = Quaternion.AngleAxis(maxDeflectionDeg * s * Mathf.Clamp(input, -1f, 1f), o);
                fins[i].localRotation = twist * deploy;
            }
        }
    }
}
