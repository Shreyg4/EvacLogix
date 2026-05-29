# Vincent Plan

Replace a phase tag from `[PLANNED]` to `[DONE]` only when the phase is fully implemented and verified.

## Overview

This plan covers the Unity-side and integration-side work required to bring `SandboxEditor` into the EvacLogix website as a full WebGL-capable experience with browser-native file workflows.

This is not a generic roadmap. It is written against the sandbox as it exists now in `Assets/Scripts/Sandbox/`.

## Current Baseline

The current sandbox already has a substantial desktop/editor-oriented persistence and transfer layer:

- `SandboxSaveLoadService`
  - owns active project save/load
  - owns autosave and recovery prompt behavior
  - reads and writes project JSON through `System.IO`
  - builds autosave paths under `Application.persistentDataPath`
- `SandboxProjectTransferService`
  - owns project JSON import/export
  - owns runtime export
  - owns floor import analysis and floor import execution
  - reads and writes files directly through `System.IO`
- `SandboxBlueprintImportService`
  - imports blueprint images by copying them into `Assets/Art/Blueprints/Sandbox`
  - currently supports `.png`, `.jpg`, and `.jpeg`
  - uses `UnityEditor.AssetDatabase` when available
- `SandboxPreviewImageExportService`
  - exports preview imagery
  - has editor-oriented behavior and asset assumptions
- `SandboxProjectWorkspaceService`
  - owns active project, floor, and blueprint assignment state
- `SandboxValidationService`
  - validates project state and gates preview/export readiness
- `SandboxColliderRebuildService`
  - rebuilds generated colliders after structural changes

Current UI entry points for file-related behavior:

- `SandboxTopBarShell`
  - save project
  - load project
  - export project JSON
  - import project JSON
  - export runtime project data
  - import floors
  - recovery restore and dismiss
- `SandboxInspectorPanelShell`
  - import blueprint to active floor
- `SandboxEditorHud`
  - currently exposes path-based buttons and text fields for several file workflows

Current WebGL blockers already visible in the codebase:

- direct `System.IO` assumptions in save/load, transfer, and import services
- arbitrary local path workflows exposed in the HUD
- editor-only `AssetDatabase` paths in blueprint and preview-related flows
- autosave and recovery semantics designed around local file persistence

## Phase List

### [DONE] Phase 1: Define The Cross-Platform File Boundary In Unity

Goal:
- stop higher-level UI from talking directly to low-level save/load/transfer/import services for core file actions

Implemented:
- added `ISandboxFileActionService`
- added `SandboxFileActionService`
- registered the new service in `SandboxEditorInstaller`
- rewired `SandboxTopBarShell` to use the new file-action boundary for:
  - save project
  - load project
  - export project JSON
  - import project JSON
  - export runtime project data
  - import floors
  - recovery restore and dismiss
- rewired `SandboxInspectorPanelShell` blueprint import to use the same boundary

Current Phase 1 boundary shape:
- `SaveProject`
- `LoadProject`
- `ExportProjectJson`
- `ImportProjectJson`
- `ExportRuntimeProjectData`
- `ImportBlueprintToActiveFloor`
- `AnalyzeFloorImport`
- `ImportFloors`
- `TryRestoreRecovery`
- `DismissRecoveryPrompt`

Phase 1 verification completed:
- Unity compile succeeded after patching the new service

Phase 1 intentionally did not do the following:
- it did not eliminate desktop-style file paths yet
- it did not add typed request/response models yet
- it did not introduce WebGL behavior yet
- it did not move preview-image export behind the file boundary yet

### [DONE] Phase 2: Inventory And Refactor Existing Unity File Workflows

Goal:
- separate product actions from platform mechanics without breaking current behavior

Concrete work:
- audit and document the exact responsibilities of:
  - `SandboxSaveLoadService`
  - `SandboxProjectTransferService`
  - `SandboxBlueprintImportService`
  - `SandboxPreviewImageExportService`
- split each workflow into explicit layers:
  - action entry point
  - file selection or file source
  - raw byte or text read/write
  - serialization or deserialization
  - schema migration
  - validation
  - workspace application
  - post-load rebuild and validation refresh
- remove duplicated “after load/import” behavior by centralizing:
  - collider rebuild
  - validation refresh
  - workspace updates
  - status/error propagation
