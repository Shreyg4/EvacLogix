using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring
{
    public sealed class SandboxWallAuthoringService : MonoBehaviour
    {
        [SerializeField] private float defaultWallThickness = 0.2f;
        [SerializeField] private float brushPointReductionDistance = 0.25f;
        [SerializeField] private int brushSmoothingWindow = 1;
        [SerializeField] private float nearJoinCleanupDistance = 0.3f;
        [SerializeField] private float mergeAngleToleranceDegrees = 10f;
        [SerializeField] private bool hasPendingLineStart;
        [SerializeField] private Vector2 pendingLineStart;
        [SerializeField] private bool isBrushCaptureActive;
        [SerializeField] private List<Vector2> activeBrushStrokePoints = new();
        [SerializeField] private List<Vector2> lastCleanedBrushStrokePoints = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxSelectionService selectionService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxWallSnappingService wallSnappingService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;

        public event Action PreviewStateChanged;
        public event Action TopologyChanged;

        public float DefaultWallThickness => defaultWallThickness;
        public float BrushPointReductionDistance => brushPointReductionDistance;
        public int BrushSmoothingWindow => brushSmoothingWindow;
        public float NearJoinCleanupDistance => nearJoinCleanupDistance;
        public bool HasPendingLineStart => hasPendingLineStart;
        public Vector2 PendingLineStart => pendingLineStart;
        public bool IsBrushCaptureActive => isBrushCaptureActive;
        public IReadOnlyList<Vector2> ActiveBrushStrokePoints => activeBrushStrokePoints;
        public IReadOnlyList<Vector2> LastCleanedBrushStrokePoints => lastCleanedBrushStrokePoints;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            selectionService = GetComponent<SandboxSelectionService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            wallSnappingService = GetComponent<SandboxWallSnappingService>();
            visualOrganizationService = GetComponent<SandboxVisualOrganizationService>();
            previewService = GetComponent<SandboxPreviewService>();
        }

        public void SetBrushCleanupSettings(int smoothingWindow, float pointReductionDistance, float nearJoinDistance)
        {
            brushSmoothingWindow = Mathf.Max(0, smoothingWindow);
            brushPointReductionDistance = Mathf.Max(0.01f, pointReductionDistance);
            nearJoinCleanupDistance = Mathf.Max(0.01f, nearJoinDistance);
            RaisePreviewStateChanged();
        }

        public bool TryRegisterLinePoint(Vector2 worldPoint, out string createdWallSegmentId, float thickness = -1f)
        {
            createdWallSegmentId = string.Empty;
            var activeFloor = workspaceService?.ActiveFloor;
            if (activeFloor == null || IsEditingBlocked())
            {
                return false;
            }

            if (IsWallTypeLocked())
            {
                return false;
            }

            if (!hasPendingLineStart)
            {
                pendingLineStart = SnapPoint(activeFloor.floorId, worldPoint, null);
                hasPendingLineStart = true;
                RaisePreviewStateChanged();
                return false;
            }

            var startPoint = pendingLineStart;
            var endPoint = SnapPoint(activeFloor.floorId, worldPoint, startPoint);
            hasPendingLineStart = false;
            pendingLineStart = Vector2.zero;
            RaisePreviewStateChanged();

            createdWallSegmentId = SandboxId.NewId();
            var didCreate = CreateLineWallInternal(
                startPoint,
                endPoint,
                ResolveThickness(thickness),
                createdWallSegmentId,
                SandboxId.NewId(),
                SandboxId.NewId());

            if (!didCreate)
            {
                createdWallSegmentId = string.Empty;
            }

            return didCreate;
        }

        public void CancelLinePlacement()
        {
            if (!hasPendingLineStart)
            {
                return;
            }

            hasPendingLineStart = false;
            pendingLineStart = Vector2.zero;
            RaisePreviewStateChanged();
        }

        public bool CreateLineWall(Vector2 startPoint, Vector2 endPoint, float thickness = -1f)
        {
            if (IsWallTypeLocked() || IsEditingBlocked())
            {
                return false;
            }

            return CreateLineWallInternal(
                SnapPoint(workspaceService?.ActiveFloorId, startPoint, null),
                SnapPoint(workspaceService?.ActiveFloorId, endPoint, startPoint),
                ResolveThickness(thickness),
                SandboxId.NewId(),
                SandboxId.NewId(),
                SandboxId.NewId());
        }

        public bool BeginBrushStrokeCapture(Vector2 worldPoint)
        {
            if (workspaceService?.ActiveFloor == null || IsWallTypeLocked() || IsEditingBlocked())
            {
                return false;
            }

            activeBrushStrokePoints = new List<Vector2> { worldPoint };
            lastCleanedBrushStrokePoints = new List<Vector2>();
            isBrushCaptureActive = true;
            RaisePreviewStateChanged();
            return true;
        }

        public bool AppendBrushStrokePoint(Vector2 worldPoint)
        {
            if (!isBrushCaptureActive || IsEditingBlocked())
            {
                return false;
            }

            if (activeBrushStrokePoints.Count > 0 &&
                Vector2.Distance(activeBrushStrokePoints[^1], worldPoint) <= 0.01f)
            {
                return false;
            }

            activeBrushStrokePoints.Add(worldPoint);
            RaisePreviewStateChanged();
            return true;
        }

        public void EndBrushStrokeCapture()
        {
            if (!isBrushCaptureActive)
            {
                return;
            }

            isBrushCaptureActive = false;
            lastCleanedBrushStrokePoints = CleanupBrushStroke(activeBrushStrokePoints);
            RaisePreviewStateChanged();
        }

        public void CancelBrushStroke()
        {
            isBrushCaptureActive = false;
            activeBrushStrokePoints = new List<Vector2>();
            lastCleanedBrushStrokePoints = new List<Vector2>();
            RaisePreviewStateChanged();
        }

        public bool AcceptActiveBrushStroke(float thickness = -1f)
        {
            if (workspaceService?.ActiveFloor == null || IsEditingBlocked())
            {
                return false;
            }

            if (IsWallTypeLocked())
            {
                return false;
            }

            if (activeBrushStrokePoints.Count < 2)
            {
                return false;
            }

            var cleanedPoints = CleanupBrushStroke(activeBrushStrokePoints);
            if (cleanedPoints.Count < 2)
            {
                return false;
            }

            var createdWallIds = new List<string>();
            var floorId = workspaceService.ActiveFloor.floorId;
            var requestedThickness = ResolveThickness(thickness);

            var didApply = ExecuteProjectMutation(
                "Accept Brush Walls",
                (project, floor) =>
                {
                    if (!string.Equals(floor.floorId, floorId, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    for (var i = 0; i < cleanedPoints.Count - 1; i += 1)
                    {
                        var startPoint = ConformBrushPoint(floorId, cleanedPoints[i], i == 0 ? null : cleanedPoints[i - 1]);
                        var endPoint = ConformBrushPoint(floorId, cleanedPoints[i + 1], startPoint);
                        if (Vector2.Distance(startPoint, endPoint) <= 0.02f)
                        {
                            continue;
                        }

                        var wallId = SandboxId.NewId();
                        var startJunctionId = SandboxId.NewId();
                        var endJunctionId = SandboxId.NewId();
                        if (!AddWallSegment(floor, wallId, startJunctionId, endJunctionId, startPoint, endPoint, requestedThickness, wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance))
                        {
                            continue;
                        }

                        createdWallIds.Add(wallId);
                    }

                    return createdWallIds.Count > 0;
                },
                createdWallIds);

            if (didApply)
            {
                activeBrushStrokePoints = new List<Vector2>();
                lastCleanedBrushStrokePoints = cleanedPoints;
                isBrushCaptureActive = false;
                RaisePreviewStateChanged();
            }

            return didApply;
        }

        public bool MoveWallStartHandle(string wallSegmentId, Vector2 newStartPoint)
        {
            return MoveWallEndpoint(wallSegmentId, true, newStartPoint);
        }

        public bool MoveWallEndHandle(string wallSegmentId, Vector2 newEndPoint)
        {
            return MoveWallEndpoint(wallSegmentId, false, newEndPoint);
        }

        public bool SetWallThickness(string wallSegmentId, float thickness)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId) || thickness <= 0f)
            {
                return false;
            }

            if (IsWallLocked(wallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Set Wall Thickness",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    wall.thickness = thickness;
                    return true;
                },
                new[] { wallSegmentId });
        }

        public bool SetWallEndpoints(string wallSegmentId, Vector2 startPoint, Vector2 endPoint)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            if (IsWallLocked(wallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Set Wall Endpoints",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    var snappedStart = SnapPoint(floor.floorId, startPoint, null);
                    var snappedEnd = SnapPoint(floor.floorId, endPoint, snappedStart);
                    if (Vector2.Distance(snappedStart, snappedEnd) <= 0.02f)
                    {
                        return false;
                    }

                    if (!UpdateWallEndpointPosition(floor, wall, true, snappedStart, wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance))
                    {
                        return false;
                    }

                    return UpdateWallEndpointPosition(floor, wall, false, snappedEnd, wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance);
                },
                new[] { wallSegmentId });
        }

        public bool SplitWall(string wallSegmentId, Vector2 splitPoint)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            if (IsWallLocked(wallSegmentId))
            {
                return false;
            }

            var newWallId = SandboxId.NewId();
            var newJunctionId = SandboxId.NewId();
            return ExecuteProjectMutation(
                "Split Wall",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    var projectedPoint = ProjectPointOntoSegment(splitPoint, wall.startPoint, wall.endPoint);
                    if (Vector2.Distance(projectedPoint, wall.startPoint) <= 0.05f ||
                        Vector2.Distance(projectedPoint, wall.endPoint) <= 0.05f)
                    {
                        return false;
                    }

                    var originalEndJunctionId = wall.endJunctionId;
                    var originalEndPoint = wall.endPoint;
                    var originalEndJunction = FindJunction(floor, originalEndJunctionId);
                    var midJunction = FindOrCreateJunction(
                        floor,
                        projectedPoint,
                        wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance,
                        newJunctionId,
                        wall.startJunctionId,
                        originalEndJunctionId);

                    if (midJunction == null)
                    {
                        return false;
                    }

                    if (originalEndJunction != null)
                    {
                        RemoveConnection(originalEndJunction, wall.wallSegmentId);
                    }

                    wall.endPoint = midJunction.position;
                    wall.endJunctionId = midJunction.wallJunctionId;
                    AddConnection(midJunction, wall.wallSegmentId);

                    if (originalEndJunction != null)
                    {
                        AddConnection(originalEndJunction, newWallId);
                    }

                    var newWall = new WallSegmentData
                    {
                        wallSegmentId = newWallId,
                        startJunctionId = midJunction.wallJunctionId,
                        endJunctionId = originalEndJunctionId,
                        startPoint = midJunction.position,
                        endPoint = originalEndPoint,
                        thickness = wall.thickness
                    };

                    midJunction.connectedWallSegmentIds.Add(newWallId);
                    floor.wallSegments.Add(newWall);
                    return true;
                },
                new[] { wallSegmentId, newWallId });
        }

        public bool MergeWalls(string firstWallSegmentId, string secondWallSegmentId)
        {
            if (string.IsNullOrWhiteSpace(firstWallSegmentId) || string.IsNullOrWhiteSpace(secondWallSegmentId))
            {
                return false;
            }

            if (IsWallLocked(firstWallSegmentId) || IsWallLocked(secondWallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Merge Walls",
                (_, floor) =>
                {
                    var firstWall = FindWallSegment(floor, firstWallSegmentId);
                    var secondWall = FindWallSegment(floor, secondWallSegmentId);
                    if (firstWall == null || secondWall == null)
                    {
                        return false;
                    }

                    var sharedJunctionId = FindSharedJunctionId(firstWall, secondWall);
                    if (string.IsNullOrWhiteSpace(sharedJunctionId))
                    {
                        return false;
                    }

                    var firstFarPoint = GetFarEndpoint(firstWall, sharedJunctionId);
                    var secondFarPoint = GetFarEndpoint(secondWall, sharedJunctionId);
                    var sharedPoint = FindJunction(floor, sharedJunctionId)?.position ?? firstFarPoint;
                    var firstDirection = (firstFarPoint - sharedPoint).normalized;
                    var secondDirection = (secondFarPoint - sharedPoint).normalized;
                    var angle = Vector2.Angle(firstDirection, secondDirection);
                    if (Mathf.Abs(180f - angle) > mergeAngleToleranceDegrees)
                    {
                        return false;
                    }

                    var mergedStartJunctionId = GetFarJunctionId(firstWall, sharedJunctionId);
                    var mergedEndJunctionId = GetFarJunctionId(secondWall, sharedJunctionId);
                    if (string.Equals(mergedStartJunctionId, mergedEndJunctionId, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var mergedStartJunction = FindJunction(floor, mergedStartJunctionId);
                    var mergedEndJunction = FindJunction(floor, mergedEndJunctionId);
                    var sharedJunction = FindJunction(floor, sharedJunctionId);
                    if (mergedStartJunction == null || mergedEndJunction == null || sharedJunction == null)
                    {
                        return false;
                    }

                    RemoveConnection(mergedStartJunction, firstWall.wallSegmentId);
                    RemoveConnection(mergedEndJunction, secondWall.wallSegmentId);
                    RemoveConnection(sharedJunction, firstWall.wallSegmentId);
                    RemoveConnection(sharedJunction, secondWall.wallSegmentId);

                    firstWall.startJunctionId = mergedStartJunctionId;
                    firstWall.endJunctionId = mergedEndJunctionId;
                    firstWall.startPoint = mergedStartJunction.position;
                    firstWall.endPoint = mergedEndJunction.position;
                    firstWall.thickness = Mathf.Max(firstWall.thickness, secondWall.thickness);

                    AddConnection(mergedStartJunction, firstWall.wallSegmentId);
                    AddConnection(mergedEndJunction, firstWall.wallSegmentId);

                    floor.wallSegments.Remove(secondWall);
                    PruneJunctionIfOrphan(floor, sharedJunctionId);
                    return true;
                },
                new[] { firstWallSegmentId });
        }

        public bool TrimWallStart(string wallSegmentId, Vector2 trimPoint)
        {
            return TrimWall(wallSegmentId, trimPoint, true);
        }

        public bool TrimWallEnd(string wallSegmentId, Vector2 trimPoint)
        {
            return TrimWall(wallSegmentId, trimPoint, false);
        }

        public bool EraseWall(string wallSegmentId)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            if (IsWallLocked(wallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Erase Wall",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    var startJunctionId = wall.startJunctionId;
                    var endJunctionId = wall.endJunctionId;
                    var startJunction = FindJunction(floor, startJunctionId);
                    var endJunction = FindJunction(floor, endJunctionId);

                    if (startJunction != null)
                    {
                        RemoveConnection(startJunction, wall.wallSegmentId);
                    }

                    if (endJunction != null)
                    {
                        RemoveConnection(endJunction, wall.wallSegmentId);
                    }

                    floor.wallSegments.Remove(wall);
                    PruneJunctionIfOrphan(floor, startJunctionId);
                    PruneJunctionIfOrphan(floor, endJunctionId);
                    return true;
                },
                Array.Empty<string>());
        }

        private bool CreateLineWallInternal(
            Vector2 startPoint,
            Vector2 endPoint,
            float thickness,
            string wallSegmentId,
            string startJunctionId,
            string endJunctionId)
        {
            return ExecuteProjectMutation(
                "Create Line Wall",
                (_, floor) => AddWallSegment(
                    floor,
                    wallSegmentId,
                    startJunctionId,
                    endJunctionId,
                    startPoint,
                    endPoint,
                    thickness,
                    wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance),
                new[] { wallSegmentId });
        }

        private bool MoveWallEndpoint(string wallSegmentId, bool isStartPoint, Vector2 targetPoint)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            if (IsWallLocked(wallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                isStartPoint ? "Move Wall Start Handle" : "Move Wall End Handle",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    var anchorPoint = isStartPoint ? wall.endPoint : wall.startPoint;
                    var snappedPoint = SnapPoint(floor.floorId, targetPoint, anchorPoint);
                    return UpdateWallEndpointPosition(
                        floor,
                        wall,
                        isStartPoint,
                        snappedPoint,
                        wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance);
                },
                new[] { wallSegmentId });
        }

        private bool TrimWall(string wallSegmentId, Vector2 trimPoint, bool trimStart)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                trimStart ? "Trim Wall Start" : "Trim Wall End",
                (_, floor) =>
                {
                    var wall = FindWallSegment(floor, wallSegmentId);
                    if (wall == null)
                    {
                        return false;
                    }

                    var projectedPoint = ProjectPointOntoSegment(trimPoint, wall.startPoint, wall.endPoint);
                    return UpdateWallEndpointPosition(
                        floor,
                        wall,
                        trimStart,
                        projectedPoint,
                        wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance);
                },
                new[] { wallSegmentId });
        }

        private bool ExecuteProjectMutation(
            string description,
            Func<BuildingProjectData, FloorData, bool> mutation,
            IReadOnlyList<string> nextSelection)
        {
            if (workspaceService?.ActiveProject == null ||
                workspaceService.ActiveFloor == null ||
                mutation == null ||
                IsEditingBlocked())
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var beforeProject = CloneProject(workspaceService.ActiveProject);
            var afterProject = CloneProject(workspaceService.ActiveProject);
            var beforeSelection = selectionService != null
                ? new List<string>(selectionService.SelectedObjectIds)
                : new List<string>();

            var targetFloor = afterProject.floors.FirstOrDefault(floor =>
                string.Equals(floor.floorId, activeFloorId, StringComparison.Ordinal));
            if (targetFloor == null || !mutation(afterProject, targetFloor))
            {
                return false;
            }

            SandboxProjectDataUtility.EnsureIds(afterProject);

            void ApplyAfter()
            {
                ApplyProjectState(CloneProject(afterProject), activeFloorId, nextSelection);
            }

            void ApplyBefore()
            {
                ApplyProjectState(CloneProject(beforeProject), activeFloorId, beforeSelection);
            }

            if (commandHistory == null)
            {
                ApplyAfter();
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(description, ApplyAfter, ApplyBefore));
            return true;
        }

        private void ApplyProjectState(BuildingProjectData project, string activeFloorId, IReadOnlyList<string> selection)
        {
            workspaceService.SetActiveProject(project);
            if (!string.IsNullOrWhiteSpace(activeFloorId))
            {
                workspaceService.SetActiveFloor(activeFloorId);
            }

            if (selectionService != null && selection != null)
            {
                selectionService.ReplaceSelection(selection);
            }

            colliderRebuildService?.RequestRebuild(activeFloorId);
            TopologyChanged?.Invoke();
        }

        private Vector2 ConformBrushPoint(string floorId, Vector2 point, Vector2? anchorPoint)
        {
            var snappedPoint = SnapPoint(floorId, point, anchorPoint);
            var floor = workspaceService?.FindFloor(floorId);
            if (floor == null)
            {
                return snappedPoint;
            }

            var nearbyJunction = floor.wallJunctions
                .Where(junction => Vector2.Distance(junction.position, snappedPoint) <= nearJoinCleanupDistance)
                .OrderBy(junction => Vector2.Distance(junction.position, snappedPoint))
                .FirstOrDefault();

            return nearbyJunction != null ? nearbyJunction.position : snappedPoint;
        }

        private List<Vector2> CleanupBrushStroke(IReadOnlyList<Vector2> sourcePoints)
        {
            var reducedPoints = ReduceBrushPoints(sourcePoints);
            if (reducedPoints.Count <= 2 || brushSmoothingWindow <= 0)
            {
                return reducedPoints;
            }

            var smoothedPoints = new List<Vector2>(reducedPoints.Count) { reducedPoints[0] };
            for (var i = 1; i < reducedPoints.Count - 1; i += 1)
            {
                var minIndex = Mathf.Max(0, i - brushSmoothingWindow);
                var maxIndex = Mathf.Min(reducedPoints.Count - 1, i + brushSmoothingWindow);
                var total = Vector2.zero;
                var sampleCount = 0;

                for (var sampleIndex = minIndex; sampleIndex <= maxIndex; sampleIndex += 1)
                {
                    total += reducedPoints[sampleIndex];
                    sampleCount += 1;
                }

                smoothedPoints.Add(total / Mathf.Max(1, sampleCount));
            }

            smoothedPoints.Add(reducedPoints[^1]);
            return ReduceBrushPoints(smoothedPoints);
        }

        private List<Vector2> ReduceBrushPoints(IReadOnlyList<Vector2> sourcePoints)
        {
            if (sourcePoints == null || sourcePoints.Count == 0)
            {
                return new List<Vector2>();
            }

            var reducedPoints = new List<Vector2> { sourcePoints[0] };
            for (var i = 1; i < sourcePoints.Count; i += 1)
            {
                if (Vector2.Distance(reducedPoints[^1], sourcePoints[i]) >= brushPointReductionDistance)
                {
                    reducedPoints.Add(sourcePoints[i]);
                }
            }

            if (reducedPoints.Count == 1 || reducedPoints[^1] != sourcePoints[^1])
            {
                reducedPoints.Add(sourcePoints[^1]);
            }

            return reducedPoints;
        }

        private Vector2 SnapPoint(string floorId, Vector2 rawPoint, Vector2? anchorPoint)
        {
            if (wallSnappingService == null || string.IsNullOrWhiteSpace(floorId))
            {
                return rawPoint;
            }

            return wallSnappingService.SnapPoint(floorId, rawPoint, anchorPoint).position;
        }

        private float ResolveThickness(float thickness)
        {
            return thickness > 0f ? thickness : defaultWallThickness;
        }

        private static bool AddWallSegment(
            FloorData floor,
            string wallSegmentId,
            string startJunctionId,
            string endJunctionId,
            Vector2 startPoint,
            Vector2 endPoint,
            float thickness,
            float junctionReuseDistance)
        {
            if (floor == null || string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            if (Vector2.Distance(startPoint, endPoint) <= 0.02f)
            {
                return false;
            }

            var startJunction = FindOrCreateJunction(floor, startPoint, junctionReuseDistance, startJunctionId, endJunctionId);
            var endJunction = FindOrCreateJunction(floor, endPoint, junctionReuseDistance, endJunctionId, startJunction.wallJunctionId);
            if (startJunction == null || endJunction == null ||
                string.Equals(startJunction.wallJunctionId, endJunction.wallJunctionId, StringComparison.Ordinal))
            {
                return false;
            }

            var wall = new WallSegmentData
            {
                wallSegmentId = wallSegmentId,
                startJunctionId = startJunction.wallJunctionId,
                endJunctionId = endJunction.wallJunctionId,
                startPoint = startJunction.position,
                endPoint = endJunction.position,
                thickness = thickness
            };

            AddConnection(startJunction, wall.wallSegmentId);
            AddConnection(endJunction, wall.wallSegmentId);
            floor.wallSegments.Add(wall);
            return true;
        }

        private static bool UpdateWallEndpointPosition(
            FloorData floor,
            WallSegmentData wall,
            bool updateStartPoint,
            Vector2 targetPoint,
            float junctionReuseDistance)
        {
            if (floor == null || wall == null)
            {
                return false;
            }

            var otherPoint = updateStartPoint ? wall.endPoint : wall.startPoint;
            var otherJunctionId = updateStartPoint ? wall.endJunctionId : wall.startJunctionId;
            if (Vector2.Distance(targetPoint, otherPoint) <= 0.02f)
            {
                return false;
            }

            var currentJunctionId = updateStartPoint ? wall.startJunctionId : wall.endJunctionId;
            var currentJunction = FindJunction(floor, currentJunctionId);
            var targetJunction = FindReusableJunction(floor, targetPoint, junctionReuseDistance, currentJunctionId, otherJunctionId);

            if (targetJunction == null && currentJunction != null && currentJunction.connectedWallSegmentIds.Count <= 1)
            {
                currentJunction.position = targetPoint;
                targetJunction = currentJunction;
            }

            if (targetJunction == null)
            {
                targetJunction = new WallJunctionData
                {
                    wallJunctionId = SandboxId.NewId(),
                    position = targetPoint
                };
                floor.wallJunctions.Add(targetJunction);
            }

            if (currentJunction != null && !string.Equals(currentJunction.wallJunctionId, targetJunction.wallJunctionId, StringComparison.Ordinal))
            {
                RemoveConnection(currentJunction, wall.wallSegmentId);
                PruneJunctionIfOrphan(floor, currentJunction.wallJunctionId);
            }

            AddConnection(targetJunction, wall.wallSegmentId);

            if (updateStartPoint)
            {
                wall.startJunctionId = targetJunction.wallJunctionId;
                wall.startPoint = targetJunction.position;
            }
            else
            {
                wall.endJunctionId = targetJunction.wallJunctionId;
                wall.endPoint = targetJunction.position;
            }

            return !string.Equals(wall.startJunctionId, wall.endJunctionId, StringComparison.Ordinal);
        }

        private static WallSegmentData FindWallSegment(FloorData floor, string wallSegmentId)
        {
            return floor?.wallSegments.FirstOrDefault(wall =>
                string.Equals(wall.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
        }

        private static WallJunctionData FindJunction(FloorData floor, string wallJunctionId)
        {
            return floor?.wallJunctions.FirstOrDefault(junction =>
                string.Equals(junction.wallJunctionId, wallJunctionId, StringComparison.Ordinal));
        }

        private static WallJunctionData FindOrCreateJunction(
            FloorData floor,
            Vector2 position,
            float reuseDistance,
            string preferredJunctionId,
            params string[] excludedJunctionIds)
        {
            var existingJunction = FindReusableJunction(floor, position, reuseDistance, excludedJunctionIds);
            if (existingJunction != null)
            {
                return existingJunction;
            }

            var junction = new WallJunctionData
            {
                wallJunctionId = preferredJunctionId,
                position = position
            };

            floor.wallJunctions.Add(junction);
            return junction;
        }

        private static WallJunctionData FindReusableJunction(
            FloorData floor,
            Vector2 position,
            float reuseDistance,
            params string[] excludedJunctionIds)
        {
            var excluded = new HashSet<string>(excludedJunctionIds ?? Array.Empty<string>());
            return floor?.wallJunctions
                .Where(junction => !excluded.Contains(junction.wallJunctionId))
                .Where(junction => Vector2.Distance(junction.position, position) <= reuseDistance)
                .OrderBy(junction => Vector2.Distance(junction.position, position))
                .FirstOrDefault();
        }

        private static void AddConnection(WallJunctionData junction, string wallSegmentId)
        {
            if (junction == null || string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return;
            }

            if (!junction.connectedWallSegmentIds.Contains(wallSegmentId))
            {
                junction.connectedWallSegmentIds.Add(wallSegmentId);
            }
        }

        private static void RemoveConnection(WallJunctionData junction, string wallSegmentId)
        {
            junction?.connectedWallSegmentIds.RemoveAll(id => string.Equals(id, wallSegmentId, StringComparison.Ordinal));
        }

        private static void PruneJunctionIfOrphan(FloorData floor, string wallJunctionId)
        {
            var junction = FindJunction(floor, wallJunctionId);
            if (junction != null && junction.connectedWallSegmentIds.Count == 0)
            {
                floor.wallJunctions.Remove(junction);
            }
        }

        private static string FindSharedJunctionId(WallSegmentData firstWall, WallSegmentData secondWall)
        {
            if (firstWall == null || secondWall == null)
            {
                return string.Empty;
            }

            if (string.Equals(firstWall.startJunctionId, secondWall.startJunctionId, StringComparison.Ordinal) ||
                string.Equals(firstWall.startJunctionId, secondWall.endJunctionId, StringComparison.Ordinal))
            {
                return firstWall.startJunctionId;
            }

            if (string.Equals(firstWall.endJunctionId, secondWall.startJunctionId, StringComparison.Ordinal) ||
                string.Equals(firstWall.endJunctionId, secondWall.endJunctionId, StringComparison.Ordinal))
            {
                return firstWall.endJunctionId;
            }

            return string.Empty;
        }

        private static Vector2 GetFarEndpoint(WallSegmentData wall, string sharedJunctionId)
        {
            return string.Equals(wall.startJunctionId, sharedJunctionId, StringComparison.Ordinal)
                ? wall.endPoint
                : wall.startPoint;
        }

        private static string GetFarJunctionId(WallSegmentData wall, string sharedJunctionId)
        {
            return string.Equals(wall.startJunctionId, sharedJunctionId, StringComparison.Ordinal)
                ? wall.endJunctionId
                : wall.startJunctionId;
        }

        private static Vector2 ProjectPointOntoSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return segmentStart;
            }

            var t = Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Mathf.Clamp01(t);
            return segmentStart + segment * t;
        }

        private static BuildingProjectData CloneProject(BuildingProjectData project)
        {
            var serialized = SandboxProjectSerializer.Serialize(project, false);
            return SandboxProjectSerializer.Deserialize(serialized);
        }

        private bool IsWallTypeLocked()
        {
            return visualOrganizationService != null && visualOrganizationService.IsTypeLocked(SandboxVisualObjectType.Wall);
        }

        private bool IsEditingBlocked()
        {
            return previewService != null && previewService.IsPreviewModeActive;
        }

        private bool IsWallLocked(string wallSegmentId)
        {
            return IsWallTypeLocked() ||
                   (visualOrganizationService != null && visualOrganizationService.IsObjectLocked(wallSegmentId));
        }

        private void RaisePreviewStateChanged()
        {
            PreviewStateChanged?.Invoke();
        }
    }
}
