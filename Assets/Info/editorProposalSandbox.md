# EvacLogix Sandbox Editor Proposal

## Purpose

This document records the current design decisions for the dedicated sandbox editor used in EvacLogix. It will be updated continuously as the editor design is stress-tested and refined.

## Ownership Boundary

This proposal is scoped specifically to the sandbox editor portion of EvacLogix.

Out of scope for this document:

- Website architecture and implementation
- Fire simulation systems
- Agent behavior systems

These systems may consume sandbox-authored data, but they are not the responsibility of this proposal unless they directly affect sandbox authoring workflows.

## Current Direction

The sandbox editor is intended to be a dedicated, fully user-facing feature for building evacuation maps in Unity. It will support:

- Tracing blueprint walls with a reference image and opacity slider
- Importing blueprint images in `PNG` and `JPG`
- Wall tracing with both a brush tool and a line tool
- Converting traced walls into colliders
- Adding doors
- Adding windows
- Adding exit goals
- Drawing obstacles
- Creating stair-based teleportation between floors
- Organizing floors through separate tabs in the sandbox menu

## Decisions Made So Far

### 1. Source of Truth

The source of truth should be editable Unity-side geometry and objects, not the imported image itself.

- Blueprint images are reference layers only
- Traced walls, openings, exits, stairs, and obstacles are the authoritative authored data

### 2. Product Positioning

The sandbox is being treated as a dedicated, user-facing tool rather than an internal-only authoring utility.

- This increases scope and implementation risk
- It should be treated as a core product feature, not just a dev convenience

### 3. Image Import and Export

Recommended handling:

- Import `PNG` and `JPG` blueprints as reference overlays
- Support opacity control for tracing
- Save editable projects as structured building data, not as flattened images
- Export preview images as a secondary sharing feature if time allows

Why:

- Images are good inputs, but poor editable source-of-truth formats
- Flattened image export would lose wall semantics, doors, stairs, exits, and object data

### 4. Scale Calibration

After importing a blueprint, the user should calibrate the image scale manually.

Recommended workflow:

- User clicks two points on the blueprint
- User enters the real-world distance between them
- The system computes the world scale from that measurement

This allows the editor to maintain meaningful distances for movement, collision, and pathfinding.

### 5. Wall Authoring Model

Recommended approach:

- `Line tool` creates straight wall segments
- `Brush tool` creates a polyline stroke that is simplified into connected wall segments
- Colliders are generated automatically from the resulting wall network

Walls should be authored as centerlines with editable thickness rather than filled polygons.

Why:

- Segment-based walls are easier to edit
- Doors and windows can snap directly to wall segments
- Collider rebuilding can be deterministic
- The data remains compact and semantic

### 6. Doors and Windows

Recommended approach:

- Doors and windows should be wall-attached objects
- They must snap onto an existing wall segment
- They should create an opening plus metadata on that wall

Recommended behavior:

- Doors are traversable by default
- Windows are non-traversable by default
- Windows can optionally be flagged as emergency exits
- Both should store width, position along the wall, and optional tags

Why:

- This keeps the geometry semantically correct
- Openings remain aligned with wall edits
- Invalid free-floating placement can be prevented

### 7. Collider and Navigation Updates

Recommended approach:

- Rebuild colliders and related navigation data live after each edit
- Provide a visible `Rebuild All` fallback button

Why:

- Users get immediate feedback while tracing and placing objects
- The tool feels interactive rather than batch-oriented
- The fallback provides recovery if incremental rebuilds ever desync

### 8. Stair Teleportation

Recommended approach:

- Model stairs as paired portal objects with explicit links

Each stair endpoint should store:

- Source floor
- Local position
- Target floor
- Target stair endpoint
- Optional travel cost or direction metadata

Why:

- It supports multi-floor buildings cleanly
- Validation is simple
- Later pathfinding can treat stairs as graph edges

## Open Design Questions

### 9. Validation Behavior

Recommended approach:

- Allow users to keep editing even when the building contains invalid data
- Prevent simulation and final export until blocking validation errors are fixed
- Show a validation panel listing errors by floor and object
- Highlight invalid objects directly in the editor

