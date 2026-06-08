using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Config
{
    [InitializeOnLoad]
    internal static class VersionGuard
    {
        private const string MinUnityVersion = "2021.3";
        private const string SuppressKey = "UIPrefabBuilder_SuppressVersionWarning";

        static VersionGuard()
        {
            if (SessionState.GetBool(SuppressKey, false)) return;
            if (MeetsMinimumVersion()) return;

            SessionState.SetBool(SuppressKey, true);
            Debug.LogWarning(
                $"[UIPrefabBuilder] This package requires Unity {MinUnityVersion} or newer. " +
                $"Current version: {Application.unityVersion}. " +
                "Some features (dynamic C# compilation) may not work correctly.");
        }

        private static bool MeetsMinimumVersion()
        {
#if UNITY_2021_3_OR_NEWER
            return true;
#else
            return false;
#endif
        }
    }
}
