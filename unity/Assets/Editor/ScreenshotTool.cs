using System;
using System.IO;
using RocketSlam.Cameras;
using RocketSlam.Gameplay;
using RocketSlam.Hud;
using RocketSlam.Rocket;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Renders fixed views of Level01 from the scene cameras into <c>screenshots/</c> (repo root), without
/// entering play mode. Run with graphics (no -nographics):
/// <c>Unity -batchmode -projectPath unity -executeMethod ScreenshotTool.CaptureM1 -quit</c>.
/// Renders every camera view (chase, engine, pad-side) at 3000 m, 1000 m, 150 m and landed.
/// The UI Toolkit HUD is an overlay and does not appear in camera renders; HUD shots come from the
/// player's <c>-autocapture</c> mode (see <c>AutoCapture</c>).
/// </summary>
public static class ScreenshotTool
{
    const int Width = 1920, Height = 1080;

    static string OutputFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots"));

    [MenuItem("Rocket Slam/Capture M1 Screenshots")]
    public static void CaptureM1()
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            EditorSceneManager.OpenScene(SceneBuilder.Level01Path);
            var session = Object.FindAnyObjectByType<LandingSession>();
            var director = Object.FindAnyObjectByType<CameraDirector>();
            RocketController rocket = session.Rocket;
            rocket.ReferenceHeight = session.PadTopY;
            Vector3 target = session.TargetPosition;

            var cue = Object.FindAnyObjectByType<GroundCue>();

            // The very first render after opening a scene has no ambient/shadow data yet: render once and discard.
            Pose(rocket, target + new Vector3(0f, 2.2f, 0f), Quaternion.identity, false, 1f, 1f);
            director.Snap();
            Render(director.ActiveCamera, null);

            LevelConfig level = session.Level;
            var shots = new (string name, Vector3 offset, Quaternion rot, bool engine, float throttle, float legs)[]
            {
                ("3000", new Vector3(level.startOffset.x, level.startAltitude, level.startOffset.y), Quaternion.identity, false, 1f, 0f),
                ("1000", new Vector3(35f, 1000f, -22f), Quaternion.Euler(2f, 0f, 3f), true, 0.6f, 0f),
                ("150", new Vector3(6f, 150f, -4f), Quaternion.Euler(1f, 0f, 1f), true, 0.7f, 1f),
                ("landed", new Vector3(0f, 2.2f, 0f), Quaternion.identity, false, 1f, 1f),
            };
            foreach (var shot in shots)
            {
                Pose(rocket, target + shot.offset, shot.rot, shot.engine, shot.throttle, shot.legs);
                foreach (CameraView view in new[] { CameraView.Chase, CameraView.Engine, CameraView.PadSide })
                {
                    director.SetView(view, blend: false);
                    director.Snap();
                    if (cue != null) cue.Refresh();
                    Render(director.ActiveCamera, $"m1_cam_{view.ToString().ToLowerInvariant()}_{shot.name}");
                }
            }
            director.SetView(CameraView.Chase, blend: false);

