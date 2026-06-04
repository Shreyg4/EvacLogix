using System;
using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    public sealed class SandboxCommandHistory : MonoBehaviour
    {
        // Each command captures before/after project snapshots. An unbounded history therefore grows the
        // heap with every edit, which OOMs the capped WebGL heap. We bound BOTH the entry count and the
        // total retained snapshot bytes: small projects keep a deep history, while a massive project
        // (large snapshots) automatically keeps fewer entries so memory stays within budget.
        private const int MaxUndoDepth = 50;
        private const long MaxUndoMemoryBytes = 96L * 1024L * 1024L; // ~96 MB of retained snapshots

        // Undo is a list used as a stack (top = last element) so the oldest entries can be trimmed
        // from the bottom once the depth cap is exceeded.
        private readonly List<ISandboxEditorCommand> undoStack = new();
        private readonly Stack<ISandboxEditorCommand> redoStack = new();

        public event Action<ISandboxEditorCommand> CommandExecuted;
        public event Action<ISandboxEditorCommand> CommandUndone;
        public event Action<ISandboxEditorCommand> CommandRedone;

        public int UndoCount => undoStack.Count;
        public int RedoCount => redoStack.Count;
        public bool CanUndo => undoStack.Count > 0;
        public bool CanRedo => redoStack.Count > 0;

        public void Execute(ISandboxEditorCommand command)
        {
            if (command == null)
            {
                return;
            }

            command.Execute();
            undoStack.Add(command);
            TrimUndoHistory();
            redoStack.Clear();
            CommandExecuted?.Invoke(command);
        }

        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            var command = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            command.Undo();
            redoStack.Push(command);
            CommandUndone?.Invoke(command);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
            {
                return false;
            }

            var command = redoStack.Pop();
            command.Execute();
            undoStack.Add(command);
            TrimUndoHistory();
            CommandRedone?.Invoke(command);
            return true;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
        }

        // Drops the oldest commands (and the project snapshots they retain) once either the depth cap or
        // the retained-bytes budget is exceeded, bounding total memory regardless of edit count or
        // project size. Always keeps at least one entry so even a single large edit stays undoable.
        private void TrimUndoHistory()
        {
            while (undoStack.Count > MaxUndoDepth)
            {
                undoStack.RemoveAt(0);
            }

            var bytes = CurrentUndoBytes();
            while (undoStack.Count > 1 && bytes > MaxUndoMemoryBytes)
            {
                bytes -= undoStack[0].EstimatedMemoryBytes;
                undoStack.RemoveAt(0);
            }
        }

        private long CurrentUndoBytes()
        {
            long total = 0;
            for (var i = 0; i < undoStack.Count; i += 1)
            {
                total += undoStack[i].EstimatedMemoryBytes;
            }

            return total;
        }
    }
}
