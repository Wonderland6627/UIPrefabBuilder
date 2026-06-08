using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Indey.UIPrefabBuilder.Async;
using Indey.UIPrefabBuilder.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Indey.UIPrefabBuilder.Compiler
{
    public class CompileResult
    {
        public bool Success;
        public Assembly Assembly;
        public string[] Errors = Array.Empty<string>();
        public string WrappedSource;
        public bool IsConfigError;
    }

    public class DynamicCompiler
    {
        private static string _cscPath;
        private static string _dotnetPath;
        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "UIPrefabBuilder_Compile");

        public void CompileAsync(string code, Action<CompileResult> onResult, Action<Exception> onError = null)
        {
            BackgroundWorker.Run(
                () => CompileInternal(code),
                result => onResult?.Invoke(result),
                onError);
        }

        public CompileResult CompileInternal(string code)
        {
            var wrapped = WrapCode(code);

            if (!FindCompiler(out var error))
                return new CompileResult { Success = false, WrappedSource = wrapped, Errors = new[] { error }, IsConfigError = true };

            try
            {
                var assembly = CompileViaProcess(wrapped);
                return new CompileResult { Success = true, Assembly = assembly, WrappedSource = wrapped };
            }
            catch (Exception e)
            {
                return new CompileResult { Success = false, WrappedSource = wrapped, Errors = new[] { e.Message } };
            }
        }

        public static Type FindActionType(Assembly assembly)
        {
            if (assembly == null) return null;
            return assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && typeof(IAgentAction).IsAssignableFrom(t));
        }

        private static bool FindCompiler(out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(_cscPath) && File.Exists(_cscPath)) return true;

            var editorPath = EditorApplication.applicationContentsPath;

            // Unity 2021.3+ ships csc in DotNetSdkRoslyn
            var candidates = new[]
            {
                Path.Combine(editorPath, "DotNetSdkRoslyn", "csc.dll"),
                Path.Combine(editorPath, "Tools", "Roslyn", "csc.exe"),
                Path.Combine(editorPath, "Tools", "RoslynScripts", "csc.dll"),
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c)) { _cscPath = c; break; }
            }

            if (string.IsNullOrEmpty(_cscPath))
            {
                error = $"C# compiler not found. Searched:\n" + string.Join("\n", candidates);
                return false;
            }

            // Find dotnet executable (needed to run csc.dll)
            if (_cscPath.EndsWith(".dll"))
            {
                var dotnetCandidates = new[]
                {
                    Path.Combine(editorPath, "NetCoreRuntime", "dotnet.exe"),
                    Path.Combine(editorPath, "NetCoreRuntime", "dotnet"),
                    "dotnet", // System PATH
                };
                foreach (var d in dotnetCandidates)
                {
                    if (d == "dotnet" || File.Exists(d)) { _dotnetPath = d; break; }
                }
                if (string.IsNullOrEmpty(_dotnetPath)) _dotnetPath = "dotnet";
            }

            return true;
        }

        private Assembly CompileViaProcess(string source)
        {
            Directory.CreateDirectory(TempDir);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var srcFile = Path.Combine(TempDir, $"Agent_{id}.cs");
            var outFile = Path.Combine(TempDir, $"Agent_{id}.dll");

            File.WriteAllText(srcFile, source, Encoding.UTF8);

            try
            {
                var refs = CollectReferences();
                var args = BuildCompilerArgs(srcFile, outFile, refs);

                string exe, arguments;
                if (_cscPath.EndsWith(".dll"))
                {
                    exe = _dotnetPath;
                    arguments = $"exec \"{_cscPath}\" {args}";
                }
                else
                {
                    exe = _cscPath;
                    arguments = args;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = TempDir,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["DOTNET_CLI_UI_LANGUAGE"] = "en";
                psi.EnvironmentVariables["VSLANG"] = "1033";

                using var proc = Process.Start(psi);
                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(15000);

                if (proc.ExitCode != 0)
                {
                    var output = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                    var lines = output.Split('\n')
                        .Where(l => l.Contains("error"))
                        .Select(l => l.Trim())
                        .ToArray();
                    if (lines.Length == 0) lines = new[] { output.Trim() };
                    throw new InvalidOperationException("Compile errors:\n" + string.Join("\n", lines));
                }

                if (!File.Exists(outFile))
                    throw new InvalidOperationException("Compilation succeeded but output DLL not found.");

                var bytes = File.ReadAllBytes(outFile);
                return Assembly.Load(bytes);
            }
            finally
            {
                try { if (File.Exists(srcFile)) File.Delete(srcFile); } catch { }
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
            }
        }

        private static string BuildCompilerArgs(string srcFile, string outFile, List<string> refs)
        {
            var langVer = GetMaxLangVersion();
            var sb = new StringBuilder();
            sb.Append($"-target:library -out:\"{outFile}\" -langversion:{langVer} -nowarn:CS0162,CS0168,CS0219 -nologo -utf8output ");
            sb.Append("-unsafe- ");
            foreach (var r in refs) sb.Append($"-reference:\"{r}\" ");
            sb.Append($"\"{srcFile}\"");
            return sb.ToString();
        }

        private static List<string> CollectReferences()
        {
            var refs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                try
                {
                    var loc = asm.Location;
                    if (string.IsNullOrEmpty(loc) || !File.Exists(loc)) continue;
                    if (seen.Add(loc)) refs.Add(loc);
                }
                catch { }
            }
            return refs;
        }

        private string WrapCode(string code)
        {
            if (code.Contains("IAgentAction") && code.Contains("class"))
                return PrependUsings(code);

            var className = "Gen_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var sb = new StringBuilder();
            sb.AppendLine(StandardUsings);
            sb.AppendLine($"public class {className} : IAgentAction {{");
            sb.AppendLine("  public string ActionName => \"Generated\";");
            sb.AppendLine("  public string Description => \"Auto-wrapped\";");
            sb.AppendLine("  public ActionResult Execute(ActionContext context) {");
            sb.AppendLine("    try {");
            sb.AppendLine(code);
            sb.AppendLine("      return ActionResult.Ok(\"Done.\");");
            sb.AppendLine("    } catch (Exception e) { return ActionResult.Fail(e.Message); }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private string PrependUsings(string code)
        {
            var sb = new StringBuilder();
            // Always ensure critical usings are present
            foreach (var u in RequiredUsings)
            {
                if (!code.Contains(u))
                    sb.AppendLine(u);
            }
            sb.Append(code);
            return sb.ToString();
        }

        private static readonly string[] RequiredUsings = new[]
        {
            "using System;",
            "using System.Collections.Generic;",
            "using System.Linq;",
            "using UnityEngine;",
            "using UnityEngine.UI;",
            "using UnityEditor;",
            "using Indey.UIPrefabBuilder.Core;",
            "using Indey.UIPrefabBuilder.Skills;",
        };

        private const string StandardUsings = @"using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Indey.UIPrefabBuilder.Core;
using Indey.UIPrefabBuilder.Skills;";

        internal static string GetMaxLangVersion()
        {
#if UNITY_2022_2_OR_NEWER
            return "9.0";
#elif UNITY_2021_2_OR_NEWER
            return "9.0";
#else
            return "8.0";
#endif
        }
    }
}