- identify which current methods should stay public and which should become helpers

Expected output of Phase 2:
- a clearer dependency map of current file workflows
- fewer mixed-responsibility methods
- less duplication between save/load and transfer flows
- no feature loss in current desktop/editor behavior

Must preserve current behavior:
- autosave and recovery still work
- top bar file actions still work
- floor import analysis and import still work
- blueprint import still assigns the imported blueprint to the active floor
- runtime export still respects validation gates

Phase 2 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 3: Define Typed Unity-Side Contracts

Goal:
- replace ambiguous string-path-driven expectations with explicit action contracts that work for both desktop and browser backends

Concrete work:
- introduce typed action result models for file workflows
- model outcomes explicitly as:
  - `Success`
  - `Cancelled`
  - `Error`
- define typed payload contracts for:
  - blueprint image import input
  - project JSON import input
  - project JSON export output
  - runtime export output if that is kept as a browser-facing action
- include metadata in import/export payloads:
  - file name
  - mime type
  - byte size
  - payload contents
- define error categories at the Unity contract level:
  - unsupported type
  - file too large
  - read failure
  - parse failure
  - migration failure
  - validation failure
  - bridge unavailable
  - unexpected internal error

Important constraint:
- Unity remains the source of truth for sandbox schema parsing, migration, and validation
- the website should never become the primary parser for project JSON

Phase 3 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 4: Build The Desktop Or Editor Backend

Goal:
- preserve today’s workflows under the new abstraction so the sandbox still works locally while WebGL support is added

Concrete work:
- make the desktop/editor backend the formal implementation of the file-action contract
- isolate path-based file access to desktop/editor-specific code paths
- isolate any `UnityEditor` usage to editor-only branches or services
- formalize support for current local workflows:
  - save active project to path
  - load project from path
  - export project JSON to path
  - import project JSON from path
  - import floors from path
  - import blueprint image from path
  - export runtime-ready data to path
- decide whether preview image export belongs in the same backend contract or a separate export service contract

Expected output of Phase 4:
- desktop/editor behavior still works, but now it is clearly one backend implementation rather than the default assumption everywhere

Must preserve current behavior:
- `SandboxEditorHud` path-driven actions still work until they are intentionally replaced
- `SandboxTopBarShell` still delegates successfully
- `SandboxInspectorPanelShell` blueprint import still works

Phase 4 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 5: Build The WebGL Backend Seam In Unity

Goal:
- make Unity capable of asking for browser-native file operations without knowing browser details

Concrete work:
- add a WebGL-oriented backend implementation behind the same action contract
- do not use arbitrary local paths in this backend
- introduce a bridge adapter layer that Unity can call for:
  - import blueprint image
  - import project JSON
  - export project JSON
- make Unity UI code and higher-level editor logic agnostic to whether the backend is desktop or browser
- ensure missing bridge support returns typed failures instead of silent no-ops

Non-goals for this phase:
- no website-side parsing
- no multi-target site work yet
- no ad hoc JavaScript calls directly from random UI scripts

Expected output of Phase 5:
- the sandbox can be compiled with a browser-oriented backend seam even if the actual website bridge is still incomplete

Phase 5 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 6: Define The Browser Bridge Contract

Goal:
- create a minimal, reusable, typed bridge contract between the website and Unity

Concrete work:
- define a command-based bridge with explicit request and response schemas
- start with these commands only:
  - `ImportBlueprintImage`
  - `ImportProjectJson`
  - `ExportProjectJson`
- define per-command response outcomes:
  - success
  - cancelled
  - error
- define import restrictions:
  - image files only for blueprint import
  - JSON only for project import
- define an explicit image whitelist
- define separate size limits for:
  - blueprint images
  - project JSON
- specify that the website enforces file type and size before Unity receives payloads

Important constraint:
- the bridge should be generic enough for multiple future Unity app targets, but limited to one active target at runtime

Phase 6 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 7: Build The Website Bridge Layer

Goal:
- implement the browser-native side of import/export without moving sandbox business logic out of Unity

Concrete work:
- build a website-side browser file bridge service
- keep it separate from page content and visual presentation
- support:
  - file picker for blueprint image import
  - file picker for project JSON import
  - browser download for project JSON export
