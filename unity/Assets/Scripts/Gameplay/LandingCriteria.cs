using System;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>Thresholds that decide success or failure. Stored inside <c>RocketConfig</c>.</summary>
    [Serializable]
    public class LandingCriteria
    {
        [Tooltip("Max vertical speed at touchdown, m/s. Faster = hard impact (explosion).")]
        public float maxVerticalSpeed = 2.5f;
        [Tooltip("Max horizontal speed at touchdown, m/s. Faster = hard impact (explosion).")]
        public float maxHorizontalSpeed = 1.5f;
        [Tooltip("Max tilt from vertical at touchdown, degrees.")]
        public float maxTiltDeg = 6f;
        [Tooltip("Tilt that counts as tipped over while settling, degrees.")]
        public float tipOverTiltDeg = 25f;
        [Tooltip("Seconds the stage must stay upright on the pad after touchdown.")]
        public float settleTime = 3f;
    }
}
