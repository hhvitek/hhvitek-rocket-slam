using NUnit.Framework;
using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Sound;
using UnityEngine;

namespace RocketSlam.Tests.EditMode
{
    public class EffectsMathTests
    {
        [Test]
        public void FlameGrowsWithThrustAndVanishesWhenOff()
        {
            Assert.AreEqual(0f, EffectsMath.FlameLength(0f));
            Assert.AreEqual(0f, EffectsMath.FlameBrightness(0f));
            Assert.AreEqual(EffectsMath.MinFlameLength, EffectsMath.FlameLength(0.0001f), 0.01f);
            Assert.AreEqual(EffectsMath.MaxFlameLength, EffectsMath.FlameLength(1f), 1e-4f);
            Assert.AreEqual(EffectsMath.MaxFlameLength, EffectsMath.FlameLength(3f), 1e-4f, "clamped");
            float last = 0f;
            for (float f = 0.1f; f <= 1f; f += 0.1f)
            {
                Assert.Greater(EffectsMath.FlameLength(f), last);
                last = EffectsMath.FlameLength(f);
            }
            Assert.Less(EffectsMath.FlameBrightness(0.2f), EffectsMath.FlameBrightness(1f));
            Assert.AreEqual(1f, EffectsMath.PlumeExpansion(0f), 1e-5f);
            Assert.Greater(EffectsMath.PlumeExpansion(200f), EffectsMath.PlumeExpansion(20f));
            Assert.AreEqual(1f, EffectsMath.FlameBrightness(1f), 1e-5f);
        }

        [Test]
        public void ThrustFractionIsClamped()
        {
            Assert.AreEqual(0.5f, EffectsMath.ThrustFraction(425000f, 850000f), 1e-5f);
            Assert.AreEqual(1f, EffectsMath.ThrustFraction(2e6f, 850000f));
            Assert.AreEqual(0f, EffectsMath.ThrustFraction(1000f, 0f));
        }

        [Test]
        public void GroundDustOnlyLowAndWithThrust()
        {
            Assert.AreEqual(0f, EffectsMath.GroundDust(1f, EffectsMath.DustMaxHeight));
            Assert.AreEqual(0f, EffectsMath.GroundDust(1f, 500f));
            Assert.AreEqual(0f, EffectsMath.GroundDust(0f, 5f));
            Assert.AreEqual(1f, EffectsMath.GroundDust(1f, 0f), 1e-5f);
            Assert.Greater(EffectsMath.GroundDust(1f, 10f), EffectsMath.GroundDust(1f, 40f), "stronger nearer the ground");
            Assert.Greater(EffectsMath.GroundDust(1f, 20f), EffectsMath.GroundDust(0.3f, 20f), "stronger with more thrust");
        }

        [Test]
        public void RumbleOnlyNearTheGroundNeverDuringHighDescent()
        {
            Assert.AreEqual(0f, EffectsMath.Rumble(1f, 3000f));
            Assert.AreEqual(0f, EffectsMath.Rumble(1f, EffectsMath.RumbleMaxHeight));
            Assert.AreEqual(0f, EffectsMath.Rumble(0f, 1f), "engine off: no rumble");
            Assert.Greater(EffectsMath.Rumble(1f, 5f), EffectsMath.Rumble(1f, 30f));
            Assert.LessOrEqual(EffectsMath.Rumble(1f, 0f), 1f);
        }

        [Test]
        public void TraumaDecaysLinearlyToZero()
        {
            Assert.AreEqual(0.6f, EffectsMath.DecayTrauma(1f, 0.5f, 0.8f), 1e-5f);
            Assert.AreEqual(0f, EffectsMath.DecayTrauma(0.1f, 1f, 0.8f));
            Assert.AreEqual(0.5f, EffectsMath.DecayTrauma(0.5f, -1f, 0.8f), 1e-5f, "negative dt ignored");
            Assert.AreEqual(0.25f, EffectsMath.ShakeAmount(0.5f), 1e-5f, "amplitude = trauma squared");
            Assert.Greater(EffectsMath.ExplosionTrauma(40f), EffectsMath.ExplosionTrauma(250f));
            Assert.AreEqual(0f, EffectsMath.ExplosionTrauma(2000f));
        }

        [Test]
        public void CameraShakeFadesOutAfterExplosionAndIsZeroAtRest()
        {
            var shake = new CameraShake();
            shake.Step(0.016f, out Vector3 offset, out Quaternion rot);
            Assert.AreEqual(Vector3.zero, offset);
            Assert.AreEqual(Quaternion.identity, rot);

            shake.AddTrauma(1f);
            float maxAngle = 0f;
            for (int i = 0; i < 30; i++)
            {
                shake.Step(0.016f, out _, out rot);
                maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(Quaternion.identity, rot));
            }
            Assert.Greater(maxAngle, 0.2f, "explosion shakes the camera");
            Assert.LessOrEqual(maxAngle, shake.maxAngleDeg * 1.6f);

            float seconds = 1f / shake.decayPerSecond + 0.1f;
            for (float t = 0f; t < seconds; t += 0.02f) shake.Step(0.02f, out _, out _);
            Assert.AreEqual(0f, shake.Trauma);
            shake.Step(0.016f, out offset, out rot);
            Assert.AreEqual(Vector3.zero, offset);
            Assert.AreEqual(Quaternion.identity, rot);
        }

        [Test]
        public void EngineAndWindSoundFollowThrustAndAirspeed()
        {
            Assert.AreEqual(0f, EffectsMath.EngineVolume(0f));
            Assert.Less(EffectsMath.EngineVolume(0.2f), EffectsMath.EngineVolume(1f));
            Assert.Less(EffectsMath.EnginePitch(0.2f), EffectsMath.EnginePitch(1f));
            Assert.Less(EffectsMath.EngineHighWeight(0.1f), EffectsMath.EngineHighWeight(0.9f));
            Assert.AreEqual(0f, EffectsMath.WindVolume(2f));
            Assert.Less(EffectsMath.WindVolume(50f), EffectsMath.WindVolume(220f));
            Assert.Less(EffectsMath.WindPitch(10f), EffectsMath.WindPitch(200f));
            Assert.AreEqual(-80f, EffectsMath.LinearToDecibels(0f));
            Assert.AreEqual(0f, EffectsMath.LinearToDecibels(1f), 1e-4f);
        }

        [Test]
        public void WindLoopIsSeamlessAndNormalised()
        {
            float[] s = WindSynth.Generate(2f);
            Assert.AreEqual(2 * WindSynth.SampleRate, s.Length);
            float peak = 0f, maxStep = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                peak = Mathf.Max(peak, Mathf.Abs(s[i]));
                if (i > 0) maxStep = Mathf.Max(maxStep, Mathf.Abs(s[i] - s[i - 1]));
            }
            Assert.AreEqual(0.8f, peak, 1e-4f);
            float seam = Mathf.Abs(s[0] - s[s.Length - 1]);
            Assert.LessOrEqual(seam, maxStep * 1.05f, "the wrap-around step is no bigger than a normal sample step");
        }
    }
}
