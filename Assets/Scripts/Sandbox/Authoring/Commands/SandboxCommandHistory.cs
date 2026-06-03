using System;
using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    public sealed class SandboxCommandHistory : MonoBehaviour
    {
        // Each command captures full before/after project snapshots (deep clones). An unbounded history
        // therefore grows the heap with every edit, which OOMs the capped WebGL heap on dense drawings.
        // Cap the depth so the oldest edits are discarded (no longer undoable) instead of leaking memory.
        private const int MaxUndoDepth = 50;

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

        // Drops the oldest commands (and the large project snapshots they retain) once the depth cap is
        // exceeded, bounding total memory regardless of how many edits the user makes.
        private void TrimUndoHistory()
        {
            while (undoStack.Count > MaxUndoDepth)
            {
                undoStack.RemoveAt(0);
            }
        }
    }
}
