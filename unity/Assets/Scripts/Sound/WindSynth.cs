using UnityEngine;

namespace RocketSlam.Sound
{
    /// <summary>
    /// Generates a seamless wind loop: low-passed white noise with a band-passed "whistle" layer and slow gusts.
    /// The tail is cross-faded into the head so the loop has no seam; gust modulation is periodic over the clip.
    /// Deterministic (fixed seed), created once at start-up.
    /// </summary>
    public static class WindSynth
    {
        public const int SampleRate = 44100;

        /// <summary>Raw loop samples in [-0.8, 0.8] (EditMode tests check the seam).</summary>
        public static float[] Generate(float seconds = 4f, int seed = 4242)
        {
            int n = Mathf.Max(1024, Mathf.RoundToInt(seconds * SampleRate));
            int fade = SampleRate / 2;
            var raw = new float[n + fade];
            var rng = new System.Random(seed);

            // Two cascaded one-pole low-passes (~350 Hz) = rumble; a 1.2 kHz band = hiss/whistle.
            float lp1 = 0f, lp2 = 0f, bpLow = 0f, bpHigh = 0f;
            float aLow = OnePole(350f), aBandHi = OnePole(1600f), aBandLo = OnePole(700f);
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp1 += aLow * (white - lp1);
                lp2 += aLow * (lp1 - lp2);
                bpHigh += aBandHi * (white - bpHigh);
                bpLow += aBandLo * (white - bpLow);
                raw[i] = lp2 * 3.2f + (bpHigh - bpLow) * 0.9f;
            }

            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float s = raw[i];
                if (i < fade)
                {
                    float w = (float)i / fade; // equal-power cross-fade of the tail into the head
                    s = raw[i] * Mathf.Sin(w * Mathf.PI * 0.5f) + raw[n + i] * Mathf.Cos(w * Mathf.PI * 0.5f);
                }
                float phase = (float)i / n * Mathf.PI * 2f;
                float gust = 0.72f + 0.18f * Mathf.Sin(phase * 2f) + 0.1f * Mathf.Sin(phase * 5f + 1.3f);
                samples[i] = s * gust;
            }

            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            float gain = peak > 0f ? 0.8f / peak : 1f;
            for (int i = 0; i < n; i++) samples[i] *= gain;
            return samples;
        }

        public static AudioClip CreateClip(float seconds = 4f)
        {
            float[] samples = Generate(seconds);
            AudioClip clip = AudioClip.Create("WindLoop", samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static float OnePole(float cutoffHz) => 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / SampleRate);
    }
}
