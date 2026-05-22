# EvacLogix Sandbox Editor Implementation Plan

Project: EvacLogix  
Scope: Unity-based public sandbox editor for authoring evacuation-ready building maps  
Primary reference: `Assets/Info/editorProposalSandbox.md`

Status note:
All implementation phases should be marked with `[PLANNED]` until completed. Replace the phase prefix with `[DONE]` when a phase is fully complete and verified.

## Purpose

This plan converts the sandbox editor proposal into an implementation roadmap with explicit build steps, deliverables, and exit criteria. It is written against the current project reality: one light Unity scaffold, one sample scene, and no existing sandbox architecture.

This version is intentionally stricter than the earlier draft:

- every proposal feature is mapped into the plan or explicitly deferred
- each phase includes concrete implementation tasks rather than broad goals
- ambiguous phrases like "support X" are replaced with actions such as create, store, validate, expose, block, or test

## Non-Negotiable Constraints

- The sandbox is a public-facing product feature, not an internal-only utility.
- Imported blueprints are reference overlays only.
- Editable building data is the source of truth.
- Saved projects must preserve wall semantics, openings, stairs, exits, obstacles, floors, metadata, and scenarios.
- Users may continue editing with invalid data, but simulation/export must be blocked by blocking validation errors.
- The first public version is single-user-first.
- External portability must exist through structured export/import.
- Raw schema editing stays out of the public UI in v1.

## Original Unity Starting Point

Before Phase 0, the Unity side of the repo had:

- `Assets/Scenes/SampleScene.unity`
- `Assets/Scripts/SampleScript.cs`
- no sandbox-specific data model
- no sandbox-specific editor shell
- no sandbox-specific save/load flow

Because of that baseline, Phase 0 and Phase 1 are mandatory foundation phases, not optional cleanup.

## Recommended Folder and Scene Structure

```text
Assets/
  Info/
    editorProposalSandbox.md
    sandboxplan.md
  Scenes/
    Bootstrap.unity
    SandboxEditor.unity
    SandboxPreview.unity
  Prefabs/
    Sandbox/
      UI/
      Authoring/
      Handles/
      Overlays/
  ScriptableObjects/
    Sandbox/
      Defaults/
      Themes/
  Scripts/
    Sandbox/
      Core/
      Data/
      Data/Serialization/
      Data/Migrations/
      Data/Validation/
      Authoring/
      Authoring/Commands/
      Authoring/Selection/
      Authoring/Tools/
      Authoring/Snapping/
      Runtime/
      Runtime/Preview/
      Runtime/Preview/Diagnostics/
      UI/
      UI/Panels/
      UI/Overlays/
      UI/Shortcuts/
      Rendering/
      Infrastructure/
      Debug/
```

## Recommended Scene Responsibilities

- `Assets/Scenes/Bootstrap.unity`
  Responsibility: initialize app-level services, load defaults, route into editor, and later support recovery prompts.
- `Assets/Scenes/SandboxEditor.unity`
  Responsibility: authoring, import, calibration, editing, validation, persistence, lifecycle state display.
- `Assets/Scenes/SandboxPreview.unity`
  Responsibility: isolated preview mode, scenario setup, fire placement, spawn authoring, diagnostics, and return-to-editor.

If preview must initially stay inside the editor scene, keep the code modular enough that it can be moved into `SandboxPreview.unity` later without rewriting the preview systems.

## Canonical V1 Feature Inventory

The first public sandbox must explicitly include:

