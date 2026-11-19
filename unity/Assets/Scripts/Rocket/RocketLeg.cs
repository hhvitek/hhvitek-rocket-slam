using UnityEngine;

namespace RocketSlam.Rocket
{
    /// <summary>
    /// One landing leg: a hinge at the bottom of the body that swings from stowed (pointing up along the
    /// body) down and out to deployed. The foot is a child of the leg, keeps the rocket's orientation and
    /// carries the only leg collider. Lives on the hinge transform (the model's <c>Leg_i</c> node); set up by SceneBuilder.
    /// </summary>
    public class RocketLeg : MonoBehaviour
    {
        [SerializeField] Transform foot;
        [SerializeField] Collider footCollider;
        [SerializeField] Quaternion stowedRotation = Quaternion.identity;
        [Tooltip("Hinge axis in the parent's (rocket's) space; positive rotation swings the leg's up towards outward.")]
        [SerializeField] Vector3 deployAxis = Vector3.right;
        [SerializeField] float deployAngleDeg = 111.94f;
        [Tooltip("Foot orientation relative to the leg's parent; kept while the leg swings (pads stay level).")]
        [SerializeField] Quaternion footRotationInParent = Quaternion.identity;

        public Collider FootCollider => footCollider;
        public Vector3 FootPosition => foot.position;
        public Transform Foot => foot;

        /// <summary>Hinge axis for a part whose deployed direction is <paramref name="outward"/> (parent space):
        /// rotating about it by a positive angle swings the part's up vector towards outward.</summary>
        public static Vector3 DeployAxisFor(Vector3 outward)
        {
            outward.y = 0f;
            return Vector3.Cross(Vector3.up, outward.normalized);
        }

        /// <summary>Call with the leg in its stowed pose and the foot already parented under it.</summary>
        public void Setup(Transform footTransform, Collider collider, Vector3 axisInParent, float angleDeg)
        {
            foot = footTransform;
            footCollider = collider;
            stowedRotation = transform.localRotation;
            deployAxis = axisInParent.normalized;
            deployAngleDeg = angleDeg;
            footRotationInParent = transform.localRotation * foot.localRotation;
        }

        /// <summary>Pose the leg: 0 = stowed, 1 = deployed. The foot collider is active once deployment starts.</summary>
        public void SetDeployment(float t)
        {
            t = Mathf.Clamp01(t);
            Quaternion swing = Quaternion.AngleAxis(deployAngleDeg * Mathf.SmoothStep(0f, 1f, t), deployAxis);
            transform.localRotation = swing * stowedRotation;
            foot.localRotation = Quaternion.Inverse(transform.localRotation) * footRotationInParent;
            footCollider.enabled = t > 0f;
        }
    }
}
