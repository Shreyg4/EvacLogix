# EvacLogix Agent Behavior Proposal

## Purpose

This document records the current design decisions for EvacLogix agent behavior and AI logic. It will be updated as the agent model is refined and tested.

## Ownership Boundary

This proposal is scoped specifically to agent behavior and AI logic.

Out of scope for this document:

- Website architecture and implementation
- Sandbox editor UX and tooling
- Fire simulation systems outside their effect on agent pathing and health

These systems may feed inputs to agents, but they are not owned by this proposal unless they directly affect agent decision logic.

## Current Direction

The agent model is intended to be a NavMesh-driven simulation with fire-aware pathing and minimal but clear status exposure in the sandbox UI. The behavior aims to avoid fire, keep pathing stable, and provide a legible interpretation of agent state for the user.

## Decisions Made So Far

### 1. Fire-Aware Traversal Cost Model

Chosen option: A

Decision:
- Use `NavMeshModifierVolume` to mark fire regions as a distinct area type and set higher costs per agent via `NavMeshAgent.SetAreaCost` or `NavMeshQueryFilter`.

### 2. Fire Spread Integration

Chosen option: A

Decision:
- Drive fire growth via a lightweight grid or cell map, and update `NavMeshModifierVolume` regions as cells ignite.

### 3. Exit Endpoints Are Sandbox Items

Decision:
- Exit endpoints are placed and managed through sandbox tooling, not the AI agent logic.

### 4. Agent Placement Is Sandbox Scope

Decision:
- Agent placement (one-by-one spawning) is handled by sandbox tooling, not AI agent logic.

### 5. Path Recalculation Triggers

Chosen option: A

Decision:
- Replan on a timer and on fire map updates using `NavMeshAgent.SetDestination` refresh.

### 6. Behavior Tuning for Crowd Realism

Chosen option: A

Decision:
- Use built-in avoidance (avoidance priority, radius, speed variance) with small per-agent variation.

### 7. Failure Handling and Fallback Behavior

Chosen option: A

Decision:
- If no viable path exists, send the agent to a nearest safe staging zone or stop with a "blocked" state indicator.

### 8. Agent Data Model for AI Controls

Chosen option: A

Decision:
- Minimal agent config (speed, radius, area cost multipliers) in a simple `ScriptableObject` profile.

### 9. Agent Info Display (Speed, Health)

Decision:
- Display per-agent info (at minimum speed and health) in the sandbox UI when an agent is selected or hovered.

### 10. Agent Info UI Behavior

Chosen option: A

Decision:
- Show a compact inspector panel with key stats for the currently selected agent.

### 11. Health Model Source

Chosen option: A

Decision:
- Health is derived from time exposed to fire and proximity intensity (simple decay).

### 12. Fire Proximity Metric for Health Decay

Chosen option: A

Decision:
- Sample distance to nearest active fire cell and scale decay by inverse distance.

### 13. Health Display Format

Chosen option: A

Decision:
- Show health as a percent plus a simple status label (Safe / Caution / Critical).

### 14. Speed Display Format

Chosen option: B

Decision:
- Show speed as a single current value only.

### 15. Agent Selection Method

Chosen option: B

Decision:
- Hover to select; info updates live with cursor movement.

### 16. Hover Selection Behavior for UI Clarity

Chosen option: A

Decision:
- Add a small highlight outline and lock the inspector panel while hovering.

### 17. Replan Cadence (Timer Interval)

Chosen option: A

Decision:
- Replan every 0.5-1.0 seconds (tunable per scene).

### 18. Replan Trigger on Hazard Update

Chosen option: A

Decision:
- Trigger immediate replan for agents within a radius of newly ignited cells.

### 19. Blocked-Path Handling (Window Break Option)

Decision:
- If an agent is blocked but there is an available window to break through, it will attempt that option; otherwise it continues avoiding fire without a staging-zone fallback.

### 20. Window-Break Eligibility

Chosen option: A

Decision:
- Only allow breakable windows that are explicitly tagged as sandbox items.

### 21. Window-Break Cost Tradeoff

Chosen option: A

Decision:
- Assign a high traversal cost to windows so they are only chosen when significantly better than fire-avoid routes.