- return metadata plus contents back to Unity
- surface clear outcomes for:
  - success
  - cancel
  - unsupported type
  - file too large
  - read failure
- avoid parsing sandbox project semantics in the React app

Expected output of Phase 7:
- the website can satisfy the Unity bridge contract for the first browser-native file flows

Phase 7 verification completed:
- `npm test` passed
- `npm run build` passed

### [DONE] Phase 8: Extend The Web App To Support Multiple Unity Targets

Goal:
- prepare the website to host more than one Unity app without hardcoding one permanent build

Concrete work:
- add named Unity app profiles such as:
  - `sandbox-editor`
  - future simulation targets
- let each profile declare:
  - display name
  - build config location
  - fallback copy
  - allowed bridge commands
- store each Unity target in its own WebGL build folder and config
- keep `SandboxEditor` hidden from user-facing site navigation until its browser-native flows actually work

Expected output of Phase 8:
- the site can load different Unity targets intentionally rather than treating every future build as the same app

Phase 8 verification completed:
- `npm test` passed
- `npm run build` passed

### [DONE] Phase 9: Produce The First SandboxEditor Vertical Slice

Goal:
- get one real end-to-end browser-capable `SandboxEditor` slice working inside the site

Concrete work:
- export `SandboxEditor` as its own WebGL build
- place it in a dedicated website target folder
- create the site profile/config for that target
- verify the embed loads the correct target
- verify browser-native flows for:
  - blueprint image import
  - project JSON import
  - project JSON export
- verify Unity still owns:
  - project parsing
  - migration
  - validation
  - workspace application

Expected output of Phase 9:
- `SandboxEditor` launches inside the site and completes the first browser-native file workflows successfully

Phase 9 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [DONE] Phase 10: Resolve WebGL-Specific Runtime Issues

Goal:
- clean up the remaining runtime assumptions that will break or behave poorly in WebGL

Concrete work:
- audit remaining `System.IO` usage across the sandbox
- remove or isolate remaining editor-only assumptions from runtime paths
- audit `AssetDatabase`-dependent logic in blueprint and preview paths
- decide how browser autosave and recovery should work
- validate Input System behavior in WebGL
- validate memory behavior for large blueprint images
- validate export and persistence behavior under browser constraints

Known likely hotspots:
- `SandboxSaveLoadService`
- `SandboxProjectTransferService`
- `SandboxBlueprintImportService`
- `SandboxPreviewImageExportService`
- rendering or preview code that assumes asset-database-backed textures

Phase 10 verification completed:
- Unity compile succeeded
- sandbox run-time behavior was verified by the user

### [PLANNED] Phase 11: Testing, Validation, And Release Readiness

Goal:
- verify that the refactor preserved the existing sandbox and that the browser path is reliable enough to present

Unity-side testing:
- verify the new file-action boundary remains wired and available
- verify desktop/editor backend behavior for:
  - save project
  - load project
  - import project JSON
  - export project JSON
  - import blueprint image
  - import floors
  - recovery restore
- verify rebuild and validation refresh still happen after relevant imports and loads
- verify validation-gated runtime export still behaves correctly

Website-side testing:
- verify bridge command validation
- verify file type restrictions
- verify image and JSON size-limit enforcement
- verify success, cancel, and error result handling

End-to-end testing:
- verify embedded `SandboxEditor` launch
- verify blueprint import from browser picker
- verify project JSON import from browser picker
- verify project JSON export to browser download
- verify graceful failure when bridge support or target assets are unavailable

Manual QA:
- blueprint import still assigns to the active floor
- save/load still preserve project state
- floor import still behaves correctly
- preview/export flows that remain in scope still work
- browser flow failures are understandable and non-destructive

## Definition Of Done

- [PLANNED] `SandboxEditor` launches as a WebGL target inside the EvacLogix site.
- [DONE] Unity file actions now route through one shared high-level boundary for the implemented Phase 1 flows.
- [PLANNED] The remaining desktop/editor file workflows are preserved under the refactored architecture.
- [PLANNED] Browser-native import and export work for blueprint images and project JSON.
- [DONE] The website bridge is typed, limited, and target-aware.
- [DONE] Unity remains the owner of sandbox parsing, migration, validation, and workspace application.
- [DONE] The embedded experience fails gracefully when assets, bridge support, or allowed capabilities are unavailable.
