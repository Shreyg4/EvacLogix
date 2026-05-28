using System;
using System.Collections.Generic;
using System.IO;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxDesktopFileActionBackend : MonoBehaviour, ISandboxFileActionBackend
    {
        private SandboxBlueprintImportService blueprintImportService;
        private SandboxProjectTransferService projectTransferService;
        private SandboxProjectRefreshService projectRefreshService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSaveLoadService saveLoadService;

        public string BackendId => "desktop-editor";
        public int BackendPriority => 0;
        public bool IsSupportedInCurrentRuntime => Application.platform != RuntimePlatform.WebGLPlayer || Application.isEditor;

        public string LastError
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(projectTransferService?.LastError))
                {
                    return projectTransferService.LastError;
                }

                return saveLoadService?.LastError ?? string.Empty;
            }
        }

        public bool HasRecoveryPrompt => saveLoadService != null && saveLoadService.HasRecoveryPrompt;
        public string RecoveryPromptMessage => saveLoadService?.RecoveryPromptMessage ?? string.Empty;

        private void Awake()
        {
            RefreshDependenciesIfNeeded();
        }

        public bool SaveProject(string filePath)
        {
            RefreshDependenciesIfNeeded();
            return saveLoadService != null && saveLoadService.SaveActiveProjectToPath(filePath);
        }

        public BuildingProjectData LoadProject(string filePath)
        {
            RefreshDependenciesIfNeeded();
            if (saveLoadService == null)
            {
                return null;
            }

            var project = saveLoadService.LoadProjectFromPath(filePath);
            if (project == null)
            {
                return null;
            }

            projectRefreshService?.RefreshDerivedProjectState();
            return project;
        }

        public bool ExportProjectJson(string filePath)
        {
            RefreshDependenciesIfNeeded();
            return projectTransferService != null && projectTransferService.ExportProjectJson(filePath);
        }

        public BuildingProjectData ImportProjectJson(string filePath)
        {
            RefreshDependenciesIfNeeded();
            return projectTransferService?.ImportProjectJson(filePath);
        }

        public bool ExportRuntimeProjectData(string filePath)
        {
            RefreshDependenciesIfNeeded();
            return projectTransferService != null && projectTransferService.ExportRuntimeProjectData(filePath);
        }

        public BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath)
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveFloor == null || blueprintImportService == null)
            {
                return null;
            }

            var blueprintReference = blueprintImportService.ImportBlueprint(sourceFilePath);
            workspaceService.AddBlueprintReference(blueprintReference);
            workspaceService.AssignBlueprintToFloor(workspaceService.ActiveFloor.floorId, blueprintReference.blueprintReferenceId);
            return blueprintReference;
        }

        public SandboxFileActionResult<BuildingProjectData> ImportProjectJson(SandboxImportedFileData fileData)
        {
            RefreshDependenciesIfNeeded();
            if (fileData == null)
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.ReadFailure, "Project import payload was missing.");
            }

            if (!LooksLikeJson(fileData))
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.UnsupportedType, "Project import requires a JSON file.");
            }

            var json = fileData.TryGetPayloadText();
            if (json == null)
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.ReadFailure, "Project import payload could not be decoded.");
            }

            var project = projectTransferService?.ImportProjectJsonContent(json, fileData.fileName);
            if (project == null)
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(ResolveJsonImportErrorCode(), LastError);
            }

            return SandboxFileActionResult<BuildingProjectData>.Success(project, $"Imported sandbox project '{fileData.fileName}'.");
        }

        public SandboxFileActionResult<BlueprintReferenceData> ImportBlueprintToActiveFloor(SandboxImportedFileData fileData)
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService == null)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(
                    SandboxFileActionErrorCode.UnexpectedInternalError,
                    "Workspace service was unavailable for blueprint import.");
            }

            if (blueprintImportService == null)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(
                    SandboxFileActionErrorCode.UnexpectedInternalError,
                    "Blueprint import service was unavailable.");
            }

            if (workspaceService.ActiveFloor == null)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No active floor is available for blueprint import.");
            }

            if (fileData == null)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.ReadFailure, "Blueprint import payload was missing.");
            }

            if (!LooksLikeSupportedBlueprint(fileData))
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnsupportedType, "Blueprint import currently supports PNG and JPEG files.");
            }

            var bytes = fileData.TryGetPayloadBytes();
            if (bytes == null || bytes.Length == 0)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.ReadFailure, "Blueprint import payload could not be decoded.");
            }

            try
            {
                var blueprintReference = blueprintImportService.ImportBlueprint(fileData.fileName, bytes);
                workspaceService.AddBlueprintReference(blueprintReference);
                workspaceService.AssignBlueprintToFloor(workspaceService.ActiveFloor.floorId, blueprintReference.blueprintReferenceId);
                return SandboxFileActionResult<BlueprintReferenceData>.Success(blueprintReference, $"Imported blueprint '{fileData.fileName}'.");
            }
            catch (InvalidOperationException exception)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnsupportedType, exception.Message);
            }
            catch (IOException exception)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.ReadFailure, exception.Message);
            }
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildProjectJsonExportPayload(bool prettyPrint = true)
        {
            RefreshDependenciesIfNeeded();
            var payload = projectTransferService?.BuildProjectJsonExportPayload(prettyPrint);
            return payload == null
                ? SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No active project was available to export.")
                : SandboxFileActionResult<SandboxExportFileData>.Success(payload, "Prepared sandbox project JSON export payload.");
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildRuntimeProjectExportPayload(bool prettyPrint = true)
        {
            RefreshDependenciesIfNeeded();
            var payload = projectTransferService?.BuildRuntimeProjectExportPayload(prettyPrint);
            return payload == null
                ? SandboxFileActionResult<SandboxExportFileData>.Error(ResolveRuntimeExportErrorCode(), LastError)
                : SandboxFileActionResult<SandboxExportFileData>.Success(payload, "Prepared runtime-ready sandbox project export payload.");
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            RefreshDependenciesIfNeeded();
            return projectTransferService == null
                ? new SandboxFloorImportAnalysis()
                : projectTransferService.AnalyzeFloorImportFromPath(filePath, selectedFloorIds);
        }

        public bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            RefreshDependenciesIfNeeded();
            return projectTransferService != null && projectTransferService.ImportFloorsFromPath(filePath, selectedFloorIds);
        }

        public bool TryRestoreRecovery()
        {
            RefreshDependenciesIfNeeded();
            if (saveLoadService == null || !saveLoadService.TryRestoreRecovery())
            {
                return false;
            }

            projectRefreshService?.RefreshDerivedProjectState();
            return true;
        }

        public void DismissRecoveryPrompt()
        {
            RefreshDependenciesIfNeeded();
            saveLoadService?.DismissRecoveryPrompt();
        }

        private void RefreshDependenciesIfNeeded()
        {
            blueprintImportService ??= GetComponent<SandboxBlueprintImportService>();
            projectTransferService ??= GetComponent<SandboxProjectTransferService>();
            projectRefreshService ??= GetComponent<SandboxProjectRefreshService>();
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            saveLoadService ??= GetComponent<SandboxSaveLoadService>();
        }

        private SandboxFileActionErrorCode ResolveJsonImportErrorCode()
        {
            return string.IsNullOrWhiteSpace(LastError)
                ? SandboxFileActionErrorCode.UnexpectedInternalError
                : SandboxFileActionErrorCode.ParseFailure;
        }

        private SandboxFileActionErrorCode ResolveRuntimeExportErrorCode()
        {
            if (!string.IsNullOrWhiteSpace(LastError) &&
                LastError.Contains("validation", StringComparison.OrdinalIgnoreCase))
            {
                return SandboxFileActionErrorCode.ValidationFailure;
            }

            return SandboxFileActionErrorCode.UnexpectedInternalError;
        }

        private static bool LooksLikeJson(SandboxImportedFileData fileData)
        {
            var extension = Path.GetExtension(fileData.fileName);
            return string.Equals(fileData.mimeType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeSupportedBlueprint(SandboxImportedFileData fileData)
        {
            var extension = Path.GetExtension(fileData.fileName);
            return string.Equals(fileData.mimeType, "image/png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileData.mimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileData.mimeType, "image/webp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileData.mimeType, "image/bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileData.mimeType, "image/gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileData.mimeType, "image/tiff", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase);
        }
    }
}
