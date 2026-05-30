using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    // Centralized, every-frame alignment guide overlay. While an entity is being drawn, moved,
    // resized, or placed, it draws viewport-spanning guide lines when the entity lines up with
    // another entity OF THE SAME TYPE (or the grid), plus a measurement label. Visual only:
    // it never changes where anything lands.
    public sealed class SandboxAlignmentGuideOverlay : MonoBehaviour
    {
        [SerializeField] private Color entityGuideColor = new(0.46f, 0.86f, 1f, 0.95f);
        [SerializeField] private Color wallGuideColor = new(1f, 0.72f, 0.28f, 0.95f);
        [SerializeField] private Color intersectionGuideColor = new(0.55f, 0.95f, 0.6f, 0.85f);
        [SerializeField] private Color gridGuideColor = new(0.82f, 0.88f, 0.96f, 0.4f);
        [SerializeField] private Color labelColor = new(0.98f, 1f, 1f, 1f);
        [SerializeField] private float guidePixelTolerance = 8f;
        [SerializeField] private float guideLinePixelThickness = 2f;

        private const int GuiDepthBehindHud = 50;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxToolStateService toolStateService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxWallAuthoringOverlay wallAuthoringOverlay;
        private SandboxObjectInteractionOverlay objectInteractionOverlay;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxPreviewService previewService;
        private Camera targetCamera;
        private Texture2D solidTexture;
        private GUIStyle labelStyle;

        private readonly struct Manipulation
        {
            public Manipulation(SandboxVisualObjectType type, List<float> candidateXs, List<float> candidateYs, Vector2 labelWorldAnchor, string label, string ignoredObjectId)
            {
                Type = type;
                CandidateXs = candidateXs;
                CandidateYs = candidateYs;
                LabelWorldAnchor = labelWorldAnchor;
                Label = label;
                IgnoredObjectId = ignoredObjectId;
            }

            public SandboxVisualObjectType Type { get; }
            public List<float> CandidateXs { get; }
            public List<float> CandidateYs { get; }
            public Vector2 LabelWorldAnchor { get; }
            public string Label { get; }
            public string IgnoredObjectId { get; }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // Render behind the HUD (which draws at depth 0) so guide lines/labels stay over the
            // canvas but never paint over the editor panels. Higher GUI.depth = further back.
            GUI.depth = GuiDepthBehindHud;
            EnsureDependencies();
            if (targetCamera == null || !targetCamera.orthographic || workspaceService?.ActiveFloor == null || workspaceStateService == null)
            {
                return;
            }

            if (previewService != null && previewService.IsPreviewModeActive)
            {
                return;
            }

            if (!TryGetActiveManipulation(workspaceService.ActiveFloor, out var manipulation))
            {
                return;
            }

            var floor = workspaceService.ActiveFloor;
            var gridSize = workspaceStateService.GridSize;
            var worldTolerance = SandboxAlignmentGuideUtility.PixelToleranceToWorld(targetCamera, guidePixelTolerance);

            // Same-type peer references (for walls, this already includes the rich wall geometry).
            var peerXs = new List<float>();
            var peerYs = new List<float>();
            SandboxAlignmentGuideUtility.CollectSameTypeAxisReferences(floor, manipulation.Type, manipulation.IgnoredObjectId, peerXs, peerYs);

            // Wall references are a universal anchor for every NON-wall type.
            var wallXs = new List<float>();
            var wallYs = new List<float>();
            if (manipulation.Type != SandboxVisualObjectType.Wall)
            {
                SandboxAlignmentGuideUtility.CollectWallAxisReferences(floor, wallXs, wallYs);
            }

            var vertical = ResolveAxis(manipulation.CandidateXs, peerXs, wallXs, gridSize, worldTolerance, true);
            var horizontal = ResolveAxis(manipulation.CandidateYs, peerYs, wallYs, gridSize, worldTolerance, false);

            if (vertical.guide.IsValid)
            {
                DrawVerticalGuide(vertical.guide.Coordinate, ResolveGuideColor(vertical.guide, vertical.isWall));
            }

            if (horizontal.guide.IsValid)
            {
                DrawHorizontalGuide(horizontal.guide.Coordinate, ResolveGuideColor(horizontal.guide, horizontal.isWall));
            }

            // Mark the wall intersection you're aligned to (junction or segment crossing) with a
            // green cross — detected from the raw cursor and uncapped, so it appears from any
            // distance along whichever axis is aligned.
            var rawCursorWorld = ScreenToWorld(SandboxInputAdapter.PointerScreenPosition);
            if (SandboxAlignmentGuideUtility.TryFindAxisAlignedIntersection(floor, rawCursorWorld, worldTolerance, out var intersectionPoint))
            {
                DrawVerticalGuide(intersectionPoint.x, intersectionGuideColor);
                DrawHorizontalGuide(intersectionPoint.y, intersectionGuideColor);
            }

            DrawLabel(manipulation.LabelWorldAnchor, manipulation.Label);
            if (statusBar != null && !string.IsNullOrWhiteSpace(manipulation.Label))
            {
                statusBar.StatusMessage = manipulation.Label.Replace('\n', ' ');
            }
        }

        private bool TryGetActiveManipulation(FloorData floor, out Manipulation manipulation)
        {
            manipulation = default;

            // 1. Drawing a wall line.
            if (wallAuthoringService != null && wallAuthoringService.HasPendingLineStart && wallAuthoringOverlay != null)
            {
                var start = wallAuthoringService.PendingLineStart;
                var end = wallAuthoringOverlay.CurrentLinePreviewPoint;
                manipulation = new Manipulation(
                    SandboxVisualObjectType.Wall,
                    new List<float> { end.x },
                    new List<float> { end.y },
                    (start + end) * 0.5f,
                    BuildWallLabel(start, end),
                    null);
                return true;
            }

            // 2. Dragging a wall endpoint handle.
            if (wallAuthoringOverlay != null && wallAuthoringOverlay.IsJunctionDragActive)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, wallAuthoringOverlay.DraggedWallSegmentId, System.StringComparison.Ordinal));
                if (wall != null)
                {
                    var anchor = wallAuthoringOverlay.DraggedHandleIsStart ? wall.endPoint : wall.startPoint;
                    var target = wallAuthoringOverlay.DraggedHandlePreviewPoint;
                    manipulation = new Manipulation(
                        SandboxVisualObjectType.Wall,
                        new List<float> { target.x },
                        new List<float> { target.y },
                        (anchor + target) * 0.5f,
                        BuildWallLabel(anchor, target),
                        wall.wallSegmentId);
                    return true;
                }
            }

            // 3. Resizing a rectangle (exit / obstacle / teleport) via a corner handle.
            if (objectInteractionOverlay != null && objectInteractionOverlay.IsRectangleHandleDragActive &&
                objectInteractionOverlay.DraggedRectangleObjectType.HasValue)
            {
                var center = objectInteractionOverlay.DraggedRectanglePreviewCenter;
                var size = objectInteractionOverlay.DraggedRectanglePreviewSize;
                var rotation = objectInteractionOverlay.DraggedRectanglePreviewRotationDegrees;
                var candidateXs = new List<float>();
                var candidateYs = new List<float>();
                SandboxAlignmentGuideUtility.AppendRectangleCandidates(center, size, rotation, candidateXs, candidateYs);
                manipulation = new Manipulation(
                    objectInteractionOverlay.DraggedRectangleObjectType.Value,
                    candidateXs,
                    candidateYs,
                    center,
                    BuildSizeLabel(size),
                    objectInteractionOverlay.DraggedRectangleObjectId);
                return true;
            }

            // 4. Moving a selected object (drag-and-drop).
            if (objectInteractionOverlay != null && objectInteractionOverlay.IsSelectionDragActive)
            {
                var delta = objectInteractionOverlay.SelectionDragCurrentWorldPoint - objectInteractionOverlay.SelectionDragStartWorldPoint;
                if (TryResolveDraggedObject(floor, objectInteractionOverlay.DraggedObjectId, delta, out manipulation))
                {
                    return true;
                }
            }

            // 5. Placing a new object with a placement tool.
            if (toolStateService != null && TryGetPlacementType(toolStateService.CurrentToolMode, out var placementType) && IsPointerOverWorld())
            {
                var cursor = ScreenToWorld(SandboxInputAdapter.PointerScreenPosition);
                manipulation = new Manipulation(
                    placementType,
                    new List<float> { cursor.x },
                    new List<float> { cursor.y },
                    cursor,
                    string.Empty,
                    null);
                return true;
            }

            return false;
        }

        private bool TryResolveDraggedObject(FloorData floor, string objectId, Vector2 delta, out Manipulation manipulation)
        {
            manipulation = default;
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            var exit = floor.exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, objectId, System.StringComparison.Ordinal));
            if (exit != null)
            {
                manipulation = BuildRectangleManipulation(SandboxVisualObjectType.Exit, exit.center + delta, exit.size, exit.rotationDegrees, objectId);
                return true;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, objectId, System.StringComparison.Ordinal));
            if (obstacle != null)
            {
                manipulation = BuildRectangleManipulation(SandboxVisualObjectType.Obstacle, obstacle.center + delta, obstacle.size, obstacle.rotationDegrees, objectId);
                return true;
            }

            var teleportPortal = floor.teleportPortals.FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, objectId, System.StringComparison.Ordinal));
            if (teleportPortal != null)
            {
                manipulation = BuildRectangleManipulation(SandboxVisualObjectType.Teleport, teleportPortal.localPosition + delta, teleportPortal.size, teleportPortal.rotationDegrees, objectId);
                return true;
            }

            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, objectId, System.StringComparison.Ordinal));
            if (wall != null)
            {
                var start = wall.startPoint + delta;
                var end = wall.endPoint + delta;
                manipulation = new Manipulation(
                    SandboxVisualObjectType.Wall,
                    new List<float> { start.x, end.x },
                    new List<float> { start.y, end.y },
                    (start + end) * 0.5f,
                    BuildWallLabel(start, end),
                    objectId);
                return true;
            }

            var door = floor.doors.FirstOrDefault(candidate => string.Equals(candidate.doorId, objectId, System.StringComparison.Ordinal));
            if (door != null && SandboxAlignmentGuideUtility.TryResolveOpeningCenter(floor, door.wallSegmentId, door.offsetAlongWall, out var doorCenter))
            {
                manipulation = BuildPointManipulation(SandboxVisualObjectType.Door, doorCenter + delta, BuildWidthLabel(door.width), objectId);
                return true;
            }

            var window = floor.windows.FirstOrDefault(candidate => string.Equals(candidate.windowId, objectId, System.StringComparison.Ordinal));
            if (window != null && SandboxAlignmentGuideUtility.TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out var windowCenter))
            {
                manipulation = BuildPointManipulation(SandboxVisualObjectType.Window, windowCenter + delta, BuildWidthLabel(window.width), objectId);
                return true;
            }

            return false;
        }

        private Manipulation BuildRectangleManipulation(SandboxVisualObjectType type, Vector2 center, Vector2 size, float rotationDegrees, string ignoredObjectId)
        {
            var candidateXs = new List<float>();
            var candidateYs = new List<float>();
            SandboxAlignmentGuideUtility.AppendRectangleCandidates(center, size, rotationDegrees, candidateXs, candidateYs);
            return new Manipulation(type, candidateXs, candidateYs, center, BuildSizeLabel(size), ignoredObjectId);
        }

        private Manipulation BuildPointManipulation(SandboxVisualObjectType type, Vector2 point, string label, string ignoredObjectId)
        {
            return new Manipulation(type, new List<float> { point.x }, new List<float> { point.y }, point, label, ignoredObjectId);
        }

        // Resolves the strongest guide for one axis against same-type peers and walls separately,
        // so the winner can be colored by its source (peer vs wall vs grid).
        private static (SandboxAxisGuide guide, bool isWall) ResolveAxis(
            List<float> candidates,
            List<float> peerReferences,
            List<float> wallReferences,
            float gridSize,
            float tolerance,
            bool isVertical)
        {
            var peerGuide = ResolveStrongestGuide(candidates, peerReferences, gridSize, tolerance, isVertical);
            var wallGuide = ResolveStrongestGuide(candidates, wallReferences, gridSize, tolerance, isVertical);

            // Prefer the closer match; ties go to the peer guide.
            if (wallGuide.IsValid && (!peerGuide.IsValid || wallGuide.Distance < peerGuide.Distance))
            {
                return (wallGuide, !wallGuide.IsGrid);
            }

            return (peerGuide, false);
        }

        private Color ResolveGuideColor(SandboxAxisGuide guide, bool isWall)
        {
            if (guide.IsGrid)
            {
                return gridGuideColor;
            }

            return isWall ? wallGuideColor : entityGuideColor;
        }

        private static SandboxAxisGuide ResolveStrongestGuide(List<float> candidates, List<float> references, float gridSize, float tolerance, bool isVertical)
        {
            var best = new SandboxAxisGuide(false, isVertical, 0f, 0f, 0f, false, 0f);
            if (candidates == null)
            {
                return best;
            }

            for (var i = 0; i < candidates.Count; i += 1)
            {
                var guide = isVertical
                    ? SandboxAlignmentGuideUtility.ResolveBestVerticalGuide(candidates[i], 0f, 0f, references, gridSize, tolerance)
                    : SandboxAlignmentGuideUtility.ResolveBestHorizontalGuide(candidates[i], 0f, 0f, references, gridSize, tolerance);
                if (guide.IsValid && (!best.IsValid || guide.Distance < best.Distance))
                {
                    best = guide;
                }
            }

            return best;
        }

        private static bool TryGetPlacementType(SandboxToolMode toolMode, out SandboxVisualObjectType type)
        {
            switch (toolMode)
            {
                case SandboxToolMode.Exit:
                    type = SandboxVisualObjectType.Exit;
                    return true;
                case SandboxToolMode.Obstacle:
                    type = SandboxVisualObjectType.Obstacle;
                    return true;
                case SandboxToolMode.Teleport:
                    type = SandboxVisualObjectType.Teleport;
                    return true;
                case SandboxToolMode.Door:
                    type = SandboxVisualObjectType.Door;
                    return true;
                case SandboxToolMode.Window:
                    type = SandboxVisualObjectType.Window;
                    return true;
                default:
                    type = default;
                    return false;
            }
        }

        private string BuildWallLabel(Vector2 start, Vector2 end)
        {
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            var delta = end - start;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            return
                $"DX: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(Mathf.Abs(delta.x), gridSize, distanceUnit)}\n" +
                $"DY: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(Mathf.Abs(delta.y), gridSize, distanceUnit)}\n" +
                $"L: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(delta.magnitude, gridSize, distanceUnit)}\n" +
                $"A: {SandboxAlignmentGuideUtility.FormatAngle(angle)}";
        }

        private string BuildSizeLabel(Vector2 size)
        {
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            return
                $"W: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(Mathf.Abs(size.x), gridSize, distanceUnit)}\n" +
                $"H: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(Mathf.Abs(size.y), gridSize, distanceUnit)}";
        }

        private string BuildWidthLabel(float width)
        {
            var distanceUnit = workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            return $"W: {SandboxAlignmentGuideUtility.FormatSquaresAndDistance(Mathf.Abs(width), gridSize, distanceUnit)}";
        }

        private void DrawVerticalGuide(float worldX, Color color)
        {
            var screenX = targetCamera.WorldToScreenPoint(new Vector3(worldX, targetCamera.transform.position.y, 0f)).x;
            var thickness = Mathf.Max(1f, guideLinePixelThickness);
            DrawFilledRect(new Rect(screenX - (thickness * 0.5f), 0f, thickness, Screen.height), color);
        }

        private void DrawHorizontalGuide(float worldY, Color color)
        {
            var screenPoint = targetCamera.WorldToScreenPoint(new Vector3(targetCamera.transform.position.x, worldY, 0f));
            var guiY = Screen.height - screenPoint.y;
            var thickness = Mathf.Max(1f, guideLinePixelThickness);
            DrawFilledRect(new Rect(0f, guiY - (thickness * 0.5f), Screen.width, thickness), color);
        }

        private void DrawLabel(Vector2 worldAnchor, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            EnsureGuiResources();
            var guiPoint = SandboxAlignmentGuideUtility.ToGuiPoint(targetCamera, worldAnchor);
            var content = new GUIContent(text);
            var size = labelStyle.CalcSize(content);
            var lineCount = Mathf.Max(1, text.Count(character => character == '\n') + 1);
            var rect = new Rect(guiPoint.x - (size.x * 0.5f) - 8f, guiPoint.y - (18f * lineCount) - 18f, size.x + 16f, Mathf.Max(24f, 20f * lineCount));
            DrawFilledRect(rect, new Color(0.05f, 0.08f, 0.12f, 0.92f));
            GUI.Label(rect, text, labelStyle);
        }

        private void DrawFilledRect(Rect rect, Color color)
        {
            EnsureGuiResources();
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = previousColor;
        }

        private Vector2 ScreenToWorld(Vector3 screenPoint)
        {
            screenPoint.z = Mathf.Abs(targetCamera.transform.position.z);
            var worldPoint = targetCamera.ScreenToWorldPoint(screenPoint);
            return new Vector2(worldPoint.x, worldPoint.y);
        }

        private bool IsPointerOverWorld()
        {
            return inputRouter == null ||
                   inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition) == SandboxInputTarget.World;
        }

        private void EnsureGuiResources()
        {
            solidTexture ??= Texture2D.whiteTexture;
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                padding = new RectOffset(6, 6, 4, 4),
                normal = { textColor = labelColor }
            };
        }

        private void EnsureDependencies()
        {
            targetCamera ??= Camera.main;
            workspaceService ??= FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService ??= FindAnyObjectByType<SandboxWorkspaceStateService>();
            toolStateService ??= FindAnyObjectByType<SandboxToolStateService>();
            wallAuthoringService ??= FindAnyObjectByType<SandboxWallAuthoringService>();
            wallAuthoringOverlay ??= FindAnyObjectByType<SandboxWallAuthoringOverlay>();
            objectInteractionOverlay ??= FindAnyObjectByType<SandboxObjectInteractionOverlay>();
            inputRouter ??= FindAnyObjectByType<SandboxInputRouter>();
            statusBar ??= FindAnyObjectByType<SandboxStatusBarShell>();
            previewService ??= FindAnyObjectByType<SandboxPreviewService>();
        }
    }
}
