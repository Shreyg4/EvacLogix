using System;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxBrowserFileActionCoordinator : MonoBehaviour
    {
        private ISandboxBrowserFileBridgeAdapter bridgeAdapter;
        private SandboxFileActionService fileActionService;
        private bool isBusy;

        public bool IsBusy => isBusy;
        public bool SupportsBrowserFileActions
        {
            get
            {
                RefreshDependenciesIfNeeded();
                return fileActionService != null &&
                    string.Equals(fileActionService.ActiveBackendId, "webgl-browser", StringComparison.OrdinalIgnoreCase) &&
                    bridgeAdapter != null;
            }
        }

        public event Action<string> StatusMessagePublished;

        private void Awake()
        {
            RefreshDependenciesIfNeeded();
            Debug.Log(
                "SandboxBrowserFileActionCoordinator initialized: " +
                $"BridgeAdapterPresent={bridgeAdapter != null}, " +
                $"BridgeAvailable={bridgeAdapter != null && bridgeAdapter.IsBridgeAvailable}, " +
                $"ActiveBackend={fileActionService?.ActiveBackendId ?? "None"}, " +
                $"SupportsBrowserFileActions={SupportsBrowserFileActions}");
        }

        public bool RequestProjectJsonImport()
        {
            Debug.Log("SandboxBrowserFileActionCoordinator: RequestProjectJsonImport invoked.");
            if (!TryBeginOperation("Project import is unavailable because the browser file bridge is not ready."))
            {
                return false;
            }

            PublishStatus("Choose a sandbox project JSON file to import.");
            bridgeAdapter.ExecuteImportRequestAsync(
                new SandboxBrowserBridgeRequest
                {
                    command = SandboxBrowserBridgeCommand.ImportProjectJson,
                    importPolicy = SandboxBrowserBridgePolicies.CreateProjectJsonImportPolicy(),
                },
                HandleProjectImportResponse);
            return true;
        }

        public bool RequestProjectJsonExport()
        {
            Debug.Log("SandboxBrowserFileActionCoordinator: RequestProjectJsonExport invoked.");
            if (!TryBeginOperation("Project export is unavailable because the browser file bridge is not ready."))
            {
                return false;
            }

            var payloadResult = fileActionService.BuildProjectJsonExportPayload();
            if (payloadResult.outcome != SandboxFileActionOutcome.Success || payloadResult.payload == null)
            {
                isBusy = false;
                PublishStatus(string.IsNullOrWhiteSpace(payloadResult.message)
                    ? "Project export payload could not be prepared."
                    : payloadResult.message);
                return false;
            }

            PublishStatus("Preparing browser download for the current sandbox project.");
            bridgeAdapter.ExecuteExportRequestAsync(
                new SandboxBrowserBridgeRequest
                {
                    command = SandboxBrowserBridgeCommand.ExportProjectJson,
                    exportPayload = payloadResult.payload,
                },
                HandleProjectExportResponse);
            return true;
        }

        public bool RequestBlueprintImageImport()
        {
            Debug.Log("SandboxBrowserFileActionCoordinator: RequestBlueprintImageImport invoked.");
            if (!TryBeginOperation("Blueprint import is unavailable because the browser file bridge is not ready."))
            {
                return false;
            }

            PublishStatus("Choose a blueprint image to import for the active floor.");
            bridgeAdapter.ExecuteImportRequestAsync(
                new SandboxBrowserBridgeRequest
                {
                    command = SandboxBrowserBridgeCommand.ImportBlueprintImage,
                    importPolicy = SandboxBrowserBridgePolicies.CreateBlueprintImageImportPolicy(),
                },
                HandleBlueprintImportResponse);
            return true;
        }

        private bool TryBeginOperation(string unavailableMessage)
        {
            if (isBusy)
            {
                Debug.LogWarning("SandboxBrowserFileActionCoordinator: browser file action rejected because another action is already in progress.");
                PublishStatus("A browser file action is already in progress.");
                return false;
            }

            if (!SupportsBrowserFileActions)
            {
                Debug.LogWarning(
                    "SandboxBrowserFileActionCoordinator: browser file action unavailable. " +
                    $"ActiveBackend={fileActionService?.ActiveBackendId ?? "None"}, " +
                    $"BridgeAdapterPresent={bridgeAdapter != null}, " +
                    $"BridgeAvailable={bridgeAdapter != null && bridgeAdapter.IsBridgeAvailable}");
                PublishStatus(unavailableMessage);
                return false;
            }

            if (bridgeAdapter == null || !bridgeAdapter.IsBridgeAvailable)
            {
                Debug.LogWarning(
                    "SandboxBrowserFileActionCoordinator: browser bridge is not currently available even though browser mode is active.");
                PublishStatus("Browser file actions are active, but the page bridge is not available yet.");
                return false;
            }

            isBusy = true;
            Debug.Log("SandboxBrowserFileActionCoordinator: browser file action started.");
            return true;
        }

        private void RefreshDependenciesIfNeeded()
        {
            if (bridgeAdapter == null)
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
            }

            if (fileActionService == null || string.IsNullOrWhiteSpace(fileActionService.ActiveBackendId))
            {
                fileActionService = GetComponent<SandboxFileActionService>();
                if (fileActionService == null)
                {
                    fileActionService = FindAnyObjectByType<SandboxFileActionService>();
                }
            }
        }

        private void HandleProjectImportResponse(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            isBusy = false;
            Debug.Log($"SandboxBrowserFileActionCoordinator: project import response outcome={response?.outcome}, error={response?.errorCode}, message={response?.message}");
            PublishStatus(ResolveProjectImportMessage(response));
        }

        private void HandleBlueprintImportResponse(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            isBusy = false;
            Debug.Log($"SandboxBrowserFileActionCoordinator: blueprint import response outcome={response?.outcome}, error={response?.errorCode}, message={response?.message}");
            PublishStatus(ResolveBlueprintImportMessage(response));
        }

        private void HandleProjectExportResponse(SandboxBrowserBridgeResponse<SandboxExportFileData> response)
        {
            isBusy = false;
            Debug.Log($"SandboxBrowserFileActionCoordinator: project export response outcome={response?.outcome}, error={response?.errorCode}, message={response?.message}");
            PublishStatus(ResolveExportMessage(response));
        }

        private string ResolveProjectImportMessage(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            if (response == null)
            {
                return "Project import failed because the browser did not return a response.";
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return string.IsNullOrWhiteSpace(response.message) ? "Project import was cancelled." : response.message;
            }

            if (response.outcome == SandboxFileActionOutcome.Error)
            {
                return string.IsNullOrWhiteSpace(response.message) ? "Project import failed." : response.message;
            }

            var result = fileActionService.ImportProjectJson(response.payload);
            return string.IsNullOrWhiteSpace(result.message)
                ? (result.outcome == SandboxFileActionOutcome.Success ? "Imported sandbox project JSON." : "Project import failed.")
                : result.message;
        }

        private string ResolveBlueprintImportMessage(SandboxBrowserBridgeResponse<SandboxImportedFileData> response)
        {
            if (response == null)
            {
                return "Blueprint import failed because the browser did not return a response.";
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return string.IsNullOrWhiteSpace(response.message) ? "Blueprint import was cancelled." : response.message;
            }

            if (response.outcome == SandboxFileActionOutcome.Error)
            {
                return string.IsNullOrWhiteSpace(response.message) ? "Blueprint import failed." : response.message;
            }

            var result = fileActionService.ImportBlueprintToActiveFloor(response.payload);
            return string.IsNullOrWhiteSpace(result.message)
                ? (result.outcome == SandboxFileActionOutcome.Success ? "Imported blueprint image." : "Blueprint import failed.")
                : result.message;
        }

        private static string ResolveExportMessage(SandboxBrowserBridgeResponse<SandboxExportFileData> response)
        {
            if (response == null)
            {
                return "Project export failed because the browser did not return a response.";
            }

            if (response.outcome == SandboxFileActionOutcome.Cancelled)
            {
                return string.IsNullOrWhiteSpace(response.message) ? "Project export was cancelled." : response.message;
            }

            return string.IsNullOrWhiteSpace(response.message)
                ? (response.outcome == SandboxFileActionOutcome.Success ? "Downloaded sandbox project JSON." : "Project export failed.")
                : response.message;
        }

        private void PublishStatus(string message)
        {
            Debug.Log($"SandboxBrowserFileActionCoordinator status: {message}");
            StatusMessagePublished?.Invoke(message ?? string.Empty);
        }
    }
}
