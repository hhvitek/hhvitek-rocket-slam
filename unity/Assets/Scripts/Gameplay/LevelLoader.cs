using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RocketSlam.Gameplay
{
    /// <summary>
    /// Scene switching: the <c>MainMenu</c> scene (first in the build) and the levels <c>Level01</c> .. <c>Level05</c>.
    /// The player command line <c>-level N</c> skips the menu and starts directly in level N.
    /// </summary>
    public static class LevelLoader
    {
        public const int LevelCount = 5;
        public const string MenuScene = "MainMenu";

        public static string SceneName(int level) => $"Level{Mathf.Clamp(level, 1, LevelCount):00}";

        /// <summary>The level of the active scene (1 when the scene is not a level).</summary>
        public static int CurrentLevel
        {
            get
            {
                string name = SceneManager.GetActiveScene().name;
                return name.StartsWith("Level") && int.TryParse(name.Substring(5), out int n) ? n : 1;
            }
        }

        /// <summary>True when the active scene is a level (not the menu).</summary>
        public static bool InLevel
        {
            get
            {
                string name = SceneManager.GetActiveScene().name;
                return name.StartsWith("Level") && int.TryParse(name.Substring(5), out _);
            }
        }

        public static int Next(int level) => level >= LevelCount ? 1 : level + 1;

        public static void Load(int level)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneName(level), LoadSceneMode.Single);
        }

        public static void LoadMenu()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(MenuScene, LoadSceneMode.Single);
        }

        /// <summary>The level given with <c>-level N</c> on the command line, or 0.</summary>
        public static int CommandLineLevel()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-level");
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n >= 1 && n <= LevelCount ? n : 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyCommandLine()
        {
            if (Application.isEditor) return;
            int n = CommandLineLevel();
            if (n > 0 && (!InLevel || n != CurrentLevel)) Load(n);
        }
    }
}
