using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxDesktopPreviewImageExportBackend : MonoBehaviour, ISandboxPreviewImageExportBackend
    {
        private SandboxPreviewImageExportService previewImageExportService;

        public string BackendId => "desktop-editor-preview-export";

        private void Awake()
        {
            previewImageExportService = GetComponent<SandboxPreviewImageExportService>();
        }

        public bool TryExportActiveBlueprintPreview(string destinationPath)
        {
            return previewImageExportService != null && previewImageExportService.TryExportActiveBlueprintPreview(destinationPath);
        }
    }
}
