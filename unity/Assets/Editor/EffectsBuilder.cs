using System;
using System.IO;
using System.Reflection;
using RocketSlam.Cameras;
using RocketSlam.Effects;
using RocketSlam.Gameplay;
using RocketSlam.Sound;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the M3 effects and sound for <see cref="SceneBuilder"/>: particle materials (Kenney CC0 sprites on the URP
/// Particles/Unlit shader), every particle system configured from code (engine flame, ground dust, explosion),
/// the bloom post-process volume, the <c>Main.mixer</c> (Master / SFX / UI, exposed MasterVolume) and the
/// <see cref="GameAudio"/> object with its clips.
/// </summary>
public static class EffectsBuilder
{
    const string ParticlesFolder = "Assets/ThirdParty/Kenney/Particles/";
    const string AudioFolder = "Assets/ThirdParty/Kenney/Audio/";
    public const string MixerPath = "Assets/Audio/Main.mixer";
    const string VolumeProfilePath = "Assets/Settings/GameVolumeProfile.asset";

    // ---- materials ------------------------------------------------------------------------------

    struct FxMaterials
    {
        public Material core, glow, fire, smoke, dust, spark, flash, debris;
    }

    static FxMaterials materials;
    static bool materialsReady;

    static FxMaterials Materials()
    {
        if (materialsReady && materials.core != null) return materials;
        materials = new FxMaterials
        {
            core = Particle("FxFlameCore", "circle_05", new Color(3f, 2.5f, 1.9f), additive: true, queueOffset: 10),
            glow = Particle("FxFlameGlow", "circle_05", new Color(2.2f, 1.3f, 0.6f), additive: true, queueOffset: 10),
            fire = Particle("FxFire", "fire_01", new Color(3f, 2.2f, 1.6f), additive: false),
            smoke = Particle("FxSmoke", "smoke_04", Color.white, additive: false),
            dust = Particle("FxDust", "smoke_04", Color.white, additive: false),
            spark = Particle("FxSpark", "circle_05", new Color(3f, 2.1f, 1.2f), additive: true, queueOffset: 10),
            flash = Particle("FxFlash", "circle_05", new Color(3f, 2.4f, 1.6f), additive: true, queueOffset: 10),
            debris = Debris(),
        };
        materialsReady = true;
        return materials;
    }

    static Material Particle(string name, string texture, Color baseColor, bool additive, int queueOffset = 0)
    {
        string texPath = ParticlesFolder + texture + ".png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) ?? throw new FileNotFoundException(texPath);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
        mat = SceneBuilder.SaveMaterial(mat);
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_ColorMode", 0f);
        mat.SetFloat("_QueueOffset", queueOffset);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent + queueOffset;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material Debris()
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FxDebris" };
        mat.SetColor("_BaseColor", new Color(0.13f, 0.12f, 0.115f));
        mat.SetFloat("_Smoothness", 0.25f);
        mat.enableInstancing = true;
        return SceneBuilder.SaveMaterial(mat);
    }

    // ---- particle helpers -----------------------------------------------------------------------

    static ParticleSystem NewSystem(string name, Transform parent, Material mat, bool local, int maxParticles, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = loop ? 1f : 5f;
        main.loop = loop;
        main.playOnAwake = loop;
        main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = maxParticles;
        main.startColor = Color.white;
        main.gravityModifier = 0f;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.minParticleSize = 0f;
        r.maxParticleSize = 4f; // big smoke/fireball sprites may cover the screen up close
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return ps;
    }

    static ParticleSystem.MinMaxCurve Range(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

    static ParticleSystem.MinMaxCurve Curve(params float[] keys)
    {
        var c = new AnimationCurve();
        for (int i = 0; i < keys.Length; i += 2) c.AddKey(keys[i], keys[i + 1]);
        return new ParticleSystem.MinMaxCurve(1f, c);
    }

    /// <summary>Colour gradient from (time, r, g, b) keys and (time, alpha) keys.</summary>
    static ParticleSystem.MinMaxGradient Gradient(float[] colorKeys, float[] alphaKeys)
    {
        var ck = new GradientColorKey[colorKeys.Length / 4];
        for (int i = 0; i < ck.Length; i++)
            ck[i] = new GradientColorKey(new Color(colorKeys[i * 4 + 1], colorKeys[i * 4 + 2], colorKeys[i * 4 + 3]), colorKeys[i * 4]);
        var ak = new GradientAlphaKey[alphaKeys.Length / 2];
        for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(alphaKeys[i * 2 + 1], alphaKeys[i * 2]);
        var g = new Gradient();
        g.SetKeys(ck, ak);
        return new ParticleSystem.MinMaxGradient(g);
    }

    static void SizeOverLife(ParticleSystem ps, params float[] keys)
    {
        ParticleSystem.SizeOverLifetimeModule m = ps.sizeOverLifetime;
        m.enabled = true;
        m.size = Curve(keys);
    }

    static void ColorOverLife(ParticleSystem ps, float[] colorKeys, float[] alphaKeys)
    {
        ParticleSystem.ColorOverLifetimeModule m = ps.colorOverLifetime;
        m.enabled = true;
        m.color = Gradient(colorKeys, alphaKeys);
    }

    static void Spin(ParticleSystem ps, float degPerSecond)
    {
        ParticleSystem.MainModule main = ps.main;
        main.startRotation = Range(0f, Mathf.PI * 2f);
        ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = Range(-degPerSecond * Mathf.Deg2Rad, degPerSecond * Mathf.Deg2Rad);
    }

    static void Drag(ParticleSystem ps, float drag)
    {
        ParticleSystem.LimitVelocityOverLifetimeModule lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 1000f;
        lv.dampen = 0f;
        lv.drag = drag;
        lv.multiplyDragByParticleSize = false;
        lv.multiplyDragByParticleVelocity = false;
    }

    static void NoShape(ParticleSystem ps)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;
    }

    static void Sphere(ParticleSystem ps, float radius)
    {
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.radiusThickness = 1f;
    }

    static void Burst(ParticleSystem ps, int count, float time = 0f)
    {
        ParticleSystem.EmissionModule e = ps.emission;
        e.enabled = true;
        int n = e.burstCount;
        var bursts = new ParticleSystem.Burst[n + 1];
        e.GetBursts(bursts);
        bursts[n] = new ParticleSystem.Burst(time, (short)count);
        e.SetBursts(bursts);
    }

    // ---- engine ---------------------------------------------------------------------------------

    /// <summary>Flame rig under the rocket root at the nozzle exit; flame along local -Y (gimballed at runtime).</summary>
    public static EngineFlame BuildEngineFlame(Transform rocketRoot, float nozzleExitY)
    {
        FxMaterials m = Materials();
        var root = new GameObject("EngineFx");
        root.transform.SetParent(rocketRoot, false);
        root.transform.localPosition = new Vector3(0f, nozzleExitY, 0f);

        // Core: white-hot streaks rushing out of the nozzle.
        ParticleSystem core = NewSystem("FlameCore", root.transform, m.core, local: true, maxParticles: 40, loop: true);
        {
            ParticleSystem.MainModule main = core.main;
            main.startLifetime = 0.1f;
            main.startSpeed = 60f;
            main.startSize = Range(2.2f, 2.6f);
            ParticleSystem.ShapeModule shape = core.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 2f;
            shape.radius = 0.5f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f); // cone axis +Z -> local -Y
            SizeOverLife(core, 0f, 1f, 1f, 0.45f);
            ColorOverLife(core, new[] { 0f, 1f, 0.97f, 0.9f, 0.5f, 1f, 0.8f, 0.5f, 1f, 1f, 0.5f, 0.25f },
                new[] { 0f, 1f, 0.6f, 0.8f, 1f, 0f });
            var r = core.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2f;
            r.velocityScale = 0.04f;
            r.cameraVelocityScale = 0f;
        }

        // Plume: orange billowing fire around and beyond the core.
        ParticleSystem plume = NewSystem("FlamePlume", root.transform, m.fire, local: true, maxParticles: 30, loop: true);
        {
            ParticleSystem.MainModule main = plume.main;
            main.startLifetime = Range(0.16f, 0.22f);
            main.startSpeed = 80f;
            main.startSize = Range(3f, 4.2f);
            ParticleSystem.ShapeModule shape = plume.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 4f;
            shape.radius = 0.7f;
            shape.radiusThickness = 1f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            Spin(plume, 120f);
            SizeOverLife(plume, 0f, 0.8f, 1f, 3.2f);
            ColorOverLife(plume, new[] { 0f, 1f, 0.92f, 0.65f, 0.35f, 1f, 0.6f, 0.22f, 1f, 0.7f, 0.25f, 0.1f },
                new[] { 0f, 1f, 0.5f, 0.85f, 1f, 0f });
        }

        // Glow: a soft bright halo right at the nozzle exit.
        ParticleSystem glow = NewSystem("FlameGlow", root.transform, m.glow, local: true, maxParticles: 6, loop: true);
        {
            glow.transform.localPosition = new Vector3(0f, -1.2f, 0f);
            ParticleSystem.MainModule main = glow.main;
            main.startLifetime = 0.08f;
            main.startSpeed = 0f;
            main.startSize = 10f;
            NoShape(glow);
            ColorOverLife(glow, new[] { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }, new[] { 0f, 0.8f, 1f, 0f });
        }

        var lightGo = new GameObject("FlameLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, -4f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.66f, 0.32f);
        light.range = 45f;
        light.intensity = 6f;
        light.shadows = LightShadows.None;
        light.enabled = false;

        EngineFlame flame = root.AddComponent<EngineFlame>();
        flame.Setup(core, plume, glow, light);
        return flame;
    }

    // ---- ground dust ----------------------------------------------------------------------------

    /// <summary>Dust puffs per second at full intensity.</summary>
    const float DustMaxRate = 32f;

    public static GroundDust BuildGroundDust(Transform parent)
    {
        FxMaterials m = Materials();
        var root = new GameObject("GroundDust");
        root.transform.SetParent(parent, false);
        ParticleSystem ps = NewSystem("Dust", root.transform, m.dust, local: false, maxParticles: 70, loop: true);
        ParticleSystem.MainModule main = ps.main;
        // M4b: fewer, smaller puffs (the old 260 × 7-12 m growing to 31 m dropped the iGPU to ~35 FPS at touchdown).
        // M6: fewer and shorter-lived again, and no sprite larger than 40 % of the screen: the blended overdraw of
        // big puffs right in front of the chase camera was the last slow frames of the descent.
        main.startLifetime = Range(2.2f, 3.4f);
        main.startSpeed = Range(10f, 20f);
        main.startSize = Range(4.5f, 7.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.82f, 0.79f, 0.74f), new Color(0.96f, 0.95f, 0.92f));
        main.gravityModifier = -0.03f; // warm dust drifts up a little
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 3f;
        shape.radiusThickness = 0.2f;
        shape.rotation = new Vector3(90f, 0f, 0f); // flat on the ground, particles fly outward
        shape.randomDirectionAmount = 0.08f;
        Drag(ps, 1.1f);
        Spin(ps, 25f);
        SizeOverLife(ps, 0f, 0.5f, 1f, 1.8f);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.4f;
        ColorOverLife(ps, new[] { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }, new[] { 0f, 0f, 0.1f, 0.8f, 0.5f, 0.5f, 1f, 0f });

        GroundDust dust = root.AddComponent<GroundDust>();
        dust.Setup(ps, DustMaxRate);
        return dust;
    }

    // ---- explosion ------------------------------------------------------------------------------

    public static ExplosionEffect BuildExplosion(Transform parent)
    {
        FxMaterials m = Materials();
        var root = new GameObject("Explosion");
        root.transform.SetParent(parent, false);

        ParticleSystem flash = NewSystem("Flash", root.transform, m.flash, local: false, maxParticles: 3, loop: false);
        {
            ParticleSystem.MainModule main = flash.main;
            main.duration = 0.5f;
            main.startLifetime = 0.35f;
            main.startSpeed = 0f;
            main.startSize = Range(45f, 60f);
            NoShape(flash);
            Burst(flash, 3);
            SizeOverLife(flash, 0f, 0.6f, 1f, 1.3f);
            ColorOverLife(flash, new[] { 0f, 1f, 1f, 1f, 1f, 1f, 0.7f, 0.4f }, new[] { 0f, 1f, 1f, 0f });
        }

        ParticleSystem fireball = NewSystem("Fireball", root.transform, m.fire, local: false, maxParticles: 30, loop: false);
        {
            ParticleSystem.MainModule main = fireball.main;
            main.duration = 1f;
            main.startLifetime = Range(1.1f, 1.9f);
            main.startSpeed = Range(6f, 18f);
            main.startSize = Range(12f, 22f);
            main.gravityModifier = -0.15f;
            Sphere(fireball, 4f);
            Burst(fireball, 24);
            Drag(fireball, 2f);
            Spin(fireball, 50f);
            SizeOverLife(fireball, 0f, 0.5f, 0.3f, 1.1f, 1f, 1.7f);
            ColorOverLife(fireball,
                new[] { 0f, 1f, 0.95f, 0.75f, 0.3f, 1f, 0.55f, 0.15f, 0.65f, 0.25f, 0.12f, 0.08f, 1f, 0.1f, 0.1f, 0.1f },
                new[] { 0f, 1f, 0.35f, 0.95f, 0.7f, 0.6f, 1f, 0f });
        }

        ParticleSystem smoke = NewSystem("SmokeColumn", root.transform, m.smoke, local: false, maxParticles: 90, loop: false);
        {
            ParticleSystem.MainModule main = smoke.main;
            main.duration = 4f;
            main.startLifetime = Range(6f, 9f);
            main.startSpeed = Range(2f, 6f);
            main.startSize = Range(14f, 24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.16f, 0.15f, 0.14f), new Color(0.3f, 0.29f, 0.28f));
            Sphere(smoke, 6f);
            ParticleSystem.EmissionModule e = smoke.emission;
            e.rateOverTime = 12f;
            Burst(smoke, 14);
            ParticleSystem.VelocityOverLifetimeModule v = smoke.velocityOverLifetime;
            v.enabled = true;
            v.space = ParticleSystemSimulationSpace.World;
            v.x = Range(-1f, 1f);
            v.y = Range(6f, 11f);
            v.z = Range(-1f, 1f);
            Drag(smoke, 0.6f);
            Spin(smoke, 15f);
            SizeOverLife(smoke, 0f, 0.6f, 1f, 2.6f);
            ColorOverLife(smoke, new[] { 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f }, new[] { 0f, 0f, 0.08f, 0.9f, 0.6f, 0.55f, 1f, 0f });
        }

        ParticleSystem sparks = NewSystem("Sparks", root.transform, m.spark, local: false, maxParticles: 80, loop: false);
        {
            ParticleSystem.MainModule main = sparks.main;
            main.duration = 0.5f;
            main.startLifetime = Range(0.8f, 2f);
            main.startSpeed = Range(20f, 65f);
            main.startSize = Range(0.35f, 0.7f);
            main.gravityModifier = 1f;
            Sphere(sparks, 2f);
            Burst(sparks, 70);
            Drag(sparks, 0.8f);
            ColorOverLife(sparks, new[] { 0f, 1f, 0.9f, 0.6f, 1f, 1f, 0.4f, 0.1f }, new[] { 0f, 1f, 0.7f, 0.9f, 1f, 0f });
            var r = sparks.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1f;
            r.velocityScale = 0.06f;
            r.cameraVelocityScale = 0f;
        }

        ParticleSystem debris = NewSystem("Debris", root.transform, m.debris, local: false, maxParticles: 26, loop: false);
        {
            ParticleSystem.MainModule main = debris.main;
            main.duration = 0.5f;
            main.startLifetime = Range(5f, 7f);
            main.startSpeed = Range(15f, 40f);
            main.startSize3D = true; // plates and shards rather than cubes
            main.startSizeX = Range(0.5f, 1.6f);
            main.startSizeY = Range(0.12f, 0.35f);
            main.startSizeZ = Range(0.3f, 1.1f);
            main.startRotation3D = true;
            main.startRotationX = Range(0f, Mathf.PI * 2f);
            main.startRotationY = Range(0f, Mathf.PI * 2f);
            main.startRotationZ = Range(0f, Mathf.PI * 2f);
            main.gravityModifier = 1f;
            Sphere(debris, 3f);
            Burst(debris, 26);
            ParticleSystem.RotationOverLifetimeModule rot = debris.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = Range(-6f, 6f);
            rot.y = Range(-6f, 6f);
            rot.z = Range(-6f, 6f);
            ParticleSystem.CollisionModule col = debris.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;
            col.dampen = 0.45f;
            col.bounce = 0.25f;
            col.radiusScale = 0.5f;
            col.collidesWith = ~(1 << 2);
            var r = debris.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.alignment = ParticleSystemRenderSpace.World;
            r.sortMode = ParticleSystemSortMode.None;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
        }

        var lightGo = new GameObject("FlashLight");
        lightGo.transform.SetParent(root.transform, false);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.7f, 0.38f);
        light.range = 160f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.enabled = false;

        ExplosionEffect fx = root.AddComponent<ExplosionEffect>();
        fx.Setup(new[] { flash, fireball, smoke, sparks, debris }, light);
        return fx;
    }

    // ---- post-processing ------------------------------------------------------------------------

    /// <summary>Global volume with bloom so the HDR flame and fireball glow; enables post-processing on the camera.</summary>
    public static void BuildPostProcessing(Camera cam)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);
        }
        if (!profile.TryGet(out Bloom bloom))
        {
            bloom = profile.Add<Bloom>(true);
            bloom.name = "Bloom";
            AssetDatabase.AddObjectToAsset(bloom, profile);
        }
        bloom.active = true;
        // Only emissive sources bloom (flame and spark particles stack to 3+, deck lights 4-7). A sunlit white body
        // reaches ~2.3 (albedo 0.89 × sun 2.6 × cos 43° + sky ambient); threshold 1 haloed it on sea / L2 views.
        bloom.threshold.Override(2.5f);
        bloom.intensity.Override(0.8f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(false);
        bloom.maxIterations.Override(5);
        EditorUtility.SetDirty(bloom);

        Tonemapping tone = VolumeComp<Tonemapping>(profile, "Tonemapping");
        tone.mode.Override(TonemappingMode.ACES);
        ColorAdjustments color = VolumeComp<ColorAdjustments>(profile, "ColorAdjustments");
        color.postExposure.Override(0.15f);
        color.contrast.Override(12f);
        color.saturation.Override(0f);
        WhiteBalance white = VolumeComp<WhiteBalance>(profile, "WhiteBalance");
        white.temperature.Override(4f);
        Vignette vignette = VolumeComp<Vignette>(profile, "Vignette");
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.45f);
        EditorUtility.SetDirty(profile);

        var go = new GameObject("PostProcess");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        cam.allowHDR = true;
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
    }

    static T VolumeComp<T>(VolumeProfile profile, string name) where T : VolumeComponent
    {
        if (!profile.TryGet(out T c))
        {
            c = profile.Add<T>(true);
            c.name = name;
            AssetDatabase.AddObjectToAsset(c, profile);
        }
        c.active = true;
        EditorUtility.SetDirty(c);
        return c;
    }

    // ---- audio ----------------------------------------------------------------------------------

    public static GameAudio BuildAudio(LandingSession session, CameraDirector cameras, PlayerControls controls)
    {
        AudioMixer mixer = EnsureMixer();
        AudioMixerGroup sfx = Group(mixer, "SFX");
        AudioMixerGroup ui = Group(mixer, "UI");
        var clips = new GameAudio.Clips
        {
            engineLoop = Clip("Sci-Fi/engine_loop"),
            engineLowLoop = Clip("Sci-Fi/engine_low_loop"),
            explosion1 = Clip("Sci-Fi/explosion_01"),
            explosion2 = Clip("Sci-Fi/explosion_02"),
            explosionRumble = Clip("Sci-Fi/explosion_rumble"),
            legClunk = Clip("Impact/leg_clunk"),
            legLatch = Clip("Impact/leg_latch"),
            landingThud = Clip("Impact/landing_thud"),
            uiClick = Clip("UI/ui_click"),
            uiConfirm = Clip("UI/ui_confirm"),
            uiBack = Clip("UI/ui_back"),
            uiError = Clip("UI/ui_error"),
        };
        var go = new GameObject("Audio");
        GameAudio audio = go.AddComponent<GameAudio>();
        audio.Setup(session, cameras, controls, mixer, sfx, ui, clips);
        return audio;
    }

    /// <summary>UI sounds for the main menu scene (same clips and mixer group as the levels' UI sounds).</summary>
    public static MenuAudio BuildMenuAudio()
    {
        AudioMixer mixer = EnsureMixer();
        var go = new GameObject("MenuAudio");
        MenuAudio audio = go.AddComponent<MenuAudio>();
        audio.Setup(mixer, Group(mixer, "UI"), Clip("UI/ui_click"), Clip("UI/ui_confirm"), Clip("UI/ui_back"), Clip("UI/ui_error"));
        return audio;
    }

    static AudioClip Clip(string name)
    {
        string path = AudioFolder + name + ".ogg";
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new FileNotFoundException(path);
    }

    static AudioMixerGroup Group(AudioMixer mixer, string name)
    {
        foreach (AudioMixerGroup g in mixer.FindMatchingGroups(name))
            if (g.name == name) return g;
        throw new InvalidDataException($"Mixer group '{name}' missing in {MixerPath}");
    }

    /// <summary>
    /// Load <see cref="MixerPath"/> or create it (only when missing, so hand tuning in the mixer window survives).
    /// Unity has no public API to author mixers, so this uses the editor's internal AudioMixerController by reflection.
    /// </summary>
    public static AudioMixer EnsureMixer()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (existing != null) return existing;
        Directory.CreateDirectory(Path.GetDirectoryName(MixerPath));

        Assembly editor = typeof(UnityEditor.Editor).Assembly;
        Type controllerType = editor.GetType("UnityEditor.Audio.AudioMixerController", true);
        object controller = Invoke(controllerType, null, "CreateMixerControllerAtPath", MixerPath);
        object master = controllerType.GetProperty("masterGroup", All).GetValue(controller);
        var groups = new System.Collections.Generic.List<object> { master };
        foreach (string name in new[] { "SFX", "UI" })
        {
            object group = Invoke(controllerType, controller, "CreateNewGroup", name, false);
            Invoke(controllerType, controller, "AddChildToParent", group, master);
            groups.Add(group);
        }
        AddDefaultView(editor, controllerType, controller, groups);

        object volumeGuid = Invoke(master.GetType(), master, "GetGUIDForVolume");
        Type pathType = editor.GetType("UnityEditor.Audio.AudioGroupParameterPath", true);
        object parameterPath = Activator.CreateInstance(pathType, All, null, new[] { master, volumeGuid }, null);
        Invoke(controllerType, controller, "AddExposedParameter", parameterPath);
        PropertyInfo exposedProp = controllerType.GetProperty("exposedParameters", All);
        var exposed = (Array)exposedProp.GetValue(controller);
        object p = exposed.GetValue(0);
        p.GetType().GetField("name", All).SetValue(p, GameAudio.MasterVolumeParameter);
        exposed.SetValue(p, 0);
        exposedProp.SetValue(controller, exposed);
        MethodInfo changed = FindMethod(controllerType, "OnChangedExposedParameter", 0);
        changed?.Invoke(controller, null);

        EditorUtility.SetDirty((Object)controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(MixerPath);
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (mixer == null) throw new IOException("Could not create " + MixerPath);
        Debug.Log("[EffectsBuilder] Created " + MixerPath);
        return mixer;
    }

    /// <summary>The mixer window lists only groups in the current view; a new controller has no views.</summary>
    static void AddDefaultView(Assembly editor, Type controllerType, object controller, System.Collections.Generic.List<object> groups)
    {
        Type viewType = editor.GetType("UnityEditor.Audio.MixerGroupView", true);
        PropertyInfo groupId = groups[0].GetType().GetProperty("groupID", All);
        var ids = new GUID[groups.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = (GUID)groupId.GetValue(groups[i]);
        object view = Activator.CreateInstance(viewType);
        viewType.GetField("guids", All).SetValue(view, ids);
        viewType.GetField("name", All).SetValue(view, "View");
        Array views = Array.CreateInstance(viewType, 1);
        views.SetValue(view, 0);
        controllerType.GetProperty("views", All).SetValue(controller, views);
        controllerType.GetProperty("currentViewIndex", All)?.SetValue(controller, 0);
    }

    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    static MethodInfo FindMethod(Type type, string name, int parameterCount)
    {
        foreach (MethodInfo m in type.GetMethods(All))
            if (m.Name == name && m.GetParameters().Length == parameterCount) return m;
        return null;
    }

    static object Invoke(Type type, object target, string name, params object[] args)
    {
        MethodInfo m = FindMethod(type, name, args.Length)
                       ?? throw new MissingMethodException(type.FullName, name);
        return m.Invoke(target, args);
    }
}
