namespace EvacLogix.Sandbox.Authoring.Commands
{
    public interface ISandboxEditorCommand
    {
        string Description { get; }

        void Execute();

        void Undo();
    }
}
