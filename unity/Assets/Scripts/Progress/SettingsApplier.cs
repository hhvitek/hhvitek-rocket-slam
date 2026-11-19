using RocketSlam.Effects;
using UnityEngine;
using UnityEngine.Audio;

namespace RocketSlam.Progress
{
    /// <summary>
    /// Pushes <see cref="GameSettings"/> into the engine: master volume into the exposed <c>MasterVolume</c> parameter of
    /// <c>Main.mixer</c> (registered by the scene's audio component, because the mixer is an asset referenced from the
    /// scene) and the graphics preset into the matching quality level ("Low" / "Medium" / "High", each with its own URP
    /// asset, see <c>Editor/QualityBuilder.cs</c>). Also applied once at start-up, before the first scene loads.
    /// </summary>
    public static class SettingsApplier
    {
        public const string MasterVolumeParameter = "MasterVolume";
        public static readonly string[] QualityNames = { "Low", "Medium", "High" };

        static AudioMixer mixer;

        public static AudioMixer Mixer => mixer;

        /// <summary>No stale mixer from a previous play session when domain reload is off.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => mixer = null;

        /// <summary>Called by the scene's audio component in Start (an AudioMixer ignores SetFloat during Awake).</summary>
        public static void RegisterMixer(AudioMixer m)
        {
            mixer = m;
            ApplyVolume(SaveGame.Settings.masterVolume);
        }

        public static void Apply(GameSettings s)
        {
            ApplyVolume(s.masterVolume);
            ApplyQuality((QualityPreset)s.quality);
        }

        public static void ApplyVolume(float linear)
        {
            if (mixer != null) mixer.SetFloat(MasterVolumeParameter, EffectsMath.LinearToDecibels(Mathf.Clamp01(linear)));
        }

        /// <summary>Index of the quality level for a preset, or -1 when the project has no such level.</summary>
        public static int QualityIndex(QualityPreset p)
        {
            string name = QualityNames[Mathf.Clamp((int)p, 0, QualityNames.Length - 1)];
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
                if (names[i] == name) return i;
            return -1;
        }

        public static void ApplyQuality(QualityPreset p)
        {
            int index = QualityIndex(p);
            if (index >= 0 && index != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);
        }

        public static QualityPreset CurrentQuality()
        {
            string name = QualitySettings.names[QualitySettings.GetQualityLevel()];
            for (int i = 0; i < QualityNames.Length; i++)
                if (QualityNames[i] == name) return (QualityPreset)i;
            return QualityPreset.Medium;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyAtStartup()
        {
            if (Application.isEditor) return; // editor play keeps the editor's quality level
            ApplyQuality((QualityPreset)SaveGame.Settings.quality);
        }
    }
}
