using UnityEngine;

namespace RocketSlam.Menus
{
    /// <summary>Main-menu backdrop: the camera circles slowly around the landed booster on the pad, framed to the right
    /// of the menu panel, with a gentle bob in height.</summary>
    public class MenuBackdrop : MonoBehaviour
    {
        [SerializeField] Transform focus;
        [SerializeField] float radius = 62f;
        [SerializeField] float height = 14f;
        [SerializeField] float lookHeight = 24f;
        [SerializeField] float degreesPerSecond = 3.5f;
        [SerializeField] float startAngle = 200f;
        [Tooltip("Sideways shift of the look point (m) so the booster sits right of the left-hand menu panel.")]
        [SerializeField] float lookSideOffset = -16f;

        public void Setup(Transform target, float orbitRadius, float orbitHeight, float lookAtHeight, float orbitStartDeg)
        {
            startAngle = orbitStartDeg;
            focus = target;
            radius = orbitRadius;
            height = orbitHeight;
            lookHeight = lookAtHeight;
        }

        void LateUpdate() => Apply(Time.unscaledTime);

        public void Apply(float t)
        {
            Vector3 c = focus != null ? focus.position : Vector3.zero;
            float a = (startAngle + t * degreesPerSecond) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius;
            Vector3 pos = c + offset + Vector3.up * (height + Mathf.Sin(t * 0.21f) * 2.5f);
            Vector3 look = c + Vector3.up * lookHeight;
            Vector3 right = Vector3.Cross(Vector3.up, (look - pos).normalized).normalized;
            look += right * lookSideOffset;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }
    }
}
