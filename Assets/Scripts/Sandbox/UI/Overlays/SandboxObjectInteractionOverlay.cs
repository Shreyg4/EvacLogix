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
        public enum EraseMode
        {
            Point = 0,
            Brush = 1,
        }

        private const float WallHitPadding = 0.3f;
        private const float OpeningHitRadius = 0.5f;
        private const float StairHitRadius = 0.55f;
        private const float RectangleHandleHitRadius = 0.28f;
        private const float RegionEdgeHitRadius = 0.35f;
        private const float SelectionDragThreshold = 0.2f;
        private const float MinEraseBrushRadius = 0.35f;
        private const float MaxEraseBrushRadius = 3.5f;
        private const float DefaultEraseBrushRadius = 0.9f;
        private static readonly Color ErasePanelBackdropColor = new(0.14f, 0.07f, 0.07f, 0.88f);
        private static readonly Color EraseAccentColor = new(0.97f, 0.37f, 0.31f, 0.96f);
        private static readonly Color EraseSecondaryColor = new(1f, 0.79f, 0.31f, 0.94f);
        private static readonly Color EraseTextColor = new(1f, 0.96f, 0.94f, 1f);
        private static readonly Color EraseMutedTextColor = new(0.92f, 0.82f, 0.79f, 1f);

        private enum SandboxHitKind
        {
            None = 0,
            Wall = 1,
            Door = 2,
            Window = 3,
            Exit = 4,
            Obstacle = 5,
            Stair = 6,
            Teleport = 7,
            Region = 8,
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
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxClipboardService clipboardService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxMeasurementService measurementService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private Texture2D solidTexture;
        private Font overlayFont;
        private GUIStyle erasePanelStyle;
        private GUIStyle eraseTitleStyle;
        private GUIStyle eraseBodyStyle;
        private GUIStyle eraseBadgeStyle;
        private GUIStyle eraseButtonStyle;
        private GUIStyle eraseActiveButtonStyle;
        private Rect erasePanelRect;
        private EraseMode eraseMode;
        private float eraseBrushRadius = DefaultEraseBrushRadius;
        private bool eraseBrushStrokeActive;
        private int eraseBrushStrokeEraseCount;
        private readonly HashSet<string> erasedObjectIdsThisBrushStroke = new();
        private bool hasPendingSelectPress;
        private bool pendingAdditiveSelect;
        private SandboxHitResult pendingPressedHit;
        private Vector2 pendingPressWorldPoint;
        private bool isSelectionDragActive;
        private string draggedObjectId = string.Empty;
        private SandboxHitKind draggedHitKind = SandboxHitKind.None;
        private Vector2 selectionDragStartWorldPoint;
        private Vector2 selectionDragCurrentWorldPoint;
        private bool isRectangleHandleDragActive;
        private string draggedRectangleObjectId = string.Empty;
        private SandboxHitKind draggedRectangleHitKind = SandboxHitKind.None;
        private int draggedRectangleHandleIndex = -1;
        private Vector2 draggedRectanglePreviewCenter;
        private Vector2 draggedRectanglePreviewSize;
        private float draggedRectanglePreviewRotationDegrees;
        private Vector2 draggedRectangleAnchorWorld;

        private void Awake()
        {
            EnsureDependencies();
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
            switch (toolStateService.CurrentToolMode)
            {
                case SandboxToolMode.Select:
                    if (currentTarget == SandboxInputTarget.World)
                    {
                        HandleSelectTool();
                    }

                    break;
                case SandboxToolMode.Erase:
                    HandleEraseTool(currentTarget);
                    break;
            }
        }

        private void OnGUI()
        {
            EnsureDependencies();
            EnsureGuiResources();
            if (!IsEraseVisualAidVisible)
            {
                if (eraseBrushStrokeActive)
                {
                    ResetBrushEraseStroke();
                }

                return;
            }

            DrawErasePanel();
            DrawEraseVisualAid();
        }

        public bool IsSelectionDragActive => isSelectionDragActive;
        public string DraggedObjectId => draggedObjectId;
        public Vector2 SelectionDragStartWorldPoint => selectionDragStartWorldPoint;
        public Vector2 SelectionDragCurrentWorldPoint => selectionDragCurrentWorldPoint;
        public bool IsRectangleHandleDragActive => isRectangleHandleDragActive;
        public string DraggedRectangleObjectId => draggedRectangleObjectId;
        public SandboxVisualObjectType? DraggedRectangleObjectType => draggedRectangleHitKind switch
        {
            SandboxHitKind.Exit => SandboxVisualObjectType.Exit,
            SandboxHitKind.Obstacle => SandboxVisualObjectType.Obstacle,
            SandboxHitKind.Teleport => SandboxVisualObjectType.Teleport,
            _ => null,
        };
        public Vector2 DraggedRectanglePreviewCenter => draggedRectanglePreviewCenter;
        public Vector2 DraggedRectanglePreviewSize => draggedRectanglePreviewSize;
        public float DraggedRectanglePreviewRotationDegrees => draggedRectanglePreviewRotationDegrees;
        public bool IsEraseVisualAidVisible => toolStateService != null && toolStateService.CurrentToolMode == SandboxToolMode.Erase;
        public EraseMode CurrentEraseMode => eraseMode;
        public bool BrushEraseEnabled => eraseMode == EraseMode.Brush;
        public float EraseBrushRadius => eraseBrushRadius;

        private void EnsureDependencies()
        {
            if (toolStateService == null)
            {
                toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            }

            if (workspaceService == null)
            {
                workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            }

            if (workspaceStateService == null)
            {
                workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            }

            if (selectionService == null)
            {
                selectionService = FindAnyObjectByType<SandboxSelectionService>();
            }

            if (inputRouter == null)
            {
                inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            }

            if (statusBar == null)
            {
                statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            }

            if (clipboardService == null)
            {
                clipboardService = FindAnyObjectByType<SandboxClipboardService>();
            }

            if (wallAuthoringService == null)
            {
                wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            }

            if (measurementService == null)
            {
                measurementService = FindAnyObjectByType<SandboxMeasurementService>();
            }

            if (visualOrganizationService == null)
            {
                visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            }

            if (previewService == null)
            {
                previewService = FindAnyObjectByType<SandboxPreviewService>();
            }

            if (semanticObjectAuthoringService == null)
            {
                semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            }
        }

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

        public void SetBrushEraseEnabled(bool enabled)
        {
            eraseMode = enabled ? EraseMode.Brush : EraseMode.Point;
            if (!enabled)
            {
                ResetBrushEraseStroke();
            }
        }

        public void SetEraseBrushRadius(float radius)
        {
            eraseBrushRadius = Mathf.Clamp(radius, MinEraseBrushRadius, MaxEraseBrushRadius);
        }

        public int EraseWithinBrush(Vector2 worldPoint, float? overrideBrushRadius = null)
        {
            var hits = CollectHitsWithinBrush(worldPoint, overrideBrushRadius ?? eraseBrushRadius);
            return EraseHits(hits, suppressEmptyMessage: false);
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
            if (isRectangleHandleDragActive)
            {
                if (SandboxInputAdapter.GetMouseButton(0))
                {
                    UpdateRectangleHandleDragPreview(worldPoint);
                }

                if (SandboxInputAdapter.GetMouseButtonUp(0))
                {
                    CommitRectangleHandleDrag();
                }

                if (SandboxInputAdapter.GetMouseButtonDown(1))
                {
                    CancelRectangleHandleDrag();
                }

                return;
            }

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

        private void HandleEraseTool(SandboxInputTarget currentTarget)
        {
            var screenPosition = SandboxInputAdapter.PointerScreenPosition;
            var guiPoint = ScreenToGuiPoint(screenPosition);
            var pointerOverErasePanel = erasePanelRect.Contains(guiPoint);
            var pointerCanEditWorld = currentTarget == SandboxInputTarget.World && !pointerOverErasePanel;
            if (eraseMode == EraseMode.Point)
            {
                if (SandboxInputAdapter.GetMouseButtonDown(0) && pointerCanEditWorld)
                {
                    EraseAtWorldPoint(ScreenToWorldPoint(screenPosition));
                }

                return;
            }

            var worldPoint = ScreenToWorldPoint(screenPosition);
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                StartBrushEraseStroke(worldPoint, pointerCanEditWorld);
            }
            else if (eraseBrushStrokeActive && SandboxInputAdapter.GetMouseButton(0))
            {
                ContinueBrushEraseStroke(worldPoint, pointerCanEditWorld);
            }

            if (eraseBrushStrokeActive && SandboxInputAdapter.GetMouseButtonUp(0))
            {
                FinishBrushEraseStroke();
            }
        }

        private void StartBrushEraseStroke(Vector2 worldPoint, bool pointerCanEditWorld)
        {
            eraseBrushStrokeActive = true;
            eraseBrushStrokeEraseCount = 0;
            erasedObjectIdsThisBrushStroke.Clear();
            if (pointerCanEditWorld)
            {
                eraseBrushStrokeEraseCount += EraseWithinBrushStroke(worldPoint);
            }
        }

        private void ContinueBrushEraseStroke(Vector2 worldPoint, bool pointerCanEditWorld)
        {
            if (!pointerCanEditWorld)
            {
                return;
            }

            eraseBrushStrokeEraseCount += EraseWithinBrushStroke(worldPoint);
        }

        private void FinishBrushEraseStroke()
        {
            if (eraseBrushStrokeEraseCount <= 0)
            {
                UpdateStatus("Nothing to erase inside the brush.");
            }
            else
            {
                var objectLabel = eraseBrushStrokeEraseCount == 1 ? "object" : "objects";
                UpdateStatus($"Erased {eraseBrushStrokeEraseCount} {objectLabel} with the brush.");
            }

            ResetBrushEraseStroke();
        }

        private void ResetBrushEraseStroke()
        {
            eraseBrushStrokeActive = false;
            eraseBrushStrokeEraseCount = 0;
            erasedObjectIdsThisBrushStroke.Clear();
        }

        private void BeginSelectionPress(Vector2 worldPoint)
        {
            if (TryBeginRectangleHandleDrag(worldPoint))
            {
                hasPendingSelectPress = false;
                return;
            }

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

        private bool TryBeginRectangleHandleDrag(Vector2 worldPoint)
        {
            if (selectionService == null || selectionService.SelectedObjectIds.Count != 1)
            {
                return false;
            }

            var selectedId = selectionService.SelectedObjectIds[0];
            if (TryResolveResizableRect(selectedId, out var hitKind, out var center, out var size, out var rotationDegrees))
            {
                var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
                for (var i = 0; i < corners.Length; i += 1)
                {
                    if (Vector2.Distance(worldPoint, corners[i]) > RectangleHandleHitRadius)
                    {
                        continue;
                    }

                    isRectangleHandleDragActive = true;
                    draggedRectangleObjectId = selectedId;
                    draggedRectangleHitKind = hitKind;
                    draggedRectangleHandleIndex = i;
                    draggedRectanglePreviewCenter = center;
                    draggedRectanglePreviewSize = size;
                    draggedRectanglePreviewRotationDegrees = rotationDegrees;
                    draggedRectangleAnchorWorld = corners[(i + 2) % corners.Length];
                    UpdateStatus("Dragging rectangle corner handle. Release to resize.");
                    return true;
                }
            }

            return false;
        }

        private void UpdateRectangleHandleDragPreview(Vector2 worldPoint)
        {
            if (!isRectangleHandleDragActive)
            {
                return;
            }

            var rotation = Quaternion.Euler(0f, 0f, draggedRectanglePreviewRotationDegrees);
            var inverseRotation = Quaternion.Inverse(rotation);
            var anchorInRotationSpace = (Vector2)(inverseRotation * new Vector3(draggedRectangleAnchorWorld.x, draggedRectangleAnchorWorld.y, 0f));
            var currentInRotationSpace = (Vector2)(inverseRotation * new Vector3(worldPoint.x, worldPoint.y, 0f));
            var min = Vector2.Min(anchorInRotationSpace, currentInRotationSpace);
            var max = Vector2.Max(anchorInRotationSpace, currentInRotationSpace);
            var nextSize = max - min;
            if (nextSize.x <= 0.05f || nextSize.y <= 0.05f)
            {
                return;
            }

            var centerInRotationSpace = (anchorInRotationSpace + currentInRotationSpace) * 0.5f;
            draggedRectanglePreviewCenter = rotation * new Vector3(centerInRotationSpace.x, centerInRotationSpace.y, 0f);
            draggedRectanglePreviewSize = nextSize;
        }

        private void CommitRectangleHandleDrag()
        {
            if (!isRectangleHandleDragActive || semanticObjectAuthoringService == null)
            {
                return;
            }

            var didUpdate = false;
            switch (draggedRectangleHitKind)
            {
                case SandboxHitKind.Exit:
                    if (TryFindExit(draggedRectangleObjectId, out _, out var exitZone))
                    {
                        didUpdate = semanticObjectAuthoringService.UpdateExit(
                            exitZone.exitZoneId,
                            draggedRectanglePreviewCenter,
                            draggedRectanglePreviewSize,
                            draggedRectanglePreviewRotationDegrees,
                            exitZone.width,
                            exitZone.capacity,
                            exitZone.priority,
                            exitZone.name,
                            exitZone.tags,
                            exitZone.metadataFields);
                    }

                    break;
                case SandboxHitKind.Obstacle:
                    if (TryFindObstacle(draggedRectangleObjectId, out _, out var obstacle))
                    {
                        didUpdate = semanticObjectAuthoringService.UpdateObstacle(
                            obstacle.obstacleId,
                            draggedRectanglePreviewCenter,
                            draggedRectanglePreviewSize,
                            draggedRectanglePreviewRotationDegrees,
                            obstacle.discourageWeight,
                            obstacle.movementSpeedPenalty,
                            obstacle.name,
                            obstacle.tags,
                            obstacle.metadataFields);
                    }

                    break;
                case SandboxHitKind.Teleport:
                    if (TryFindTeleport(draggedRectangleObjectId, out _, out var teleportPortal))
                    {
                        didUpdate = semanticObjectAuthoringService.UpdateTeleportPortal(
                            teleportPortal.teleportPortalId,
                            draggedRectanglePreviewCenter,
                            draggedRectanglePreviewSize,
                            draggedRectanglePreviewRotationDegrees,
                            teleportPortal.name,
                            teleportPortal.kind,
                            teleportPortal.travelCost,
                            teleportPortal.isPairEnabled,
                            teleportPortal.tags,
                            teleportPortal.metadataFields);
                    }

                    break;
            }

            ClearRectangleHandleDragState();
            measurementService?.RefreshSelectionReadout();
            UpdateStatus(didUpdate ? "Resized selection." : "Resize cancelled.");
        }

        private void CancelRectangleHandleDrag()
        {
            if (!isRectangleHandleDragActive)
            {
                return;
            }

            ClearRectangleHandleDragState();
            UpdateStatus("Cancelled rectangle resize.");
        }

        private void ClearRectangleHandleDragState()
        {
            isRectangleHandleDragActive = false;
            draggedRectangleObjectId = string.Empty;
            draggedRectangleHitKind = SandboxHitKind.None;
            draggedRectangleHandleIndex = -1;
            draggedRectanglePreviewCenter = Vector2.zero;
            draggedRectanglePreviewSize = Vector2.zero;
            draggedRectanglePreviewRotationDegrees = 0f;
            draggedRectangleAnchorWorld = Vector2.zero;
        }

        private void EnsureGuiResources()
        {
            if (solidTexture == null)
            {
                solidTexture = Texture2D.whiteTexture;
            }

            if (overlayFont == null)
            {
                overlayFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (erasePanelStyle == null)
            {
                erasePanelStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(16, 16, 14, 14),
                    alignment = TextAnchor.UpperLeft
                };
            }

            if (eraseTitleStyle == null)
            {
                eraseTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    font = overlayFont,
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = EraseTextColor }
                };
            }

            if (eraseBodyStyle == null)
            {
                eraseBodyStyle = new GUIStyle(GUI.skin.label)
                {
                    font = overlayFont,
                    fontSize = 12,
                    wordWrap = true,
                    normal = { textColor = EraseMutedTextColor }
                };
            }

            if (eraseBadgeStyle == null)
            {
                eraseBadgeStyle = new GUIStyle(GUI.skin.label)
                {
                    font = overlayFont,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.2f, 0.08f, 0.08f, 1f) }
                };
            }

            if (eraseButtonStyle == null)
            {
                eraseButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    font = overlayFont,
                    fontSize = 12
                };
            }

            if (eraseActiveButtonStyle == null)
            {
                eraseActiveButtonStyle = new GUIStyle(eraseButtonStyle)
                {
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private void DrawErasePanel()
        {
            var panelWidth = Mathf.Min(Screen.width - 32f, 360f);
            erasePanelRect = new Rect(
                Mathf.Max(16f, (Screen.width - panelWidth) * 0.5f),
                Mathf.Max(96f, Screen.height - 206f),
                panelWidth,
                136f);
            DrawFilledRect(erasePanelRect, ErasePanelBackdropColor);
            GUI.Box(erasePanelRect, GUIContent.none, erasePanelStyle);

            var contentRect = new Rect(erasePanelRect.x + 16f, erasePanelRect.y + 14f, erasePanelRect.width - 32f, erasePanelRect.height - 28f);
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 22f), "Erase Guide", eraseTitleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 22f, contentRect.width, 34f), GetEraseInstructionText(), eraseBodyStyle);

            var pointButtonRect = new Rect(contentRect.x, contentRect.y + 58f, 92f, 28f);
            var brushButtonRect = new Rect(contentRect.x + 100f, contentRect.y + 58f, 92f, 28f);
            if (GUI.Button(pointButtonRect, "Point", eraseMode == EraseMode.Point ? eraseActiveButtonStyle : eraseButtonStyle))
            {
                SetBrushEraseEnabled(false);
            }

            if (GUI.Button(brushButtonRect, "Brush", eraseMode == EraseMode.Brush ? eraseActiveButtonStyle : eraseButtonStyle))
            {
                SetBrushEraseEnabled(true);
            }

            DrawModeBadge(pointButtonRect, eraseMode == EraseMode.Point ? EraseSecondaryColor : new Color(0.48f, 0.32f, 0.28f, 0.9f));
            DrawModeBadge(brushButtonRect, eraseMode == EraseMode.Brush ? EraseAccentColor : new Color(0.48f, 0.32f, 0.28f, 0.9f));

            var sliderLabelRect = new Rect(contentRect.x, contentRect.y + 92f, contentRect.width, 18f);
            var sliderRect = new Rect(contentRect.x, contentRect.y + 112f, contentRect.width - 96f, 18f);
            var valueRect = new Rect(contentRect.x + contentRect.width - 88f, contentRect.y + 108f, 88f, 22f);
            GUI.Label(sliderLabelRect, $"Brush Size ({(eraseBrushRadius * 2f):0.0} diameter)", eraseBodyStyle);
            var previousEnabled = GUI.enabled;
            GUI.enabled = eraseMode == EraseMode.Brush;
            var nextRadius = GUI.HorizontalSlider(sliderRect, eraseBrushRadius, MinEraseBrushRadius, MaxEraseBrushRadius);
            if (!Mathf.Approximately(nextRadius, eraseBrushRadius))
            {
                SetEraseBrushRadius(nextRadius);
            }

            GUI.Label(valueRect, $"R {eraseBrushRadius:0.00}", eraseBodyStyle);
            GUI.enabled = previousEnabled;
        }

        private void DrawEraseVisualAid()
        {
            var screenPosition = SandboxInputAdapter.PointerScreenPosition;
            var guiPoint = ScreenToGuiPoint(screenPosition);
            var target = inputRouter != null
                ? inputRouter.ResolvePointerTarget(screenPosition)
                : SandboxInputTarget.World;
            if (target != SandboxInputTarget.World || erasePanelRect.Contains(guiPoint))
            {
                return;
            }

            if (eraseMode == EraseMode.Point)
            {
                DrawMarker(guiPoint, "X", EraseAccentColor);
                return;
            }

            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return;
            }

            var worldPoint = ScreenToWorldPoint(screenPosition);
            var center = WorldToGuiPoint(cameraComponent, worldPoint);
            var edge = WorldToGuiPoint(cameraComponent, worldPoint + Vector2.right * eraseBrushRadius);
            var radiusPixels = Mathf.Max(12f, Mathf.Abs(edge.x - center.x));
            DrawCircleOutline(center, radiusPixels, eraseBrushStrokeActive ? EraseAccentColor : EraseSecondaryColor, 40, 2.5f);
            DrawMarker(center, "X", eraseBrushStrokeActive ? EraseAccentColor : EraseSecondaryColor, 20f);
        }

        private string GetEraseInstructionText()
        {
            return eraseMode == EraseMode.Point
                ? "Click a single object to remove it."
                : "Drag the brush over nearby geometry to scrub it away. Use the size slider for small cleanups.";
        }

        private void DrawModeBadge(Rect buttonRect, Color color)
        {
            var badgeRect = new Rect(buttonRect.x + buttonRect.width - 18f, buttonRect.y + 6f, 12f, 12f);
            DrawFilledRect(badgeRect, color);
        }

        private int EraseWithinBrushStroke(Vector2 worldPoint)
        {
            var hits = CollectHitsWithinBrush(worldPoint, eraseBrushRadius)
                .Where(hit => !erasedObjectIdsThisBrushStroke.Contains(hit.objectId))
                .ToList();
            if (hits.Count == 0)
            {
                return 0;
            }

            var erasedCount = 0;
            for (var i = 0; i < hits.Count; i += 1)
            {
                if (!EraseHit(hits[i]))
                {
                    continue;
                }

                erasedObjectIdsThisBrushStroke.Add(hits[i].objectId);
                erasedCount += 1;
            }

            if (erasedCount > 0)
            {
                selectionService?.ClearSelection();
                measurementService?.RefreshSelectionReadout();
            }

            return erasedCount;
        }

        private int EraseHits(IReadOnlyList<SandboxHitResult> hits, bool suppressEmptyMessage)
        {
            if (hits == null || hits.Count == 0)
            {
                if (!suppressEmptyMessage)
                {
                    UpdateStatus("Nothing to erase at that point.");
                }

                return 0;
            }

            var erasedCount = 0;
            for (var i = 0; i < hits.Count; i += 1)
            {
                if (EraseHit(hits[i]))
                {
                    erasedCount += 1;
                }
            }

            if (erasedCount > 0)
            {
                selectionService?.ClearSelection();
                measurementService?.RefreshSelectionReadout();
            }
            else if (!suppressEmptyMessage)
            {
                UpdateStatus("Could not erase the targeted objects. They may be locked or unavailable.");
            }

            return erasedCount;
        }

        private bool EraseHit(SandboxHitResult hit)
        {
            return hit.kind switch
            {
                SandboxHitKind.Wall => wallAuthoringService != null && wallAuthoringService.EraseWall(hit.objectId),
                _ => EraseNonWallObject(hit.objectId),
            };
        }

        private List<SandboxHitResult> CollectHitsWithinBrush(Vector2 worldPoint, float brushRadius)
        {
            var hits = new List<SandboxHitResult>();
            var knownObjectIds = new HashSet<string>(StringComparer.Ordinal);
            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return hits;
            }

            CollectWallsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectOpeningsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectExitsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectObstaclesInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectStairsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectTeleportsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);
            CollectRegionsInBrush(floor, worldPoint, brushRadius, hits, knownObjectIds);

            return hits
                .OrderBy(hit => GetBrushErasePriority(hit.kind))
                .ThenBy(hit => hit.score)
                .ToList();
        }

        private void CollectWallsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var wall in floor.wallSegments)
            {
                if (!IsInteractable(SandboxVisualObjectType.Wall, wall.wallSegmentId))
                {
                    continue;
                }

                var hitRadius = Mathf.Max(WallHitPadding, (wall.thickness * 0.5f) + WallHitPadding);
                var score = DistanceToSegment(worldPoint, wall.startPoint, wall.endPoint);
                if (score > hitRadius + brushRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Wall,
                        objectId = wall.wallSegmentId,
                        label = "wall segment",
                        score = score
                    });
            }
        }

        private void CollectOpeningsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var door in floor.doors)
            {
                if (!IsInteractable(SandboxVisualObjectType.Door, door.doorId) ||
                    !TryBuildOpeningBrushHit(floor, worldPoint, door.wallSegmentId, door.offsetAlongWall, door.width, brushRadius, out var score))
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
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
                    !TryBuildOpeningBrushHit(floor, worldPoint, window.wallSegmentId, window.offsetAlongWall, window.width, brushRadius, out var score))
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Window,
                        objectId = window.windowId,
                        label = "window",
                        score = score
                    });
            }
        }

        private void CollectExitsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var exitZone in floor.exits)
            {
                if (!IsInteractable(SandboxVisualObjectType.Exit, exitZone.exitZoneId))
                {
                    continue;
                }

                var score = DistanceToRotatedRect(worldPoint, exitZone.center, exitZone.size, exitZone.rotationDegrees);
                if (score > brushRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Exit,
                        objectId = exitZone.exitZoneId,
                        label = string.IsNullOrWhiteSpace(exitZone.name) ? "exit zone" : $"exit '{exitZone.name}'",
                        score = score
                    });
            }
        }

        private void CollectObstaclesInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var obstacle in floor.obstacles)
            {
                if (!IsInteractable(SandboxVisualObjectType.Obstacle, obstacle.obstacleId))
                {
                    continue;
                }

                var score = DistanceToRotatedRect(worldPoint, obstacle.center, obstacle.size, obstacle.rotationDegrees);
                if (score > brushRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Obstacle,
                        objectId = obstacle.obstacleId,
                        label = string.IsNullOrWhiteSpace(obstacle.name) ? "obstacle" : $"obstacle '{obstacle.name}'",
                        score = score
                    });
            }
        }

        private void CollectStairsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var stairPortal in floor.stairPortals)
            {
                if (!IsInteractable(SandboxVisualObjectType.Stair, stairPortal.stairPortalId))
                {
                    continue;
                }

                var score = DistanceToRotatedRect(worldPoint, stairPortal.localPosition, stairPortal.size, stairPortal.rotationDegrees);
                if (score > brushRadius + StairHitRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Stair,
                        objectId = stairPortal.stairPortalId,
                        label = string.IsNullOrWhiteSpace(stairPortal.name) ? "stair portal" : $"stair '{stairPortal.name}'",
                        score = score
                    });
            }
        }

        private void CollectTeleportsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (!IsInteractable(SandboxVisualObjectType.Teleport, teleportPortal.teleportPortalId))
                {
                    continue;
                }

                var score = DistanceToRotatedRect(worldPoint, teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees);
                if (score > brushRadius + StairHitRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Teleport,
                        objectId = teleportPortal.teleportPortalId,
                        label = string.IsNullOrWhiteSpace(teleportPortal.name) ? "teleport endpoint" : $"teleport '{teleportPortal.name}'",
                        score = score
                    });
            }
        }

        private void CollectRegionsInBrush(
            FloorData floor,
            Vector2 worldPoint,
            float brushRadius,
            List<SandboxHitResult> hits,
            HashSet<string> knownObjectIds)
        {
            foreach (var region in floor.regions)
            {
                if (!IsInteractable(SandboxVisualObjectType.Region, region.regionId) || region.polygonPoints.Count < 2)
                {
                    continue;
                }

                var isInside = IsPointInsidePolygon(worldPoint, region.polygonPoints);
                var score = isInside ? 0f : DistanceToPolygonEdges(worldPoint, region.polygonPoints);
                if (!isInside && score > RegionEdgeHitRadius + brushRadius)
                {
                    continue;
                }

                AddBrushHit(
                    hits,
                    knownObjectIds,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Region,
                        objectId = region.regionId,
                        label = string.IsNullOrWhiteSpace(region.name) ? "region" : $"region '{region.name}'",
                        score = score
                    });
            }
        }

        private static void AddBrushHit(List<SandboxHitResult> hits, HashSet<string> knownObjectIds, SandboxHitResult hit)
        {
            if (!knownObjectIds.Add(hit.objectId))
            {
                return;
            }

            hits.Add(hit);
        }

        private static int GetBrushErasePriority(SandboxHitKind kind)
        {
            return kind switch
            {
                SandboxHitKind.Door => 0,
                SandboxHitKind.Window => 1,
                SandboxHitKind.Exit => 2,
                SandboxHitKind.Obstacle => 3,
                SandboxHitKind.Stair => 4,
                SandboxHitKind.Teleport => 5,
                SandboxHitKind.Region => 6,
                SandboxHitKind.Wall => 7,
                _ => 8
            };
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
            EvaluateTeleports(floor, worldPoint, ref hit);
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

            if (floor.teleportPortals.Any(candidate => candidate.teleportPortalId == objectId))
            {
                hit = new SandboxHitResult { kind = SandboxHitKind.Teleport, objectId = objectId, label = "teleport endpoint" };
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
            return hitKind != SandboxHitKind.None;
        }

        private bool TryResolveResizableRect(
            string objectId,
            out SandboxHitKind hitKind,
            out Vector2 center,
            out Vector2 size,
            out float rotationDegrees)
        {
            hitKind = SandboxHitKind.None;
            center = Vector2.zero;
            size = Vector2.zero;
            rotationDegrees = 0f;

            if (TryFindExit(objectId, out _, out var exitZone))
            {
                hitKind = SandboxHitKind.Exit;
                center = exitZone.center;
                size = exitZone.size;
                rotationDegrees = exitZone.rotationDegrees;
                return true;
            }

            if (TryFindObstacle(objectId, out _, out var obstacle))
            {
                hitKind = SandboxHitKind.Obstacle;
                center = obstacle.center;
                size = obstacle.size;
                rotationDegrees = obstacle.rotationDegrees;
                return true;
            }

            if (TryFindTeleport(objectId, out _, out var teleportPortal))
            {
                hitKind = SandboxHitKind.Teleport;
                center = teleportPortal.localPosition;
                size = teleportPortal.size;
                rotationDegrees = teleportPortal.rotationDegrees;
                return true;
            }

            return false;
        }

        private bool TryFindObstacle(string objectId, out FloorData floor, out ObstacleData obstacle)
        {
            floor = null;
            obstacle = null;
            var project = workspaceService?.ActiveProject;
            if (project?.floors == null)
            {
                return false;
            }

            foreach (var candidateFloor in project.floors)
            {
                obstacle = candidateFloor.obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, objectId, StringComparison.Ordinal));
                if (obstacle == null)
                {
                    continue;
                }

                floor = candidateFloor;
                return true;
            }

            return false;
        }

        private bool TryFindExit(string objectId, out FloorData floor, out ExitZoneData exitZone)
        {
            floor = null;
            exitZone = null;
            var project = workspaceService?.ActiveProject;
            if (project?.floors == null)
            {
                return false;
            }

            foreach (var candidateFloor in project.floors)
            {
                exitZone = candidateFloor.exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, objectId, StringComparison.Ordinal));
                if (exitZone == null)
                {
                    continue;
                }

                floor = candidateFloor;
                return true;
            }

            return false;
        }

        private bool TryFindTeleport(string objectId, out FloorData floor, out TeleportPortalData teleportPortal)
        {
            floor = null;
            teleportPortal = null;
            var project = workspaceService?.ActiveProject;
            if (project?.floors == null)
            {
                return false;
            }

            foreach (var candidateFloor in project.floors)
            {
                teleportPortal = candidateFloor.teleportPortals.FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, objectId, StringComparison.Ordinal));
                if (teleportPortal == null)
                {
                    continue;
                }

                floor = candidateFloor;
                return true;
            }

            return false;
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

                var distance = DistanceToRotatedRect(worldPoint, stairPortal.localPosition, stairPortal.size, stairPortal.rotationDegrees);
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

        private void EvaluateTeleports(FloorData floor, Vector2 worldPoint, ref SandboxHitResult bestHit)
        {
            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (!IsInteractable(SandboxVisualObjectType.Teleport, teleportPortal.teleportPortalId))
                {
                    continue;
                }

                var distance = DistanceToRotatedRect(worldPoint, teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees);
                if (distance > StairHitRadius)
                {
                    continue;
                }

                TryPromoteHit(
                    ref bestHit,
                    new SandboxHitResult
                    {
                        kind = SandboxHitKind.Teleport,
                        objectId = teleportPortal.teleportPortalId,
                        label = string.IsNullOrWhiteSpace(teleportPortal.name) ? "teleport endpoint" : $"teleport '{teleportPortal.name}'",
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
            var worldWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(
                workspaceService,
                workspaceStateService,
                floor,
                openingWidth);
            var halfWidth = worldWidth * 0.5f;
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

        private bool TryBuildOpeningBrushHit(
            FloorData floor,
            Vector2 worldPoint,
            string wallSegmentId,
            float openingOffset,
            float openingWidth,
            float brushRadius,
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
            var worldWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(
                workspaceService,
                workspaceStateService,
                floor,
                openingWidth);
            var halfWidth = worldWidth * 0.5f;
            var segmentStart = wall.startPoint + wallDirection * Mathf.Clamp(openingOffset - halfWidth, 0f, wallLength);
            var segmentEnd = wall.startPoint + wallDirection * Mathf.Clamp(openingOffset + halfWidth, 0f, wallLength);
            score = DistanceToSegment(worldPoint, segmentStart, segmentEnd);
            return score <= OpeningHitRadius + brushRadius;
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

        private static float DistanceToRotatedRect(Vector2 worldPoint, Vector2 center, Vector2 size, float rotationDegrees)
        {
            TryPointInRotatedRect(worldPoint, center, size, rotationDegrees, out var localPoint);
            var halfSize = size * 0.5f;
            var deltaX = Mathf.Max(Mathf.Abs(localPoint.x) - halfSize.x, 0f);
            var deltaY = Mathf.Max(Mathf.Abs(localPoint.y) - halfSize.y, 0f);
            return Mathf.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        }

        private static Vector2[] BuildRotatedRectCorners(Vector2 center, Vector2 size, float rotationDegrees)
        {
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            return new[]
            {
                center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(-half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, -half.y, 0f))
            };
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

        private static Vector2 ScreenToGuiPoint(Vector2 screenPoint)
        {
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private static Vector2 WorldToGuiPoint(Camera cameraComponent, Vector2 worldPoint)
        {
            var screenPoint = cameraComponent.WorldToScreenPoint(new Vector3(worldPoint.x, worldPoint.y, 0f));
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private void DrawCircleOutline(Vector2 center, float radiusPixels, Color color, int segments, float thickness)
        {
            if (segments < 3 || radiusPixels <= Mathf.Epsilon)
            {
                return;
            }

            var previousPoint = center + new Vector2(radiusPixels, 0f);
            for (var index = 1; index <= segments; index += 1)
            {
                var angle = (index / (float)segments) * Mathf.PI * 2f;
                var nextPoint = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusPixels;
                DrawGuideLine(previousPoint, nextPoint, color, thickness);
                previousPoint = nextPoint;
            }
        }

        private void DrawGuideLine(Vector2 startPoint, Vector2 endPoint, Color color, float thickness)
        {
            var delta = endPoint - startPoint;
            var length = delta.magnitude;
            if (length <= Mathf.Epsilon)
            {
                return;
            }

            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUIUtility.RotateAroundPivot(angle, startPoint);
            GUI.color = color;
            GUI.DrawTexture(new Rect(startPoint.x, startPoint.y - (thickness * 0.5f), length, thickness), solidTexture);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawMarker(Vector2 center, string label, Color color, float size = 28f)
        {
            var markerRect = new Rect(center.x - (size * 0.5f), center.y - (size * 0.5f), size, size);
            DrawFilledRect(markerRect, color);
            GUI.Label(markerRect, label, eraseBadgeStyle);
        }

        private void DrawFilledRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = previousColor;
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
