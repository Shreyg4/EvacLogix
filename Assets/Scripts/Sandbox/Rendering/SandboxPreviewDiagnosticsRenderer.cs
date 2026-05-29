using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Runtime;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxPreviewDiagnosticsRenderer : MonoBehaviour
    {
        [SerializeField] private Color fireOriginColor = new(1f, 0.25f, 0.2f, 0.95f);
        [SerializeField] private Color routeColor = new(0.95f, 0.9f, 0.2f, 0.9f);
        [SerializeField] private Color unreachableColor = new(0.95f, 0.25f, 0.25f, 0.95f);
        [SerializeField] private Color chokePointColor = new(1f, 0.55f, 0.1f, 0.95f);
        [SerializeField] private Color heatmapColor = new(1f, 0.7f, 0.15f, 0.55f);
        [SerializeField] private Color fireCellColor = new(1f, 0.4f, 0.1f, 0.6f);
        [SerializeField] private Color agentColor = new(0.35f, 0.95f, 0.65f, 0.95f);
        [SerializeField] private float lineWidth = 0.05f;
        [SerializeField] private float markerRadius = 0.22f;

        private readonly List<GameObject> renderedObjects = new();
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxFireSimulationService fireSimulationService;
        private SandboxAgentSimulationService agentSimulationService;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            fireSimulationService = FindAnyObjectByType<SandboxFireSimulationService>();
            agentSimulationService = FindAnyObjectByType<SandboxAgentSimulationService>();

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
            }

            if (previewService != null)
            {
                previewService.PreviewModeChanged += HandlePreviewModeChanged;
                previewService.PreviewStateChanged += HandlePreviewStateChanged;
                previewService.PreviewReportChanged += HandlePreviewReportChanged;
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

            if (previewService != null)
            {
                previewService.PreviewModeChanged -= HandlePreviewModeChanged;
                previewService.PreviewStateChanged -= HandlePreviewStateChanged;
                previewService.PreviewReportChanged -= HandlePreviewReportChanged;
            }
        }

        public void Refresh()
        {
            Clear();
            var project = workspaceService?.ActiveProject;
            var floor = workspaceService?.ActiveFloor;
            if (project == null || floor == null || previewService == null)
            {
                return;
            }

            if (previewService.IsPreviewModeActive)
            {
                foreach (var fireOrigin in project.fireOrigins.Where(origin => origin.floorId == floor.floorId))
                {
                    RenderCross($"FireOrigin_{fireOrigin.fireOriginId}", fireOrigin.position, fireOriginColor, markerRadius * 1.25f);
                    RenderCircle($"FireOriginHeat_{fireOrigin.fireOriginId}", fireOrigin.position, markerRadius * (1.1f + fireOrigin.spreadIntensity * 0.3f), fireOriginColor);
                }
            }

            if (fireSimulationService != null && fireSimulationService.SimulationActive)
            {
                foreach (var fireCell in fireSimulationService.ActiveFireCells.Where(cell => cell.floorId == floor.floorId))
                {
                    var radius = markerRadius * (0.75f + Mathf.Clamp01(fireCell.intensity) * 0.95f);
                    var alpha = Mathf.Lerp(0.15f, 0.85f, Mathf.Clamp01(fireCell.intensity));
                    RenderCircle(
                        $"FireCell_{fireCell.cellId}",
                        fireCell.position,
                        radius,
                        new Color(fireCellColor.r, fireCellColor.g, fireCellColor.b, alpha));
                }
            }

            if (agentSimulationService != null && agentSimulationService.SimulationActive)
            {
                foreach (var agent in agentSimulationService.ActiveAgents.Where(candidate => candidate != null && !candidate.HasExited && candidate.FloorId == floor.floorId))
                {
                    var position = (Vector2)agent.transform.position;
                    var urgency = Mathf.Clamp01(1f - agent.Health);
                    var radius = markerRadius * (0.6f + urgency * 0.35f);
                    var alpha = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(agent.Health));
                    RenderCross($"Agent_{agent.AgentId}", position, new Color(agentColor.r, agentColor.g, agentColor.b, alpha), radius);
                    RenderCircle($"AgentHalo_{agent.AgentId}", position, radius * 1.4f, new Color(agentColor.r, agentColor.g, agentColor.b, alpha * 0.35f));
                }
            }

            var report = previewService.LastPreviewReport;
            if (!report.didRun)
            {
                return;
            }

            foreach (var routeSegment in report.routeSegments.Where(segment => segment.floorId == floor.floorId))
            {
                RenderLine($"PreviewRoute_{routeSegment.segmentId}", routeSegment.start, routeSegment.end, routeColor, lineWidth + routeSegment.traversalCount * 0.01f);
            }

            foreach (var diagnostic in report.diagnostics.Where(diagnostic => diagnostic.floorId == floor.floorId && diagnostic.hasAnchorPoint))
            {
                switch (diagnostic.diagnosticKind)
                {
                    case SandboxPreviewDiagnosticKind.UnreachableArea:
                    case SandboxPreviewDiagnosticKind.BlockedArea:
                        RenderCross($"PreviewIssue_{diagnostic.diagnosticId}", diagnostic.anchorPoint, unreachableColor, markerRadius);
                        break;
                    case SandboxPreviewDiagnosticKind.ChokePoint:
                        RenderCircle($"PreviewChoke_{diagnostic.diagnosticId}", diagnostic.anchorPoint, markerRadius * 1.2f, chokePointColor);
                        break;
                    case SandboxPreviewDiagnosticKind.BlockedExit:
                        RenderDiamond($"PreviewBlockedExit_{diagnostic.diagnosticId}", diagnostic.anchorPoint, chokePointColor);
                        break;
                }
            }

            foreach (var heatPoint in report.heatPoints.Where(point => point.floorId == floor.floorId))
            {
                RenderCircle(
                    $"PreviewHeat_{heatPoint.pointId}",
                    heatPoint.position,
                    markerRadius * (1f + Mathf.Clamp(heatPoint.intensity, 0f, 5f) * 0.15f),
                    heatmapColor);
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

        private void HandlePreviewModeChanged(bool isPreviewModeActive)
        {
            Refresh();
        }

        private void HandlePreviewStateChanged()
        {
            Refresh();
        }

        private void HandlePreviewReportChanged(SandboxPreviewReportData report)
        {
            Refresh();
        }

        private void RenderLine(string name, Vector2 start, Vector2 end, Color color, float width)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, new Vector3(start.x, start.y, 0f));
            lineRenderer.SetPosition(1, new Vector3(end.x, end.y, 0f));
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = width;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            renderedObjects.Add(lineObject);
        }

        private void RenderCross(string name, Vector2 center, Color color, float size)
        {
            RenderLine($"{name}_A", center + new Vector2(-size, -size), center + new Vector2(size, size), color, lineWidth);
            RenderLine($"{name}_B", center + new Vector2(-size, size), center + new Vector2(size, -size), color, lineWidth);
        }

        private void RenderDiamond(string name, Vector2 center, Color color)
        {
            var points = new[]
            {
                center + new Vector2(0f, markerRadius),
                center + new Vector2(markerRadius, 0f),
                center + new Vector2(0f, -markerRadius),
                center + new Vector2(-markerRadius, 0f)
            };
            RenderPolyline(name, points, color);
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

            RenderPolyline(name, points, color);
        }

        private void RenderPolyline(string name, IReadOnlyList<Vector2> points, Color color)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = points.Count;
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

        private void Clear()
        {
            for (var i = 0; i < renderedObjects.Count; i += 1)
            {
                if (renderedObjects[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(renderedObjects[i]);
                }
                else
                {
                    DestroyImmediate(renderedObjects[i]);
                }
            }

            renderedObjects.Clear();
        }
    }
}
