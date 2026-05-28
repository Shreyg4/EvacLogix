using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxSemanticObjectRenderer : MonoBehaviour
    {
        [SerializeField] private Color selectedColor = new(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color openingMaskColor = new(0.11f, 0.18f, 0.3f, 1f);
        [SerializeField] private float lineWidth = 0.05f;
        [SerializeField] private float openingEdgeWidth = 0.1f;
        [SerializeField] private float openingMaskWidth = 0.18f;
        [SerializeField] private float openingEdgeLength = 0.42f;
        [SerializeField] private float markerRadius = 0.25f;

        private readonly List<GameObject> renderedObjects = new();
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxSelectionService selectionService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxEditorQoLService editorQoLService;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();

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

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }

            if (semanticObjectAuthoringService != null)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged -= HandleSemanticObjectsChanged;
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
            Clear();

            var floor = workspaceService?.ActiveFloor;
            var project = workspaceService?.ActiveProject;
            if (floor == null || project == null)
            {
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

                    RenderRectangle(
                        $"Exit_{exitZone.exitZoneId}",
                        exitZone.center,
                        exitZone.size,
                        exitZone.rotationDegrees,
                        ResolveSelectionColor(exitZone.exitZoneId, ResolveBaseColor(SandboxVisualObjectType.Exit)));
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

                    RenderRectangle(
                        $"Obstacle_{obstacle.obstacleId}",
                        obstacle.center,
                        obstacle.size,
                        obstacle.rotationDegrees,
                        ResolveSelectionColor(obstacle.obstacleId, ResolveBaseColor(SandboxVisualObjectType.Obstacle)));
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

            if (IsVisible(SandboxVisualObjectType.Region))
            {
                foreach (var region in floor.regions)
                {
                    if (IsHidden(region.regionId, SandboxVisualObjectType.Region))
                    {
                        continue;
                    }

                    RenderPolygon(
                        $"Region_{region.regionId}",
                        region.polygonPoints,
                        ResolveSelectionColor(region.regionId, ResolveBaseColor(SandboxVisualObjectType.Region)));
                }
            }

            if (IsVisible(SandboxVisualObjectType.Spawn))
            {
                foreach (var layout in project.spawnLayouts)
                {
                    foreach (var spawnPoint in layout.spawnPoints.Where(point => point.floorId == floor.floorId))
                    {
                        if (IsHidden(spawnPoint.spawnPointId, SandboxVisualObjectType.Spawn))
                        {
                            continue;
                        }

                        RenderDiamond(
                            $"SpawnPoint_{spawnPoint.spawnPointId}",
                            spawnPoint.position,
                            ResolveSelectionColor(spawnPoint.spawnPointId, ResolveBaseColor(SandboxVisualObjectType.Spawn)));
                    }

                    foreach (var spawnBrushStroke in layout.spawnBrushStrokes.Where(stroke => stroke.floorId == floor.floorId))
                    {
                        if (IsHidden(spawnBrushStroke.spawnBrushStrokeId, SandboxVisualObjectType.Spawn))
                        {
                            continue;
                        }

                        RenderPolygon(
                            $"SpawnBrush_{spawnBrushStroke.spawnBrushStrokeId}",
                            spawnBrushStroke.polygonPoints,
                            ResolveSelectionColor(spawnBrushStroke.spawnBrushStrokeId, ResolveBaseColor(SandboxVisualObjectType.Spawn)));
                    }
                }
            }
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            Refresh();
        }

        private void HandleFloorChanged(FloorData floor)
        {
            Refresh();
        }

        private void HandleSelectionChanged(IReadOnlyList<string> selection)
        {
            Refresh();
        }

        private void HandleSemanticObjectsChanged()
        {
            Refresh();
        }

        private void HandleVisualStateChanged()
        {
            Refresh();
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
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            var corners = new[]
            {
                center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(-half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, -half.y, 0f))
            };

            RenderPolyline(name, corners, color, true);
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

        private void RenderCross(string name, Vector2 center, Color color)
        {
            RenderLine($"{name}_A", center + new Vector2(-markerRadius, -markerRadius), center + new Vector2(markerRadius, markerRadius), color);
            RenderLine($"{name}_B", center + new Vector2(-markerRadius, markerRadius), center + new Vector2(markerRadius, -markerRadius), color);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color)
        {
            RenderLine(name, start, end, color, lineWidth, 0f);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color, float width, float zOffset)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, zOffset));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, zOffset));
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = width;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            renderedObjects.Add(lineObject);
        }

        private void RenderPolyline(string name, IReadOnlyList<Vector2> points, Color color, bool loop)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = points.Count;
            lineRenderer.loop = loop;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            for (var i = 0; i < points.Count; i += 1)
            {
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0f));
            }

            renderedObjects.Add(lineObject);
        }

        private Color ResolveDoorColor(DoorData door)
        {
            var baseColor = ResolveBaseColor(SandboxVisualObjectType.Door);
            return door.state == DoorState.Normal || door.state == DoorState.Closed || door.state == DoorState.OneWay
                ? ResolveSelectionColor(door.doorId, baseColor)
                : ResolveSelectionColor(door.doorId, Color.Lerp(baseColor, Color.red, 0.35f));
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

        private void Clear()
        {
            for (var i = 0; i < renderedObjects.Count; i += 1)
            {
                if (renderedObjects[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(renderedObjects[i]);
                    }
                    else
                    {
                        DestroyImmediate(renderedObjects[i]);
                    }
                }
            }

            renderedObjects.Clear();
        }
    }
}
