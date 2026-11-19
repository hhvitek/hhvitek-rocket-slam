using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// Graphics presets for the settings screen: three Standalone quality levels "Low", "Medium" (the tuned PC asset, the
/// performance target) and "High", each with its own URP asset (copies of <c>PC_RPAsset</c>, same renderer):
/// <list type="bullet">
/// <item>Low: render scale 0.7, 2 shadow cascades at 1024 px, hard shadows, no MSAA, LOD bias 1.</item>
/// <item>Medium: the PC asset (render scale 1, 4 cascades at 2048 px, medium-quality soft shadows, no MSAA).</item>
/// <item>High: 4x MSAA, 4096 px soft shadows (high quality), LOD bias 3.</item>
/// </list>
/// Idempotent; runs as part of <c>SceneBuilder.BuildAll</c>. The Mobile level is left alone.
/// </summary>
public static class QualityBuilder
{
    const string SettingsFolder = "Assets/Settings";
    const string PcAssetPath = SettingsFolder + "/PC_RPAsset.asset";
    const string LowAssetPath = SettingsFolder + "/Low_RPAsset.asset";
    const string HighAssetPath = SettingsFolder + "/High_RPAsset.asset";
    static readonly string[] Names = { "Low", "Medium", "High" };

    [MenuItem("Rocket Slam/Build Quality Levels")]
    public static void Ensure()
    {
        var pc = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcAssetPath)
                 ?? throw new FileNotFoundException(PcAssetPath);
        UniversalRenderPipelineAsset low = Copy(pc, LowAssetPath);
        UniversalRenderPipelineAsset high = Copy(pc, HighAssetPath);

        low.renderScale = 0.7f;
        low.upscalingFilter = UpscalingFilterSelection.Linear;
        low.msaaSampleCount = 1;
        low.shadowCascadeCount = 2;
        low.cascade2Split = 0.15f;
        SetInt(low, "m_MainLightShadowmapResolution", 1024);
        SetBool(low, "m_SoftShadowsSupported", false);
        EditorUtility.SetDirty(low);

        high.renderScale = 1f;
        high.msaaSampleCount = 4;
        high.shadowCascadeCount = 4;
        SetInt(high, "m_MainLightShadowmapResolution", 4096);
        SetBool(high, "m_SoftShadowsSupported", true);
        SetInt(high, "m_SoftShadowQuality", 3);
        EditorUtility.SetDirty(high);

        // Medium (the 60 FPS target on an integrated GPU): medium soft shadows (9-tap tent instead of 16-tap), the
        // per-pixel shadow filtering of a screen full of shadow-receiving ground was a large share of the frame.
        SetInt(pc, "m_SoftShadowQuality", 2);
        EditorUtility.SetDirty(pc);

        Object qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
        var so = new SerializedObject(qualityAsset);
        SerializedProperty levels = so.FindProperty("m_QualitySettings");
        int pcIndex = Find(levels, "PC");
        if (pcIndex < 0) pcIndex = Find(levels, "Medium");
        if (pcIndex < 0) throw new InvalidDataException("QualitySettings has neither a 'PC' nor a 'Medium' level");
        levels.GetArrayElementAtIndex(pcIndex).FindPropertyRelative("name").stringValue = "Medium";

        // Low just before Medium, High just after, both copied from Medium (same platform exclusions).
        if (Find(levels, "Low") < 0)
        {
            levels.InsertArrayElementAtIndex(pcIndex); // duplicates Medium at pcIndex; Medium moves to pcIndex + 1
            levels.GetArrayElementAtIndex(pcIndex).FindPropertyRelative("name").stringValue = "Low";
        }
        int medium = Find(levels, "Medium");
        if (Find(levels, "High") < 0)
        {
            levels.InsertArrayElementAtIndex(medium);
            levels.GetArrayElementAtIndex(medium + 1).FindPropertyRelative("name").stringValue = "High";
            levels.GetArrayElementAtIndex(medium).FindPropertyRelative("name").stringValue = "Medium";
        }

        Configure(levels.GetArrayElementAtIndex(Find(levels, "Low")), low, lodBias: 1f, aniso: 1);
        Configure(levels.GetArrayElementAtIndex(Find(levels, "Medium")), pc, lodBias: 2f, aniso: 2);
        Configure(levels.GetArrayElementAtIndex(Find(levels, "High")), high, lodBias: 3f, aniso: 2);

        // Standalone (and anything that defaulted to PC) starts on Medium.
        int mediumIndex = Find(levels, "Medium");
        SerializedProperty defaults = so.FindProperty("m_PerPlatformDefaultQuality");
        for (int i = 0; i < defaults.arraySize; i++)
        {
            SerializedProperty pair = defaults.GetArrayElementAtIndex(i);
            if (pair.FindPropertyRelative("first").stringValue == "Standalone")
                pair.FindPropertyRelative("second").intValue = mediumIndex;
        }
        so.FindProperty("m_CurrentQuality").intValue = mediumIndex;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"[QualityBuilder] quality levels: {string.Join(", ", QualitySettings.names)}; Medium = {mediumIndex}");
    }

    static UniversalRenderPipelineAsset Copy(UniversalRenderPipelineAsset source, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
            throw new IOException("Could not copy URP asset to " + path);
        return AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
    }

    static void SetInt(Object asset, string property, int value)
    {
        var so = new SerializedObject(asset);
        SerializedProperty p = so.FindProperty(property) ?? throw new InvalidDataException($"{property} missing on {asset.name}");
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetBool(Object asset, string property, bool value)
    {
        var so = new SerializedObject(asset);
        SerializedProperty p = so.FindProperty(property) ?? throw new InvalidDataException($"{property} missing on {asset.name}");
        p.boolValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static int Find(SerializedProperty levels, string name)
    {
        for (int i = 0; i < levels.arraySize; i++)
            if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
        return -1;
    }

    static void Configure(SerializedProperty level, RenderPipelineAsset pipeline, float lodBias, int aniso)
    {
        level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = pipeline;
        level.FindPropertyRelative("lodBias").floatValue = lodBias;
        level.FindPropertyRelative("anisotropicTextures").intValue = aniso;
    }
}
