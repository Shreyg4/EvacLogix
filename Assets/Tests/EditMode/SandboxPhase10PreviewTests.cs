using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase10PreviewTests
    {
        [Test]
        public void PreviewMode_UsesExplicitEnterExitAndBlocksNormalAuthoring()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out var inputRouter,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out _,
                out _,
                out _,
                out _);

            var statusBarObject = new GameObject("StatusBar");
            var statusBar = statusBarObject.AddComponent<SandboxStatusBarShell>();
            statusBar.SendMessage("Awake");

            var topBarObject = new GameObject("TopBar");
            var topBar = topBarObject.AddComponent<SandboxTopBarShell>();
            topBar.SendMessage("Awake");

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);
            Assert.That(topBar.EnterPreviewMode(), Is.True);
            Assert.That(topBar.IsPreviewModeActive, Is.True);
            Assert.That(inputRouter.ResolvePointerTarget(Vector2.zero), Is.EqualTo(SandboxInputTarget.PreviewOverlay));

            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 1f), new Vector2(5f, 1f), 0.25f), Is.False);
            Assert.That(semanticObjectAuthoringService.PlaceObstacle(new Vector2(2f, 2f), out _), Is.False);

            topBar.ExitPreviewMode();
            Assert.That(topBar.IsPreviewModeActive, Is.False);
            Assert.That(inputRouter.ResolvePointerTarget(Vector2.zero), Is.EqualTo(SandboxInputTarget.World));
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 1f), new Vector2(5f, 1f), 0.25f), Is.True);

            Object.DestroyImmediate(topBarObject);
            Object.DestroyImmediate(statusBarObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void PreviewAuthoring_CreatesFireOriginsLayoutsSpawnPointBrushesAndNamedRegions()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out _,
                out var previewAuthoringService,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            CreateEnclosedRoom(wallAuthoringService);

            Assert.That(previewAuthoringService.PlaceFireOrigin(new Vector2(3f, 3f), out var fireOriginId, 1.5f, 2f, true), Is.True);
            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out var spawnPointId, out var persistentLayoutId, null, "Saved Layout", true), Is.True);
            Assert.That(previewAuthoringService.PlaceSpawnPointBrush(
                new[] { new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(2f, 2f), new Vector2(2f, 0f) },
                out var brushedSpawnPointIds,
                out var temporaryLayoutId,
                2f,
                null,
                "Temporary Layout",
                false), Is.True);
            Assert.That(previewAuthoringService.PlaceRegion(new Vector2(4f, 4f), new Vector2(2f, 3f), out var regionId, "North Spawn Zone", RegionSemanticType.SpawnZone), Is.True);

            var project = workspaceService.ActiveProject;
            Assert.That(project.fireOrigins.Single(origin => origin.fireOriginId == fireOriginId).isPersistent, Is.True);
            Assert.That(project.spawnLayouts.Single(layout => layout.spawnLayoutId == persistentLayoutId).isPersistent, Is.True);
            Assert.That(project.spawnLayouts.Single(layout => layout.spawnLayoutId == temporaryLayoutId).isPersistent, Is.False);
            Assert.That(project.spawnLayouts.SelectMany(layout => layout.spawnPoints).Any(point => point.spawnPointId == spawnPointId), Is.True);
            Assert.That(project.spawnLayouts.SelectMany(layout => layout.spawnPoints).Count(point => brushedSpawnPointIds.Contains(point.spawnPointId)), Is.EqualTo(brushedSpawnPointIds.Count));
            Assert.That(project.floors.Single().regions.Single(region => region.regionId == regionId).name, Is.EqualTo("North Spawn Zone"));
            Assert.That(project.floors.Single().regions.Single(region => region.regionId == regionId).semanticType, Is.EqualTo(RegionSemanticType.SpawnZone));

            var semanticRendererObject = new GameObject("SemanticRenderer");
            var semanticRenderer = semanticRendererObject.AddComponent<SandboxSemanticObjectRenderer>();
            semanticRenderer.SendMessage("Awake");
            Assert.That(semanticRendererObject.transform.Cast<Transform>().Any(child => child.name.StartsWith($"SpawnPoint_{spawnPointId}", System.StringComparison.Ordinal)), Is.True);

            Object.DestroyImmediate(semanticRendererObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void PreviewAuthoring_ReportsDistinctSpawnPlacementFailures()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out _,
                out var previewAuthoringService,
                out _,
                out _,
                out _);
            var validationService = host.GetComponent<SandboxValidationService>();

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out _, out var pointFailure, null, "Layout", true), Is.False);
            Assert.That(pointFailure, Is.EqualTo("Spawn points must be placed inside a detected room."));
            Assert.That(validationService.Issues.Any(issue => issue.message == pointFailure && issue.issueType == ValidationIssueType.Preview), Is.True);
            Assert.That(previewAuthoringService.PlaceSpawnPointBrush(
                new[] { new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(2f, 2f), new Vector2(2f, 0f) },
                out _,
                out _,
                out var brushFailure,
                1f,
                null,
                "Layout",
                false), Is.False);
            Assert.That(brushFailure, Is.EqualTo("Spawn point brushes must stay inside detected rooms."));
            Assert.That(validationService.Issues.Any(issue => issue.message == brushFailure && issue.issueType == ValidationIssueType.Preview), Is.True);

            CreateEnclosedRoom(wallAuthoringService);

            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out _, out var pointNoAccessFailure, null, "Layout", true), Is.False);
            Assert.That(pointNoAccessFailure, Is.EqualTo("Spawn points require at least one exit or window on the floor."));
            Assert.That(previewAuthoringService.PlaceSpawnPointBrush(
                new[] { new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(2f, 2f), new Vector2(2f, 0f) },
                out _,
                out _,
                out var brushNoAccessFailure,
                1f,
                null,
                "Layout",
                false), Is.False);
            Assert.That(brushNoAccessFailure, Is.EqualTo("Spawn point brushes require at least one exit or window on the floor."));

            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(1f, 1f), out _, new Vector2(1.5f, 1.5f), 0f, 1.5f, 50f, 1f, "Lobby Exit"), Is.True);

            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out _, out _, null, "Layout", true), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ScenarioPreview_RunProducesRoutesHeatmapAndCrossFloorDiagnostics()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out var previewAuthoringService,
                out var previewService,
                out var scenarioManagementService,
                out var floorManagementService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var firstFloorId = workspaceService.ActiveFloor.floorId;
            Assert.That(floorManagementService.AddFloor(out var secondFloorId, "Level 2", 3f), Is.True);

            workspaceService.SetActiveFloor(firstFloorId);
            CreateEnclosedRoom(wallAuthoringService);
            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(0f, 0f), out _, out var spawnLayoutId, null, "Evac Layout", true), Is.True);
            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(0f, 1f), out _, out _, spawnLayoutId, "Evac Layout", true), Is.True);
            Assert.That(previewAuthoringService.PlaceFireOrigin(new Vector2(-4f, -4f), out var fireOriginId, 1f, 0f, true), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(4f, 0f), out var lowerPortalId), Is.True);

            workspaceService.SetActiveFloor(secondFloorId);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(4f, 0f), out var upperPortalId), Is.True);
            Assert.That(semanticObjectAuthoringService.LinkStairPortals(firstFloorId, lowerPortalId, secondFloorId, upperPortalId, StairTraversalDirection.Bidirectional, 2f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(8f, 0f), out _, new Vector2(2f, 1f), 0f, 1.5f, 50f, 1f, "Roof Exit"), Is.True);

            Assert.That(scenarioManagementService.CreateScenarioPreset(
                "Vertical Drill",
                new[] { spawnLayoutId },
                new[] { fireOriginId },
                new PreviewParameterData { spreadIntensity = 1f, startDelaySeconds = 0f },
                out var scenarioPresetId), Is.True);
            Assert.That(scenarioManagementService.ApplyScenarioPreset(scenarioPresetId), Is.True);

            var cameraObject = new GameObject("Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<Camera>().orthographic = true;

            var debugRoot = new GameObject("PreviewDiagnosticsRoot");
            var renderer = debugRoot.AddComponent<SandboxPreviewDiagnosticsRenderer>();
            renderer.SendMessage("Awake");

            Assert.That(previewService.EnterPreviewMode(), Is.True);
            Assert.That(previewService.RunPreview(), Is.True);
            renderer.Refresh();

            var report = previewService.LastPreviewReport;
            Assert.That(report.didRun, Is.True);
            Assert.That(report.reachableSpawnSamples, Is.EqualTo(report.totalSpawnSamples));
            Assert.That(report.routeSegments.Any(segment => segment.floorId == firstFloorId), Is.True);
            Assert.That(report.routeSegments.Any(segment => segment.floorId == secondFloorId), Is.True);
            Assert.That(report.heatPoints.Count, Is.GreaterThan(0));
            Assert.That(report.diagnostics.Any(diagnostic => diagnostic.diagnosticKind == SandboxPreviewDiagnosticKind.ChokePoint), Is.True);
            Assert.That(debugRoot.transform.childCount, Is.GreaterThan(0));

            Object.DestroyImmediate(debugRoot);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void PreviewRun_StopsWhenBlockingValidationIssuesExist()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out var previewAuthoringService,
                out var previewService,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            CreateEnclosedRoom(wallAuthoringService);
            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(0f, 0f), out _, out _, null, "Validation Layout", true), Is.True);
            Assert.That(previewAuthoringService.PlaceFireOrigin(new Vector2(-3f, -3f), out _, 1f, 0f, true), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(5f, 0f), out _, new Vector2(2f, 2f), 0f, 1.5f, 20f, 1f, "North Exit"), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceObstacle(new Vector2(5f, 0f), out _, new Vector2(2f, 2f), 0f, 1f, 0f, "Blocked Exit"), Is.True);

            Assert.That(previewService.EnterPreviewMode(), Is.True);
            Assert.That(previewService.RunPreview(), Is.False);
            Assert.That(previewService.LastPreviewReport.didRun, Is.False);
            Assert.That(previewService.LastPreviewReport.summary, Is.EqualTo("Preview blocked by validation issues."));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void PreviewRun_RequiresExplicitPreviewMode()
        {
            var host = CreatePhase10Host(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out _,
                out var previewAuthoringService,
                out var previewService,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            CreateEnclosedRoom(wallAuthoringService);
            Assert.That(previewAuthoringService.PlaceSpawnPoint(new Vector2(0f, 0f), out _, out _, null, "Preview Layout", true), Is.True);
            Assert.That(previewAuthoringService.PlaceFireOrigin(new Vector2(-2f, -2f), out _, 1f, 0f, true), Is.True);

            Assert.That(previewService.RunPreview(), Is.False);
            Assert.That(previewService.LastPreviewReport.didRun, Is.False);
            Assert.That(previewService.LastPreviewReport.summary, Is.EqualTo("Enter preview mode before running preview."));

            Object.DestroyImmediate(host);
        }

        private static GameObject CreatePhase10Host(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxInputRouter inputRouter,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxSemanticObjectAuthoringService semanticObjectAuthoringService,
            out SandboxPreviewAuthoringService previewAuthoringService,
            out SandboxPreviewService previewService,
            out SandboxScenarioManagementService scenarioManagementService,
            out SandboxFloorManagementService floorManagementService)
        {
            var host = new GameObject("Phase10Host");
            host.AddComponent<SandboxSaveLoadService>();
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxSelectionService>();
            inputRouter = host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxToolStateService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            var colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            var validationService = host.AddComponent<SandboxValidationService>();
            var roomDetectionService = host.AddComponent<SandboxRoomDetectionService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            host.AddComponent<SandboxClipboardService>();
            host.AddComponent<SandboxWallSnappingService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();
            previewService = host.AddComponent<SandboxPreviewService>();
            previewAuthoringService = host.AddComponent<SandboxPreviewAuthoringService>();
            scenarioManagementService = host.AddComponent<SandboxScenarioManagementService>();
            floorManagementService = host.AddComponent<SandboxFloorManagementService>();

            workspaceService.SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            roomDetectionService.SendMessage("Awake");
            host.GetComponent<SandboxVisualOrganizationService>().SendMessage("Awake");
            host.GetComponent<SandboxClipboardService>().SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            previewService.SendMessage("Awake");
            previewAuthoringService.SendMessage("Awake");
            scenarioManagementService.SendMessage("Awake");
            floorManagementService.SendMessage("Awake");
            return host;
        }

        private static void CreateEnclosedRoom(SandboxWallAuthoringService wallAuthoringService)
        {
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(-2f, -2f), new Vector2(4f, -2f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(4f, -2f), new Vector2(4f, 4f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(4f, 4f), new Vector2(-2f, 4f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(-2f, 4f), new Vector2(-2f, -2f), 0.2f), Is.True);
        }

    }
}
