using System;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    public sealed class DelegateSandboxEditorCommand : ISandboxEditorCommand
    {
        private readonly Action execute;
        private readonly Action undo;

        public DelegateSandboxEditorCommand(string description, Action execute, Action undo, long estimatedMemoryBytes = 0)
        {
            Description = string.IsNullOrWhiteSpace(description) ? "Unnamed Command" : description;
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
            EstimatedMemoryBytes = estimatedMemoryBytes < 0 ? 0 : estimatedMemoryBytes;
        }

        public string Description { get; }

        public long EstimatedMemoryBytes { get; }

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
