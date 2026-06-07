using System;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Transaction
{
    public sealed class UndoScope : IDisposable
    {
        private readonly int _group;
        private bool _disposed, _rolledBack;

        public UndoScope(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"[AI] {name}");
            _group = Undo.GetCurrentGroup();
        }

        public void Rollback()
        {
            if (_rolledBack) return;
            Undo.RevertAllDownToGroup(_group);
            _rolledBack = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_rolledBack) Undo.CollapseUndoOperations(_group);
        }
    }

    public class TransactionManager
    {
        private readonly System.Collections.Generic.Stack<int> _groups = new System.Collections.Generic.Stack<int>();

        public void Begin(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"[AI Tx] {name}");
            _groups.Push(Undo.GetCurrentGroup());
        }

        public void Commit()
        {
            if (_groups.Count == 0) return;
            Undo.CollapseUndoOperations(_groups.Pop());
        }

        public void RollbackLast()
        {
            if (_groups.Count == 0) return;
            Undo.RevertAllDownToGroup(_groups.Pop());
        }

        public void RollbackAll()
        {
            while (_groups.Count > 0) RollbackLast();
        }
    }
}
