using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxMeasurementService : MonoBehaviour
    {
        [SerializeField] private bool hasPointA;
        [SerializeField] private bool hasPointB;
        [SerializeField] private Vector2 pointA;
        [SerializeField] private Vector2 pointB;
        [SerializeField] private string lastDistanceReadout = string.Empty;
        [SerializeField] private string lastSelectionReadout = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;

        public event Action MeasurementsChanged;

        public bool HasPointA => hasPointA;
        public bool HasPointB => hasPointB;
        public Vector2 PointA => pointA;
        public Vector2 PointB => pointB;
        public string LastDistanceReadout => lastDistanceReadout;
        public string LastSelectionReadout => lastSelectionReadout;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            selectionService = GetComponent<SandboxSelectionService>();

            if (selectionService != null)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
            }

            if (workspaceService != null)
            {
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
            }

            RefreshSelectionReadout();
        }

        private void OnDestroy()
        {
            if (selectionService != null)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }

            if (workspaceService != null)
            {
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
            }
        }

        public string RegisterMeasurementPoint(Vector2 worldPoint)
        {
            if (!hasPointA)
            {
                pointA = worldPoint;
                hasPointA = true;
                hasPointB = false;
                lastDistanceReadout = "Measurement point A captured.";
                RaiseChanged();
                return lastDistanceReadout;
            }

            pointB = worldPoint;
            hasPointB = true;
            lastDistanceReadout = BuildDistanceReadoutWithUnit(pointA, pointB);
            RaiseChanged();
            return lastDistanceReadout;
        }

        public void ClearMeasurement()
        {
            hasPointA = false;
            hasPointB = false;
            pointA = Vector2.zero;
            pointB = Vector2.zero;
            lastDistanceReadout = string.Empty;
            RaiseChanged();
        }

        public string RefreshSelectionReadout()
        {
            var floor = workspaceService?.ActiveFloor;
            var selectedIds = selectionService?.SelectedObjectIds ?? Array.Empty<string>();
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            if (floor == null || selectedIds.Count == 0)
            {
                lastSelectionReadout = "No selection geometry to measure.";
                RaiseChanged();
                return lastSelectionReadout;
            }

            var points = new List<Vector2>();
            var totalWallLength = 0f;
            var totalDoorWidth = 0f;
            var totalWindowWidth = 0f;
            var totalObstacleArea = 0f;

            foreach (var wall in floor.wallSegments.Where(wall => selectedIds.Contains(wall.wallSegmentId)))
            {
                points.Add(wall.startPoint);
                points.Add(wall.endPoint);
                totalWallLength += Vector2.Distance(wall.startPoint, wall.endPoint);
            }

            foreach (var door in floor.doors.Where(door => selectedIds.Contains(door.doorId)))
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == door.wallSegmentId);
                if (wall == null)
                {
                    continue;
                }

                var direction = (wall.endPoint - wall.startPoint).normalized;
                points.Add(wall.startPoint + direction * door.offsetAlongWall);
                totalDoorWidth += door.width;
            }

            foreach (var window in floor.windows.Where(window => selectedIds.Contains(window.windowId)))
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == window.wallSegmentId);
                if (wall == null)
                {
                    continue;
                }

                var direction = (wall.endPoint - wall.startPoint).normalized;
                points.Add(wall.startPoint + direction * window.offsetAlongWall);
                totalWindowWidth += window.width;
            }

            foreach (var exitZone in floor.exits.Where(exitZone => selectedIds.Contains(exitZone.exitZoneId)))
            {
                AddRotatedRectPoints(points, exitZone.center, exitZone.size, exitZone.rotationDegrees);
            }

            foreach (var obstacle in floor.obstacles.Where(obstacle => selectedIds.Contains(obstacle.obstacleId)))
            {
                AddRotatedRectPoints(points, obstacle.center, obstacle.size, obstacle.rotationDegrees);
                totalObstacleArea += obstacle.size.x * obstacle.size.y;
            }

            foreach (var stairPortal in floor.stairPortals.Where(portal => selectedIds.Contains(portal.stairPortalId)))
            {
                AddRotatedRectPoints(points, stairPortal.localPosition, stairPortal.size, stairPortal.rotationDegrees);
            }

            foreach (var teleportPortal in floor.teleportPortals.Where(portal => selectedIds.Contains(portal.teleportPortalId)))
            {
                AddRotatedRectPoints(points, teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees);
            }

            foreach (var fireOrigin in workspaceService.ActiveProject.fireOrigins.Where(origin => origin.floorId == floor.floorId && selectedIds.Contains(origin.fireOriginId)))
            {
                points.Add(fireOrigin.position);
            }

            foreach (var layout in workspaceService.ActiveProject.spawnLayouts)
            {
                foreach (var spawnPoint in layout.spawnPoints.Where(point => point.floorId == floor.floorId && selectedIds.Contains(point.spawnPointId)))
                {
                    points.Add(spawnPoint.position);
                }

                foreach (var stroke in layout.spawnBrushStrokes.Where(stroke => stroke.floorId == floor.floorId && selectedIds.Contains(stroke.spawnBrushStrokeId)))
                {
                    points.AddRange(stroke.polygonPoints);
                }
            }

            if (points.Count == 0)
            {
                lastSelectionReadout = "Selected objects do not expose measurable geometry yet.";
                RaiseChanged();
                return lastSelectionReadout;
            }

            var minX = points.Min(point => point.x);
            var minY = points.Min(point => point.y);
            var maxX = points.Max(point => point.x);
            var maxY = points.Max(point => point.y);
            var boundsWidth = maxX - minX;
            var boundsHeight = maxY - minY;
            lastSelectionReadout =
                $"Selection bounds {SandboxDistanceUnitUtility.FormatDistance(boundsWidth, distanceUnit)} x {SandboxDistanceUnitUtility.FormatDistance(boundsHeight, distanceUnit)}, wall length {SandboxDistanceUnitUtility.FormatDistance(totalWallLength, distanceUnit)}, door width {SandboxDistanceUnitUtility.FormatDistance(totalDoorWidth, distanceUnit)}, window width {SandboxDistanceUnitUtility.FormatDistance(totalWindowWidth, distanceUnit)}, obstacle area {SandboxDistanceUnitUtility.FormatArea(totalObstacleArea, distanceUnit)}.";
            RaiseChanged();
            return lastSelectionReadout;
        }

        private void HandleSelectionChanged(IReadOnlyList<string> selection)
        {
            RefreshSelectionReadout();
        }

        private void HandleFloorChanged(FloorData floor)
        {
            RefreshSelectionReadout();
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            RefreshSelectionReadout();
        }

        private static string BuildDistanceReadout(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            return $"Measured {delta.magnitude:0.###}.";
        }

        private string BuildDistanceReadoutWithUnit(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            var formattedDistance = SandboxDistanceUnitUtility.FormatDistance(delta.magnitude, distanceUnit, "0.###");
            var formattedX = delta.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var formattedY = delta.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return $"Measured {formattedDistance} ({formattedX}, {formattedY}).";
        }

        private static void AddRotatedRectPoints(ICollection<Vector2> points, Vector2 center, Vector2 size, float rotationDegrees)
        {
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            points.Add(center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f)));
            points.Add(center + (Vector2)(rotation * new Vector3(-half.x, half.y, 0f)));
            points.Add(center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f)));
            points.Add(center + (Vector2)(rotation * new Vector3(half.x, -half.y, 0f)));
        }

        private void RaiseChanged()
        {
            MeasurementsChanged?.Invoke();
        }
    }
}
