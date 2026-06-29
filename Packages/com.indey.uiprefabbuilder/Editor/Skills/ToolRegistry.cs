using System;
using System.Collections.Generic;
using Indey.UIPrefabBuilder.Core;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Skills
{
    public class ToolRegistry
    {
        private readonly ToolExecutor _executor = new ToolExecutor();
        private JArray _cachedDefinitions;
        private HashSet<string> _toolNames;

        private static readonly HashSet<string> ReadOnlyTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "get_scene_overview", "get_scene_info", "get_project_config",
            "inspect_hierarchy", "inspect_components", "get_asset_info",
            "search_assets", "search_assets_glob",
            "search_sprites_by_text", "search_sprites_by_text_batch", "match_sprite_by_image",
            "get_index_status",
            "take_screenshot", "analyze_screenshot",
            "update_progress"
        };

        public JArray Definitions
        {
            get
            {
                if (_cachedDefinitions == null) Rebuild();
                return _cachedDefinitions;
            }
        }

        public JArray GetDefinitionsForIntent(TaskIntent intent)
        {
            if (_cachedDefinitions == null) Rebuild();

            if (intent == TaskIntent.Build || intent == TaskIntent.Modify)
                return _cachedDefinitions;

            var filtered = new JArray();
            foreach (var tool in _cachedDefinitions)
            {
                var name = tool["function"]?["name"]?.ToString();
                if (!string.IsNullOrEmpty(name) && ReadOnlyTools.Contains(name))
                    filtered.Add(tool);
            }
            return filtered;
        }

        public void Rebuild()
        {
            _cachedDefinitions = ToolDefinitions.BuildAll();
            _toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in _cachedDefinitions)
            {
                var name = tool["function"]?["name"]?.ToString();
                if (!string.IsNullOrEmpty(name))
                    _toolNames.Add(name);
            }
            ConsoleLogger.Log($"[ToolRegistry] Registered {_toolNames.Count} tools.");
        }

        public bool HasTool(string name)
        {
            if (_toolNames == null) Rebuild();
            return _toolNames.Contains(name);
        }

        public string Execute(string toolName, string argumentsJson)
        {
            return _executor.Execute(toolName, argumentsJson);
        }

        public IReadOnlyCollection<string> ToolNames
        {
            get
            {
                if (_toolNames == null) Rebuild();
                return _toolNames;
            }
        }

    }
}
