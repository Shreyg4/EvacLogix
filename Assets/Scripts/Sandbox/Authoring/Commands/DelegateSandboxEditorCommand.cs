using System;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    public sealed class DelegateSandboxEditorCommand : ISandboxEditorCommand
    {
        private readonly Action execute;
        private readonly Action undo;

        public DelegateSandboxEditorCommand(string description, Action execute, Action undo)
        {
            Description = string.IsNullOrWhiteSpace(description) ? "Unnamed Command" : description;
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
        }

        public string Description { get; }

        public void Execute()
        {
            execute();
        }

        public void Undo()
        {
            undo();
        }
    }
}
