using UnityEditor;

namespace StomachAbyss.UI.Editor
{
    [InitializeOnLoad]
    public static class FeverLogoImporterSetup
    {
        private const string ResLogoPath = "Assets/Resources/UI/tex_fever_logo.png";

        static FeverLogoImporterSetup()
        {
            EditorApplication.delayCall += ConfigureLogoSprite;
        }

        private static void ConfigureLogoSprite()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            TextureImporter importer = AssetImporter.GetAtPath(ResLogoPath) as TextureImporter;
            if (importer == null) return;

            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                dirty = true;
            }
            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }
    }
}
