using UnityEditor;

namespace Indey.UIPrefabBuilder.UI
{
    /// <summary>
    /// Isolated menu registration. Child path avoids Unity submenu/leaf path conflicts
    /// when "UI Prefab Builder" was previously registered as a submenu parent.
    /// </summary>
    internal static class BuilderMenu
    {
        [MenuItem("Window/UI Prefab Builder/Open Builder", false, 1000)]
        public static void Open()
        {
            BuilderWindow.ShowWindow();
        }
    }
}
