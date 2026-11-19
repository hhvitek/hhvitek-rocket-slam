using System.IO;
using NUnit.Framework;
using RocketSlam.Progress;

namespace RocketSlam.Tests.EditMode
{
    /// <summary>Save file round trip, robustness against missing / corrupt / out-of-range files, unlock rules.</summary>
    public class SaveGameTests
    {
        const int Levels = 5;
        string dir, file;

        [SetUp]
        public void CreateFolder()
        {
            dir = Path.Combine(Path.GetTempPath(), "rocketslam_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            file = Path.Combine(dir, "save.json");
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void RoundTripKeepsEverything()
        {
            var data = new SaveData();
            data.Record(1, Levels, true, 812, 3, 0.9f);
            data.Record(2, Levels, false, 0, 0, 0f);
            data.settings.masterVolume = 0.35f;
            data.settings.quality = (int)QualityPreset.High;
            data.settings.easyAssist = false;
            data.settings.cameraView = 2;
            Assert.IsTrue(SaveStore.Save(file, data));
            Assert.IsTrue(File.Exists(file));
            StringAssert.Contains("\"version\": 1", File.ReadAllText(file));

            SaveData back = SaveStore.Load(file, Levels, out SaveLoadStatus status);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            Assert.AreEqual(SaveData.CurrentVersion, back.version);
            Assert.AreEqual(812, back.Level(1).bestScore);
            Assert.AreEqual(3, back.Level(1).stars);
            Assert.AreEqual(0.9f, back.Level(1).bestTouchdownSpeed, 1e-5f);
            Assert.AreEqual(1, back.Level(2).attempts);
            Assert.IsFalse(back.Level(2).landed);
            Assert.AreEqual(2, back.unlockedLevels);
            Assert.AreEqual(0.35f, back.settings.masterVolume, 1e-5f);
            Assert.AreEqual((int)QualityPreset.High, back.settings.quality);
            Assert.IsFalse(back.settings.easyAssist);
            Assert.AreEqual(2, back.settings.cameraView);

            // overwrite (replace path) works too
            back.Record(2, Levels, true, 600, 1, 2f);
            Assert.IsTrue(SaveStore.Save(file, back));
            Assert.IsTrue(SaveStore.Load(file, Levels, out _).IsLanded(2));
            Assert.IsFalse(File.Exists(file + ".tmp"));
        }

        [Test]
        public void MissingFileGivesDefaults()
        {
            SaveData d = SaveStore.Load(Path.Combine(dir, "nope.json"), Levels, out SaveLoadStatus status);
            Assert.AreEqual(SaveLoadStatus.Missing, status);
            Assert.AreEqual(1, d.unlockedLevels);
            Assert.AreEqual(0, d.levels.Count);
            Assert.IsTrue(d.settings.easyAssist);
            Assert.AreEqual((int)QualityPreset.Medium, d.settings.quality);
        }

        [Test]
        public void CorruptFileFallsBackToDefaultsAndIsKept([Values("{ this is not json", "", "\0\0\0garbage", "{\"levels\": [ {\"level\": 1, \"landed\": tr")] string content)
        {
            File.WriteAllText(file, content);
            SaveData d = null;
            Assert.DoesNotThrow(() => d = SaveStore.Load(file, Levels, out _));
            Assert.IsNotNull(d);
            Assert.IsNotNull(d.levels);
            Assert.IsNotNull(d.settings);
            Assert.AreEqual(1, d.unlockedLevels);
            Assert.IsFalse(d.IsUnlocked(2));
            if (content.Length > 0) Assert.IsTrue(File.Exists(file + ".corrupt"), "the broken file is kept for inspection");
        }

        [Test]
        public void OutOfRangeValuesAreRepaired()
        {
            File.WriteAllText(file, "{\"version\":1,\"unlockedLevels\":99,\"levels\":[{\"level\":1,\"landed\":true,\"bestScore\":5000,\"stars\":9}," +
                                    "{\"level\":1,\"landed\":false},{\"level\":42,\"landed\":true},{\"level\":3,\"landed\":false,\"bestScore\":300,\"stars\":2}]," +
                                    "\"settings\":{\"masterVolume\":7,\"quality\":-3,\"easyAssist\":true,\"cameraView\":12}}");
            SaveData d = SaveStore.Load(file, Levels, out SaveLoadStatus status);
            Assert.AreEqual(SaveLoadStatus.Loaded, status);
            Assert.AreEqual(2, d.levels.Count, "duplicate and unknown levels dropped");
            Assert.AreEqual(1000, d.Level(1).bestScore);
            Assert.AreEqual(3, d.Level(1).stars);
            Assert.AreEqual(0, d.Level(3).stars, "no stars without a landing");
            Assert.AreEqual(0, d.Level(3).bestScore);
            Assert.AreEqual(Levels, d.unlockedLevels);
            Assert.AreEqual(1f, d.settings.masterVolume);
            Assert.AreEqual(0, d.settings.quality);
            Assert.AreEqual(2, d.settings.cameraView);
        }

        [Test]
        public void OldFileWithoutVersionOrSettingsLoads()
        {
            File.WriteAllText(file, "{\"levels\":[{\"level\":1,\"landed\":true,\"bestScore\":700,\"stars\":2}]}");
            SaveData d = SaveStore.Load(file, Levels, out _);
            Assert.AreEqual(SaveData.CurrentVersion, d.version);
            Assert.IsNotNull(d.settings);
            Assert.IsTrue(d.IsUnlocked(2), "landing level 1 unlocks level 2 even if the file did not say so");
        }

        [Test]
        public void NewerVersionIsBackedUpAndNotSilentlyDowngraded()
        {
            string json = "{\"version\":7,\"unlockedLevels\":3,\"levels\":[{\"level\":1,\"landed\":true,\"bestScore\":700,\"stars\":2}],\"futureField\":42}";
            File.WriteAllText(file, json);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("save version 7"));
            SaveData d = SaveStore.Load(file, Levels, out SaveLoadStatus status);
            Assert.AreEqual(SaveLoadStatus.NewerVersion, status);
            string bak = SaveStore.BackupPath(file, 7);
            Assert.IsTrue(File.Exists(bak), "untouched copy kept");
            Assert.AreEqual(json, File.ReadAllText(bak));
            Assert.AreEqual(700, d.Level(1).bestScore, "what this version understands still loads");

            // Saving and loading again must not overwrite the backup.
            Assert.IsTrue(SaveStore.Save(file, d));
            File.WriteAllText(file, json.Replace("700", "650"));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("save version 7"));
            SaveStore.Load(file, Levels, out _);
            Assert.AreEqual(json, File.ReadAllText(bak), "the first backup is never replaced");
        }

