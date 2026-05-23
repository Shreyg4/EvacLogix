using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Validation;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxTopBarShell : MonoBehaviour
    {
        [SerializeField] private string title = "Sandbox Editor";
        [SerializeField] private string lifecycleStateLabel = "Draft";
        [SerializeField] private string modeLabel = "Edit Mode";

        private SandboxNewProjectDialogShell newProjectDialog;
        private SandboxSaveLoadService saveLoadService;
        private SandboxProjectTransferService projectTransferService;
        private SandboxPreviewImageExportService previewImageExportService;
        private SandboxPreviewService previewService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxValidationService validationService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxStatusBarShell statusBar;

        public string Title => title;
        public string LifecycleStateLabel => lifecycleStateLabel;
        public string ModeLabel => modeLabel;
        public bool HasRecoveryPrompt => saveLoadService != null && saveLoadService.HasRecoveryPrompt;
        public string RecoveryPromptMessage => saveLoadService?.RecoveryPromptMessage ?? string.Empty;
        public bool IsPreviewModeActive => previewService != null && previewService.IsPreviewModeActive;
        public string PreviewSummary => previewService?.LastPreviewReport?.summary ?? string.Empty;

        private void Awake()
        {
            newProjectDialog = FindAnyObjectByType<SandboxNewProjectDialogShell>();
            saveLoadService = FindAnyObjectByType<SandboxSaveLoadService>();
            projectTransferService = FindAnyObjectByType<SandboxProjectTransferService>();
            previewImageExportService = FindAnyObjectByType<SandboxPreviewImageExportService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            colliderRebuildService = FindAnyObjectByType<SandboxColliderRebuildService>();
            validationService = FindAnyObjectByType<SandboxValidationService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                HandleProjectChanged(workspaceService.ActiveProject);
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged += HandleIssuesChanged;
            }

            if (previewService != null)
            {
                previewService.PreviewModeChanged += HandlePreviewModeChanged;
                HandlePreviewModeChanged(previewService.IsPreviewModeActive);
            }
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged -= HandleIssuesChanged;
            }

            if (previewService != null)
            {
                previewService.PreviewModeChanged -= HandlePreviewModeChanged;
            }
        }

        public void OpenNewProjectDialog()
        {
            newProjectDialog?.Open();
        }

        public bool SaveProject(string filePath)
        {
            var didSave = saveLoadService != null && saveLoadService.SaveActiveProjectToPath(filePath);
            if (didSave && statusBar != null)
            {
                statusBar.StatusMessage = "Saved sandbox project.";
            }

            RefreshLifecycleState();
            return didSave;
        }

        public bool LoadProject(string filePath)
        {
            if (saveLoadService == null || saveLoadService.LoadProjectFromPath(filePath) == null)
            {
                return false;
            }

            colliderRebuildService?.RebuildAll();
            validationService?.ValidateActiveProject();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Loaded sandbox project.";
            }

            RefreshLifecycleState();
            return true;
        }

        public bool ExportProjectJson(string filePath)
        {
            var didExport = projectTransferService != null && projectTransferService.ExportProjectJson(filePath);
            if (didExport && statusBar != null)
            {
                statusBar.StatusMessage = "Exported full sandbox project JSON.";
            }

            return didExport;
        }

        public bool ImportProjectJson(string filePath)
        {
            var project = projectTransferService?.ImportProjectJson(filePath);
            if (project == null)
            {
                return false;
            }

            if (statusBar != null)
            {
                statusBar.StatusMessage = "Imported sandbox project JSON.";
            }

            RefreshLifecycleState();
            return true;
        }

        public bool ExportRuntimeProjectData(string filePath)
        {
            var didExport = projectTransferService != null && projectTransferService.ExportRuntimeProjectData(filePath);
            if (didExport && statusBar != null)
            {
                statusBar.StatusMessage = "Exported runtime-ready sandbox data.";
            }

            RefreshLifecycleState();
            return didExport;
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return projectTransferService == null
                ? new SandboxFloorImportAnalysis()
                : projectTransferService.AnalyzeFloorImportFromPath(filePath, selectedFloorIds);
        }

        public bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            var didImport = projectTransferService != null && projectTransferService.ImportFloorsFromPath(filePath, selectedFloorIds);
            if (didImport && statusBar != null)
            {
                statusBar.StatusMessage = "Imported selected floors into the current project.";
            }

            RefreshLifecycleState();
            return didImport;
        }

        public bool TryRestoreRecovery()
        {
            var didRestore = saveLoadService != null && saveLoadService.TryRestoreRecovery();
            if (didRestore)
            {
                colliderRebuildService?.RebuildAll();
                validationService?.ValidateActiveProject();
                if (statusBar != null)
                {
                    statusBar.StatusMessage = "Restored recovery autosave.";
                }
            }

            RefreshLifecycleState();
            return didRestore;
        }

        public void DismissRecoveryPrompt()
        {
            saveLoadService?.DismissRecoveryPrompt();
        }

        public bool ExportPreviewImage(string destinationPath)
        {
            if (validationService != null && !validationService.CanPreviewOrExport())
            {
                if (statusBar != null)
                {
                    statusBar.StatusMessage = "Resolve blocking validation issues before preview or export.";
                }
                return false;
            }

            return previewImageExportService != null && previewImageExportService.TryExportActiveBlueprintPreview(destinationPath);
        }

        public void RebuildAll()
        {
            colliderRebuildService?.RebuildAll();
            validationService?.ValidateActiveProject();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Rebuilt all generated colliders and refreshed validation.";
            }

            RefreshLifecycleState();
        }

        public bool CanOpenPreview()
        {
            return validationService == null || validationService.CanPreviewOrExport();
        }

        public bool EnterPreviewMode()
        {
            var didEnter = previewService != null && previewService.EnterPreviewMode();
            if (didEnter && statusBar != null)
            {
                statusBar.StatusMessage = "Entered preview mode.";
            }

            return didEnter;
        }

        public void ExitPreviewMode()
        {
            previewService?.ExitPreviewMode();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Exited preview mode.";
            }
        }

        public bool RunPreview()
        {
            var didRun = previewService != null && previewService.RunPreview();
            if (statusBar != null)
            {
                statusBar.StatusMessage = didRun
                    ? (previewService?.LastPreviewReport?.summary ?? "Ran preview.")
                    : (previewService?.LastPreviewReport?.summary ?? "Preview did not run.");
            }

            return didRun;
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            RefreshLifecycleState();
        }

        private void HandleIssuesChanged(IReadOnlyList<ValidationIssueData> issues)
        {
            RefreshLifecycleState();
        }

        private void HandlePreviewModeChanged(bool isPreviewModeActive)
        {
            modeLabel = isPreviewModeActive ? "Preview Mode" : "Edit Mode";
        }

        private void RefreshLifecycleState()
        {
            lifecycleStateLabel = BuildingLifecycleStateUtility.GetDisplayLabel(
                workspaceService?.ActiveProject?.LifecycleState ?? BuildingLifecycleState.Draft);
        }
    }
}
