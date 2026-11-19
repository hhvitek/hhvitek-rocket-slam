using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RocketSlam.World
{
    /// <summary>
    /// Scales the main-light shadow distance with how far the camera sees the ground: tight, sharp cascades
    /// around the rocket near the pad, long ones at 3 km so buildings and forests still cast shadows seen from the
    /// top of the fall. Writes the active URP asset at runtime and restores it on disable (so editor play mode
    /// does not leave the asset changed).
    /// </summary>
    public class AdaptiveShadows : MonoBehaviour
    {
        [SerializeField] float minDistance = 220f;
        [SerializeField] float maxDistance = 4200f;
        [Tooltip("Shadow distance per metre of camera height, on top of minDistance.")]
        [SerializeField] float perMetre = 1.25f;

        UniversalRenderPipelineAsset asset;
        float savedDistance;
        Vector3 savedSplits;
        int savedCascades;

        public float CurrentDistance { get; private set; }

        void OnEnable() => Capture();

        void OnDisable() => Release();

        /// <summary>Take over the active URP asset (again after the graphics quality changed). Two-cascade assets (Low
        /// quality) keep two cascades and get a shorter range.</summary>
        void Capture()
        {
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            savedDistance = asset.shadowDistance;
            savedSplits = asset.cascade4Split;
            savedCascades = asset.shadowCascadeCount;
            if (savedCascades >= 3)
            {
                asset.shadowCascadeCount = 4;
                // Fractions of the shadow distance: first cascade covers the rocket and pad, the last the far ground.
                asset.cascade4Split = new Vector3(0.06f, 0.18f, 0.45f);
            }
        }

        void Release()
        {
            if (asset == null) return;
            asset.shadowDistance = savedDistance;
            asset.cascade4Split = savedSplits;
            asset.shadowCascadeCount = savedCascades;
            asset = null;
        }

        void LateUpdate()
        {
            if (!ReferenceEquals(GraphicsSettings.currentRenderPipeline, asset))
            {
                Release();
                Capture();
            }
            if (asset == null) return;
            // Height above the ground plus, for cameras looking down at an angle, the distance to where the view
            // centre meets the ground (aerial / establishing views see the ground far ahead).
            float h = Mathf.Max(0f, transform.position.y);
            float down = -transform.forward.y;
            float focus = down > 0.02f ? h / down : 0f;
            CurrentDistance = Mathf.Clamp(Mathf.Max(minDistance + h * perMetre, focus * 1.4f + 150f), minDistance, maxDistance);
            if (savedCascades < 3) CurrentDistance = Mathf.Min(CurrentDistance * 0.6f, maxDistance * 0.5f);
            asset.shadowDistance = CurrentDistance;
        }
    }
}
