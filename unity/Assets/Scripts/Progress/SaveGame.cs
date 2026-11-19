using System;
using System.IO;
using RocketSlam.Gameplay;
using UnityEngine;

namespace RocketSlam.Progress
{
    public enum SaveLoadStatus { Loaded, Missing, Corrupt, /** Written by a newer game version (kept as *.bak). */ NewerVersion }

    /// <summary>
    /// Reading and writing a <see cref="SaveData"/> file (pure file IO, EditMode-tested). A missing, empty or
    /// unreadable file gives defaults and never throws; a corrupt file is kept as <c>*.corrupt</c> for inspection.
    /// Writes go to a temporary file first and then replace the old one, so a crash mid-write cannot corrupt it.
    /// </summary>
    public static class SaveStore
    {
        public static SaveData Load(string path, int levelCount, out SaveLoadStatus status)
        {
            SaveData data = null;
            status = SaveLoadStatus.Missing;
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    data = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<SaveData>(json);
                    status = data != null ? SaveLoadStatus.Loaded : SaveLoadStatus.Corrupt;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveGame] could not read {path}: {e.Message}; using defaults");
                data = null;
                status = SaveLoadStatus.Corrupt;
            }
            if (status == SaveLoadStatus.Corrupt)
            {
                try { File.Copy(path, path + ".corrupt", overwrite: true); }
                catch (Exception) { /* best effort */ }
            }
            else if (data != null && data.version > SaveData.CurrentVersion)
            {
                // A newer game wrote this file: keep an untouched copy before this version rewrites it in its own
                // (older) format, and say so in the log instead of downgrading silently.
                status = SaveLoadStatus.NewerVersion;
                string bak = BackupPath(path, data.version);
                try
                {
                    if (!File.Exists(bak)) File.Copy(path, bak);
                    Debug.LogWarning($"[SaveGame] {path} is save version {data.version} (this game reads {SaveData.CurrentVersion}); " +
                                     $"a copy is kept as {bak}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SaveGame] could not back up {path}: {e.Message}");
                }
                data.version = SaveData.CurrentVersion;
            }
            data ??= new SaveData();
            data.Sanitize(levelCount);
            return data;
        }

        /// <summary>Where a save of a newer <paramref name="version"/> is kept: <c>&lt;file&gt;.v&lt;version&gt;.bak</c>.</summary>
        public static string BackupPath(string path, int version) => $"{path}.v{version}.bak";

        /// <summary>Write the file; false (and a warning) on any IO error.</summary>
        public static bool Save(string path, SaveData data)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, prettyPrint: true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveGame] could not write {path}: {e.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// The game's save, loaded on first use from <c>Application.persistentDataPath/rocketslam_save.json</c>
    /// (<c>rocketslam_save_editor.json</c> in the editor, so editor play and tests never touch the player's file).
    /// Player command line: <c>-save &lt;path&gt;</c> uses another file; automated runs (<c>-autocapture</c>,
    /// <c>-benchmark</c>) keep the save in memory only. Tests call <see cref="UseFile"/> / <see cref="UseMemory"/>.
    /// </summary>
    public static class SaveGame
    {
        public const string FileName = "rocketslam_save.json";
        public const string EditorFileName = "rocketslam_save_editor.json";

        static SaveData current;
        static string path;
        static bool memoryOnly;
        static bool configured;

        /// <summary>Raised after the settings changed (and were applied).</summary>
        public static event Action SettingsChanged;

        public static SaveData Current
        {
            get
            {
                if (current == null) Load();
                return current;
            }
        }

        public static GameSettings Settings => Current.settings;
        /// <summary>The file in use (null when memory only).</summary>
        public static string FilePath
        {
            get
            {
                Configure();
                return memoryOnly ? null : path;
            }
        }
        public static SaveLoadStatus LastLoadStatus { get; private set; }

        /// <summary>Statics survive play-mode entry when domain reload is off: start clean every time.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            path = null;
            memoryOnly = false;
            configured = false;
            SettingsChanged = null;
            LastLoadStatus = SaveLoadStatus.Missing;
        }

        static void Configure()
        {
            if (configured) return;
            configured = true;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-save");
            if (i >= 0 && i + 1 < args.Length)
            {
                path = Path.GetFullPath(args[i + 1]);
                return;
            }
            if (!Application.isEditor && (Array.IndexOf(args, "-autocapture") >= 0 || Array.IndexOf(args, "-benchmark") >= 0))
            {
                memoryOnly = true;
                return;
            }
            path = Path.Combine(Application.persistentDataPath, Application.isEditor ? EditorFileName : FileName);
        }

        public static void Load()
        {
            Configure();
            if (memoryOnly)
            {
                current ??= new SaveData();
                current.Sanitize(LevelLoader.LevelCount);
                LastLoadStatus = SaveLoadStatus.Missing;
                return;
            }
            current = SaveStore.Load(path, LevelLoader.LevelCount, out SaveLoadStatus status);
            LastLoadStatus = status;
        }

        public static void Save()
        {
            Configure();
            if (current == null || memoryOnly) return;
            SaveStore.Save(path, current);
        }

        /// <summary>Use (and load) another file; null = back to the default location.</summary>
        public static void UseFile(string file)
        {
            configured = true;
            memoryOnly = false;
            path = file ?? Path.Combine(Application.persistentDataPath, Application.isEditor ? EditorFileName : FileName);
            current = null;
            Load();
        }

        /// <summary>Keep the save in memory only (nothing is written), starting from <paramref name="data"/> or defaults.</summary>
        public static void UseMemory(SaveData data = null)
        {
            configured = true;
            memoryOnly = true;
            current = data ?? new SaveData();
            current.Sanitize(LevelLoader.LevelCount);
        }

        /// <summary>Store the settings, apply them (volume, quality) and save.</summary>
        public static void CommitSettings()
        {
            Current.Sanitize(LevelLoader.LevelCount);
            SettingsApplier.Apply(Settings);
            Save();
            SettingsChanged?.Invoke();
        }
    }
}
