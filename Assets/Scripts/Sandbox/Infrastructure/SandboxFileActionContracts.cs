using System;
using System.Text;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxFileActionOutcome
    {
        Success = 0,
        Cancelled = 1,
        Error = 2,
    }

    public enum SandboxFileActionErrorCode
    {
        None = 0,
        UnsupportedType = 1,
        FileTooLarge = 2,
        ReadFailure = 3,
        ParseFailure = 4,
        MigrationFailure = 5,
        ValidationFailure = 6,
        BridgeUnavailable = 7,
        UnexpectedInternalError = 8,
    }

    public enum SandboxBrowserBridgeCommand
    {
        ImportBlueprintImage = 0,
        ImportProjectJson = 1,
        ExportProjectJson = 2,
    }

    [Serializable]
    public sealed class SandboxFileImportPolicy
    {
        public string[] allowedMimeTypes = Array.Empty<string>();
        public string[] allowedExtensions = Array.Empty<string>();
        public long maxSizeBytes;
    }

    [Serializable]
    public sealed class SandboxBrowserBridgeRequest
    {
        public SandboxBrowserBridgeCommand command;
        public SandboxFileImportPolicy importPolicy;
        public SandboxExportFileData exportPayload;
    }

    [Serializable]
    public sealed class SandboxBrowserBridgeResponse<TPayload>
    {
        public SandboxBrowserBridgeCommand command;
        public SandboxFileActionOutcome outcome = SandboxFileActionOutcome.Error;
        public SandboxFileActionErrorCode errorCode = SandboxFileActionErrorCode.None;
        public string message = string.Empty;
        public TPayload payload;
    }

    [Serializable]
    public sealed class SandboxImportedFileData
    {
        public string fileName = string.Empty;
        public string mimeType = string.Empty;
        public long sizeBytes;
        public string payloadBase64 = string.Empty;

        public byte[] TryGetPayloadBytes()
        {
            if (string.IsNullOrWhiteSpace(payloadBase64))
            {
                return Array.Empty<byte>();
            }

            try
            {
                return Convert.FromBase64String(payloadBase64);
            }
            catch
            {
                return null;
            }
        }

        public string TryGetPayloadText()
        {
            var bytes = TryGetPayloadBytes();
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }
    }

    public static class SandboxBrowserBridgePolicies
    {
        public const long DefaultBlueprintImageMaxSizeBytes = 25L * 1024L * 1024L;
        public const long DefaultProjectJsonMaxSizeBytes = 5L * 1024L * 1024L;

        public static SandboxFileImportPolicy CreateBlueprintImageImportPolicy()
        {
            return new SandboxFileImportPolicy
            {
                allowedMimeTypes = new[] { "image/png", "image/jpeg", "image/webp", "image/bmp", "image/gif", "image/tiff" },
                allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff" },
                maxSizeBytes = DefaultBlueprintImageMaxSizeBytes,
            };
        }

        public static SandboxFileImportPolicy CreateProjectJsonImportPolicy()
        {
            return new SandboxFileImportPolicy
            {
                allowedMimeTypes = new[] { "application/json", "text/json", "text/plain" },
                allowedExtensions = new[] { ".json" },
                maxSizeBytes = DefaultProjectJsonMaxSizeBytes,
            };
        }
    }

    [Serializable]
    public sealed class SandboxExportFileData
    {
        public string fileName = string.Empty;
        public string mimeType = string.Empty;
        public long sizeBytes;
        public string payloadBase64 = string.Empty;
    }

    [Serializable]
    public sealed class SandboxFileActionResult<TPayload>
    {
        public SandboxFileActionOutcome outcome = SandboxFileActionOutcome.Error;
        public SandboxFileActionErrorCode errorCode = SandboxFileActionErrorCode.None;
        public string message = string.Empty;
        public TPayload payload;

        public static SandboxFileActionResult<TPayload> Success(TPayload payload, string message = "")
        {
            return new SandboxFileActionResult<TPayload>
            {
                outcome = SandboxFileActionOutcome.Success,
                errorCode = SandboxFileActionErrorCode.None,
                message = message ?? string.Empty,
                payload = payload
            };
        }

        public static SandboxFileActionResult<TPayload> Cancelled(string message = "")
        {
            return new SandboxFileActionResult<TPayload>
            {
                outcome = SandboxFileActionOutcome.Cancelled,
                errorCode = SandboxFileActionErrorCode.None,
                message = message ?? string.Empty
            };
        }

        public static SandboxFileActionResult<TPayload> Error(SandboxFileActionErrorCode errorCode, string message)
        {
            return new SandboxFileActionResult<TPayload>
            {
                outcome = SandboxFileActionOutcome.Error,
                errorCode = errorCode,
                message = message ?? string.Empty
            };
        }
    }
}
