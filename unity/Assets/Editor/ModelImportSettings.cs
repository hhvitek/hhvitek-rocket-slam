using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the Blender-generated models (<c>Assets/Models/*.fbx</c>) and their palette atlas
/// (<c>Assets/Textures/palette.png</c>). Settings live here so they are reproducible; the material remap to
/// <c>Assets/Materials/Palette.mat</c> is written into the FBX .meta files by <see cref="EnsureSetup"/>.
/// </summary>
public class ModelImportSettings : AssetPostprocessor
{
    public const string ModelsFolder = "Assets/Models/";
    public const string PaletteTexturePath = "Assets/Textures/palette.png";
    public const string PaletteMaterialPath = "Assets/Materials/Palette.mat";
    public const string BoosterPath = ModelsFolder + "Booster.fbx";
    public const string LandingPadPath = ModelsFolder + "LandingPad.fbx";
    public const string DroneshipPath = ModelsFolder + "Droneship.fbx";
    const string PaletteSlot = "Palette";

    void OnPreprocessTexture()
    {
        if (assetPath != PaletteTexturePath) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = false; // flat swatches: no mip bleeding between neighbours
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.isReadable = false;
        ti.alphaSource = TextureImporterAlphaSource.None;
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(ModelsFolder)) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.isReadable = false;
        mi.importNormals = ModelImporterNormals.Import; // flat shading authored in Blender
        mi.importBlendShapes = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importVisibility = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.addCollider = false;
    }

    /// <summary>Create/refresh Palette.mat and remap the "Palette" slot of every model to it. Idempotent.</summary>
    public static Material EnsureSetup()
    {
        Material mat = EnsurePaletteMaterial();
        foreach (string path in new[] { BoosterPath, LandingPadPath, DroneshipPath })
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) throw new FileNotFoundException(path);
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), PaletteSlot);
            if (mi.GetExternalObjectMap().TryGetValue(id, out Object existing) && existing == mat) continue;
            mi.AddRemap(id, mat);
            mi.SaveAndReimport();
        }
        return mat;
    }

    static Material EnsurePaletteMaterial()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteTexturePath)
                  ?? throw new FileNotFoundException(PaletteTexturePath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
        bool created = mat == null;
        if (created) mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Palette" };
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetFloat("_Metallic", 0f);
        mat.enableInstancing = true; // scenery props are drawn with Graphics.RenderMeshInstanced
        if (created) AssetDatabase.CreateAsset(mat, PaletteMaterialPath);
        else EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Debug: log every node of the imported models (local pose, mesh bounds, materials).</summary>
    [MenuItem("Rocket Slam/Dump Model Hierarchies")]
    public static void DumpModels()
    {
        EnsureSetup();
        var sb = new StringBuilder();
        foreach (string path in new[] { BoosterPath, LandingPadPath, DroneshipPath })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            sb.AppendLine($"== {path}");
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                string parent = t.parent != null ? t.parent.name : "-";
                sb.Append($"  {t.name} (parent {parent}) pos {t.localPosition:F4} rot {t.localRotation.eulerAngles:F2} scale {t.localScale:F3}");
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    sb.Append($" mesh {mf.sharedMesh.name} bounds c{mf.sharedMesh.bounds.center:F3} s{mf.sharedMesh.bounds.size:F3} v{mf.sharedMesh.vertexCount}");
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                    foreach (Material m in mr.sharedMaterials) sb.Append($" mat {(m != null ? AssetDatabase.GetAssetPath(m) : "null")}");
                sb.AppendLine();
            }
        }
        Debug.Log("[ModelDump]\n" + sb);
    }
}
