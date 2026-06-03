using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxWallAuthoringOverlay : MonoBehaviour
    {
        [SerializeField] private float minimumBrushSampleDistance = 0.1f;
        [SerializeField] private float handleHitRadius = 0.35f;
        [SerializeField] private List<string> selectedJunctionIds = new();

        private SandboxToolStateService toolStateService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxWallOverlayRenderer wallOverlayRenderer;
        private SandboxWallSnappingService wallSnappingService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxEditorQoLService editorQoLService;
        private SandboxPreviewService previewService;

        private Vector2 lastBrushPoint;
        private Vector2 currentPointerWorldPoint;
        private Vector2 currentLinePreviewPoint;
        private bool isJunctionDragActive;
        private string draggedPrimaryJunctionId = string.Empty;
        private string draggedWallSegmentId = string.Empty;
        private bool draggedHandleIsStart;
        private Vector2 draggedPrimaryJunctionStartPoint;
        private Vector2 draggedPrimaryJunctionPreviewPoint;
        private SandboxWallSnapResult dragPreviewSnapResult;

        public bool IsJunctionDragActive => isJunctionDragActive;
        public bool IsHandleDragActive => isJunctionDragActive;
        public string DraggedWallSegmentId => draggedWallSegmentId;
        public bool DraggedHandleIsStart => draggedHandleIsStart;
        public Vector2 DraggedHandlePreviewPoint => draggedPrimaryJunctionPreviewPoint;
        public Vector2 CurrentLinePreviewPoint => currentLinePreviewPoint;
        public IReadOnlyList<string> SelectedJunctionIds => selectedJunctionIds;
        public SandboxWallSnapResult DragPreviewSnapResult => dragPreviewSnapResult;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            wallOverlayRenderer = FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallSnappingService = FindAnyObjectByType<SandboxWallSnappingService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();

            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged += HandleToolModeChanged;
            }
        }

        private void HandleToolModeChanged(SandboxToolMode toolMode)
        {
            if (toolMode == SandboxToolMode.WallLine || toolMode == SandboxToolMode.WallBrush || wallAuthoringService == null)
            {
                return;
            }

            wallAuthoringService.CancelLinePlacement();
            wallAuthoringService.CancelBrushStroke();
        }

        private void Update()
        {
            if (toolStateService == null || wallAuthoringService == null)
            {
                return;
            }

            if (previewService != null && previewService.IsPreviewModeActive)
            {
                wallSnappingService?.SetTemporarySnappingBypass(false);
                inputRouter?.SetPointerOverHandle(false);
                return;
            }

            currentPointerWorldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            UpdateLinePreviewPoint();

            var currentToolMode = toolStateService.CurrentToolMode;
            if (!SupportsWallEditing(currentToolMode))
            {
                if (isJunctionDragActive)
                {
                    CancelJunctionDrag();
                }

                wallSnappingService?.SetTemporarySnappingBypass(false);
                inputRouter?.SetPointerOverHandle(false);
                return;
            }

            var isFineAdjustmentActive = SandboxInputAdapter.GetKey(KeyCode.LeftAlt) || SandboxInputAdapter.GetKey(KeyCode.RightAlt);
            wallSnappingService?.SetTemporarySnappingBypass(isFineAdjustmentActive);

            var isHoveringHandle = TryGetNearestHandle(currentPointerWorldPoint, out var hoveredJunctionId);
            // Handle hover/drag is a Select-tool affordance. For the wall tools, don't flag the pointer
            // as "over a handle" — otherwise the input target becomes Handle (not World) and the click is
            // blocked from reaching the tool, so you can't begin/continue a line at a junction.
            if (!isJunctionDragActive)
            {
                inputRouter?.SetPointerOverHandle(currentToolMode == SandboxToolMode.Select && isHoveringHandle);
            }

            if (isJunctionDragActive)
            {
                if (SandboxInputAdapter.GetMouseButton(0))
                {
                    UpdateJunctionDragPreview(currentPointerWorldPoint);
                }

                if (SandboxInputAdapter.GetMouseButtonUp(0))
                {
                    CommitJunctionDrag();
                }

                if (SandboxInputAdapter.WasRightMouseClickReleasedThisFrame())
                {
                    CancelJunctionDrag();
                }

                return;
            }

            // Dragging a junction handle is a Select-tool action only; with the wall tools, a click on a
            // junction should begin/continue a line snapped to it, not start a handle drag.
            if (currentToolMode == SandboxToolMode.Select && SandboxInputAdapter.GetMouseButtonDown(0) && isHoveringHandle)
            {
                TryBeginJunctionDrag(hoveredJunctionId, currentPointerWorldPoint);
                return;
            }

            if (currentToolMode == SandboxToolMode.Select)
            {
                return;
            }

            if (SandboxInputAdapter.WasRightMouseClickReleasedThisFrame())
            {
                var hadPendingSegment = wallAuthoringService.HasPendingLineStart;
                wallAuthoringService.CancelLinePlacement();
                wallAuthoringService.CancelBrushStroke();
                UpdateStatus(hadPendingSegment ? "Ended wall chain." : "Cancelled wall authoring preview.");
                return;
            }

            var currentTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (currentTarget != SandboxInputTarget.World)
            {
                if (SandboxInputAdapter.GetMouseButtonUp(0) && wallAuthoringService.IsBrushCaptureActive)
                {
                    wallAuthoringService.EndBrushStrokeCapture();
                }

                return;
            }

            switch (currentToolMode)
            {
                case SandboxToolMode.WallLine:
                    HandleLineTool();
                    break;
                case SandboxToolMode.WallBrush:
                    HandleBrushTool();
                    break;
            }
        }

        private static bool SupportsWallEditing(SandboxToolMode toolMode)
        {
            return toolMode == SandboxToolMode.Select ||
                   toolMode == SandboxToolMode.WallLine ||
                   toolMode == SandboxToolMode.WallBrush;
        }

        public bool TryGetPreviewJunctionPosition(string junctionId, out Vector2 previewPosition)
        {
            previewPosition = Vector2.zero;
            if (!isJunctionDragActive ||
                string.IsNullOrWhiteSpace(junctionId) ||
                !selectedJunctionIds.Contains(junctionId, StringComparer.Ordinal))
            {
                return false;
            }

            var floor = workspaceService?.ActiveFloor;
            var junction = floor?.wallJunctions.FirstOrDefault(candidate =>
                string.Equals(candidate.wallJunctionId, junctionId, StringComparison.Ordinal));
            if (junction == null)
            {
                return false;
            }

            var delta = draggedPrimaryJunctionPreviewPoint - draggedPrimaryJunctionStartPoint;
            previewPosition = junction.position + delta;
            return true;
        }

        public bool TryBeginHandleDrag(Vector2 worldPoint)
        {
            return TryGetNearestHandle(worldPoint, out var junctionId) &&
                   TryBeginJunctionDrag(junctionId, worldPoint);
        }

        public void UpdateHandleDragPreview(Vector2 worldPoint)
        {
            UpdateJunctionDragPreview(worldPoint);
        }

        public bool CommitHandleDrag()
        {
            return CommitJunctionDrag();
        }

        public void CancelHandleDrag()
        {
            CancelJunctionDrag();
        }

        private void HandleLineTool()
        {
            if (!SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var didCreate = wallAuthoringService.TryRegisterLinePoint(currentPointerWorldPoint, out _);
            if (didCreate)
            {
                UpdateStatus("Created wall segment. Click to continue chaining or right-click to end.");
            }
            else if (wallAuthoringService.HasPendingLineStart)
            {
                UpdateStatus("Placed wall start point. Move the cursor to preview the segment, then click to commit.");
            }
        }

        private void HandleBrushTool()
        {
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                if (wallAuthoringService.BeginBrushStrokeCapture(currentPointerWorldPoint))
                {
                    lastBrushPoint = currentPointerWorldPoint;
                    UpdateStatus("Recording wall brush stroke.");
                }
            }

            if (SandboxInputAdapter.GetMouseButton(0) && wallAuthoringService.IsBrushCaptureActive)
            {
                if (Vector2.Distance(lastBrushPoint, currentPointerWorldPoint) >= minimumBrushSampleDistance &&
                    wallAuthoringService.AppendBrushStrokePoint(currentPointerWorldPoint))
                {
                    lastBrushPoint = currentPointerWorldPoint;
                }
            }

            if (SandboxInputAdapter.GetMouseButtonUp(0) && wallAuthoringService.IsBrushCaptureActive)
            {
                wallAuthoringService.EndBrushStrokeCapture();
                UpdateStatus("Captured brush polyline. Accept or cancel it from the inspector.");
            }
        }

        private bool TryBeginJunctionDrag(string junctionId, Vector2 worldPoint)
        {
            if (string.IsNullOrWhiteSpace(junctionId))
            {
                return false;
            }

            var floor = workspaceService?.ActiveFloor;
            var junction = floor?.wallJunctions.FirstOrDefault(candidate =>
                string.Equals(candidate.wallJunctionId, junctionId, StringComparison.Ordinal));
            if (junction == null)
            {
                return false;
            }

            var isAdditive = SandboxInputAdapter.GetKey(KeyCode.LeftShift) || SandboxInputAdapter.GetKey(KeyCode.RightShift);
            if (!isAdditive)
            {
                selectedJunctionIds = new List<string> { junctionId };
            }
            else if (!selectedJunctionIds.Contains(junctionId, StringComparer.Ordinal))
            {
                selectedJunctionIds.Add(junctionId);
            }

            selectedJunctionIds = selectedJunctionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            selectionService?.ClearSelection();

            isJunctionDragActive = true;
            draggedPrimaryJunctionId = junctionId;
            draggedPrimaryJunctionStartPoint = junction.position;
            draggedPrimaryJunctionPreviewPoint = junction.position;
            dragPreviewSnapResult = new SandboxWallSnapResult(junction.position, SandboxWallSnapTargetKind.None, string.Empty);
            ResolveLegacyDraggedHandleInfo(floor, junctionId, out draggedWallSegmentId, out draggedHandleIsStart);
            inputRouter?.SetPointerOverHandle(true);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
            UpdateStatus(selectedJunctionIds.Count > 1
                ? "Dragging selected wall nodes. Release to move the full set."
                : "Dragging wall node. Release to commit the corner move.");
            return true;
        }

        private void UpdateJunctionDragPreview(Vector2 worldPoint)
        {
            if (!isJunctionDragActive || wallAuthoringService == null || workspaceService == null)
            {
                return;
            }

            var anchorPoint = wallAuthoringService.ResolveJunctionDragAnchor(
                workspaceService.ActiveFloorId,
                draggedPrimaryJunctionId,
                selectedJunctionIds);
            dragPreviewSnapResult = wallSnappingService != null
                ? wallSnappingService.SnapPoint(workspaceService.ActiveFloorId, worldPoint, anchorPoint)
                : new SandboxWallSnapResult(worldPoint, SandboxWallSnapTargetKind.None, string.Empty);
            draggedPrimaryJunctionPreviewPoint = dragPreviewSnapResult.position;
            inputRouter?.SetPointerOverHandle(true);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
        }

        private bool CommitJunctionDrag()
        {
            if (!isJunctionDragActive || wallAuthoringService == null)
            {
                return false;
            }

            var didCommit = wallAuthoringService.MoveWallJunctions(
                draggedPrimaryJunctionId,
                selectedJunctionIds,
                draggedPrimaryJunctionPreviewPoint);
            if (didCommit)
            {
                UpdateStatus(selectedJunctionIds.Count > 1 ? "Moved wall nodes." : "Moved wall node.");
            }

            ClearJunctionDragState();
            return didCommit;
        }

        private void CancelJunctionDrag()
        {
            if (!isJunctionDragActive)
            {
                return;
            }

            ClearJunctionDragState();
            UpdateStatus("Cancelled wall node drag.");
        }

        private bool TryGetNearestHandle(Vector2 worldPoint, out string junctionId)
        {
            junctionId = string.Empty;
            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return false;
            }

            var visibleJunctionIds = GetVisibleHandleJunctionIds(floor);
            var bestDistance = float.MaxValue;
            foreach (var candidateJunctionId in visibleJunctionIds)
            {
                var junction = floor.wallJunctions.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallJunctionId, candidateJunctionId, StringComparison.Ordinal));
                if (junction == null)
                {
                    continue;
                }

                var distance = Vector2.Distance(worldPoint, junction.position);
                if (distance <= handleHitRadius && distance < bestDistance)
                {
                    bestDistance = distance;
                    junctionId = junction.wallJunctionId;
                }
            }

            return !string.IsNullOrWhiteSpace(junctionId);
        }

        private IReadOnlyCollection<string> GetVisibleHandleJunctionIds(FloorData floor)
        {
            var visibleIds = new HashSet<string>(selectedJunctionIds, StringComparer.Ordinal);
            if (selectionService == null)
            {
                return visibleIds;
            }

            for (var i = 0; i < selectionService.SelectedObjectIds.Count; i += 1)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, selectionService.SelectedObjectIds[i], StringComparison.Ordinal));
                if (wall == null)
                {
                    continue;
                }

                visibleIds.Add(wall.startJunctionId);
                visibleIds.Add(wall.endJunctionId);
            }

            return visibleIds;
        }

        private void UpdateLinePreviewPoint()
        {
            currentLinePreviewPoint = currentPointerWorldPoint;
            if (workspaceService?.ActiveFloor == null || !wallAuthoringService.HasPendingLineStart)
            {
                return;
            }

            currentLinePreviewPoint = wallSnappingService != null
                ? wallSnappingService.SnapPoint(
                    workspaceService.ActiveFloorId,
                    currentPointerWorldPoint,
                    wallAuthoringService.PendingLineStart).position
                : currentPointerWorldPoint;
        }

        private void ClearJunctionDragState()
        {
            isJunctionDragActive = false;
            draggedPrimaryJunctionId = string.Empty;
            draggedWallSegmentId = string.Empty;
            draggedHandleIsStart = false;
            draggedPrimaryJunctionStartPoint = Vector2.zero;
            draggedPrimaryJunctionPreviewPoint = Vector2.zero;
            dragPreviewSnapResult = new SandboxWallSnapResult(Vector2.zero, SandboxWallSnapTargetKind.None, string.Empty);
            inputRouter?.SetPointerOverHandle(false);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
        }

        private static void ResolveLegacyDraggedHandleInfo(FloorData floor, string junctionId, out string wallSegmentId, out bool isStartHandle)
        {
            wallSegmentId = string.Empty;
            isStartHandle = false;
            if (floor == null || string.IsNullOrWhiteSpace(junctionId))
            {
                return;
            }

            var wall = floor.wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.startJunctionId, junctionId, StringComparison.Ordinal) ||
                string.Equals(candidate.endJunctionId, junctionId, StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            wallSegmentId = wall.wallSegmentId;
            isStartHandle = string.Equals(wall.startJunctionId, junctionId, StringComparison.Ordinal);
        }

        private void OnDisable()
        {
            wallSnappingService?.SetTemporarySnappingBypass(false);
        }

        private void OnDestroy()
        {
            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged -= HandleToolModeChanged;
            }

            wallSnappingService?.SetTemporarySnappingBypass(false);
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
    }
}
