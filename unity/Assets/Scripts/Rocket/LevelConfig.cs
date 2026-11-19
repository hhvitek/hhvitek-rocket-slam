using System;
using UnityEngine;

namespace RocketSlam.Rocket
{
    public enum LandingSiteKind { GroundPad, Droneship }

    public enum TimeOfDay { Day, Night }

    /// <summary>Steady wind plus gusts (all horizontal). See <see cref="RocketSlam.World.WindField"/>.</summary>
    [Serializable]
    public struct WindSettings
    {
        [Tooltip("Steady wind velocity the air moves with (x = east, y = north), m/s.")]
        public Vector2 steady;
        [Tooltip("Peak gust speed added on top of the steady wind, m/s (0 = no gusts).")]
        public float gustStrength;
        [Tooltip("Typical gust rate, gusts per second (0.1 = one every ~10 s).")]
        public float gustFrequency;
        [Tooltip("Seed of the gust pattern.")]
        public int seed;

        public bool IsCalm => steady.sqrMagnitude < 1e-4f && gustStrength <= 0f;
    }

    /// <summary>Sea state for the droneship levels. See <see cref="RocketSlam.World.WaveField"/>.</summary>
    [Serializable]
    public struct SeaState
    {
        [Tooltip("Amplitude of the main swell, m (crest height above the mean level).")]
        public float swellAmplitude;
        [Tooltip("Period of the main swell, s (wavelength follows from deep-water dispersion).")]
        public float swellPeriod;
        [Tooltip("Direction the swell travels towards, degrees clockwise from north (+z).")]
        public float swellDirection;
        [Tooltip("Amplitude of the shorter wind waves on top, m.")]
        public float chopAmplitude;
        [Tooltip("Seed of the secondary wave phases / directions.")]
        public int seed;
    }

    /// <summary>Per-level start state, resources and conditions. Assets live in <c>Assets/Settings/Levels/</c>;
    /// the values are written from the level table in <c>SceneBuilder</c> on every scene build.</summary>
    [CreateAssetMenu(menuName = "Rocket Slam/Level Config", fileName = "Level")]
    public class LevelConfig : ScriptableObject
    {
        [Tooltip("Level number, 1-based (scene Level0N).")]
        public int number = 1;
        public string displayName = "Level 1 - Ground pad";
        [Tooltip("One line for the start screen: the conditions of this level.")]
        public string briefing = "Calm day. Land on the pad.";
        [Tooltip("Short conditions summary for the level-select card.")]
        public string conditions = "Calm, day";
        public LandingSiteKind site = LandingSiteKind.GroundPad;
        [Tooltip("Landing propellant at the start, kg.")]
        public float fuelKg = 10000f;
        [Tooltip("Start altitude of the engine plane above the pad surface, m.")]
        public float startAltitude = 3000f;
        [Tooltip("Start vertical speed, m/s (negative = falling).")]
        public float startVerticalSpeed = -225f;
        [Tooltip("Start horizontal offset from the pad centre (x = east, y = north), m.")]
        public Vector2 startOffset = new Vector2(60f, -40f);
        [Tooltip("Start horizontal velocity (x = east, y = north), m/s.")]
        public Vector2 startHorizontalVelocity = Vector2.zero;

        [Header("Scoring")]
        [Tooltip("Score (0-1000) for the second star; landing at all gives the first.")]
        public int twoStarScore = 650;
        [Tooltip("Score for the third star: a skilled Manual landing or a precise Easy one.")]
        public int threeStarScore = 790;

        public string SiteName => site == LandingSiteKind.Droneship ? "Drone ship" : "Ground pad";

        [Header("Conditions")]
        public WindSettings wind;
        public SeaState sea;
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        [Tooltip("Exponential-squared fog density (0.0001 = light haze, 0.0005 = thick fog).")]
        public float fogDensity = 0.000105f;
        [Tooltip("Sky (HDRI) yaw, degrees; moves the sun with it.")]
        public float skyRotation = 75f;
    }
}
