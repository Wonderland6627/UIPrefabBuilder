using System;
using System.Threading;
using System.Threading.Tasks;

namespace Indey.UIPrefabBuilder.Async
{
    public static class BackgroundWorker
    {
        public static void Run(Action work, Action onComplete = null, Action<Exception> onError = null, CancellationToken ct = default)
        {
            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    work();
                    if (onComplete != null) MainThreadDispatcher.Enqueue(onComplete);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    if (onError != null) MainThreadDispatcher.Enqueue(() => onError(e));
                }
            }, ct);
        }

        public static void Run<T>(Func<T> work, Action<T> onComplete, Action<Exception> onError = null, CancellationToken ct = default)
        {
            Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var result = work();
                    if (onComplete != null) MainThreadDispatcher.Enqueue(() => onComplete(result));
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    if (onError != null) MainThreadDispatcher.Enqueue(() => onError(e));
                }
            }, ct);
        }
    }
}
