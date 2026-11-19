using NUnit.Framework;
using UnityEditor;
using RocketSlam.Sound;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    /// <summary>The engine loops (Kenney clips after <see cref="LoopTools.MakeSeamless"/>) wrap without a click.</summary>
    public class AudioAssetTests
    {
        [TestCase("Assets/ThirdParty/Kenney/Audio/Sci-Fi/engine_loop.ogg")]
        [TestCase("Assets/ThirdParty/Kenney/Audio/Sci-Fi/engine_low_loop.ogg")]
        public void EngineLoopWrapsWithoutAClick(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Assert.IsNotNull(clip, path);
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) Assert.Inconclusive("audio data not readable here");

            int ch = clip.channels;
            data = LoopTools.CrossfadeLoop(data, ch, clip.frequency / 4); // what GameAudio plays
            int n = data.Length / ch;
            var steps = new float[n - 1];
            for (int i = 1; i < n; i++) steps[i - 1] = Mathf.Abs(data[i * ch] - data[(i - 1) * ch]);
            System.Array.Sort(steps);
            float p99 = steps[(int)(steps.Length * 0.99f)];
            float wrap = Mathf.Abs(data[0] - data[(n - 1) * ch]);
            TestContext.Out.WriteLine($"{path}: wrap step {wrap:0.0000}, p99 step {p99:0.0000}, max {steps[steps.Length - 1]:0.0000}");
            Assert.LessOrEqual(wrap, p99 * 1.5f, "loop seam jump larger than normal waveform steps");
        }
    }
}
