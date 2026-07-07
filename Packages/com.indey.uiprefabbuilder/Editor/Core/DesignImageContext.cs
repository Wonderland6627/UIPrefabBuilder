namespace Indey.UIPrefabBuilder.Core
{
    /// <summary>
    /// Holds the current task's design mockup image as an Assets-relative path so that
    /// tools (e.g. crop_design_image, match_sprite_by_region) can access the raw reference
    /// image without needing the LLM to smuggle image bytes through a tool-call parameter.
    /// </summary>
    public static class DesignImageContext
    {
        /// <summary>Assets-relative path (e.g. "Assets/Screenshots/design_reference_foo.png") of the
        /// design mockup attached to the current task, or null if none.</summary>
        public static string CurrentAssetPath { get; set; }

        public static void Clear()
        {
            CurrentAssetPath = null;
        }
    }
}
