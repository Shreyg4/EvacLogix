using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxPreviewInteractionMode
    {
        None = 0,
        PlaceFireOrigin = 1,
        PlaceSpawnPoint = 2,
        PaintSpawnBrush = 3,
        PlaceRegion = 4,
    }

    public enum SandboxPreviewDiagnosticKind
    {
        ValidationStatus = 0,
        MissingSetup = 1,
        BlockedArea = 2,
        BrokenStairLink = 3,
        BlockedExit = 4,
        UnreachableArea = 5,
        RoutePreview = 6,
        ChokePoint = 7,
        DoorState = 8,
        Info = 9,
    }

    [Serializable]
    public sealed class SandboxPreviewDiagnosticData
    {
        public string diagnosticId = string.Empty;
        public string floorId = string.Empty;
        public string objectId = string.Empty;
        public SandboxPreviewDiagnosticKind diagnosticKind;
        public ValidationIssueSeverity severity = ValidationIssueSeverity.Warning;
        public string title = string.Empty;
        public string message = string.Empty;
        public bool hasAnchorPoint;
        public Vector2 anchorPoint;
    }

    [Serializable]
    public sealed class SandboxPreviewRouteSegmentData
    {
        public string segmentId = string.Empty;
        public string floorId = string.Empty;
        public Vector2 start;
        public Vector2 end;
        public string label = string.Empty;
        public int traversalCount;
    }

    [Serializable]
    public sealed class SandboxPreviewHeatPointData
    {
        public string pointId = string.Empty;
        public string floorId = string.Empty;
        public Vector2 position;
        public float intensity;
        public string label = string.Empty;
    }

    [Serializable]
    public sealed class SandboxPreviewReportData
    {
        public bool didRun;
        public bool passed;
        public bool hasBlockingValidationIssues;
        public string summary = string.Empty;
        public int totalSpawnSamples;
        public int reachableSpawnSamples;
        public float estimatedRouteSuccess;
        public List<string> activeSpawnLayoutIds = new();
        public List<string> activeFireOriginIds = new();
        public List<SandboxPreviewDiagnosticData> diagnostics = new();
        public List<SandboxPreviewRouteSegmentData> routeSegments = new();
        public List<SandboxPreviewHeatPointData> heatPoints = new();
    }

    public sealed class SandboxPreviewService : MonoBehaviour
    {
        private sealed class PreviewSample
        {
            public string sampleId = string.Empty;
            public string floorId = string.Empty;
            public Vector2 position;
        }

        private sealed class PreviewNode
        {
            public string nodeId = string.Empty;
            public string floorId = string.Empty;
            public Vector2 position;
            public bool isExit;
            public bool isStairPortal;
            public bool isBlocked;
            public string objectId = string.Empty;
            public string linkedNodeId = string.Empty;
            public float travelCost;
        }

        [SerializeField] private bool isPreviewModeActive;
        [SerializeField] private SandboxPreviewInteractionMode interactionMode;
        [SerializeField] private string activeScenarioPresetId = string.Empty;
        [SerializeField] private string activeSpawnLayoutId = string.Empty;
        [SerializeField] private string pendingSpawnLayoutName = "Main Preview Layout";
        [SerializeField] private string pendingSpawnLayoutId = string.Empty;
        [SerializeField] private bool pendingSpawnLayoutIsPersistent = true;
        [SerializeField] private float pendingSpawnBrushDensity = 1f;
        [SerializeField] private string pendingRegionName = "Preview Region";
        [SerializeField] private RegionSemanticType pendingRegionSemanticType = RegionSemanticType.SpawnZone;
        [SerializeField] private bool pendingFireOriginIsPersistent = true;
        [SerializeField] private PreviewParameterData activePreviewParameters = new();
        [SerializeField] private SandboxPreviewReportData lastPreviewReport = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxValidationService validationService;
        private SandboxInputRouter inputRouter;

        public event Action<bool> PreviewModeChanged;
        public event Action PreviewStateChanged;
        public event Action<SandboxPreviewReportData> PreviewReportChanged;

        public bool IsPreviewModeActive => isPreviewModeActive;
        public SandboxPreviewInteractionMode InteractionMode => interactionMode;
        public string ActiveScenarioPresetId => activeScenarioPresetId;
        public string ActiveSpawnLayoutId => activeSpawnLayoutId;
        public string PendingSpawnLayoutName => pendingSpawnLayoutName;
        public string PendingSpawnLayoutId => pendingSpawnLayoutId;
        public bool PendingSpawnLayoutIsPersistent => pendingSpawnLayoutIsPersistent;
        public float PendingSpawnBrushDensity => pendingSpawnBrushDensity;
        public string PendingRegionName => pendingRegionName;
        public RegionSemanticType PendingRegionSemanticType => pendingRegionSemanticType;
        public bool PendingFireOriginIsPersistent => pendingFireOriginIsPersistent;
        public PreviewParameterData ActivePreviewParameters => activePreviewParameters;
        public SandboxPreviewReportData LastPreviewReport => lastPreviewReport;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            validationService = GetComponent<SandboxValidationService>();
            inputRouter = GetComponent<SandboxInputRouter>();
        }

        public bool EnterPreviewMode()
        {
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            if (!isPreviewModeActive)
            {
                isPreviewModeActive = true;
                inputRouter?.SetPreviewOverlayCapturingInput(true);
                PreviewModeChanged?.Invoke(true);
            }

            RaisePreviewStateChanged();
            return true;
        }

        public void ExitPreviewMode()
        {
            interactionMode = SandboxPreviewInteractionMode.None;
            if (!isPreviewModeActive)
            {
                RaisePreviewStateChanged();
                return;
            }

            isPreviewModeActive = false;
            inputRouter?.SetPreviewOverlayCapturingInput(false);
            PreviewModeChanged?.Invoke(false);
            RaisePreviewStateChanged();
        }

        public void SetInteractionMode(SandboxPreviewInteractionMode nextMode)
        {
            if (interactionMode == nextMode)
            {
                return;
            }

            interactionMode = nextMode;
            RaisePreviewStateChanged();
        }

        public void ClearInteractionMode()
        {
            SetInteractionMode(SandboxPreviewInteractionMode.None);
        }

        public void ConfigureSpawnPlacement(string layoutId, string layoutName, bool isPersistent)
        {
            pendingSpawnLayoutId = layoutId ?? string.Empty;
            pendingSpawnLayoutName = string.IsNullOrWhiteSpace(layoutName)
                ? (isPersistent ? "Main Preview Layout" : "Preview Temporary Layout")
                : layoutName.Trim();
            pendingSpawnLayoutIsPersistent = isPersistent;
            RaisePreviewStateChanged();
        }

        public void ConfigureSpawnBrush(float density, string layoutId, string layoutName, bool isPersistent)
        {
            pendingSpawnBrushDensity = Mathf.Max(0.1f, density);
            ConfigureSpawnPlacement(layoutId, layoutName, isPersistent);
        }

        public void ConfigureRegionPlacement(string regionName, RegionSemanticType semanticType)
        {
            pendingRegionName = string.IsNullOrWhiteSpace(regionName) ? "Preview Region" : regionName.Trim();
            pendingRegionSemanticType = semanticType;
            RaisePreviewStateChanged();
        }

        public void ConfigureFirePlacement(bool isPersistent)
        {
            pendingFireOriginIsPersistent = isPersistent;
            RaisePreviewStateChanged();
        }

        public void SetPreviewParameters(float spreadIntensity, float startDelaySeconds)
        {
            activePreviewParameters.spreadIntensity = Mathf.Max(0.1f, spreadIntensity);
            activePreviewParameters.startDelaySeconds = Mathf.Max(0f, startDelaySeconds);
            RaisePreviewStateChanged();
        }

        public bool SetActiveScenarioPreset(string scenarioPresetId)
        {
            var scenarioPreset = ResolveScenarioPreset(scenarioPresetId);
            if (scenarioPreset == null)
            {
                return false;
            }

            activeScenarioPresetId = scenarioPreset.scenarioPresetId;
            activePreviewParameters = ClonePreviewParameters(scenarioPreset.previewParameters);
            RaisePreviewStateChanged();
            return true;
        }

        public void ClearActiveScenarioPreset()
        {
            if (string.IsNullOrWhiteSpace(activeScenarioPresetId))
            {
                return;
            }

            activeScenarioPresetId = string.Empty;
            RaisePreviewStateChanged();
        }

        public void SetActiveSpawnLayout(string spawnLayoutId)
        {
            activeSpawnLayoutId = spawnLayoutId ?? string.Empty;
            RaisePreviewStateChanged();
        }

        public void NotifyPreviewInputsChanged()
        {
            RaisePreviewStateChanged();
        }

        public bool RunPreview()
        {
            lastPreviewReport = BuildPreviewReport();
            PreviewReportChanged?.Invoke(lastPreviewReport);
            return lastPreviewReport.didRun;
        }

        private SandboxPreviewReportData BuildPreviewReport()
        {
            var report = new SandboxPreviewReportData();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                report.summary = "Open a sandbox project before entering preview.";
                report.diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.MissingSetup,
                    ValidationIssueSeverity.BlockingError,
                    string.Empty,
                    string.Empty,
                    "Missing project",
                    report.summary));
                return report;
            }

            if (!isPreviewModeActive)
            {
                report.summary = "Enter preview mode before running preview.";
                report.diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.MissingSetup,
                    ValidationIssueSeverity.BlockingError,
                    string.Empty,
                    string.Empty,
                    "Preview mode required",
                    report.summary));
                return report;
            }

            var validationIssues = validationService?.ValidateActiveProject() ?? project.validationSnapshot?.issues ?? new List<ValidationIssueData>();
            report.hasBlockingValidationIssues = validationIssues.Any(issue => issue.severity == ValidationIssueSeverity.BlockingError);
            report.diagnostics.Add(CreateDiagnostic(
                SandboxPreviewDiagnosticKind.ValidationStatus,
                report.hasBlockingValidationIssues ? ValidationIssueSeverity.BlockingError : ValidationIssueSeverity.Warning,
                string.Empty,
                string.Empty,
                report.hasBlockingValidationIssues ? "Blocking validation issues" : "Validation ready",
                report.hasBlockingValidationIssues
                    ? "Preview cannot run until blocking validation issues are resolved."
                    : "Base validation is clear enough to run preview diagnostics."));

            if (report.hasBlockingValidationIssues)
            {
                report.summary = "Preview blocked by validation issues.";
                return report;
            }

            var scenarioPreset = ResolveScenarioPreset(activeScenarioPresetId);
            var activeParameters = scenarioPreset != null
                ? ClonePreviewParameters(scenarioPreset.previewParameters)
                : ClonePreviewParameters(activePreviewParameters);
            var spawnLayouts = ResolveActiveSpawnLayouts(project, scenarioPreset);
            report.activeSpawnLayoutIds = spawnLayouts.Select(layout => layout.spawnLayoutId).ToList();
            if (spawnLayouts.Count == 0)
            {
                report.summary = "Preview requires at least one authored spawn layout.";
                report.diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.MissingSetup,
                    ValidationIssueSeverity.BlockingError,
                    string.Empty,
                    string.Empty,
                    "Missing spawns",
                    report.summary));
                return report;
            }

            var fireOrigins = ResolveActiveFireOrigins(project, scenarioPreset);
            report.activeFireOriginIds = fireOrigins.Select(origin => origin.fireOriginId).ToList();
            if (fireOrigins.Count == 0)
            {
                report.summary = "Preview requires at least one authored fire origin.";
                report.diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.MissingSetup,
                    ValidationIssueSeverity.BlockingError,
                    string.Empty,
                    string.Empty,
                    "Missing fire origin",
                    report.summary));
                return report;
            }

            var spawnSamples = ExpandSpawnSamples(spawnLayouts);
            report.totalSpawnSamples = spawnSamples.Count;
            if (spawnSamples.Count == 0)
            {
                report.summary = "Selected spawn layouts do not contain any usable spawn samples.";
                report.diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.MissingSetup,
                    ValidationIssueSeverity.BlockingError,
                    string.Empty,
                    string.Empty,
                    "No usable spawn samples",
                    report.summary));
                return report;
            }

            var blockedExitIds = BuildBlockedExitLookup(project, report.diagnostics);
            var floorNodeLookup = BuildPreviewNodes(project, blockedExitIds);
            var routeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var routeSegments = new List<SandboxPreviewRouteSegmentData>();
            var heatPointWeights = new Dictionary<string, SandboxPreviewHeatPointData>(StringComparer.Ordinal);
            var hazardRadius = Mathf.Max(0.5f, activeParameters.spreadIntensity * (1.5f + activeParameters.startDelaySeconds * 0.25f));

            AddDoorStateDiagnostics(project, report.diagnostics);
            AddBrokenStairDiagnostics(validationIssues, report.diagnostics);

            for (var sampleIndex = 0; sampleIndex < spawnSamples.Count; sampleIndex += 1)
            {
                var sample = spawnSamples[sampleIndex];
                if (IsBlockedArea(project, sample.floorId, sample.position, fireOrigins, hazardRadius, out var blockedMessage))
                {
                    report.diagnostics.Add(CreateDiagnostic(
                        SandboxPreviewDiagnosticKind.BlockedArea,
                        ValidationIssueSeverity.Warning,
                        sample.floorId,
                        sample.sampleId,
                        "Blocked preview area",
                        blockedMessage,
                        sample.position));
                    continue;
                }

                if (!TryResolveRoute(project, floorNodeLookup, sample.floorId, sample.position, fireOrigins, hazardRadius, out var resolvedSegments, out var visitedNodeIds))
                {
                    report.diagnostics.Add(CreateDiagnostic(
                        SandboxPreviewDiagnosticKind.UnreachableArea,
                        ValidationIssueSeverity.Warning,
                        sample.floorId,
                        sample.sampleId,
                        "Unreachable preview area",
                        "This authored spawn cannot find a viable preview route to an exit.",
                        sample.position));
                    continue;
                }

                report.reachableSpawnSamples += 1;
                for (var segmentIndex = 0; segmentIndex < resolvedSegments.Count; segmentIndex += 1)
                {
                    routeSegments.Add(resolvedSegments[segmentIndex]);
                    AccumulateHeatPoint(heatPointWeights, resolvedSegments[segmentIndex].floorId, (resolvedSegments[segmentIndex].start + resolvedSegments[segmentIndex].end) * 0.5f, 1f, "Route");
                }

                for (var nodeIndex = 0; nodeIndex < visitedNodeIds.Count; nodeIndex += 1)
                {
                    if (!routeCounts.ContainsKey(visitedNodeIds[nodeIndex]))
                    {
                        routeCounts[visitedNodeIds[nodeIndex]] = 0;
                    }

                    routeCounts[visitedNodeIds[nodeIndex]] += 1;
                }
            }

            report.didRun = true;
            report.estimatedRouteSuccess = report.totalSpawnSamples <= 0
                ? 0f
                : (float)report.reachableSpawnSamples / report.totalSpawnSamples;
            report.routeSegments = CoalesceSegments(routeSegments);
            report.heatPoints = heatPointWeights.Values.OrderByDescending(point => point.intensity).ToList();
            AddChokePointDiagnostics(floorNodeLookup, routeCounts, report, heatPointWeights);
            report.heatPoints = heatPointWeights.Values.OrderByDescending(point => point.intensity).ToList();
            report.passed = report.estimatedRouteSuccess >= 0.999f &&
                            !report.diagnostics.Any(diagnostic =>
                                diagnostic.severity == ValidationIssueSeverity.BlockingError &&
                                diagnostic.diagnosticKind != SandboxPreviewDiagnosticKind.ValidationStatus);
            report.summary = report.passed
                ? $"Preview ready. {report.reachableSpawnSamples} of {report.totalSpawnSamples} preview spawns can reach an exit."
                : $"Preview found actionable issues. {report.reachableSpawnSamples} of {report.totalSpawnSamples} preview spawns can reach an exit.";
            return report;
        }

        private ScenarioPresetData ResolveScenarioPreset(string scenarioPresetId)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(scenarioPresetId))
            {
                return null;
            }

            return workspaceService.ActiveProject.scenarioPresets.FirstOrDefault(candidate =>
                string.Equals(candidate.scenarioPresetId, scenarioPresetId, StringComparison.Ordinal));
        }

        private List<SpawnLayoutData> ResolveActiveSpawnLayouts(BuildingProjectData project, ScenarioPresetData scenarioPreset)
        {
            if (scenarioPreset != null)
            {
                return project.spawnLayouts
                    .Where(layout => scenarioPreset.spawnLayoutIds.Contains(layout.spawnLayoutId))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(activeSpawnLayoutId))
            {
                return project.spawnLayouts
                    .Where(layout => string.Equals(layout.spawnLayoutId, activeSpawnLayoutId, StringComparison.Ordinal))
                    .ToList();
            }

            return project.spawnLayouts
                .Where(layout => layout.spawnPoints.Count > 0 || layout.spawnBrushStrokes.Count > 0)
                .ToList();
        }

        private static List<FireOriginData> ResolveActiveFireOrigins(BuildingProjectData project, ScenarioPresetData scenarioPreset)
        {
            if (scenarioPreset != null)
            {
                return project.fireOrigins
                    .Where(origin => scenarioPreset.fireOriginIds.Contains(origin.fireOriginId))
                    .ToList();
            }

            return project.fireOrigins.ToList();
        }

        private static List<PreviewSample> ExpandSpawnSamples(IReadOnlyList<SpawnLayoutData> spawnLayouts)
        {
            var samples = new List<PreviewSample>();
            for (var layoutIndex = 0; layoutIndex < spawnLayouts.Count; layoutIndex += 1)
            {
                var layout = spawnLayouts[layoutIndex];
                for (var pointIndex = 0; pointIndex < layout.spawnPoints.Count; pointIndex += 1)
                {
                    samples.Add(new PreviewSample
                    {
                        sampleId = layout.spawnPoints[pointIndex].spawnPointId,
                        floorId = layout.spawnPoints[pointIndex].floorId,
                        position = layout.spawnPoints[pointIndex].position
                    });
                }

                for (var strokeIndex = 0; strokeIndex < layout.spawnBrushStrokes.Count; strokeIndex += 1)
                {
                    var stroke = layout.spawnBrushStrokes[strokeIndex];
                    var generatedPoints = GenerateSpawnBrushSamples(stroke);
                    for (var sampleIndex = 0; sampleIndex < generatedPoints.Count; sampleIndex += 1)
                    {
                        samples.Add(new PreviewSample
                        {
                            sampleId = $"{stroke.spawnBrushStrokeId}-sample-{sampleIndex:D3}",
                            floorId = stroke.floorId,
                            position = generatedPoints[sampleIndex]
                        });
                    }
                }
            }

            return samples;
        }

        private static List<Vector2> GenerateSpawnBrushSamples(SpawnBrushStrokeData stroke)
        {
            var samples = new List<Vector2>();
            if (stroke == null || stroke.polygonPoints == null || stroke.polygonPoints.Count < 3)
            {
                return samples;
            }

            var minX = stroke.polygonPoints.Min(point => point.x);
            var minY = stroke.polygonPoints.Min(point => point.y);
            var maxX = stroke.polygonPoints.Max(point => point.x);
            var maxY = stroke.polygonPoints.Max(point => point.y);
            var spacing = 1f / Mathf.Sqrt(Mathf.Max(0.1f, stroke.density));

            for (var x = minX; x <= maxX + spacing * 0.5f; x += spacing)
            {
                for (var y = minY; y <= maxY + spacing * 0.5f; y += spacing)
                {
                    var point = new Vector2(x, y);
                    if (PointInPolygon(point, stroke.polygonPoints))
                    {
                        samples.Add(point);
                    }
                }
            }

            if (samples.Count == 0)
            {
                var centroid = Vector2.zero;
                for (var i = 0; i < stroke.polygonPoints.Count; i += 1)
                {
                    centroid += stroke.polygonPoints[i];
                }

                samples.Add(centroid / stroke.polygonPoints.Count);
            }

            return samples;
        }

        private static HashSet<string> BuildBlockedExitLookup(BuildingProjectData project, ICollection<SandboxPreviewDiagnosticData> diagnostics)
        {
            var blockedExitIds = new HashSet<string>(StringComparer.Ordinal);
            for (var floorIndex = 0; floorIndex < project.floors.Count; floorIndex += 1)
            {
                var floor = project.floors[floorIndex];
                for (var exitIndex = 0; exitIndex < floor.exits.Count; exitIndex += 1)
                {
                    var exitZone = floor.exits[exitIndex];
                    if (!floor.obstacles.Any(obstacle =>
                            obstacle.semanticType == ObstacleSemanticType.HardBlocking &&
                            RectsOverlap(obstacle.center, obstacle.size, exitZone.center, exitZone.size)))
                    {
                        continue;
                    }

                    blockedExitIds.Add(exitZone.exitZoneId);
                    diagnostics.Add(CreateDiagnostic(
                        SandboxPreviewDiagnosticKind.BlockedExit,
                        ValidationIssueSeverity.Warning,
                        floor.floorId,
                        exitZone.exitZoneId,
                        "Blocked exit",
                        "A hard-blocking obstacle overlaps this exit zone and will distort preview routes.",
                        exitZone.center));
                }
            }

            return blockedExitIds;
        }

        private static Dictionary<string, List<PreviewNode>> BuildPreviewNodes(BuildingProjectData project, HashSet<string> blockedExitIds)
        {
            var floorNodeLookup = new Dictionary<string, List<PreviewNode>>(StringComparer.Ordinal);
            for (var floorIndex = 0; floorIndex < project.floors.Count; floorIndex += 1)
            {
                var floor = project.floors[floorIndex];
                if (!floorNodeLookup.TryGetValue(floor.floorId, out var nodes))
                {
                    nodes = new List<PreviewNode>();
                    floorNodeLookup[floor.floorId] = nodes;
                }

                for (var exitIndex = 0; exitIndex < floor.exits.Count; exitIndex += 1)
                {
                    var exitZone = floor.exits[exitIndex];
                    nodes.Add(new PreviewNode
                    {
                        nodeId = $"exit:{exitZone.exitZoneId}",
                        floorId = floor.floorId,
                        position = exitZone.center,
                        isExit = true,
                        objectId = exitZone.exitZoneId,
                        isBlocked = blockedExitIds.Contains(exitZone.exitZoneId)
                    });
                }

                for (var stairIndex = 0; stairIndex < floor.stairPortals.Count; stairIndex += 1)
                {
                    var stairPortal = floor.stairPortals[stairIndex];
                    nodes.Add(new PreviewNode
                    {
                        nodeId = $"stair:{stairPortal.stairPortalId}",
                        floorId = floor.floorId,
                        position = stairPortal.localPosition,
                        isStairPortal = true,
                        objectId = stairPortal.stairPortalId,
                        linkedNodeId = string.IsNullOrWhiteSpace(stairPortal.targetStairPortalId)
                            ? string.Empty
                            : $"stair:{stairPortal.targetStairPortalId}",
                        travelCost = Mathf.Max(0.1f, stairPortal.travelCost),
                        isBlocked = false
                    });
                }
            }

            return floorNodeLookup;
        }

        private static void AddDoorStateDiagnostics(BuildingProjectData project, ICollection<SandboxPreviewDiagnosticData> diagnostics)
        {
            for (var floorIndex = 0; floorIndex < project.floors.Count; floorIndex += 1)
            {
                var floor = project.floors[floorIndex];
                var blockedDoorCount = floor.doors.Count(door => door.state == DoorState.Blocked || door.state == DoorState.Locked);
                if (blockedDoorCount <= 0)
                {
                    continue;
                }

                diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.DoorState,
                    ValidationIssueSeverity.Warning,
                    floor.floorId,
                    string.Empty,
                    "Constrained door states",
                    $"{blockedDoorCount} door(s) are marked blocked or locked on this floor and may create preview choke points."));
            }
        }

        private static void AddBrokenStairDiagnostics(
            IReadOnlyList<ValidationIssueData> validationIssues,
            ICollection<SandboxPreviewDiagnosticData> diagnostics)
        {
            foreach (var issue in validationIssues.Where(issue =>
                         issue.title.Contains("stair", StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(CreateDiagnostic(
                    SandboxPreviewDiagnosticKind.BrokenStairLink,
                    issue.severity,
                    issue.floorId,
                    issue.objectId,
                    issue.title,
                    issue.message));
            }
        }

        private static bool TryResolveRoute(
            BuildingProjectData project,
            IReadOnlyDictionary<string, List<PreviewNode>> floorNodeLookup,
            string startFloorId,
            Vector2 startPosition,
            IReadOnlyList<FireOriginData> fireOrigins,
            float hazardRadius,
            out List<SandboxPreviewRouteSegmentData> routeSegments,
            out List<string> visitedNodeIds)
        {
            routeSegments = new List<SandboxPreviewRouteSegmentData>();
            visitedNodeIds = new List<string>();
            if (!floorNodeLookup.TryGetValue(startFloorId, out var startNodes) || startNodes.Count == 0)
            {
                return false;
            }

            var allNodes = floorNodeLookup.Values.SelectMany(nodes => nodes).ToList();
            var distances = new Dictionary<string, float>(StringComparer.Ordinal);
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var pending = new List<string>();
            var startNodeId = "__start__";
            distances[startNodeId] = 0f;
            pending.Add(startNodeId);

            for (var nodeIndex = 0; nodeIndex < allNodes.Count; nodeIndex += 1)
            {
                distances[allNodes[nodeIndex].nodeId] = float.PositiveInfinity;
                pending.Add(allNodes[nodeIndex].nodeId);
            }

            while (pending.Count > 0)
            {
                pending.Sort((left, right) => distances[left].CompareTo(distances[right]));
                var currentNodeId = pending[0];
                pending.RemoveAt(0);

                if (float.IsPositiveInfinity(distances[currentNodeId]))
                {
                    break;
                }

                if (TryResolveNode(currentNodeId, startNodeId, startFloorId, startPosition, allNodes, out var currentNode) &&
                    currentNode != null &&
                    currentNode.isExit &&
                    !currentNode.isBlocked)
                {
                    BuildRouteSegments(previous, currentNodeId, startNodeId, startFloorId, startPosition, allNodes, out routeSegments, out visitedNodeIds);
                    return routeSegments.Count > 0;
                }

                foreach (var edge in EnumerateEdges(project, floorNodeLookup, startNodeId, startFloorId, startPosition, currentNodeId, allNodes, fireOrigins, hazardRadius))
                {
                    var nextDistance = distances[currentNodeId] + edge.cost;
                    if (nextDistance >= distances[edge.targetNodeId])
                    {
                        continue;
                    }

                    distances[edge.targetNodeId] = nextDistance;
                    previous[edge.targetNodeId] = currentNodeId;
                }
            }

            return false;
        }

        private static IEnumerable<(string targetNodeId, float cost)> EnumerateEdges(
            BuildingProjectData project,
            IReadOnlyDictionary<string, List<PreviewNode>> floorNodeLookup,
            string startNodeId,
            string startFloorId,
            Vector2 startPosition,
            string currentNodeId,
            IReadOnlyList<PreviewNode> allNodes,
            IReadOnlyList<FireOriginData> fireOrigins,
            float hazardRadius)
        {
            if (string.Equals(currentNodeId, startNodeId, StringComparison.Ordinal))
            {
                if (!floorNodeLookup.TryGetValue(startFloorId, out var startFloorNodes))
                {
                    yield break;
                }

                for (var i = 0; i < startFloorNodes.Count; i += 1)
                {
                    var targetNode = startFloorNodes[i];
                    if (targetNode.isBlocked || IsBlockedSegment(project, startFloorId, startPosition, targetNode.position, fireOrigins, hazardRadius))
                    {
                        continue;
                    }

                    yield return (targetNode.nodeId, Vector2.Distance(startPosition, targetNode.position));
                }

                yield break;
            }

            var currentNode = allNodes.FirstOrDefault(node => string.Equals(node.nodeId, currentNodeId, StringComparison.Ordinal));
            if (currentNode == null || currentNode.isBlocked)
            {
                yield break;
            }

            if (floorNodeLookup.TryGetValue(currentNode.floorId, out var floorNodes))
            {
                for (var i = 0; i < floorNodes.Count; i += 1)
                {
                    var targetNode = floorNodes[i];
                    if (targetNode == currentNode || targetNode.isBlocked)
                    {
                        continue;
                    }

                    if (IsBlockedSegment(project, currentNode.floorId, currentNode.position, targetNode.position, fireOrigins, hazardRadius))
                    {
                        continue;
                    }

                    yield return (targetNode.nodeId, Vector2.Distance(currentNode.position, targetNode.position));
                }
            }

            if (currentNode.isStairPortal && !string.IsNullOrWhiteSpace(currentNode.linkedNodeId))
            {
                var targetNode = allNodes.FirstOrDefault(node => string.Equals(node.nodeId, currentNode.linkedNodeId, StringComparison.Ordinal));
                if (targetNode != null && !targetNode.isBlocked)
                {
                    yield return (targetNode.nodeId, currentNode.travelCost);
                }
            }
        }

        private static bool TryResolveNode(
            string nodeId,
            string startNodeId,
            string startFloorId,
            Vector2 startPosition,
            IReadOnlyList<PreviewNode> allNodes,
            out PreviewNode previewNode)
        {
            if (string.Equals(nodeId, startNodeId, StringComparison.Ordinal))
            {
                previewNode = new PreviewNode
                {
                    nodeId = startNodeId,
                    floorId = startFloorId,
                    position = startPosition
                };
                return true;
            }

            previewNode = allNodes.FirstOrDefault(node => string.Equals(node.nodeId, nodeId, StringComparison.Ordinal));
            return previewNode != null;
        }

        private static void BuildRouteSegments(
            IReadOnlyDictionary<string, string> previous,
            string finalNodeId,
            string startNodeId,
            string startFloorId,
            Vector2 startPosition,
            IReadOnlyList<PreviewNode> allNodes,
            out List<SandboxPreviewRouteSegmentData> routeSegments,
            out List<string> visitedNodeIds)
        {
            routeSegments = new List<SandboxPreviewRouteSegmentData>();
            visitedNodeIds = new List<string>();
            var orderedNodeIds = new List<string> { finalNodeId };
            var currentNodeId = finalNodeId;
            while (previous.TryGetValue(currentNodeId, out var previousNodeId))
            {
                orderedNodeIds.Add(previousNodeId);
                currentNodeId = previousNodeId;
            }

            orderedNodeIds.Reverse();
            visitedNodeIds.AddRange(orderedNodeIds.Where(id => !string.Equals(id, startNodeId, StringComparison.Ordinal)));

            for (var i = 1; i < orderedNodeIds.Count; i += 1)
            {
                var leftNode = ResolveNodeForRoute(orderedNodeIds[i - 1], startNodeId, startFloorId, startPosition, allNodes);
                var rightNode = ResolveNodeForRoute(orderedNodeIds[i], startNodeId, startFloorId, startPosition, allNodes);
                if (leftNode == null || rightNode == null || !string.Equals(leftNode.floorId, rightNode.floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                routeSegments.Add(new SandboxPreviewRouteSegmentData
                {
                    segmentId = $"route-{i:D3}-{leftNode.nodeId}-{rightNode.nodeId}",
                    floorId = leftNode.floorId,
                    start = leftNode.position,
                    end = rightNode.position,
                    label = rightNode.isExit ? "Exit Route" : (rightNode.isStairPortal ? "Stair Transition" : "Route"),
                    traversalCount = 1
                });
            }
        }

        private static PreviewNode ResolveNodeForRoute(
            string nodeId,
            string startNodeId,
            string startFloorId,
            Vector2 startPosition,
            IReadOnlyList<PreviewNode> allNodes)
        {
            if (string.Equals(nodeId, startNodeId, StringComparison.Ordinal))
            {
                return new PreviewNode
                {
                    nodeId = startNodeId,
                    floorId = startFloorId,
                    position = startPosition
                };
            }

            return allNodes.FirstOrDefault(node => string.Equals(node.nodeId, nodeId, StringComparison.Ordinal));
        }

        private static List<SandboxPreviewRouteSegmentData> CoalesceSegments(IReadOnlyList<SandboxPreviewRouteSegmentData> rawSegments)
        {
            return rawSegments
                .GroupBy(segment => $"{segment.floorId}|{segment.start}|{segment.end}|{segment.label}", StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    first.traversalCount = group.Sum(segment => Mathf.Max(1, segment.traversalCount));
                    return first;
                })
                .OrderByDescending(segment => segment.traversalCount)
                .ThenBy(segment => segment.floorId, StringComparer.Ordinal)
                .ToList();
        }

        private static void AddChokePointDiagnostics(
            IReadOnlyDictionary<string, List<PreviewNode>> floorNodeLookup,
            IReadOnlyDictionary<string, int> routeCounts,
            SandboxPreviewReportData report,
            IDictionary<string, SandboxPreviewHeatPointData> heatPointWeights)
        {
            foreach (var floorNodes in floorNodeLookup.Values)
            {
                foreach (var node in floorNodes)
                {
                    if (!routeCounts.TryGetValue(node.nodeId, out var usageCount) || usageCount < 2)
                    {
                        continue;
                    }

                    report.diagnostics.Add(CreateDiagnostic(
                        SandboxPreviewDiagnosticKind.ChokePoint,
                        ValidationIssueSeverity.Warning,
                        node.floorId,
                        node.objectId,
                        "Potential choke point",
                        $"{usageCount} preview routes converge on this {(node.isExit ? "exit" : "stair")} node.",
                        node.position));
                    AccumulateHeatPoint(heatPointWeights, node.floorId, node.position, usageCount, "Choke");
                }
            }
        }

        private static void AccumulateHeatPoint(
            IDictionary<string, SandboxPreviewHeatPointData> heatPointWeights,
            string floorId,
            Vector2 position,
            float intensity,
            string label)
        {
            var key = $"{floorId}:{Mathf.Round(position.x * 10f) / 10f:0.0}:{Mathf.Round(position.y * 10f) / 10f:0.0}:{label}";
            if (!heatPointWeights.TryGetValue(key, out var heatPoint))
            {
                heatPoint = new SandboxPreviewHeatPointData
                {
                    pointId = key,
                    floorId = floorId,
                    position = position,
                    label = label
                };
                heatPointWeights[key] = heatPoint;
            }

            heatPoint.intensity += intensity;
        }

        private static bool IsBlockedArea(
            BuildingProjectData project,
            string floorId,
            Vector2 position,
            IReadOnlyList<FireOriginData> fireOrigins,
            float hazardRadius,
            out string blockedMessage)
        {
            blockedMessage = string.Empty;
            var floor = project.floors.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                blockedMessage = "Preview sample references a floor that no longer exists.";
                return true;
            }

            if (floor.obstacles.Any(obstacle =>
                    obstacle.semanticType == ObstacleSemanticType.HardBlocking &&
                    PointInsideRect(position, obstacle.center, obstacle.size)))
            {
                blockedMessage = "Preview sample starts inside a hard-blocking obstacle.";
                return true;
            }

            if (floor.regions.Any(region =>
                    region.semanticType == RegionSemanticType.RestrictedZone &&
                    PointInPolygon(position, region.polygonPoints)))
            {
                blockedMessage = "Preview sample starts inside a restricted region.";
                return true;
            }

            if (fireOrigins.Any(origin =>
                    string.Equals(origin.floorId, floorId, StringComparison.Ordinal) &&
                    Vector2.Distance(position, origin.position) <= hazardRadius))
            {
                blockedMessage = "Preview sample starts inside the active fire hazard radius.";
                return true;
            }

            return false;
        }

        private static bool IsBlockedSegment(
            BuildingProjectData project,
            string floorId,
            Vector2 start,
            Vector2 end,
            IReadOnlyList<FireOriginData> fireOrigins,
            float hazardRadius)
        {
            var floor = project.floors.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                return true;
            }

            for (var obstacleIndex = 0; obstacleIndex < floor.obstacles.Count; obstacleIndex += 1)
            {
                var obstacle = floor.obstacles[obstacleIndex];
                if (obstacle.semanticType != ObstacleSemanticType.HardBlocking)
                {
                    continue;
                }

                if (SegmentIntersectsRect(start, end, obstacle.center, obstacle.size))
                {
                    return true;
                }
            }

            for (var regionIndex = 0; regionIndex < floor.regions.Count; regionIndex += 1)
            {
                var region = floor.regions[regionIndex];
                if (region.semanticType != RegionSemanticType.RestrictedZone)
                {
                    continue;
                }

                if (PointInPolygon((start + end) * 0.5f, region.polygonPoints))
                {
                    return true;
                }
            }

            for (var fireIndex = 0; fireIndex < fireOrigins.Count; fireIndex += 1)
            {
                var fireOrigin = fireOrigins[fireIndex];
                if (!string.Equals(fireOrigin.floorId, floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (SegmentIntersectsCircle(start, end, fireOrigin.position, hazardRadius))
                {
                    return true;
                }
            }

            return false;
        }

        private static SandboxPreviewDiagnosticData CreateDiagnostic(
            SandboxPreviewDiagnosticKind diagnosticKind,
            ValidationIssueSeverity severity,
            string floorId,
            string objectId,
            string title,
            string message,
            Vector2? anchorPoint = null)
        {
            return new SandboxPreviewDiagnosticData
            {
                diagnosticId = SandboxId.NewId(),
                floorId = floorId ?? string.Empty,
                objectId = objectId ?? string.Empty,
                diagnosticKind = diagnosticKind,
                severity = severity,
                title = title ?? string.Empty,
                message = message ?? string.Empty,
                hasAnchorPoint = anchorPoint.HasValue,
                anchorPoint = anchorPoint ?? Vector2.zero
            };
        }

        private static PreviewParameterData ClonePreviewParameters(PreviewParameterData source)
        {
            return source == null
                ? new PreviewParameterData()
                : new PreviewParameterData
                {
                    spreadIntensity = Mathf.Max(0.1f, source.spreadIntensity),
                    startDelaySeconds = Mathf.Max(0f, source.startDelaySeconds),
                    previewAgentCap = Mathf.Max(1, source.previewAgentCap)
                };
        }

        private static bool PointInsideRect(Vector2 point, Vector2 center, Vector2 size)
        {
            var half = size * 0.5f;
            return point.x >= center.x - half.x &&
                   point.x <= center.x + half.x &&
                   point.y >= center.y - half.y &&
                   point.y <= center.y + half.y;
        }

        private static bool RectsOverlap(Vector2 leftCenter, Vector2 leftSize, Vector2 rightCenter, Vector2 rightSize)
        {
            var leftHalf = leftSize * 0.5f;
            var rightHalf = rightSize * 0.5f;
            return Mathf.Abs(leftCenter.x - rightCenter.x) <= leftHalf.x + rightHalf.x &&
                   Mathf.Abs(leftCenter.y - rightCenter.y) <= leftHalf.y + rightHalf.y;
        }

        private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Vector2 rectCenter, Vector2 rectSize)
        {
            if (PointInsideRect(start, rectCenter, rectSize) || PointInsideRect(end, rectCenter, rectSize))
            {
                return true;
            }

            var half = rectSize * 0.5f;
            var topLeft = rectCenter + new Vector2(-half.x, half.y);
            var topRight = rectCenter + new Vector2(half.x, half.y);
            var bottomLeft = rectCenter + new Vector2(-half.x, -half.y);
            var bottomRight = rectCenter + new Vector2(half.x, -half.y);

            return SegmentsIntersect(start, end, topLeft, topRight) ||
                   SegmentsIntersect(start, end, topRight, bottomRight) ||
                   SegmentsIntersect(start, end, bottomRight, bottomLeft) ||
                   SegmentsIntersect(start, end, bottomLeft, topLeft);
        }

        private static bool SegmentIntersectsCircle(Vector2 start, Vector2 end, Vector2 center, float radius)
        {
            var line = end - start;
            var lineLengthSquared = line.sqrMagnitude;
            if (lineLengthSquared <= Mathf.Epsilon)
            {
                return Vector2.Distance(start, center) <= radius;
            }

            var t = Mathf.Clamp01(Vector2.Dot(center - start, line) / lineLengthSquared);
            var closestPoint = start + line * t;
            return Vector2.Distance(closestPoint, center) <= radius;
        }

        private static bool SegmentsIntersect(Vector2 aStart, Vector2 aEnd, Vector2 bStart, Vector2 bEnd)
        {
            var a = aEnd - aStart;
            var b = bEnd - bStart;
            var denominator = a.x * b.y - a.y * b.x;
            if (Mathf.Abs(denominator) <= Mathf.Epsilon)
            {
                return false;
            }

            var difference = bStart - aStart;
            var t = (difference.x * b.y - difference.y * b.x) / denominator;
            var u = (difference.x * a.y - difference.y * a.x) / denominator;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return false;
            }

            var inside = false;
            for (var i = 0; i < polygon.Count; i += 1)
            {
                var j = (i + polygon.Count - 1) % polygon.Count;
                var intersects = ((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                                 (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / Mathf.Max(0.0001f, polygon[j].y - polygon[i].y) + polygon[i].x);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private void RaisePreviewStateChanged()
        {
            PreviewStateChanged?.Invoke();
        }
    }
}
