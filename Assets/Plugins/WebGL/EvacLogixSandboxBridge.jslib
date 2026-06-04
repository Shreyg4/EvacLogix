mergeInto(LibraryManager.library, {
  EvacLogixSandboxBridge_IsAvailable: function () {
    return typeof window !== "undefined" && !!window.EvacLogixSandboxBridge ? 1 : 0;
  },

  EvacLogixSandboxBridge_RequestImport: function (gameObjectNamePtr, requestJsonPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var requestJson = UTF8ToString(requestJsonPtr);
    console.log("[EvacLogixSandboxBridge] import request received", gameObjectName, requestJson);

    if (typeof window === "undefined" || !window.EvacLogixSandboxBridge) {
      console.warn("[EvacLogixSandboxBridge] import request failed because the browser bridge is missing.");
      SendMessage(gameObjectName, "HandleImportResponse", JSON.stringify({
        command: 1,
        outcome: 2,
        errorCode: 7,
        message: "No active browser bridge is registered on the page.",
        payload: null
      }));
      return;
    }

    var commandNames = ["ImportBlueprintImage", "ImportProjectJson", "ExportProjectJson"];
    var outcomeNames = ["Success", "Cancelled", "Error"];
    var errorCodeNames = [
      "None",
      "UnsupportedType",
      "FileTooLarge",
      "ReadFailure",
      "ParseFailure",
      "MigrationFailure",
      "ValidationFailure",
      "BridgeUnavailable",
      "UnexpectedInternalError"
    ];
    var rawRequest = JSON.parse(requestJson);
    var request = Object.assign({}, rawRequest || {});
    if (typeof request.command === "number") {
      request.command = commandNames[request.command] || request.command;
    }
    window.EvacLogixSandboxBridge.executeImportRequest(request)
      .then(function (response) {
        var commandIndex = rawRequest.command;
        if (response && typeof response.command === "string") {
          var resolvedCommandIndex = commandNames.indexOf(response.command);
          if (resolvedCommandIndex >= 0) {
            commandIndex = resolvedCommandIndex;
          }
        } else if (response && typeof response.command === "number") {
          commandIndex = response.command;
        }

        var outcomeIndex = 2;
        if (response && typeof response.outcome === "string") {
          var resolvedOutcomeIndex = outcomeNames.indexOf(response.outcome);
          if (resolvedOutcomeIndex >= 0) {
            outcomeIndex = resolvedOutcomeIndex;
          }
        } else if (response && typeof response.outcome === "number") {
          outcomeIndex = response.outcome;
        }

        var errorCodeIndex = 0;
        if (response && typeof response.errorCode === "string") {
          var resolvedErrorCodeIndex = errorCodeNames.indexOf(response.errorCode);
          if (resolvedErrorCodeIndex >= 0) {
            errorCodeIndex = resolvedErrorCodeIndex;
          }
        } else if (response && typeof response.errorCode === "number") {
          errorCodeIndex = response.errorCode;
        }

        var unityResponse = {
          command: commandIndex,
          outcome: outcomeIndex,
          errorCode: errorCodeIndex,
          message: response && response.message ? response.message : "",
          payload: response && response.payload ? response.payload : null
        };
        console.log("[EvacLogixSandboxBridge] import response", response, unityResponse);
        SendMessage(gameObjectName, "HandleImportResponse", JSON.stringify(unityResponse));
      })
      .catch(function (error) {
        console.error("[EvacLogixSandboxBridge] import request threw", error);
        SendMessage(gameObjectName, "HandleImportResponse", JSON.stringify({
          command: rawRequest.command,
          outcome: 2,
          errorCode: 8,
          message: error && error.message ? error.message : "Browser import request failed.",
          payload: null
        }));
      });
  },

  EvacLogixSandboxBridge_RequestExport: function (gameObjectNamePtr, requestJsonPtr) {
    var gameObjectName = UTF8ToString(gameObjectNamePtr);
    var requestJson = UTF8ToString(requestJsonPtr);
    console.log("[EvacLogixSandboxBridge] export request received", gameObjectName, requestJson);

    if (typeof window === "undefined" || !window.EvacLogixSandboxBridge) {
      console.warn("[EvacLogixSandboxBridge] export request failed because the browser bridge is missing.");
      SendMessage(gameObjectName, "HandleExportResponse", JSON.stringify({
        command: 2,
        outcome: 2,
        errorCode: 7,
        message: "No active browser bridge is registered on the page.",
        payload: null
      }));
      return;
    }

    var commandNames = ["ImportBlueprintImage", "ImportProjectJson", "ExportProjectJson"];
    var outcomeNames = ["Success", "Cancelled", "Error"];
    var errorCodeNames = [
      "None",
      "UnsupportedType",
      "FileTooLarge",
      "ReadFailure",
      "ParseFailure",
      "MigrationFailure",
      "ValidationFailure",
      "BridgeUnavailable",
      "UnexpectedInternalError"
    ];
    var rawRequest = JSON.parse(requestJson);
    var request = Object.assign({}, rawRequest || {});
    if (typeof request.command === "number") {
      request.command = commandNames[request.command] || request.command;
    }
    window.EvacLogixSandboxBridge.executeExportRequest(request)
      .then(function (response) {
        var commandIndex = rawRequest.command;
        if (response && typeof response.command === "string") {
          var resolvedCommandIndex = commandNames.indexOf(response.command);
          if (resolvedCommandIndex >= 0) {
            commandIndex = resolvedCommandIndex;
          }
        } else if (response && typeof response.command === "number") {
          commandIndex = response.command;
        }

        var outcomeIndex = 2;
        if (response && typeof response.outcome === "string") {
          var resolvedOutcomeIndex = outcomeNames.indexOf(response.outcome);
          if (resolvedOutcomeIndex >= 0) {
            outcomeIndex = resolvedOutcomeIndex;
          }
        } else if (response && typeof response.outcome === "number") {
          outcomeIndex = response.outcome;
        }

        var errorCodeIndex = 0;
        if (response && typeof response.errorCode === "string") {
          var resolvedErrorCodeIndex = errorCodeNames.indexOf(response.errorCode);
          if (resolvedErrorCodeIndex >= 0) {
            errorCodeIndex = resolvedErrorCodeIndex;
          }
        } else if (response && typeof response.errorCode === "number") {
          errorCodeIndex = response.errorCode;
        }

        var unityResponse = {
          command: commandIndex,
          outcome: outcomeIndex,
          errorCode: errorCodeIndex,
          message: response && response.message ? response.message : "",
          payload: response && response.payload ? response.payload : null
        };
        console.log("[EvacLogixSandboxBridge] export response", response, unityResponse);
        SendMessage(gameObjectName, "HandleExportResponse", JSON.stringify(unityResponse));
      })
      .catch(function (error) {
        console.error("[EvacLogixSandboxBridge] export request threw", error);
        SendMessage(gameObjectName, "HandleExportResponse", JSON.stringify({
          command: rawRequest.command,
          outcome: 2,
          errorCode: 8,
          message: error && error.message ? error.message : "Browser export request failed.",
          payload: null
        }));
      });
  },

  // Current wasm heap size in bytes (the value the OOM abort watches). Returned as a double because it
  // can exceed Int32 (the cap is 2 GB). Used by the memory guard to relieve pressure before the abort.
  EvacLogixSandboxBridge_GetHeapBytes: function () {
    try {
      if (typeof HEAP8 !== "undefined" && HEAP8 && HEAP8.buffer) {
        return HEAP8.buffer.byteLength;
      }
      if (typeof Module !== "undefined" && Module.HEAP8 && Module.HEAP8.buffer) {
        return Module.HEAP8.buffer.byteLength;
      }
    } catch (e) {}
    return 0;
  },

  // Persists the recovery snapshot to localStorage. Falls back to a file download if storage is full or
  // unavailable, so work is never lost. Returns 1 = stored, 2 = downloaded fallback, 0 = failed.
  EvacLogixSandboxBridge_WriteRecovery: function (keyPtr, jsonPtr) {
    var key = UTF8ToString(keyPtr);
    var json = UTF8ToString(jsonPtr);
    try {
      window.localStorage.setItem(key, json);
      return 1;
    } catch (e) {
      try {
        var blob = new Blob([json], { type: "application/json" });
        var url = URL.createObjectURL(blob);
        var a = document.createElement("a");
        a.href = url;
        a.download = "evaclogix-recovery.json";
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        return 2;
      } catch (e2) {
        return 0;
      }
    }
  },

  // Reads a recovery snapshot back out of localStorage. The returned buffer is allocated with _malloc;
  // it is read once on load so the small one-time leak is acceptable.
  EvacLogixSandboxBridge_ReadRecovery: function (keyPtr) {
    var key = UTF8ToString(keyPtr);
    var value = "";
    try { value = window.localStorage.getItem(key) || ""; } catch (e) { value = ""; }
    var size = lengthBytesUTF8(value) + 1;
    var buffer = _malloc(size);
    stringToUTF8(value, buffer, size);
    return buffer;
  },

  EvacLogixSandboxBridge_ClearRecovery: function (keyPtr) {
    var key = UTF8ToString(keyPtr);
    try { window.localStorage.removeItem(key); } catch (e) {}
  },

  // Always triggers a browser download of the given JSON (used by the "Download backup now" action).
  EvacLogixSandboxBridge_DownloadJson: function (fileNamePtr, jsonPtr) {
    var fileName = UTF8ToString(fileNamePtr);
    var json = UTF8ToString(jsonPtr);
    try {
      var blob = new Blob([json], { type: "application/json" });
      var url = URL.createObjectURL(blob);
      var a = document.createElement("a");
      a.href = url;
      a.download = fileName || "evaclogix-recovery.json";
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch (e) {}
  },

  // Installs an onAbort hook so that if the wasm heap aborts (OOM) anyway, the last recovery snapshot is
  // downloaded and a banner is shown — the safety net for crashes the proactive guard can't prevent.
  EvacLogixSandboxBridge_InstallAbortHandler: function (keyPtr) {
    var key = UTF8ToString(keyPtr);
    try {
      if (typeof Module === "undefined" || Module.__evacLogixAbortHooked) {
        return;
      }
      Module.__evacLogixAbortHooked = true;
      var previousOnAbort = Module.onAbort;
      Module.onAbort = function (what) {
        try {
          var json = window.localStorage.getItem(key) || "";
          if (json) {
            var blob = new Blob([json], { type: "application/json" });
            var url = URL.createObjectURL(blob);
            var a = document.createElement("a");
            a.href = url;
            a.download = "evaclogix-recovery.json";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
          }
          var banner = document.createElement("div");
          banner.style.cssText = "position:fixed;top:0;left:0;right:0;z-index:99999;background:#7a1c1c;color:#fff;font:14px sans-serif;padding:12px;text-align:center;";
          banner.textContent = "EvacLogix ran out of memory. Your latest work was downloaded as evaclogix-recovery.json — reload the page and use Recover to restore it.";
          document.body.appendChild(banner);
        } catch (e) {}
        if (typeof previousOnAbort === "function") {
          try { previousOnAbort(what); } catch (e) {}
        }
      };
    } catch (e) {}
  }
});
