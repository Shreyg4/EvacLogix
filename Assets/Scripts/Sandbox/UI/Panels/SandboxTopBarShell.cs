using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Validation;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Runtime;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxTopBarShell : MonoBehaviour
    {
        [SerializeField] private string title = "Sandbox Editor";
        [SerializeField] private string lifecycleStateLabel = "Draft";
        [SerializeField] private string modeLabel = "Edit Mode";

        private ISandboxFileActionService fileActionService;
        private SandboxBrowserFileActionCoordinator browserFileActionCoordinator;
        private ISandboxPreviewImageExportBackend previewImageExportBackend;
        private SandboxNewProjectDialogShell newProjectDialog;
        private SandboxProjectRefreshService projectRefreshService;
        private SandboxPreviewService previewService;
        private SandboxAgentSimulationService agentSimulationService;
        private SandboxValidationService validationService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSaveLoadService saveLoadService;
        private SandboxStatusBarShell statusBar;

        public string Title => title;
        public string LifecycleStateLabel => lifecycleStateLabel;
        public string ModeLabel => modeLabel;
        public bool UsesBrowserHostedFileActions => browserFileActionCoordinator != null && browserFileActionCoordinator.SupportsBrowserFileActions;
        public bool IsBrowserFileActionBusy => browserFileActionCoordinator != null && browserFileActionCoordinator.IsBusy;
        public bool UsesBrowserPersistenceMode => saveLoadService != null && saveLoadService.UsesBrowserPersistenceMode;
        public string PersistenceModeSummary => UsesBrowserPersistenceMode
            ? "Browser mode saves projects to this browser/device. Use JSON import/export to move projects elsewhere."
            : "Working files are stored using local project paths.";
        public bool HasRecoveryPrompt => fileActionService != null && fileActionService.HasRecoveryPrompt;
        public string RecoveryPromptMessage => fileActionService?.RecoveryPromptMessage ?? string.Empty;
        public bool HasUnsavedChanges => saveLoadService != null && saveLoadService.HasUnsavedChanges;
        public bool HasSavedBrowserProjects => saveLoadService != null && saveLoadService.GetSavedProjects().Length > 0;
        public bool IsPreviewModeActive => previewService != null && previewService.IsPreviewModeActive;
        public string PreviewSummary => previewService?.LastPreviewReport?.summary ?? string.Empty;
        public bool HasCompletedSimulationRunReport => agentSimulationService != null && agentSimulationService.HasCompletedSimulationRunReport;
        public SandboxSimulationRunReportData LastSimulationRunReport => agentSimulationService?.LastSimulationRunReport;

        private void Awake()
        {
            fileActionService = FindAnyObjectByType<SandboxFileActionService>();
            browserFileActionCoordinator = FindAnyObjectByType<SandboxBrowserFileActionCoordinator>();
            previewImageExportBackend = FindAnyObjectByType<SandboxDesktopPreviewImageExportBackend>();
            newProjectDialog = FindAnyObjectByType<SandboxNewProjectDialogShell>();
            projectRefreshService = FindAnyObjectByType<SandboxProjectRefreshService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            agentSimulationService = FindAnyObjectByType<SandboxAgentSimulationService>();
            saveLoadService = FindAnyObjectByType<SandboxSaveLoadService>();
            validationService = FindAnyObjectByType<SandboxValidationService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();

            Debug.Log(
                "SandboxTopBarShell initialized: " +
                $"BrowserCoordinatorPresent={browserFileActionCoordinator != null}, " +
                $"UsesBrowserHostedFileActions={UsesBrowserHostedFileActions}, " +
                $"UsesBrowserPersistenceMode={UsesBrowserPersistenceMode}, " +
                $"ActiveBackendId={((SandboxFileActionService)fileActionService)?.ActiveBackendId ?? "None"}");

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

            if (browserFileActionCoordinator != null)
            {
                browserFileActionCoordinator.StatusMessagePublished += HandleBrowserFileActionStatusPublished;
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

            if (browserFileActionCoordinator != null)
            {
                browserFileActionCoordinator.StatusMessagePublished -= HandleBrowserFileActionStatusPublished;
            }
        }

        public void OpenNewProjectDialog()
        {
            newProjectDialog?.Open();
        }

        public bool SaveProject(string filePath)
        {
            var didSave = fileActionService != null && fileActionService.SaveProject(filePath);
            if (didSave && statusBar != null)
            {
                statusBar.StatusMessage = "Saved sandbox project.";
            }

            RefreshLifecycleState();
            return didSave;
        }

        public bool SaveProjectToBrowserLibrary(string projectName = "")
        {
            var didSave = saveLoadService != null && saveLoadService.SaveActiveProjectToLibrary(projectName);
            if (statusBar != null)
            {
                statusBar.StatusMessage = didSave
                    ? "Saved sandbox project to browser storage."
                    : saveLoadService?.LastError ?? "Project was not saved.";
            }

            RefreshLifecycleState();
            return didSave;
        }

        public SandboxSavedProjectInfo[] GetSavedBrowserProjects()
        {
            return saveLoadService?.GetSavedProjects() ?? System.Array.Empty<SandboxSavedProjectInfo>();
        }

        public bool LoadBrowserProject(string projectId)
        {
            var project = saveLoadService?.LoadProjectFromLibrary(projectId);
            if (statusBar != null)
            {
                statusBar.StatusMessage = project != null
                    ? "Loaded sandbox project from browser storage."
                    : saveLoadService?.LastError ?? "Project was not loaded.";
            }

            RefreshLifecycleState();
            return project != null;
        }

        public bool DeleteBrowserProject(string projectId)
        {
            var didDelete = saveLoadService != null && saveLoadService.DeleteProjectFromLibrary(projectId);
            if (statusBar != null)
            {
                statusBar.StatusMessage = didDelete
                    ? "Deleted saved sandbox project."
                    : saveLoadService?.LastError ?? "Project was not deleted.";
            }

            return didDelete;
        }

        public bool LoadProject(string filePath)
        {
            if (fileActionService == null || fileActionService.LoadProject(filePath) == null)
            {
                return false;
            }
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Loaded sandbox project.";
            }

            RefreshLifecycleState();
            return true;
        }

        public bool ExportProjectJson(string filePath)
        {
            var didExport = fileActionService != null && fileActionService.ExportProjectJson(filePath);
            if (didExport && statusBar != null)
            {
                statusBar.StatusMessage = "Exported full sandbox project JSON.";
            }

            return didExport;
        }

        public bool ImportProjectJson(string filePath)
        {
            var project = fileActionService?.ImportProjectJson(filePath);
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
            var didExport = fileActionService != null && fileActionService.ExportRuntimeProjectData(filePath);
            if (didExport && statusBar != null)
            {
                statusBar.StatusMessage = "Exported runtime-ready sandbox data.";
            }

            RefreshLifecycleState();
            return didExport;
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return fileActionService == null
                ? new SandboxFloorImportAnalysis()
                : fileActionService.AnalyzeFloorImport(filePath, selectedFloorIds);
        }

        public bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            var didImport = fileActionService != null && fileActionService.ImportFloors(filePath, selectedFloorIds);
            if (didImport && statusBar != null)
            {
                statusBar.StatusMessage = "Imported selected floors into the current project.";
            }

            RefreshLifecycleState();
            return didImport;
        }

        public bool TryRestoreRecovery()
        {
            var didRestore = fileActionService != null && fileActionService.TryRestoreRecovery();
            if (didRestore)
            {
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
            fileActionService?.DismissRecoveryPrompt();
        }

        public bool RequestBrowserProjectJsonImport()
        {
            var didRequest = browserFileActionCoordinator != null && browserFileActionCoordinator.RequestProjectJsonImport();
            Debug.Log($"SandboxTopBarShell: RequestBrowserProjectJsonImport result={didRequest}");
            if (!didRequest && statusBar != null)
            {
                statusBar.StatusMessage = "Browser JSON import request did not start.";
            }

            return didRequest;
        }

        public bool RequestBrowserProjectJsonExport()
        {
            var didRequest = browserFileActionCoordinator != null && browserFileActionCoordinator.RequestProjectJsonExport();
            Debug.Log($"SandboxTopBarShell: RequestBrowserProjectJsonExport result={didRequest}");
            if (!didRequest && statusBar != null)
            {
                statusBar.StatusMessage = "Browser JSON export request did not start.";
            }

            return didRequest;
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

            return previewImageExportBackend != null && previewImageExportBackend.TryExportActiveBlueprintPreview(destinationPath);
        }

        public bool ExportSimulationSummaryReport(string destinationPath)
        {
            var didExport = agentSimulationService != null && agentSimulationService.ExportSimulationSummaryReport(destinationPath);
            if (statusBar != null)
            {
                statusBar.StatusMessage = didExport
                    ? "Exported simulation summary report."
                    : "Run a successful simulation before exporting the summary report.";
            }

            return didExport;
        }

        public bool ExportSimulationTravelDensityHeatmapReport(string destinationPath)
        {
            var didExport = agentSimulationService != null && agentSimulationService.ExportSimulationTravelDensityHeatmapReport(destinationPath);
            if (statusBar != null)
            {
                statusBar.StatusMessage = didExport
                    ? "Exported simulation travel density heatmap report."
                    : "Run a successful simulation before exporting the heatmap report.";
            }

            return didExport;
        }

        public void RebuildAll()
        {
            projectRefreshService?.RefreshDerivedProjectState();
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

        private void HandleBrowserFileActionStatusPublished(string message)
        {
            if (statusBar != null && !string.IsNullOrWhiteSpace(message))
            {
                statusBar.StatusMessage = message;
            }

            RefreshLifecycleState();
        }

        private void RefreshLifecycleState()
        {
            lifecycleStateLabel = BuildingLifecycleStateUtility.GetDisplayLabel(
                workspaceService?.ActiveProject?.LifecycleState ?? BuildingLifecycleState.Draft);
        }
    }
}
