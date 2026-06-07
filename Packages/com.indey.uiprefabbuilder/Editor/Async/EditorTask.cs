using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Async
{
    public static class EditorTask
    {
        public static Task Delay(int milliseconds, CancellationToken ct = default)
        {
            return Task.Delay(milliseconds, ct);
        }

        public static void RunDelayed(float seconds, Action action)
        {
            var start = EditorApplication.timeSinceStartup;
            void Check()
            {
                if (EditorApplication.timeSinceStartup - start >= seconds)
                {
                    EditorApplication.update -= Check;
                    action?.Invoke();
                }
            }
            EditorApplication.update += Check;
        }

        public static Task<T> RunInBackground<T>(Func<T> work, CancellationToken ct = default)
        {
            return Task.Run(work, ct);
        }
    }
}
