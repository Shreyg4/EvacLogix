namespace EvacLogix.Sandbox.Infrastructure
{
    public interface ISandboxPreviewImageExportBackend
    {
        string BackendId { get; }
        bool TryExportActiveBlueprintPreview(string destinationPath);
    }
}