Why:

- It protects users from running broken maps
- It avoids turning normal editing into a frustrating hard-stop workflow
- It creates a clear difference between "editable" and "publishable" states

### 10. Save Model

Recommended approach:

- Save one building project that contains all floor tabs and all authored data
- The project should reference imported blueprint images rather than flatten them into the project format
- Offer separate export actions for preview images and runtime-ready data if needed

Why:

- Separate floor tabs naturally belong to one building-level project
- Shared metadata is easier to manage at the building level
- Multi-floor stair links become much simpler to validate and serialize

## Open Design Questions

### 11. Floor Coordinate System

Recommended approach:

- Use one aligned building coordinate system across all floors
- Keep separate editing tabs for each floor

Why:

- Stair endpoints are easier to reason about across floors
- Blueprint alignment remains more coherent
- Future analytics, overlays, and comparisons across floors become simpler
- The building behaves like one connected structure rather than isolated canvases

## Open Design Questions

### 12. Brush Tool Cleanup

Recommended approach:

- Brush strokes should be simplified into connected wall segments
- The cleanup should use tunable smoothing and point reduction
- Users should still be able to inspect and edit the generated result afterward

Why:

- Rough tracing becomes practical without taking control away from the user
- The resulting geometry stays clean enough for snapping, colliders, and openings
- It avoids over-aggressive automatic inference that could misread messy blueprints

## Open Design Questions

### 13. Undo and Redo

Recommended approach:

- The first public version should support at least basic undo and redo

Why:

- A public-facing editor without undo becomes frustrating very quickly
- Users will make frequent mistakes while tracing, placing objects, and linking stairs
- Undo reduces the penalty for experimentation and makes the sandbox feel trustworthy

Recommended minimum undo coverage:

- Wall tracing actions
- Door, window, exit, obstacle, and stair placement
- Object deletion
- Basic object movement and editing

## Open Design Questions

### 14. Direct Editing Workflow

Recommended approach:

- Users should be able to select an existing wall or object
- Editing should use visible handles plus an inspector-style properties panel

Why:

- This matches user expectations for a public sandbox editor
- It supports safer iteration alongside undo and redo
- Properties like wall thickness, opening width, exit behavior, and stair links need a clear editing path

Recommended editable items:

- Wall endpoints and thickness
- Door and window width and placement along the wall
- Exit metadata
- Obstacle bounds
- Stair links and travel metadata

## Open Design Questions

### 15. Snapping Assistance

Recommended approach:

- Provide strong optional snapping and enable it by default

Recommended snapping targets:

- Grid
- Wall endpoints
- Wall segments
- Aligned angles

Why:

- Blueprint tracing benefits from precision
- Doors, windows, and stairs need reliable placement behavior
- Optional toggles preserve flexibility for irregular or noisy layouts
- Mandatory snapping would become frustrating on messy source images

## Open Design Questions

### 16. Blueprint Asset Storage

Recommended approach:

- Imported blueprint images should be copied into the Unity project
- The sandbox should reference the Unity-managed asset copy rather than an external file path

Why:

- External paths hurt portability
- Team collaboration becomes safer when assets live inside the project
- Reloading, export, and validation become more predictable
- Unity's asset pipeline can manage imported images consistently

## Open Design Questions

### 17. Save and Recovery Behavior

Recommended approach:

- Use explicit manual save as the primary workflow
- Add periodic or event-based recovery autosave slots

Why:

- Public editors need protection against accidental data loss
- Explicit save gives users confidence and control
- Recovery autosaves improve resilience without making the state model feel mysterious

Recommended autosave role:

- Recover from crashes or accidental closures
- Preserve recent edits between manual saves
- Remain clearly distinct from the user's main saved project state

## Open Design Questions

### 18. Floor Management

Recommended approach:

- Users should be able to add, rename, reorder, duplicate, and delete floor tabs
- Stair links and validation should update accordingly
- Destructive changes should include guardrails and confirmation where needed

Why:

