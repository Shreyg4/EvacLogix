using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxWebGlFileActionBackend : MonoBehaviour, ISandboxFileActionBackend
    {
        private const string PathWorkflowUnavailableMessage = "WebGL backend does not support arbitrary local file paths.";
        private const string BridgeUnavailableMessage = "Browser file bridge is unavailable.";

        private ISandboxBrowserFileBridgeAdapter bridgeAdapter;
        private SandboxDesktopFileActionBackend desktopBackend;
        private string lastError = string.Empty;

        public string BackendId => "webgl-browser";
        public int BackendPriority => 100;
        public bool IsSupportedInCurrentRuntime =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor;
#endif

        public string LastError => lastError;
        public bool HasRecoveryPrompt => desktopBackend != null && desktopBackend.HasRecoveryPrompt;
        public string RecoveryPromptMessage => desktopBackend?.RecoveryPromptMessage ?? string.Empty;

        private void Awake()
        {
            bridgeAdapter = GetComponent<MonoBehaviour>() as ISandboxBrowserFileBridgeAdapter;
            if (bridgeAdapter == null)
            {
                foreach (var candidate in GetComponents<MonoBehaviour>())
                {
                    if (candidate is ISandboxBrowserFileBridgeAdapter adapter)
                    {
                        bridgeAdapter = adapter;
                        break;
                    }
                }
            }

            desktopBackend = GetComponent<SandboxDesktopFileActionBackend>();
        }

        public bool SaveProject(string filePath)
        {
            return FailPathWorkflow();
        }

        public BuildingProjectData LoadProject(string filePath)
        {
            FailPathWorkflow();
            return null;
        }

        public bool ExportProjectJson(string filePath)
        {
            return FailPathWorkflow();
        }

        public BuildingProjectData ImportProjectJson(string filePath)
        {
            FailPathWorkflow();
            return null;
        }

        public bool ExportRuntimeProjectData(string filePath)
        {
            return FailPathWorkflow();
        }

        public BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath)
        {
            FailPathWorkflow();
            return null;
        }

        public SandboxFileActionResult<BuildingProjectData> ImportProjectJson(SandboxImportedFileData fileData)
        {
            if (fileData != null)
            {
                return desktopBackend != null
                    ? desktopBackend.ImportProjectJson(fileData)
                    : SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style import handler was unavailable.");
            }

            if (!IsBridgeAvailable())
            {
                return BridgeUnavailableResult<BuildingProjectData>();
            }

            var response = bridgeAdapter.ExecuteImportRequest(new SandboxBrowserBridgeRequest
            {
                command = SandboxBrowserBridgeCommand.ImportProjectJson,
                importPolicy = SandboxBrowserBridgePolicies.CreateProjectJsonImportPolicy(),
            });

            var result = TranslateProjectImportResponse(response);
            UpdateLastError(result);
            return result;
        }

        public SandboxFileActionResult<BlueprintReferenceData> ImportBlueprintToActiveFloor(SandboxImportedFileData fileData)
        {
            if (fileData != null)
            {
                return desktopBackend != null
                    ? desktopBackend.ImportBlueprintToActiveFloor(fileData)
                    : SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style blueprint import handler was unavailable.");
            }

            if (!IsBridgeAvailable())
            {
                return BridgeUnavailableResult<BlueprintReferenceData>();
            }

            var response = bridgeAdapter.ExecuteImportRequest(new SandboxBrowserBridgeRequest
            {
                command = SandboxBrowserBridgeCommand.ImportBlueprintImage,
                importPolicy = SandboxBrowserBridgePolicies.CreateBlueprintImageImportPolicy(),
            });

            var result = TranslateBlueprintImportResponse(response);
            UpdateLastError(result);
            return result;
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildProjectJsonExportPayload(bool prettyPrint = true)
        {
            if (desktopBackend == null)
            {
                return SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style export payload builder was unavailable.");
            }

            var payloadResult = desktopBackend.BuildProjectJsonExportPayload(prettyPrint);
            UpdateLastError(payloadResult);
            return payloadResult;
        }

        public SandboxFileActionResult<SandboxExportFileData> BuildRuntimeProjectExportPayload(bool prettyPrint = true)
        {
            if (desktopBackend == null)
            {
                return SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style runtime export payload builder was unavailable.");
            }

            var payloadResult = desktopBackend.BuildRuntimeProjectExportPayload(prettyPrint);
            UpdateLastError(payloadResult);
            return payloadResult;
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImport(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return desktopBackend == null
                ? new SandboxFloorImportAnalysis()
                : desktopBackend.AnalyzeFloorImport(filePath, selectedFloorIds);
        }

        public bool ImportFloors(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            return desktopBackend != null && desktopBackend.ImportFloors(filePath, selectedFloorIds);
        }

        public bool TryRestoreRecovery()
        {
            return desktopBackend != null && desktopBackend.TryRestoreRecovery();
        }

        public void DismissRecoveryPrompt()
        {
            desktopBackend?.DismissRecoveryPrompt();
        }

        private bool FailPathWorkflow()
        {
            lastError = PathWorkflowUnavailableMessage;
            return false;
        }

        private bool IsBridgeAvailable()
        {
            return bridgeAdapter != null && bridgeAdapter.IsBridgeAvailable;
        }

        private SandboxFileActionResult<TPayload> BridgeUnavailableResult<TPayload>()
        {
            lastError = BridgeUnavailableMessage;
            return SandboxFileActionResult<TPayload>.Error(SandboxFileActionErrorCode.BridgeUnavailable, BridgeUnavailableMessage);
        }

        private void UpdateLastError<TPayload>(SandboxFileActionResult<TPayload> result)
        {
            lastError = result == null || result.outcome == SandboxFileActionOutcome.Success
                ? string.Empty
                : result.message ?? string.Empty;
        }

        private SandboxFileActionResult<BuildingProjectData> TranslateProjectImportResponse(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            if (response == null)
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.BridgeUnavailable, BridgeUnavailableMessage);
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return SandboxFileActionResult<BuildingProjectData>.Cancelled(response.message);
            }

            if (response.outcome == SandboxFileActionOutcome.Error)
            {
                return SandboxFileActionResult<BuildingProjectData>.Error(response.errorCode, response.message);
            }

            return desktopBackend != null
                ? desktopBackend.ImportProjectJson(response.payload)
                : SandboxFileActionResult<BuildingProjectData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style import handler was unavailable.");
        }

        private SandboxFileActionResult<BlueprintReferenceData> TranslateBlueprintImportResponse(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            if (response == null)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.BridgeUnavailable, BridgeUnavailableMessage);
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Cancelled(response.message);
            }

            if (response.outcome == SandboxFileActionOutcome.Error)
            {
                return SandboxFileActionResult<BlueprintReferenceData>.Error(response.errorCode, response.message);
            }

            return desktopBackend != null
                ? desktopBackend.ImportBlueprintToActiveFloor(response.payload)
                : SandboxFileActionResult<BlueprintReferenceData>.Error(SandboxFileActionErrorCode.UnexpectedInternalError, "Desktop-style blueprint import handler was unavailable.");
        }

        private SandboxFileActionResult<SandboxExportFileData> TranslateExportResponse(SandboxBrowserBridgeResponse<SandboxExportFileData> response)
        {
            if (response == null)
            {
                return SandboxFileActionResult<SandboxExportFileData>.Error(SandboxFileActionErrorCode.BridgeUnavailable, BridgeUnavailableMessage);
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return SandboxFileActionResult<SandboxExportFileData>.Cancelled(response.message);
            }

            if (response.outcome == SandboxFileActionOutcome.Error)
            {
                return SandboxFileActionResult<SandboxExportFileData>.Error(response.errorCode, response.message);
            }

            return SandboxFileActionResult<SandboxExportFileData>.Success(response.payload, response.message);
        }
    }
}
