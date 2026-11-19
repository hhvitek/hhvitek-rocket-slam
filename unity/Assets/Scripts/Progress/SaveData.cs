using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketSlam.Progress
{
    /// <summary>Best result of one level.</summary>
    [Serializable]
    public class LevelRecord
    {
        public int level;
        public bool landed;
        public int bestScore;
        public int stars;
        /// <summary>Softest successful touchdown, m/s (vertical, relative to the surface); -1 = none yet.</summary>
        public float bestTouchdownSpeed = -1f;
        public int attempts;
    }

    public enum QualityPreset { Low = 0, Medium = 1, High = 2 }

    /// <summary>Player options (settings screen).</summary>
    [Serializable]
    public class GameSettings
    {
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        /// <summary><see cref="QualityPreset"/> as int.</summary>
        public int quality = (int)QualityPreset.Medium;
        /// <summary>Default control mode: true = Easy (assist on), false = Manual. M in a level still toggles.</summary>
        public bool easyAssist = true;
        /// <summary>Default camera view (<c>CameraView</c> as int: 0 chase, 1 engine, 2 pad-side).</summary>
        public int cameraView;

        public GameSettings Clone() => (GameSettings)MemberwiseClone();
    }

    /// <summary>What <see cref="SaveData.Record"/> changed.</summary>
    public struct RecordOutcome
    {
        public bool newBestScore;
        public int previousBestScore;
        public int previousStars;
        /// <summary>The landing unlocked a level that was locked before (0 = none).</summary>
        public int unlockedLevel;
    }

    /// <summary>
    /// The save file (<c>rocketslam_save.json</c>, see <see cref="SaveGame"/>): version, unlocked levels, per-level bests
    /// and settings. JsonUtility format; <see cref="Sanitize"/> repairs anything out of range after loading.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        /// <summary>Highest unlocked level (level N+1 unlocks when N is landed; level 1 is always unlocked).</summary>
        public int unlockedLevels = 1;
        public List<LevelRecord> levels = new List<LevelRecord>();
        public GameSettings settings = new GameSettings();

        /// <summary>The record of level <paramref name="n"/> (created when missing).</summary>
        public LevelRecord Level(int n)
        {
            foreach (LevelRecord r in levels)
                if (r.level == n) return r;
            var rec = new LevelRecord { level = n };
            levels.Add(rec);
            levels.Sort((a, b) => a.level.CompareTo(b.level));
            return rec;
        }

        /// <summary>The record of level n, or null (read-only lookups do not grow the list).</summary>
        public LevelRecord Find(int n)
        {
            foreach (LevelRecord r in levels)
                if (r.level == n) return r;
            return null;
        }

        public bool IsLanded(int n) => Find(n)?.landed ?? false;

        public bool IsUnlocked(int n) => n == 1 || (n >= 1 && (n <= unlockedLevels || IsLanded(n - 1)));

        /// <summary>Where Play continues: the first unlocked level not landed yet, else the first without 3 stars,
        /// else level 1.</summary>
        public int ContinueLevel(int levelCount)
        {
            for (int n = 1; n <= levelCount; n++)
                if (IsUnlocked(n) && !IsLanded(n)) return n;
            for (int n = 1; n <= levelCount; n++)
                if ((Find(n)?.stars ?? 0) < 3) return n;
            return 1;
        }

        public int TotalStars()
        {
            int s = 0;
            foreach (LevelRecord r in levels) s += r.stars;
            return s;
        }

        /// <summary>Store one finished attempt; returns what changed (new best, unlocked level).</summary>
        public RecordOutcome Record(int level, int levelCount, bool landed, int score, int stars, float touchdownSpeed)
        {
            LevelRecord r = Level(level);
            var outcome = new RecordOutcome { previousBestScore = r.bestScore, previousStars = r.stars };
            r.attempts++;
            if (!landed) return outcome;
            outcome.newBestScore = !r.landed || score > r.bestScore;
            r.landed = true;
            r.bestScore = Mathf.Clamp(Mathf.Max(r.bestScore, score), 0, 1000);
            r.stars = Mathf.Clamp(Mathf.Max(r.stars, stars), 1, 3);
            if (IsFinite(touchdownSpeed) && touchdownSpeed >= 0f && (r.bestTouchdownSpeed < 0f || touchdownSpeed < r.bestTouchdownSpeed))
                r.bestTouchdownSpeed = touchdownSpeed;
            int next = Mathf.Min(level + 1, levelCount);
            if (next > unlockedLevels)
            {
                if (next > level) outcome.unlockedLevel = next;
                unlockedLevels = next;
            }
            return outcome;
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Clamp every field into range (hand-edited or older files) and fill missing parts.</summary>
        public void Sanitize(int levelCount)
        {
            if (version <= 0) version = CurrentVersion;
            levels ??= new List<LevelRecord>();
            settings ??= new GameSettings();
            levels.RemoveAll(r => r == null || r.level < 1 || r.level > levelCount);
            // one record per level (keep the first)
            for (int i = levels.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if (levels[j].level == levels[i].level) { levels.RemoveAt(i); break; }
            levels.Sort((a, b) => a.level.CompareTo(b.level));
            foreach (LevelRecord r in levels)
            {
                r.bestScore = Mathf.Clamp(r.bestScore, 0, 1000);
                r.stars = r.landed ? Mathf.Clamp(r.stars, 1, 3) : 0;
                if (!r.landed) r.bestScore = 0;
                if (!IsFinite(r.bestTouchdownSpeed) || r.bestTouchdownSpeed < 0f) r.bestTouchdownSpeed = -1f;
                r.attempts = Mathf.Max(0, r.attempts);
            }
            int unlocked = 1;
            for (int n = 1; n < levelCount; n++)
                if (IsLanded(n)) unlocked = Mathf.Max(unlocked, n + 1);
            unlockedLevels = Mathf.Clamp(Mathf.Max(unlockedLevels, unlocked), 1, levelCount);
            GameSettings s = settings;
            s.masterVolume = !IsFinite(s.masterVolume) ? 0.8f : Mathf.Clamp01(s.masterVolume);
            s.quality = Mathf.Clamp(s.quality, 0, 2);
            s.cameraView = Mathf.Clamp(s.cameraView, 0, 2);
        }
    }
}
