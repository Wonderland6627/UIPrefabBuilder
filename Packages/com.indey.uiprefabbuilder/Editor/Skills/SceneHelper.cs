using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class SceneHelper
    {
        public static string GetCurrentSceneName() => SceneManager.GetActiveScene().name;
        public static string GetCurrentScenePath() => SceneManager.GetActiveScene().path;

        public static bool SaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            return EditorSceneManager.SaveScene(scene);
        }

        public static void MarkDirty()
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        public static GameObject[] FindByTag(string tag)
        {
            try { return GameObject.FindGameObjectsWithTag(tag); }
            catch { return new GameObject[0]; }
        }

        public static GameObject[] FindByName(string name)
        {
            var all = Object.FindObjectsOfType<GameObject>();
            var results = new System.Collections.Generic.List<GameObject>();
            foreach (var go in all)
                if (go.name == name) results.Add(go);
            return results.ToArray();
        }
    }
}
