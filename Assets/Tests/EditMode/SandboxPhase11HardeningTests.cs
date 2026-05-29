using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using EvacLogix.Sandbox.UI.Shortcuts;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase11HardeningTests
    {
        [Test]
        public void LegendAndShortcuts_AreDiscoverableAndConsistent()
        {
            var harness = CreateHarness();

            Assert.That(harness.legendShell.HasCompleteCoverage, Is.True);
            Assert.That(harness.legendShell.LegendEntries.Count, Is.EqualTo(SandboxObjectPresentationCatalog.Definitions.Count));
            Assert.That(harness.legendShell.LegendEntries.All(entry => !string.IsNullOrWhiteSpace(entry.label)), Is.True);
            Assert.That(harness.legendShell.LegendEntries.All(entry => !string.IsNullOrWhiteSpace(entry.description)), Is.True);

            var shortcutEntries = harness.keyboardShortcutService.GetShortcutCatalogEntries();
            Assert.That(shortcutEntries.Count, Is.EqualTo(harness.keyboardShortcutService.Bindings.Count));
            Assert.That(shortcutEntries.All(entry => !string.IsNullOrWhiteSpace(entry.category)), Is.True);
            Assert.That(shortcutEntries.All(entry => !string.IsNullOrWhiteSpace(entry.label)), Is.True);
            Assert.That(shortcutEntries.All(entry => !string.IsNullOrWhiteSpace(entry.description)), Is.True);
            Assert.That(shortcutEntries.All(entry => !string.IsNullOrWhiteSpace(entry.bindingDisplay)), Is.True);
            Assert.That(harness.keyboardShortcutService.HasBindingConflicts, Is.False);

            harness.Destroy();
        }

        [Test]
        public void InspectorAudit_CoversReleaseEntitiesAndSeedsAdvancedFoldouts()
        {
            var harness = CreateHarness();

            var auditEntries = harness.inspectorShell.GetInspectorAuditEntries();
            Assert.That(harness.inspectorShell.IsFullyWired, Is.True);
            Assert.That(harness.inspectorShell.GetMissingDependencies(), Is.Empty);
            Assert.That(auditEntries.Select(entry => entry.key), Is.SupersetOf(new[]
            {
                "project",
                "floor",
                "wall",
                "door",
                "window",
                "exit",
                "obstacle",
                "stair",
                "region",
                "spawn",
                "preview",
                "scenario"
            }));

            foreach (var advancedKey in auditEntries.Select(entry => entry.advancedFoldoutKey).Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                Assert.That(harness.editorQoLService.AdvancedFoldouts.Any(entry => entry.key == advancedKey), Is.True, advancedKey);
                Assert.That(harness.editorQoLService.IsAdvancedFoldoutExpanded(advancedKey), Is.False, advancedKey);
            }

            Assert.That(harness.inspectorShell.GetShortcutConflicts().Count, Is.EqualTo(0));

            harness.Destroy();
        }

        [Test]
        public void InspectorUpdateFlows_RoundTripProjectPreviewAndScenarioMetadata()
        {
            var harness = CreateHarness();
            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            CreateEnclosedRoom(harness.wallAuthoringService);

            Assert.That(harness.previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out var spawnLayoutId, null, "Morning Layout", true), Is.True);
            Assert.That(harness.previewAuthoringService.PlaceFireOrigin(new Vector2(6f, 2f), out var fireOriginId, 1f, 0f, true), Is.True);
            Assert.That(harness.previewAuthoringService.PlaceRegion(new Vector2(3f, 3f), new Vector2(2f, 2f), out var regionId, "North Zone", RegionSemanticType.SpawnZone), Is.True);
            Assert.That(harness.scenarioManagementService.CreateScenarioPreset(
                "Morning Drill",
                new[] { spawnLayoutId },
                new[] { fireOriginId },
                new PreviewParameterData { spreadIntensity = 1f, startDelaySeconds = 0f },
                out var scenarioPresetId), Is.True);

            Assert.That(harness.inspectorShell.UpdateProjectMetadata(
                "West Tower",
                "Release candidate sandbox project",
                "QA Team",
                new[] { new MetadataFieldData { key = "review", value = "phase11" } }), Is.True);
            Assert.That(harness.inspectorShell.UpdateSpawnLayout(
                spawnLayoutId,
                "Evening Layout",
                true,
                new[] { new MetadataFieldData { key = "occupancy", value = "high" } }), Is.True);
            Assert.That(harness.inspectorShell.UpdateFireOrigin(
                fireOriginId,
                new Vector2(7f, 2f),
                1.8f,
                4f,
                false), Is.True);
            Assert.That(harness.inspectorShell.UpdateRegion(
                regionId,
                "Restricted North Zone",
                RegionSemanticType.RestrictedZone,
                new[]
                {
                    new Vector2(1f, 1f),
                    new Vector2(1f, 4f),
                    new Vector2(4f, 4f),
                    new Vector2(4f, 1f)
                },
                new[] { new MetadataFieldData { key = "note", value = "keep clear" } }), Is.True);
            Assert.That(harness.inspectorShell.UpdateScenarioPreset(
                scenarioPresetId,
                "Evening Drill",
                new[] { spawnLayoutId },
                new[] { fireOriginId },
                2.1f,
                6f,
                new[] { new MetadataFieldData { key = "owner", value = "ops" } }), Is.True);

            var roundTripped = SandboxProjectSerializer.Deserialize(SandboxProjectSerializer.Serialize(harness.workspaceService.ActiveProject));

            Assert.That(roundTripped.metadata.buildingName, Is.EqualTo("West Tower"));
            Assert.That(roundTripped.metadata.authorName, Is.EqualTo("QA Team"));
            Assert.That(roundTripped.metadata.customFields.Single().value, Is.EqualTo("phase11"));
            Assert.That(roundTripped.spawnLayouts.Single(layout => layout.spawnLayoutId == spawnLayoutId).name, Is.EqualTo("Evening Layout"));
            Assert.That(roundTripped.spawnLayouts.Single(layout => layout.spawnLayoutId == spawnLayoutId).metadataFields.Single().value, Is.EqualTo("high"));
            Assert.That(roundTripped.fireOrigins.Single(origin => origin.fireOriginId == fireOriginId).position, Is.EqualTo(new Vector2(7f, 2f)));
            Assert.That(roundTripped.fireOrigins.Single(origin => origin.fireOriginId == fireOriginId).startDelaySeconds, Is.EqualTo(4f).Within(0.001f));
            Assert.That(roundTripped.fireOrigins.Single(origin => origin.fireOriginId == fireOriginId).isPersistent, Is.False);
            Assert.That(roundTripped.floors.Single().regions.Single(region => region.regionId == regionId).name, Is.EqualTo("Restricted North Zone"));
            Assert.That(roundTripped.floors.Single().regions.Single(region => region.regionId == regionId).semanticType, Is.EqualTo(RegionSemanticType.RestrictedZone));
            Assert.That(roundTripped.scenarioPresets.Single(preset => preset.scenarioPresetId == scenarioPresetId).name, Is.EqualTo("Evening Drill"));
            Assert.That(roundTripped.scenarioPresets.Single(preset => preset.scenarioPresetId == scenarioPresetId).previewParameters.spreadIntensity, Is.EqualTo(2.1f).Within(0.001f));
            Assert.That(roundTripped.scenarioPresets.Single(preset => preset.scenarioPresetId == scenarioPresetId).metadataFields.Single().value, Is.EqualTo("ops"));

            harness.Destroy();
        }

        [Test]
        public void StressPreview_LargeFloorsDenseObstaclesAndMultiFloorStairsRemainUsable()
        {
            var harness = CreateHarness();
            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var floorOneId = harness.workspaceService.ActiveFloor.floorId;
            Assert.That(harness.floorManagementService.AddFloor(out var floorTwoId, "Level 2", 3f), Is.True);
            Assert.That(harness.floorManagementService.AddFloor(out var floorThreeId, "Level 3", 6f), Is.True);

            PopulateStressFloor(harness, floorOneId, 0f);
            PopulateStressFloor(harness, floorTwoId, 12f);
            PopulateStressFloor(harness, floorThreeId, 24f);

            CreateEnclosedRoom(harness.wallAuthoringService);
            Assert.That(harness.previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out var spawnLayoutId, null, "Stress Layout", true), Is.True);
            Assert.That(harness.previewAuthoringService.PlaceFireOrigin(new Vector2(-4f, -4f), out var fireOriginId, 1.2f, 1f, true), Is.True);
            Assert.That(harness.scenarioManagementService.CreateScenarioPreset(
                "Stress Drill",
                new[] { spawnLayoutId },
                new[] { fireOriginId },
                new PreviewParameterData { spreadIntensity = 1.2f, startDelaySeconds = 1f },
                out var scenarioId), Is.True);
            Assert.That(harness.scenarioManagementService.ApplyScenarioPreset(scenarioId), Is.True);

            var stopwatch = Stopwatch.StartNew();
            Assert.That(harness.previewService.EnterPreviewMode(), Is.True);
            Assert.That(harness.previewService.RunPreview(), Is.True);
            stopwatch.Stop();

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(5000));
            Assert.That(harness.previewService.LastPreviewReport.didRun, Is.True);
            Assert.That(harness.previewService.LastPreviewReport.routeSegments.Count, Is.GreaterThan(0));
            Assert.That(harness.previewService.LastPreviewReport.heatPoints.Count, Is.GreaterThan(0));
            Assert.That(harness.previewService.LastPreviewReport.diagnostics.Any(diagnostic => diagnostic.diagnosticKind == SandboxPreviewDiagnosticKind.ChokePoint), Is.True);

            harness.Destroy();
        }

        [Test]
        public void PublicFacingShells_ShowModeValidationAndSemanticReadiness()
        {
            var harness = CreateHarness();
            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            var topBarObject = new GameObject("TopBar");
            var topBarShell = topBarObject.AddComponent<SandboxTopBarShell>();
            topBarShell.SendMessage("Awake");

            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Edit Mode"));
            Assert.That(harness.statusBarShell.ModeLabel, Is.EqualTo("Edit Mode"));
            Assert.That(topBarShell.LifecycleStateLabel, Is.EqualTo(harness.statusBarShell.LifecycleStateLabel));
            Assert.That(harness.legendShell.HasCompleteCoverage, Is.True);
            Assert.That(harness.legendShell.LegendEntries.All(entry => !string.IsNullOrWhiteSpace(entry.description)), Is.True);

            Assert.That(harness.previewService.EnterPreviewMode(), Is.True);
            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Preview Mode"));
            Assert.That(harness.statusBarShell.ModeLabel, Is.EqualTo("Preview Mode"));

            harness.previewService.ExitPreviewMode();
            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Edit Mode"));
            Assert.That(harness.statusBarShell.ModeLabel, Is.EqualTo("Edit Mode"));

            Object.DestroyImmediate(topBarObject);
            harness.Destroy();
        }

        private static void PopulateStressFloor(Phase11Harness harness, string floorId, float xOffset)
        {
            harness.workspaceService.SetActiveFloor(floorId);
            for (var row = 0; row < 12; row += 1)
            {
                var y = row * 1.5f;
                Assert.That(harness.wallAuthoringService.CreateLineWall(new Vector2(xOffset, y), new Vector2(xOffset + 10f, y), 0.2f), Is.True);
            }

            for (var column = 0; column < 8; column += 1)
            {
                var x = xOffset + column * 1.25f;
                Assert.That(harness.wallAuthoringService.CreateLineWall(new Vector2(x, 0f), new Vector2(x, 16f), 0.2f), Is.True);
            }

            for (var index = 0; index < 20; index += 1)
            {
                var center = new Vector2(xOffset + 0.75f + (index % 5) * 1.6f, 0.75f + (index / 5) * 2.6f);
                Assert.That(harness.semanticObjectAuthoringService.PlaceObstacle(center, out _, new Vector2(0.5f, 0.5f), 0f, 0.5f, 0.5f, $"Obstacle {floorId}-{index}"), Is.True);
            }

            Assert.That(harness.semanticObjectAuthoringService.PlaceExit(new Vector2(xOffset + 11f, 8f), out _, new Vector2(1.5f, 3f), 0f, 2f, 75f, 1f, $"Exit {floorId}"), Is.True);
            Assert.That(harness.semanticObjectAuthoringService.PlaceStairPortal(new Vector2(xOffset + 9f, 1f), out _, null, 0f, $"Stair {floorId}"), Is.True);

            if (harness.workspaceService.ActiveProject.floors.Select(floor => floor.floorId).ToList().IndexOf(floorId) > 0)
            {
                var orderedFloors = harness.workspaceService.ActiveProject.floors.OrderBy(floor => floor.order).ToList();
                var floorIndex = orderedFloors.FindIndex(floor => floor.floorId == floorId);
                var lowerFloor = orderedFloors[floorIndex - 1];
                var lowerPortalId = lowerFloor.floorModifiers
                    .FirstOrDefault(field => field.key == "stress-stair-portal")?.value;
                var currentPortalId = harness.workspaceService.ActiveFloor.stairPortals.Last().stairPortalId;
                Assert.That(string.IsNullOrWhiteSpace(lowerPortalId), Is.False);
                Assert.That(harness.semanticObjectAuthoringService.LinkStairPortals(lowerFloor.floorId, lowerPortalId, floorId, currentPortalId, StairTraversalDirection.Bidirectional, 2f), Is.True);
            }

            harness.workspaceService.ActiveFloor.floorModifiers.RemoveAll(field => field.key == "stress-stair-portal");
            harness.workspaceService.ActiveFloor.floorModifiers.Add(new MetadataFieldData
            {
                key = "stress-stair-portal",
                value = harness.workspaceService.ActiveFloor.stairPortals.Last().stairPortalId
            });
        }

        private static Phase11Harness CreateHarness()
        {
            var host = new GameObject("Phase11Host");
            host.AddComponent<SandboxSaveLoadService>();
            host.AddComponent<SandboxCommandHistory>();
            var selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxToolStateService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            var workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            var colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            var validationService = host.AddComponent<SandboxValidationService>();
            var roomDetectionService = host.AddComponent<SandboxRoomDetectionService>();
            var visualOrganizationService = host.AddComponent<SandboxVisualOrganizationService>();
            var clipboardService = host.AddComponent<SandboxClipboardService>();
            var wallSnappingService = host.AddComponent<SandboxWallSnappingService>();
            var wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            var semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();
            var floorManagementService = host.AddComponent<SandboxFloorManagementService>();
            var previewService = host.AddComponent<SandboxPreviewService>();
            var previewAuthoringService = host.AddComponent<SandboxPreviewAuthoringService>();
            var scenarioManagementService = host.AddComponent<SandboxScenarioManagementService>();
            var projectMetadataService = host.AddComponent<SandboxProjectMetadataService>();
            var editorQoLService = host.AddComponent<SandboxEditorQoLService>();
            var keyboardShortcutService = host.AddComponent<SandboxKeyboardShortcutService>();
            var measurementService = host.AddComponent<SandboxMeasurementService>();

            workspaceService.SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            roomDetectionService.SendMessage("Awake");
            visualOrganizationService.SendMessage("Awake");
            clipboardService.SendMessage("Awake");
            wallSnappingService.SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            floorManagementService.SendMessage("Awake");
            previewService.SendMessage("Awake");
            previewAuthoringService.SendMessage("Awake");
            scenarioManagementService.SendMessage("Awake");
            projectMetadataService.SendMessage("Awake");
            editorQoLService.SendMessage("Awake");
            keyboardShortcutService.SendMessage("Awake");
            measurementService.SendMessage("Awake");

            var statusBarObject = new GameObject("StatusBar");
            var statusBarShell = statusBarObject.AddComponent<SandboxStatusBarShell>();
            statusBarShell.SendMessage("Awake");

            var inspectorObject = new GameObject("Inspector");
            var inspectorShell = inspectorObject.AddComponent<SandboxInspectorPanelShell>();
            inspectorShell.SendMessage("Awake");

            var legendObject = new GameObject("Legend");
            var legendShell = legendObject.AddComponent<SandboxVisualLegendShell>();
            legendShell.SendMessage("Awake");

            return new Phase11Harness
            {
                host = host,
                statusBarObject = statusBarObject,
                inspectorObject = inspectorObject,
                legendObject = legendObject,
                statusBarShell = statusBarShell,
                workspaceService = workspaceService,
                selectionService = selectionService,
                wallAuthoringService = wallAuthoringService,
                semanticObjectAuthoringService = semanticObjectAuthoringService,
                floorManagementService = floorManagementService,
                previewService = previewService,
                previewAuthoringService = previewAuthoringService,
                scenarioManagementService = scenarioManagementService,
                projectMetadataService = projectMetadataService,
                editorQoLService = editorQoLService,
                keyboardShortcutService = keyboardShortcutService,
                inspectorShell = inspectorShell,
                legendShell = legendShell
            };
        }

        private static void CreateEnclosedRoom(SandboxWallAuthoringService wallAuthoringService)
        {
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(-2f, -2f), new Vector2(4f, -2f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(4f, -2f), new Vector2(4f, 4f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(4f, 4f), new Vector2(-2f, 4f), 0.2f), Is.True);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(-2f, 4f), new Vector2(-2f, -2f), 0.2f), Is.True);
        }

        private sealed class Phase11Harness
        {
            public GameObject host;
            public GameObject statusBarObject;
            public GameObject inspectorObject;
            public GameObject legendObject;
            public SandboxStatusBarShell statusBarShell;
            public SandboxProjectWorkspaceService workspaceService;
            public SandboxSelectionService selectionService;
            public SandboxWallAuthoringService wallAuthoringService;
            public SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
            public SandboxFloorManagementService floorManagementService;
            public SandboxPreviewService previewService;
            public SandboxPreviewAuthoringService previewAuthoringService;
            public SandboxScenarioManagementService scenarioManagementService;
            public SandboxProjectMetadataService projectMetadataService;
            public SandboxEditorQoLService editorQoLService;
            public SandboxKeyboardShortcutService keyboardShortcutService;
            public SandboxInspectorPanelShell inspectorShell;
            public SandboxVisualLegendShell legendShell;

            public void Destroy()
            {
                Object.DestroyImmediate(legendObject);
                Object.DestroyImmediate(inspectorObject);
                Object.DestroyImmediate(statusBarObject);
                Object.DestroyImmediate(host);
            }
        }
    }
}
