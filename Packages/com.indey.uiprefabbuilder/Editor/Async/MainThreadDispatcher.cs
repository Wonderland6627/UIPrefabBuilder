using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Async
{
    [InitializeOnLoad]
    public static class MainThreadDispatcher
    {
        private const int FrameBudget = 10;
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<Action> NextFrameQueue = new ConcurrentQueue<Action>();

        static MainThreadDispatcher()
        {
            EditorApplication.update += ProcessQueue;
        }

        public static void Enqueue(Action action)
        {
            if (action != null) Queue.Enqueue(action);
        }

        public static void EnqueueNextFrame(Action action)
        {
            if (action != null) NextFrameQueue.Enqueue(action);
        }

        public static Task<T> RunOnMainThread<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        public static Task RunOnMainThread(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();
            Enqueue(() =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        private static void ProcessQueue()
        {
            while (NextFrameQueue.TryDequeue(out var nf))
                Queue.Enqueue(nf);

            var budget = FrameBudget;
            while (budget-- > 0 && Queue.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
