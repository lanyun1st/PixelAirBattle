using UnityEditor;
using UnityEngine;

// Preserve the supplied pixel dimensions and alpha, including non-power-of-two scenery.
public sealed class ArtImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Art/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        if (assetPath.EndsWith("ui_panel_v2.png") || assetPath.EndsWith("ui_button_v2.png"))
        {
            importer.maxTextureSize = 128;
            importer.filterMode = FilterMode.Point;
        }
    }
    public static void RefreshArt()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Art" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
    }
}
