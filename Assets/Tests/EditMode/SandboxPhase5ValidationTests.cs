using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase5ValidationTests
    {
        [Test]
        public void ColliderRebuildService_BuildsDeterministicCollidersAndSupportsFullRebuild()
        {
            var runtimeRoot = new GameObject("ColliderRoot");
            var host = CreateValidationHost(out var workspace, out _, out var authoringService, out var colliderRebuildService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(authoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f), 0.4f), Is.True);

            Assert.That(colliderRebuildService.RebuildRequestCount, Is.EqualTo(1));
            Assert.That(colliderRebuildService.IncrementalRebuildCount, Is.EqualTo(1));
            Assert.That(colliderRebuildService.GeneratedColliders.Count, Is.EqualTo(1));

            var generatedCollider = colliderRebuildService.GeneratedColliders[0];
            Assert.That(generatedCollider.size.x, Is.EqualTo(6f).Within(0.001f));
            Assert.That(generatedCollider.size.y, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(runtimeRoot.transform.childCount, Is.EqualTo(1));

            colliderRebuildService.RebuildAll();
            Assert.That(colliderRebuildService.FullRebuildCount, Is.EqualTo(1));
            Assert.That(colliderRebuildService.LastRebuildWasFull, Is.True);

            Object.DestroyImmediate(host);
            Object.DestroyImmediate(runtimeRoot);
        }

        [Test]
        public void ValidationService_FlagsBlockingAndWarningIssuesAndGroupsThem()
        {
            var colliderRoot = new GameObject("ColliderRoot");
            var host = CreateValidationHost(out var workspace, out _, out _, out var colliderRebuildService, out var validationService);
            var panelObject = new GameObject("ValidationPanel");
            var panel = panelObject.AddComponent<SandboxValidationPanelShell>();
            panel.SendMessage("Awake");

            var project = CreateInvalidProject();
            workspace.SetActiveProject(project);
            workspace.SetActiveFloor(project.floors[0].floorId);

            colliderRebuildService.RebuildAll();
            validationService.ValidateActiveProject();

            Assert.That(validationService.HasBlockingIssues, Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Disconnected wall structures" && issue.severity == ValidationIssueSeverity.Warning), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title.Contains("Invalid door") && issue.severity == ValidationIssueSeverity.BlockingError), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid stair link" && issue.severity == ValidationIssueSeverity.BlockingError), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Overlapping exits" && issue.severity == ValidationIssueSeverity.Warning), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid obstacle overlap" && issue.severity == ValidationIssueSeverity.BlockingError), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title.Contains("Duplicate wall segment ID")), Is.True);
            Assert.That(panel.HasBlockingIssues, Is.True);
            Assert.That(panel.IssueGroups.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(panel.IssueGroups.Any(group => group.label == "Level 1"), Is.True);

            Object.DestroyImmediate(panelObject);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(colliderRoot);
        }

        [Test]
        public void ValidationHighlightRenderer_CreatesHighlightObjectsForIssues()
        {
            var colliderRoot = new GameObject("ColliderRoot");
            var host = CreateValidationHost(out var workspace, out _, out _, out var colliderRebuildService, out var validationService);
            var highlightRoot = new GameObject("ValidationHighlightRoot");
            var renderer = highlightRoot.AddComponent<SandboxValidationHighlightRenderer>();
            renderer.SendMessage("Awake");

            var project = CreateInvalidProject();
            workspace.SetActiveProject(project);
            workspace.SetActiveFloor(project.floors[0].floorId);
            colliderRebuildService.RebuildAll();
            validationService.ValidateActiveProject();

            Assert.That(highlightRoot.transform.childCount, Is.GreaterThan(0));

            Object.DestroyImmediate(highlightRoot);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(colliderRoot);
        }

        [Test]
        public void TopBarAndValidationPanel_BlockPreviewExportButKeepEditingEnabled()
        {
            var colliderRoot = new GameObject("ColliderRoot");
            var host = CreateValidationHost(out var workspace, out _, out var authoringService, out var colliderRebuildService, out var validationService);
            var statusBarObject = new GameObject("StatusBar");
            var statusBar = statusBarObject.AddComponent<SandboxStatusBarShell>();

            var topBarObject = new GameObject("TopBar");
            var topBar = topBarObject.AddComponent<SandboxTopBarShell>();
            topBar.SendMessage("Awake");

            var panelObject = new GameObject("ValidationPanel");
            var panel = panelObject.AddComponent<SandboxValidationPanelShell>();
            panel.SendMessage("Awake");

            var project = CreateInvalidProject();
            workspace.SetActiveProject(project);
            workspace.SetActiveFloor(project.floors[0].floorId);
            colliderRebuildService.RebuildAll();
            validationService.ValidateActiveProject();

            var exportPath = Path.Combine(Path.GetTempPath(), $"blocked_export_{SandboxId.NewId()}.png");
            try
            {
                Assert.That(topBar.ExportPreviewImage(exportPath), Is.False);
                Assert.That(statusBar.StatusMessage, Is.EqualTo("Resolve blocking validation issues before preview or export."));
                Assert.That(topBar.CanOpenPreview(), Is.False);
            }
            finally
            {
                if (File.Exists(exportPath))
                {
                    File.Delete(exportPath);
                }
            }

            Assert.That(authoringService.CreateLineWall(new Vector2(10f, 0f), new Vector2(13f, 0f)), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.GreaterThan(2));

            panel.RebuildAll();
            Assert.That(colliderRebuildService.FullRebuildCount, Is.GreaterThanOrEqualTo(2));

            Object.DestroyImmediate(panelObject);
            Object.DestroyImmediate(topBarObject);
            Object.DestroyImmediate(statusBarObject);
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(colliderRoot);
        }

        private static GameObject CreateValidationHost(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxCommandHistory commandHistory,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxColliderRebuildService colliderRebuildService,
            out SandboxValidationService validationService)
        {
            var host = new GameObject("ValidationHost");
            host.AddComponent<SandboxSaveLoadService>();
            commandHistory = host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxWallSnappingService>();
            colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            validationService = host.AddComponent<SandboxValidationService>();
            host.AddComponent<SandboxScaleCalibrationService>();
            host.AddComponent<SandboxCalibrationWorkflowService>();
            host.AddComponent<SandboxPreviewImageExportService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();

            workspaceService.SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            host.GetComponent<SandboxScaleCalibrationService>().SendMessage("Awake");
            host.GetComponent<SandboxCalibrationWorkflowService>().SendMessage("Awake");
            host.GetComponent<SandboxPreviewImageExportService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            return host;
        }

        private static BuildingProjectData CreateInvalidProject()
        {
            var floor1 = new FloorData
            {
                floorId = "floor-1",
                name = "Level 1",
                order = 0,
            };

            var junctionA = new WallJunctionData { wallJunctionId = "j-a", position = new Vector2(0f, 0f) };
            var junctionB = new WallJunctionData { wallJunctionId = "j-b", position = new Vector2(4f, 0f) };
            var junctionC = new WallJunctionData { wallJunctionId = "j-c", position = new Vector2(10f, 0f) };
            var junctionD = new WallJunctionData { wallJunctionId = "j-d", position = new Vector2(14f, 0f) };
            junctionA.connectedWallSegmentIds.Add("wall-duplicate");
            junctionB.connectedWallSegmentIds.Add("wall-duplicate");
            junctionC.connectedWallSegmentIds.Add("wall-duplicate");
            junctionD.connectedWallSegmentIds.Add("wall-duplicate");
            floor1.wallJunctions.AddRange(new[] { junctionA, junctionB, junctionC, junctionD });
            floor1.wallSegments.Add(new WallSegmentData
            {
                wallSegmentId = "wall-duplicate",
                startJunctionId = "j-a",
                endJunctionId = "j-b",
                startPoint = junctionA.position,
                endPoint = junctionB.position,
                thickness = 0.25f
            });
            floor1.wallSegments.Add(new WallSegmentData
            {
                wallSegmentId = "wall-duplicate",
                startJunctionId = "j-c",
                endJunctionId = "j-d",
                startPoint = junctionC.position,
                endPoint = junctionD.position,
                thickness = 0.25f
            });

            floor1.doors.Add(new DoorData
            {
                doorId = "door-invalid",
                wallSegmentId = "missing-wall",
                offsetAlongWall = 1f,
                width = 1f
            });

            floor1.exits.Add(new ExitZoneData
            {
                exitZoneId = "exit-invalid",
                center = new Vector2(2f, 2f),
                size = new Vector2(-1f, 1f),
                width = 1f
            });
            floor1.exits.Add(new ExitZoneData
            {
                exitZoneId = "exit-overlap-a",
                center = new Vector2(5f, 5f),
                size = new Vector2(2f, 2f),
                width = 1f
            });
            floor1.exits.Add(new ExitZoneData
            {
                exitZoneId = "exit-overlap-b",
                center = new Vector2(5.5f, 5f),
                size = new Vector2(2f, 2f),
                width = 1f
            });

            floor1.obstacles.Add(new ObstacleData
            {
                obstacleId = "obstacle-blocking",
                center = new Vector2(5f, 5f),
                size = new Vector2(1f, 1f),
                semanticType = ObstacleSemanticType.HardBlocking
            });
            floor1.obstacles.Add(new ObstacleData
            {
                obstacleId = "obstacle-conflict",
                center = new Vector2(5f, 5f),
                size = new Vector2(1f, 1f),
                semanticType = ObstacleSemanticType.SlowThrough,
                traversalCostMultiplier = 2f
            });

            floor1.stairPortals.Add(new StairPortalData
            {
                stairPortalId = "stair-a",
                localPosition = new Vector2(8f, 8f),
                targetFloorId = "missing-floor",
                targetStairPortalId = "missing-portal"
            });
            floor1.stairPortals.Add(new StairPortalData
            {
                stairPortalId = "stair-b",
                localPosition = new Vector2(8f, 8f),
                targetFloorId = "floor-2",
                targetStairPortalId = "stair-c"
            });

            var floor2 = new FloorData
            {
                floorId = "floor-2",
                name = "Level 1",
                order = 0,
            };

            floor2.stairPortals.Add(new StairPortalData
            {
                stairPortalId = "stair-c",
                localPosition = new Vector2(1f, 1f),
                targetFloorId = "floor-2",
                targetStairPortalId = "stair-c"
            });

            var project = new BuildingProjectData
            {
                projectId = "invalid-project",
                metadata = new ProjectMetadataData
                {
                    buildingName = "Invalid Sample"
                }
            };
            project.floors.Add(floor1);
            project.floors.Add(floor2);
            SandboxProjectDataUtility.EnsureIds(project);
            return project;
        }
    }
}