- new project creation with a default floor and a blank-template option
- building-level project save containing all floors and authored data
- blueprint import for `PNG` and `JPG`
- blueprint asset copy into Unity-managed project storage
- blueprint opacity control
- two-point scale calibration
- one aligned building coordinate system across floors
- floor tabs with add, rename, reorder, duplicate, and delete
- line wall tool
- brush wall tool with tunable simplification
- wall centerlines with editable thickness
- wall junctions as editable shared connection points
- wall split, merge, trim, erase, and conservative near-join cleanup
- snapping to grid, wall endpoints, wall segments, and aligned angles
- automatic collider rebuild after edits plus `Rebuild All`
- wall-attached doors with states
- wall-attached windows with escape metadata
- exit zones with width, orientation, and optional capacity or priority
- obstacles with a limited semantic type set
- stair portals with paired links, direction restrictions, and travel cost
- selection, multi-select, handles, and inspector editing
- undo and redo for all core authoring actions
- hide, lock, focus, and isolation controls
- per-type visual differentiation and legend
- object naming where proposal requires it
- measurement tool and visual readouts
- grid visibility and snap increment control
- keyboard shortcuts for core commands
- minimap or overview navigation aid
- numeric property editing
- per-floor metadata
- fixed built-in validation rules
- duplicate and conflict detection
- lifecycle states
- manual save plus recovery autosaves
- JSON import/export, round-tripping, schema versioning, and partial floor import
- preview mode with fire-only hazard setup
- explicit spawn authoring with point placement and density brush
- persistent and temporary spawn layouts
- lightweight scenario presets
- preview diagnostics, route previews, unreachable-area reporting, blocked-exit reporting, and broken-stair reporting
- optional debug overlays

## Explicit V1 Deferrals

The following items are intentionally out of v1 and must not be silently added into the plan scope:

- real-time collaboration
- public raw-data editing
- full room or hallway semantic modeling
- full template or reusable prefab library
- user-managed style preset library
- advanced hazard families beyond fire
- detailed window breakability simulation
- CAD-style dedicated offset or parallel-wall tool
- comment or review-annotation system

## Proposal Coverage Checklist

This section exists so we can verify that no proposal feature was dropped.

### Core product and data model coverage

- Proposal `1`, `2`, `10`, `19`, `52`, `53`, `54`: covered by Phase 1 and Phase 9
- Proposal `3`, `16`: covered by Phase 3 and Phase 9
- Proposal `11`, `18`, `50`: covered by Phase 7
- Proposal `22`: covered by Phase 9

### Authoring geometry and editing coverage

- Proposal `4`, `45`, `46`: covered by Phase 3 and Phase 8
- Proposal `5`, `12`, `57`, `58`, `59`, `61`, `62`: covered by Phase 4
- Proposal `6`, `35`, `37`, `38`, `39`, `44`: covered by Phase 6
- Proposal `14`, `15`, `24`, `25`, `43`, `47`, `49`, `65`: covered by Phase 2 and Phase 8
- Proposal `26`, `27`, `56`: covered by Phase 7 and Phase 11

### Validation, save, and portability coverage

- Proposal `7`, `9`, `17`, `51`, `55`, `63`, `64`: covered by Phase 5 and Phase 9

### Preview and scenario coverage

- Proposal `20`, `21`, `23`, `28`, `29`, `30`, `31`, `32`, `33`, `34`, `40`, `41`, `48`, `60`: covered by Phase 3, Phase 8, and Phase 10

### Explicit non-v1 coverage

- Proposal `42`: explicitly deferred in `Explicit V1 Deferrals`
- Proposal unresolved note on pinned callouts or traced checkpoints: not committed to v1 and should be revisited after Phase 8 usability testing

## System Boundaries

### 1. Data Layer

Responsible for:

- persistent building schema
- floor, wall, junction, opening, exit, obstacle, stair, region, spawn, scenario, and metadata records
- schema versioning and migration

Must not contain:

- primary truth stored only in scene objects
- UI-only transient panel state

### 2. Authoring Layer

Responsible for:

- tool execution
- snapping
- selection
- direct handle editing
- undoable edit commands

Must not contain:

- file format knowledge beyond data contracts
- preview simulation logic

### 3. Validation and Rebuild Layer

Responsible for:

- collider rebuilds
- structural checks
- duplicate/conflict checks
- preview/export readiness gating

Must not contain:

- direct mutation from UI widgets without going through commands or services

### 4. UI Layer

Responsible for:

- top bar
- floor tabs
- inspector
- validation panel
- lifecycle state badges
- onboarding overlays
- preview setup panels

Must not contain:

- hidden geometry ownership
- persistence logic embedded in click handlers

### 5. Preview Layer

Responsible for:

- fire setup
- spawn setup
- scenario preset selection
- route checks
- congestion and reachability diagnostics

