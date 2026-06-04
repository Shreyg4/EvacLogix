namespace EvacLogix.Sandbox.Authoring.Commands
{
    public interface ISandboxEditorCommand
    {
        string Description { get; }

        // Approximate bytes this command retains (e.g. its undo/redo snapshots). Lets the history bound
        // total memory rather than just entry count, so large projects keep fewer entries automatically.
        long EstimatedMemoryBytes { get; }

        void Execute();

        void Undo();
    }
}
