using System;
using EvacLogix.Sandbox.Data;
using UnityEngine;
using System.Runtime.InteropServices;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxWebGlBrowserFileBridgeAdapter : MonoBehaviour, ISandboxBrowserFileBridgeAdapter
    {
        private const string BridgeUnavailableMessage =
            "Browser file bridge adapter is present, but no active JavaScript bridge binding has been registered yet.";

        private Action<SandboxBrowserBridgeResponse<SandboxImportedFileData>> pendingImportCallback;
        private Action<SandboxBrowserBridgeResponse<SandboxExportFileData>> pendingExportCallback;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int EvacLogixSandboxBridge_IsAvailable();

        [DllImport("__Internal")]
        private static extern void EvacLogixSandboxBridge_RequestImport(string gameObjectName, string requestJson);

        [DllImport("__Internal")]
        private static extern void EvacLogixSandboxBridge_RequestExport(string gameObjectName, string requestJson);
#endif

        public bool IsBridgeAvailable
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    return EvacLogixSandboxBridge_IsAvailable() != 0;
                }
                catch
                {
                    return false;
                }
#else
                return false;
#endif
            }
        }

        public SandboxBrowserBridgeResponse<SandboxImportedFileData> ExecuteImportRequest(SandboxBrowserBridgeRequest request)
        {
            return CreateUnavailableImportResponse(request);
        }

        public SandboxBrowserBridgeResponse<SandboxExportFileData> ExecuteExportRequest(SandboxBrowserBridgeRequest request)
        {
            return CreateUnavailableExportResponse(request);
        }

        public void ExecuteImportRequestAsync(
            SandboxBrowserBridgeRequest request,
            Action<SandboxBrowserBridgeResponse<SandboxImportedFileData>> onCompleted)
        {
            Debug.Log($"SandboxWebGlBrowserFileBridgeAdapter: ExecuteImportRequestAsync command={request?.command}, bridgeAvailable={IsBridgeAvailable}");
            if (!IsBridgeAvailable)
            {
                onCompleted?.Invoke(CreateUnavailableImportResponse(request));
                return;
            }

            if (pendingImportCallback != null)
            {
                onCompleted?.Invoke(CreateBusyImportResponse(request));
                return;
            }

            pendingImportCallback = onCompleted;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                Debug.Log("SandboxWebGlBrowserFileBridgeAdapter: dispatching import request to JavaScript bridge.");
                EvacLogixSandboxBridge_RequestImport(gameObject.name, JsonUtility.ToJson(request));
            }
            catch (Exception exception)
            {
                var callback = pendingImportCallback;
                pendingImportCallback = null;
                callback?.Invoke(new SandboxBrowserBridgeResponse<SandboxImportedFileData>
                {
                    command = request?.command ?? SandboxBrowserBridgeCommand.ImportProjectJson,
                    outcome = SandboxFileActionOutcome.Error,
                    errorCode = SandboxFileActionErrorCode.BridgeUnavailable,
                    message = exception.Message,
                    payload = null
                });
            }
#else
            var callback = pendingImportCallback;
            pendingImportCallback = null;
            callback?.Invoke(CreateUnavailableImportResponse(request));
#endif
        }

        public void ExecuteExportRequestAsync(
            SandboxBrowserBridgeRequest request,
            Action<SandboxBrowserBridgeResponse<SandboxExportFileData>> onCompleted)
        {
            Debug.Log($"SandboxWebGlBrowserFileBridgeAdapter: ExecuteExportRequestAsync command={request?.command}, bridgeAvailable={IsBridgeAvailable}");
            if (!IsBridgeAvailable)
            {
                onCompleted?.Invoke(CreateUnavailableExportResponse(request));
                return;
            }

            if (pendingExportCallback != null)
            {
                onCompleted?.Invoke(CreateBusyExportResponse(request));
                return;
            }

            pendingExportCallback = onCompleted;

#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                Debug.Log("SandboxWebGlBrowserFileBridgeAdapter: dispatching export request to JavaScript bridge.");
                EvacLogixSandboxBridge_RequestExport(gameObject.name, JsonUtility.ToJson(request));
            }
            catch (Exception exception)
            {
                var callback = pendingExportCallback;
                pendingExportCallback = null;
                callback?.Invoke(new SandboxBrowserBridgeResponse<SandboxExportFileData>
                {
                    command = request?.command ?? SandboxBrowserBridgeCommand.ExportProjectJson,
                    outcome = SandboxFileActionOutcome.Error,
                    errorCode = SandboxFileActionErrorCode.BridgeUnavailable,
                    message = exception.Message,
                    payload = null
                });
            }