- This supports realistic building authoring workflows
- It matches the promise of separate floor tabs
- It avoids unnecessary rigidity while still protecting project integrity

## Open Design Questions

### 19. Collaboration Model

Recommended approach:

- Design the first public sandbox around single-user editing
- Keep project assets portable so they can still be shared through Unity and Git workflows

Why:

- Real-time collaboration would add major scope and synchronization complexity
- Current authoring needs are satisfied by portable building project assets
- This keeps the sandbox focused on editing quality rather than multiplayer state management

## Open Design Questions

### 20. Onboarding

Recommended approach:

- Include lightweight onboarding in the first public version
- Use a short first-run guided overlay
- Keep persistent tooltips or help text for key tools and validation states

Why:

- New users need guidance for scale calibration, tracing, and stair linking
- A mandatory deep tutorial would add friction
- No onboarding would make the sandbox feel intimidating

## Open Design Questions

### 21. Simulation Metadata Exposure

Recommended approach:

- Expose core editable metadata during sandbox authoring
- Keep advanced simulation settings inside expandable advanced-properties sections

Examples of metadata that may be edited:

- Exit weight or priority
- Window escape behavior or cost
- Obstacle type
- Stair travel cost or directional metadata

Why:

- The authored map needs semantics as well as geometry
- Advanced settings are useful without needing to dominate the main workflow
- This keeps geometry and behavior in one coherent authoring flow

## Open Design Questions

### 22. Building Lifecycle States

Recommended approach:

- Buildings should have explicit lifecycle states visible in the UI

Recommended states:

- `Draft`
- `Validation Failed`
- `Ready for Simulation`
- `Ready for Export`

Why:

- Users get a clearer mental model of project readiness
- Lifecycle states pair naturally with validation and blocked simulation/export actions
- Multi-floor authoring feels more intentional when project state is visible
- This creates room for future workflows such as publishable templates or approved scenarios

## Open Design Questions

### 23. New Project Creation

Recommended approach:

- Start new projects with a lightweight template by default
- Include one default floor tab
- Show empty building metadata
- Present an import and scale-calibration prompt
- Suggest the next steps for tracing and object placement
- Also provide an explicit option to start from a completely blank template

Why:

- The default lightweight template reduces first-run confusion
- It complements the planned onboarding flow
- The blank option preserves flexibility for advanced users or unusual workflows
- It avoids the friction of a heavy project-creation wizard

## Open Design Questions

### 24. Multi-Select Editing

Recommended approach:

- Support multi-select in the first public version for simple batch actions
- Allow batch delete, move, duplicate, and safe basic property edits
- Keep complex topology-sensitive edits single-object only in v1

Why:

- Users expect some batch editing in a serious sandbox
- It is especially useful for obstacles, exits, and repeated placements
- Full topology-aware multi-edit for walls and openings would add much more risk and complexity

## Open Design Questions

### 25. Copy-Paste and Templates

Recommended approach:

- Support copy-paste for selected objects in the first public version
- Support duplication of whole floors
- Defer richer reusable prefab or template libraries until later

Why:

- Copy-paste is a practical and expected editing feature
- Floor duplication is valuable for similar building levels
- A full reusable library system would add significant UI and persistence complexity

## Open Design Questions

### 26. Authoring Visual Differentiation

Recommended approach:

- Use clear default visual styling for each object type
- Provide a simple legend
- Add per-type visibility toggles

Relevant object types include:

- Walls
- Doors
- Windows
- Exits
- Stairs
- Obstacles

Why:

- Users need to parse the authored map quickly
- Different object types carry different semantics
- Visibility toggles help when tracing dense or cluttered blueprints

## Open Design Questions

### 27. Locking and Hiding

Recommended approach:

- Support hide and lock controls in the first public version
- Allow these controls by object type
- Allow them for selected objects or selected groups
- Do not build a full custom layer hierarchy in v1

Why:

- Blueprint tracing benefits from temporary visibility control
- Locking reduces accidental edits
- A simple system captures most of the practical value without the complexity of a full layer stack

## Open Design Questions

### 28. In-Editor Simulation Preview

Recommended approach:

