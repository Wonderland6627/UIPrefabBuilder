using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Skills
{
    [Serializable]
    public class SkillMeta
    {
        public string Name;
        public string Description;
        public string DirectoryPath;
    }

    public class SkillRegistry
    {
        private readonly List<SkillMeta> _skills = new List<SkillMeta>();
        private string _root;

        public IReadOnlyList<SkillMeta> AllSkills => _skills;

        public void DiscoverSkills()
        {
            _skills.Clear();
            _root = Path.GetFullPath("Packages/com.indey.uiprefabbuilder/Skills~");
            if (!Directory.Exists(_root)) { Debug.LogWarning("[UIPrefabBuilder] Skills~ not found."); return; }
            foreach (var dir in Directory.GetDirectories(_root))
            {
                var md = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(md)) continue;
                var meta = ParseMeta(md, dir);
                if (meta != null) _skills.Add(meta);
            }
        }

        public string GenerateSkillsSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Available Skills:");
            foreach (var s in _skills) sb.AppendLine($"- **{s.Name}**: {s.Description}");
            return sb.ToString();
        }

        public IReadOnlyList<SkillMeta> FindRelevant(string intent, int max = 3)
        {
            if (string.IsNullOrWhiteSpace(intent)) return _skills.Take(max).ToList();
            var lower = intent.ToLowerInvariant();
            var results = _skills.Select(s => new { s, score = Score(s, lower) })
                .Where(x => x.score > 0).OrderByDescending(x => x.score)
                .Take(max).Select(x => x.s).ToList();

            if (results.Count == 0 && _skills.Count > 0)
                return _skills.ToList();

            return results;
        }

        public string LoadSkillBody(string name)
        {
            var s = _skills.FirstOrDefault(x => x.Name == name);
            if (s == null) return "";
            var txt = File.ReadAllText(Path.Combine(s.DirectoryPath, "SKILL.md"));
            return Regex.Replace(txt, @"\A---.*?---\s*", "", RegexOptions.Singleline).Trim();
        }

        public string LoadRelevantDocs(string intent, int maxChars = 12000)
        {
            var sb = new StringBuilder();
            foreach (var s in FindRelevant(intent))
            {
                sb.AppendLine($"# Skill: {s.Name}");
                sb.AppendLine(LoadSkillBody(s.Name));
                sb.AppendLine();
                if (sb.Length >= maxChars) break;
            }
            return sb.ToString();
        }

        public string LoadSkillFile(string name, string rel)
        {
            var s = _skills.FirstOrDefault(x => x.Name == name);
            if (s == null) return "";
            var p = Path.Combine(s.DirectoryPath, rel);
            return File.Exists(p) ? File.ReadAllText(p) : "";
        }

        public string LoadSystemPrompt()
        {
            var p = Path.Combine(_root ?? "", "system-prompt.md");
            return File.Exists(p) ? File.ReadAllText(p) : "You are UIPrefabBuilder, a Unity UGUI expert agent.";
        }

        private static readonly Dictionary<string, string[]> ChineseKeywordMap = new Dictionary<string, string[]>
        {
            { "creating-ui-elements", new[] { "创建", "按钮", "面板", "弹窗", "界面", "菜单", "文本", "图片", "输入框", "滑块", "开关", "滚动", "UI", "ui" } },
            { "finding-assets", new[] { "查找", "搜索", "找到", "资源", "贴图", "图片", "sprite", "Sprite", "素材", "文件", "使用" } },
            { "managing-rect-transform", new[] { "锚点", "位置", "大小", "尺寸", "居中", "拉伸", "对齐", "定位", "布局" } },
            { "modifying-ui-properties", new[] { "颜色", "设置", "修改", "属性", "透明", "字体", "文字" } },
            { "applying-layout-groups", new[] { "排列", "垂直", "水平", "网格", "间距", "布局", "排布", "堆叠" } },
            { "managing-prefabs", new[] { "预制体", "prefab", "保存", "实例化" } },
            { "managing-scenes", new[] { "场景", "scene", "保存场景" } },
            { "inspecting-hierarchy", new[] { "层级", "检查", "查看", "组件", "节点" } },
        };

        private static int Score(SkillMeta s, string intent)
        {
            int sc = 0;
            if (intent.Contains(s.Name)) sc += 10;
            foreach (var w in (s.Description ?? "").Split(' ', ',', '.', '\n'))
            {
                if (w.Length < 3) continue;
                if (intent.Contains(w.ToLowerInvariant())) sc++;
            }
            if (ChineseKeywordMap.TryGetValue(s.Name, out var cnKeywords))
            {
                foreach (var kw in cnKeywords)
                {
                    if (intent.Contains(kw)) sc += 2;
                }
            }
            return sc;
        }

        private static SkillMeta ParseMeta(string path, string dir)
        {
            var txt = File.ReadAllText(path);
            var m = Regex.Match(txt, @"\A---\s*\n(.*?)\n---", RegexOptions.Singleline);
            if (!m.Success) return null;
            var block = m.Groups[1].Value;
            var name = ExtractYaml(block, "name");
            var desc = ExtractYaml(block, "description");
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new SkillMeta { Name = name.Trim(), Description = (desc ?? "").Trim(), DirectoryPath = dir };
        }

        private static string ExtractYaml(string block, string key)
        {
            var m = Regex.Match(block, key + @"\s*:\s*\|?\s*\n?\s*(.+?)(?:\n\w|\z)", RegexOptions.Singleline);
            return m.Success ? m.Groups[1].Value.Trim() : Regex.Match(block, key + @"\s*:\s*(.+)")?.Groups[1].Value;
        }
    }
}
