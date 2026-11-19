using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using RocketSlam;
using UnityEngine;

// Global namespace on purpose: -executeMethod BuildScript.BuildLinux stays short.
/// <summary>
/// Command-line builds. Run with
/// <c>Unity -batchmode -projectPath unity -executeMethod BuildScript.BuildLinux -quit</c>.
/// Output goes to <c>builds/</c> at the repository root (one level above the Unity project).
/// </summary>
public static class BuildScript
{
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

    [MenuItem("Rocket Slam/Build/Linux")]
    public static void BuildLinux()
    {
        Build(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone,
            Path.Combine(RepoRoot, "builds", "Linux", GameInfo.ExecutableName + ".x86_64"));
    }

    /// <summary>Rebuild the scenes, then the Linux player, in one editor run (one import, one Unity start).</summary>
    public static void BuildScenesAndLinux()
    {
        SceneBuilder.BuildAll();
        BuildLinux();
    }

    [MenuItem("Rocket Slam/Build/Windows")]
    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone,
            Path.Combine(RepoRoot, "builds", "Windows", GameInfo.ExecutableName + ".exe"));
    }

    static void Build(BuildTarget target, BuildTargetGroup group, string outputPath)
    {
        try
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "No enabled scenes in EditorBuildSettings. Run SceneBuilder.BuildAll first.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"Build {target} finished with result {summary.result}, {summary.totalErrors} error(s).");

            Debug.Log($"[BuildScript] {target} build succeeded: {outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s)");
        }
        catch (Exception e)
        {
            Debug.LogError($"[BuildScript] Build failed: {e.Message}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw;
        }
    }
}