- Include a preview or run mode directly inside the sandbox
- Keep it clearly separated from editing mode

Why:

- Users benefit from fast feedback on the maps they author
- Forcing a scene or tool switch would add friction
- Strong mode separation avoids confusion between editing interactions and runtime behavior
- This fits cleanly with the planned lifecycle-state model

## Open Design Questions

### 29. Preview Depth

Recommended approach:

- The in-editor preview should be medium-depth
- It should validate geometry
- It should show navigable and blocked structure
- It should test exits, stairs, and obstacles
- It should run a lightweight evacuation check
- Advanced scenario controls should remain outside the core sandbox authoring flow

Why:

- Users need more than a static geometry check
- Full simulation controls would overload the editor
- A bounded preview keeps authoring and verification closely connected

## Open Design Questions

### 30. Preview Diagnostics and Results

Recommended approach:

- Show practical authoring diagnostics first
- Include pass/fail validation feedback
- Highlight unreachable areas
- Report broken stair links
- Report blocked exits
- Show simple path previews
- Include summary metrics such as estimated route success and obvious choke points
- Include lightweight congestion heatmaps during or after preview runs

Why:

- Authors need actionable fixes more than research-grade dashboards
- Minimal feedback would not help users improve their maps effectively
- Congestion heatmaps are highly valuable for spotting bottlenecks during authoring
- Keeping the results lightweight preserves the sandbox's focus while still making preview meaningfully informative

## Open Design Questions

### 31. Hazard Scope for Preview

Recommended approach:

- Support fire only in the first public sandbox preview

Why:

- This matches the current EvacLogix proposal
- It keeps preview logic and validation grounded
- It avoids multiplying behavior rules too early
- The underlying data model can still remain extensible for future hazard types

## Open Design Questions

### 32. Fire Placement Control

Recommended approach:

- Let users place one fire origin point, or a small number of fire origin points, directly on the map during preview
- Expose only a few simple parameters such as spread intensity or start delay

Why:

- This gives immediate useful feedback during authoring
- It aligns with the proposal's requirement that users can choose where the fire starts
- It keeps preview setup fast and approachable
- Deep hazard authoring belongs in a more advanced simulation layer

## Open Design Questions

### 33. Agent Spawn Authoring

Chosen direction:

- Preview runs should require explicit spawn authoring
- The sandbox should support one-by-one agent placement
- The sandbox should also support a density spawning brush for painting groups of agents into valid regions

Why:

- This gives authors precise control over where evacuation scenarios begin
- A density brush makes crowd setup practical for larger scenarios
- Single-agent placement supports targeted testing of edge cases and choke points

Tradeoff:

- This adds friction compared with automatic spawning
- It should therefore be supported by clear UI, good defaults, and validation feedback for invalid spawn regions

## Open Design Questions

### 34. Spawn Persistence

Recommended approach:

- Support both persistent and temporary spawn layouts
- Let users save spawn layouts as part of the building project when they are meaningful authored scenarios
- Also allow temporary preview-only spawns that can be cleared separately

Why:

- Some spawn setups represent intentional authored scenarios
- Some spawn setups are only quick tests and should not permanently modify the project
- This supports both experimentation and repeatable demonstrations

## Open Design Questions

### 35. Exit Modeling

Recommended approach:

- Author exits as zones rather than simple points
- Store width, orientation, and optional capacity or priority metadata

Why:

- Real exits have width and flow implications
- Congestion analysis becomes more meaningful
- This stays consistent with the editor's semantic treatment of doors and windows
- Point-only exits would be too crude for meaningful crowd preview behavior

## Open Design Questions

### 36. Obstacle Modeling

Recommended approach:

- Support a small set of semantic obstacle types in the first public version
- Keep the initial set limited and practical

Recommended initial obstacle semantics:

- Hard-blocking obstacles
- Slow-through or high-cost obstacles
- Temporary hazard-linked blockages where relevant

Stretch goal:

- Hazards may later support customized traversal weights that discourage agents from using those regions without making them fully impassable

Why:

