using System;
using System.Collections.Generic;
using System.IO;
using RocketSlam.Rocket;
using RocketSlam.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Level surroundings, built by <see cref="SceneBuilder"/> from a <see cref="LevelConfig"/>: HDRI sky (day or night)
/// with a matching sun / moon, environment lighting and fog; then either the launch site (levels 1-2: generated
/// ground <see cref="GroundTerrain"/> and the props and buildings of <see cref="LaunchSiteLayout"/>, all generated
/// from a seed at load so the scene stays small) or the open sea (levels 3-5: <see cref="OceanSurface"/> and a sea
/// collider; the droneship is placed by the scene builder).
/// </summary>
public static class EnvironmentBuilder
{
    const string EnvFolder = "Assets/Settings/Environment";
    internal const string DayHdriPath = "Assets/ThirdParty/PolyHaven/kloofendal_43d_clear_puresky_2k.hdr";
    internal const string NightHdriPath = "Assets/ThirdParty/PolyHaven/dikhololo_night_2k.hdr";
    const string AmbientCG = "Assets/ThirdParty/ambientCG/";
    const string Models = "Assets/Models/";

    // Sun in the day HDRI: brightest pixel at row 267 / col 1229 of 2048x1024 → elevation 43.0°, longitude
    // atan2(z, x) = -36.1° in the Skybox/Panoramic lat-long mapping with _Rotation 0 (longitude = 180° − 360°·u).
    public const float HdriSunElevation = 43.0f;
    public const float HdriSunLongitude = -36.1f;
    // Moon in the night HDRI: small bright disc at row 163 / col 926 → elevation 61.3°, longitude 17.1°.
    public const float HdriMoonElevation = 61.3f;
    public const float HdriMoonLongitude = 17.1f;
    /// <summary>Default sky yaw (level 1): puts the sun to the side of the default chase view (camera south-east of
    /// the pad looking north-west) so buildings and the rocket get modelling light instead of flat front light.</summary>
    public const float SkyRotation = 75f;
    static readonly Color SunColor = new Color(1f, 0.965f, 0.91f);
    const float SunIntensity = 2.6f;
    const float DayReflectionIntensity = 0.6f;
    // Day ambient gradient (sRGB colours, as the lighting window shows them): a hint of sky blue from above, a neutral
    // horizon, a warm grey bounce from the ground.
    public static readonly Color DayAmbientSky = new Color(0.62f, 0.645f, 0.68f);
    public static readonly Color DayAmbientEquator = new Color(0.56f, 0.565f, 0.575f);
    public static readonly Color DayAmbientGround = new Color(0.42f, 0.40f, 0.37f);
    // Fog close to the HDRI horizon (a little darker than the brightest side, so distant ground meets the sky).
    static readonly Color DayFogColor = new Color(0.71f, 0.76f, 0.83f);
    // Night: a cool, dark moonlit haze (the HDRI horizon glow is warmer, but the fog covers the sea long before it).
    static readonly Color NightFogColor = new Color(0.2f, 0.23f, 0.3f); // sRGB; 0.13 grey went near-black after ACES
    const float NightSkyExposure = 0.3f;
    static readonly Color MoonColor = new Color(0.62f, 0.72f, 1f);
    const float MoonIntensity = 1.2f;

    public const int Seed = 4242;

    // ---- entry ----------------------------------------------------------------------------------

    /// <summary>Sky, light, fog and the level's surroundings under <paramref name="env"/>. Returns the sun / moon.</summary>
    public static Light Build(Transform env, LevelConfig level)
    {
        Directory.CreateDirectory(EnvFolder);
        Light sun = SetupSky(level);
        if (level.site == LandingSiteKind.GroundPad) BuildLaunchSite(env);
        else BuildSea(env, level);
        return sun;
    }

