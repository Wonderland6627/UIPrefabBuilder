namespace Indey.UIPrefabBuilder.Core
{
    /// <summary>
    /// Holds the current task's design mockup image as an Assets-relative path so that
    /// tools (e.g. crop_design_image, match_sprite_by_region, map_design_rect) can access
    /// the raw reference image without needing the LLM to smuggle image bytes through a
    /// tool-call parameter.
    /// </summary>
    public static class DesignImageContext
    {
        /// <summary>Assets-relative path (e.g. "Assets/Screenshots/design_reference_foo.png") of the
        /// design mockup attached to the current task, or null if none.</summary>
        public static string CurrentAssetPath { get; set; }

        /// <summary>Pixel width of the design mockup, or 0 if unknown.</summary>
        public static int Width { get; set; }

        /// <summary>Pixel height of the design mockup, or 0 if unknown.</summary>
        public static int Height { get; set; }

        public static bool HasSize => Width > 0 && Height > 0;

        public static void Clear()
        {
            CurrentAssetPath = null;
            Width = 0;
            Height = 0;
        }

        public static void SetFromTexture(string assetPath, UnityEngine.Texture2D tex)
        {
            CurrentAssetPath = assetPath;
            if (tex != null)
            {
                Width = tex.width;
                Height = tex.height;
            }
            else
            {
                Width = 0;
                Height = 0;
            }
        }
    }
}
