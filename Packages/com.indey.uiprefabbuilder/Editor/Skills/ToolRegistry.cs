using System;
using System.Collections.Generic;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Skills
{
    public class ToolRegistry
    {
        private readonly ToolExecutor _executor = new ToolExecutor();
        private JArray _cachedDefinitions;
        private HashSet<string> _toolNames;

        public JArray Definitions
        {
            get
            {
                if (_cachedDefinitions == null) Rebuild();
                return _cachedDefinitions;
            }
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
