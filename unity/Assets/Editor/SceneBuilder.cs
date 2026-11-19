using System;
using System.IO;
using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Gameplay;
using RocketSlam.Hud;
using RocketSlam.Menus;
using RocketSlam.Sound;
using RocketSlam.Rocket;
using RocketSlam.World;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Builds every game scene from code so scenes are reproducible and never hand-edited.
/// Run with <c>Unity -batchmode -projectPath unity -executeMethod SceneBuilder.BuildAll -quit</c>.
/// Config assets (RocketConfig, levels) are created only when missing so tuning survives a rebuild;
/// materials and UI panel settings are regenerated every time.
/// </summary>
public static class SceneBuilder
{
    public const string ScenesFolder = "Assets/Scenes";
    public const string Level01Path = ScenesFolder + "/Level01.unity";
    public static string LevelScenePath(int n) => $"{ScenesFolder}/Level{n:00}.unity";
    public const string RocketConfigPath = "Assets/Settings/RocketConfig.asset";
    const string LevelsFolder = "Assets/Settings/Levels";
    public static string LevelConfigPath(int n) => $"{LevelsFolder}/Level{n:00}.asset";
    const string LightingPath = LevelsFolder + "/Level01.lighting"; // shared by all levels (no GI, sky only)
    const string MaterialsFolder = "Assets/Materials";
    const string InputActionsPath = "Assets/Input/RocketControls.inputactions";
    const string HudUxmlPath = "Assets/UI/Hud.uxml";
    const string MainMenuUxmlPath = "Assets/UI/MainMenu.uxml";
    const string LevelMenusUxmlPath = "Assets/UI/LevelMenus.uxml";
    const string SettingsUxmlPath = "Assets/UI/Settings.uxml";
    public const string MainMenuPath = ScenesFolder + "/MainMenu.unity";
    /// <summary>Unity azimuth of the "SLAM-1" lettering on the booster model (outward = (cos a, 0, sin a);
    /// <c>blender/scripts/booster.py</c>, <c>_add_lettering</c>).</summary>
    const float LetteringAzimuthDeg = 112.5f;
    /// <summary>The menu orbit starts this far before the sun azimuth (side light for shape), and the lettering
    /// faces the camera this far into the orbit.</summary>
    const float MenuOrbitLeadDeg = 35f;
    const float MenuLetteringAheadDeg = 20f;
    const string ThemePath = "Assets/UI/RuntimeTheme.tss";
    const string PanelSettingsPath = "Assets/UI/HudPanelSettings.asset";
    const string PrefabsFolder = "Assets/Prefabs";
    public const string DroneshipPrefabPath = PrefabsFolder + "/Droneship.prefab";

    // Droneship deck: 90 m (x) × 50 m (z), deck top at the model origin; hull bottom 7 m below, waterline 3 m below.
    static readonly Vector3 DroneshipDeckSize = new Vector3(90f, 7f, 50f);
    const float DroneshipFreeboard = 3f;
    /// <summary>Landing area of the deck (inside the parapets), half sizes along local x / z.</summary>
    static readonly Vector2 DeckHalf = new Vector2(45f, 24.5f);
    /// <summary>Foot collider (box) on each model foot pad; the pad bottom is 0.2 m below the foot pivot.</summary>
    static readonly Vector3 FootColliderSize = new Vector3(1.4f, 0.4f, 1.4f);
    const float NozzleExitY = -1.25f;
    const float GridFinDeployDeg = 90f;

    const float PadSize = 40f;
    const float PadThickness = 0.5f;

