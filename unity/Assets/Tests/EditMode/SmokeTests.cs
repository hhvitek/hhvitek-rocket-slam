using NUnit.Framework;

namespace RocketSlam.Tests.EditMode
{
    public class SmokeTests
    {
        [Test]
        public void RuntimeAssemblyIsReachable()
        {
            Assert.AreEqual("Rocket Slam", GameInfo.Name);
        }
    }
}
