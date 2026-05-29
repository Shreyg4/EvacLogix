using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxFileActionService : MonoBehaviour, ISandboxFileActionService
    {
        private ISandboxFileActionBackend activeBackend;
        public string ActiveBackendId => activeBackend?.BackendId ?? string.Empty;

        public string LastError
        {
            get => activeBackend?.LastError ?? string.Empty;
        }

        public bool HasRecoveryPrompt => activeBackend != null && activeBackend.HasRecoveryPrompt;
        public string RecoveryPromptMessage => activeBackend?.RecoveryPromptMessage ?? string.Empty;

        private void Awake()
        {
            activeBackend = GetComponents<MonoBehaviour>()
                .OfType<ISandboxFileActionBackend>()
                .Where(candidate => candidate.IsSupportedInCurrentRuntime)
                .OrderByDescending(candidate => candidate.BackendPriority)
                .FirstOrDefault();
        }

        public bool SaveProject(string filePath)
        {
            return activeBackend != null && activeBackend.SaveProject(filePath);
        }

        public BuildingProjectData LoadProject(string filePath)
        {
            return activeBackend?.LoadProject(filePath);
        }

        public bool ExportProjectJson(string filePath)
        {
            return activeBackend != null && activeBackend.ExportProjectJson(filePath);
        }

        public BuildingProjectData ImportProjectJson(string filePath)
        {
            return activeBackend?.ImportProjectJson(filePath);
        }

        public bool ExportRuntimeProjectData(string filePath)
        {
            return activeBackend != null && activeBackend.ExportRuntimeProjectData(filePath);
        }

        public BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath)
        {
            return activeBackend?.ImportBlueprintToActiveFloor(sourceFilePath);
        }

        public SandboxFileActionResult<BuildingProjectData> ImportProjectJson(SandboxImportedFileData fileData)
        {
            return activeBackend == null
                ? SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No file-action backend is available.")
                : activeBackend.ImportProjectJson(fileData);
        }

        public SandboxFileActionResult<BlueprintReferenceData> ImportBlueprintToActiveFloor(SandboxImportedFileData fileData)
        {
            return activeBackend == null
                ? SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No file-action backend is available.")
                : activeBackend.ImportBlueprintToActiveFloor(fileData);
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildProjectJsonExportPayload(bool prettyPrint = true)
        {
            return activeBackend == null
                ? SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No file-action backend is available.")
                : activeBackend.BuildProjectJsonExportPayload(prettyPrint);
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildRuntimeProjectExportPayload(bool prettyPrint = true)
        {
            return activeBackend == null
                ? SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "No file-action backend is available.")
                : activeBackend.BuildRuntimeProjectExportPayload(prettyPrint);
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return activeBackend == null
                ? new SandboxFloorImportAnalysis()
                : activeBackend.AnalyzeFloorImport(filePath, selectedFloorIds);
        }

        public bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return activeBackend != null && activeBackend.ImportFloors(filePath, selectedFloorIds);
        }

        public bool TryRestoreRecovery()
        {
            return activeBackend != null && activeBackend.TryRestoreRecovery();
        }

        public void DismissRecoveryPrompt()
        {
            activeBackend?.DismissRecoveryPrompt();
        }
    }
}
