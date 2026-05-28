using System;
using EvacLogix.Sandbox.Data;

namespace EvacLogix.Sandbox.Infrastructure
{
    public interface ISandboxBrowserFileBridgeAdapter
    {
        bool IsBridgeAvailable { get; }
        SandboxBrowserBridgeResponse<SandboxImportedFileData> ExecuteImportRequest(SandboxBrowserBridgeRequest request);
        SandboxBrowserBridgeResponse<SandboxExportFileData> ExecuteExportRequest(SandboxBrowserBridgeRequest request);
        void ExecuteImportRequestAsync(
            SandboxBrowserBridgeRequest request,
            Action<SandboxBrowserBridgeResponse<SandboxImportedFileData>> onCompleted);
        void ExecuteExportRequestAsync(
            SandboxBrowserBridgeRequest request,
            Action<SandboxBrowserBridgeResponse<SandboxExportFileData>> onCompleted);
    }
}
