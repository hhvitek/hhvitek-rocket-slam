using RocketSlam.Cameras;
using RocketSlam.Rocket;
using UnityEngine;

namespace RocketSlam.Hud
{
    /// <summary>
    /// Depth cue: a vertical drop line from the rocket to the surface below it and a dark puck where it
    /// meets the surface. The line gets thicker with camera distance so it stays visible from far away.
    /// </summary>
    public class GroundCue : MonoBehaviour
    {
        [SerializeField] RocketController rocket;
        [SerializeField] CameraDirector cameras;
        [SerializeField] Transform line;
        [SerializeField] Transform puck;

        public void Setup(RocketController r, CameraDirector c, Transform dropLine, Transform shadowPuck)
        {
            rocket = r;
            cameras = c;
            line = dropLine;
            puck = shadowPuck;
        }

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (rocket == null) return;
            Vector3 p = rocket.transform.position;
            bool hit = Physics.Raycast(new Vector3(p.x, p.y + 1f, p.z), Vector3.down, out RaycastHit info, 50000f);
            bool show = hit && !rocket.Exploded && p.y - info.point.y > 3f;
            // The engine cam sits right next to the line, where it would fill the screen.
            line.gameObject.SetActive(show && (cameras == null || cameras.View != CameraView.Engine));
            puck.gameObject.SetActive(show);
            if (!show) return;

            float top = p.y - 1.2f;
            float bottom = info.point.y;
            Vector3 mid = new Vector3(p.x, (top + bottom) * 0.5f, p.z);
            float camDist = cameras != null && cameras.ActiveCamera != null
                ? Vector3.Distance(cameras.ActiveCamera.transform.position, mid)
                : 100f;
            float width = Mathf.Clamp(camDist * 0.0012f, 0.15f, 3f);
            line.position = mid;
            line.localScale = new Vector3(width, Mathf.Max(0.01f, (top - bottom) * 0.5f), width);

            float puckSize = Mathf.Max(8f, camDist * 0.01f);
            puck.position = new Vector3(p.x, bottom + 0.6f, p.z);
            puck.localScale = new Vector3(puckSize, 0.6f, puckSize);
        }
    }
}