            Debug.Log($"[ScreenshotTool] Screenshots written to {OutputFolder}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenshotTool] Failed: {e}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw;
        }
    }

    /// <summary>
    /// M2 model check: close-ups of the Booster (stowed, legs deploying, landed feet on the pad), the chase
    /// camera at 1000 m and the droneship prefab in a throwaway scene. Writes <c>screenshots/m2_*.png</c>.
    /// </summary>
    [MenuItem("Rocket Slam/Capture M2 Screenshots")]
    public static void CaptureM2()
    {
        try
        {
            Directory.CreateDirectory(OutputFolder);
            EditorSceneManager.OpenScene(SceneBuilder.Level01Path);
            var session = Object.FindAnyObjectByType<LandingSession>();
            var director = Object.FindAnyObjectByType<CameraDirector>();
            var cue = Object.FindAnyObjectByType<GroundCue>();
            RocketController rocket = session.Rocket;
            rocket.ReferenceHeight = session.PadTopY;
            Vector3 target = session.TargetPosition;
            Camera cam = director.ActiveCamera;

            Pose(rocket, target + new Vector3(0f, 2.2f, 0f), Quaternion.identity, false, 1f, 1f);
            director.Snap();
            Render(cam, null); // warm-up render

            // High altitude, everything stowed; then fins out.
            Vector3 high = target + new Vector3(60f, 2000f, -40f);
            Pose(rocket, high, Quaternion.Euler(0f, 20f, 0f), false, 1f, 0f, 0f);
            RenderFrom(cam, high + new Vector3(-22f, 12f, -34f), high + new Vector3(0f, 18f, 0f), 50f, "m2_stowed_closeup");
            RenderFrom(cam, high + new Vector3(-8f, 46f, -12f), high + new Vector3(0f, 36f, 0f), 50f, "m2_stowed_top");
            // Lettering faces azimuth 112.5° (rocket-local); readable text = model not mirrored.
            Pose(rocket, high, Quaternion.identity, false, 1f, 0f, 0f);
            Vector3 letterDir = new Vector3(Mathf.Cos(112.5f * Mathf.Deg2Rad), 0f, Mathf.Sin(112.5f * Mathf.Deg2Rad));
            RenderFrom(cam, high + letterDir * 18f + new Vector3(0f, 27f, 0f), high + new Vector3(0f, 27f, 0f), 40f, "m2_lettering");
            Pose(rocket, high, Quaternion.Euler(0f, 20f, 0f), false, 1f, 0f, 1f);
            RenderFrom(cam, high + new Vector3(-8f, 46f, -12f), high + new Vector3(0f, 36f, 0f), 50f, "m2_fins_deployed_top");

            // Legs half way, engine burning.
            Vector3 mid = target + new Vector3(10f, 200f, -6f);
            Pose(rocket, mid, Quaternion.identity, true, 0.8f, 0.5f);
            RenderFrom(cam, mid + new Vector3(-30f, 4f, -36f), mid + new Vector3(0f, 8f, 0f), 50f, "m2_legs_deploying");
            Pose(rocket, mid, Quaternion.identity, true, 0.8f, 1f);
            RenderFrom(cam, mid + new Vector3(-30f, -6f, -36f), mid + new Vector3(0f, 6f, 0f), 50f, "m2_legs_deployed_air");

            // Landed: feet resting on the pad (foot pad bottom 2.202 m below the rocket origin).
            Vector3 landed = target + new Vector3(0f, 2.202f, 0f);
            Pose(rocket, landed, Quaternion.identity, false, 1f, 1f);
            RenderFrom(cam, landed + new Vector3(0f, -0.4f, -36f), landed + new Vector3(0f, 6f, 0f), 45f, "m2_landed_side");
            RenderFrom(cam, landed + new Vector3(4f, -1.2f, -10f), landed + new Vector3(7.08f, -2.2f, -7.08f), 35f, "m2_landed_foot");
            RenderFrom(cam, landed + new Vector3(-55f, 30f, -60f), landed + new Vector3(0f, 10f, 0f), 50f, "m2_landed_pad");
            LogFootHeights(rocket, session.PadTopY);

            // Chase camera at 1000 m.
            Pose(rocket, target + new Vector3(35f, 1000f, -22f), Quaternion.Euler(2f, 0f, 3f), true, 0.6f, 0f);
            director.SetView(CameraView.Chase, blend: false);
            director.Snap();
            if (cue != null) cue.Refresh();
            Render(director.ActiveCamera, "m2_chase_1000");

            CaptureDroneship();
            Debug.Log($"[ScreenshotTool] M2 screenshots written to {OutputFolder}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ScreenshotTool] Failed: {e}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw;
        }
    }

    static void LogFootHeights(RocketController rocket, float padTop)
    {
        for (int i = 0; i < rocket.LegCount; i++)
        {
            RocketLeg leg = rocket.GetLeg(i);
            Vector3 local = rocket.transform.InverseTransformPoint(leg.FootPosition);
            float r = new Vector2(local.x, local.z).magnitude;
            Debug.Log($"[ScreenshotTool] Foot {i}: local {local:F3} r {r:F3}, collider bottom {leg.FootCollider.bounds.min.y - padTop:F3} m above pad top");
        }
    }

    /// <summary>Droneship prefab in a throwaway scene with a flat sea and a landed booster on the deck.</summary>
    static void CaptureDroneship()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var light = Object.FindAnyObjectByType<Light>();
        light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
        light.intensity = 1.3f;
        Camera cam = Camera.main;
        cam.farClipPlane = 5000f;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SceneBuilder.DroneshipPrefabPath);
        var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        ship.transform.position = Vector3.zero;

        var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
        sea.transform.position = new Vector3(0f, -3f, 0f); // waterline 3 m below the deck
        sea.transform.localScale = new Vector3(200f, 1f, 200f);
        var seaMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        seaMat.SetColor("_BaseColor", new Color(0.12f, 0.27f, 0.38f));
        seaMat.SetFloat("_Smoothness", 0.7f);
        sea.GetComponent<MeshRenderer>().sharedMaterial = seaMat;

        var config = AssetDatabase.LoadAssetAtPath<RocketConfig>(SceneBuilder.RocketConfigPath);
        RocketController rocket = SceneBuilder.BuildRocket(config);
        float deckTop = ship.transform.Find("Deck").GetComponent<Collider>().bounds.max.y;
        Pose(rocket, new Vector3(0f, deckTop + 2.202f, 0f), Quaternion.identity, false, 1f, 1f);

        Render(cam, null);
        RenderFrom(cam, new Vector3(-85f, 45f, -95f), new Vector3(0f, 0f, 0f), 50f, "m2_droneship");
        RenderFrom(cam, new Vector3(-30f, 4f, -40f), new Vector3(0f, 12f, 0f), 50f, "m2_droneship_landed");
        Object.DestroyImmediate(seaMat);
    }

    static void RenderFrom(Camera cam, Vector3 position, Vector3 lookAt, float fov, string name)
    {
        float oldFov = cam.fieldOfView, oldNear = cam.nearClipPlane;
        Vector3 oldPos = cam.transform.position;
        Quaternion oldRot = cam.transform.rotation;
        cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.3f;
        Render(cam, name);
        cam.transform.SetPositionAndRotation(oldPos, oldRot);
        cam.fieldOfView = oldFov;
        cam.nearClipPlane = oldNear;
    }

    static void Pose(RocketController rocket, Vector3 position, Quaternion rotation, bool engineOn, float throttle, float legs, float fins = 1f)
    {
        rocket.transform.SetPositionAndRotation(position, rotation);
        rocket.SetPreviewState(engineOn, throttle, legs, fins);
        Physics.SyncTransforms();
    }
    static void Render(Camera cam, string name)
    {
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        bool wasEnabled = cam.enabled;
        cam.enabled = true;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        cam.enabled = wasEnabled;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;

        if (name != null)
        {
            string path = Path.Combine(OutputFolder, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[ScreenshotTool] {path}");
        }
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
