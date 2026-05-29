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
  }
});