- This gives the sandbox more realistic authoring power than generic blocking shapes alone
- It supports congestion-aware and rerouting-aware preview behavior
- A small typed system stays manageable while preserving room for future expansion

## Open Design Questions

### 37. Window Modeling

Recommended approach:

- Windows should be wall-attached objects with semantic metadata in the first public version
- They may serve as alternate escape routes when explicitly flagged for escape use
- They should store escape eligibility, cost, and risk metadata
- Escape risk should depend on the floor the window is on, with higher floors carrying higher escape risk
- Do not focus on detailed breakability simulation in v1

Why:

- Windows matter semantically in the EvacLogix concept
- They can function as alternate exits without requiring a full destruction or access simulation model
- Floor-dependent escape risk creates a meaningful rule that is easy to understand and useful in preview behavior
- Deferring breakability keeps the first version focused

## Open Design Questions

### 38. Door Modeling

Recommended approach:

- Support a small set of door states in the first public version

Recommended initial door states:

- Normal
- Blocked
- Locked
- Optional one-way

Why:

- Door behavior is central to evacuation realism
- This pairs naturally with validation and preview
- A small state set adds meaningful value without requiring a complex rule engine

## Open Design Questions

### 39. Stair Portal Behavior

Recommended approach:

- Support directional restrictions on stair links in the first public version
- Support simple travel costs on stair links

Why:

- Some stairs may behave differently going up versus going down
- Travel cost improves route quality across floors during preview
- This fits naturally with the explicit paired-portal stair model
- Richer dynamic stair behavior would add too much complexity for v1

## Open Design Questions

### 40. Scenario Presets

Recommended approach:

- Support lightweight saved scenario presets tied to a building project
- Allow a preset to bundle fire origins, spawn layouts, and a few preview parameters

Why:

- Persistent spawns and explicit fire placement already point toward repeatable scenarios
- Saved presets make demonstrations and testing much easier
- Lightweight presets add strong value without requiring a large scenario-management system

## Open Design Questions

### 41. Debug and Inspection Overlays

Recommended approach:

- Provide optional low-level debug overlays in the first public version
- Hide them behind an inspection or debug toggle

Recommended overlays:

- Collider outlines
- Stair link visualization
- Passable versus blocked regions
- Basic route inspection overlays

Why:

- Authors benefit from seeing what the system thinks is real
- These views help diagnose bad wall traces, door openings, and stair links quickly
- Keeping them optional avoids cluttering the core public-facing UX

## Open Design Questions

### 42. Comments and Review Annotations

Recommended approach:

- Do not build a dedicated comment or review-annotation system into the first public version

Why:

- Comments are useful, but they push the sandbox toward collaboration and review tooling
- The current direction is explicitly single-user-first
- Exported previews, scenario names, and portable project assets already cover many review needs

## Open Design Questions

### 43. Custom Naming

Recommended approach:

- Support custom naming selectively in the first public version

Recommended named objects:

- Floors
- Exits
- Stairs
- Scenarios
- Important obstacles

Why:

- Human-readable names help with validation, debugging, and scenario setup
- Some objects benefit strongly from identity, while tiny primitives usually do not
- This keeps the UI helpful without becoming noisy

## Open Design Questions

### 44. Object Rotation

Recommended approach:

- Support rotation where it is semantically useful
- Keep walls and wall-attached openings aligned to wall geometry
- Avoid arbitrary free-rotation for every object type

Recommended objects for limited rotation:

- Obstacles
- Exits
- Stair markers

Why:

- Some authored objects genuinely need orientation
- Wall-attached objects already inherit orientation naturally
- Full free-rotation everywhere would add unnecessary UI and validation complexity

## Open Design Questions

### 45. Measurement Tools

Recommended approach:

- Include lightweight measurement tools beyond initial scale calibration
- Provide a simple distance or size measurement tool
- Show visual measurement readouts for selected geometry where useful

Why:

- Users tracing blueprints will want to sanity-check lengths and widths
- This reinforces trust in the scale calibration
- A lightweight ruler and readout tool adds strong value without requiring a full CAD feature set

## Open Design Questions

### 46. Grid Controls

Recommended approach:

