export type UnityBridgeCommand = "ImportBlueprintImage" | "ImportProjectJson" | "ExportProjectJson"

export type UnityBridgeOutcome = "Success" | "Cancelled" | "Error"

export type UnityBridgeErrorCode =
  | "None"
  | "UnsupportedType"
  | "FileTooLarge"
  | "ReadFailure"
  | "ParseFailure"
  | "MigrationFailure"
  | "ValidationFailure"
  | "BridgeUnavailable"
  | "UnexpectedInternalError"

export type UnityBridgeFileImportPolicy = {
  allowedMimeTypes: string[]
  allowedExtensions: string[]
  maxSizeBytes: number
}

export type UnityBridgeImportedFileData = {
  fileName: string
  mimeType: string
  sizeBytes: number
  payloadBase64: string
}

export type UnityBridgeExportFileData = {
  fileName: string
  mimeType: string
  sizeBytes: number
  payloadBase64: string
}

export type UnityBridgeImportRequest = {
  command: "ImportBlueprintImage" | "ImportProjectJson"
  importPolicy: UnityBridgeFileImportPolicy
  exportPayload?: never
}

export type UnityBridgeExportRequest = {
  command: "ExportProjectJson"
  exportPayload: UnityBridgeExportFileData
  importPolicy?: never
}

export type UnityBridgeRequest = UnityBridgeImportRequest | UnityBridgeExportRequest

export type UnityBridgeResponse<TPayload> = {
  command: UnityBridgeCommand
  outcome: UnityBridgeOutcome
  errorCode: UnityBridgeErrorCode
  message: string
  payload: TPayload | null
}

export type UnityBrowserBridgeApi = {
  isBridgeAvailable: boolean
  setAllowedCommands: (commands: UnityBridgeCommand[]) => void
  executeImportRequest: (
    request: UnityBridgeImportRequest
  ) => Promise<UnityBridgeResponse<UnityBridgeImportedFileData>>
  executeExportRequest: (
    request: UnityBridgeExportRequest
  ) => Promise<UnityBridgeResponse<UnityBridgeExportFileData>>
}

declare global {
  interface Window {
    EvacLogixSandboxBridge?: UnityBrowserBridgeApi
  }
}