Must not contain:

- free-form editing while preview mode is active

## Phase Plan

### [DONE] Phase 0: Bootstrap the Sandbox Workspace

Objective:
Replace the sample-scene scaffold with a dedicated sandbox workspace.

Implementation steps:

1. Create `Assets/Scenes/Bootstrap.unity`, `Assets/Scenes/SandboxEditor.unity`, and `Assets/Scenes/SandboxPreview.unity`.
2. Add all three scenes to `ProjectSettings/EditorBuildSettings.asset`, with `Bootstrap.unity` first.
3. Create the `Assets/Scripts/Sandbox/` folder structure shown in this plan.
4. Replace `Assets/Scripts/SampleScript.cs` with initial sandbox bootstrap scripts or delete it after equivalent bootstrap scripts exist.
5. In `SandboxEditor.unity`, create root GameObjects named `Systems`, `World`, `UI`, `OverlayRoot`, and `DebugRoot`.
6. Under `World`, create `BlueprintRoot`, `GridRoot`, `FloorRoot`, and `RuntimeOverlayRoot`.
7. Under `UI`, create `TopBar`, `LeftToolPanel`, `RightInspectorPanel`, `BottomStatusBar`, `FloorTabsBar`, `ValidationPanelRoot`, and `ModalRoot`.
8. Create a single `SandboxApp` bootstrap component that wires shared services at scene start.

Exit criteria:

- the project launches into a sandbox-owned scene
- scene roots exist with stable names
- the editor scene can enter play mode without missing-reference errors

### [DONE] Phase 1: Define Persistent Building Schema

Objective:
Create the authoritative saved-data model before tool logic is built.

Implementation steps:

1. Create persistent data classes for:
   `BuildingProjectData`, `FloorData`, `BlueprintReferenceData`, `WallSegmentData`, `WallJunctionData`, `DoorData`, `WindowData`, `ExitZoneData`, `ObstacleData`, `StairPortalData`, `RegionData`, `SpawnLayoutData`, `ScenarioPresetData`, `ValidationIssueData`, and `ProjectMetadataData`.
2. Add `schemaVersion` to the root project data object.
3. Add stable UUID-style IDs for every saved entity that may be referenced by another entity.
4. Define explicit references for:
   wall-attached objects to wall segments, stair endpoints to paired stair endpoints, regions to floors, and scenarios to saved spawn/fire data.
5. Store per-floor metadata: name, order, elevation, and floor-level modifiers if required.
6. Store building lifecycle state as derived status data, not manual user text.
7. Store building-level metadata separately from floor-level metadata.
8. Add migration entry points even if only schema version `1` exists at first.
9. Write unit tests that serialize, deserialize, and compare a sample two-floor building with walls, doors, windows, stairs, exits, obstacles, and scenario data.

Exit criteria:

- a multi-floor building project can be serialized and deserialized losslessly
- every cross-reference resolves through stable IDs
- migration hooks exist and are test-covered

### [DONE] Phase 2: Build the Editor Shell and Command Framework

Objective:
Stand up the editor shell and the mutation model all later features depend on.

Implementation steps:

1. Create a central tool mode enum with at least:
   `Select`, `Pan`, `Measure`, `WallLine`, `WallBrush`, `Erase`, `Door`, `Window`, `Exit`, `Obstacle`, `Stair`, `SpawnPoint`, `SpawnBrush`, and `Region`.
2. Create a command system for undoable edits with `Execute`, `Undo`, and `Redo`.
3. Route all mutating authoring actions through commands instead of direct component edits.
4. Create a selection service supporting single-select and multi-select.
5. Create an input router that resolves whether pointer input is targeting UI, world canvas, handles, or preview overlays.
6. Create the top bar, tool palette, floor tabs bar shell, inspector shell, validation panel shell, and status bar shell.
7. Implement camera pan, zoom, and reset view controls.
8. Add overview navigation scaffolding for a future minimap widget.
9. Add a keyboard-shortcut registry for select, delete, undo, redo, copy, paste, duplicate, tool switching, grid toggle, and snap toggle.

Exit criteria:

