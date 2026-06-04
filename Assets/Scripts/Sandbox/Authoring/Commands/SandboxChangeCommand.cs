using System;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Commands
{
    // A reversible change applied directly to the live project (no whole-project clone). This is the
    // foundation for diff-based undo: a command stores only what changed, so per-edit and retained
    // memory scale with the size of the edit rather than the size of the whole project.
    public interface ISandboxChange
    {
        // Approximate retained bytes (used by the history's memory budget).
        long EstimatedBytes { get; }

        void Apply(BuildingProjectData project);

        void Revert(BuildingProjectData project);
    }

    // Diff at floor granularity: stores the affected floor's before/after JSON instead of the whole
    // project. This is the first concrete slice — it deliberately does NOT capture project-level data
    // (blueprint image payloads, sibling floors, spawn layouts), which is what made whole-project undo
    // snapshots so heavy. Applying swaps just that one floor in place on the live project.
    public sealed class SandboxFloorSnapshotChange : ISandboxChange
    {
        private readonly string floorId;
        private readonly string beforeFloorJson;
        private readonly string afterFloorJson;

        public SandboxFloorSnapshotChange(string floorId, string beforeFloorJson, string afterFloorJson)
        {
            this.floorId = floorId ?? string.Empty;
            this.beforeFloorJson = beforeFloorJson ?? string.Empty;
            this.afterFloorJson = afterFloorJson ?? string.Empty;
        }

        public long EstimatedBytes => ((long)beforeFloorJson.Length + afterFloorJson.Length) * sizeof(char);

        public void Apply(BuildingProjectData project) => ReplaceFloor(project, afterFloorJson);

        public void Revert(BuildingProjectData project) => ReplaceFloor(project, beforeFloorJson);

        private void ReplaceFloor(BuildingProjectData project, string floorJson)
        {
            if (project?.floors == null || string.IsNullOrWhiteSpace(floorJson))
            {
                return;
            }

            var index = project.floors.FindIndex(floor =>
                floor != null && string.Equals(floor.floorId, floorId, StringComparison.Ordinal));
            if (index < 0)
            {
                // Under strict-LIFO undo with nested scopes this is unreachable (a floor-diff is always
                // older than any deletion of its floor, so the deletion's undo restores the floor first).
                // If it ever fires, a command-history invariant has been broken — surface it loudly
                // instead of silently desyncing undo/redo.
                Debug.LogWarning($"[Sandbox] Floor-diff undo could not find floor '{floorId}'; undo/redo may be out of sync.");
                return;
            }

            var restored = JsonUtility.FromJson<FloorData>(floorJson);
            if (restored != null)
            {
                project.floors[index] = restored;
            }
        }
    }

    // Wraps an ISandboxChange as an undoable command. It resolves the CURRENT live project at apply time
    // (rather than capturing a reference), so it stays correct even if another command that replaces the
    // project instance runs in between — important while diff-based and snapshot-based commands coexist.
    public sealed class SandboxChangeCommand : ISandboxEditorCommand
    {
        private readonly ISandboxChange change;
        private readonly Func<BuildingProjectData> resolveProject;
        private readonly Action onForwardApplied;
        private readonly Action onReverted;

        public SandboxChangeCommand(
            string description,
            ISandboxChange change,
            Func<BuildingProjectData> resolveProject,
            Action onForwardApplied,
            Action onReverted)
        {
            Description = string.IsNullOrWhiteSpace(description) ? "Unnamed Command" : description;
            this.change = change ?? throw new ArgumentNullException(nameof(change));
            this.resolveProject = resolveProject ?? throw new ArgumentNullException(nameof(resolveProject));
            this.onForwardApplied = onForwardApplied;
            this.onReverted = onReverted;
        }

        public string Description { get; }

        public long EstimatedMemoryBytes => change.EstimatedBytes;

        public void Execute()
        {
            var project = resolveProject();
            if (project != null)
            {
                change.Apply(project);
            }

            onForwardApplied?.Invoke();
        }

        public void Undo()
        {
            var project = resolveProject();
            if (project != null)
            {
                change.Revert(project);
            }

            onReverted?.Invoke();
        }
    }
}