    [MenuItem("Rocket Slam/Build All Scenes")]
    public static void BuildAll()
    {
        try
        {
            Directory.CreateDirectory(ScenesFolder);
            Directory.CreateDirectory(LevelsFolder);
            Directory.CreateDirectory(MaterialsFolder);
            Directory.CreateDirectory(PrefabsFolder);
            ModelImportSettings.EnsureSetup();
            QualityBuilder.Ensure();

            RocketConfig rocketConfig = LoadOrCreate<RocketConfig>(RocketConfigPath);
            BuildDroneshipPrefab();
            // The menu is the first scene of the build; then the levels in order.
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainMenuPath, true) };
            var levels = new LevelConfig[LevelLoader.LevelCount];
            for (int n = 1; n <= LevelLoader.LevelCount; n++)
            {
                LevelConfig level = LoadOrCreate<LevelConfig>(LevelConfigPath(n));
                ApplyLevelTable(level, n);
                EditorUtility.SetDirty(level);
                levels[n - 1] = level;
                BuildLevel(LevelScenePath(n), rocketConfig, level);
                scenes.Add(new EditorBuildSettingsScene(LevelScenePath(n), true));
            }
            BuildMainMenu(rocketConfig, levels);
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] All scenes built and registered in build settings.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneBuilder] Failed: {e}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw;
        }
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    /// <summary>Build only one level scene (iteration): <c>-executeMethod SceneBuilder.BuildOne -level N</c>.</summary>
    public static void BuildOne()
    {
        int n = LevelLoader.CommandLineLevel();
        if (n <= 0) n = 1;
        RocketConfig rocketConfig = LoadOrCreate<RocketConfig>(RocketConfigPath);
        LevelConfig level = LoadOrCreate<LevelConfig>(LevelConfigPath(n));
        ApplyLevelTable(level, n);
        EditorUtility.SetDirty(level);
        BuildLevel(LevelScenePath(n), rocketConfig, level);
        AssetDatabase.SaveAssets();
    }

    // ---- level table ----------------------------------------------------------------------------

    /// <summary>
    /// The level table (PLAN.md §2b): written into the level assets on every build, so the scenes are reproducible
    /// from code. Fuel 10 → 6 t; wind = steady (x east, y north) + gust peak; sea = swell amplitude / period /
    /// direction + chop. Start speed is a little lower on the low-fuel levels so a first-time Easy player still makes it.
    /// Star scores (see <c>Scoring</c>), calibrated with the PlayMode bots in <c>CampaignLevelsTests</c> (L1..L5):
    /// first-time Easy player (0.5 s reaction, eases off in the last metres) 750/738/720/696/649 → two stars (≈100 above
    /// the 2-star line); skilled Easy player (fires 1.2 s after FIRE NOW, holds Space to the pad) 840/825/815/788/757 and
    /// the suicide-burn autopilot (≈ a skilled Manual landing) 947/913/884/844/810 → three stars. The 3-star line sits
    /// midway between the first-time and the skilled Easy scores.
    /// </summary>
    static void ApplyLevelTable(LevelConfig l, int n)
    {
        l.number = n;
        l.startAltitude = 3000f;
        l.startHorizontalVelocity = Vector2.zero;
        l.wind = default;
        l.sea = default;
        l.timeOfDay = TimeOfDay.Day;
        switch (n)
        {
            case 1:
                l.displayName = "Ground pad";
                l.conditions = "Calm air, clear day.";
                l.twoStarScore = 650;
                l.threeStarScore = 795;
                l.briefing = "Calm day. Land softly on the pad.";
                l.site = LandingSiteKind.GroundPad;
                l.fuelKg = 10000f;
                l.startVerticalSpeed = -225f;
                l.startOffset = new Vector2(60f, -40f);
                l.fogDensity = 0.000105f;
                l.skyRotation = 75f;
                break;
            case 2:
                l.displayName = "Crosswind";
                l.conditions = "Crosswind 9 m/s with light gusts.";
                l.twoStarScore = 640;
                l.threeStarScore = 780;
                l.briefing = "A steady crosswind pushes you sideways. The rocket leans into it.";
                l.site = LandingSiteKind.GroundPad;
                l.fuelKg = 9000f;
                l.startVerticalSpeed = -220f;
                l.startOffset = new Vector2(-70f, 50f);
                l.wind = new WindSettings { steady = new Vector2(9f, 3f), gustStrength = 2f, gustFrequency = 0.07f, seed = 2 };
                l.fogDensity = 0.00013f;
                l.skyRotation = 205f;
                break;
            case 3:
                l.displayName = "Droneship";
                l.conditions = "Calm sea; the deck barely moves.";
                l.twoStarScore = 620;
                l.threeStarScore = 765;
                l.briefing = "Calm sea. Land on the deck of the drone ship.";
                l.site = LandingSiteKind.Droneship;
                l.fuelKg = 8000f;
                l.startVerticalSpeed = -215f;
                l.startOffset = new Vector2(50f, 60f);
                l.sea = new SeaState { swellAmplitude = 0.35f, swellPeriod = 9f, swellDirection = 30f, chopAmplitude = 0.15f, seed = 3 };
                l.fogDensity = 0.00011f;
                l.skyRotation = 140f;
                break;
            case 4:
                l.displayName = "Rough sea";
                l.conditions = "Waves heave the deck; gusty wind.";
                l.twoStarScore = 600;
                l.threeStarScore = 740;
                l.briefing = "Waves heave, pitch and roll the deck, and the wind gusts. Touchdown speed counts relative to the deck.";
                l.site = LandingSiteKind.Droneship;
                l.fuelKg = 7000f;
                l.startVerticalSpeed = -205f;
                l.startOffset = new Vector2(-60f, -50f);
                l.wind = new WindSettings { steady = new Vector2(-6f, 4f), gustStrength = 3.5f, gustFrequency = 0.1f, seed = 4 };
                l.sea = new SeaState { swellAmplitude = 1.1f, swellPeriod = 10f, swellDirection = 60f, chopAmplitude = 0.35f, seed = 4 };
                l.fogDensity = 0.00016f;
                l.skyRotation = 290f;
                break;
            default:
                l.displayName = "Night";
                l.conditions = "Night, fog and strong gusts.";
                l.twoStarScore = 560;
                l.threeStarScore = 700;
                l.briefing = "Night, fog and gusty wind. Follow the deck lights and the SHIP marker.";
                l.site = LandingSiteKind.Droneship;
                l.fuelKg = 6000f;
                l.startVerticalSpeed = -195f;
                l.startOffset = new Vector2(40f, 70f);
                l.wind = new WindSettings { steady = new Vector2(5f, -5f), gustStrength = 4.5f, gustFrequency = 0.13f, seed = 5 };
                l.sea = new SeaState { swellAmplitude = 0.7f, swellPeriod = 9f, swellDirection = 200f, chopAmplitude = 0.3f, seed = 5 };
                l.timeOfDay = TimeOfDay.Night;
                l.fogDensity = 0.00034f;
                l.skyRotation = 0f;
                break;
        }
        l.name = $"Level{n:00}";
    }

    // ---- materials ------------------------------------------------------------------------------

    static Material LitMaterial(string name, Color color, float smoothness = 0.15f)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        return SaveMaterial(mat);
    }

    static Material UnlitMaterial(string name, Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
        mat.SetColor("_BaseColor", color);
        return SaveMaterial(mat);
    }

    /// <summary>Create the material asset, or update the existing one in place (keeps its GUID stable).</summary>
    internal static Material SaveMaterial(Material mat)
    {
        string path = $"{MaterialsFolder}/{mat.name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        existing.shader = mat.shader;
        existing.CopyPropertiesFromMaterial(mat);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mat);
        return existing;
    }

    // ---- level ----------------------------------------------------------------------------------

    static void BuildLevel(string scenePath, RocketConfig config, LevelConfig level)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var mats = new
        {
            markerYellow = UnlitMaterial("MarkerYellow", new Color(1f, 0.8f, 0.1f)),
            markerWhite = UnlitMaterial("MarkerWhite", new Color(0.97f, 0.97f, 0.97f)),
            cueLine = UnlitMaterial("CueLine", new Color(0.35f, 0.85f, 1f)),
            cuePuck = UnlitMaterial("CuePuck", new Color(0.08f, 0.09f, 0.1f)),
        };

        GameObject env = new GameObject("Environment");
        // Sky, sun / moon, fog; generated terrain + launch-site scenery, or the sea.
        EnvironmentBuilder.Build(env.transform, level);

        LandingSite site;
        Transform target;
        if (level.site == LandingSiteKind.GroundPad)
        {
            // Pad: LandingPad.fbx (slab top y 0.5, markings and edge lights above it) on a box collider = the slab.
            var pad = new GameObject("Pad");
            pad.transform.SetParent(env.transform, false);
            var padCollider = pad.AddComponent<BoxCollider>();
            padCollider.center = new Vector3(0f, PadThickness * 0.5f, 0f);
            padCollider.size = new Vector3(PadSize, PadThickness, PadSize);
            pad.AddComponent<LandingSurface>().isPad = true;
            InstantiateModel(ModelImportSettings.LandingPadPath, "LandingPadModel", pad.transform);
            site = pad.AddComponent<LandingSite>();
            site.Setup(new Vector3(0f, PadThickness, 0f), new Vector2(PadSize * 0.5f, PadSize * 0.5f), null, "PAD");
            BuildGroundMarker(env.transform, mats.markerYellow, mats.markerWhite);
            target = new GameObject("PadTarget").transform;
            target.SetParent(env.transform);
            target.position = new Vector3(0f, PadThickness, 0f);
        }
        else
        {
            GameObject ship = CreateDroneship(level.timeOfDay == TimeOfDay.Night);
            ship.transform.SetParent(env.transform, false);
            ship.transform.position = new Vector3(0f, DroneshipFreeboard, 0f);
            ShipMotion motion = ship.GetComponent<ShipMotion>();
            motion.Setup(level.sea, DroneshipFreeboard, DroneshipDeckSize.x * 0.5f, DroneshipDeckSize.z * 0.5f);
            site = ship.GetComponent<LandingSite>();
            site.Setup(Vector3.zero, DeckHalf, motion, "SHIP");
            target = new GameObject("PadTarget").transform; // rides with the deck (camera aim)
            target.SetParent(ship.transform, false);
        }

        RocketController rocket = BuildRocket(config);
        Vector3 top = target.position;
        rocket.transform.position = new Vector3(top.x + level.startOffset.x, top.y + level.startAltitude, top.z + level.startOffset.y);

        // One physical camera; CameraDirector moves it between the chase / engine / pad-side views.
        Camera mainCam = MakeCamera("Main Camera", new Vector3(0f, 0f, -80f), 55f);
        mainCam.gameObject.tag = "MainCamera";
        mainCam.gameObject.AddComponent<AudioListener>();
        mainCam.gameObject.AddComponent<AdaptiveShadows>();
        var camRoot = new GameObject("Cameras");
        mainCam.transform.SetParent(camRoot.transform);
        CameraDirector director = camRoot.AddComponent<CameraDirector>();
        director.Setup(rocket, mainCam, target);
        EffectsBuilder.BuildPostProcessing(mainCam);

        // Depth cue: drop line + puck under the rocket.
        var cueRoot = new GameObject("GroundCue");
        GameObject dropLine = Primitive(PrimitiveType.Cylinder, "DropLine", cueRoot.transform, Vector3.zero, Vector3.one, mats.cueLine);
        GameObject puck = Primitive(PrimitiveType.Cylinder, "Puck", cueRoot.transform, Vector3.zero, Vector3.one, mats.cuePuck);
        dropLine.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        puck.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        cueRoot.AddComponent<GroundCue>().Setup(rocket, director, dropLine.transform, puck.transform);

        // Game logic
        var game = new GameObject("Game");
        LevelRuntime runtime = game.AddComponent<LevelRuntime>();
        runtime.Setup(rocket, site, level);
        LandingSession session = game.AddComponent<LandingSession>();
        session.Setup(rocket, level, site, runtime);
        PlayerControls controls = game.AddComponent<PlayerControls>();
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath)
                      ?? throw new FileNotFoundException(InputActionsPath);
        EasyAssist assist = game.AddComponent<EasyAssist>();
        assist.Bind(rocket, target.position);
        controls.Setup(actions, rocket, session, director, assist);
        SuicideBurnAutopilot autopilot = game.AddComponent<SuicideBurnAutopilot>();
        autopilot.Bind(rocket, target.position);
        autopilot.enabled = false; // used by tests / capture runs; the player flies by default
        game.AddComponent<AutoCapture>().Setup(session, autopilot, controls);
        AttemptTracker tracker = game.AddComponent<AttemptTracker>();
        tracker.Setup(session);

        // Effects (flame lives under the rocket, built in BuildRocket) and sound.
        var effectsRoot = new GameObject("Effects");
        GroundDust dust = EffectsBuilder.BuildGroundDust(effectsRoot.transform);
        ExplosionEffect explosion = EffectsBuilder.BuildExplosion(effectsRoot.transform);
        effectsRoot.AddComponent<RocketEffects>().Setup(session, director, rocket.GetComponentInChildren<EngineFlame>(true), dust, explosion);
        GameAudio gameAudio = EffectsBuilder.BuildAudio(session, director, controls);

        // HUD
        var hudGo = new GameObject("HUD");
        UIDocument doc = hudGo.AddComponent<UIDocument>();
        doc.panelSettings = HudPanelSettings();
        doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(HudUxmlPath)
                              ?? throw new FileNotFoundException(HudUxmlPath);
        hudGo.AddComponent<HudController>().Setup(session, director, controls);

        // Pause menu and results screen (drawn above the HUD).
        var menusGo = new GameObject("LevelMenus");
        UIDocument menusDoc = menusGo.AddComponent<UIDocument>();
        menusDoc.panelSettings = doc.panelSettings;
        menusDoc.sortingOrder = 10;
        menusDoc.visualTreeAsset = LoadUxml(LevelMenusUxmlPath);
        menusGo.AddComponent<LevelMenus>().Setup(session, tracker, controls, gameAudio, LoadUxml(SettingsUxmlPath), doc);

        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new IOException("Could not save " + scenePath);
        BakeEnvironmentLighting(scene, scenePath);
    }

    static VisualTreeAsset LoadUxml(string path)
        => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path) ?? throw new FileNotFoundException(path);

    // ---- main menu --------------------------------------------------------------------------------

    /// <summary>Build only the menu scene (iteration): <c>-executeMethod SceneBuilder.BuildMenuOnly</c>.</summary>
    public static void BuildMenuOnly()
    {
        RocketConfig rocketConfig = LoadOrCreate<RocketConfig>(RocketConfigPath);
        var levels = new LevelConfig[LevelLoader.LevelCount];
        for (int n = 1; n <= levels.Length; n++) levels[n - 1] = LoadOrCreate<LevelConfig>(LevelConfigPath(n));
        BuildMainMenu(rocketConfig, levels);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Main menu scene: the level-1 launch site (sky, terrain, scenery) with the booster standing landed on the pad,
    /// a slowly orbiting camera (<see cref="MenuBackdrop"/>), the menu UI and its sounds. No physics, no rocket logic.
    /// </summary>
    static void BuildMainMenu(RocketConfig config, LevelConfig[] levels)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var env = new GameObject("Environment");
        EnvironmentBuilder.Build(env.transform, levels[0]);

        var pad = new GameObject("Pad");
        pad.transform.SetParent(env.transform, false);
        var padCollider = pad.AddComponent<BoxCollider>(); // terrain props / rays treat it like the level pad
        padCollider.center = new Vector3(0f, PadThickness * 0.5f, 0f);
        padCollider.size = new Vector3(PadSize, PadThickness, PadSize);
        InstantiateModel(ModelImportSettings.LandingPadPath, "LandingPadModel", pad.transform);

        Transform booster = BuildLandedBooster(config).transform;
        booster.position = new Vector3(0f, PadThickness, 0f);
        // The orbit starts on the sun side (booster front-lit), a little before the camera passes the sun azimuth,
        // and the booster is turned so its painted "SLAM-1" faces the camera there.
        Vector3 sunDir = EnvironmentBuilder.SkyDirection(EnvironmentBuilder.HdriSunElevation, EnvironmentBuilder.HdriSunLongitude, levels[0].skyRotation);
        float sunAzimuth = Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg; // MenuBackdrop angle convention: (sin a, cos a)
        float orbitStart = sunAzimuth - MenuOrbitLeadDeg;
        float letteringAzimuth = Mathf.Atan2(Mathf.Cos(LetteringAzimuthDeg * Mathf.Deg2Rad), Mathf.Sin(LetteringAzimuthDeg * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
        booster.rotation = Quaternion.Euler(0f, orbitStart + MenuLetteringAheadDeg - letteringAzimuth, 0f);

        Camera cam = MakeCamera("Main Camera", new Vector3(0f, 20f, -60f), 50f);
        cam.gameObject.tag = "MainCamera";
        cam.gameObject.AddComponent<AudioListener>();
        cam.gameObject.AddComponent<AdaptiveShadows>();
        EffectsBuilder.BuildPostProcessing(cam);
        MenuBackdrop backdrop = cam.gameObject.AddComponent<MenuBackdrop>();
        backdrop.Setup(booster, 64f, 12f, config.bodyHeight * 0.55f, orbitStart);
        backdrop.Apply(0f);

        MenuAudio audio = EffectsBuilder.BuildMenuAudio();

        var ui = new GameObject("MainMenu");
        UIDocument doc = ui.AddComponent<UIDocument>();
        doc.panelSettings = HudPanelSettings();
        doc.visualTreeAsset = LoadUxml(MainMenuUxmlPath);
        ui.AddComponent<MainMenuController>().Setup(LoadUxml(SettingsUxmlPath), levels, audio);

        if (!EditorSceneManager.SaveScene(scene, MainMenuPath))
            throw new IOException("Could not save " + MainMenuPath);
        BakeEnvironmentLighting(scene, MainMenuPath);
    }

    /// <summary>The booster model with legs and grid fins deployed, standing with its feet on y = 0 (static: the pose
    /// helpers are removed after posing).</summary>
    static GameObject BuildLandedBooster(RocketConfig c)
    {
        var root = new GameObject("LandedBooster");
        Transform model = InstantiateModel(ModelImportSettings.BoosterPath, "BoosterModel", root.transform).transform;
        var helpers = new List<Component>();
        for (int i = 0; i < 4; i++)
        {
            Transform hinge = Child(model, $"Leg_{i}");
            Transform foot = Child(model, $"Foot_{i}");
            foot.SetParent(hinge, worldPositionStays: true);
            RocketLeg leg = hinge.gameObject.AddComponent<RocketLeg>();
            BoxCollider footCollider = foot.gameObject.AddComponent<BoxCollider>();
            footCollider.size = FootColliderSize;
            leg.Setup(foot, footCollider, RocketLeg.DeployAxisFor(hinge.localPosition), c.legDeployAngleDeg);
            leg.SetDeployment(1f);
            helpers.Add(leg);
        }
        var fins = new Transform[4];
        for (int i = 0; i < 4; i++) fins[i] = Child(model, $"GridFin_{i}");
        RocketGridFins gridFins = root.AddComponent<RocketGridFins>();
        gridFins.Setup(fins, GridFinDeployDeg);
        gridFins.SetPose(1f, Vector2.zero);
        helpers.Add(gridFins);
        foreach (Component h in helpers) Object.DestroyImmediate(h);

        // Stand on the feet: lift so the lowest renderer point is at the root's y.
        float minY = float.MaxValue;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>()) minY = Mathf.Min(minY, r.bounds.min.y);
        if (minY < float.MaxValue) model.localPosition = new Vector3(0f, -minY, 0f);
        return root;
    }

    /// <summary>
    /// Environment lighting from the HDRI sky: no lightmaps (baked and realtime GI off), only the ambient probe and
    /// the default reflection cubemap, which Unity derives from the skybox when the scene's lighting is generated.
    /// </summary>
    static void BakeEnvironmentLighting(Scene scene, string scenePath)
    {
        var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
        if (settings == null)
        {
            settings = new LightingSettings { name = "Level01" };
            AssetDatabase.CreateAsset(settings, LightingPath);
        }
        settings.bakedGI = false;
        settings.realtimeGI = false;
        EditorUtility.SetDirty(settings);
        Lightmapping.lightingSettings = settings;
        if (!Lightmapping.Bake())
            throw new InvalidOperationException("Environment lighting bake failed");
        if (!EditorSceneManager.SaveScene(scene, scenePath))
            throw new IOException("Could not save " + scenePath);
    }

    /// <summary>
    /// Large high-contrast target around the pad, readable from 3 km: white scale ring (r 120 m), yellow ring
    /// (r 60 m) and four yellow arms. Unlit 0.6 m high slabs without colliders (tall enough to avoid z-fighting
    /// with the ground at long range, low enough not to wall off the pad-side view).
    /// </summary>
    static void BuildGroundMarker(Transform parent, Material yellow, Material white)
    {
        var root = new GameObject("GroundMarker").transform;
        root.SetParent(parent, false);
        Ring(root, 120f, 8f, 64, white);
        Ring(root, 60f, 8f, 40, yellow);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f;
            var arm = Primitive(PrimitiveType.Cube, $"Arm{i}", root, Vector3.zero, new Vector3(8f, 0.5f, 95f), yellow); // lower than the rings: no z-fighting at crossings
            arm.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            arm.transform.localPosition = arm.transform.localRotation * new Vector3(0f, 0.25f, 32f + 95f * 0.5f);
            arm.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    static void Ring(Transform parent, float radius, float width, int segments, Material mat)
    {
        float segLength = 2f * Mathf.PI * radius / segments * 1.02f;
        for (int i = 0; i < segments; i++)
        {
            float a = 360f * i / segments;
            Quaternion rot = Quaternion.Euler(0f, a, 0f);
            var seg = Primitive(PrimitiveType.Cube, $"Ring{radius:0}_{i:00}", parent, rot * new Vector3(0f, 0.3f, radius), new Vector3(segLength, 0.6f, width), mat);
            seg.transform.localRotation = rot;
            seg.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    static PanelSettings HudPanelSettings()
    {
        var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        if (ps == null)
        {
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(ps, PanelSettingsPath);
        }
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;
        ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath)
                             ?? throw new FileNotFoundException(ThemePath);
        EditorUtility.SetDirty(ps);
        return ps;
    }

    static Camera MakeCamera(string name, Vector3 position, float fov)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        Camera cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = fov;
        cam.nearClipPlane = 1f; // nothing is ever closer; improves depth precision at 3 km
        cam.farClipPlane = 20000f; // fog is opaque well before this
        return cam;
    }

    // ---- rocket ---------------------------------------------------------------------------------

    /// <summary>Instantiate a model (no prefab link) under <paramref name="parent"/> at its origin.</summary>
    static GameObject InstantiateModel(string path, string name, Transform parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new FileNotFoundException(path);
        var go = (GameObject)Object.Instantiate(asset, parent, false);
        go.name = name;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        return go;
    }

    static Transform Child(Transform parent, string name)
        => parent.Find(name) ?? throw new InvalidDataException($"Model node '{name}' missing under '{parent.name}'");

    /// <summary>
    /// Rocket root (Rigidbody + controller) with the Booster.fbx model as a child. The model origin is the
    /// centre of the body's bottom face, same as the root. Physics colliders are primitives sized to the model:
    /// a capsule for the body and a box on each foot pad. Feet are re-parented under their legs so they ride
    /// along while <see cref="RocketLeg"/> keeps them level.
    /// </summary>
    internal static RocketController BuildRocket(RocketConfig c)
    {
        var root = new GameObject("Rocket");
        root.AddComponent<Rigidbody>();

        var bodyCollider = new GameObject("BodyCollider").AddComponent<CapsuleCollider>();
        bodyCollider.transform.SetParent(root.transform, false);
        bodyCollider.direction = 1;
        bodyCollider.radius = c.bodyRadius;
        bodyCollider.height = c.bodyHeight;
        bodyCollider.center = new Vector3(0f, c.bodyHeight * 0.5f, 0f);

        Transform model = InstantiateModel(ModelImportSettings.BoosterPath, "BoosterModel", root.transform).transform;

        EffectsBuilder.BuildEngineFlame(root.transform, NozzleExitY);

        var legs = new RocketLeg[4];
        for (int i = 0; i < 4; i++)
        {
            Transform hinge = Child(model, $"Leg_{i}");
            Transform foot = Child(model, $"Foot_{i}");
            foot.SetParent(hinge, worldPositionStays: true); // FBX export flattens the hierarchy
            BoxCollider footCollider = foot.gameObject.AddComponent<BoxCollider>();
            footCollider.center = Vector3.zero;
            footCollider.size = FootColliderSize;

            RocketLeg leg = hinge.gameObject.AddComponent<RocketLeg>();
            leg.Setup(foot, footCollider, RocketLeg.DeployAxisFor(hinge.localPosition), c.legDeployAngleDeg);
            leg.SetDeployment(0f);
            legs[i] = leg;
        }

        var finTransforms = new Transform[4];
        for (int i = 0; i < 4; i++) finTransforms[i] = Child(model, $"GridFin_{i}");
        RocketGridFins fins = root.AddComponent<RocketGridFins>();
        fins.Setup(finTransforms, GridFinDeployDeg);
        fins.SetPose(0f, Vector2.zero);

        // Ignore Raycast layer: radar-altitude and ground-cue rays pass through the rocket; collisions are unaffected.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;

        RocketController controller = root.AddComponent<RocketController>();
        controller.SetConfig(c, legs, fins, NozzleExitY);
        return controller;
    }

    // ---- droneship ------------------------------------------------------------------------------

    /// <summary>Droneship prefab (the scenes build their own copy with <see cref="CreateDroneship"/>).</summary>
    static void BuildDroneshipPrefab()
    {
        GameObject root = CreateDroneship(night: false);
        PrefabUtility.SaveAsPrefabAsset(root, DroneshipPrefabPath, out bool ok);
        Object.DestroyImmediate(root);
        if (!ok) throw new IOException("Could not save " + DroneshipPrefabPath);
    }

    /// <summary>
    /// Droneship.fbx on a kinematic Rigidbody moved by <see cref="ShipMotion"/> (root = deck centre at deck height).
    /// Colliders: "Deck" = the hull box up to the deck top, the only landing surface; "Obstacles" = parapets,
    /// stern blast walls, containers, machinery house, mast, thruster pods (touching them with a foot = off the pad,
    /// with the body = tipped over). Model axes: Blender (x, y, z) → Unity (−x, z, −y); stern at −x.
    /// At night: floodlights on the deck plus glowing marker lights around the target ring and the deck edge.
    /// </summary>
    /// <summary>Non-skid deck: high friction, and the higher of the two friction values wins in a contact.</summary>
    static PhysicsMaterial DeckGripMaterial()
    {
        const string path = "Assets/Settings/DeckGrip.physicsMaterial";
        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (mat == null)
        {
            mat = new PhysicsMaterial("DeckGrip");
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.staticFriction = 1.1f;
        mat.dynamicFriction = 1.0f;
        mat.bounciness = 0f;
        mat.frictionCombine = PhysicsMaterialCombine.Maximum;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    internal static GameObject CreateDroneship(bool night)
    {
        var root = new GameObject("Droneship");
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        root.AddComponent<ShipMotion>();
        root.AddComponent<LandingSite>();

        var deck = new GameObject("Deck");
        deck.transform.SetParent(root.transform, false);
        var deckBox = deck.AddComponent<BoxCollider>();
        deckBox.size = DroneshipDeckSize;
        deckBox.center = new Vector3(0f, -DroneshipDeckSize.y * 0.5f, 0f);
        deck.AddComponent<LandingSurface>().isPad = true;
        deckBox.sharedMaterial = DeckGripMaterial(); // a landed stage must not creep on the pitching deck

        var obstacles = new GameObject("Obstacles");
        obstacles.transform.SetParent(root.transform, false);
        obstacles.AddComponent<LandingSurface>().isPad = false;
        void Solid(string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(obstacles.transform, false);
            var b = go.AddComponent<BoxCollider>();
            b.center = center;
            b.size = size;
        }
        foreach (float side in new[] { -1f, 1f })
        {
            string s = side > 0f ? "N" : "S";
            Solid($"Parapet{s}", new Vector3(11f, 0.6f, side * 24.75f), new Vector3(68f, 1.2f, 0.5f));
            Solid($"BlastWall{s}", new Vector3(-34f, 1.75f, side * 24.75f), new Vector3(22f, 3.5f, 0.5f));
            Solid($"ThrusterPod{s}", new Vector3(-32f, -2.2f, side * 26.5f), new Vector3(12f, 7.6f, 3f));
        }
        Solid("ContainerStack", new Vector3(-36f, 2.6f, 19.5f), new Vector3(6.1f, 5.2f, 2.44f));
        Solid("ContainerA", new Vector3(-36f, 1.3f, -19.5f), new Vector3(6.1f, 2.6f, 2.44f));
        Solid("ContainerB", new Vector3(-36f, 1.3f, -16.6f), new Vector3(6.1f, 2.6f, 2.44f));
        Solid("MachineryHouse", new Vector3(-41.5f, 1.75f, 0f), new Vector3(5f, 3.5f, 12.6f));
        Solid("Mast", new Vector3(-41.5f, 6.3f, 0f), new Vector3(0.8f, 6.1f, 0.8f));

        GameObject model = InstantiateModel(ModelImportSettings.DroneshipPath, "DroneshipModel", root.transform);
        foreach (MeshRenderer mr in model.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.On;
        if (night) AddDeckLights(root.transform);
        return root;
    }

    /// <summary>Night lighting of the deck: four floodlights and small glowing markers (bloom makes them readable
    /// through the fog from far away).</summary>
    static void AddDeckLights(Transform ship)
    {
        var lights = new GameObject("DeckLights").transform;
        lights.SetParent(ship, false);
        Material marker = UnlitMaterial("DeckMarkerLight", new Color(6f, 4.2f, 1.1f));
        Material white = UnlitMaterial("DeckEdgeLight", new Color(4f, 4.4f, 5f));
        Material red = UnlitMaterial("DeckBeacon", new Color(7f, 0.6f, 0.4f));
        foreach (Vector3 p in new[] { new Vector3(-43f, 11f, 23f), new Vector3(-43f, 11f, -23f), new Vector3(43f, 8f, 23f), new Vector3(43f, 8f, -23f) })
        {
            var go = new GameObject("Floodlight");
            go.transform.SetParent(lights, false);
            go.transform.localPosition = p;
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0f, 0f) - p);
            Light l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = new Color(1f, 0.93f, 0.8f);
            l.range = 110f;
            l.spotAngle = 75f;
            l.innerSpotAngle = 40f;
            l.intensity = 25f;
            l.shadows = LightShadows.None;
            var head = Primitive(PrimitiveType.Cube, "Head", go.transform, Vector3.zero, new Vector3(1.2f, 0.7f, 0.5f), white);
            head.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var pole = Primitive(PrimitiveType.Cylinder, "Pole", lights, new Vector3(p.x, p.y * 0.5f, p.z), new Vector3(0.3f, p.y * 0.5f, 0.3f), white);
            pole.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            pole.GetComponent<MeshRenderer>().sharedMaterial = UnlitMaterial("DeckPole", new Color(0.12f, 0.12f, 0.13f));
        }
        // Ring of markers just outside the 26 m yellow circle, and white lights along the deck edges.
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            var m = Primitive(PrimitiveType.Cube, $"RingLight{i:00}", lights, new Vector3(Mathf.Cos(a) * 14f, 0.12f, Mathf.Sin(a) * 14f), new Vector3(0.5f, 0.24f, 0.5f), marker);
            m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        for (float x = -40f; x <= 44f; x += 12f)
        foreach (float z in new[] { -23.6f, 23.6f })
        {
            var m = Primitive(PrimitiveType.Cube, "EdgeLight", lights, new Vector3(x, 0.15f, z), new Vector3(0.4f, 0.3f, 0.4f), white);
            m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        foreach (float z in new[] { -18f, -6f, 6f, 18f })
        {
            var m = Primitive(PrimitiveType.Cube, "BowLight", lights, new Vector3(44f, 0.15f, z), new Vector3(0.4f, 0.3f, 0.4f), white);
            m.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        var beacon = Primitive(PrimitiveType.Sphere, "MastBeacon", lights, new Vector3(-41.5f, 10f, 0f), Vector3.one * 0.9f, red);
        beacon.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---- primitives -----------------------------------------------------------------------------

    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
        Material mat, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat)
        => Primitive(PrimitiveType.Cube, name, parent, pos, size, mat, keepCollider: true);
}