- tools can be switched from UI and shortcuts
- selection and multi-selection are stable
- all edit actions can be added later without bypassing the command stack

### [DONE] Phase 3: Implement New Project Flow, Blueprint Import, and Calibration

Objective:
Make the first real user workflow possible: create project, import plan, calibrate, start tracing.

Implementation steps:

1. Create a new-project dialog with two explicit choices:
   default template with one floor, or completely blank project.
2. On default project creation, create one floor tab, empty building metadata, and onboarding text describing next steps.
3. Implement import for `PNG` and `JPG` blueprint files.
4. Copy imported blueprint images into a Unity-managed sandbox asset location inside the project.
5. Store imported blueprint references in project data, not external absolute paths.
6. Render blueprint overlays under `BlueprintRoot`.
7. Add per-floor blueprint assignment.
8. Add blueprint opacity slider and visibility toggle.
9. Implement two-point scale calibration:
   click point A, click point B, input real-world distance, compute world scale, persist calibration values.
10. Display measurement feedback immediately after calibration.
11. Add onboarding hints for import, calibration, tracing, and stair linking.
12. Add a secondary export action for preview images as a stretch deliverable after base project save works.

Exit criteria:

- a user can start a project, import a floor blueprint, set opacity, calibrate it, save, reload, and see the same calibrated overlay
- imported blueprint data remains portable inside the Unity project

### [PLANNED] Phase 4: Build Wall Authoring and Cleanup Tools

Objective:
Implement the editable wall-authoring backbone as semantic centerline geometry.

Implementation steps:

1. Implement the line wall tool to place straight wall centerline segments.
2. Implement the brush wall tool to record pointer strokes as temporary polylines.
3. Add brush cleanup settings for smoothing and point reduction.
4. Convert accepted brush strokes into connected wall segments.
5. Store walls as centerlines plus thickness values, not filled polygons.
6. Create explicit wall junction records where segments meet.
7. Allow direct handle editing of wall endpoints.
8. Allow numeric editing of wall thickness and endpoint coordinates from the inspector.
9. Implement split wall, merge wall, trim wall, and erase wall actions.
10. Add conservative near-corner snapping and optional near-join cleanup thresholds.
11. Add snapping targets for grid, wall endpoints, wall segments, and aligned angles.
12. Do not implement a dedicated parallel-wall or offset-wall tool in this phase.

Exit criteria:

- users can trace a realistic room layout with line and brush tools
- generated wall data stays editable after creation
- cleanup actions do not destroy neighboring topology unexpectedly

### [PLANNED] Phase 5: Rebuild Colliders and Enforce Structural Validation

Objective:
Turn wall geometry into reliable simulation structure and surface errors clearly.

Implementation steps:

1. Generate deterministic colliders from the current wall network after each accepted edit.
2. Rebuild colliders incrementally after local edits where possible.
3. Add a visible `Rebuild All` button that forces a full rebuild.
4. Build fixed validation rules for:
   disconnected wall structures, invalid openings, invalid stair links, invalid exits, invalid obstacle overlaps, duplicate stair endpoints, overlapping exits, and conflicting obstacles.
5. Mark validation issues with severity levels: warning or blocking error.
6. Add project-level duplicate and conflict detection.
7. Show validation results in a panel grouped by floor and object.
8. Highlight invalid objects in the scene view.
9. Block preview and export actions only when blocking issues are present.
10. Keep editing enabled even when validation issues exist.

Exit criteria:

- colliders update after edits
- invalid data is visible in both panel and scene
- preview/export readiness is enforced by blocking validation results

### [PLANNED] Phase 6: Add Semantic Objects and Inspector Editing

Objective:
Add the authoring objects that give the building behavioral meaning.

Implementation steps:

