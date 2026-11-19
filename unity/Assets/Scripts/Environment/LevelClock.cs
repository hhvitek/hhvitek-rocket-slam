using UnityEngine;

namespace RocketSlam.World
{
    /// <summary>
    /// The one time base of the level's moving conditions (sea waves, droneship motion, wind gusts). It restarts at 0
    /// with every attempt (<see cref="Restart"/>), so the deck and the gusts replay identically on each try.
    /// <para><see cref="Physics"/> is the time of the current physics step (ship pose, wind, deck velocity).
    /// <see cref="Render"/> is the same clock at the rendered frame: the droneship is an interpolated kinematic
    /// Rigidbody whose pose after the step at <c>t</c> is the pose for <c>t + dt</c>, so interpolation shows it at
    /// exactly <see cref="Render"/>, and the visible ocean is displaced at that same time: hull and waves match.</para>
    /// </summary>
    public static class LevelClock
    {
        static float epoch;

        /// <summary>Level time of the current physics step, s (0 outside play mode).</summary>
        public static float Physics => Application.isPlaying ? Time.fixedTime - epoch : 0f;

        /// <summary>Level time of the rendered frame, s (0 outside play mode).</summary>
        public static float Render => Application.isPlaying ? Time.time - epoch : 0f;

        /// <summary>Statics survive play-mode entry when domain reload is off: start clean every time.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => epoch = 0f;

        /// <summary>Start a new attempt: level time 0 is now.</summary>
        public static void Restart() => epoch = Application.isPlaying ? Time.fixedTime : 0f;
    }
}
