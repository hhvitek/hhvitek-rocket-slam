using NUnit.Framework;
using RocketSlam.Gameplay;

namespace RocketSlam.Tests.EditMode
{
    public class TelemetryLogTests
    {
        static TelemetrySample At(float t, float alt = 100f) => new TelemetrySample { time = t, altitude = alt, verticalSpeed = -alt / 10f, thrust = 0.5f };

        [Test]
        public void RecordsAtTenHertzFromFiftyHertzSteps()
        {
            var log = new TelemetryLog();
            for (int i = 0; i <= 500; i++) log.Record(At(i * 0.02f)); // 10 s of physics steps
            Assert.AreEqual(101, log.Count, "one sample per 0.1 s");
            Assert.AreEqual(10f, log.Duration, 1e-3f);
        }

        [Test]
        public void ForcedSampleIsAlwaysKept()
        {
            var log = new TelemetryLog();
            log.Record(At(0f));
            Assert.IsFalse(log.Record(At(0.03f)));
            Assert.IsTrue(log.Record(At(0.04f, 0f), force: true));
            Assert.AreEqual(2, log.Count);
            Assert.AreEqual(0f, log[1].altitude);
        }

        [Test]
        public void FullLogHalvesItsRateInsteadOfGrowing()
        {
            var log = new TelemetryLog(capacity: 100);
            for (int i = 0; i < 10000; i++) log.Record(At(i * 0.02f, 3000f - i * 0.1f));
            Assert.LessOrEqual(log.Count, 100);
            Assert.Greater(log.Count, 40);
            Assert.Greater(log.Interval, TelemetryLog.DefaultInterval);
            Assert.AreEqual(0f, log[0].time, "the start stays");
            Assert.Greater(log.Duration, 190f, "covers the whole flight");
            for (int i = 1; i < log.Count; i++) Assert.Greater(log[i].time, log[i - 1].time);
        }

        [Test]
        public void ClearAndMaxima()
        {
            var log = new TelemetryLog();
            log.Record(At(0f, 3000f));
            log.Record(At(0.1f, 50f));
            Assert.AreEqual(3000f, log.MaxAltitude());
            Assert.AreEqual(300f, log.MaxFallSpeed());
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsTrue(log.Record(At(0f)));
        }
    }
}