1. Implement door placement only on existing wall segments.
2. Store door width, wall position, tags, and state metadata.
3. Implement v1 door states: `Normal`, `Blocked`, `Locked`, and optional `OneWay`.
4. Implement window placement only on existing wall segments.
5. Store window width, wall position, tags, escape eligibility, escape cost, and floor-dependent risk metadata.
6. Implement exits as zones rather than points.
7. Store exit width, orientation, optional capacity, optional priority, and custom name.
8. Implement obstacle placement with support for limited rotation.
9. Implement v1 obstacle semantics: `HardBlocking`, `SlowThrough`, and optional hazard-linked blockage metadata.
10. Implement stair endpoints as paired portals across floors.
11. Store source floor, target floor, target endpoint ID, direction restrictions, travel cost, orientation, and custom name.
12. Add inspector controls for all semantic metadata defined above.
13. Keep wall-attached objects inheriting wall alignment rather than allowing arbitrary rotation.

Exit criteria:

- doors and windows cannot exist off-wall
- exits, obstacles, and stairs save/load correctly with metadata
- inspector edits persist and are undoable

### [PLANNED] Phase 7: Implement Floor Management and Visual Organization

Objective:
Make multi-floor projects manageable without losing alignment or data integrity.

Implementation steps:

1. Implement floor tabs with add, rename, reorder, duplicate, and delete.
2. Preserve one aligned building coordinate system across all floors.
3. On floor duplication, copy authored floor geometry and metadata while remapping entity IDs.
4. On floor delete or reorder, revalidate stair links and scenarios immediately.
5. Add confirmations for destructive floor changes that may invalidate stairs or scenarios.
6. Add per-floor metadata editing for name, order, and elevation.
7. Add per-object-type visibility toggles for walls, doors, windows, exits, stairs, obstacles, spawns, and regions.
8. Add lock/hide controls by object type and by current selection.
9. Add default visual styling per object type and a visible legend.
10. Keep styling simple and global; do not create user-managed style preset libraries.

Exit criteria:

- floors can be managed without breaking cross-floor references silently
- users can isolate active floors and object types
- duplicated floors do not retain duplicated entity IDs

### [PLANNED] Phase 8: Add Editing QoL, Precision, and Onboarding

Objective:
Make the sandbox trustworthy and efficient enough for real users.

Implementation steps:

1. Expand the command system to cover undo and redo for:
   wall tracing, door/window/exit/obstacle/stair placement, deletion, movement, metadata edits, floor edits, and link changes.
2. Implement copy, paste, duplicate, and batch delete.
3. Limit complex topology-sensitive batch edits to safe cases only.
4. Implement multi-select move and basic property edits for safe object types.
5. Implement measurement tool with distance readouts and selected-geometry readouts.
6. Add numeric property fields for wall thickness, opening width, travel cost, coordinates where appropriate, and obstacle sizes.
7. Add grid visibility toggle and configurable snap increments.
8. Add a minimap or overview navigator for large floors.
9. Add custom naming for floors, exits, stairs, scenarios, and important obstacles.
10. Add focus and isolation modes for active floor, selected objects, and selected object types.
11. Add optional low-level debug overlays for collider outlines, stair links, passable versus blocked regions, and route inspection.
12. Add first-run onboarding overlay and persistent tooltip/help text for tools and validation states.
13. Add advanced-properties foldouts for simulation metadata so advanced settings are visible but not dominant.

Exit criteria:

- common mistakes can be undone reliably
- precision editing is possible without handle dragging alone
- a new user can reach first successful tracing flow with in-product guidance

### [PLANNED] Phase 9: Add Save, Recovery, JSON Round-Trip, and Lifecycle States

Objective:
Protect work, preserve portability, and make project readiness explicit.

Implementation steps:

1. Implement manual save as the primary workflow.
2. Implement autosave recovery slots on a timer or significant edit cadence.
3. Keep autosaves distinct from the main saved state in both storage and UI language.
4. Add startup recovery prompt when autosave state is newer than the last manual save.
5. Implement JSON export for full building projects.
6. Implement JSON import for full building projects.
7. Implement reopening of previously exported structured files with schema compatibility checks and migration path execution.
8. Implement partial floor import into an existing project.
9. Validate floor-import conflicts for names, IDs, stair links, and cross-floor references before accepting import.
10. Add lifecycle state derivation and display:
    `Draft`, `Validation Failed`, `Ready for Simulation`, `Ready for Export`.
11. Add separate export actions where needed for runtime-ready data and preview images.
12. Keep raw data editing outside the public UI.