    /// <summary>Generated ground, buildings (GameObjects with colliders) and the instanced props. The props and the
    /// ground paint are regenerated at load from the same seed; only the buildings and the prop kinds (mesh +
    /// material, no instances) are stored in the scene.</summary>
    static void BuildLaunchSite(Transform env)
    {
        Material palette = AssetDatabase.LoadAssetAtPath<Material>(ModelImportSettings.PaletteMaterialPath)
                           ?? throw new FileNotFoundException(ModelImportSettings.PaletteMaterialPath);
        Material foliage = FoliageMaterial(palette, "Foliage", new Color(0.7f, 0.76f, 0.66f));
        // Trees far from the pad are only ever seen from far away, where a forest canopy reads darker.
        Material foliageFar = FoliageMaterial(palette, "FoliageFar", new Color(0.5f, 0.56f, 0.47f));

        var radii = new Dictionary<string, float>();
        float Radius(string model)
        {
            if (!radii.TryGetValue(model, out float r)) radii[model] = r = BuildingFootprintRadius(model);
            return r;
        }
        LaunchSiteLayout.Result layout = LaunchSiteLayout.Generate(Seed, Radius);

        var scenery = new GameObject("Scenery").transform;
        scenery.SetParent(env, false);
        var site = scenery.gameObject.AddComponent<LaunchSite>();
        site.Setup(Seed, radii);
        var buildings = new GameObject("Buildings").transform;
        buildings.SetParent(scenery, false);
        foreach (BuildingSpot b in layout.buildings) Building(buildings, palette, b.model, b.position, b.yaw);

        // Ground (generated at load) and the instanced props (instances generated at load).
        var groundGo = new GameObject("Ground");
        groundGo.transform.SetParent(env, false);
        groundGo.AddComponent<GroundTerrain>().Setup(new TerrainShape(), TerrainLayers(), TerrainMaterial(), FarGroundMaterial(), site);

        var props = new GameObject("Props");
        props.transform.SetParent(scenery, false);
        var list = new List<InstancedScenery.Kind>();
        int total = 0;
        foreach (PropKind k in layout.kinds)
        {
            Material mat = LaunchSiteLayout.IsFoliage(k.model) ? (k.suffix == "_far" ? foliageFar : foliage) : palette;
            InstancedScenery.Kind kind = MakeKind(k.model, k.key, mat, k.shadows, k.cull);
            kind.cullPerMetreHeight = k.cullPerMetre;
            kind.thinDistance = k.thin;
            list.Add(kind);
            total += k.items.Count;
        }
        props.AddComponent<InstancedScenery>().Setup(list, site);
        Debug.Log($"[EnvironmentBuilder] {layout.buildings.Count} buildings, {total} props in {list.Count} kinds, " +
                  $"{layout.paints.Count} paints (generated in {layout.milliseconds} ms; regenerated at load)");
    }

    // ---- sea ------------------------------------------------------------------------------------

