using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Indey.UIPrefabBuilder.Indexing;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Config
{
    [Serializable]
    public class ProjectConfig
    {
        private static ProjectConfig _current;
        private static bool _loaded;

        public BasicInfo basicInfo = new BasicInfo();
        public List<SpriteEntry> spriteMapping = new List<SpriteEntry>();
        public List<PrefabEntry> prefabMapping = new List<PrefabEntry>();
        public ComponentOverrides componentOverrides = new ComponentOverrides();
        public List<string> rules = new List<string>();
        public IndexingConfig indexingConfig = new IndexingConfig();

        public static string ConfigPath
        {
            get
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                return Path.Combine(projectRoot, "ProjectSettings", "UIPrefabBuilderConfig.json");
            }
        }

        public static ProjectConfig Current
        {
            get
            {
                if (!_loaded) Load();
                return _current;
            }
        }

        public static void Load()
        {
            _loaded = true;
            var path = ConfigPath;
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    _current = JsonConvert.DeserializeObject<ProjectConfig>(json) ?? new ProjectConfig();
                    ConsoleLogger.Log("[ProjectConfig] Loaded from " + path);
                    return;
                }
                catch (Exception e)
                {
                    ConsoleLogger.Warning($"[ProjectConfig] Failed to load: {e.Message}");
                }
            }
            _current = new ProjectConfig();
        }

        public static void Reload()
        {
            _loaded = false;
            Load();
        }

        public static void Save()
        {
            if (_current == null) return;
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
                ConsoleLogger.Log("[ProjectConfig] Saved to " + ConfigPath);
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[ProjectConfig] Failed to save: {e.Message}");
            }
        }

        public static void Export(string path)
        {
            if (_current == null) return;
            try
            {
                var json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(path, json, Encoding.UTF8);
                ConsoleLogger.Log("[ProjectConfig] Exported to " + path);
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[ProjectConfig] Export failed: {e.Message}");
            }
        }

        public static void Import(string path)
        {
            if (!File.Exists(path))
            {
                ConsoleLogger.Warning("[ProjectConfig] Import file not found: " + path);
                return;
            }
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                _current = JsonConvert.DeserializeObject<ProjectConfig>(json) ?? new ProjectConfig();
                _loaded = true;
                ConsoleLogger.Log("[ProjectConfig] Imported from " + path);
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[ProjectConfig] Import failed: {e.Message}");
            }
        }

        public static void Reset()
        {
            _current = new ProjectConfig();
            _loaded = true;
            ConsoleLogger.Log("[ProjectConfig] Reset to defaults");
        }

        public bool HasAnyConfig()
        {
            return spriteMapping.Count > 0
                || prefabMapping.Count > 0
                || componentOverrides.HasOverrides()
                || rules.Count > 0
                || !string.IsNullOrEmpty(basicInfo.projectNotes)
                || (indexingConfig?.indexDirectories?.Count > 0);
        }

        public string BuildPromptSection()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## PROJECT CONFIGURATION (MUST FOLLOW)");
            sb.AppendLine();

            // Basic Info
            sb.AppendLine("### Design Basics");
            sb.AppendLine($"- Orientation: {basicInfo.orientation}");
            sb.AppendLine($"- Design Resolution: {basicInfo.designWidth}x{basicInfo.designHeight}");
            sb.AppendLine($"- CanvasScaler matchWidthOrHeight: {basicInfo.canvasMatchMode}");
            if (!string.IsNullOrEmpty(basicInfo.projectNotes))
                sb.AppendLine($"- Project Notes: {basicInfo.projectNotes}");
            sb.AppendLine();

            // Sprite Mapping
            if (spriteMapping.Count > 0)
            {
                sb.AppendLine("### Sprite Mapping (USE THESE instead of searching)");
                sb.AppendLine("When building UI, ALWAYS check this mapping first. Use the specified asset path directly instead of calling search_assets.");
                foreach (var entry in spriteMapping)
                {
                    sb.Append($"- {entry.role} → {entry.assetPath}");
                    if (!string.IsNullOrEmpty(entry.description))
                        sb.Append($" ({entry.description})");
                    if (!string.IsNullOrEmpty(entry.imageType))
                        sb.Append($" [Image.Type={entry.imageType}]");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // Prefab Mapping
            if (prefabMapping.Count > 0)
            {
                sb.AppendLine("### Prefab Mapping (INSTANTIATE instead of creating from scratch)");
                sb.AppendLine("When encountering these UI elements, use `instantiate_prefab` with the specified path instead of building from scratch.");
                foreach (var entry in prefabMapping)
                {
                    sb.Append($"- {entry.role} → {entry.prefabPath}");
                    if (!string.IsNullOrEmpty(entry.description))
                        sb.Append($" ({entry.description})");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // Component Overrides
            if (componentOverrides.HasOverrides())
            {
                sb.AppendLine("### Component Overrides (USE THESE component types)");
                if (!string.IsNullOrEmpty(componentOverrides.textComponent))
                    sb.AppendLine($"- When creating Text, use component type: {componentOverrides.textComponent} (NOT UnityEngine.UI.Text)");
                if (!string.IsNullOrEmpty(componentOverrides.buttonComponent))
                    sb.AppendLine($"- When creating Button, use component type: {componentOverrides.buttonComponent} (NOT UnityEngine.UI.Button)");
                if (!string.IsNullOrEmpty(componentOverrides.imageComponent))
                    sb.AppendLine($"- When creating Image, use component type: {componentOverrides.imageComponent} (NOT UnityEngine.UI.Image)");
                if (!string.IsNullOrEmpty(componentOverrides.inputFieldComponent))
                    sb.AppendLine($"- When creating InputField, use component type: {componentOverrides.inputFieldComponent} (NOT UnityEngine.UI.InputField)");
                foreach (var c in componentOverrides.customComponents)
                {
                    sb.Append($"- {c.role}: use component type {c.typeName}");
                    if (!string.IsNullOrEmpty(c.description))
                        sb.Append($" ({c.description})");
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // Rules
            var activeRules = rules.FindAll(r => !string.IsNullOrWhiteSpace(r));
            if (activeRules.Count > 0)
            {
                sb.AppendLine("### PROJECT RULES (MUST FOLLOW — these are user-defined conventions for this project)");
                for (int i = 0; i < activeRules.Count; i++)
                {
                    sb.AppendLine($"- {activeRules[i]}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string BuildConfigSummaryJson()
        {
            var obj = new Newtonsoft.Json.Linq.JObject
            {
                ["basicInfo"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["orientation"] = basicInfo.orientation.ToString(),
                    ["designResolution"] = $"{basicInfo.designWidth}x{basicInfo.designHeight}",
                    ["canvasMatchMode"] = basicInfo.canvasMatchMode,
                    ["projectNotes"] = basicInfo.projectNotes ?? ""
                }
            };

            var sprites = new Newtonsoft.Json.Linq.JArray();
            foreach (var s in spriteMapping)
            {
                sprites.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["role"] = s.role,
                    ["assetPath"] = s.assetPath,
                    ["description"] = s.description ?? "",
                    ["imageType"] = s.imageType ?? "Simple"
                });
            }
            obj["spriteMapping"] = sprites;

            var prefabs = new Newtonsoft.Json.Linq.JArray();
            foreach (var p in prefabMapping)
            {
                prefabs.Add(new Newtonsoft.Json.Linq.JObject
                {
                    ["role"] = p.role,
                    ["prefabPath"] = p.prefabPath,
                    ["description"] = p.description ?? ""
                });
            }
            obj["prefabMapping"] = prefabs;

            var overrides = new Newtonsoft.Json.Linq.JObject();
            if (!string.IsNullOrEmpty(componentOverrides.textComponent))
                overrides["text"] = componentOverrides.textComponent;
            if (!string.IsNullOrEmpty(componentOverrides.buttonComponent))
                overrides["button"] = componentOverrides.buttonComponent;
            if (!string.IsNullOrEmpty(componentOverrides.imageComponent))
                overrides["image"] = componentOverrides.imageComponent;
            if (!string.IsNullOrEmpty(componentOverrides.inputFieldComponent))
                overrides["inputField"] = componentOverrides.inputFieldComponent;
            obj["componentOverrides"] = overrides;

            var rulesArr = new Newtonsoft.Json.Linq.JArray();
            foreach (var r in rules)
                rulesArr.Add(r);
            obj["rules"] = rulesArr;

            return obj.ToString();
        }
    }

    [Serializable]
    public class BasicInfo
    {
        public ScreenOrientation orientation = ScreenOrientation.Landscape;
        public int designWidth = 1920;
        public int designHeight = 1080;
        public float canvasMatchMode = 0.5f;
        public string projectNotes = "";

        public enum ScreenOrientation { Landscape, Portrait }
    }

    [Serializable]
    public class SpriteEntry
    {
        public string role = "";
        public string assetPath = "";
        public string description = "";
        public string imageType = "Simple";
    }

    [Serializable]
    public class PrefabEntry
    {
        public string role = "";
        public string prefabPath = "";
        public string description = "";
    }

    [Serializable]
    public class ComponentOverrides
    {
        public string textComponent = "";
        public string buttonComponent = "";
        public string imageComponent = "";
        public string inputFieldComponent = "";
        public List<CustomComponentEntry> customComponents = new List<CustomComponentEntry>();

        public bool HasOverrides()
        {
            return !string.IsNullOrEmpty(textComponent)
                || !string.IsNullOrEmpty(buttonComponent)
                || !string.IsNullOrEmpty(imageComponent)
                || !string.IsNullOrEmpty(inputFieldComponent)
                || customComponents.Count > 0;
        }
    }

    [Serializable]
    public class CustomComponentEntry
    {
        public string role = "";
        public string typeName = "";
        public string description = "";
    }
}
