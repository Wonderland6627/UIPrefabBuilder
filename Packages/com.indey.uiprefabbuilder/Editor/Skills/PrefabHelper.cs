using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class PrefabHelper
    {
        /// <summary>Returns true if a prefab asset already exists at the given Assets-relative path.</summary>
        public static bool ExistsAtPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null || File.Exists(path);
        }

        /// <summary>
        /// Creates a NEW prefab asset from a scene GameObject. Refuses to overwrite an existing
        /// prefab asset at the same path — callers must check <see cref="ExistsAtPath"/> first and
        /// surface a clear error instead of silently clobbering an existing project asset.
        /// </summary>
        public static string Create(GameObject source, string path)
        {
            if (source == null || string.IsNullOrEmpty(path)) return null;
            if (ExistsAtPath(path)) return null;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(source, path, InteractionMode.UserAction);
            return prefab != null ? path : null;
        }

        public static GameObject Instantiate(string path, Transform parent = null, string name = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return null;
            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (inst == null) return null;
            if (parent != null) inst.transform.SetParent(parent, false);
            if (!string.IsNullOrEmpty(name)) inst.name = name;
            Undo.RegisterCreatedObjectUndo(inst, "Instantiate Prefab");
            return inst;
        }

        public static bool Apply(GameObject instance)
        {
            if (instance == null || !PrefabUtility.IsPartOfPrefabInstance(instance)) return false;
            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.UserAction);
            return true;
        }

        public static List<GameObject> FindInstances(string path)
        {
            var results = new List<GameObject>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return results;
            foreach (var go in Object.FindObjectsOfType<GameObject>())
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab)
                    results.Add(go);
            }
            return results;
        }
    }
}
