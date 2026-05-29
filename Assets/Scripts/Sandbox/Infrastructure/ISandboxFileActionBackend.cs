using System.Collections.Generic;
using EvacLogix.Sandbox.Data;

namespace EvacLogix.Sandbox.Infrastructure
{
    public interface ISandboxFileActionBackend
    {
        string BackendId { get; }
        int BackendPriority { get; }
        bool IsSupportedInCurrentRuntime { get; }
        string LastError { get; }
        bool HasRecoveryPrompt { get; }
        string RecoveryPromptMessage { get; }

        bool SaveProject(string filePath);
        BuildingProjectData LoadProject(string filePath);
        bool ExportProjectJson(string filePath);
        BuildingProjectData ImportProjectJson(string filePath);
        bool ExportRuntimeProjectData(string filePath);
        BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath);
        SandboxFileActionResult<BuildingProjectData> ImportProjectJson(SandboxImportedFileData fileData);
        SandboxFileActionResult<BlueprintReferenceData> ImportBlueprintToActiveFloor(SandboxImportedFileData fileData);
        SandboxFileActionResult<SandboxExportFileData> BuildProjectJsonExportPayload(bool prettyPrint = true);
        SandboxFileActionResult<SandboxExportFileData> BuildRuntimeProjectExportPayload(bool prettyPrint = true);
        SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null);
        bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null);
        bool TryRestoreRecovery();
        void DismissRecoveryPrompt();
    }
}
