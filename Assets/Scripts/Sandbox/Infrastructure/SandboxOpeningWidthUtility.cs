using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public static class SandboxOpeningWidthUtility
    {
        private const float DefaultGridSize = 0.5f;

        public static float ResolveWorldWidth(
            BuildingProjectData project,
            FloorData floor,
            float authoredWidth,
            float gridSize)
        {
            if (authoredWidth <= 0f)
            {
                return 0f;
            }

            if (IsFloorCalibrated(project, floor))
            {
                return authoredWidth;
            }

            return authoredWidth * Mathf.Max(0.05f, gridSize);
        }

        public static float ResolveWorldWidth(
            SandboxProjectWorkspaceService workspaceService,
            SandboxWorkspaceStateService workspaceStateService,
            FloorData floor,
            float authoredWidth)
        {
            var gridSize = workspaceStateService != null
                ? workspaceStateService.GridSize
                : DefaultGridSize;
            return ResolveWorldWidth(workspaceService?.ActiveProject, floor, authoredWidth, gridSize);
        }

        public static bool IsFloorCalibrated(BuildingProjectData project, FloorData floor)
        {
            if (project == null || floor == null || string.IsNullOrWhiteSpace(floor.blueprintReferenceId))
            {
                return false;
            }

            var blueprintReference = project.blueprintReferences?.Find(candidate =>
                string.Equals(candidate.blueprintReferenceId, floor.blueprintReferenceId, System.StringComparison.Ordinal));
            return blueprintReference != null && blueprintReference.isCalibrated;
        }
    }
}
