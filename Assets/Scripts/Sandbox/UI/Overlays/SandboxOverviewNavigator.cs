using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxOverviewNavigator : MonoBehaviour
    {
        [SerializeField] private bool overviewEnabled = true;
        [SerializeField] private Rect worldBounds = new(-10f, -10f, 20f, 20f);

        private SandboxCameraController cameraController;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;

        public bool OverviewEnabled => overviewEnabled;
        public Rect WorldBounds => worldBounds;

        private void Awake()
        {
            cameraController = FindAnyObjectByType<SandboxCameraController>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
            }

            RefreshBounds();
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }
        }

        public void SetOverviewEnabled(bool enabled)
        {
            overviewEnabled = enabled;
        }

        public void SetWorldBounds(Rect bounds)
        {
            worldBounds = bounds;
        }

        public void FocusOnWorldPoint(Vector2 point)
        {
            cameraController ??= FindAnyObjectByType<SandboxCameraController>();
            if (!overviewEnabled || cameraController == null)
            {
                return;
            }

            cameraController.FocusOnPoint(point);
        }

        public void FocusOnActiveFloor()
        {
            cameraController ??= FindAnyObjectByType<SandboxCameraController>();
            if (!overviewEnabled || cameraController == null)
            {
                return;
            }

            RefreshBounds();
            cameraController.FrameBounds(worldBounds);
        }

        public bool FocusOnSelection()
        {
            cameraController ??= FindAnyObjectByType<SandboxCameraController>();
            if (!overviewEnabled || cameraController == null || !TryBuildSelectionBounds(out var selectionBounds))
            {
                return false;
            }

            cameraController.FrameBounds(selectionBounds, 0.75f);
            return true;
        }

        public void ResetView()
        {
            cameraController?.ResetView();
        }

        public void RefreshBounds()
        {
            worldBounds = TryBuildActiveFloorBounds(out var bounds)
                ? bounds
                : new Rect(-10f, -10f, 20f, 20f);
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            RefreshBounds();
        }

        private void HandleFloorChanged(FloorData floor)
        {
            RefreshBounds();
        }

        private void HandleSelectionChanged(IReadOnlyList<string> selection)
        {
            RefreshBounds();
        }

        private bool TryBuildActiveFloorBounds(out Rect bounds)
        {
            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                bounds = new Rect();
                return false;
            }

            var points = new List<Vector2>();
            CollectFloorPoints(floor, points);

            var project = workspaceService.ActiveProject;
            if (project != null)
            {
                foreach (var layout in project.spawnLayouts)
                {
                    points.AddRange(layout.spawnPoints.Where(point => point.floorId == floor.floorId).Select(point => point.position));
                    foreach (var stroke in layout.spawnBrushStrokes.Where(stroke => stroke.floorId == floor.floorId))
                    {
                        points.AddRange(stroke.polygonPoints);
                    }
                }
            }

            return TryBuildBounds(points, out bounds);
        }

        private bool TryBuildSelectionBounds(out Rect bounds)
        {
            bounds = new Rect();
            var floor = workspaceService?.ActiveFloor;
            var project = workspaceService?.ActiveProject;
            var selectedIds = selectionService?.SelectedObjectIds;
            if (floor == null || project == null || selectedIds == null || selectedIds.Count == 0)
            {
                return false;
            }

            var points = new List<Vector2>();

            foreach (var wall in floor.wallSegments.Where(candidate => selectedIds.Contains(candidate.wallSegmentId)))
            {
                points.Add(wall.startPoint);
                points.Add(wall.endPoint);
            }

            foreach (var door in floor.doors.Where(candidate => selectedIds.Contains(candidate.doorId)))
            {
                AddOpeningPoint(floor, door.wallSegmentId, door.offsetAlongWall, points);
            }

            foreach (var window in floor.windows.Where(candidate => selectedIds.Contains(candidate.windowId)))
            {
                AddOpeningPoint(floor, window.wallSegmentId, window.offsetAlongWall, points);
            }

            points.AddRange(floor.exits.Where(candidate => selectedIds.Contains(candidate.exitZoneId)).Select(candidate => candidate.center));
            points.AddRange(floor.obstacles.Where(candidate => selectedIds.Contains(candidate.obstacleId)).Select(candidate => candidate.center));
            points.AddRange(floor.stairPortals.Where(candidate => selectedIds.Contains(candidate.stairPortalId)).Select(candidate => candidate.localPosition));
            points.AddRange(floor.teleportPortals.Where(candidate => selectedIds.Contains(candidate.teleportPortalId)).Select(candidate => candidate.localPosition));
            foreach (var region in floor.regions.Where(candidate => selectedIds.Contains(candidate.regionId)))
            {
                points.AddRange(region.polygonPoints);
            }

            foreach (var layout in project.spawnLayouts)
            {
                points.AddRange(layout.spawnPoints
                    .Where(candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.spawnPointId))
                    .Select(candidate => candidate.position));
                foreach (var stroke in layout.spawnBrushStrokes.Where(candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.spawnBrushStrokeId)))
                {
                    points.AddRange(stroke.polygonPoints);
                }
            }

            return TryBuildBounds(points, out bounds);
        }

        private static void CollectFloorPoints(FloorData floor, List<Vector2> points)
        {
            points.AddRange(floor.wallJunctions.Select(junction => junction.position));

            foreach (var wall in floor.wallSegments)
            {
                points.Add(wall.startPoint);
                points.Add(wall.endPoint);
            }

            points.AddRange(floor.exits.Select(exitZone => exitZone.center));
            points.AddRange(floor.obstacles.Select(obstacle => obstacle.center));
            points.AddRange(floor.stairPortals.Select(stair => stair.localPosition));
            points.AddRange(floor.teleportPortals.Select(teleport => teleport.localPosition));

            foreach (var region in floor.regions)
            {
                points.AddRange(region.polygonPoints);
            }
        }

        private static void AddOpeningPoint(FloorData floor, string wallSegmentId, float offsetAlongWall, List<Vector2> points)
        {
            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            var direction = (wall.endPoint - wall.startPoint).normalized;
            points.Add(wall.startPoint + direction * offsetAlongWall);
        }

        private static bool TryBuildBounds(IReadOnlyList<Vector2> points, out Rect bounds)
        {
            if (points == null || points.Count == 0)
            {
                bounds = new Rect();
                return false;
            }

            var minX = points.Min(point => point.x);
            var minY = points.Min(point => point.y);
            var maxX = points.Max(point => point.x);
            var maxY = points.Max(point => point.y);
            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }
    }
}
