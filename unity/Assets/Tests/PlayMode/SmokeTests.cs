using System.Collections;
using NUnit.Framework;
using RocketSlam.Progress;
using UnityEngine;
using UnityEngine.TestTools;

namespace RocketSlam.Tests.PlayMode
{
    public class SmokeTests
    {
        /// <summary>Tests never read or write the developer's editor save.</summary>
        [SetUp]
        public void UseMemorySave() => SaveGame.UseMemory();

        [UnityTest]
        public IEnumerator FramesAdvanceInPlayMode()
        {
            int start = Time.frameCount;
            yield return null;
            yield return null;
            Assert.Greater(Time.frameCount, start);
            Assert.AreEqual("Rocket Slam", GameInfo.Name);
        }
    }
}