        [Test]
        public void NonFiniteNumbersAreRepaired()
        {
            var d = new SaveData();
            d.Record(1, Levels, true, 700, 2, float.NaN);
            Assert.AreEqual(-1f, d.Level(1).bestTouchdownSpeed, "NaN touchdown is not a record");
            d.Record(1, Levels, true, 700, 2, float.PositiveInfinity);
            Assert.AreEqual(-1f, d.Level(1).bestTouchdownSpeed);
            d.Level(1).bestTouchdownSpeed = float.PositiveInfinity;
            d.settings.masterVolume = float.NegativeInfinity;
            d.Sanitize(Levels);
            Assert.AreEqual(-1f, d.Level(1).bestTouchdownSpeed);
            Assert.AreEqual(0.8f, d.settings.masterVolume);
        }

        [Test]
        public void UnlockRules()
        {
            var d = new SaveData();
            Assert.IsTrue(d.IsUnlocked(1), "level 1 is always open");
            for (int n = 2; n <= Levels; n++) Assert.IsFalse(d.IsUnlocked(n));
            Assert.AreEqual(1, d.ContinueLevel(Levels));

            RecordOutcome fail = d.Record(1, Levels, false, 0, 0, 0f);
            Assert.IsFalse(d.IsUnlocked(2), "a failed attempt unlocks nothing");
            Assert.AreEqual(0, fail.unlockedLevel);

            RecordOutcome ok = d.Record(1, Levels, true, 700, 2, 1.5f);
            Assert.AreEqual(2, ok.unlockedLevel);
            Assert.IsTrue(ok.newBestScore);
            Assert.IsTrue(d.IsUnlocked(2));
            Assert.IsFalse(d.IsUnlocked(3));
            Assert.AreEqual(2, d.ContinueLevel(Levels), "Play continues at the first unfinished level");

            RecordOutcome worse = d.Record(1, Levels, true, 600, 1, 2f);
            Assert.IsFalse(worse.newBestScore);
            Assert.AreEqual(700, worse.previousBestScore);
            Assert.AreEqual(700, d.Level(1).bestScore, "best score kept");
            Assert.AreEqual(2, d.Level(1).stars, "best stars kept");
            Assert.AreEqual(1.5f, d.Level(1).bestTouchdownSpeed, "softest touchdown kept");
            Assert.AreEqual(0, worse.unlockedLevel);

            RecordOutcome better = d.Record(1, Levels, true, 900, 3, 0.8f);
            Assert.IsTrue(better.newBestScore);
            Assert.AreEqual(0.8f, d.Level(1).bestTouchdownSpeed);

            for (int n = 2; n <= Levels; n++) d.Record(n, Levels, true, 650, 2, 1.5f);
            for (int n = 1; n <= Levels; n++) Assert.IsTrue(d.IsUnlocked(n));
            Assert.AreEqual(Levels, d.unlockedLevels);
            Assert.AreEqual(2, d.ContinueLevel(Levels), "all landed: the first level without three stars");
            Assert.AreEqual(3 + 2 * 4, d.TotalStars());
        }
    }
}
