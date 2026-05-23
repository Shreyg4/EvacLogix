using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxValidationHighlightRenderer : MonoBehaviour
    {
        [SerializeField] private Color warningColor = new(1f, 0.75f, 0.2f, 1f);
        [SerializeField] private Color blockingColor = new(0.9f, 0.2f, 0.2f, 1f);
        [SerializeField] private float lineWidth = 0.05f;
        [SerializeField] private float markerSize = 0.4f;

        private readonly List<GameObject> highlightObjects = new();
        private SandboxValidationService validationService;
        private SandboxProjectWorkspaceService workspaceService;

        private void Awake()
        {
            validationService = FindAnyObjectByType<SandboxValidationService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged += HandleIssuesChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (validationService != null)
            {
                validationService.ValidationIssuesChanged -= HandleIssuesChanged;
            }
        }

        public void Refresh()
        {
            ClearHighlights();
            var project = workspaceService?.ActiveProject;
            var issues = validationService?.Issues;
            if (project == null || issues == null)
            {
                return;
            }

            for (var i = 0; i < issues.Count; i += 1)
            {
                if (string.IsNullOrWhiteSpace(issues[i].floorId) || string.IsNullOrWhiteSpace(issues[i].objectId))
                {
                    continue;
                }

                RenderIssueHighlight(project, issues[i]);
            }
        }

        private void HandleIssuesChanged(IReadOnlyList<ValidationIssueData> nextIssues)
        {
            Refresh();
        }

        private void RenderIssueHighlight(BuildingProjectData project, ValidationIssueData issue)
        {
            var floor = project.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, issue.floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                return;
            }

            var color = issue.severity == ValidationIssueSeverity.BlockingError ? blockingColor : warningColor;

            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, issue.objectId, StringComparison.Ordinal));
            if (wall != null)
            {
                RenderLineHighlight($"IssueWall_{issue.issueId}", wall.startPoint, wall.endPoint, color);
                return;
            }

            var exitZone = floor.exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, issue.objectId, StringComparison.Ordinal));
            if (exitZone != null)
            {
                RenderRectHighlight($"IssueExit_{issue.issueId}", exitZone.center, exitZone.size, color);
                return;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, issue.objectId, StringComparison.Ordinal));
            if (obstacle != null)
            {
                RenderRectHighlight($"IssueObstacle_{issue.issueId}", obstacle.center, obstacle.size, color);
                return;
            }

            var stair = floor.stairPortals.FirstOrDefault(candidate => string.Equals(candidate.stairPortalId, issue.objectId, StringComparison.Ordinal));
            if (stair != null)
            {
                RenderCrossHighlight($"IssueStair_{issue.issueId}", stair.localPosition, color);
                return;
            }

            var door = floor.doors.FirstOrDefault(candidate => string.Equals(candidate.doorId, issue.objectId, StringComparison.Ordinal));
            if (door != null)
            {
                RenderOpeningHighlight($"IssueDoor_{issue.issueId}", door.wallSegmentId, door.offsetAlongWall, floor, color);
                return;
            }

            var window = floor.windows.FirstOrDefault(candidate => string.Equals(candidate.windowId, issue.objectId, StringComparison.Ordinal));
            if (window != null)
            {
                RenderOpeningHighlight($"IssueWindow_{issue.issueId}", window.wallSegmentId, window.offsetAlongWall, floor, color);
            }
        }

        private void RenderOpeningHighlight(string name, string wallSegmentId, float offsetAlongWall, FloorData floor, Color color)
        {
            var wall = floor.wallSegments.FirstOrDefault(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            var wallDirection = (wall.endPoint - wall.startPoint).normalized;
            var point = wall.startPoint + wallDirection * offsetAlongWall;
            RenderCrossHighlight(name, point, color);
        }

        private void RenderLineHighlight(string name, Vector2 start, Vector2 end, Color color)
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
            highlightObjects.Add(lineObject);
        }

        private void RenderRectHighlight(string name, Vector2 center, Vector2 size, Color color)
        {
            var rectObject = new GameObject(name);
            rectObject.transform.SetParent(transform, false);
            var lineRenderer = rectObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = 4;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;

            var half = size * 0.5f;
            lineRenderer.SetPosition(0, new Vector3(center.x - half.x, center.y - half.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(center.x - half.x, center.y + half.y, 0f));
            lineRenderer.SetPosition(2, new Vector3(center.x + half.x, center.y + half.y, 0f));
            lineRenderer.SetPosition(3, new Vector3(center.x + half.x, center.y - half.y, 0f));
            highlightObjects.Add(rectObject);
        }

        private void RenderCrossHighlight(string name, Vector2 center, Color color)
        {
            RenderLineHighlight($"{name}_A", center + new Vector2(-markerSize, -markerSize), center + new Vector2(markerSize, markerSize), color);
            RenderLineHighlight($"{name}_B", center + new Vector2(-markerSize, markerSize), center + new Vector2(markerSize, -markerSize), color);
        }

        private void ClearHighlights()
        {
            for (var i = 0; i < highlightObjects.Count; i += 1)
            {
                if (highlightObjects[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(highlightObjects[i]);
                    }
                    else
                    {
                        DestroyImmediate(highlightObjects[i]);
                    }
                }
            }

            highlightObjects.Clear();
        }
    }
}
