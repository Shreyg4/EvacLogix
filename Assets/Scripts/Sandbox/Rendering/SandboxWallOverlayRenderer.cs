using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxWallOverlayRenderer : MonoBehaviour
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

        [SerializeField] private Color wallColor = new(0.85f, 0.32f, 0.18f, 1f);
        [SerializeField] private Color selectedWallColor = new(0.98f, 0.78f, 0.22f, 1f);
        [SerializeField] private Color handleColor = new(0.15f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color selectedHandleColor = new(0.98f, 0.9f, 0.36f, 1f);
        [SerializeField] private Color previewColor = new(0.9f, 0.9f, 0.9f, 0.9f);
        [SerializeField] private Color topologyHintColor = new(0.58f, 0.95f, 0.58f, 0.9f);
        [SerializeField] private Color guideLineColor = new(0.82f, 0.88f, 0.96f, 0.72f);
        [SerializeField] private Color guideLabelColor = new(0.98f, 1f, 1f, 1f);
        [SerializeField] private float alignmentGuideTolerance = 0.45f;
        [SerializeField] private float minimumLineWidth = 0.035f;
        [SerializeField] private float handleSize = 0.18f;
        [SerializeField] private float referenceOrthographicSize = 5f;
        [SerializeField] private float maxZoomWidthScale = 4f;
        [SerializeField] private float dragGhostAlpha = 0.45f;

        // renderedObjects is a persistent POOL of GameObject+LineRenderer, not a per-frame allocation.
        // Each Refresh acquires the first activePooledCount entries (reconfiguring them in place) and
        // deactivates the rest. Previously Refresh destroyed every object and created new ones, which —
        // because Refresh runs several times per edit and Destroy is deferred to end-of-frame — left 3-4
        // full copies of the floor's geometry live at the peak, growing the one-way WebGL heap until OOM.
        private readonly List<GameObject> renderedObjects = new();
        private int activePooledCount;
        // Coalesces the multiple change notifications fired per edit (ActiveProjectChanged +
        // ActiveFloorChanged + TopologyChanged, etc.) into a single rebuild in LateUpdate.
        private bool refreshRequested;
        private readonly List<GuideLabel> guideLabels = new();
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxSelectionService selectionService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxWallAuthoringOverlay wallAuthoringOverlay;
        private SandboxObjectInteractionOverlay objectInteractionOverlay;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxEditorQoLService editorQoLService;
        private SandboxStatusBarShell statusBar;
        private Transform handleRoot;
        private Camera targetCamera;
        private Texture2D solidTexture;
        private GUIStyle guideLabelStyle;
        private float lastOrthographicSize = -1f;
        private bool lastPendingLinePreviewActive;
        private Vector2 lastPendingLinePreviewStart;
        private Vector2 lastPendingLinePreviewEnd;
        private bool lastJunctionDragActive;
        private Vector2 lastJunctionDragPreviewPoint;
        private bool lastSelectionDragActive;
        private Vector2 lastSelectionDragCurrentPoint;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            wallAuthoringOverlay = FindAnyObjectByType<SandboxWallAuthoringOverlay>();
            objectInteractionOverlay = FindAnyObjectByType<SandboxObjectInteractionOverlay>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            handleRoot = transform.parent != null ? transform.parent.Find("HandleRoot") : null;
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

            if (wallAuthoringService != null)
            {
                wallAuthoringService.TopologyChanged += HandleTopologyChanged;
                wallAuthoringService.PreviewStateChanged += HandlePreviewStateChanged;
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

        private void LateUpdate()
        {
            targetCamera ??= Camera.main;
            wallAuthoringOverlay ??= FindAnyObjectByType<SandboxWallAuthoringOverlay>();
            wallAuthoringService ??= FindAnyObjectByType<SandboxWallAuthoringService>();
            objectInteractionOverlay ??= FindAnyObjectByType<SandboxObjectInteractionOverlay>();

            if (refreshRequested || HasPreviewStateChanged())
            {
                refreshRequested = false;
                Refresh();
                return;
            }

            if (targetCamera != null &&
                targetCamera.orthographic &&
                !Mathf.Approximately(lastOrthographicSize, targetCamera.orthographicSize))
            {
                Refresh();
            }
        }

        // One shared line material reused by every rendered line. Previously each line allocated its own
        // `new Material(Shader.Find(...))` on every Refresh — and Refresh rebuilds ALL geometry on every
        // edit, so a large project churned thousands of native Material objects per door/window placement,
        // which fed the WebGL OOM. Color is per-LineRenderer (startColor/endColor), so sharing is safe.
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

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }

            if (wallAuthoringService != null)
            {
                wallAuthoringService.TopologyChanged -= HandleTopologyChanged;
                wallAuthoringService.PreviewStateChanged -= HandlePreviewStateChanged;
            }

            if (visualOrganizationService != null)
            {
                visualOrganizationService.VisualStateChanged -= HandleVisualStateChanged;
            }

            if (editorQoLService != null)
            {
                editorQoLService.StateChanged -= HandleVisualStateChanged;
            }
        }

        public void Refresh()
        {
            BeginPooledFrame();
            wallAuthoringOverlay ??= FindAnyObjectByType<SandboxWallAuthoringOverlay>();
            targetCamera ??= Camera.main;

            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                EndPooledFrame();
                RecordCameraState();
                return;
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (!IsWallVisible(wall.wallSegmentId))
                {
                    continue;
                }

                RenderWallLine(ResolvePreviewWall(wall));
            }

            foreach (var junctionId in GetVisibleHandleJunctionIds(floor))
            {
                var junction = floor.wallJunctions.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallJunctionId, junctionId, System.StringComparison.Ordinal));
                if (junction == null)
                {
                    continue;
                }

                RenderHandle(
                    junction.wallJunctionId,
                    ResolvePreviewJunctionPosition(junction),
                    IsSelectedHandle(junction.wallJunctionId));
            }

            RenderSelectedWallEndpoints(floor);
            RenderSelectionDragGhosts(floor);
            RenderPreviewState();
            // Alignment guides are now drawn by the centralized SandboxAlignmentGuideOverlay,
            // which covers every interaction (move/resize/place) for all entity types, not just walls.
            EndPooledFrame();
            RecordCameraState();
        }

        private void OnGUI()
        {
            if (guideLabels.Count == 0 || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureGuideGuiResources();
            foreach (var label in guideLabels)
            {
                var guiPoint = ToGuiPoint(targetCamera ?? Camera.main, label.WorldAnchor);
                var content = new GUIContent(label.Text);
                var size = guideLabelStyle.CalcSize(content);
                var lineCount = Mathf.Max(1, label.Text.Count(character => character == '\n') + 1);
                var rect = new Rect(guiPoint.x - (size.x * 0.5f) - 8f, guiPoint.y - (18f * lineCount) - 18f, size.x + 16f, Mathf.Max(24f, 20f * lineCount));
                DrawFilledRect(rect, new Color(0.05f, 0.08f, 0.12f, 0.92f));
                GUI.Label(rect, label.Text, guideLabelStyle);
            }
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

            var baseColor = visualOrganizationService == null
                ? wallColor
                : visualOrganizationService.GetColor(SandboxVisualObjectType.Wall);
            var ghostColor = new Color(baseColor.r, baseColor.g, baseColor.b, dragGhostAlpha);

            for (var i = 0; i < selectionService.SelectedObjectIds.Count; i += 1)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, selectionService.SelectedObjectIds[i], System.StringComparison.Ordinal));
                if (wall == null || !IsWallVisible(wall.wallSegmentId))
                {
                    continue;
                }

                RenderGhostWallLine(wall, delta, ghostColor);
            }
        }

        private void RenderGhostWallLine(WallSegmentData wall, Vector2 delta, Color color)
        {
            var lineRenderer = AcquireLine($"WallGhost_{wall.wallSegmentId}", transform);
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(wall.startPoint.x + delta.x, wall.startPoint.y + delta.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(wall.endPoint.x + delta.x, wall.endPoint.y + delta.y, 0f));
            lineRenderer.widthMultiplier = ResolveLineWidth(Mathf.Max(minimumLineWidth, wall.thickness * 0.18f));
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
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

        private void HandleTopologyChanged()
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

        private void RenderWallLine(WallSegmentData wall)
        {
            var lineRenderer = AcquireLine($"Wall_{wall.wallSegmentId}", transform);
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(wall.startPoint.x, wall.startPoint.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(wall.endPoint.x, wall.endPoint.y, 0f));
            lineRenderer.widthMultiplier = ResolveLineWidth(Mathf.Max(minimumLineWidth, wall.thickness * 0.18f));
            var baseColor = visualOrganizationService == null
                ? wallColor
                : visualOrganizationService.GetColor(SandboxVisualObjectType.Wall);
            lineRenderer.startColor = IsSelected(wall.wallSegmentId) ? selectedWallColor : baseColor;
            lineRenderer.endColor = IsSelected(wall.wallSegmentId) ? selectedWallColor : baseColor;
        }

        private void RenderHandle(string junctionId, Vector2 position, bool isSelectedHandle)
        {
            if (handleRoot == null)
            {
                return;
            }

            var lineRenderer = AcquireLine($"WallHandle_{junctionId}", handleRoot);
            lineRenderer.positionCount = 4;
            lineRenderer.widthMultiplier = ResolveLineWidth(minimumLineWidth);
            lineRenderer.startColor = isSelectedHandle ? selectedHandleColor : handleColor;
            lineRenderer.endColor = isSelectedHandle ? selectedHandleColor : handleColor;

            var halfSize = handleSize * 0.5f;
            var basePosition = new Vector3(position.x, position.y, 0f);
            lineRenderer.SetPosition(0, basePosition + new Vector3(-halfSize, 0f, 0f));
            lineRenderer.SetPosition(1, basePosition + new Vector3(halfSize, 0f, 0f));
            lineRenderer.SetPosition(2, basePosition + new Vector3(0f, -halfSize, 0f));
            lineRenderer.SetPosition(3, basePosition + new Vector3(0f, halfSize, 0f));
        }

        // When a single wall is selected, mark its two ends distinctly (Start = green square,
        // End = blue diamond) so the inspector's "anchored/adjusted end" labels are identifiable.
        private void RenderSelectedWallEndpoints(FloorData floor)
        {
            if (selectionService == null || selectionService.SelectedObjectIds.Count != 1 || handleRoot == null)
            {
                return;
            }

            var wall = floor.wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.wallSegmentId, selectionService.SelectedObjectIds[0], System.StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            var preview = ResolvePreviewWall(wall);
            RenderEndpointMarker($"WallStartMarker_{wall.wallSegmentId}", preview.startPoint, false, new Color(0.3f, 0.95f, 0.5f, 1f));
            RenderEndpointMarker($"WallEndMarker_{wall.wallSegmentId}", preview.endPoint, true, new Color(0.3f, 0.8f, 1f, 1f));
        }

        private void RenderEndpointMarker(string name, Vector2 position, bool diamond, Color color)
        {
            var lineRenderer = AcquireLine(name, handleRoot);
            lineRenderer.positionCount = 4;
            lineRenderer.loop = true;
            lineRenderer.widthMultiplier = ResolveLineWidth(minimumLineWidth);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            var halfSize = handleSize * 0.9f;
            var basePosition = new Vector3(position.x, position.y, 0f);
            if (diamond)
            {
                lineRenderer.SetPosition(0, basePosition + new Vector3(0f, halfSize, 0f));
                lineRenderer.SetPosition(1, basePosition + new Vector3(halfSize, 0f, 0f));
                lineRenderer.SetPosition(2, basePosition + new Vector3(0f, -halfSize, 0f));
                lineRenderer.SetPosition(3, basePosition + new Vector3(-halfSize, 0f, 0f));
            }
            else
            {
                lineRenderer.SetPosition(0, basePosition + new Vector3(-halfSize, -halfSize, 0f));
                lineRenderer.SetPosition(1, basePosition + new Vector3(halfSize, -halfSize, 0f));
                lineRenderer.SetPosition(2, basePosition + new Vector3(halfSize, halfSize, 0f));
                lineRenderer.SetPosition(3, basePosition + new Vector3(-halfSize, halfSize, 0f));
            }
        }

        private WallSegmentData ResolvePreviewWall(WallSegmentData wall)
        {
            if (wall == null ||
                wallAuthoringOverlay == null ||
                !wallAuthoringOverlay.IsJunctionDragActive)
            {
                return wall;
            }

            var startPoint = ResolvePreviewJunctionPosition(wall.startJunctionId, wall.startPoint);
            var endPoint = ResolvePreviewJunctionPosition(wall.endJunctionId, wall.endPoint);
            if (startPoint == wall.startPoint && endPoint == wall.endPoint)
            {
                return wall;
            }

            return new WallSegmentData
            {
                wallSegmentId = wall.wallSegmentId,
                startJunctionId = wall.startJunctionId,
                endJunctionId = wall.endJunctionId,
                startPoint = startPoint,
                endPoint = endPoint,
                thickness = wall.thickness,
                tags = wall.tags
            };
        }

        private void RenderPreviewState()
        {
            if (wallAuthoringService == null ||
                (visualOrganizationService != null && !visualOrganizationService.IsTypeVisible(SandboxVisualObjectType.Wall)))
            {
                return;
            }

            if (wallAuthoringService.ActiveBrushStrokePoints.Count >= 2)
            {
                RenderPolyline("BrushStrokePreview", wallAuthoringService.ActiveBrushStrokePoints, previewColor, minimumLineWidth);
                return;
            }

            if (wallAuthoringService.LastCleanedBrushStrokePoints.Count >= 2)
            {
                RenderPolyline("BrushStrokeCleanPreview", wallAuthoringService.LastCleanedBrushStrokePoints, previewColor, minimumLineWidth);
            }

            if (wallAuthoringService.HasPendingLineStart)
            {
                RenderCross("PendingLineStart", wallAuthoringService.PendingLineStart, previewColor, handleSize);
                RenderLine("PendingLinePreview", wallAuthoringService.PendingLineStart, wallAuthoringOverlay.CurrentLinePreviewPoint, previewColor);
            }

            if (wallAuthoringOverlay != null && wallAuthoringOverlay.IsJunctionDragActive)
            {
                RenderDragPreviewHint();
            }
        }

        private void RenderAlignmentGuides(FloorData floor)
        {
            guideLabels.Clear();
            targetCamera ??= Camera.main;
            // statusBar can be null when this renderer's Awake ran before the UI shells were
            // installed, so resolve it lazily here instead of relying on the Awake-time lookup.
            statusBar ??= FindAnyObjectByType<SandboxStatusBarShell>();
            if (floor == null || targetCamera == null || workspaceStateService == null)
            {
                return;
            }

            if (wallAuthoringService != null && wallAuthoringService.HasPendingLineStart && wallAuthoringOverlay != null)
            {
                var start = wallAuthoringService.PendingLineStart;
                var end = wallAuthoringOverlay.CurrentLinePreviewPoint;
                var label = BuildWallMeasurementLabel(start, end);
                RenderAxisGuides(floor, start, end, null, guideLineColor);
                RegisterGuideLabel((start + end) * 0.5f, label, guideLabelColor);
                if (statusBar != null)
                {
                    statusBar.StatusMessage = label.Replace('\n', ' ');
                }
                return;
            }

            if (wallAuthoringOverlay != null && wallAuthoringOverlay.IsJunctionDragActive)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, wallAuthoringOverlay.DraggedWallSegmentId, System.StringComparison.Ordinal));
                if (wall == null)
                {
                    return;
                }

                var anchor = wallAuthoringOverlay.DraggedHandleIsStart ? wall.endPoint : wall.startPoint;
                var target = wallAuthoringOverlay.DraggedHandlePreviewPoint;
                var label = BuildWallMeasurementLabel(anchor, target);
                RenderAxisGuides(floor, anchor, target, wall.wallSegmentId, guideLineColor);
                RegisterGuideLabel((anchor + target) * 0.5f, label, guideLabelColor);
                if (statusBar != null)
                {
                    statusBar.StatusMessage = label.Replace('\n', ' ');
                }
            }
        }

        private void RenderDragPreviewHint()
        {
            var snapResult = wallAuthoringOverlay.DragPreviewSnapResult;
            switch (snapResult.targetKind)
            {
                case SandboxWallSnapTargetKind.Endpoint:
                case SandboxWallSnapTargetKind.Segment:
                    RenderCross("DragTopologyHint", snapResult.position, topologyHintColor, handleSize * 1.2f);
                    break;
            }
        }

        private void RenderPolyline(string name, IReadOnlyList<Vector2> points, Color color, float width)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            var lineRenderer = AcquireLine(name, transform);
            lineRenderer.positionCount = points.Count;
            lineRenderer.widthMultiplier = ResolveLineWidth(width);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            for (var i = 0; i < points.Count; i += 1)
            {
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0f));
            }
        }

        private void RenderCross(string name, Vector2 center, Color color, float size)
        {
            var halfSize = size * 0.5f;
            RenderLine($"{name}_A", center + new Vector2(-halfSize, 0f), center + new Vector2(halfSize, 0f), color);
            RenderLine($"{name}_B", center + new Vector2(0f, -halfSize), center + new Vector2(0f, halfSize), color);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color)
        {
            var lineRenderer = AcquireLine(name, transform);
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, 0f));
            lineRenderer.widthMultiplier = ResolveLineWidth(minimumLineWidth);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color, float widthMultiplier)
        {
            var lineRenderer = AcquireLine(name, transform);
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, 0.01f));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, 0.01f));
            lineRenderer.widthMultiplier = ResolveLineWidth(widthMultiplier);
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
        }

        private void RenderAxisGuides(FloorData floor, Vector2 anchor, Vector2 target, string ignoredObjectId, Color color)
        {
            var xReferences = new List<float>();
            var yReferences = new List<float>();
            CollectFloorAxisReferences(floor, xReferences, yReferences, ignoredObjectId);

            var verticalGuide = ResolveBestVerticalGuide(
                target.x,
                Mathf.Min(anchor.y, target.y),
                Mathf.Max(anchor.y, target.y),
                xReferences,
                workspaceStateService.GridSize,
                alignmentGuideTolerance);
            if (verticalGuide.IsValid)
            {
                RenderLine(
                    "AlignmentGuide_V",
                    new Vector2(verticalGuide.Coordinate, verticalGuide.Start),
                    new Vector2(verticalGuide.Coordinate, verticalGuide.End),
                    color,
                    minimumLineWidth * 0.8f);
            }

            var horizontalGuide = ResolveBestHorizontalGuide(
                target.y,
                Mathf.Min(anchor.x, target.x),
                Mathf.Max(anchor.x, target.x),
                yReferences,
                workspaceStateService.GridSize,
                alignmentGuideTolerance);
            if (horizontalGuide.IsValid)
            {
                RenderLine(
                    "AlignmentGuide_H",
                    new Vector2(horizontalGuide.Start, horizontalGuide.Coordinate),
                    new Vector2(horizontalGuide.End, horizontalGuide.Coordinate),
                    color,
                    minimumLineWidth * 0.8f);
            }
        }

        private string BuildWallMeasurementLabel(Vector2 start, Vector2 end)
        {
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            var delta = end - start;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            var length = delta.magnitude;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            return
                $"DX: {FormatSquaresAndDistance(Mathf.Abs(delta.x), gridSize, distanceUnit)}\n" +
                $"DY: {FormatSquaresAndDistance(Mathf.Abs(delta.y), gridSize, distanceUnit)}\n" +
                $"L: {FormatSquaresAndDistance(length, gridSize, distanceUnit)}\n" +
                $"A: {FormatAngle(angle)}";
        }

        private float ResolveLineWidth(float baseWidth)
        {
            if (targetCamera == null || !targetCamera.orthographic)
            {
                return baseWidth;
            }

            var safeReferenceSize = Mathf.Max(0.01f, referenceOrthographicSize);
            var zoomScale = Mathf.Clamp(targetCamera.orthographicSize / safeReferenceSize, 1f, Mathf.Max(1f, maxZoomWidthScale));
            return baseWidth * zoomScale;
        }

        private void RecordCameraState()
        {
            lastOrthographicSize = targetCamera != null && targetCamera.orthographic
                ? targetCamera.orthographicSize
                : -1f;
            lastPendingLinePreviewActive = wallAuthoringService != null && wallAuthoringService.HasPendingLineStart;
            lastPendingLinePreviewStart = lastPendingLinePreviewActive && wallAuthoringService != null
                ? wallAuthoringService.PendingLineStart
                : Vector2.zero;
            lastPendingLinePreviewEnd = lastPendingLinePreviewActive && wallAuthoringOverlay != null
                ? wallAuthoringOverlay.CurrentLinePreviewPoint
                : Vector2.zero;
            lastJunctionDragActive = wallAuthoringOverlay != null && wallAuthoringOverlay.IsJunctionDragActive;
            lastJunctionDragPreviewPoint = lastJunctionDragActive && wallAuthoringOverlay != null
                ? wallAuthoringOverlay.DraggedHandlePreviewPoint
                : Vector2.zero;
            lastSelectionDragActive = objectInteractionOverlay != null && objectInteractionOverlay.IsSelectionDragActive;
            lastSelectionDragCurrentPoint = lastSelectionDragActive && objectInteractionOverlay != null
                ? objectInteractionOverlay.SelectionDragCurrentWorldPoint
                : Vector2.zero;
        }

        private bool HasPreviewStateChanged()
        {
            var pendingLinePreviewActive = wallAuthoringService != null && wallAuthoringService.HasPendingLineStart;
            if (pendingLinePreviewActive != lastPendingLinePreviewActive)
            {
                return true;
            }

            if (pendingLinePreviewActive && wallAuthoringOverlay != null && wallAuthoringService != null)
            {
                if (wallAuthoringService.PendingLineStart != lastPendingLinePreviewStart ||
                    wallAuthoringOverlay.CurrentLinePreviewPoint != lastPendingLinePreviewEnd)
                {
                    return true;
                }
            }

            var junctionDragActive = wallAuthoringOverlay != null && wallAuthoringOverlay.IsJunctionDragActive;
            if (junctionDragActive != lastJunctionDragActive)
            {
                return true;
            }

            if (junctionDragActive && wallAuthoringOverlay != null &&
                wallAuthoringOverlay.DraggedHandlePreviewPoint != lastJunctionDragPreviewPoint)
            {
                return true;
            }

            var selectionDragActive = objectInteractionOverlay != null && objectInteractionOverlay.IsSelectionDragActive;
            if (selectionDragActive != lastSelectionDragActive)
            {
                return true;
            }

            if (selectionDragActive && objectInteractionOverlay != null &&
                objectInteractionOverlay.SelectionDragCurrentWorldPoint != lastSelectionDragCurrentPoint)
            {
                return true;
            }

            return false;
        }

        private bool IsSelected(string wallSegmentId)
        {
            return selectionService != null && selectionService.SelectedObjectIds.Contains(wallSegmentId);
        }

        private bool IsSelectedHandle(string junctionId)
        {
            return wallAuthoringOverlay != null &&
                   wallAuthoringOverlay.SelectedJunctionIds.Contains(junctionId, System.StringComparer.Ordinal);
        }

        private IReadOnlyCollection<string> GetVisibleHandleJunctionIds(FloorData floor)
        {
            var junctionIds = new HashSet<string>(System.StringComparer.Ordinal);
            if (wallAuthoringOverlay != null)
            {
                for (var i = 0; i < wallAuthoringOverlay.SelectedJunctionIds.Count; i += 1)
                {
                    junctionIds.Add(wallAuthoringOverlay.SelectedJunctionIds[i]);
                }
            }

            if (selectionService == null)
            {
                return junctionIds;
            }

            for (var i = 0; i < selectionService.SelectedObjectIds.Count; i += 1)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, selectionService.SelectedObjectIds[i], System.StringComparison.Ordinal));
                if (wall == null)
                {
                    continue;
                }

                junctionIds.Add(wall.startJunctionId);
                junctionIds.Add(wall.endJunctionId);
            }

            return junctionIds;
        }

        private Vector2 ResolvePreviewJunctionPosition(WallJunctionData junction)
        {
            return ResolvePreviewJunctionPosition(junction.wallJunctionId, junction.position);
        }

        private Vector2 ResolvePreviewJunctionPosition(string junctionId, Vector2 fallbackPosition)
        {
            return wallAuthoringOverlay != null && wallAuthoringOverlay.TryGetPreviewJunctionPosition(junctionId, out var previewPosition)
                ? previewPosition
                : fallbackPosition;
        }

        private bool IsWallVisible(string wallSegmentId)
        {
            return (visualOrganizationService == null ||
                    (visualOrganizationService.IsTypeVisible(SandboxVisualObjectType.Wall) &&
                     !visualOrganizationService.IsObjectHidden(wallSegmentId))) &&
                   (editorQoLService == null || editorQoLService.IsObjectVisibleForIsolation(wallSegmentId, SandboxVisualObjectType.Wall));
        }

        // Start a pooled render pass: reset the active cursor so AcquireLine hands out (and reconfigures)
        // existing pooled objects from the top, reusing them instead of allocating fresh ones.
        private void BeginPooledFrame()
        {
            guideLabels.Clear();
            activePooledCount = 0;
        }

        // Finish a pooled render pass: deactivate any pooled objects that weren't reused this frame. They
        // stay allocated for the next pass (no destroy/recreate churn) but don't render while idle.
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

        // Hands out a pooled GameObject+LineRenderer, reconfiguring its common state, instead of creating
        // a new one each call. Every rendered element here is a LineRenderer, so one pool serves them all.
        private LineRenderer AcquireLine(string objectName, Transform parent)
        {
            GameObject pooledObject;
            LineRenderer lineRenderer;
            if (activePooledCount < renderedObjects.Count)
            {
                pooledObject = renderedObjects[activePooledCount];
                if (pooledObject == null)
                {
                    pooledObject = CreatePooledLineObject(out lineRenderer);
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
                renderedObjects.Add(pooledObject);
            }

            activePooledCount += 1;

            pooledObject.name = objectName;
            if (parent != null && pooledObject.transform.parent != parent)
            {
                pooledObject.transform.SetParent(parent, false);
            }

            pooledObject.transform.localPosition = Vector3.zero;
            pooledObject.transform.localRotation = Quaternion.identity;
            pooledObject.transform.localScale = Vector3.one;

            // Reset the state a reused object might carry over from a different element type.
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = false;
            lineRenderer.sharedMaterial = GetLineMaterial();
            return lineRenderer;
        }

        private static GameObject CreatePooledLineObject(out LineRenderer lineRenderer)
        {
            var pooledObject = new GameObject("PooledWallLine");
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
                if (string.Equals(exitZone.exitZoneId, ignoredObjectId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(exitZone.center, exitZone.size, exitZone.rotationDegrees, xReferences, yReferences);
            }

            foreach (var obstacle in floor.obstacles)
            {
                if (string.Equals(obstacle.obstacleId, ignoredObjectId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(obstacle.center, obstacle.size, obstacle.rotationDegrees, xReferences, yReferences);
            }

            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (string.Equals(teleportPortal.teleportPortalId, ignoredObjectId, System.StringComparison.Ordinal))
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

        private static string FormatAngle(float angleDegrees)
        {
            return $"{angleDegrees.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} deg";
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

        private void AddRectangleReferences(Vector2 center, Vector2 size, float rotationDegrees, ICollection<float> xReferences, ICollection<float> yReferences)
        {
            xReferences.Add(center.x);
            yReferences.Add(center.y);
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            var corners = new[]
            {
                center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(-half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, -half.y, 0f))
            };

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
