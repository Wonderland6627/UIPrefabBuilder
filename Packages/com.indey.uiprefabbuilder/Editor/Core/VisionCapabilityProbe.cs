using System;
using System.Threading;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;

namespace Indey.UIPrefabBuilder.Core
{
    public class VisionProbeResult
    {
        public bool Supported;
        /// <summary>True when failure is auth/network/config rather than "model has no vision".</summary>
        public bool IsAuthOrNetworkError;
        public string Reason;
    }

    /// <summary>
    /// Probes whether the configured OpenAI-compatible endpoint actually accepts multimodal
    /// image input by sending a tiny test PNG. Caches successful results for 30 minutes
    /// (invalidated when BaseUrl or ModelName changes).
    /// </summary>
    public static class VisionCapabilityProbe
    {
        private static readonly TimeSpan CacheWindow = TimeSpan.FromMinutes(30);
        private static DateTime _lastSuccessUtc = DateTime.MinValue;
        private static string _lastBaseUrl;
        private static string _lastModel;
        private static bool _inFlight;
        private static CancellationTokenSource _cts;

        public static bool IsProbing => _inFlight;

        public static bool IsCacheValid(BuilderSettings settings)
        {
            if (settings == null) return false;
            if (_lastSuccessUtc == DateTime.MinValue) return false;
            if (!string.Equals(_lastBaseUrl, settings.BaseUrl, StringComparison.Ordinal)
                || !string.Equals(_lastModel, settings.ModelName, StringComparison.Ordinal))
                return false;
            return DateTime.UtcNow - _lastSuccessUtc < CacheWindow;
        }

        public static void InvalidateCache()
        {
            _lastSuccessUtc = DateTime.MinValue;
            _lastBaseUrl = null;
            _lastModel = null;
        }

        /// <summary>
        /// Runs an async vision probe. Callback is always dispatched on the main thread.
        /// If a probe is already in flight, the new callback waits for that result.
        /// </summary>
        public static void ProbeAsync(BuilderSettings settings, Action<VisionProbeResult> onDone, bool force = false)
        {
            if (settings == null)
            {
                onDone?.Invoke(new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = true,
                    Reason = "BuilderSettings is null."
                });
                return;
            }

            if (!force && IsCacheValid(settings))
            {
                onDone?.Invoke(new VisionProbeResult
                {
                    Supported = true,
                    Reason = "Cached vision probe succeeded for this Base URL + Model."
                });
                return;
            }

            if (_inFlight)
            {
                // Queue behind current probe by polling cache/in-flight on main thread
                EditorPollUntilDone(settings, onDone);
                return;
            }

            _inFlight = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var client = new LLMClient(settings);
            client.ProbeVisionSupportAsync(
                result =>
                {
                    try
                    {
                        if (result != null && result.Supported)
                            MarkSuccess(settings);
                        else
                            InvalidateCache();

                        ConsoleLogger.Log(result != null && result.Supported
                            ? $"[VisionProbe] OK — {result.Reason}"
                            : $"[VisionProbe] FAIL — {result?.Reason}");
                        onDone?.Invoke(result);
                    }
                    finally
                    {
                        _inFlight = false;
                        client.Dispose();
                    }
                },
                ex =>
                {
                    try
                    {
                        InvalidateCache();
                        var result = new VisionProbeResult
                        {
                            Supported = false,
                            IsAuthOrNetworkError = true,
                            Reason = ex?.Message ?? "Vision probe failed."
                        };
                        ConsoleLogger.Error($"[VisionProbe] Error — {result.Reason}");
                        onDone?.Invoke(result);
                    }
                    finally
                    {
                        _inFlight = false;
                        client.Dispose();
                    }
                },
                ct);
        }

        private static void MarkSuccess(BuilderSettings settings)
        {
            _lastSuccessUtc = DateTime.UtcNow;
            _lastBaseUrl = settings.BaseUrl;
            _lastModel = settings.ModelName;
        }

        private static void EditorPollUntilDone(BuilderSettings settings, Action<VisionProbeResult> onDone)
        {
            // If another probe is running, wait briefly then return cache or failure.
            double start = UnityEditor.EditorApplication.timeSinceStartup;
            void Tick()
            {
                if (!_inFlight)
                {
                    UnityEditor.EditorApplication.update -= Tick;
                    if (IsCacheValid(settings))
                    {
                        onDone?.Invoke(new VisionProbeResult
                        {
                            Supported = true,
                            Reason = "Cached vision probe succeeded for this Base URL + Model."
                        });
                    }
                    else
                    {
                        onDone?.Invoke(new VisionProbeResult
                        {
                            Supported = false,
                            Reason = "A concurrent vision probe finished without a successful cache."
                        });
                    }
                    return;
                }

                if (UnityEditor.EditorApplication.timeSinceStartup - start > 60)
                {
                    UnityEditor.EditorApplication.update -= Tick;
                    onDone?.Invoke(new VisionProbeResult
                    {
                        Supported = false,
                        IsAuthOrNetworkError = true,
                        Reason = "Timed out waiting for an in-flight vision probe."
                    });
                }
            }

            UnityEditor.EditorApplication.update += Tick;
        }
    }
}
