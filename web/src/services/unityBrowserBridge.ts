import type {
  UnityBridgeCommand,
  UnityBridgeErrorCode,
  UnityBridgeExportFileData,
  UnityBridgeExportRequest,
  UnityBridgeFileImportPolicy,
  UnityBridgeImportRequest,
  UnityBridgeImportedFileData,
  UnityBridgeResponse,
  UnityBrowserBridgeApi
} from "../types/unityBridge";

function logBridge(event: string, details?: unknown): void {
  if (details === undefined) {
    console.log(`[UnityBrowserBridge] ${event}`);
    return;
  }

  console.log(`[UnityBrowserBridge] ${event}`, details);
}

function buildAcceptAttribute(policy: UnityBridgeFileImportPolicy): string {
  const tokens = [...policy.allowedMimeTypes, ...policy.allowedExtensions]
    .map((value) => value.trim())
    .filter(Boolean);

  return Array.from(new Set(tokens)).join(",");
}

function normalizeExtension(fileName: string): string {
  const dotIndex = fileName.lastIndexOf(".");
  return dotIndex >= 0 ? fileName.slice(dotIndex).toLowerCase() : "";
}

function normalizeMimeType(mimeType: string): string {
  return mimeType.trim().toLowerCase();
}

function fileMatchesPolicy(file: File, policy: UnityBridgeFileImportPolicy): boolean {
  const normalizedMimeType = normalizeMimeType(file.type);
  const normalizedExtension = normalizeExtension(file.name);
  const allowedMimeTypes = policy.allowedMimeTypes.map(normalizeMimeType);
  const allowedExtensions = policy.allowedExtensions.map((value) => value.toLowerCase());

  return allowedMimeTypes.includes(normalizedMimeType) || allowedExtensions.includes(normalizedExtension);
}

function createErrorResponse<TPayload>(
  command: UnityBridgeImportRequest["command"] | UnityBridgeExportRequest["command"],
  errorCode: UnityBridgeErrorCode,
  message: string
): UnityBridgeResponse<TPayload> {
  return {
    command,
    outcome: "Error",
    errorCode,
    message,
    payload: null
  };
}

function createCommandBlockedResponse<TPayload>(
  command: UnityBridgeCommand
): UnityBridgeResponse<TPayload> {
  logBridge("command blocked", { command });
  return createErrorResponse(
    command,
    "BridgeUnavailable",
    `The active Unity target is not allowed to use ${command}.`
  );
}

function createCancelledResponse<TPayload>(
  command: UnityBridgeImportRequest["command"] | UnityBridgeExportRequest["command"],
  message: string
): UnityBridgeResponse<TPayload> {
  return {
    command,
    outcome: "Cancelled",
    errorCode: "None",
    message,
    payload: null
  };
}

function createSuccessResponse<TPayload>(
  command: UnityBridgeImportRequest["command"] | UnityBridgeExportRequest["command"],
  payload: TPayload,
  message: string
): UnityBridgeResponse<TPayload> {
  return {
    command,
    outcome: "Success",
    errorCode: "None",
    message,
    payload
  };
}

function readFileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = typeof reader.result === "string" ? reader.result : "";
      const commaIndex = result.indexOf(",");
      if (commaIndex < 0) {
        reject(new Error("File reader did not produce a base64 data URL."));
        return;
      }

      resolve(result.slice(commaIndex + 1));
    };
    reader.onerror = () => reject(new Error("File reader failed."));
    reader.readAsDataURL(file);
  });
}

function requestFileSelection(policy: UnityBridgeFileImportPolicy): Promise<File | null> {
  return new Promise((resolve) => {
    logBridge("opening file picker", {
      accept: buildAcceptAttribute(policy),
      userActivation: navigator.userActivation?.isActive ?? null
    });

    const input = document.createElement("input");
    input.type = "file";
    input.accept = buildAcceptAttribute(policy);
    input.style.position = "fixed";
    input.style.left = "-9999px";
    input.style.top = "0";
    document.body.appendChild(input);

    let settled = false;

    const cleanup = () => {
      window.removeEventListener("focus", handleWindowFocus);
      window.setTimeout(() => {
        input.remove();
      }, 0);
    };

    const resolveOnce = (file: File | null) => {
      if (settled) {
        return;
      }

      settled = true;
      cleanup();
      resolve(file);
    };

    const handleWindowFocus = () => {
      window.setTimeout(() => {
        if (settled) {
          return;
        }

        const file = input.files?.[0] ?? null;
        logBridge("file picker focus return", file ? {
          fileName: file.name,
          mimeType: file.type,
          sizeBytes: file.size
        } : { cancelled: true });
        resolveOnce(file);
      }, 0);
    };

    input.addEventListener(
      "change",
      () => {
        const file = input.files?.[0] ?? null;
        logBridge("file picker resolved", file ? {
          fileName: file.name,
          mimeType: file.type,
          sizeBytes: file.size
        } : { cancelled: true });
        resolveOnce(file);
      },
      { once: true }
    );

    input.addEventListener(
      "cancel",
      () => {
        logBridge("file picker cancelled");
        resolveOnce(null);
      },
      { once: true }
    );

    window.addEventListener("focus", handleWindowFocus, { once: false });
    input.click();
  });
}