#else
            var callback = pendingExportCallback;
            pendingExportCallback = null;
            callback?.Invoke(CreateUnavailableExportResponse(request));
#endif
        }

        public void HandleImportResponse(string responseJson)
        {
            Debug.Log($"SandboxWebGlBrowserFileBridgeAdapter: HandleImportResponse raw={responseJson}");
            var callback = pendingImportCallback;
            pendingImportCallback = null;
            callback?.Invoke(ParseImportResponse(responseJson));
        }

        public void HandleExportResponse(string responseJson)
        {
            Debug.Log($"SandboxWebGlBrowserFileBridgeAdapter: HandleExportResponse raw={responseJson}");
            var callback = pendingExportCallback;
            pendingExportCallback = null;
            callback?.Invoke(ParseExportResponse(responseJson));
        }

        private static SandboxBrowserBridgeResponse<SandboxImportedFileData> CreateUnavailableImportResponse(
            SandboxBrowserBridgeRequest request
        )
        {
            return new SandboxBrowserBridgeResponse<SandboxImportedFileData>
            {
                command = request?.command ?? SandboxBrowserBridgeCommand.ImportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.BridgeUnavailable,
                message = BridgeUnavailableMessage,
                payload = null
            };
        }

        private static SandboxBrowserBridgeResponse<SandboxExportFileData> CreateUnavailableExportResponse(
            SandboxBrowserBridgeRequest request
        )
        {
            return new SandboxBrowserBridgeResponse<SandboxExportFileData>
            {
                command = request?.command ?? SandboxBrowserBridgeCommand.ExportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.BridgeUnavailable,
                message = BridgeUnavailableMessage,
                payload = null
            };
        }

        private static SandboxBrowserBridgeResponse<SandboxImportedFileData> CreateBusyImportResponse(
            SandboxBrowserBridgeRequest request
        )
        {
            return new SandboxBrowserBridgeResponse<SandboxImportedFileData>
            {
                command = request?.command ?? SandboxBrowserBridgeCommand.ImportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.UnexpectedInternalError,
                message = "Another browser file request is already in progress.",
                payload = null
            };
        }

        private static SandboxBrowserBridgeResponse<SandboxExportFileData> CreateBusyExportResponse(
            SandboxBrowserBridgeRequest request
        )
        {
            return new SandboxBrowserBridgeResponse<SandboxExportFileData>
            {
                command = request?.command ?? SandboxBrowserBridgeCommand.ExportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.UnexpectedInternalError,
                message = "Another browser file request is already in progress.",
                payload = null
            };
        }

        private static SandboxBrowserBridgeResponse<SandboxImportedFileData> ParseImportResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return CreateUnavailableImportResponse(null);
            }

            try
            {
                return JsonUtility.FromJson<SandboxBrowserBridgeResponse<SandboxImportedFileData>>(responseJson)
                    ?? CreateUnavailableImportResponse(null);
            }
            catch
            {
                return SandboxBrowserBridgeResponseErrorImport("The browser import response could not be parsed.");
            }
        }

        private static SandboxBrowserBridgeResponse<SandboxExportFileData> ParseExportResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return CreateUnavailableExportResponse(null);
            }

            try
            {
                return JsonUtility.FromJson<SandboxBrowserBridgeResponse<SandboxExportFileData>>(responseJson)
                    ?? CreateUnavailableExportResponse(null);
            }
            catch
            {
                return SandboxBrowserBridgeResponseErrorExport("The browser export response could not be parsed.");
            }
        }

        private static SandboxBrowserBridgeResponse<SandboxImportedFileData> SandboxBrowserBridgeResponseErrorImport(string message)
        {
            return new SandboxBrowserBridgeResponse<SandboxImportedFileData>
            {
                command = SandboxBrowserBridgeCommand.ImportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.ReadFailure,
                message = message,
                payload = null
            };
        }

        private static SandboxBrowserBridgeResponse<SandboxExportFileData> SandboxBrowserBridgeResponseErrorExport(string message)
        {
            return new SandboxBrowserBridgeResponse<SandboxExportFileData>
            {
                command = SandboxBrowserBridgeCommand.ExportProjectJson,
                outcome = SandboxFileActionOutcome.Error,
                errorCode = SandboxFileActionErrorCode.ReadFailure,
                message = message,
                payload = null
            };
        }
    }
}
