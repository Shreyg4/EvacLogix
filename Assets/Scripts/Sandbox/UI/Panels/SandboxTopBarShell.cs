using UnityEngine;
using EvacLogix.Sandbox.Infrastructure;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxTopBarShell : MonoBehaviour
    {
        [SerializeField] private string title = "Sandbox Editor";

        private SandboxNewProjectDialogShell newProjectDialog;
        private SandboxSaveLoadService saveLoadService;
        private SandboxPreviewImageExportService previewImageExportService;

        public string Title => title;

        private void Awake()
        {
            newProjectDialog = FindAnyObjectByType<SandboxNewProjectDialogShell>();
            saveLoadService = FindAnyObjectByType<SandboxSaveLoadService>();
            previewImageExportService = FindAnyObjectByType<SandboxPreviewImageExportService>();
        }

        public void OpenNewProjectDialog()
        {
            newProjectDialog?.Open();
        }

        public bool SaveProject(string filePath)
        {
            return saveLoadService != null && saveLoadService.SaveActiveProjectToPath(filePath);
        }

        public bool LoadProject(string filePath)
        {
            return saveLoadService != null && saveLoadService.LoadProjectFromPath(filePath) != null;
        }

        public bool ExportPreviewImage(string destinationPath)
        {
            return previewImageExportService != null && previewImageExportService.TryExportActiveBlueprintPreview(destinationPath);
        }
    }
}