- Support grid visibility toggling in the first public version
- Allow users to adjust grid size and snap increments

Why:

- This pairs naturally with snapping
- Different blueprint scales benefit from different grid resolutions
- Visible grids improve orientation and precision

## Open Design Questions

### 47. Keyboard Shortcuts

Recommended approach:

- Support basic keyboard shortcuts in the first public version

Recommended shortcut coverage:

- Selection
- Delete
- Undo and redo
- Copy and paste
- Duplicate
- Tool switching
- Grid and snap toggles

Why:

- Even a public editor benefits significantly from basic efficiency
- Core shortcuts are expected and improve usability immediately
- A focused shortcut set remains approachable and practical to implement

## Open Design Questions

### 48. Overview Navigation

Recommended approach:

- Include a simple overview or minimap-style navigation aid for large floors

Why:

- Large floor plans can become tedious to navigate
- An overview helps users maintain orientation while zooming into detailed tracing work
- A lightweight navigation aid delivers strong value without requiring a complex camera-management system

## Open Design Questions

### 49. Numeric Property Editing

Recommended approach:

- Support direct numeric input for key editable values through properties panels
- Keep this alongside handle-based editing rather than replacing it

Examples of numeric-editable values:

- Wall thickness
- Opening width
- Object positions where appropriate
- Travel cost

Why:

- Numeric input improves precision and trust
- Some values are awkward to tune by dragging alone
- This complements direct manipulation without turning the editor into a CAD system

## Open Design Questions

### 50. Per-Floor Metadata

Recommended approach:

- Support focused per-floor metadata in the first public version

Recommended floor metadata:

- Floor name
- Floor order or index
- Elevation where useful
- Limited floor-level modifiers where semantically appropriate

Why:

- Floor-dependent window escape risk already implies meaningful floor semantics
- Stair logic and cross-floor reasoning benefit from explicit per-floor metadata
- A focused metadata set keeps the model useful without becoming overbuilt

## Open Design Questions

### 51. Validation Rules

Recommended approach:

- Use fixed built-in validation rules in the first public version
- Present clear messages and keep room for future expansion

Why:

- Configurable validation systems add substantial complexity
- The current sandbox goals are well served by strong default structural rules
- Built-in validation is easier to explain, test, and trust in an initial public release

## Open Design Questions

### 52. External Building Data Import and Export

Recommended approach:

- Support one structured external building-data format in the first public version, such as JSON
- Keep Unity-native assets as the main authoring workflow
- Use the external format primarily for portability, debugging, and future integration

Scope note:

- Sandbox import and export should live inside the Unity editor workflow
- How website upload, download, or presentation layers consume exported data is outside the scope of this proposal

Why:

- One external format is manageable
- It improves portability and inspection without fragmenting the core workflow
- Keeping import and export inside Unity preserves separation of concerns for the sandbox owner

### 53. Schema Versioning

Recommended approach:

- Include an explicit schema version in saved sandbox building data from the first version
- Support lightweight migration handling in code when the schema changes

Why:

- The sandbox data model is already rich and likely to evolve
- A version field plus simple migration logic protects old saved maps from breaking
- This is cheap to establish early and expensive to retrofit later

## Open Design Questions

### 54. Raw Data Editing

Recommended approach:

- Do not expose a raw-data editor in the first public version
- Allow import and export of structured files
- Keep raw schema editing as a developer-only or debug-only path outside the public sandbox UI

Why:

- Public raw-schema editing creates easy ways to corrupt project data
- It adds support burden without helping most users
- The public sandbox workflow should remain visual and structured

## Open Design Questions

### 55. Partial Import

Recommended approach:

- Support importing a single floor, or selected floor-level data, into an existing building project
- Validate imported floor data before it is accepted
- Check for conflicts with floor names, stair links, and other cross-floor references

Why:

- Floors are already a major authoring unit in the sandbox
- This adds useful flexibility without requiring a full arbitrary merge system
- It fits naturally with the separate floor-tab workflow

## Open Design Questions

### 56. Style Presets

Recommended approach:

