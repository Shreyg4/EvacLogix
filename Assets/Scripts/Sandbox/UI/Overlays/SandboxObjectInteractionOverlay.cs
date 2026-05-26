using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxObjectInteractionOverlay : MonoBehaviour
    {
        private const float WallHitPadding = 0.3f;
        private const float OpeningHitRadius = 0.5f;
        private const float StairHitRadius = 0.55f;
        private const float RegionEdgeHitRadius = 0.35f;
        private const float SelectionDragThreshold = 0.2f;

        private enum SandboxHitKind
        {
            None = 0,
            Wall = 1,
            Door = 2,
            Window = 3,
            Exit = 4,
            Obstacle = 5,
            Stair = 6,
            Region = 7,
        }

        private struct SandboxHitResult
        {
            public SandboxHitKind kind;
            public string objectId;
            public string label;
            public float score;
        }

        private SandboxToolStateService toolStateService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxClipboardService clipboardService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxMeasurementService measurementService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;
        private bool hasPendingSelectPress;
        private bool pendingAdditiveSelect;
        private SandboxHitResult pendingPressedHit;
        private Vector2 pendingPressWorldPoint;
        private bool isSelectionDragActive;
        private string draggedObjectId = string.Empty;
        private SandboxHitKind draggedHitKind = SandboxHitKind.None;
        private Vector2 selectionDragStartWorldPoint;
        private Vector2 selectionDragCurrentWorldPoint;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            clipboardService = FindAnyObjectByType<SandboxClipboardService>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            measurementService = FindAnyObjectByType<SandboxMeasurementService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
        }

        private void Update()
        {
            if (toolStateService == null || workspaceService?.ActiveFloor == null)
            {
                return;
            }

            if (previewService != null && previewService.IsPreviewModeActive)
            {
                return;
            }

            var currentTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (currentTarget != SandboxInputTarget.World)
            {
                return;
            }

            switch (toolStateService.CurrentToolMode)
            {
                case SandboxToolMode.Select:
                    HandleSelectTool();
                    break;
                case SandboxToolMode.Erase:
                    if (SandboxInputAdapter.GetMouseButtonDown(0))
                    {
                        EraseAtWorldPoint(ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition));
                    }
                    break;
            }
        }

        public bool IsSelectionDragActive => isSelectionDragActive;
        public string DraggedObjectId => draggedObjectId;
        public Vector2 SelectionDragStartWorldPoint => selectionDragStartWorldPoint;
        public Vector2 SelectionDragCurrentWorldPoint => selectionDragCurrentWorldPoint;

        public bool SelectAtWorldPoint(Vector2 worldPoint, bool additive = false)
        {
            if (selectionService == null)
            {
                return false;
            }

            if (!TryHitTestFloorObject(worldPoint, out var hit))
            {
                selectionService.ClearSelection();
                measurementService?.RefreshSelectionReadout();
                UpdateStatus("Cleared selection.");
                return false;
            }

            if (additive && selectionService.MultiSelectEnabled)
            {
                var nextSelection = new List<string>(selectionService.SelectedObjectIds);
                if (nextSelection.Contains(hit.objectId))
                {
                    nextSelection.Remove(hit.objectId);
                }
                else
                {
                    nextSelection.Add(hit.objectId);
                }

                selectionService.ReplaceSelection(nextSelection);
            }
            else
            {
                selectionService.ReplaceSelection(new[] { hit.objectId });
            }

            measurementService?.RefreshSelectionReadout();
            UpdateStatus($"Selected {hit.label}.");
            return true;
        }

        public bool EraseAtWorldPoint(Vector2 worldPoint)
        {
            if (!TryHitTestFloorObject(worldPoint, out var hit))
            {
                UpdateStatus("Nothing to erase at that point.");
                return false;
            }

            var didErase = hit.kind switch
            {
                SandboxHitKind.Wall => wallAuthoringService != null && wallAuthoringService.EraseWall(hit.objectId),
                _ => EraseNonWallObject(hit.objectId),
            };

            if (!didErase)
            {
                UpdateStatus($"Could not erase {hit.label}. It may be locked or unavailable.");
                return false;
            }

            selectionService?.ClearSelection();
            measurementService?.RefreshSelectionReadout();
            UpdateStatus($"Erased {hit.label}.");
            return true;
        }

        public bool BeginSelectionDrag(string objectId, Vector2 worldPoint)
        {
            if (string.IsNullOrWhiteSpace(objectId) ||
                selectionService == null ||
                clipboardService == null ||
                !selectionService.SelectedObjectIds.Contains(objectId) ||
                !TryFindSelectedHit(objectId, out var hit) ||
                !IsMovableHit(hit.kind))
            {
                return false;
            }

            isSelectionDragActive = true;
            draggedObjectId = objectId;
            draggedHitKind = hit.kind;
            selectionDragStartWorldPoint = worldPoint;
            selectionDragCurrentWorldPoint = worldPoint;
            UpdateStatus($"Dragging {hit.label}. Release to move.");
            return true;
        }

        public void UpdateSelectionDragPreview(Vector2 worldPoint)
        {
            if (!isSelectionDragActive)
            {
                return;
            }

            selectionDragCurrentWorldPoint = worldPoint;
        }

        public bool CommitSelectionDrag()
        {
            if (!isSelectionDragActive || clipboardService == null)
            {
                return false;
            }

            var delta = selectionDragCurrentWorldPoint - selectionDragStartWorldPoint;
            var didMove = delta.sqrMagnitude > 0.0001f && clipboardService.MoveSelection(delta);
            var movedObjectId = draggedObjectId;
            ClearSelectionDragState();
            measurementService?.RefreshSelectionReadout();
            UpdateStatus(didMove ? $"Moved selection from {movedObjectId}." : "Selection move cancelled.");
            return didMove;
        }

        public void CancelSelectionDrag()
        {
            if (!isSelectionDragActive)
            {
                return;
            }

            ClearSelectionDragState();
            UpdateStatus("Cancelled selection drag.");
        }

        private void HandleSelectTool()
        {
            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                BeginSelectionPress(worldPoint);
            }

            if (isSelectionDragActive)
            {
                if (SandboxInputAdapter.GetMouseButton(0))
                {
                    UpdateSelectionDragPreview(worldPoint);
                }

                if (SandboxInputAdapter.GetMouseButtonUp(0))
                {
                    CommitSelectionDrag();
                }

                if (SandboxInputAdapter.GetMouseButtonDown(1))
                {
                    CancelSelectionDrag();
                }

                return;
            }

            if (hasPendingSelectPress && SandboxInputAdapter.GetMouseButton(0))
            {
                TryPromotePendingPressToDrag(worldPoint);
            }

            if (hasPendingSelectPress && SandboxInputAdapter.GetMouseButtonUp(0))
            {
                FinalizeSelectionPress();
            }
        }

        private void BeginSelectionPress(Vector2 worldPoint)
        {
            pendingPressedHit = default;
            hasPendingSelectPress = true;
            pendingPressWorldPoint = worldPoint;
            pendingAdditiveSelect = SandboxInputAdapter.GetKey(KeyCode.LeftShift) || SandboxInputAdapter.GetKey(KeyCode.RightShift);
            TryHitTestFloorObject(worldPoint, out pendingPressedHit);
        }

        private void TryPromotePendingPressToDrag(Vector2 worldPoint)
        {
            if (!hasPendingSelectPress ||
                pendingPressedHit.kind == SandboxHitKind.None ||
                !IsMovableHit(pendingPressedHit.kind) ||
                selectionService == null ||
                !selectionService.SelectedObjectIds.Contains(pendingPressedHit.objectId))
            {
                return;
            }

            if (Vector2.Distance(worldPoint, pendingPressWorldPoint) < SelectionDragThreshold)
            {
                return;
            }

            if (BeginSelectionDrag(pendingPressedHit.objectId, pendingPressWorldPoint))
            {
                hasPendingSelectPress = false;
                UpdateSelectionDragPreview(worldPoint);
            }
        }

        private void FinalizeSelectionPress()
        {
            if (!hasPendingSelectPress)
            {
                return;
            }

            hasPendingSelectPress = false;
            if (pendingPressedHit.kind == SandboxHitKind.None)
            {
                selectionService?.ClearSelection();
                measurementService?.RefreshSelectionReadout();
                UpdateStatus("Cleared selection.");
                return;
            }

            if (pendingAdditiveSelect && selectionService != null && selectionService.MultiSelectEnabled)
            {
                var nextSelection = new List<string>(selectionService.SelectedObjectIds);
                if (nextSelection.Contains(pendingPressedHit.objectId))
                {
                    nextSelection.Remove(pendingPressedHit.objectId);
                }
                else
                {
                    nextSelection.Add(pendingPressedHit.objectId);
                }

                selectionService.ReplaceSelection(nextSelection);
            }
            else
            {
                selectionService?.ReplaceSelection(new[] { pendingPressedHit.objectId });
            }

            measurementService?.RefreshSelectionReadout();
            UpdateStatus($"Selected {pendingPressedHit.label}.");
        }

        private bool EraseNonWallObject(string objectId)
        {
            if (selectionService == null || clipboardService == null)
            {
                return false;
            }

            selectionService.ReplaceSelection(new[] { objectId });
            return clipboardService.DeleteSelection();
        }

        private bool TryHitTestFloorObject(Vector2 worldPoint, out SandboxHitResult hit)
        {
            hit = default;
            hit.score = float.MaxValue;

            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return false;
            }

            EvaluateWalls(floor, worldPoint, ref hit);
            EvaluateOpenings(floor, worldPoint, ref hit);
            EvaluateExits(floor, worldPoint, ref hit);
            EvaluateObstacles(floor, worldPoint, ref hit);
            EvaluateStairs(floor, worldPoint, ref hit);
            EvaluateRegions(floor, worldPoint, ref hit);
            return hit.kind != SandboxHitKind.None;
        }

        private bool TryFindSelectedHit(string objectId, out SandboxHitResult hit)
        {
            hit = default;
            var floor = workspaceService?.ActiveFloor;
            if (floor == null || string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            if (floor.doors.Any(candidate => candidate.doorId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Door, objectId = objectId, label = "door" };
                return true;
            }

            if (floor.windows.Any(candidate => candidate.windowId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Window, objectId = objectId, label = "window" };
                return true;
            }

            if (floor.exits.Any(candidate => candidate.exitZoneId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Exit, objectId = objectId, label = "exit zone" };
                return true;
            }

            if (floor.obstacles.Any(candidate => candidate.obstacleId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Obstacle, objectId = objectId, label = "obstacle" };
                return true;
            }

            if (floor.stairPortals.Any(candidate => candidate.stairPortalId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Stair, objectId = objectId, label = "stair portal" };
                return true;
            }

            if (floor.regions.Any(candidate => candidate.regionId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Region, objectId = objectId, label = "region" };
                return true;
            }

            var project = workspaceService?.ActiveProject;
            if (project != null)
            {
                foreach (var layout in project.spawnLayouts)
                {
                    if (layout.spawnPoints.Any(candidate => candidate.floorId == floor.floorId && candidate.spawnPointId == objectId) ||
                        layout.spawnBrushStrokes.Any(candidate => candidate.floorId == floor.floorId && candidate.spawnBrushStrokeId == objectId))
                    {
                        hit = new SandboxHitResult { kind = SandboxHitKind.Region, objectId = objectId, label = "spawn object" };
                        return true;
                    }
                }
            }

            if (floor.wallSegments.Any(candidate => candidate.wallSegmentId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Wall, objectId = objectId, label = "wall segment" };
                return true;
            }

            return false;
        }

        private static bool IsMovableHit(SandboxHitKind hitKind)
        {
            return hitKind != SandboxHitKind.None && hitKind != SandboxHitKind.Wall;
        }

        private void EvaluateWalls(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var wall in floor.wallSegments)
            {
                if (!IsInteractable(SandboxVisualObjectType.Wall, wall.wallSegmentId))
                {
                    continue;
                }

                var distance = DistanceToSegment(worldPoint, wall.startPoint, wall.endPoint);
                var hitRadius = Mathf.Max(WallHitPadding, (wall.thickness * 0.5f) + WallHitPadding);
                if (distance > hitRadius)
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Wall,
                        objectId = wall.wallSegmentId,
                        label = "wall segment",
                        score = distance + 0.25f
                    });
            }
        }

        private void EvaluateOpenings(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var door in floor.doors)
            {
                if (!IsInteractable(SandboxVisualObjectType.Door, door.doorId) ||
                    !TryBuildOpeningHit(floor, worldPoint, door.wallSegmentId, door.offsetAlongWall, door.width, out var score))
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Door,
                        objectId = door.doorId,
                        label = "door",
                        score = score
                    });
            }

            foreach (var window in floor.windows)
            {
                if (!IsInteractable(SandboxVisualObjectType.Window, window.windowId) ||
                    !TryBuildOpeningHit(floor, worldPoint, window.wallSegmentId, window.offsetAlongWall, window.width, out var score))
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Window,
                        objectId = window.windowId,
                        label = "window",
                        score = score
                    });
            }
        }

        private void EvaluateExits(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var exitZone in floor.exits)
            {
                if (!IsInteractable(SandboxVisualObjectType.Exit, exitZone.exitZoneId))
                {
                    continue;
                }

                if (!TryPointInRotatedRect(worldPoint, exitZone.center, exitZone.size, exitZone.rotationDegrees, out var localPoint))
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Exit,
                        objectId = exitZone.exitZoneId,
                        label = string.IsNullOrWhiteSpace(exitZone.name) ? "exit zone" : $"exit '{exitZone.name}'",
                        score = localPoint.magnitude
                    });
            }
        }

        private void EvaluateObstacles(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var obstacle in floor.obstacles)
            {
                if (!IsInteractable(SandboxVisualObjectType.Obstacle, obstacle.obstacleId))
                {
                    continue;
                }

                if (!TryPointInRotatedRect(worldPoint, obstacle.center, obstacle.size, obstacle.rotationDegrees, out var localPoint))
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Obstacle,
                        objectId = obstacle.obstacleId,
                        label = string.IsNullOrWhiteSpace(obstacle.name) ? "obstacle" : $"obstacle '{obstacle.name}'",
                        score = localPoint.magnitude
                    });
            }
        }

        private void EvaluateStairs(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var stairPortal in floor.stairPortals)
            {
                if (!IsInteractable(SandboxVisualObjectType.Stair, stairPortal.stairPortalId))
                {
                    continue;
                }

                var distance = Vector2.Distance(worldPoint, stairPortal.localPosition);
                if (distance > StairHitRadius)
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Stair,
                        objectId = stairPortal.stairPortalId,
                        label = string.IsNullOrWhiteSpace(stairPortal.name) ? "stair portal" : $"stair '{stairPortal.name}'",
                        score = distance
                    });
            }
        }

        private void EvaluateRegions(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var region in floor.regions)
            {
                if (!IsInteractable(SandboxVisualObjectType.Region, region.regionId) || region.polygonPoints.Count < 2)
                {
                    continue;
                }

                var isInside = IsPointInsidePolygon(worldPoint, region.polygonPoints);
                var edgeDistance = DistanceToPolygonEdges(worldPoint, region.polygonPoints);
                if (!isInside && edgeDistance > RegionEdgeHitRadius)
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Region,
                        objectId = region.regionId,
                        label = string.IsNullOrWhiteSpace(region.name) ? "region" : $"region '{region.name}'",
                        score = isInside ? 0.1f : edgeDistance
                    });
            }
        }

        private bool TryBuildOpeningHit(
            FloorData floor,
            Vector2 worldPoint,
            string wallSegmentId,
            float openingOffset,
            float openingWidth,
            out float score)
        {
            score = float.MaxValue;
            var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == wallSegmentId);
            if (wall == null)
            {
                return false;
            }

            var wallVector = wall.endPoint - wall.startPoint;
            var wallLength = wallVector.magnitude;
            if (wallLength <= Mathf.Epsilon)
            {
                return false;
            }

            var wallDirection = wallVector / wallLength;
            var halfWidth = openingWidth * 0.5f;
            var projectionDistance = Vector2.Dot(worldPoint - wall.startPoint, wallDirection);
            if (projectionDistance < openingOffset - halfWidth - OpeningHitRadius || projectionDistance > openingOffset + halfWidth + OpeningHitRadius)
            {
                return false;
            }

            var projection = wall.startPoint + wallDirection * Mathf.Clamp(projectionDistance, 0f, wallLength);
            var perpendicularDistance = Vector2.Distance(worldPoint, projection);
            if (perpendicularDistance > OpeningHitRadius)
            {
                return false;
            }

            score = perpendicularDistance;
            return true;
        }

        private bool IsInteractable(SandboxVisualObjectType objectType, string objectId)
        {
            return visualOrganizationService == null ||
                   (visualOrganizationService.IsTypeVisible(objectType) && !visualOrganizationService.IsObjectHidden(objectId));
        }

        private static void TryPromoteHit(ref SandboxHitResult bestHit, SandboxHitResult candidateHit)
        {
            if (candidateHit.score >= bestHit.score)
            {
                return;
            }

            bestHit = candidateHit;
        }

        private static bool TryPointInRotatedRect(
            Vector2 worldPoint,
            Vector2 center,
            Vector2 size,
            float rotationDegrees,
            out Vector2 localPoint)
        {
            var radians = -rotationDegrees * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(radians);
            var sine = Mathf.Sin(radians);
            var delta = worldPoint - center;
            localPoint = new Vector2(
                (delta.x * cosine) - (delta.y * sine),
                (delta.x * sine) + (delta.y * cosine));

            var halfSize = size * 0.5f;
            return Mathf.Abs(localPoint.x) <= halfSize.x && Mathf.Abs(localPoint.y) <= halfSize.y;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var projected = ProjectPointOntoSegment(point, segmentStart, segmentEnd);
            return Vector2.Distance(point, projected);
        }

        private static Vector2 ProjectPointOntoSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return segmentStart;
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared);
            return segmentStart + (segment * t);
        }

        private static bool IsPointInsidePolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            var isInside = false;
            for (var i = 0; i < polygon.Count; i += 1)
            {
                var j = (i + polygon.Count - 1) % polygon.Count;
                var pointI = polygon[i];
                var pointJ = polygon[j];
                var denominator = pointJ.y - pointI.y;
                if (Mathf.Abs(denominator) <= 0.0001f)
                {
                    continue;
                }

                var intersects = ((pointI.y > point.y) != (pointJ.y > point.y)) &&
                                 (point.x < ((pointJ.x - pointI.x) * (point.y - pointI.y) / denominator) + pointI.x);
                if (intersects)
                {
                    isInside = !isInside;
                }
            }

            return isInside;
        }

        private static float DistanceToPolygonEdges(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            var bestDistance = float.MaxValue;
            for (var i = 0; i < polygon.Count; i += 1)
            {
                var start = polygon[i];
                var end = polygon[(i + 1) % polygon.Count];
                bestDistance = Mathf.Min(bestDistance, DistanceToSegment(point, start, end));
            }

            return bestDistance;
        }

        private static Vector2 ScreenToWorldPoint(Vector3 screenPoint)
        {
            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return Vector2.zero;
            }

            screenPoint.z = Mathf.Abs(cameraComponent.transform.position.z);
            var worldPoint = cameraComponent.ScreenToWorldPoint(screenPoint);
            return new Vector2(worldPoint.x, worldPoint.y);
        }

        private void UpdateStatus(string message)
        {
            if (statusBar != null)
            {
                statusBar.StatusMessage = message;
            }
        }

        private void ClearSelectionDragState()
        {
            isSelectionDragActive = false;
            draggedObjectId = string.Empty;
            draggedHitKind = SandboxHitKind.None;
            selectionDragStartWorldPoint = Vector2.zero;
            selectionDragCurrentWorldPoint = Vector2.zero;
        }
    }
}
