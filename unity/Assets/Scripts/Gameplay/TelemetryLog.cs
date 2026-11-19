using System.Collections.Generic;
using UnityEngine;

namespace RocketSlam.Gameplay
{
    /// <summary>One telemetry point of an attempt.</summary>
    public struct TelemetrySample
    {
        /// <summary>Seconds since the attempt started.</summary>
        public float time;
        /// <summary>Altitude above the landing surface, m.</summary>
        public float altitude;
        /// <summary>Vertical speed relative to the landing surface, m/s (negative = falling).</summary>
        public float verticalSpeed;
        /// <summary>Engine thrust as a fraction of the maximum, 0..1.</summary>
        public float thrust;
    }

    /// <summary>
    /// Fixed-rate flight recording for the results graph (pure, EditMode-tested). <see cref="Record"/> keeps one sample
    /// per <see cref="Interval"/>; when <see cref="Capacity"/> is reached every other sample is dropped and the
    /// interval doubles, so any flight length fits.
    /// </summary>
    public class TelemetryLog
    {
        public const float DefaultInterval = 0.1f; // 10 Hz
        readonly List<TelemetrySample> samples = new List<TelemetrySample>();
        float nextTime;

        public int Capacity { get; }
        public float Interval { get; private set; }
        public int Count => samples.Count;
        public IReadOnlyList<TelemetrySample> Samples => samples;
        public TelemetrySample this[int i] => samples[i];
        public float Duration => samples.Count > 0 ? samples[samples.Count - 1].time : 0f;

        public TelemetryLog(int capacity = 1200, float interval = DefaultInterval)
        {
            Capacity = Mathf.Max(4, capacity);
            Interval = interval;
        }

        public void Clear()
        {
            samples.Clear();
            nextTime = 0f;
            Interval = DefaultInterval;
        }

        /// <summary>Add a sample if it is due (time ≥ the next slot) or <paramref name="force"/> (the final point).</summary>
        public bool Record(in TelemetrySample s, bool force = false)
        {
            if (!force && s.time + 1e-4f < nextTime) return false;
            if (samples.Count >= Capacity) Decimate();
            samples.Add(s);
            nextTime = s.time + Interval;
            return true;
        }

        void Decimate()
        {
            int w = 0;
            for (int r = 0; r < samples.Count; r += 2) samples[w++] = samples[r];
            samples.RemoveRange(w, samples.Count - w);
            Interval *= 2f;
        }

        public float MaxAltitude()
        {
            float m = 0f;
            foreach (TelemetrySample s in samples) m = Mathf.Max(m, s.altitude);
            return m;
        }

        /// <summary>Fastest fall (positive m/s).</summary>
        public float MaxFallSpeed()
        {
            float m = 0f;
            foreach (TelemetrySample s in samples) m = Mathf.Max(m, -s.verticalSpeed);
            return m;
        }
    }
}
