using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxSemanticObjectRenderer : MonoBehaviour
    {
        private readonly struct GuideLabel
        {
            public GuideLabel(Vector2 worldAnchor, string text, Color color)
            {
                WorldAnchor = worldAnchor;
                Text = text;
                Color = color;
            }

            public Vector2 WorldAnchor { get; }
            public string Text { get; }
            public Color Color { get; }
        }

        [SerializeField] private Color selectedColor = new(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color openingMaskColor = new(0.11f, 0.18f, 0.3f, 1f);
        [SerializeField] private Color guideLineColor = new(0.82f, 0.88f, 0.96f, 0.72f);
        [SerializeField] private Color invalidGuideLineColor = new(1f, 0.55f, 0.42f, 0.8f);
        [SerializeField] private Color guideLabelColor = new(0.98f, 1f, 1f, 1f);
        [SerializeField] private Color invalidGuideLabelColor = new(1f, 0.82f, 0.75f, 1f);
        [SerializeField] private float alignmentGuideTolerance = 0.45f;
        [SerializeField] private float lineWidth = 0.05f;
        [SerializeField] private float openingEdgeWidth = 0.1f;
        [SerializeField] private float openingMaskWidth = 0.18f;
        [SerializeField] private float openingEdgeLength = 0.42f;
        [SerializeField] private float markerRadius = 0.25f;
        [SerializeField] private Color exitFillColor = new(0.05f, 0.28f, 0.12f, 0.72f);
        [SerializeField] private Color rectangleHandleColor = new(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color brokenTeleportColor = new(0.5f, 0.5f, 0.5f, 0.95f);
        [SerializeField] private Color disabledTeleportSlashColor = new(0.1f, 0.1f, 0.1f, 0.7f);
        [SerializeField] private float rectangleHandleSize = 0.18f;
        [SerializeField] private float hatchSpacing = 0.32f;
        [SerializeField] private float hatchInset = 0.08f;
        [SerializeField] private float dragGhostAlpha = 0.45f;

        // Persistent POOL of GameObject+LineRenderer reused across rebuilds (see AcquireLine). Replaces
        // the previous destroy-everything-then-recreate-all per Refresh, which — run several times per
        // edit with deferred Destroy — kept multiple full copies of the floor live and grew the one-way
        // WebGL heap toward OOM as the project grew.
        private readonly List<GameObject> renderedObjects = new();
        private int activePooledCount;
        // Coalesces the several change notifications fired per edit into one rebuild in LateUpdate.
        private bool refreshRequested;
        private readonly List<GuideLabel> guideLabels = new();
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxSelectionService selectionService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxPreviewAuthoringService previewAuthoringService;
        private SandboxPreviewService previewService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxEditorQoLService editorQoLService;
        private SandboxObjectInteractionOverlay objectInteractionOverlay;
        private SandboxStatusBarShell statusBar;
        private Camera targetCamera;
        private Texture2D solidTexture;
        private GUIStyle guideLabelStyle;
        private bool lastRectangleDragActive;
        private string lastRectangleDragObjectId = string.Empty;
        private Vector2 lastRectangleDragCenter;
        private Vector2 lastRectangleDragSize;
        private bool lastSelectionDragActive;
        private Vector2 lastSelectionDragCurrentPoint;
        private bool workspaceEventsSubscribed;
        private bool selectionEventsSubscribed;
        private bool semanticAuthoringEventsSubscribed;
        private bool previewAuthoringEventsSubscribed;
        private bool previewEventsSubscribed;
        private bool visualOrganizationEventsSubscribed;
        private bool editorQoLEventsSubscribed;

        private void Awake()
        {
            ResolveDependencies();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            objectInteractionOverlay = FindAnyObjectByType<SandboxObjectInteractionOverlay>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            targetCamera = Camera.main;

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
            }

            if (semanticObjectAuthoringService != null)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged += HandleSemanticObjectsChanged;
            }

            if (visualOrganizationService != null)
            {
                visualOrganizationService.VisualStateChanged += HandleVisualStateChanged;
            }

            if (editorQoLService != null)
            {
                editorQoLService.StateChanged += HandleVisualStateChanged;
            }

            Refresh();
        }

        // One shared line material reused by every rendered line, instead of allocating a new Material
        // (with a Shader.Find) per line on every full rebuild. Line color is per-LineRenderer, so sharing
        // is safe. This removes a major per-edit native-object churn on large projects.
        private Material sharedLineMaterial;

        private Material GetLineMaterial()
        {
            if (sharedLineMaterial == null)
            {
                sharedLineMaterial = new Material(Shader.Find("Sprites/Default"));
            }

            return sharedLineMaterial;
        }

        private void OnDestroy()
        {
            if (sharedLineMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(sharedLineMaterial);
                }
                else
                {
                    DestroyImmediate(sharedLineMaterial);
                }
            }

            if (workspaceService != null && workspaceEventsSubscribed)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            if (selectionService != null && selectionEventsSubscribed)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }

            if (semanticObjectAuthoringService != null && semanticAuthoringEventsSubscribed)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged -= HandleSemanticObjectsChanged;
            }

            if (previewAuthoringService != null && previewAuthoringEventsSubscribed)
            {
                previewAuthoringService.PreviewAuthoringChanged -= HandlePreviewAuthoringChanged;
            }

            if (previewService != null && previewEventsSubscribed)
            {
                previewService.PreviewModeChanged -= HandlePreviewModeChanged;
                previewService.PreviewStateChanged -= HandlePreviewStateChanged;
            }

            if (visualOrganizationService != null && visualOrganizationEventsSubscribed)
            {
                visualOrganizationService.VisualStateChanged -= HandleVisualStateChanged;
            }

            if (editorQoLService != null && editorQoLEventsSubscribed)
            {
                editorQoLService.StateChanged -= HandleVisualStateChanged;
            }
        }

        private void LateUpdate()
        {
            var hadWorkspaceService = workspaceService != null;
            var hadPreviewAuthoringService = previewAuthoringService != null;
            var hadPreviewService = previewService != null;
            ResolveDependencies();
            if ((!hadWorkspaceService && workspaceService != null) ||
                (!hadPreviewAuthoringService && previewAuthoringService != null) ||
                (!hadPreviewService && previewService != null))
            {
                refreshRequested = false;
                Refresh();
                return;
            }

            // Coalesced rebuild: all per-edit change notifications set refreshRequested; we rebuild once
            // here instead of once per notification.
            if (refreshRequested)
            {
                refreshRequested = false;
                Refresh();
                return;
            }

            targetCamera ??= Camera.main;
            if (objectInteractionOverlay == null)
            {
                return;
            }

            var hasChanged =
                lastRectangleDragActive != objectInteractionOverlay.IsRectangleHandleDragActive ||
                !string.Equals(lastRectangleDragObjectId, objectInteractionOverlay.DraggedRectangleObjectId, StringComparison.Ordinal) ||
                lastRectangleDragCenter != objectInteractionOverlay.DraggedRectanglePreviewCenter ||
                lastRectangleDragSize != objectInteractionOverlay.DraggedRectanglePreviewSize ||
                lastSelectionDragActive != objectInteractionOverlay.IsSelectionDragActive ||
                lastSelectionDragCurrentPoint != objectInteractionOverlay.SelectionDragCurrentWorldPoint;
            if (!hasChanged)
            {
                return;
            }

            lastRectangleDragActive = objectInteractionOverlay.IsRectangleHandleDragActive;
            lastRectangleDragObjectId = objectInteractionOverlay.DraggedRectangleObjectId ?? string.Empty;
            lastRectangleDragCenter = objectInteractionOverlay.DraggedRectanglePreviewCenter;
            lastRectangleDragSize = objectInteractionOverlay.DraggedRectanglePreviewSize;
            lastSelectionDragActive = objectInteractionOverlay.IsSelectionDragActive;
            lastSelectionDragCurrentPoint = objectInteractionOverlay.SelectionDragCurrentWorldPoint;
            Refresh();
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureGuideGuiResources();
            DrawGuideLabels();
            DrawLockBadges();
        }

        private void DrawGuideLabels()
        {
            foreach (var label in guideLabels)
            {
                var guiPoint = ToGuiPoint(targetCamera ?? Camera.main, label.WorldAnchor);
                var content = new GUIContent(label.Text);
                var size = guideLabelStyle.CalcSize(content);
                var lineCount = Mathf.Max(1, label.Text.Count(character => character == '\n') + 1);
                var rect = new Rect(guiPoint.x - (size.x * 0.5f) - 8f, guiPoint.y - (18f * lineCount) - 18f, size.x + 16f, Mathf.Max(24f, 20f * lineCount));
                DrawFilledRect(rect, new Color(0.05f, 0.08f, 0.12f, 0.92f));
                var previousColor = guideLabelStyle.normal.textColor;
                guideLabelStyle.normal.textColor = label.Color;
                GUI.Label(rect, label.Text, guideLabelStyle);
                guideLabelStyle.normal.textColor = previousColor;
            }
        }

        // Draws a small padlock badge over every edit-locked object so locks are visible at a glance
        // (fields stay editable in the inspector, so this is the main on-canvas cue).
        private void DrawLockBadges()
        {
            if (visualOrganizationService == null)
            {
                return;
            }

            var floor = workspaceService?.ActiveFloor;
            var camera = targetCamera ?? Camera.main;
            if (floor == null || camera == null)
            {
                return;
            }

            foreach (var wall in floor.wallSegments)
            {
                if (IsLockedAndVisible(wall.wallSegmentId, SandboxVisualObjectType.Wall))
                {
                    DrawLockBadge(camera, (wall.startPoint + wall.endPoint) * 0.5f);
                }
            }

            foreach (var door in floor.doors)
            {
                if (IsLockedAndVisible(door.doorId, SandboxVisualObjectType.Door) &&
                    SandboxAlignmentGuideUtility.TryResolveOpeningCenter(floor, door.wallSegmentId, door.offsetAlongWall, out var doorCenter))
                {
                    DrawLockBadge(camera, doorCenter);
                }
            }

            foreach (var window in floor.windows)
            {
                if (IsLockedAndVisible(window.windowId, SandboxVisualObjectType.Window) &&
                    SandboxAlignmentGuideUtility.TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out var windowCenter))
                {
                    DrawLockBadge(camera, windowCenter);
                }
            }

            foreach (var exitZone in floor.exits)
            {
                if (IsLockedAndVisible(exitZone.exitZoneId, SandboxVisualObjectType.Exit))
                {
                    DrawLockBadge(camera, exitZone.center);
                }
            }

            foreach (var obstacle in floor.obstacles)
            {
                if (IsLockedAndVisible(obstacle.obstacleId, SandboxVisualObjectType.Obstacle))
                {
                    DrawLockBadge(camera, obstacle.center);
                }
            }

            foreach (var stairPortal in floor.stairPortals)
            {
                if (IsLockedAndVisible(stairPortal.stairPortalId, SandboxVisualObjectType.Stair))
                {
                    DrawLockBadge(camera, stairPortal.localPosition);
                }
            }

            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (IsLockedAndVisible(teleportPortal.teleportPortalId, SandboxVisualObjectType.Teleport))
                {
                    DrawLockBadge(camera, teleportPortal.localPosition);
                }
            }

            var project = workspaceService?.ActiveProject;
            if (project != null)
            {
                foreach (var fireOrigin in project.fireOrigins)
                {
                    if (fireOrigin.floorId == floor.floorId && IsLockedAndVisible(fireOrigin.fireOriginId, SandboxVisualObjectType.FireStart))
                    {
                        DrawLockBadge(camera, fireOrigin.position);
                    }
                }

                foreach (var spawnPoint in project.spawnLayouts.SelectMany(layout => layout.spawnPoints))
                {
                    if (spawnPoint.floorId == floor.floorId && IsLockedAndVisible(spawnPoint.spawnPointId, SandboxVisualObjectType.Spawn))
                    {
                        DrawLockBadge(camera, spawnPoint.position);
                    }
                }
            }
        }

        private bool IsLockedAndVisible(string objectId, SandboxVisualObjectType objectType)
        {
            return visualOrganizationService.IsObjectLocked(objectId) &&
                   IsVisible(objectType) &&
                   !IsHidden(objectId, objectType);
        }

        private void DrawLockBadge(Camera camera, Vector2 worldAnchor)
        {
            var guiPoint = ToGuiPoint(camera, worldAnchor);
            var background = new Rect(guiPoint.x - 9f, guiPoint.y - 9f, 18f, 18f);
            DrawFilledRect(background, new Color(0.05f, 0.08f, 0.12f, 0.85f));

            var gold = new Color(1f, 0.85f, 0.3f, 1f);
            // Padlock body.
            DrawFilledRect(new Rect(guiPoint.x - 5f, guiPoint.y - 1f, 10f, 8f), gold);
            // Shackle (inverted U): left, right, and top bars.
            DrawFilledRect(new Rect(guiPoint.x - 4f, guiPoint.y - 7f, 2f, 6f), gold);
            DrawFilledRect(new Rect(guiPoint.x + 2f, guiPoint.y - 7f, 2f, 6f), gold);
            DrawFilledRect(new Rect(guiPoint.x - 4f, guiPoint.y - 7f, 8f, 2f), gold);
        }

        public void Refresh()
        {
            ResolveDependencies();
            BeginPooledFrame();
            targetCamera ??= Camera.main;

            var floor = workspaceService?.ActiveFloor;
            var project = workspaceService?.ActiveProject;
            if (floor == null || project == null)
            {
                EndPooledFrame();
                return;
            }

            if (IsVisible(SandboxVisualObjectType.Door))
            {
                foreach (var door in floor.doors)
                {
                    if (IsHidden(door.doorId, SandboxVisualObjectType.Door))
                    {
                        continue;
                    }

                    RenderDoorOrWindow(
                        floor,
                        door.wallSegmentId,
                        door.offsetAlongWall,
                        door.width,
                        $"Door_{door.doorId}",
                        ResolveDoorColor(door),
                        door.doorId);
                }
            }

            if (IsVisible(SandboxVisualObjectType.Window))
            {
                foreach (var window in floor.windows)
                {
                    if (IsHidden(window.windowId, SandboxVisualObjectType.Window))
                    {
                        continue;
                    }

                    RenderDoorOrWindow(
                        floor,
                        window.wallSegmentId,
                        window.offsetAlongWall,
                        window.width,
                        $"Window_{window.windowId}",
                        ResolveSelectionColor(window.windowId, ResolveBaseColor(SandboxVisualObjectType.Window)),
                        window.windowId);
                }
            }

            if (IsVisible(SandboxVisualObjectType.Exit))
            {
                foreach (var exitZone in floor.exits)
                {
                    if (IsHidden(exitZone.exitZoneId, SandboxVisualObjectType.Exit))
                    {
                        continue;
                    }

                    var (center, size, rotationDegrees) = ResolveRectanglePresentation(
                        exitZone.exitZoneId,
                        SandboxVisualObjectType.Exit,
                        exitZone.center,
                        exitZone.size,
                        exitZone.rotationDegrees);
                    var outlineColor = ResolveSelectionColor(exitZone.exitZoneId, ResolveBaseColor(SandboxVisualObjectType.Exit));
                    RenderHatchedRectangle($"Exit_{exitZone.exitZoneId}_Fill", center, size, rotationDegrees, exitFillColor);
                    RenderRectangle($"Exit_{exitZone.exitZoneId}", center, size, rotationDegrees, outlineColor);
                    if (selectionService != null && selectionService.SelectedObjectIds.Count == 1 && selectionService.SelectedObjectIds.Contains(exitZone.exitZoneId))
                    {
                        RenderRectangleHandles($"Exit_{exitZone.exitZoneId}_Handles", center, size, rotationDegrees, rectangleHandleColor);
                    }
                }
            }

            if (IsVisible(SandboxVisualObjectType.Obstacle))
            {
                foreach (var obstacle in floor.obstacles)
                {
                    if (IsHidden(obstacle.obstacleId, SandboxVisualObjectType.Obstacle))
                    {
                        continue;
                    }

                    var (center, size, rotationDegrees) = ResolveRectanglePresentation(
                        obstacle.obstacleId,
                        SandboxVisualObjectType.Obstacle,
                        obstacle.center,
                        obstacle.size,
                        obstacle.rotationDegrees);
                    var baseColor = ResolveBaseColor(SandboxVisualObjectType.Obstacle);
                    var outlineColor = ResolveSelectionColor(obstacle.obstacleId, baseColor);
                    RenderHatchedRectangle($"Obstacle_{obstacle.obstacleId}_Fill", center, size, rotationDegrees, new Color(baseColor.r, baseColor.g, baseColor.b, 0.5f));
                    RenderRectangle($"Obstacle_{obstacle.obstacleId}", center, size, rotationDegrees, outlineColor);
                    if (selectionService != null && selectionService.SelectedObjectIds.Count == 1 && selectionService.SelectedObjectIds.Contains(obstacle.obstacleId))
                    {
                        RenderRectangleHandles($"Obstacle_{obstacle.obstacleId}_Handles", center, size, rotationDegrees, rectangleHandleColor);
                    }
                }
            }

            if (IsVisible(SandboxVisualObjectType.Stair))
            {
                foreach (var stairPortal in floor.stairPortals)
                {
                    if (IsHidden(stairPortal.stairPortalId, SandboxVisualObjectType.Stair))
                    {
                        continue;
                    }

                    var color = ResolveSelectionColor(stairPortal.stairPortalId, ResolveBaseColor(SandboxVisualObjectType.Stair));
                    RenderRectangle(
                        $"Stair_{stairPortal.stairPortalId}",
                        stairPortal.localPosition,
                        stairPortal.size,
                        stairPortal.rotationDegrees,
                        color);
                    RenderCross(
                        $"Stair_{stairPortal.stairPortalId}_Center",
                        stairPortal.localPosition,
                        color);
                }
            }

            if (IsVisible(SandboxVisualObjectType.Teleport))
            {
                var selectedTeleportId = selectionService?.SelectedObjectIds.Count == 1
                    ? selectionService.SelectedObjectIds[0]
                    : string.Empty;
                var selectedTeleport = floor.teleportPortals.FirstOrDefault(candidate =>
                    string.Equals(candidate.teleportPortalId, selectedTeleportId, StringComparison.Ordinal));
                var highlightedPartnerId = selectedTeleport?.targetTeleportPortalId ?? string.Empty;

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    if (IsHidden(teleportPortal.teleportPortalId, SandboxVisualObjectType.Teleport))
                    {
                        continue;
                    }

                    var baseColor = ResolveTeleportColor(teleportPortal);
                    var outlineColor = string.Equals(teleportPortal.teleportPortalId, highlightedPartnerId, StringComparison.Ordinal)
                        ? Color.Lerp(baseColor, selectedColor, 0.45f)
                        : ResolveSelectionColor(teleportPortal.teleportPortalId, baseColor);
                    var fillColor = baseColor;
                    if (!teleportPortal.isPairEnabled && !IsTeleportBroken(teleportPortal))
                    {
                        fillColor = Color.Lerp(baseColor, Color.black, 0.45f);
                    }

                    if (IsTeleportBroken(teleportPortal))
                    {
                        fillColor = brokenTeleportColor;
                        outlineColor = ResolveSelectionColor(teleportPortal.teleportPortalId, brokenTeleportColor);
                    }

                    var (center, size, rotationDegrees) = ResolveRectanglePresentation(
                        teleportPortal.teleportPortalId,
                        SandboxVisualObjectType.Teleport,
                        teleportPortal.localPosition,
                        teleportPortal.size,
                        teleportPortal.rotationDegrees);
                    RenderHatchedRectangle($"Teleport_{teleportPortal.teleportPortalId}_Fill", center, size, rotationDegrees, new Color(fillColor.r, fillColor.g, fillColor.b, 0.58f));
                    RenderRectangle($"Teleport_{teleportPortal.teleportPortalId}", center, size, rotationDegrees, outlineColor);
                    if (!teleportPortal.isPairEnabled && !IsTeleportBroken(teleportPortal))
                    {
                        RenderDiagonalSlash($"Teleport_{teleportPortal.teleportPortalId}_Disabled", center, size, rotationDegrees, disabledTeleportSlashColor);
                    }

                    if (selectionService != null && selectionService.SelectedObjectIds.Count == 1 && selectionService.SelectedObjectIds.Contains(teleportPortal.teleportPortalId))
                    {
                        RenderRectangleHandles($"Teleport_{teleportPortal.teleportPortalId}_Handles", center, size, rotationDegrees, rectangleHandleColor);
                    }
                }
            }

            if (IsVisible(SandboxVisualObjectType.FireStart))
            {
                foreach (var fireOrigin in project.fireOrigins.Where(origin => origin.floorId == floor.floorId))
                {
                    if (IsHidden(fireOrigin.fireOriginId, SandboxVisualObjectType.FireStart))
                    {
                        continue;
                    }

                    var (center, size, _) = ResolveRectanglePresentation(
                        fireOrigin.fireOriginId,
                        SandboxVisualObjectType.FireStart,
                        fireOrigin.position,
                        fireOrigin.size,
                        0f);
                    RenderFireStartMarker(
                        $"FireStart_{fireOrigin.fireOriginId}",
                        center,
                        size,
                        ResolveSelectionColor(fireOrigin.fireOriginId, ResolveBaseColor(SandboxVisualObjectType.FireStart)));
                    if (selectionService != null && selectionService.SelectedObjectIds.Count == 1 && selectionService.SelectedObjectIds.Contains(fireOrigin.fireOriginId))
                    {
                        RenderRectangleHandles($"FireStart_{fireOrigin.fireOriginId}_Handles", center, size, 0f, rectangleHandleColor);
                    }
                }
            }

            if (previewService == null || !previewService.IsPreviewModeActive)
            {
                foreach (var layout in project.spawnLayouts)
                {
                    foreach (var spawnPoint in layout.spawnPoints.Where(point => point.floorId == floor.floorId))
                    {
                        RenderSpawnPointMarker(
                            $"SpawnPoint_{spawnPoint.spawnPointId}",
                            spawnPoint.position,
                            ResolveSelectionColor(spawnPoint.spawnPointId, ResolveBaseColor(SandboxVisualObjectType.Spawn)));
                    }
                }
            }

            RenderSelectionDragGhosts(floor);
            EndPooledFrame();
        }

        private void RenderSelectionDragGhosts(FloorData floor)
        {
            if (objectInteractionOverlay == null ||
                !objectInteractionOverlay.IsSelectionDragActive ||
                selectionService == null)
            {
                return;
            }

            var delta = objectInteractionOverlay.SelectionDragCurrentWorldPoint -
                        objectInteractionOverlay.SelectionDragStartWorldPoint;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var project = workspaceService?.ActiveProject;
            foreach (var objectId in selectionService.SelectedObjectIds)
            {
                if (TryResolveGhostRectangle(floor, objectId, out var center, out var size, out var rotationDegrees, out var color))
                {
                    RenderRectangleGhost($"DragGhost_{objectId}", center + delta, size, rotationDegrees, color);
                    continue;
                }

                var fireOrigin = project?.fireOrigins.FirstOrDefault(origin =>
                    origin.floorId == floor.floorId && string.Equals(origin.fireOriginId, objectId, StringComparison.Ordinal));
                if (fireOrigin != null)
                {
                    RenderFireStartMarker($"DragGhost_{objectId}", fireOrigin.position + delta, fireOrigin.size, new Color(0.9f, 0.2f, 0.1f, 0.6f));
                    continue;
                }

                var spawnPoint = project?.spawnLayouts
                    .SelectMany(layout => layout.spawnPoints)
                    .FirstOrDefault(point =>
                        point.floorId == floor.floorId &&
                        string.Equals(point.spawnPointId, objectId, StringComparison.Ordinal));
                if (spawnPoint != null)
                {
                    var baseColor = ResolveSelectionColor(spawnPoint.spawnPointId, ResolveBaseColor(SandboxVisualObjectType.Spawn));
                    RenderSpawnPointMarker(
                        $"DragGhost_{objectId}",
                        spawnPoint.position + delta,
                        new Color(baseColor.r, baseColor.g, baseColor.b, 0.6f));
                }
            }

            if (objectInteractionOverlay.HasOpeningDragPreview &&
                objectInteractionOverlay.DraggedOpeningPreview.isValid &&
                string.Equals(workspaceService?.ActiveFloor?.floorId, floor.floorId, StringComparison.Ordinal))
            {
                var openingId = objectInteractionOverlay.DraggedOpeningObjectId;
                var openingType = objectInteractionOverlay.DraggedOpeningObjectType;
                if (!string.IsNullOrWhiteSpace(openingId) && openingType.HasValue)
                {
                    var ghostColor = openingType == SandboxVisualObjectType.Door
                        ? ResolveDraggedDoorGhostColor(floor, openingId)
                        : ResolveSelectionColor(openingId, ResolveBaseColor(SandboxVisualObjectType.Window));
                    RenderOpeningGhost(
                        $"DragGhost_{openingId}",
                        objectInteractionOverlay.DraggedOpeningPreview.start,
                        objectInteractionOverlay.DraggedOpeningPreview.end,
                        ghostColor);
                }
            }
        }

        private void ResolveDependencies()
        {
            workspaceService ??= FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService ??= FindAnyObjectByType<SandboxWorkspaceStateService>();
            selectionService ??= FindAnyObjectByType<SandboxSelectionService>();
            semanticObjectAuthoringService ??= FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            previewAuthoringService ??= FindAnyObjectByType<SandboxPreviewAuthoringService>();
            previewService ??= FindAnyObjectByType<SandboxPreviewService>();
            visualOrganizationService ??= FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService ??= FindAnyObjectByType<SandboxEditorQoLService>();
            objectInteractionOverlay ??= FindAnyObjectByType<SandboxObjectInteractionOverlay>();

            if (workspaceService != null && !workspaceEventsSubscribed)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
                workspaceEventsSubscribed = true;
            }

            if (selectionService != null && !selectionEventsSubscribed)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
                selectionEventsSubscribed = true;
            }

            if (semanticObjectAuthoringService != null && !semanticAuthoringEventsSubscribed)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged += HandleSemanticObjectsChanged;
                semanticAuthoringEventsSubscribed = true;
            }

            if (previewAuthoringService != null && !previewAuthoringEventsSubscribed)
            {
                previewAuthoringService.PreviewAuthoringChanged += HandlePreviewAuthoringChanged;
                previewAuthoringEventsSubscribed = true;
            }

            if (previewService != null && !previewEventsSubscribed)
            {
                previewService.PreviewModeChanged += HandlePreviewModeChanged;
                previewService.PreviewStateChanged += HandlePreviewStateChanged;
                previewEventsSubscribed = true;
            }

            if (visualOrganizationService != null && !visualOrganizationEventsSubscribed)
            {
                visualOrganizationService.VisualStateChanged += HandleVisualStateChanged;
                visualOrganizationEventsSubscribed = true;
            }

            if (editorQoLService != null && !editorQoLEventsSubscribed)
            {
                editorQoLService.StateChanged += HandleVisualStateChanged;
                editorQoLEventsSubscribed = true;
            }
        }

        private bool TryResolveGhostRectangle(
            FloorData floor,
            string objectId,
            out Vector2 center,
            out Vector2 size,
            out float rotationDegrees,
            out Color color)
        {
            center = Vector2.zero;
            size = Vector2.zero;
            rotationDegrees = 0f;
            color = selectedColor;

            var exit = floor.exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, objectId, StringComparison.Ordinal));
            if (exit != null)
            {
                center = exit.center;
                size = exit.size;
                rotationDegrees = exit.rotationDegrees;
                color = ResolveBaseColor(SandboxVisualObjectType.Exit);
                return true;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, objectId, StringComparison.Ordinal));
            if (obstacle != null)
            {
                center = obstacle.center;
                size = obstacle.size;
                rotationDegrees = obstacle.rotationDegrees;
                color = ResolveBaseColor(SandboxVisualObjectType.Obstacle);
                return true;
            }

            var stairPortal = floor.stairPortals.FirstOrDefault(candidate => string.Equals(candidate.stairPortalId, objectId, StringComparison.Ordinal));
            if (stairPortal != null)
            {
                center = stairPortal.localPosition;
                size = stairPortal.size;
                rotationDegrees = stairPortal.rotationDegrees;
                color = ResolveBaseColor(SandboxVisualObjectType.Stair);
                return true;
            }

            var teleportPortal = floor.teleportPortals.FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, objectId, StringComparison.Ordinal));
            if (teleportPortal != null)
            {
                center = teleportPortal.localPosition;
                size = teleportPortal.size;
                rotationDegrees = teleportPortal.rotationDegrees;
                color = ResolveTeleportColor(teleportPortal);
                return true;
            }

            return false;
        }

        private void RenderRectangleGhost(string name, Vector2 center, Vector2 size, float rotationDegrees, Color color)
        {
            var ghostFill = new Color(color.r, color.g, color.b, dragGhostAlpha * 0.6f);
            var ghostOutline = new Color(color.r, color.g, color.b, dragGhostAlpha);
            RenderHatchedRectangle($"{name}_Fill", center, size, rotationDegrees, ghostFill);
            RenderRectangle(name, center, size, rotationDegrees, ghostOutline);
        }

        private void RenderOpeningGhost(string name, Vector2 start, Vector2 end, Color color)
        {
            var openingVector = end - start;
            if (openingVector.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var direction = openingVector.normalized;
            var normal = new Vector2(-direction.y, direction.x);
            var ghostMask = new Color(openingMaskColor.r, openingMaskColor.g, openingMaskColor.b, dragGhostAlpha * 0.85f);
            var ghostEdge = new Color(color.r, color.g, color.b, dragGhostAlpha);
            var halfEdgeLength = openingEdgeLength * 0.5f;
            RenderLine($"{name}_Mask", start, end, ghostMask, openingMaskWidth, 0.06f);
            RenderLine($"{name}_StartEdge", start - normal * halfEdgeLength, start + normal * halfEdgeLength, ghostEdge, openingEdgeWidth, 0.08f);
            RenderLine($"{name}_EndEdge", end - normal * halfEdgeLength, end + normal * halfEdgeLength, ghostEdge, openingEdgeWidth, 0.08f);
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            refreshRequested = true;
        }

        private void HandleFloorChanged(FloorData floor)
        {
            refreshRequested = true;
        }

        private void HandleSelectionChanged(IReadOnlyList<string> selection)
        {
            refreshRequested = true;
        }

        private void HandleSemanticObjectsChanged()
        {
            refreshRequested = true;
        }

        private void HandlePreviewAuthoringChanged()
        {
            refreshRequested = true;
        }

        private void HandlePreviewModeChanged(bool isPreviewModeActive)
        {
            refreshRequested = true;
        }

        private void HandlePreviewStateChanged()
        {
            refreshRequested = true;
        }

        private void HandleVisualStateChanged()
        {
            refreshRequested = true;
        }

        private void RenderDoorOrWindow(
            FloorData floor,
            string wallSegmentId,
            float offsetAlongWall,
            float width,
            string name,
            Color color,
            string objectId)
        {
            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            var wallVector = wall.endPoint - wall.startPoint;
            if (wallVector.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var wallDirection = wallVector.normalized;
            var wallNormal = new Vector2(-wallDirection.y, wallDirection.x);
            var center = wall.startPoint + wallDirection * offsetAlongWall;
            var worldWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(
                workspaceService,
                workspaceStateService,
                floor,
                width);
            var halfWidth = Mathf.Max(0.15f, worldWidth * 0.5f);
            var start = center - wallDirection * halfWidth;
            var end = center + wallDirection * halfWidth;
            RenderLine($"{name}_Mask", start, end, openingMaskColor, openingMaskWidth, 0.02f);
            var edgeColor = ResolveOpeningSelectionColor(objectId, color);
            var halfEdgeLength = openingEdgeLength * 0.5f;
            RenderLine($"{name}_StartEdge", start - wallNormal * halfEdgeLength, start + wallNormal * halfEdgeLength, edgeColor, openingEdgeWidth, 0.04f);
            RenderLine($"{name}_EndEdge", end - wallNormal * halfEdgeLength, end + wallNormal * halfEdgeLength, edgeColor, openingEdgeWidth, 0.04f);
        }

        private void RenderRectangle(string name, Vector2 center, Vector2 size, float rotationDegrees, Color color)
        {
            var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
            RenderPolyline(name, corners, color, true);
        }

        private void RenderHatchedRectangle(string name, Vector2 center, Vector2 size, float rotationDegrees, Color color)
        {
            var half = Vector2.Max(size * 0.5f - new Vector2(hatchInset, hatchInset), Vector2.one * 0.05f);
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            var localMin = -half;
            var localMax = half;
            var startX = localMin.x - localMax.y;
            var endX = localMax.x + localMax.y;
            var lineIndex = 0;
            for (var diagonal = startX; diagonal <= endX + hatchSpacing * 0.5f; diagonal += hatchSpacing)
            {
                var intersections = new List<Vector2>();
                TryAddDiagonalRectIntersection(diagonal, localMin.x, localMax.x, localMin.y, localMax.y, intersections);
                if (intersections.Count < 2)
                {
                    continue;
                }

                var ordered = intersections.OrderBy(point => point.x).ThenBy(point => point.y).ToArray();
                var start = center + (Vector2)(rotation * new Vector3(ordered[0].x, ordered[0].y, 0f));
                var end = center + (Vector2)(rotation * new Vector3(ordered[^1].x, ordered[^1].y, 0f));
                RenderLine($"{name}_{lineIndex:D2}", start, end, color, lineWidth * 0.75f, 0.01f);
                lineIndex += 1;
            }
        }

        private void RenderRectangleHandles(string name, Vector2 center, Vector2 size, float rotationDegrees, Color color)
        {
            var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
            for (var index = 0; index < corners.Length; index += 1)
            {
                var handleSize = new Vector2(rectangleHandleSize, rectangleHandleSize);
                RenderRectangle($"{name}_{index}", corners[index], handleSize, 0f, color);
            }
        }

        private void RenderDiagonalSlash(string name, Vector2 center, Vector2 size, float rotationDegrees, Color color)
        {
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            var start = center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f));
            var end = center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f));
            RenderLine(name, start, end, color, lineWidth * 1.2f, 0.03f);
        }

        private void RenderPolygon(string name, IReadOnlyList<Vector2> points, Color color)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            RenderPolyline(name, points, color, true);
        }

        private void RenderDiamond(string name, Vector2 center, Color color)
        {
            var radius = markerRadius * 0.8f;
            var points = new[]
            {
                center + new Vector2(0f, radius),
                center + new Vector2(radius, 0f),
                center + new Vector2(0f, -radius),
                center + new Vector2(-radius, 0f)
            };
            RenderPolyline(name, points, color, true);
        }

        private void RenderSpawnPointMarker(string name, Vector2 center, Color color)
        {
            var haloColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(color.a * 0.35f));
            RenderCircle($"{name}_Halo", center, markerRadius * 0.6f, haloColor);
            RenderCircle($"{name}_Ring", center, markerRadius * 0.4f, color);
        }

        // Fire start marker: a black ellipse (matching the bounding box, so stretching makes an oval)
        // with red diagonal hatches inside, plus a thin outer ring tinted by the selection color.
        private void RenderFireStartMarker(string name, Vector2 center, Vector2 size, Color selectionColor)
        {
            var radiusX = Mathf.Max(0.1f, size.x * 0.5f);
            var radiusY = Mathf.Max(0.1f, size.y * 0.5f);
            var black = new Color(0.05f, 0.05f, 0.05f, 1f);
            var hatch = new Color(0.9f, 0.15f, 0.1f, 1f);

            RenderEllipse($"{name}_Ring", center, radiusX, radiusY, black);
            RenderEllipse($"{name}_RingInner", center, radiusX * 0.92f, radiusY * 0.92f, black);

            // Hatch chords are computed on a unit circle then scaled into the ellipse, so they follow
            // the oval as it is stretched.
            var dir = new Vector2(1f, 1f).normalized;
            var perp = new Vector2(-dir.y, dir.x);
            const float step = 0.45f;
            var index = 0;
            for (var offset = -1f + step; offset < 1f; offset += step)
            {
                var half = Mathf.Sqrt(Mathf.Max(0f, 1f - offset * offset)) * 0.92f;
                if (half <= 0.01f)
                {
                    continue;
                }

                var a = perp * offset - dir * half;
                var b = perp * offset + dir * half;
                var start = center + new Vector2(a.x * radiusX, a.y * radiusY);
                var end = center + new Vector2(b.x * radiusX, b.y * radiusY);
                RenderLine($"{name}_Hatch{index}", start, end, hatch);
                index += 1;
            }

            RenderEllipse($"{name}_Selection", center, radiusX * 1.18f, radiusY * 1.18f, selectionColor);
        }

        private void RenderEllipse(string name, Vector2 center, float radiusX, float radiusY, Color color)
        {
            const int segmentCount = 24;
            var points = new Vector2[segmentCount];
            for (var i = 0; i < segmentCount; i += 1)
            {
                var angle = i / (float)segmentCount * Mathf.PI * 2f;
                points[i] = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
            }

            RenderPolyline(name, points, color, true);
        }

        private void RenderCircle(string name, Vector2 center, float radius, Color color)
        {
            const int segmentCount = 18;
            var points = new Vector2[segmentCount];
            for (var i = 0; i < segmentCount; i += 1)
            {
                var angle = i / (float)segmentCount * Mathf.PI * 2f;
                points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            RenderPolyline(name, points, color, true);
        }

        private void RenderCross(string name, Vector2 center, Color color)
        {
            RenderCross(name, center, color, markerRadius);
        }

        private void RenderCross(string name, Vector2 center, Color color, float radius)
        {
            RenderLine($"{name}_A", center + new Vector2(-radius, -radius), center + new Vector2(radius, radius), color);
            RenderLine($"{name}_B", center + new Vector2(-radius, radius), center + new Vector2(radius, -radius), color);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color)
        {
            RenderLine(name, start, end, color, lineWidth, 0f);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color, float width, float zOffset)
        {
            var lineRenderer = AcquireLine(name);
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, zOffset));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, zOffset));
            lineRenderer.widthMultiplier = width;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }

        private void RenderAlignmentGuides(FloorData floor, BuildingProjectData project)
        {
            guideLabels.Clear();
            if (floor == null || project == null || workspaceStateService == null || objectInteractionOverlay == null)
            {
                return;
            }

            if (objectInteractionOverlay.IsSelectionDragActive && objectInteractionOverlay.HasOpeningDragPreview)
            {
                var preview = objectInteractionOverlay.DraggedOpeningPreview;
                var guideColor = preview.isValid ? guideLineColor : invalidGuideLineColor;
                var guideTextColor = preview.isValid ? guideLabelColor : invalidGuideLabelColor;
                RenderAxisGuides(floor, preview.center, preview.center, objectInteractionOverlay.DraggedOpeningObjectId, guideColor);
                var measurementLabel = BuildOpeningMeasurementLabel(floor, project, preview);
                RegisterGuideLabel(preview.center, measurementLabel, guideTextColor);
                statusBar.StatusMessage = measurementLabel.Replace('\n', ' ');
                return;
            }

            if (objectInteractionOverlay.IsRectangleHandleDragActive)
            {
                var center = objectInteractionOverlay.DraggedRectanglePreviewCenter;
                var size = objectInteractionOverlay.DraggedRectanglePreviewSize;
                var rotationDegrees = objectInteractionOverlay.DraggedRectanglePreviewRotationDegrees;
                var previewCorners = BuildRotatedRectCorners(center, size, rotationDegrees);
                var handleIndex = Mathf.Clamp(objectInteractionOverlay.DraggedRectangleHandleIndex, 0, previewCorners.Length - 1);
                var handlePoint = previewCorners[handleIndex];
                var xReferences = new List<float>();
                var yReferences = new List<float>();
                CollectFloorAxisReferences(floor, xReferences, yReferences, objectInteractionOverlay.DraggedRectangleObjectId);
                RenderAxisGuides(handlePoint, previewCorners, xReferences, yReferences, guideLineColor);
                var measurementLabel = BuildRectangleMeasurementLabel(project, size);
                RegisterGuideLabel(center, measurementLabel, guideLabelColor);
                statusBar.StatusMessage = measurementLabel.Replace('\n', ' ');
                return;
            }

            if (!objectInteractionOverlay.IsSelectionDragActive || string.IsNullOrWhiteSpace(objectInteractionOverlay.DraggedObjectId))
            {
                return;
            }

            var delta = objectInteractionOverlay.SelectionDragCurrentWorldPoint - objectInteractionOverlay.SelectionDragStartWorldPoint;
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (TryFindRectangleForMove(floor, objectInteractionOverlay.DraggedObjectId, out var centerPoint, out var moveCorners, out var moveLabelPrefix))
            {
                var movedCenter = centerPoint + delta;
                var movedCorners = moveCorners.Select(corner => corner + delta).ToArray();
                var xReferences = new List<float>();
                var yReferences = new List<float>();
                CollectFloorAxisReferences(floor, xReferences, yReferences, objectInteractionOverlay.DraggedObjectId);
                RenderAxisGuides(movedCenter, movedCorners, xReferences, yReferences, guideLineColor);
                var measurementLabel = BuildMoveMeasurementLabel(project, delta, moveLabelPrefix);
                RegisterGuideLabel(movedCenter, measurementLabel, guideLabelColor);
                statusBar.StatusMessage = measurementLabel.Replace('\n', ' ');
            }
        }

        private bool TryFindRectangleForMove(FloorData floor, string objectId, out Vector2 center, out Vector2[] corners, out string labelPrefix)
        {
            center = Vector2.zero;
            corners = Array.Empty<Vector2>();
            labelPrefix = "Move";

            var exitZone = floor.exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, objectId, StringComparison.Ordinal));
            if (exitZone != null)
            {
                center = exitZone.center;
                corners = BuildRotatedRectCorners(exitZone.center, exitZone.size, exitZone.rotationDegrees);
                labelPrefix = "Move";
                return true;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, objectId, StringComparison.Ordinal));
            if (obstacle != null)
            {
                center = obstacle.center;
                corners = BuildRotatedRectCorners(obstacle.center, obstacle.size, obstacle.rotationDegrees);
                labelPrefix = "Move";
                return true;
            }

            var teleportPortal = floor.teleportPortals.FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, objectId, StringComparison.Ordinal));
            if (teleportPortal != null)
            {
                center = teleportPortal.localPosition;
                corners = BuildRotatedRectCorners(teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees);
                labelPrefix = "Move";
                return true;
            }

            return false;
        }

        private void RenderAxisGuides(FloorData floor, Vector2 anchor, Vector2 target, string ignoredObjectId, Color color)
        {
            var xReferences = new List<float>();
            var yReferences = new List<float>();
            CollectFloorAxisReferences(floor, xReferences, yReferences, ignoredObjectId);
            var minBounds = Vector2.Min(anchor, target);
            var maxBounds = Vector2.Max(anchor, target);
            var verticalGuide = ResolveBestVerticalGuide(target.x, minBounds.y, maxBounds.y, xReferences, workspaceStateService.GridSize, alignmentGuideTolerance);
            if (verticalGuide.IsValid)
            {
                RenderLine("AlignmentGuide_V", new Vector2(verticalGuide.Coordinate, verticalGuide.Start), new Vector2(verticalGuide.Coordinate, verticalGuide.End), color, lineWidth * 0.8f, 0.01f);
            }

            var horizontalGuide = ResolveBestHorizontalGuide(target.y, minBounds.x, maxBounds.x, yReferences, workspaceStateService.GridSize, alignmentGuideTolerance);
            if (horizontalGuide.IsValid)
            {
                RenderLine("AlignmentGuide_H", new Vector2(horizontalGuide.Start, horizontalGuide.Coordinate), new Vector2(horizontalGuide.End, horizontalGuide.Coordinate), color, lineWidth * 0.8f, 0.01f);
            }
        }

        private void RenderAxisGuides(Vector2 target, IReadOnlyList<Vector2> boundsPoints, IReadOnlyCollection<float> xReferences, IReadOnlyCollection<float> yReferences, Color color)
        {
            var minX = boundsPoints.Min(point => point.x);
            var minY = boundsPoints.Min(point => point.y);
            var maxX = boundsPoints.Max(point => point.x);
            var maxY = boundsPoints.Max(point => point.y);
            var verticalGuide = ResolveBestVerticalGuide(target.x, minY, maxY, xReferences, workspaceStateService.GridSize, alignmentGuideTolerance);
            if (verticalGuide.IsValid)
            {
                RenderLine("AlignmentGuide_V", new Vector2(verticalGuide.Coordinate, verticalGuide.Start), new Vector2(verticalGuide.Coordinate, verticalGuide.End), color, lineWidth * 0.8f, 0.01f);
            }

            var horizontalGuide = ResolveBestHorizontalGuide(target.y, minX, maxX, yReferences, workspaceStateService.GridSize, alignmentGuideTolerance);
            if (horizontalGuide.IsValid)
            {
                RenderLine("AlignmentGuide_H", new Vector2(horizontalGuide.Start, horizontalGuide.Coordinate), new Vector2(horizontalGuide.End, horizontalGuide.Coordinate), color, lineWidth * 0.8f, 0.01f);
            }
        }

        private string BuildOpeningMeasurementLabel(FloorData floor, BuildingProjectData project, SandboxOpeningPlacementPreview preview)
        {
            var distanceUnit = project.metadata.distanceUnit;
            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, preview.wallSegmentId, StringComparison.Ordinal));
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            var offsetLabel = FormatSquaresAndDistance(preview.offsetAlongWall, gridSize, distanceUnit);
            var widthWorld = SandboxOpeningWidthUtility.ResolveWorldWidth(project, floor, preview.width, gridSize);
            var widthLabel = FormatSquaresAndDistance(widthWorld, gridSize, distanceUnit);
            return wall == null
                ? $"W: {widthLabel}"
                : $"Offset: {offsetLabel}\nWidth: {widthLabel}";
        }

        private string BuildRectangleMeasurementLabel(BuildingProjectData project, Vector2 size)
        {
            var distanceUnit = project.metadata.distanceUnit;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            return
                $"W: {FormatSquaresAndDistance(size.x, gridSize, distanceUnit)}\n" +
                $"H: {FormatSquaresAndDistance(size.y, gridSize, distanceUnit)}";
        }

        private string BuildMoveMeasurementLabel(BuildingProjectData project, Vector2 delta, string prefix)
        {
            var distanceUnit = project.metadata.distanceUnit;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            return
                $"{prefix}\n" +
                $"DX: {FormatSquaresAndDistance(Mathf.Abs(delta.x), gridSize, distanceUnit)}\n" +
                $"DY: {FormatSquaresAndDistance(Mathf.Abs(delta.y), gridSize, distanceUnit)}";
        }

        private void RenderPolyline(string name, IReadOnlyList<Vector2> points, Color color, bool loop)
        {
            var lineRenderer = AcquireLine(name);
            lineRenderer.positionCount = points.Count;
            lineRenderer.loop = loop;
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            for (var i = 0; i < points.Count; i += 1)
            {
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0f));
            }
        }

        private (Vector2 center, Vector2 size, float rotationDegrees) ResolveRectanglePresentation(
            string objectId,
            SandboxVisualObjectType objectType,
            Vector2 center,
            Vector2 size,
            float rotationDegrees)
        {
            if (objectInteractionOverlay != null &&
                objectInteractionOverlay.IsRectangleHandleDragActive &&
                string.Equals(objectInteractionOverlay.DraggedRectangleObjectId, objectId, StringComparison.Ordinal) &&
                objectInteractionOverlay.DraggedRectangleObjectType == objectType)
            {
                return (
                    objectInteractionOverlay.DraggedRectanglePreviewCenter,
                    objectInteractionOverlay.DraggedRectanglePreviewSize,
                    objectInteractionOverlay.DraggedRectanglePreviewRotationDegrees);
            }

            return (center, size, rotationDegrees);
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

        private static void TryAddDiagonalRectIntersection(
            float diagonal,
            float minX,
            float maxX,
            float minY,
            float maxY,
            ICollection<Vector2> intersections)
        {
            var candidateY = diagonal - minX;
            if (candidateY >= minY && candidateY <= maxY)
            {
                intersections.Add(new Vector2(minX, candidateY));
            }

            candidateY = diagonal - maxX;
            if (candidateY >= minY && candidateY <= maxY)
            {
                intersections.Add(new Vector2(maxX, candidateY));
            }

            var candidateX = diagonal - minY;
            if (candidateX >= minX && candidateX <= maxX)
            {
                intersections.Add(new Vector2(candidateX, minY));
            }

            candidateX = diagonal - maxY;
            if (candidateX >= minX && candidateX <= maxX)
            {
                intersections.Add(new Vector2(candidateX, maxY));
            }
        }

        private Color ResolveTeleportColor(TeleportPortalData teleportPortal)
        {
            if (teleportPortal == null)
            {
                return ResolveBaseColor(SandboxVisualObjectType.Teleport);
            }

            if (semanticObjectAuthoringService != null &&
                semanticObjectAuthoringService.TryGetTeleportPairColor(teleportPortal.pairColorIndex, out var color))
            {
                return color;
            }

            return ResolveBaseColor(SandboxVisualObjectType.Teleport);
        }

        private bool IsTeleportBroken(TeleportPortalData teleportPortal)
        {
            if (teleportPortal == null ||
                string.IsNullOrWhiteSpace(teleportPortal.targetFloorId) ||
                string.IsNullOrWhiteSpace(teleportPortal.targetTeleportPortalId))
            {
                return true;
            }

            var targetFloor = workspaceService?.ActiveProject?.floors?.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, teleportPortal.targetFloorId, StringComparison.Ordinal));
            return targetFloor == null || !targetFloor.teleportPortals.Any(candidate =>
                string.Equals(candidate.teleportPortalId, teleportPortal.targetTeleportPortalId, StringComparison.Ordinal));
        }

        private Color ResolveDoorColor(DoorData door)
        {
            var baseColor = ResolveBaseColor(SandboxVisualObjectType.Door);
            return door.state == DoorState.Normal || door.state == DoorState.Closed || door.state == DoorState.OneWay
                ? ResolveSelectionColor(door.doorId, baseColor)
                : ResolveSelectionColor(door.doorId, Color.Lerp(baseColor, Color.red, 0.35f));
        }

        private Color ResolveDraggedDoorGhostColor(FloorData floor, string doorId)
        {
            var door = floor?.doors?.FirstOrDefault(candidate => string.Equals(candidate.doorId, doorId, StringComparison.Ordinal));
            return door != null
                ? ResolveDoorColor(door)
                : ResolveSelectionColor(doorId, ResolveBaseColor(SandboxVisualObjectType.Door));
        }

        private Color ResolveSelectionColor(string objectId, Color baseColor)
        {
            return selectionService != null && selectionService.SelectedObjectIds.Contains(objectId)
                ? selectedColor
                : baseColor;
        }

        private Color ResolveOpeningSelectionColor(string objectId, Color baseColor)
        {
            if (selectionService == null || !selectionService.SelectedObjectIds.Contains(objectId))
            {
                return baseColor;
            }

            return Color.Lerp(baseColor, selectedColor, 0.35f);
        }

        private Color ResolveBaseColor(SandboxVisualObjectType objectType)
        {
            if (objectType == SandboxVisualObjectType.Door)
            {
                return new Color(0.18f, 0.55f, 1f, 1f);
            }

            if (objectType == SandboxVisualObjectType.Window)
            {
                return new Color(0.72f, 0.3f, 1f, 1f);
            }

            return visualOrganizationService == null
                ? Color.white
                : visualOrganizationService.GetColor(objectType);
        }

        private bool IsVisible(SandboxVisualObjectType objectType)
        {
            return (visualOrganizationService == null || visualOrganizationService.IsTypeVisible(objectType)) &&
                   (editorQoLService == null || editorQoLService.IsObjectTypeVisibleForIsolation(objectType));
        }

        private bool IsHidden(string objectId, SandboxVisualObjectType objectType)
        {
            return (visualOrganizationService != null && visualOrganizationService.IsObjectHidden(objectId)) ||
                   (editorQoLService != null && !editorQoLService.IsObjectVisibleForIsolation(objectId, objectType));
        }

        // Reset the pool cursor so AcquireLine reuses existing pooled objects from the top this pass.
        private void BeginPooledFrame()
        {
            guideLabels.Clear();
            activePooledCount = 0;
        }

        // Deactivate any pooled objects not reused this pass; they stay allocated for the next rebuild.
        private void EndPooledFrame()
        {
            for (var i = activePooledCount; i < renderedObjects.Count; i += 1)
            {
                if (renderedObjects[i] != null && renderedObjects[i].activeSelf)
                {
                    renderedObjects[i].SetActive(false);
                }
            }
        }

        // Hands out a reused (or, only when the pool runs dry, newly created) GameObject+LineRenderer,
        // resetting the state a reused object could carry over. Every rendered element is a LineRenderer.
        private LineRenderer AcquireLine(string objectName)
        {
            GameObject pooledObject;
            LineRenderer lineRenderer;
            if (activePooledCount < renderedObjects.Count)
            {
                pooledObject = renderedObjects[activePooledCount];
                if (pooledObject == null)
                {
                    pooledObject = CreatePooledLineObject(out lineRenderer);
                    pooledObject.transform.SetParent(transform, false);
                    renderedObjects[activePooledCount] = pooledObject;
                }
                else
                {
                    lineRenderer = pooledObject.GetComponent<LineRenderer>();
                    if (lineRenderer == null)
                    {
                        lineRenderer = pooledObject.AddComponent<LineRenderer>();
                    }

                    if (!pooledObject.activeSelf)
                    {
                        pooledObject.SetActive(true);
                    }
                }
            }
            else
            {
                pooledObject = CreatePooledLineObject(out lineRenderer);
                pooledObject.transform.SetParent(transform, false);
                renderedObjects.Add(pooledObject);
            }

            activePooledCount += 1;
            pooledObject.name = objectName;

            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = false;
            lineRenderer.sharedMaterial = GetLineMaterial();
            return lineRenderer;
        }

        private static GameObject CreatePooledLineObject(out LineRenderer lineRenderer)
        {
            var pooledObject = new GameObject("PooledSemanticLine");
            lineRenderer = pooledObject.AddComponent<LineRenderer>();
            return pooledObject;
        }

        private void RegisterGuideLabel(Vector2 worldAnchor, string text, Color color)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            guideLabels.Add(new GuideLabel(worldAnchor, text, color));
        }

        private void EnsureGuideGuiResources()
        {
            solidTexture ??= Texture2D.whiteTexture;
            if (guideLabelStyle != null)
            {
                return;
            }

            guideLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4),
                normal = { textColor = guideLabelColor }
            };
        }

        private void DrawFilledRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = previousColor;
        }

        private void CollectFloorAxisReferences(FloorData floor, ICollection<float> xReferences, ICollection<float> yReferences, string ignoredObjectId)
        {
            if (floor == null)
            {
                return;
            }

            foreach (var junction in floor.wallJunctions)
            {
                xReferences.Add(junction.position.x);
                yReferences.Add(junction.position.y);
            }

            foreach (var exitZone in floor.exits)
            {
                if (string.Equals(exitZone.exitZoneId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(exitZone.center, exitZone.size, exitZone.rotationDegrees, xReferences, yReferences);
            }

            foreach (var obstacle in floor.obstacles)
            {
                if (string.Equals(obstacle.obstacleId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(obstacle.center, obstacle.size, obstacle.rotationDegrees, xReferences, yReferences);
            }

            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (string.Equals(teleportPortal.teleportPortalId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees, xReferences, yReferences);
            }
        }

        private SandboxAxisGuide ResolveBestVerticalGuide(float targetX, float minY, float maxY, IEnumerable<float> referenceXs, float gridSize, float tolerance)
        {
            return ResolveBestGuide(targetX, minY, maxY, true, referenceXs, gridSize, tolerance);
        }

        private SandboxAxisGuide ResolveBestHorizontalGuide(float targetY, float minX, float maxX, IEnumerable<float> referenceYs, float gridSize, float tolerance)
        {
            return ResolveBestGuide(targetY, minX, maxX, false, referenceYs, gridSize, tolerance);
        }

        private static string FormatSquaresAndDistance(float worldDistance, float gridSize, DistanceUnit distanceUnit, string worldFormat = "0.##")
        {
            var safeGridSize = Mathf.Max(0.05f, gridSize);
            var squares = worldDistance / safeGridSize;
            return $"{squares.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} sq ({SandboxDistanceUnitUtility.FormatDistance(worldDistance, distanceUnit, worldFormat)})";
        }

        private static Vector2 ToGuiPoint(Camera cameraComponent, Vector2 worldPoint)
        {
            if (cameraComponent == null)
            {
                return Vector2.zero;
            }

            var screenPoint = cameraComponent.WorldToScreenPoint(new Vector3(worldPoint.x, worldPoint.y, 0f));
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private SandboxAxisGuide ResolveBestGuide(float targetCoordinate, float minSpan, float maxSpan, bool isVertical, IEnumerable<float> references, float gridSize, float tolerance)
        {
            var bestDistance = float.PositiveInfinity;
            var bestCoordinate = 0f;
            var safeTolerance = Mathf.Max(0.01f, tolerance);
            var safeGridSize = Mathf.Max(0.05f, gridSize);
            var gridCoordinate = Mathf.Round(targetCoordinate / safeGridSize) * safeGridSize;
            var gridDistance = Mathf.Abs(targetCoordinate - gridCoordinate);
            if (gridDistance <= safeTolerance)
            {
                bestDistance = gridDistance;
                bestCoordinate = gridCoordinate;
            }

            if (references != null)
            {
                foreach (var reference in references)
                {
                    var distance = Mathf.Abs(targetCoordinate - reference);
                    if (distance > safeTolerance || distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestCoordinate = reference;
                }
            }

            return float.IsPositiveInfinity(bestDistance)
                ? new SandboxAxisGuide(false, isVertical, 0f, 0f, 0f)
                : new SandboxAxisGuide(true, isVertical, bestCoordinate, Mathf.Min(minSpan, maxSpan) - 0.35f, Mathf.Max(minSpan, maxSpan) + 0.35f);
        }

        private static void AddRectangleReferences(Vector2 center, Vector2 size, float rotationDegrees, ICollection<float> xReferences, ICollection<float> yReferences)
        {
            xReferences.Add(center.x);
            yReferences.Add(center.y);
            var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
            for (var i = 0; i < corners.Length; i += 1)
            {
                xReferences.Add(corners[i].x);
                yReferences.Add(corners[i].y);
            }
        }

        private readonly struct SandboxAxisGuide
        {
            public SandboxAxisGuide(bool isValid, bool isVertical, float coordinate, float start, float end)
            {
                IsValid = isValid;
                IsVertical = isVertical;
                Coordinate = coordinate;
                Start = start;
                End = end;
            }

            public bool IsValid { get; }
            public bool IsVertical { get; }
            public float Coordinate { get; }
            public float Start { get; }
            public float End { get; }
        }
    }
}