Exit criteria:

- users can save manually, recover autosaves, and round-trip JSON exports back into the editor
- lifecycle states reflect actual project validity and readiness
- partial import fails safely when conflicts are detected

### [PLANNED] Phase 10: Build Preview Mode, Spawns, Scenarios, and Diagnostics

Objective:
Give users bounded but meaningful evacuation feedback directly from the sandbox.

Implementation steps:

1. Create a distinct preview mode with explicit enter and exit actions.
2. Disable normal editing interactions while preview mode is active.
3. Limit v1 preview hazard scope to fire only.
4. Implement fire origin placement with support for one or a small number of origins.
5. Expose only a small preview parameter set such as spread intensity and start delay.
6. Require explicit spawn authoring before running preview.
7. Implement individual spawn placement.
8. Implement a density brush for painting spawn groups into valid areas.
9. Support both persistent saved spawn layouts and temporary preview-only spawn layouts.
10. Implement lightweight scenario presets bundling fire origins, spawn layouts, and basic preview parameters.
11. Add support for named area regions only where they serve clear semantics such as spawn zones, restricted zones, or annotations.
12. Run medium-depth preview checks covering geometry validity, reachable structure, exits, stairs, door states, obstacles, and blocked areas.
13. Show diagnostics for:
    pass/fail validation, unreachable areas, broken stair links, blocked exits, route previews, estimated route success, and obvious choke points.
14. Add congestion heatmap output during or after preview.
15. Keep advanced simulation controls outside the sandbox preview UI.

Exit criteria:

- preview cannot run with blocking validation errors
- users can set fire and spawns intentionally
- preview results point to actionable map fixes rather than opaque internal metrics

### [PLANNED] Phase 11: Polish, Hardening, and Release Readiness

Objective:
Turn the feature-complete sandbox into a stable demonstration and early-release candidate.

Implementation steps:

1. Audit all object types for consistent default styling and legend behavior.
2. Audit all inspector panels for missing numeric fields, naming fields, and advanced-property foldouts.
3. Audit all shortcuts for discoverability and conflicts.
4. Performance-test large floors, dense obstacle layouts, and multi-floor stair networks.
5. Add automated tests for serialization, migrations, validation, undo/redo, floor duplication, stair link integrity, and preview setup.
6. Add play mode tests for wall placement, snapping, object placement, and selection.
7. Run manual QA across:
   new project, import, calibration, tracing, save, autosave recovery, floor duplication, JSON round-trip, preview setup, and validation repair.
8. Confirm v1 deferrals remain deferred and have not leaked into scope.
9. Revisit the unresolved question of pinned reference callouts or traced checkpoints only after usability evidence from manual QA.

Exit criteria:

- core workflows are test-covered and manually verified
- large demo buildings remain usable
- the public-facing editor is clear about mode, validation state, and object semantics

## Recommended First Milestone

The first milestone should be a narrow but complete authoring slice:

1. open `SandboxEditor.unity`
2. create a new project with one floor
3. import a blueprint
4. calibrate scale
5. trace walls with line and brush tools
6. rebuild colliders
7. save and reload the project
8. confirm wall data, blueprint calibration, and validation state survive reload

This milestone is the minimum proof that the sandbox has a trustworthy authoring core.

## Testing Strategy

Recommended automated coverage:

- serialization and migration unit tests
- command-stack undo/redo tests
- validation rule tests
- floor duplication and stair-link integrity tests
- JSON import/export round-trip tests
- preview scenario and spawn-layout tests

Recommended manual QA coverage:

- first-run onboarding
- import and calibration
- wall cleanup and snapping
- inspector numeric editing
- hide, lock, focus, and isolation behavior
- autosave recovery
- preview setup and diagnostics

## Final Completeness Check

Nothing from `editorProposalSandbox.md` has been dropped from this plan without being intentionally categorized.

- Included in v1 phases: proposal items `1` through `41`, `43` through `65`
- Explicitly deferred from v1: proposal item `42`
- Left as a post-v1 decision gate: the unnumbered pinned-callout or traced-checkpoint discussion at the end of the proposal
