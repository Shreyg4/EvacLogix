# Sandbox Release Readiness

This checklist closes Phase 11 by making the release gate explicit and repeatable.

## Automated Coverage Added

- Edit mode coverage now includes:
  - serialization and migration regression checks
  - command undo and redo behavior
  - validation and preview blocking checks
  - floor duplication and stair-link integrity
  - preview setup, scenarios, and diagnostics
  - Phase 11 legend, shortcut, inspector-audit, metadata-round-trip, and stress coverage
- Play mode coverage now includes:
  - wall placement across frames
  - snapping behavior with live workspace state
  - semantic and preview object placement
  - selection and clipboard-safe movement

## Manual QA Checklist

Run these in `SandboxEditor.unity` with the editor visible. Mark each line only after the full flow succeeds.

- [ ] New project: create a default project and confirm one floor opens with `Draft` lifecycle state.
- [ ] Import: assign a blueprint to the active floor and confirm opacity and visibility toggles respond immediately.
- [ ] Calibration: capture point A, capture point B, enter real-world distance, and confirm the feedback text updates.
- [ ] Tracing: place line walls, place brush walls, accept brush cleanup, then move a wall handle and confirm snapping still applies.
- [ ] Save: save to a manual path, reload the same file, and confirm wall geometry, blueprint assignment, and calibration values survive.
- [ ] Autosave recovery: make a change after saving, trigger an autosave path, reload recovery, and confirm the recovery prompt clears after restore or dismiss.
- [ ] Floor duplication: duplicate a populated floor and confirm copied walls, semantic objects, and regions receive new IDs.
- [ ] JSON round-trip: export full project JSON, re-import it, and confirm lifecycle state and validation snapshot persist.
- [ ] Preview setup: place at least one spawn layout, one fire origin, and one scenario preset, then run preview and inspect routes plus heatmap output.
- [ ] Validation repair: intentionally create a blocking issue, verify preview/export is blocked, repair it, then confirm the block clears.

## Scope Guardrails

The following v1 deferrals are still intentionally out of scope:

- Proposal item `42`: comments and review annotations
- Pinned reference callouts
- Traced checkpoints for large-blueprint walkthroughs
- Advanced simulation controls beyond the sandbox fire-preview parameter set

## Decision Gate

Do not implement pinned reference callouts or traced checkpoints until the manual QA pass above produces concrete usability evidence that the existing onboarding, shortcuts, focus tools, and preview diagnostics are insufficient.
