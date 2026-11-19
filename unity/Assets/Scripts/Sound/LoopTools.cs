using UnityEngine;

namespace RocketSlam.Sound
{
    /// <summary>
    /// Makes a looping clip seamless: the last <c>fade</c> frames are cross-faded (equal power) into the first ones
    /// and dropped, so the waveform runs continuously across the wrap point (no click).
    /// </summary>
    public static class LoopTools
    {
        /// <summary>Interleaved samples in, shorter interleaved samples out (frames - fadeFrames).</summary>
        public static float[] CrossfadeLoop(float[] data, int channels, int fadeFrames)
        {
            int frames = data.Length / channels;
            fadeFrames = Mathf.Clamp(fadeFrames, 1, frames / 2);
            int outFrames = frames - fadeFrames;
            var result = new float[outFrames * channels];
            System.Array.Copy(data, 0, result, 0, result.Length);
            for (int i = 0; i < fadeFrames; i++)
            {
                float w = (float)i / fadeFrames;
                float fadeIn = Mathf.Sin(w * Mathf.PI * 0.5f), fadeOut = Mathf.Cos(w * Mathf.PI * 0.5f);
                for (int c = 0; c < channels; c++)
                    result[i * channels + c] = data[i * channels + c] * fadeIn + data[(outFrames + i) * channels + c] * fadeOut;
            }
            return result;
        }

        /// <summary>A seamless copy of <paramref name="clip"/> (decompressed clips only); the original if its samples
        /// cannot be read.</summary>
        public static AudioClip MakeSeamless(AudioClip clip, float fadeSeconds = 0.25f)
        {
            if (clip == null) return null;
            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return clip;
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return clip;
            float[] looped = CrossfadeLoop(data, clip.channels, Mathf.RoundToInt(fadeSeconds * clip.frequency));
            AudioClip result = AudioClip.Create(clip.name + "_seamless", looped.Length / clip.channels, clip.channels, clip.frequency, false);
            result.SetData(looped, 0);
            return result;
        }
    }
}