- Keep styling simple in the first public version
- Provide a few global defaults per object type
- Do not build a user-managed style preset library in v1

Why:

- The sandbox is semantic and authoring-focused rather than a visual design tool
- A few defaults are enough to maintain clarity during editing
- User-managed style presets would add persistence and UI complexity with limited value for this scope

## Open Design Questions

### 57. Wall Junction Editing

Recommended approach:

- Support explicit junction behavior where wall segments meet
- Allow editable shared connection points for wall intersections and joins

Why:

- Wall networks are central to the sandbox
- Doors, openings, and collider generation become more reliable when junctions are treated as first-class structures
- Implicit-only intersections can become brittle and difficult to debug

## Open Design Questions

### 58. Wall Splitting and Merging

Recommended approach:

- Support direct wall split operations in the first public version
- Support direct wall merge operations in the first public version

Why:

- Openings and junction edits naturally create situations where users need to restructure wall segments
- Delete-and-recreate workflows would be clumsy and error-prone
- Split and merge operations fit strongly with the editable wall geometry model

## Open Design Questions

### 59. Parallel or Offset Wall Tools

Recommended approach:

- Do not build a dedicated offset or parallel-wall creation tool in the first public version
- Rely first on the existing line tool, brush tool, snapping, thickness editing, measurement tools, and numeric input

Why:

- Dedicated offset tools push the sandbox toward CAD-style scope
- The current wall editing toolset already covers many practical tracing needs
- It is better to validate real user pain before adding more specialized wall-construction helpers

## Open Design Questions

### 60. Non-Wall Region Drawing

Recommended approach:

- Support only simple named area regions where they serve clear sandbox semantics
- Suitable uses include spawn zones, restricted zones, or floor annotations
- Do not build a full room or hallway modeling system in the first public version

Why:

- Some region semantics are genuinely useful for authoring
- Full room and area modeling would add significant structure and UI complexity
- This keeps region tools purposeful rather than decorative

## Open Design Questions

### 61. Cleanup Tools

Recommended approach:

- Support a simple erase and trim workflow for traced walls in the first public version

Why:

- Traced input is inherently messy
- Cleanup needs to be fast, especially for brush-based wall authoring
- Delete, split, and manual edits alone would feel too clunky for a public tracing workflow

## Open Design Questions

### 62. Corner Snapping and Near-Join Cleanup

Recommended approach:

- Support conservative near-corner snapping in the first public version
- Allow optional automatic cleanup when wall endpoints fall within a threshold

Why:

- Blueprint tracing often produces almost-connected corners
- Conservative cleanup reduces frustration without over-interpreting geometry
- Aggressive topology inference would make the editor harder to trust

## Open Design Questions

### 63. Reopening External Data Files

Recommended approach:

- Allow previously exported external sandbox data files to be reopened and edited
- Treat them as first-class project inputs when they match the sandbox schema
- Validate schema compatibility and run migration checks where needed

Why:

- If the sandbox supports external structured export, users will reasonably expect round-tripping
- This improves portability and backup confidence
- Existing schema validation and migration plans provide the needed safety net

## Open Design Questions

### 64. Duplicate and Conflict Detection

Recommended approach:

- Add project-level duplicate and conflict detection as part of validation
- Report warnings or errors depending on severity

Examples of checks:

- Overlapping exits
- Duplicate stair endpoints or invalid stair pairings
- Stacked or conflicting obstacles

Why:

- Public visual editors benefit strongly from structural sanity checks
- This fits naturally into the planned validation panel
- It catches common authoring mistakes early without requiring a heavy conflict-resolution system

## Open Design Questions

### 65. Focus and Isolation Modes

Recommended approach:

- Support lightweight focus and isolation modes in the first public version
- Allow focus on the active floor, selected objects, or chosen object types

Why:

- Dense blueprint editing benefits from reduced visual noise
- This complements existing hide and lock controls
- A lightweight focus mode adds clarity without becoming a full workspace-management system

## Open Design Questions

The next unresolved branch currently under discussion is whether the first public version should support pinned reference callouts or traced checkpoints to help users work through large blueprints systematically.
