using System;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
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

        private SandboxToolStateService toolStateService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxWallOverlayRenderer wallOverlayRenderer;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxEditorQoLService editorQoLService;
        private SandboxPreviewService previewService;
        private Vector2 lastBrushPoint;
        private bool isHandleDragActive;
        private string draggedWallSegmentId = string.Empty;
        private bool draggedHandleIsStart;
        private Vector2 draggedHandlePreviewPoint;

        public bool IsHandleDragActive => isHandleDragActive;
        public string DraggedWallSegmentId => draggedWallSegmentId;
        public bool DraggedHandleIsStart => draggedHandleIsStart;
        public Vector2 DraggedHandlePreviewPoint => draggedHandlePreviewPoint;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            wallOverlayRenderer = FindAnyObjectByType<SandboxWallOverlayRenderer>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
        }

        private void Update()
        {
            if (toolStateService == null || wallAuthoringService == null)
            {
                return;
            }

            if (previewService != null && previewService.IsPreviewModeActive)
            {
                inputRouter?.SetPointerOverHandle(false);
                return;
            }

            var currentToolMode = toolStateService.CurrentToolMode;
            if (!SupportsWallHandleEditing(currentToolMode))
            {
                if (isHandleDragActive)
                {
                    CancelHandleDrag();
                }

                inputRouter?.SetPointerOverHandle(false);
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            var isHoveringHandle = TryGetNearestHandle(worldPoint, out var hoveredWallId, out var hoveredIsStartHandle);
            if (!isHandleDragActive)
            {
                inputRouter?.SetPointerOverHandle(isHoveringHandle);
            }

            if (isHandleDragActive)
            {
                if (SandboxInputAdapter.GetMouseButton(0))
                {
                    UpdateHandleDragPreview(worldPoint);
                }

                if (SandboxInputAdapter.GetMouseButtonUp(0))
                {
                    CommitHandleDrag();
                }

                if (SandboxInputAdapter.GetMouseButtonDown(1))
                {
                    CancelHandleDrag();
                }

                return;
            }

            if (SandboxInputAdapter.GetMouseButtonDown(0) && isHoveringHandle)
            {
                TryBeginHandleDrag(worldPoint, hoveredWallId, hoveredIsStartHandle);
                return;
            }

            if (SandboxInputAdapter.GetMouseButtonDown(1))
            {
                wallAuthoringService.CancelLinePlacement();
                wallAuthoringService.CancelBrushStroke();
                UpdateStatus("Cancelled wall authoring preview.");
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

            switch (toolStateService.CurrentToolMode)
            {
                case SandboxToolMode.WallLine:
                    HandleLineTool();
                    break;
                case SandboxToolMode.WallBrush:
                    HandleBrushTool();
                    break;
            }
        }

        private static bool SupportsWallHandleEditing(SandboxToolMode toolMode)
        {
            return toolMode == SandboxToolMode.WallLine || toolMode == SandboxToolMode.WallBrush;
        }

        public bool TryBeginHandleDrag(Vector2 worldPoint)
        {
            return TryGetNearestHandle(worldPoint, out var wallSegmentId, out var isStartHandle) &&
                   TryBeginHandleDrag(worldPoint, wallSegmentId, isStartHandle);
        }

        public void UpdateHandleDragPreview(Vector2 worldPoint)
        {
            if (!isHandleDragActive)
            {
                return;
            }

            draggedHandlePreviewPoint = worldPoint;
            inputRouter?.SetPointerOverHandle(true);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
        }

        public bool CommitHandleDrag()
        {
            if (!isHandleDragActive)
            {
                return false;
            }

            var didCommit = draggedHandleIsStart
                ? wallAuthoringService.MoveWallStartHandle(draggedWallSegmentId, draggedHandlePreviewPoint)
                : wallAuthoringService.MoveWallEndHandle(draggedWallSegmentId, draggedHandlePreviewPoint);

            if (didCommit)
            {
                UpdateStatus("Moved wall handle.");
            }

            ClearHandleDragState();
            return didCommit;
        }

        public void CancelHandleDrag()
        {
            if (!isHandleDragActive)
            {
                return;
            }

            ClearHandleDragState();
            UpdateStatus("Cancelled wall handle drag.");
        }

        private void HandleLineTool()
        {
            if (!SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            var didCreate = wallAuthoringService.TryRegisterLinePoint(worldPoint, out _);
            if (didCreate)
            {
                UpdateStatus("Created wall centerline segment.");
            }
            else if (wallAuthoringService.HasPendingLineStart)
            {
                UpdateStatus("Placed wall start point. Click again to finish the segment.");
            }
        }

        private void HandleBrushTool()
        {
            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                if (wallAuthoringService.BeginBrushStrokeCapture(worldPoint))
                {
                    lastBrushPoint = worldPoint;
                    UpdateStatus("Recording wall brush stroke.");
                }
            }

            if (SandboxInputAdapter.GetMouseButton(0) && wallAuthoringService.IsBrushCaptureActive)
            {
                if (Vector2.Distance(lastBrushPoint, worldPoint) >= minimumBrushSampleDistance &&
                    wallAuthoringService.AppendBrushStrokePoint(worldPoint))
                {
                    lastBrushPoint = worldPoint;
                }
            }

            if (SandboxInputAdapter.GetMouseButtonUp(0) && wallAuthoringService.IsBrushCaptureActive)
            {
                wallAuthoringService.EndBrushStrokeCapture();
                UpdateStatus("Captured brush polyline. Accept or cancel it from the inspector.");
            }
        }

        private bool TryBeginHandleDrag(Vector2 worldPoint, string wallSegmentId, bool isStartHandle)
        {
            if (string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return false;
            }

            isHandleDragActive = true;
            draggedWallSegmentId = wallSegmentId;
            draggedHandleIsStart = isStartHandle;
            draggedHandlePreviewPoint = worldPoint;
            selectionService?.ReplaceSelection(new[] { wallSegmentId });
            inputRouter?.SetPointerOverHandle(true);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
            UpdateStatus("Dragging wall handle. Release to commit the new endpoint.");
            return true;
        }

        private bool TryGetNearestHandle(Vector2 worldPoint, out string wallSegmentId, out bool isStartHandle)
        {
            wallSegmentId = string.Empty;
            isStartHandle = false;

            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return false;
            }

            var bestDistance = float.MaxValue;
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (visualOrganizationService != null &&
                    (!visualOrganizationService.IsTypeVisible(SandboxVisualObjectType.Wall) ||
                     visualOrganizationService.IsObjectHidden(wall.wallSegmentId) ||
                     visualOrganizationService.IsObjectLocked(wall.wallSegmentId) ||
                     visualOrganizationService.IsTypeLocked(SandboxVisualObjectType.Wall)))
                {
                    continue;
                }

                if (editorQoLService != null &&
                    !editorQoLService.IsObjectVisibleForIsolation(wall.wallSegmentId, SandboxVisualObjectType.Wall))
                {
                    continue;
                }

                var startDistance = Vector2.Distance(worldPoint, wall.startPoint);
                if (startDistance <= handleHitRadius && startDistance < bestDistance)
                {
                    bestDistance = startDistance;
                    wallSegmentId = wall.wallSegmentId;
                    isStartHandle = true;
                }

                var endDistance = Vector2.Distance(worldPoint, wall.endPoint);
                if (endDistance <= handleHitRadius && endDistance < bestDistance)
                {
                    bestDistance = endDistance;
                    wallSegmentId = wall.wallSegmentId;
                    isStartHandle = false;
                }
            }

            return !string.IsNullOrWhiteSpace(wallSegmentId);
        }

        private void ClearHandleDragState()
        {
            isHandleDragActive = false;
            draggedWallSegmentId = string.Empty;
            draggedHandleIsStart = false;
            draggedHandlePreviewPoint = Vector2.zero;
            inputRouter?.SetPointerOverHandle(false);
            wallOverlayRenderer ??= FindAnyObjectByType<SandboxWallOverlayRenderer>();
            wallOverlayRenderer?.Refresh();
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
