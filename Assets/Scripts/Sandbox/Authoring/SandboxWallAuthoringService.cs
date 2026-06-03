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
        private const float OpeningEndMargin = 0.05f;

        [SerializeField] private float defaultWallThickness = 0.2f;
        [SerializeField] private float brushPointReductionDistance = 0.25f;
        [SerializeField] private int brushSmoothingWindow = 1;
        [SerializeField] private float nearJoinCleanupDistance = 0.3f;
        [SerializeField] private float topologyNormalizationJunctionTolerance = 0.08f;
        [SerializeField] private float mergeAngleToleranceDegrees = 10f;
        [SerializeField] private bool hasPendingLineStart;
        [SerializeField] private Vector2 pendingLineStart;
        [SerializeField] private SandboxWallSnapTargetKind pendingLineStartTargetKind;
        [SerializeField] private string pendingLineStartReferenceId = string.Empty;
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
        private SandboxWorkspaceStateService workspaceStateService;

        public event Action PreviewStateChanged;
        public event Action TopologyChanged;

        public float DefaultWallThickness => defaultWallThickness;
        public float BrushPointReductionDistance => brushPointReductionDistance;
        public int BrushSmoothingWindow => brushSmoothingWindow;
        public float NearJoinCleanupDistance => nearJoinCleanupDistance;
        public float TopologyNormalizationJunctionTolerance => topologyNormalizationJunctionTolerance;
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
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
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
                var firstPointSnapResult = ResolveSnapResult(activeFloor.floorId, worldPoint, null);
                pendingLineStart = firstPointSnapResult.position;
                pendingLineStartTargetKind = firstPointSnapResult.targetKind;
                pendingLineStartReferenceId = firstPointSnapResult.referenceId;
                hasPendingLineStart = true;
                RaisePreviewStateChanged();
                return false;
            }

            var startSnapResult = new SandboxWallSnapResult(
                pendingLineStart,
                pendingLineStartTargetKind,
                pendingLineStartReferenceId);
            var endSnapResult = ResolveSnapResult(activeFloor.floorId, worldPoint, startSnapResult.position);

            createdWallSegmentId = SandboxId.NewId();
            var didCreate = CreateLineWallInternal(
                startSnapResult,
                endSnapResult,
                ResolveThickness(thickness),
                createdWallSegmentId,
                SandboxId.NewId(),
                SandboxId.NewId());

            if (didCreate)
            {
                pendingLineStart = endSnapResult.position;
                pendingLineStartTargetKind = endSnapResult.targetKind;
                pendingLineStartReferenceId = endSnapResult.referenceId;
                hasPendingLineStart = true;
            }
            else
            {
                createdWallSegmentId = string.Empty;
            }

            RaisePreviewStateChanged();
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
            pendingLineStartTargetKind = SandboxWallSnapTargetKind.None;
            pendingLineStartReferenceId = string.Empty;
            RaisePreviewStateChanged();
        }

        public bool CreateLineWall(Vector2 startPoint, Vector2 endPoint, float thickness = -1f)
        {
            if (IsWallTypeLocked() || IsEditingBlocked())
            {
                return false;
            }

            return CreateLineWallInternal(
                ResolveSnapResult(workspaceService?.ActiveFloorId, startPoint, null),
                ResolveSnapResult(workspaceService?.ActiveFloorId, endPoint, startPoint),
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

                    NormalizeFloorWallTopology(
                        floor,
                        topologyNormalizationJunctionTolerance);
                    SimplifyBrushGeneratedWalls(floor, createdWallIds);
                    createdWallIds.RemoveAll(id =>
                        floor.wallSegments.All(candidate => !string.Equals(candidate.wallSegmentId, id, StringComparison.Ordinal)));

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
            var floor = workspaceService?.ActiveFloor;
            var wall = floor == null ? null : FindWallSegment(floor, wallSegmentId);
            return wall != null && MoveWallJunctions(wall.startJunctionId, new[] { wall.startJunctionId }, newStartPoint);
        }

        public bool MoveWallEndHandle(string wallSegmentId, Vector2 newEndPoint)
        {
            var floor = workspaceService?.ActiveFloor;
            var wall = floor == null ? null : FindWallSegment(floor, wallSegmentId);
            return wall != null && MoveWallJunctions(wall.endJunctionId, new[] { wall.endJunctionId }, newEndPoint);
        }

        // Sets a wall's length by moving one endpoint along the wall axis. The anchored end stays put
        // (a shared corner detaches the moved end cleanly, leaving neighbours in place). Rejects with a
        // message if a door/window on the wall would no longer fit; openings keep their world position.
        public bool TrySetWallLength(string wallSegmentId, float newLength, bool anchorAtStart, out string error, out float minWorldLength, out string offenderLabel)
        {
            error = string.Empty;
            minWorldLength = 0f;
            offenderLabel = null;
            var floor = workspaceService?.ActiveFloor;
            var wall = floor == null ? null : FindWallSegment(floor, wallSegmentId);
            if (wall == null)
            {
                error = "Wall not found.";
                return false;
            }

            if (IsEditingBlocked() || IsWallLocked(wallSegmentId))
            {
                error = "Wall is locked.";
                return false;
            }

            newLength = Mathf.Max(0.05f, newLength);
            var oldStart = wall.startPoint;
            var oldEnd = wall.endPoint;
            var oldLength = Vector2.Distance(oldStart, oldEnd);
            if (oldLength <= 0.0001f)
            {
                error = "Wall has no direction to resize.";
                return false;
            }

            var dir = (oldEnd - oldStart) / oldLength;
            var newStart = anchorAtStart ? oldStart : oldEnd - dir * newLength;

            // Validate openings on this wall (their world position stays fixed).
            var minLength = 0f;
            string offender = null;
            void CheckOpening(string label, float offset, float width)
            {
                var halfWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(workspaceService, workspaceStateService, floor, width) * 0.5f;
                var worldCenter = oldStart + dir * offset;
                var newOffset = Vector2.Dot(worldCenter - newStart, dir);
                if (newOffset - halfWidth < -0.01f || newOffset + halfWidth > newLength + 0.01f)
                {
                    offender ??= label;
                }

                var requiredFromAnchor = anchorAtStart ? offset + halfWidth : (oldLength - offset) + halfWidth;
                minLength = Mathf.Max(minLength, requiredFromAnchor);
            }

            foreach (var door in floor.doors.Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
            {
                CheckOpening("a door", door.offsetAlongWall, door.width);
            }

            foreach (var window in floor.windows.Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
            {
                CheckOpening("a window", window.offsetAlongWall, window.width);
            }

            if (offender != null)
            {
                // Report the raw world-unit minimum and the offender; the caller formats the
                // message in the unit the user is actually typing in (grid/feet/meters).
                offenderLabel = offender;
                minWorldLength = minLength;
                error = $"{offender} on this wall would fall off at that length.";
                return false;
            }

            return ExecuteProjectMutation(
                "Set Wall Length",
                (_, mutableFloor) =>
                {
                    var target = FindWallSegment(mutableFloor, wallSegmentId);
                    if (target == null)
                    {
                        return false;
                    }

                    var start = target.startPoint;
                    var end = target.endPoint;
                    var length = Vector2.Distance(start, end);
                    if (length <= 0.0001f)
                    {
                        return false;
                    }

                    var direction = (end - start) / length;

                    // Capture opening world positions before the endpoint moves so we can re-derive offsets.
                    var doorCenters = mutableFloor.doors
                        .Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal))
                        .ToDictionary(candidate => candidate.doorId, candidate => start + direction * candidate.offsetAlongWall);
                    var windowCenters = mutableFloor.windows
                        .Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal))
                        .ToDictionary(candidate => candidate.windowId, candidate => start + direction * candidate.offsetAlongWall);

                    var moveStart = !anchorAtStart;
                    var movedTarget = moveStart ? end - direction * newLength : start + direction * newLength;
                    if (!UpdateWallEndpointPosition(mutableFloor, target, moveStart, movedTarget, 0.01f))
                    {
                        return false;
                    }

                    var resolvedStart = target.startPoint;
                    var resolvedDirection = (target.endPoint - target.startPoint).normalized;
                    foreach (var door in mutableFloor.doors.Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
                    {
                        door.offsetAlongWall = Vector2.Dot(doorCenters[door.doorId] - resolvedStart, resolvedDirection);
                    }

                    foreach (var window in mutableFloor.windows.Where(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
                    {
                        window.offsetAlongWall = Vector2.Dot(windowCenters[window.windowId] - resolvedStart, resolvedDirection);
                    }

                    return true;
                },
                new[] { wallSegmentId });
        }

        public bool MoveWallJunctions(string primaryJunctionId, IEnumerable<string> selectedJunctionIds, Vector2 targetPoint)
        {
            if (string.IsNullOrWhiteSpace(primaryJunctionId) || selectedJunctionIds == null)
            {
                return false;
            }

            var activeFloor = workspaceService?.ActiveFloor;
            if (activeFloor == null || IsEditingBlocked())
            {
                return false;
            }

            var junctionIds = selectedJunctionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (junctionIds.Count == 0)
            {
                return false;
            }

            var lockedWallIds = GetConnectedWallIds(activeFloor, junctionIds)
                .Where(IsWallLocked)
                .ToList();
            if (lockedWallIds.Count > 0)
            {
                return false;
            }

            var snapAnchor = ResolveJunctionDragAnchor(activeFloor.floorId, primaryJunctionId, junctionIds);
            var snappedTarget = ResolveSnapResult(activeFloor.floorId, targetPoint, snapAnchor).position;
            return ExecuteProjectMutation(
                junctionIds.Count > 1 ? "Move Wall Nodes" : "Move Wall Node",
                (_, floor) => TryMoveWallJunctionsInternal(floor, primaryJunctionId, junctionIds, snappedTarget),
                GetConnectedWallIds(activeFloor, junctionIds));
        }

        public Vector2? ResolveJunctionDragAnchor(string floorId, string primaryJunctionId, IEnumerable<string> selectedJunctionIds)
        {
            var floor = workspaceService?.FindFloor(floorId);
            if (floor == null)
            {
                return null;
            }

            return ResolveJunctionDragAnchor(
                floor,
                primaryJunctionId,
                selectedJunctionIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>());
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
                (project, floor) =>
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
                    var originalWallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
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
                        thickness = wall.thickness,
                        tags = new List<string>(wall.tags)
                    };

                    midJunction.connectedWallSegmentIds.Add(newWallId);
                    floor.wallSegments.Add(newWall);
                    ReassignAttachedOpeningsForSplit(project, floor, wall.wallSegmentId, newWallId, Vector2.Distance(wall.startPoint, midJunction.position), originalWallLength);
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

                    RemoveAttachedOpenings(floor, wall.wallSegmentId);
                    floor.wallSegments.Remove(wall);
                    PruneJunctionIfOrphan(floor, startJunctionId);
                    PruneJunctionIfOrphan(floor, endJunctionId);
                    return true;
                },
                Array.Empty<string>());
        }

        private bool CreateLineWallInternal(
            SandboxWallSnapResult startSnapResult,
            SandboxWallSnapResult endSnapResult,
            float thickness,
            string wallSegmentId,
            string startJunctionId,
            string endJunctionId)
        {
            return ExecuteProjectMutation(
                "Create Line Wall",
                (_, floor) =>
                {
                    var startJunction = EnsureJunctionForSnap(
                        floor,
                        startSnapResult,
                        startJunctionId,
                        Array.Empty<string>());
                    if (startJunction == null)
                    {
                        return false;
                    }

                    var endJunction = EnsureJunctionForSnap(
                        floor,
                        endSnapResult,
                        endJunctionId,
                        new[] { startJunction.wallJunctionId });
                    if (endJunction == null)
                    {
                        return false;
                    }

                    return AddWallSegmentBetweenJunctions(
                        floor,
                        wallSegmentId,
                        startJunction,
                        endJunction,
                        thickness);
                },
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

        private SandboxWallSnapResult ResolveSnapResult(string floorId, Vector2 rawPoint, Vector2? anchorPoint)
        {
            if (wallSnappingService == null || string.IsNullOrWhiteSpace(floorId))
            {
                return new SandboxWallSnapResult(rawPoint, SandboxWallSnapTargetKind.None, string.Empty);
            }

            return wallSnappingService.SnapPoint(floorId, rawPoint, anchorPoint);
        }

        private bool TryMoveWallJunctionsInternal(
            FloorData floor,
            string primaryJunctionId,
            IReadOnlyList<string> selectedJunctionIds,
            Vector2 snappedTargetPoint)
        {
            var selectedIdSet = new HashSet<string>(selectedJunctionIds, StringComparer.Ordinal);
            var primaryJunction = FindJunction(floor, primaryJunctionId);
            if (primaryJunction == null)
            {
                return false;
            }

            var junctions = selectedJunctionIds
                .Select(id => FindJunction(floor, id))
                .Where(junction => junction != null)
                .Distinct()
                .ToList();
            if (junctions.Count == 0)
            {
                return false;
            }

            var delta = snappedTargetPoint - primaryJunction.position;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            for (var i = 0; i < junctions.Count; i += 1)
            {
                junctions[i].position += delta;
            }

            SyncWallGeometryFromJunctions(floor);
            if (!ValidateWallGeometry(floor))
            {
                return false;
            }

            for (var i = 0; i < junctions.Count; i += 1)
            {
                var junction = FindJunction(floor, junctions[i].wallJunctionId);
                if (junction == null)
                {
                    continue;
                }

                var mergeTarget = floor.wallJunctions
                    .Where(candidate =>
                        !string.Equals(candidate.wallJunctionId, junction.wallJunctionId, StringComparison.Ordinal) &&
                        !selectedIdSet.Contains(candidate.wallJunctionId))
                    .Where(candidate => Vector2.Distance(candidate.position, junction.position) <= (wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance))
                    .OrderBy(candidate => Vector2.Distance(candidate.position, junction.position))
                    .FirstOrDefault();
                if (mergeTarget != null)
                {
                    MergeJunctionInto(floor, junction, mergeTarget);
                    continue;
                }

                var splitCandidate = FindSegmentSplitCandidate(floor, junction);
                if (splitCandidate.wall != null)
                {
                    SplitWallAtProjectedPointUsingExistingJunction(floor, splitCandidate.wall.wallSegmentId, junction, splitCandidate.projectedPoint);
                }
            }

            SyncWallGeometryFromJunctions(floor);
            PruneOrphanJunctions(floor);
            return ValidateWallGeometry(floor);
        }

        private Vector2? ResolveJunctionDragAnchor(FloorData floor, string primaryJunctionId, IReadOnlyCollection<string> selectedJunctionIds)
        {
            var primaryJunction = FindJunction(floor, primaryJunctionId);
            if (primaryJunction == null)
            {
                return null;
            }

            foreach (var connectedWallId in primaryJunction.connectedWallSegmentIds)
            {
                var wall = FindWallSegment(floor, connectedWallId);
                if (wall == null)
                {
                    continue;
                }

                var otherJunctionId = string.Equals(wall.startJunctionId, primaryJunctionId, StringComparison.Ordinal)
                    ? wall.endJunctionId
                    : wall.startJunctionId;
                if (selectedJunctionIds.Contains(otherJunctionId))
                {
                    continue;
                }

                var otherJunction = FindJunction(floor, otherJunctionId);
                if (otherJunction != null)
                {
                    return otherJunction.position;
                }
            }

            return null;
        }

        private WallJunctionData EnsureJunctionForSnap(
            FloorData floor,
            SandboxWallSnapResult snapResult,
            string preferredJunctionId,
            IReadOnlyCollection<string> excludedJunctionIds)
        {
            var exclusions = excludedJunctionIds?.ToArray() ?? Array.Empty<string>();
            switch (snapResult.targetKind)
            {
                case SandboxWallSnapTargetKind.Endpoint:
                {
                    var endpointJunction = FindJunction(floor, snapResult.referenceId);
                    // Ignore a snap that resolves to a junction we must avoid (e.g. the line's own start),
                    // so the segment can't collapse onto itself and get rejected; fall through to make a
                    // distinct junction at the snapped position instead.
                    if (endpointJunction != null && !exclusions.Contains(endpointJunction.wallJunctionId))
                    {
                        return endpointJunction;
                    }

                    break;
                }
                case SandboxWallSnapTargetKind.Segment:
                {
                    var reusableSegmentJunction = FindReusableJunction(
                        floor,
                        snapResult.position,
                        wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance,
                        exclusions);
                    if (reusableSegmentJunction != null)
                    {
                        return reusableSegmentJunction;
                    }

                    if (!string.IsNullOrWhiteSpace(snapResult.referenceId))
                    {
                        return SplitWallAtProjectedPoint(
                            floor,
                            snapResult.referenceId,
                            snapResult.position,
                            preferredJunctionId);
                    }

                    break;
                }
                case SandboxWallSnapTargetKind.Intersection:
                {
                    // Placing on a wall crossing: create/reuse a junction there and split every wall
                    // passing through it, so the new wall actually ties into the intersection instead
                    // of leaving a disconnected junction floating on top of it.
                    return SplitWallsAtIntersection(floor, snapResult.position, preferredJunctionId, exclusions);
                }
            }

            return FindOrCreateJunction(
                floor,
                snapResult.position,
                wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance,
                preferredJunctionId,
                exclusions);
        }

        private WallJunctionData SplitWallAtProjectedPoint(
            FloorData floor,
            string wallSegmentId,
            Vector2 splitPoint,
            string preferredJunctionId)
        {
            var wall = FindWallSegment(floor, wallSegmentId);
            if (wall == null)
            {
                return null;
            }

            var projectedPoint = ProjectPointOntoSegment(splitPoint, wall.startPoint, wall.endPoint);
            if (Vector2.Distance(projectedPoint, wall.startPoint) <= 0.05f)
            {
                return FindJunction(floor, wall.startJunctionId);
            }

            if (Vector2.Distance(projectedPoint, wall.endPoint) <= 0.05f)
            {
                return FindJunction(floor, wall.endJunctionId);
            }

            var existingJunction = FindReusableJunction(
                floor,
                projectedPoint,
                wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance,
                wall.startJunctionId,
                wall.endJunctionId);
            if (existingJunction != null)
            {
                return existingJunction;
            }

            var originalEndJunctionId = wall.endJunctionId;
            var originalEndPoint = wall.endPoint;
            var originalWallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var originalEndJunction = FindJunction(floor, originalEndJunctionId);
            var midJunction = new WallJunctionData
            {
                wallJunctionId = preferredJunctionId,
                position = projectedPoint
            };
            floor.wallJunctions.Add(midJunction);

            if (originalEndJunction != null)
            {
                RemoveConnection(originalEndJunction, wall.wallSegmentId);
            }

            wall.endPoint = midJunction.position;
            wall.endJunctionId = midJunction.wallJunctionId;
            AddConnection(midJunction, wall.wallSegmentId);

            var splitWallId = SandboxId.NewId();
            if (originalEndJunction != null)
            {
                AddConnection(originalEndJunction, splitWallId);
            }

            floor.wallSegments.Add(new WallSegmentData
            {
                wallSegmentId = splitWallId,
                startJunctionId = midJunction.wallJunctionId,
                endJunctionId = originalEndJunctionId,
                startPoint = midJunction.position,
                endPoint = originalEndPoint,
                thickness = wall.thickness,
                tags = new List<string>(wall.tags)
            });
            AddConnection(midJunction, splitWallId);
            ReassignAttachedOpeningsForSplit(workspaceService?.ActiveProject, floor, wall.wallSegmentId, splitWallId, Vector2.Distance(wall.startPoint, midJunction.position), originalWallLength);
            return midJunction;
        }

        // Resolves the junction for a point that snapped to a wall crossing: create/reuse a junction
        // at the point, then split every wall whose interior passes through it so they all connect
        // there. Returns the shared junction the new wall should attach to.
        private WallJunctionData SplitWallsAtIntersection(
            FloorData floor,
            Vector2 point,
            string preferredJunctionId,
            string[] excludedJunctionIds)
        {
            var reuseDistance = wallSnappingService != null ? wallSnappingService.JunctionReuseDistance : nearJoinCleanupDistance;
            var junction = FindOrCreateJunction(floor, point, reuseDistance, preferredJunctionId, excludedJunctionIds);
            if (junction == null)
            {
                return null;
            }

            var wallsSnapshot = floor.wallSegments.ToArray();
            for (var i = 0; i < wallsSnapshot.Length; i += 1)
            {
                var wall = wallsSnapshot[i];
                if (junction.connectedWallSegmentIds.Contains(wall.wallSegmentId))
                {
                    continue;
                }

                var projected = ProjectPointOntoSegment(point, wall.startPoint, wall.endPoint);
                if (Vector2.Distance(projected, point) > 0.06f)
                {
                    continue;
                }

                SplitWallAtProjectedPointUsingExistingJunction(floor, wall.wallSegmentId, junction, projected);
            }

            return junction;
        }

        private void SplitWallAtProjectedPointUsingExistingJunction(
            FloorData floor,
            string wallSegmentId,
            WallJunctionData existingJunction,
            Vector2 projectedPoint)
        {
            var wall = FindWallSegment(floor, wallSegmentId);
            if (wall == null || existingJunction == null)
            {
                return;
            }

            if (string.Equals(wall.startJunctionId, existingJunction.wallJunctionId, StringComparison.Ordinal) ||
                string.Equals(wall.endJunctionId, existingJunction.wallJunctionId, StringComparison.Ordinal))
            {
                return;
            }

            if (Vector2.Distance(projectedPoint, wall.startPoint) <= 0.05f ||
                Vector2.Distance(projectedPoint, wall.endPoint) <= 0.05f)
            {
                return;
            }

            var originalEndJunctionId = wall.endJunctionId;
            var originalEndPoint = wall.endPoint;
            var originalWallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var originalEndJunction = FindJunction(floor, originalEndJunctionId);
            if (originalEndJunction != null)
            {
                RemoveConnection(originalEndJunction, wall.wallSegmentId);
            }

            existingJunction.position = projectedPoint;
            wall.endPoint = projectedPoint;
            wall.endJunctionId = existingJunction.wallJunctionId;
            AddConnection(existingJunction, wall.wallSegmentId);

            var splitWallId = SandboxId.NewId();
            if (originalEndJunction != null)
            {
                AddConnection(originalEndJunction, splitWallId);
            }

            floor.wallSegments.Add(new WallSegmentData
            {
                wallSegmentId = splitWallId,
                startJunctionId = existingJunction.wallJunctionId,
                endJunctionId = originalEndJunctionId,
                startPoint = projectedPoint,
                endPoint = originalEndPoint,
                thickness = wall.thickness,
                tags = new List<string>(wall.tags)
            });
            AddConnection(existingJunction, splitWallId);
            ReassignAttachedOpeningsForSplit(workspaceService?.ActiveProject, floor, wall.wallSegmentId, splitWallId, Vector2.Distance(wall.startPoint, projectedPoint), originalWallLength);
        }

        private (WallSegmentData wall, Vector2 projectedPoint) FindSegmentSplitCandidate(FloorData floor, WallJunctionData movedJunction)
        {
            if (movedJunction == null)
            {
                return default;
            }

            var bestDistance = float.MaxValue;
            WallSegmentData bestWall = null;
            var bestPoint = Vector2.zero;
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (movedJunction.connectedWallSegmentIds.Contains(wall.wallSegmentId))
                {
                    continue;
                }

                var projectedPoint = ProjectPointOntoSegment(movedJunction.position, wall.startPoint, wall.endPoint);
                if (Vector2.Distance(projectedPoint, wall.startPoint) <= 0.05f ||
                    Vector2.Distance(projectedPoint, wall.endPoint) <= 0.05f)
                {
                    continue;
                }

                var distance = Vector2.Distance(movedJunction.position, projectedPoint);
                if (distance > (wallSnappingService != null ? wallSnappingService.SegmentSnappingEnabled ? 0.25f : float.MaxValue : 0.25f))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestWall = wall;
                    bestPoint = projectedPoint;
                }
            }

            return (bestWall, bestPoint);
        }

        private static void MergeJunctionInto(FloorData floor, WallJunctionData sourceJunction, WallJunctionData targetJunction)
        {
            if (floor == null || sourceJunction == null || targetJunction == null ||
                string.Equals(sourceJunction.wallJunctionId, targetJunction.wallJunctionId, StringComparison.Ordinal))
            {
                return;
            }

            var connectedWallIds = sourceJunction.connectedWallSegmentIds.ToList();
            for (var i = 0; i < connectedWallIds.Count; i += 1)
            {
                var wall = FindWallSegment(floor, connectedWallIds[i]);
                if (wall == null)
                {
                    continue;
                }

                if (string.Equals(wall.startJunctionId, sourceJunction.wallJunctionId, StringComparison.Ordinal))
                {
                    wall.startJunctionId = targetJunction.wallJunctionId;
                    wall.startPoint = targetJunction.position;
                }

                if (string.Equals(wall.endJunctionId, sourceJunction.wallJunctionId, StringComparison.Ordinal))
                {
                    wall.endJunctionId = targetJunction.wallJunctionId;
                    wall.endPoint = targetJunction.position;
                }

                AddConnection(targetJunction, wall.wallSegmentId);
            }

            sourceJunction.connectedWallSegmentIds.Clear();
            floor.wallJunctions.Remove(sourceJunction);
        }

        private static void SyncWallGeometryFromJunctions(FloorData floor)
        {
            if (floor == null)
            {
                return;
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                var startJunction = FindJunction(floor, wall.startJunctionId);
                var endJunction = FindJunction(floor, wall.endJunctionId);
                if (startJunction != null)
                {
                    wall.startPoint = startJunction.position;
                }

                if (endJunction != null)
                {
                    wall.endPoint = endJunction.position;
                }
            }
        }

        private static bool ValidateWallGeometry(FloorData floor)
        {
            if (floor == null)
            {
                return false;
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (string.Equals(wall.startJunctionId, wall.endJunctionId, StringComparison.Ordinal) ||
                    Vector2.Distance(wall.startPoint, wall.endPoint) <= 0.02f)
                {
                    return false;
                }
            }

            return true;
        }

        private static void PruneOrphanJunctions(FloorData floor)
        {
            if (floor == null)
            {
                return;
            }

            floor.wallJunctions.RemoveAll(junction => junction == null || junction.connectedWallSegmentIds.Count == 0);
        }

        private void NormalizeFloorWallTopology(FloorData floor, float junctionTolerance)
        {
            if (floor == null)
            {
                return;
            }

            junctionTolerance = Mathf.Max(0.01f, junctionTolerance);
            var iterationBudget = Mathf.Max(32, floor.wallSegments.Count * 8);
            for (var iteration = 0; iteration < iterationBudget; iteration += 1)
            {
                SyncWallGeometryFromJunctions(floor);

                if (MergeNearbyJunctions(floor, junctionTolerance))
                {
                    continue;
                }

                if (SplitWallsAtExistingJunctions(floor, junctionTolerance))
                {
                    continue;
                }

                if (SplitIntersectingWalls(floor, junctionTolerance))
                {
                    continue;
                }

                break;
            }

            SyncWallGeometryFromJunctions(floor);
            PruneOrphanJunctions(floor);
        }

        private void SimplifyBrushGeneratedWalls(FloorData floor, IReadOnlyCollection<string> createdWallIds)
        {
            if (floor == null || createdWallIds == null || createdWallIds.Count == 0)
            {
                return;
            }

            var createdSet = new HashSet<string>(createdWallIds, StringComparer.Ordinal);
            var iterationBudget = Mathf.Max(16, floor.wallSegments.Count * 4);
            for (var iteration = 0; iteration < iterationBudget; iteration += 1)
            {
                SyncWallGeometryFromJunctions(floor);

                if (RemoveDuplicateBrushWalls(floor, createdSet))
                {
                    continue;
                }

                if (MergeCollinearBrushWalls(floor, createdSet))
                {
                    continue;
                }

                if (PruneShortBrushStubs(floor, createdSet))
                {
                    continue;
                }

                break;
            }

            SyncWallGeometryFromJunctions(floor);
            PruneOrphanJunctions(floor);
        }

        private bool RemoveDuplicateBrushWalls(FloorData floor, ISet<string> createdWallIds)
        {
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var first = floor.wallSegments[i];
                if (first == null || !createdWallIds.Contains(first.wallSegmentId) || HasAttachedOpenings(floor, first.wallSegmentId))
                {
                    continue;
                }

                for (var j = i + 1; j < floor.wallSegments.Count; j += 1)
                {
                    var second = floor.wallSegments[j];
                    if (second == null || HasAttachedOpenings(floor, second.wallSegmentId))
                    {
                        continue;
                    }

                    if (!AreWallsDuplicate(first, second))
                    {
                        continue;
                    }

                    var wallToRemove = createdWallIds.Contains(second.wallSegmentId) ? second : first;
                    if (RemoveWallInternal(floor, wallToRemove))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool MergeCollinearBrushWalls(FloorData floor, ISet<string> createdWallIds)
        {
            foreach (var junction in floor.wallJunctions.ToList())
            {
                if (junction == null || junction.connectedWallSegmentIds.Count != 2)
                {
                    continue;
                }

                var firstWall = FindWallSegment(floor, junction.connectedWallSegmentIds[0]);
                var secondWall = FindWallSegment(floor, junction.connectedWallSegmentIds[1]);
                if (firstWall == null || secondWall == null)
                {
                    continue;
                }

                if (!createdWallIds.Contains(firstWall.wallSegmentId) && !createdWallIds.Contains(secondWall.wallSegmentId))
                {
                    continue;
                }

                if (HasAttachedOpenings(floor, firstWall.wallSegmentId) || HasAttachedOpenings(floor, secondWall.wallSegmentId))
                {
                    continue;
                }

                if (TryMergeConnectedWallsInternal(floor, firstWall.wallSegmentId, secondWall.wallSegmentId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PruneShortBrushStubs(FloorData floor, ISet<string> createdWallIds)
        {
            const float maximumStubLength = 0.2f;
            foreach (var wall in floor.wallSegments.ToList())
            {
                if (wall == null || !createdWallIds.Contains(wall.wallSegmentId) || HasAttachedOpenings(floor, wall.wallSegmentId))
                {
                    continue;
                }

                var length = Vector2.Distance(wall.startPoint, wall.endPoint);
                if (length > maximumStubLength)
                {
                    continue;
                }

                var startJunction = FindJunction(floor, wall.startJunctionId);
                var endJunction = FindJunction(floor, wall.endJunctionId);
                var hasDanglingEndpoint = (startJunction?.connectedWallSegmentIds.Count ?? 0) <= 1 ||
                    (endJunction?.connectedWallSegmentIds.Count ?? 0) <= 1;
                if (!hasDanglingEndpoint)
                {
                    continue;
                }

                if (RemoveWallInternal(floor, wall))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MergeNearbyJunctions(FloorData floor, float junctionTolerance)
        {
            for (var i = 0; i < floor.wallJunctions.Count; i += 1)
            {
                var first = floor.wallJunctions[i];
                if (first == null)
                {
                    continue;
                }

                for (var j = i + 1; j < floor.wallJunctions.Count; j += 1)
                {
                    var second = floor.wallJunctions[j];
                    if (second == null)
                    {
                        continue;
                    }

                    if (Vector2.Distance(first.position, second.position) > junctionTolerance)
                    {
                        continue;
                    }

                    MergeJunctionInto(floor, second, first);
                    return true;
                }
            }

            return false;
        }

        private bool SplitWallsAtExistingJunctions(FloorData floor, float junctionTolerance)
        {
            for (var i = 0; i < floor.wallJunctions.Count; i += 1)
            {
                var junction = floor.wallJunctions[i];
                if (junction == null)
                {
                    continue;
                }

                for (var wallIndex = 0; wallIndex < floor.wallSegments.Count; wallIndex += 1)
                {
                    var wall = floor.wallSegments[wallIndex];
                    if (wall == null || junction.connectedWallSegmentIds.Contains(wall.wallSegmentId))
                    {
                        continue;
                    }

                    var projectedPoint = ProjectPointOntoSegment(junction.position, wall.startPoint, wall.endPoint);
                    if (!IsPointNear(junction.position, projectedPoint, junctionTolerance) ||
                        IsPointNear(projectedPoint, wall.startPoint, 0.05f) ||
                        IsPointNear(projectedPoint, wall.endPoint, 0.05f))
                    {
                        continue;
                    }

                    SplitWallAtProjectedPointUsingExistingJunction(floor, wall.wallSegmentId, junction, projectedPoint);
                    return true;
                }
            }

            return false;
        }

        private bool SplitIntersectingWalls(FloorData floor, float junctionTolerance)
        {
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var first = floor.wallSegments[i];
                if (first == null)
                {
                    continue;
                }

                for (var j = i + 1; j < floor.wallSegments.Count; j += 1)
                {
                    var second = floor.wallSegments[j];
                    if (second == null)
                    {
                        continue;
                    }

                    if (!TryGetSegmentIntersection(first.startPoint, first.endPoint, second.startPoint, second.endPoint, out var intersectionPoint))
                    {
                        continue;
                    }

                    var firstStart = IsPointNear(intersectionPoint, first.startPoint, 0.05f);
                    var firstEnd = IsPointNear(intersectionPoint, first.endPoint, 0.05f);
                    var secondStart = IsPointNear(intersectionPoint, second.startPoint, 0.05f);
                    var secondEnd = IsPointNear(intersectionPoint, second.endPoint, 0.05f);
                    var firstAtEndpoint = firstStart || firstEnd;
                    var secondAtEndpoint = secondStart || secondEnd;

                    if (firstAtEndpoint && secondAtEndpoint)
                    {
                        var firstJunction = FindJunction(
                            floor,
                            firstStart ? first.startJunctionId : first.endJunctionId);
                        var secondJunction = FindJunction(
                            floor,
                            secondStart ? second.startJunctionId : second.endJunctionId);
                        if (firstJunction != null &&
                            secondJunction != null &&
                            !string.Equals(firstJunction.wallJunctionId, secondJunction.wallJunctionId, StringComparison.Ordinal) &&
                            Vector2.Distance(firstJunction.position, secondJunction.position) <= junctionTolerance)
                        {
                            MergeJunctionInto(floor, secondJunction, firstJunction);
                            return true;
                        }

                        continue;
                    }

                    if (firstAtEndpoint && !secondAtEndpoint)
                    {
                        var firstJunction = FindJunction(
                            floor,
                            firstStart ? first.startJunctionId : first.endJunctionId);
                        if (firstJunction != null)
                        {
                            SplitWallAtProjectedPointUsingExistingJunction(
                                floor,
                                second.wallSegmentId,
                                firstJunction,
                                intersectionPoint);
                            return true;
                        }

                        continue;
                    }

                    if (!firstAtEndpoint && secondAtEndpoint)
                    {
                        var secondJunction = FindJunction(
                            floor,
                            secondStart ? second.startJunctionId : second.endJunctionId);
                        if (secondJunction != null)
                        {
                            SplitWallAtProjectedPointUsingExistingJunction(
                                floor,
                                first.wallSegmentId,
                                secondJunction,
                                intersectionPoint);
                            return true;
                        }

                        continue;
                    }

                    var intersectionJunction = SplitWallAtProjectedPoint(
                        floor,
                        first.wallSegmentId,
                        intersectionPoint,
                        SandboxId.NewId());
                    if (intersectionJunction != null)
                    {
                        SplitWallAtProjectedPointUsingExistingJunction(
                            floor,
                            second.wallSegmentId,
                            intersectionJunction,
                            intersectionPoint);
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<string> GetConnectedWallIds(FloorData floor, IEnumerable<string> junctionIds)
        {
            if (floor == null || junctionIds == null)
            {
                return new List<string>();
            }

            return junctionIds
                .Select(id => FindJunction(floor, id))
                .Where(junction => junction != null)
                .SelectMany(junction => junction.connectedWallSegmentIds)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private Vector2 SnapPoint(string floorId, Vector2 rawPoint, Vector2? anchorPoint)
        {
            return ResolveSnapResult(floorId, rawPoint, anchorPoint).position;
        }

        private float ResolveThickness(float thickness)
        {
            return thickness > 0f ? thickness : defaultWallThickness;
        }

        private static bool AddWallSegmentBetweenJunctions(
            FloorData floor,
            string wallSegmentId,
            WallJunctionData startJunction,
            WallJunctionData endJunction,
            float thickness)
        {
            if (floor == null ||
                startJunction == null ||
                endJunction == null ||
                string.IsNullOrWhiteSpace(wallSegmentId) ||
                string.Equals(startJunction.wallJunctionId, endJunction.wallJunctionId, StringComparison.Ordinal) ||
                Vector2.Distance(startJunction.position, endJunction.position) <= 0.02f)
            {
                return false;
            }

            floor.wallSegments.Add(new WallSegmentData
            {
                wallSegmentId = wallSegmentId,
                startJunctionId = startJunction.wallJunctionId,
                endJunctionId = endJunction.wallJunctionId,
                startPoint = startJunction.position,
                endPoint = endJunction.position,
                thickness = thickness
            });
            AddConnection(startJunction, wallSegmentId);
            AddConnection(endJunction, wallSegmentId);
            return true;
        }

        // Public so non-drawing flows (e.g. clipboard paste) can rebuild a wall with correct junction
        // topology on a given floor. Pure/static: it only mutates the floor passed in, which is what
        // clipboard paste needs since it operates on a cloned project.
        public static bool AddWallSegment(
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

            return AddWallSegmentBetweenJunctions(
                floor,
                wallSegmentId,
                startJunction,
                endJunction,
                thickness);
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

        private bool TryMergeConnectedWallsInternal(FloorData floor, string firstWallSegmentId, string secondWallSegmentId)
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
            firstWall.tags = firstWall.tags
                .Concat(secondWall.tags ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            AddConnection(mergedStartJunction, firstWall.wallSegmentId);
            AddConnection(mergedEndJunction, firstWall.wallSegmentId);

            floor.wallSegments.Remove(secondWall);
            PruneJunctionIfOrphan(floor, sharedJunctionId);
            return true;
        }

        private static WallJunctionData FindJunction(FloorData floor, string wallJunctionId)
        {
            return floor?.wallJunctions.FirstOrDefault(junction =>
                string.Equals(junction.wallJunctionId, wallJunctionId, StringComparison.Ordinal));
        }

        private bool RemoveWallInternal(FloorData floor, WallSegmentData wall)
        {
            if (floor == null || wall == null)
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

            RemoveAttachedOpenings(floor, wall.wallSegmentId);
            floor.wallSegments.Remove(wall);
            PruneJunctionIfOrphan(floor, startJunctionId);
            PruneJunctionIfOrphan(floor, endJunctionId);
            return true;
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

        private void ReassignAttachedOpeningsForSplit(
            BuildingProjectData project,
            FloorData floor,
            string originalWallSegmentId,
            string newWallSegmentId,
            float splitDistance,
            float originalWallLength)
        {
            if (floor == null ||
                string.IsNullOrWhiteSpace(originalWallSegmentId) ||
                string.IsNullOrWhiteSpace(newWallSegmentId) ||
                splitDistance <= 0f ||
                originalWallLength <= splitDistance)
            {
                return;
            }

            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            ReassignAttachedDoorsForSplit(project, floor, originalWallSegmentId, newWallSegmentId, splitDistance, originalWallLength, gridSize);
            ReassignAttachedWindowsForSplit(project, floor, originalWallSegmentId, newWallSegmentId, splitDistance, originalWallLength, gridSize);
        }

        private void ReassignAttachedDoorsForSplit(
            BuildingProjectData project,
            FloorData floor,
            string originalWallSegmentId,
            string newWallSegmentId,
            float splitDistance,
            float originalWallLength,
            float gridSize)
        {
            var attachedDoors = floor.doors
                .Where(candidate => string.Equals(candidate.wallSegmentId, originalWallSegmentId, StringComparison.Ordinal))
                .ToList();
            if (attachedDoors.Count == 0)
            {
                return;
            }

            var doorsToRemove = new List<DoorData>();
            for (var i = 0; i < attachedDoors.Count; i += 1)
            {
                if (!TryReassignOpeningForSplit(project, floor, attachedDoors[i], attachedDoors[i].width, gridSize, originalWallSegmentId, newWallSegmentId, splitDistance, originalWallLength, out var nextWallId, out var nextOffset))
                {
                    doorsToRemove.Add(attachedDoors[i]);
                    continue;
                }

                attachedDoors[i].wallSegmentId = nextWallId;
                attachedDoors[i].offsetAlongWall = nextOffset;
            }

            if (doorsToRemove.Count > 0)
            {
                floor.doors.RemoveAll(candidate => doorsToRemove.Contains(candidate));
            }
        }

        private void ReassignAttachedWindowsForSplit(
            BuildingProjectData project,
            FloorData floor,
            string originalWallSegmentId,
            string newWallSegmentId,
            float splitDistance,
            float originalWallLength,
            float gridSize)
        {
            var attachedWindows = floor.windows
                .Where(candidate => string.Equals(candidate.wallSegmentId, originalWallSegmentId, StringComparison.Ordinal))
                .ToList();
            if (attachedWindows.Count == 0)
            {
                return;
            }

            var windowsToRemove = new List<WindowData>();
            for (var i = 0; i < attachedWindows.Count; i += 1)
            {
                if (!TryReassignOpeningForSplit(project, floor, attachedWindows[i], attachedWindows[i].width, gridSize, originalWallSegmentId, newWallSegmentId, splitDistance, originalWallLength, out var nextWallId, out var nextOffset))
                {
                    windowsToRemove.Add(attachedWindows[i]);
                    continue;
                }

                attachedWindows[i].wallSegmentId = nextWallId;
                attachedWindows[i].offsetAlongWall = nextOffset;
            }

            if (windowsToRemove.Count > 0)
            {
                floor.windows.RemoveAll(candidate => windowsToRemove.Contains(candidate));
            }
        }

        private bool TryReassignOpeningForSplit<T>(
            BuildingProjectData project,
            FloorData floor,
            T opening,
            float authoredWidth,
            float gridSize,
            string originalWallSegmentId,
            string newWallSegmentId,
            float splitDistance,
            float originalWallLength,
            out string resolvedWallSegmentId,
            out float resolvedOffset) where T : class
        {
            resolvedWallSegmentId = originalWallSegmentId;
            resolvedOffset = 0f;
            if (opening == null)
            {
                return false;
            }

            var currentOffset = opening switch
            {
                DoorData door => door.offsetAlongWall,
                WindowData window => window.offsetAlongWall,
                _ => 0f
            };

            var leftLength = splitDistance;
            var rightLength = originalWallLength - splitDistance;
            var halfWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(project, floor, authoredWidth, gridSize) * 0.5f;
            var minimumOffset = halfWidth + OpeningEndMargin;
            var leftMaximumOffset = leftLength - halfWidth - OpeningEndMargin;
            var rightMaximumOffset = rightLength - halfWidth - OpeningEndMargin;
            var prefersLeft = currentOffset <= splitDistance;

            if (prefersLeft && leftMaximumOffset >= minimumOffset)
            {
                resolvedWallSegmentId = originalWallSegmentId;
                resolvedOffset = Mathf.Clamp(currentOffset, minimumOffset, leftMaximumOffset);
                return true;
            }

            if (!prefersLeft && rightMaximumOffset >= minimumOffset)
            {
                resolvedWallSegmentId = newWallSegmentId;
                resolvedOffset = Mathf.Clamp(currentOffset - splitDistance, minimumOffset, rightMaximumOffset);
                return true;
            }

            if (prefersLeft ? rightMaximumOffset >= minimumOffset : leftMaximumOffset >= minimumOffset)
            {
                resolvedWallSegmentId = prefersLeft ? newWallSegmentId : originalWallSegmentId;
                resolvedOffset = prefersLeft
                    ? minimumOffset
                    : Mathf.Clamp(currentOffset, minimumOffset, leftMaximumOffset);
                return resolvedWallSegmentId == newWallSegmentId || resolvedOffset >= minimumOffset;
            }

            return false;
        }

        private static void RemoveAttachedOpenings(FloorData floor, string wallSegmentId)
        {
            if (floor == null || string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return;
            }

            floor.doors?.RemoveAll(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            floor.windows?.RemoveAll(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
        }

        private static bool HasAttachedOpenings(FloorData floor, string wallSegmentId)
        {
            return (floor?.doors?.Any(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)) ?? false) ||
                (floor?.windows?.Any(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal)) ?? false);
        }

        private static bool AreWallsDuplicate(WallSegmentData first, WallSegmentData second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            return
                (string.Equals(first.startJunctionId, second.startJunctionId, StringComparison.Ordinal) &&
                 string.Equals(first.endJunctionId, second.endJunctionId, StringComparison.Ordinal)) ||
                (string.Equals(first.startJunctionId, second.endJunctionId, StringComparison.Ordinal) &&
                 string.Equals(first.endJunctionId, second.startJunctionId, StringComparison.Ordinal)) ||
                ((IsPointNear(first.startPoint, second.startPoint, 0.02f) &&
                  IsPointNear(first.endPoint, second.endPoint, 0.02f)) ||
                 (IsPointNear(first.startPoint, second.endPoint, 0.02f) &&
                  IsPointNear(first.endPoint, second.startPoint, 0.02f)));
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

        private static bool TryGetSegmentIntersection(
            Vector2 firstStart,
            Vector2 firstEnd,
            Vector2 secondStart,
            Vector2 secondEnd,
            out Vector2 intersectionPoint)
        {
            intersectionPoint = Vector2.zero;
            var r = firstEnd - firstStart;
            var s = secondEnd - secondStart;
            var denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= 0.0001f)
            {
                return false;
            }

            var delta = secondStart - firstStart;
            var t = Cross(delta, s) / denominator;
            var u = Cross(delta, r) / denominator;
            if (t < -0.0001f || t > 1.0001f || u < -0.0001f || u > 1.0001f)
            {
                return false;
            }

            intersectionPoint = firstStart + (r * t);
            return true;
        }

        private static float Cross(Vector2 first, Vector2 second)
        {
            return (first.x * second.y) - (first.y * second.x);
        }

        private static bool IsPointNear(Vector2 first, Vector2 second, float tolerance)
        {
            return Vector2.Distance(first, second) <= Mathf.Max(0.0001f, tolerance);
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
