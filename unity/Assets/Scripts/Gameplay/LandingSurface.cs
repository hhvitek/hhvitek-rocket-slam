using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>Marks a collider as a landing surface. Colliders without it count as plain ground.</summary>
    public class LandingSurface : MonoBehaviour
    {
        [Tooltip("True for the landing pad / ship deck, false for ground or sea.")]
        public bool isPad;
    }
}
