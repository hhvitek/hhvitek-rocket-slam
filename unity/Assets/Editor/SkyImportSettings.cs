using UnityEditor;

/// <summary>
/// Import settings for the lat-long sky panoramas (Skybox/Panoramic): no mipmaps. With mipmaps the longitude wrap
/// (u = 0 / 1) makes the screen-space UV derivative jump inside each 2×2 pixel quad, the GPU picks the smallest mip
/// there and a thin dashed vertical line appears in the sky at that heading (seen on level 4, sky rotation 290°).
/// The sky needs no mips: it is a 2k texture shown at screen resolution, and ambient / reflections are baked from it.
/// </summary>
public class SkyImportSettings : AssetPostprocessor
{
    public override uint GetVersion() => 1;

    void OnPreprocessTexture()
    {
        if (assetPath != EnvironmentBuilder.DayHdriPath && assetPath != EnvironmentBuilder.NightHdriPath) return;
        var importer = (TextureImporter)assetImporter;
        importer.mipmapEnabled = false;
    }
}
