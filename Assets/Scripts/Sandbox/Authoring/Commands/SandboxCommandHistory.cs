using System;
using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    public sealed class SandboxCommandHistory : MonoBehaviour
    {
        private readonly Stack<ISandboxEditorCommand> undoStack = new();
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
            undoStack.Push(command);
            redoStack.Clear();
            CommandExecuted?.Invoke(command);
        }

        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            var command = undoStack.Pop();
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
            undoStack.Push(command);
            CommandRedone?.Invoke(command);
            return true;
        }

        public void Clear()
        {
            undoStack.Clear();
            redoStack.Clear();
        }
    }
}