async function handleImportRequest(
  request: UnityBridgeImportRequest
): Promise<UnityBridgeResponse<UnityBridgeImportedFileData>> {
  logBridge("handling import request", {
    command: request.command,
    importPolicy: request.importPolicy,
    userActivation: navigator.userActivation?.isActive ?? null
  });
  const selectedFile = await requestFileSelection(request.importPolicy);

  if (!selectedFile) {
    return createCancelledResponse(request.command, "The file picker was cancelled.");
  }

  if (!fileMatchesPolicy(selectedFile, request.importPolicy)) {
    return createErrorResponse(
      request.command,
      "UnsupportedType",
      `The selected file type is not allowed for ${request.command}.`
    );
  }

  if (selectedFile.size > request.importPolicy.maxSizeBytes) {
    return createErrorResponse(
      request.command,
      "FileTooLarge",
      `The selected file exceeds the ${request.importPolicy.maxSizeBytes} byte limit.`
    );
  }

  try {
    const payloadBase64 = await readFileAsBase64(selectedFile);
    logBridge("import file read successfully", {
      command: request.command,
      fileName: selectedFile.name
    });
    return createSuccessResponse(
      request.command,
      {
        fileName: selectedFile.name,
        mimeType: selectedFile.type,
        sizeBytes: selectedFile.size,
        payloadBase64
      },
      `Selected ${selectedFile.name}.`
    );
  } catch {
    return createErrorResponse(request.command, "ReadFailure", "The selected file could not be read.");
  }
}

function decodeBase64Payload(payloadBase64: string): Uint8Array | null {
  try {
    const decoded = window.atob(payloadBase64);
    const bytes = new Uint8Array(decoded.length);
    for (let index = 0; index < decoded.length; index += 1) {
      bytes[index] = decoded.charCodeAt(index);
    }

    return bytes;
  } catch {
    return null;
  }
}

async function handleExportRequest(
  request: UnityBridgeExportRequest
): Promise<UnityBridgeResponse<UnityBridgeExportFileData>> {
  const payload = request.exportPayload;
  const bytes = decodeBase64Payload(payload.payloadBase64);

  if (!bytes) {
    return createErrorResponse(request.command, "ReadFailure", "The export payload could not be decoded.");
  }

  const blob = new Blob([bytes.buffer as ArrayBuffer], {
    type: payload.mimeType || "application/octet-stream"
  });
  logBridge("handling export request", {
    command: request.command,
    fileName: payload.fileName,
    mimeType: payload.mimeType,
    sizeBytes: payload.sizeBytes,
    userActivation: navigator.userActivation?.isActive ?? null
  });
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = objectUrl;
  anchor.download = payload.fileName || "sandbox-export.json";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(objectUrl);

  return createSuccessResponse(request.command, payload, `Downloaded ${anchor.download}.`);
}

export function createUnityBrowserBridge(): UnityBrowserBridgeApi {
  let allowedCommands = new Set<UnityBridgeCommand>();

  return {
    isBridgeAvailable: true,
    setAllowedCommands(commands) {
      allowedCommands = new Set(commands);
      logBridge("allowed commands updated", Array.from(allowedCommands));
    },
    async executeImportRequest(request) {
      logBridge("executeImportRequest called", {
        command: request.command,
        allowedCommands: Array.from(allowedCommands)
      });
      if (!allowedCommands.has(request.command)) {
        return createCommandBlockedResponse(request.command);
      }

      return handleImportRequest(request);
    },
    async executeExportRequest(request) {
      logBridge("executeExportRequest called", {
        command: request.command,
        allowedCommands: Array.from(allowedCommands)
      });
      if (!allowedCommands.has(request.command)) {
        return createCommandBlockedResponse(request.command);
      }

      return handleExportRequest(request);
    }
  };
}

export function installUnityBrowserBridge(targetWindow: Window = window): UnityBrowserBridgeApi {
  const bridge = createUnityBrowserBridge();
  targetWindow.EvacLogixSandboxBridge = bridge;
  logBridge("bridge installed");
  return bridge;
}