    /// <summary>Ocean (CPU-displaced near patch + static far rings) and a flat sea collider at the mean level (plain
    /// "ground": touching the water is a miss).</summary>
    static void BuildSea(Transform env, LevelConfig level)
    {
        var sea = new GameObject("Sea");
        sea.transform.SetParent(env, false);
        var ocean = sea.AddComponent<OceanSurface>();
        ocean.Setup(level.sea, OceanMaterial(level.timeOfDay == TimeOfDay.Night));

        var water = new GameObject("SeaCollider");
        water.transform.SetParent(sea.transform, false);
        var box = water.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -25f, 0f);
        box.size = new Vector3(40000f, 50f, 40000f);
    }

    static Material OceanMaterial(bool night)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = night ? "OceanNight" : "Ocean" };
        mat.SetColor("_BaseColor", night ? new Color(0.13f, 0.21f, 0.27f) : new Color(0.05f, 0.2f, 0.27f));
        mat.SetFloat("_Smoothness", night ? 0.86f : 0.78f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_EnvironmentReflections", 1f);
        mat.SetFloat("_SpecularHighlights", 1f);
        return SceneBuilder.SaveMaterial(mat);
    }

    // ---- sky, light, fog ------------------------------------------------------------------------

    /// <summary>Direction (from the ground to the light) of a point in the HDRI at the given elevation / longitude
    /// after the sky is rotated by <paramref name="rotation"/>.</summary>
    public static Vector3 SkyDirection(float elevationDeg, float longitudeDeg, float rotation)
    {
        float e = elevationDeg * Mathf.Deg2Rad, lon = longitudeDeg * Mathf.Deg2Rad;
        var v = new Vector3(Mathf.Cos(e) * Mathf.Cos(lon), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(lon));
        // Skybox/Panoramic rotates the sky by _Rotation: x' = c·x − s·z, z' = s·x + c·z.
        float r = rotation * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector3(c * v.x - s * v.z, v.y, s * v.x + c * v.z).normalized;
    }

    /// <summary>Sun direction for the default (level 1) sky rotation.</summary>
    public static Vector3 SunDirection() => SkyDirection(HdriSunElevation, HdriSunLongitude, SkyRotation);

    static Light SetupSky(LevelConfig level)
    {
        bool night = level.timeOfDay == TimeOfDay.Night;
        string hdrPath = night ? NightHdriPath : DayHdriPath;
        var hdr = AssetDatabase.LoadAssetAtPath<Texture>(hdrPath) ?? throw new FileNotFoundException(hdrPath);
        var mat = new Material(Shader.Find("Skybox/Panoramic")) { name = night ? "SkyNight" : $"Sky{level.number:00}" };
        mat.SetTexture("_MainTex", hdr);
        mat.SetFloat("_Mapping", 1f);   // latitude-longitude layout
        mat.SetFloat("_ImageType", 0f); // 360°
        mat.SetFloat("_Rotation", level.skyRotation);
        mat.SetFloat("_Exposure", night ? NightSkyExposure : 1f);
        mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        mat = SceneBuilder.SaveMaterial(mat);
        mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        EditorUtility.SetDirty(mat);

        var sunGo = new GameObject(night ? "Moon" : "Sun");
        Light sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = night ? MoonColor : SunColor;
        sun.intensity = night ? MoonIntensity : SunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = night ? 0.6f : 0.9f;
        sun.shadowBias = 0.4f;
        sun.shadowNormalBias = 0.6f;
        Vector3 dir = night ? SkyDirection(HdriMoonElevation, HdriMoonLongitude, level.skyRotation)
                            : SkyDirection(HdriSunElevation, HdriSunLongitude, level.skyRotation);
        sunGo.transform.rotation = Quaternion.LookRotation(-dir);

        RenderSettings.sun = sun;
        RenderSettings.skybox = mat;
        if (night)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 3.5f;
        }
        else
        {
            // Day: a fixed, nearly neutral sky / horizon / ground gradient instead of the clear-sky HDRI probe, whose
            // strong blue made the shadow side of the white booster read as blue paint (and varied with the sky
            // rotation from level to level). The HDRI still gives the sky, the sun direction and the reflections.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = DayAmbientSky;
            RenderSettings.ambientEquatorColor = DayAmbientEquator;
            RenderSettings.ambientGroundColor = DayAmbientGround;
            RenderSettings.ambientIntensity = 1f;
        }
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.defaultReflectionResolution = 128;
        RenderSettings.reflectionIntensity = night ? 1f : DayReflectionIntensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = night ? NightFogColor : DayFogColor;
        RenderSettings.fogDensity = level.fogDensity;
        return sun;
    }

    // ---- ground materials -----------------------------------------------------------------------

    // Tints multiply the (linear) texture colour. Targets are what farmland looks like from a plane: muted
    // olive pasture, khaki dry meadow, grey-beige gravel, dark blue-green forest, brown plough.
    static TerrainLayer[] TerrainLayers() => new[]
    {
        Layer("Grass", "Grass004/Grass004_Color.jpg", 7f, new Vector3(0.55f, 0.6f, 1.2f), 0.05f),
        Layer("Meadow", "Grass004/Grass004_Color.jpg", 29f, new Vector3(0.95f, 0.75f, 1.9f), 0.03f),
        Layer("Gravel", "Gravel023/Gravel023_Color.jpg", 5f, new Vector3(0.28f, 0.27f, 0.25f), 0.08f),
        Layer("Asphalt", "Asphalt012/Asphalt012_Color.jpg", 9f, new Vector3(0.95f, 0.95f, 0.95f), 0.15f),
        Layer("Forest", "Grass004/Grass004_Color.jpg", 23f, new Vector3(0.2f, 0.27f, 0.5f), 0.02f),
        Layer("Soil", "Gravel023/Gravel023_Color.jpg", 19f, new Vector3(0.16f, 0.12f, 0.085f), 0.05f),
    };

    static TerrainLayer Layer(string name, string texture, float tile, Vector3 tint, float smoothness)
    {
        string path = $"{EnvFolder}/{name}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AmbientCG + texture)
                               ?? throw new FileNotFoundException(AmbientCG + texture);
        layer.normalMapTexture = null;
        layer.maskMapTexture = null;
        layer.tileSize = new Vector2(tile, tile);
        layer.tileOffset = Vector2.zero;
        layer.diffuseRemapMin = Vector4.zero;
        layer.diffuseRemapMax = new Vector4(tint.x, tint.y, tint.z, 1f);
        layer.smoothness = smoothness;
        layer.metallic = 0f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    static Material TerrainMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "Terrain" };
        mat.enableInstancing = true;
        return SceneBuilder.SaveMaterial(mat);
    }

    /// <summary>The palette atlas, darkened and less glossy: the palette greens are tuned for close-ups and read as
    /// bright lime en masse; a forest seen from altitude is a dark blue-green.</summary>
    static Material FoliageMaterial(Material palette, string name, Color tint)
    {
        var mat = new Material(palette) { name = name };
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Smoothness", 0.05f);
        mat = SceneBuilder.SaveMaterial(mat);
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material FarGroundMaterial()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FarGround" };
        mat.SetColor("_BaseColor", new Color(0.3f, 0.33f, 0.2f));
        mat.SetFloat("_Smoothness", 0.05f);
        return SceneBuilder.SaveMaterial(mat);
    }

    // ---- instanced kinds ------------------------------------------------------------------------

    static GameObject LoadModel(string model)
        => AssetDatabase.LoadAssetAtPath<GameObject>(Models + model + ".fbx") ?? throw new FileNotFoundException(Models + model + ".fbx");

    /// <summary>Half the diagonal of the model's horizontal footprint (props keep clear of it), m.</summary>
    static float BuildingFootprintRadius(string model)
    {
        Bounds b = ModelBounds(LoadModel(model));
        return 0.5f * Mathf.Sqrt(b.size.x * b.size.x + b.size.z * b.size.z);
    }

    /// <summary>Bounds of all meshes of a model in its root's local space.</summary>
    static Bounds ModelBounds(GameObject root)
    {
        Bounds local = new Bounds();
        bool first = true;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Matrix4x4 m = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Bounds b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (first) { local = new Bounds(p, Vector3.zero); first = false; }
                else local.Encapsulate(p);
            }
        }
        return local;
    }

    /// <summary>A solid building (counts as plain ground: hitting it is a crash).</summary>
    static void Building(Transform parent, Material palette, string model, Vector2 at, float yaw)
    {
        GameObject asset = LoadModel(model);
        var go = (GameObject)Object.Instantiate(asset, parent, false);
        go.name = model.Replace("Scenery_", "");
        go.transform.SetPositionAndRotation(new Vector3(at.x, 0f, at.y), Quaternion.Euler(0f, yaw, 0f));
        foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            mr.sharedMaterial = palette;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        Bounds local = ModelBounds(go);
        var box = go.AddComponent<BoxCollider>();
        box.center = local.center;
        box.size = local.size;
    }

    static InstancedScenery.Kind MakeKind(string model, string name, Material palette, bool shadows, float cull)
    {
        GameObject go = LoadModel(model);
        MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length != 1) throw new InvalidDataException($"{model}: expected one mesh, found {filters.Length}");
        return new InstancedScenery.Kind
        {
            name = name,
            mesh = filters[0].sharedMesh,
            material = palette,
            meshLocal = go.transform.worldToLocalMatrix * filters[0].transform.localToWorldMatrix,
            castShadows = shadows,
            cullDistance = cull,
        };
    }
}
