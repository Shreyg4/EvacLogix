using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxDiagnosticsOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private Color colliderColor = new(0.2f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color stairLinkColor = new(1f, 0.4f, 0.9f, 0.95f);
        [SerializeField] private Color passableColor = new(0.2f, 0.95f, 0.35f, 0.75f);
        [SerializeField] private Color blockedColor = new(0.95f, 0.2f, 0.2f, 0.75f);
        [SerializeField] private Color routeColor = new(1f, 0.95f, 0.35f, 1f);
        [SerializeField] private float lineWidth = 0.04f;

        private readonly List<GameObject> renderedObjects = new();
        private SandboxEditorQoLService qosService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;

        private void Awake()
        {
            qosService = FindAnyObjectByType<SandboxEditorQoLService>();
            colliderRebuildService = FindAnyObjectByType<SandboxColliderRebuildService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();

            if (qosService != null)
            {
                qosService.StateChanged += HandleStateChanged;
            }

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
            }

            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt += HandleCollidersRebuilt;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (qosService != null)
            {
                qosService.StateChanged -= HandleStateChanged;
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

            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt -= HandleCollidersRebuilt;
            }
        }

        public void Refresh()
        {
            Clear();

            var floor = workspaceService?.ActiveFloor;
            if (floor == null || qosService == null)
            {
                return;
            }

            if (qosService.ShowColliderOutlines)
            {
                foreach (var collider in colliderRebuildService.GeneratedColliders.Where(candidate => candidate.floorId == floor.floorId))
                {
                    RenderRectangle($"Collider_{collider.colliderId}", collider.center, collider.size, collider.rotationDegrees, colliderColor);
                }
            }

            if (qosService.ShowStairLinks)
            {
                foreach (var stairPortal in floor.stairPortals)
                {
                    var targetFloor = workspaceService.ActiveProject.floors.FirstOrDefault(candidate => candidate.floorId == stairPortal.targetFloorId);
                    var targetPortal = targetFloor?.stairPortals.FirstOrDefault(candidate => candidate.stairPortalId == stairPortal.targetStairPortalId);
                    if (targetPortal == null)
                    {
                        continue;
                    }

                    RenderLine($"StairLink_{stairPortal.stairPortalId}", stairPortal.localPosition, targetPortal.localPosition, stairLinkColor);
                }
            }

            if (qosService.ShowPassableBlockedRegions)
            {
                foreach (var exitZone in floor.exits)
                {
                    RenderRectangle($"PassableExit_{exitZone.exitZoneId}", exitZone.center, exitZone.size, exitZone.rotationDegrees, passableColor);
                }

                foreach (var obstacle in floor.obstacles)
                {
                    RenderRectangle($"BlockedObstacle_{obstacle.obstacleId}", obstacle.center, obstacle.size, obstacle.rotationDegrees, blockedColor);
                }
            }

            if (qosService.ShowRouteInspection)
            {
                RenderRouteInspection(floor);
            }
        }

        private void HandleStateChanged()
        {
            Refresh();
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

        private void HandleCollidersRebuilt(IReadOnlyList<SandboxGeneratedColliderData> colliders, bool wasFullRebuild, string floorId)
        {
            Refresh();
        }

        private void RenderRouteInspection(FloorData floor)
        {
            var exits = floor.exits;
            if (exits.Count == 0 || selectionService == null)
            {
                return;
            }

            foreach (var selectedId in selectionService.SelectedObjectIds)
            {
                if (!TryResolveSelectedPoint(floor, selectedId, out var selectedPoint))
                {
                    continue;
                }

                var nearestExit = exits.OrderBy(exitZone => Vector2.Distance(exitZone.center, selectedPoint)).FirstOrDefault();
                if (nearestExit == null)
                {
                    continue;
                }

                RenderLine($"Route_{selectedId}", selectedPoint, nearestExit.center, routeColor);
            }
        }

        private static bool TryResolveSelectedPoint(FloorData floor, string selectedId, out Vector2 point)
        {
            var exitZone = floor.exits.FirstOrDefault(candidate => candidate.exitZoneId == selectedId);
            if (exitZone != null)
            {
                point = exitZone.center;
                return true;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => candidate.obstacleId == selectedId);
            if (obstacle != null)
            {
                point = obstacle.center;
                return true;
            }

            var stairPortal = floor.stairPortals.FirstOrDefault(candidate => candidate.stairPortalId == selectedId);
            if (stairPortal != null)
            {
                point = stairPortal.localPosition;
                return true;
            }

            var door = floor.doors.FirstOrDefault(candidate => candidate.doorId == selectedId);
            if (door != null)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == door.wallSegmentId);
                if (wall != null)
                {
                    point = wall.startPoint + (wall.endPoint - wall.startPoint).normalized * door.offsetAlongWall;
                    return true;
                }
            }

            var window = floor.windows.FirstOrDefault(candidate => candidate.windowId == selectedId);
            if (window != null)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == window.wallSegmentId);
                if (wall != null)
                {
                    point = wall.startPoint + (wall.endPoint - wall.startPoint).normalized * window.offsetAlongWall;
                    return true;
                }
            }

            point = Vector2.zero;
            return false;
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

            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = corners.Length;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            for (var i = 0; i < corners.Length; i += 1)
            {
                lineRenderer.SetPosition(i, new Vector3(corners[i].x, corners[i].y, 0f));
            }

            renderedObjects.Add(lineObject);
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, 0f));
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            renderedObjects.Add(lineObject);
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
