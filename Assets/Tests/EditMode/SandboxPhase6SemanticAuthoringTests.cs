using System.Collections.Generic;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase6SemanticAuthoringTests
    {
        [Test]
        public void DoorAndWindowPlacement_RequireExistingWallSegments()
        {
            var host = CreateSemanticHost(
                out var workspaceService,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(1f, 0f), out var failedDoorId), Is.False);
            Assert.That(failedDoorId, Is.EqualTo(string.Empty));
            Assert.That(semanticObjectAuthoringService.PlaceWindow(new Vector2(1f, 0f), out var failedWindowId), Is.False);
            Assert.That(failedWindowId, Is.EqualTo(string.Empty));

            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(4.75f, 0.05f), out var overhangingDoorId, 1f), Is.False);
            Assert.That(overhangingDoorId, Is.EqualTo(string.Empty));
            Assert.That(semanticObjectAuthoringService.PlaceWindow(new Vector2(4.7f, 0.05f), out var overhangingWindowId, 1f), Is.False);
            Assert.That(overhangingWindowId, Is.EqualTo(string.Empty));
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(4.49f, 0.05f), out var edgeDoorId, 1f), Is.True);
            Assert.That(edgeDoorId, Is.Not.EqualTo(string.Empty));
            Assert.That(semanticObjectAuthoringService.PlaceWindow(new Vector2(0.51f, 0.05f), out var edgeWindowId, 1f), Is.True);
            Assert.That(edgeWindowId, Is.Not.EqualTo(string.Empty));
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(1.5f, 0.15f), out var doorId, 1.2f, DoorState.OneWay), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceWindow(new Vector2(3.5f, 0.1f), out var windowId, 1f, true, 2.5f, 1.4f), Is.True);

            var floor = workspaceService.ActiveFloor;
            var wallId = floor.wallSegments[0].wallSegmentId;
            var edgeDoor = floor.doors.Single(candidate => candidate.doorId == edgeDoorId);
            var door = floor.doors.Single(candidate => candidate.doorId == doorId);
            var edgeWindow = floor.windows.Single(candidate => candidate.windowId == edgeWindowId);
            var window = floor.windows.Single(candidate => candidate.windowId == windowId);

            Assert.That(edgeDoor.wallSegmentId, Is.EqualTo(wallId));
            Assert.That(edgeDoor.offsetAlongWall, Is.EqualTo(4.49f).Within(0.1f));
            Assert.That(door.wallSegmentId, Is.EqualTo(wallId));
            Assert.That(door.state, Is.EqualTo(DoorState.OneWay));
            Assert.That(door.offsetAlongWall, Is.GreaterThan(0f));
            Assert.That(edgeWindow.wallSegmentId, Is.EqualTo(wallId));
            Assert.That(edgeWindow.offsetAlongWall, Is.EqualTo(0.51f).Within(0.1f));
            Assert.That(window.wallSegmentId, Is.EqualTo(wallId));
            Assert.That(window.canBeUsedForEscape, Is.True);
            Assert.That(window.escapeCost, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(window.escapeRiskMultiplier, Is.EqualTo(1.4f).Within(0.001f));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void InspectorEdits_AreUndoableAndSemanticMetadataRoundTripsThroughSaveLoad()
        {
            var host = CreateSemanticHost(
                out var workspaceService,
                out var commandHistory,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out var saveLoadService);

            var inspectorObject = new GameObject("Inspector");
            var inspector = inspectorObject.AddComponent<SandboxInspectorPanelShell>();
            inspector.SendMessage("Awake");

            var project = workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            project.floors.Add(new FloorData
            {
                floorId = "floor-2",
                name = "Floor 2",
                order = 1,
                elevation = 3f,
            });
            SandboxProjectDataUtility.EnsureIds(project);
            workspaceService.SetActiveProject(project);
            workspaceService.SetActiveFloor(project.floors[0].floorId);

            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.3f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(1f, 0.1f), out var doorId, 1f, DoorState.Normal), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceWindow(new Vector2(2.5f, 0.1f), out var windowId, 1f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(6f, 1.5f), out var exitZoneId, new Vector2(2f, 1.5f), 10f, 1.5f, 20f, 1f, "North Exit"), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceObstacle(new Vector2(8.5f, 1.5f), out var obstacleId, new Vector2(1f, 2f), 0f, 1f, 0f, "Stacked Chairs"), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(10f, 1.5f), out var lowerStairPortalId), Is.True);

            workspaceService.SetActiveFloor("floor-2");
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(2f, 2f), out var upperStairPortalId), Is.True);

            Assert.That(
                inspector.LinkStairPortals(
                    project.floors[0].floorId,
                    lowerStairPortalId,
                    "floor-2",
                    upperStairPortalId,
                    StairTraversalDirection.AscendOnly,
                    3f),
                Is.True);

            Assert.That(
                inspector.UpdateDoor(
                    doorId,
                    1.4f,
                    1.2f,
                    DoorState.Locked,
                    new[] { "staff-only", "egress" },
                    new[] { new MetadataFieldData { key = "badge_required", value = "true" } }),
                Is.True);

            Assert.That(
                inspector.UpdateWindow(
                    windowId,
                    1.1f,
                    2.6f,
                    true,
                    4f,
                    1.8f,
                    new[] { "rescue", "ladder" },
                    new[] { new MetadataFieldData { key = "floor_risk", value = "high" } }),
                Is.True);

            Assert.That(
                inspector.UpdateExit(
                    exitZoneId,
                    new Vector2(6.25f, 1.75f),
                    new Vector2(2.5f, 1.75f),
                    25f,
                    2f,
                    80f,
                    3f,
                    "Main Lobby Exit",
                    new[] { "primary" },
                    new[] { new MetadataFieldData { key = "assembly_point", value = "north-lawn" } }),
                Is.True);

            Assert.That(
                inspector.UpdateObstacle(
                    obstacleId,
                    new Vector2(8.75f, 1.5f),
                    new Vector2(1.5f, 2f),
                    17f,
                    0.5f,
                    0.5f,
                    "Movable Chairs",
                    new[] { "temporary" },
                    new[] { new MetadataFieldData { key = "hazard_link", value = "smoke-control" } }),
                Is.True);

            workspaceService.SetActiveFloor(project.floors[0].floorId);
            Assert.That(
                inspector.UpdateStairPortal(
                    lowerStairPortalId,
                    new Vector2(10.5f, 1.75f),
                    new Vector2(1.75f, 1.25f),
                    90f,
                    "North Stair Lower",
                    StairTraversalDirection.AscendOnly,
                    3f,
                    new[] { "egress-core" },
                    new[] { new MetadataFieldData { key = "orientation", value = "north" } }),
                Is.True);

            commandHistory.Undo();
            var undoneLowerPortal = workspaceService.ActiveProject.floors[0].stairPortals.Single(candidate => candidate.stairPortalId == lowerStairPortalId);
            Assert.That(undoneLowerPortal.name, Is.EqualTo(string.Empty));
            Assert.That(undoneLowerPortal.rotationDegrees, Is.EqualTo(0f).Within(0.001f));

            commandHistory.Redo();

            var floor1 = workspaceService.ActiveProject.floors.Single(candidate => candidate.floorId == project.floors[0].floorId);
            var floor2 = workspaceService.ActiveProject.floors.Single(candidate => candidate.floorId == "floor-2");
            var door = floor1.doors.Single(candidate => candidate.doorId == doorId);
            var window = floor1.windows.Single(candidate => candidate.windowId == windowId);
            var exitZone = floor1.exits.Single(candidate => candidate.exitZoneId == exitZoneId);
            var obstacle = floor1.obstacles.Single(candidate => candidate.obstacleId == obstacleId);
            var lowerPortal = floor1.stairPortals.Single(candidate => candidate.stairPortalId == lowerStairPortalId);
            var upperPortal = floor2.stairPortals.Single(candidate => candidate.stairPortalId == upperStairPortalId);

            Assert.That(door.state, Is.EqualTo(DoorState.Locked));
            Assert.That(door.tags, Is.EquivalentTo(new[] { "staff-only", "egress" }));
            Assert.That(door.metadataFields.Any(field => field.key == "badge_required" && field.value == "true"), Is.True);
            Assert.That(window.canBeUsedForEscape, Is.True);
            Assert.That(window.escapeCost, Is.EqualTo(4f).Within(0.001f));
            Assert.That(window.escapeRiskMultiplier, Is.EqualTo(1.8f).Within(0.001f));
            Assert.That(window.metadataFields.Any(field => field.key == "floor_risk" && field.value == "high"), Is.True);
            Assert.That(exitZone.capacity, Is.EqualTo(80f).Within(0.001f));
            Assert.That(exitZone.priority, Is.EqualTo(3f).Within(0.001f));
            Assert.That(exitZone.name, Is.EqualTo("Main Lobby Exit"));
            Assert.That(obstacle.discourageWeight, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(obstacle.movementSpeedPenalty, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(obstacle.rotationDegrees, Is.EqualTo(15f).Within(0.001f));
            Assert.That(obstacle.metadataFields.Any(field => field.key == "hazard_link" && field.value == "smoke-control"), Is.True);
            Assert.That(lowerPortal.sourceFloorId, Is.EqualTo(project.floors[0].floorId));
            Assert.That(lowerPortal.targetFloorId, Is.EqualTo("floor-2"));
            Assert.That(lowerPortal.targetStairPortalId, Is.EqualTo(upperStairPortalId));
            Assert.That(lowerPortal.name, Is.EqualTo("North Stair Lower"));
            Assert.That(lowerPortal.size, Is.EqualTo(new Vector2(1.75f, 1.25f)));
            Assert.That(upperPortal.targetFloorId, Is.EqualTo(project.floors[0].floorId));
            Assert.That(upperPortal.targetStairPortalId, Is.EqualTo(lowerStairPortalId));
            Assert.That(upperPortal.direction, Is.EqualTo(StairTraversalDirection.DescendOnly));

            var savePath = Path.Combine(Path.GetTempPath(), $"sandbox_phase6_{SandboxId.NewId()}.json");
            try
            {
                Assert.That(saveLoadService.SaveActiveProjectToPath(savePath, false), Is.True);
                var loadedProject = saveLoadService.LoadProjectFromPath(savePath);
                Assert.That(loadedProject, Is.Not.Null);

                var reloadedFloor1 = loadedProject.floors.Single(candidate => candidate.floorId == project.floors[0].floorId);
                var reloadedFloor2 = loadedProject.floors.Single(candidate => candidate.floorId == "floor-2");
                var reloadedDoor = reloadedFloor1.doors.Single(candidate => candidate.doorId == doorId);
                var reloadedWindow = reloadedFloor1.windows.Single(candidate => candidate.windowId == windowId);
                var reloadedExit = reloadedFloor1.exits.Single(candidate => candidate.exitZoneId == exitZoneId);
                var reloadedObstacle = reloadedFloor1.obstacles.Single(candidate => candidate.obstacleId == obstacleId);
                var reloadedLowerPortal = reloadedFloor1.stairPortals.Single(candidate => candidate.stairPortalId == lowerStairPortalId);
                var reloadedUpperPortal = reloadedFloor2.stairPortals.Single(candidate => candidate.stairPortalId == upperStairPortalId);

                Assert.That(reloadedDoor.state, Is.EqualTo(DoorState.Locked));
                Assert.That(reloadedDoor.metadataFields.Any(field => field.key == "badge_required" && field.value == "true"), Is.True);
                Assert.That(reloadedWindow.canBeUsedForEscape, Is.True);
                Assert.That(reloadedWindow.escapeCost, Is.EqualTo(4f).Within(0.001f));
                Assert.That(reloadedExit.name, Is.EqualTo("Main Lobby Exit"));
                Assert.That(reloadedExit.capacity, Is.EqualTo(80f).Within(0.001f));
                Assert.That(reloadedObstacle.discourageWeight, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(reloadedObstacle.movementSpeedPenalty, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(reloadedObstacle.rotationDegrees, Is.EqualTo(15f).Within(0.001f));
                Assert.That(reloadedLowerPortal.sourceFloorId, Is.EqualTo(project.floors[0].floorId));
                Assert.That(reloadedLowerPortal.targetFloorId, Is.EqualTo("floor-2"));
                Assert.That(reloadedLowerPortal.targetStairPortalId, Is.EqualTo(upperStairPortalId));
                Assert.That(reloadedLowerPortal.size, Is.EqualTo(new Vector2(1.75f, 1.25f)));
                Assert.That(reloadedUpperPortal.direction, Is.EqualTo(StairTraversalDirection.DescendOnly));
            }
            finally
            {
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }

            Object.DestroyImmediate(inspectorObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void Bootstrap_InstallsSemanticAuthoringServicesAndRenderers()
        {
            var systems = new GameObject("Systems");
            systems.AddComponent<SandboxEditorInstaller>();

            var world = new GameObject("World");
            new GameObject("BlueprintRoot").transform.SetParent(world.transform);
            new GameObject("GridRoot").transform.SetParent(world.transform);
            new GameObject("FloorRoot").transform.SetParent(world.transform);
            new GameObject("RuntimeOverlayRoot").transform.SetParent(world.transform);

            var ui = new GameObject("UI");
            new GameObject("TopBar").transform.SetParent(ui.transform);
            new GameObject("LeftToolPanel").transform.SetParent(ui.transform);
            new GameObject("RightInspectorPanel").transform.SetParent(ui.transform);
            new GameObject("BottomStatusBar").transform.SetParent(ui.transform);
            new GameObject("FloorTabsBar").transform.SetParent(ui.transform);
            new GameObject("ValidationPanelRoot").transform.SetParent(ui.transform);
            new GameObject("ModalRoot").transform.SetParent(ui.transform);

            var overlayRoot = new GameObject("OverlayRoot");
            var debugRoot = new GameObject("DebugRoot");

            systems.SendMessage("Awake");

            Assert.That(systems.GetComponent<SandboxSemanticObjectAuthoringService>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxSemanticObjectAuthoringOverlay>(), Is.Not.Null);
            Assert.That(world.transform.Find("SemanticRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("SemanticRoot").GetComponent<SandboxSemanticObjectRenderer>(), Is.Not.Null);

            Object.DestroyImmediate(systems);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(overlayRoot);
            Object.DestroyImmediate(debugRoot);
        }

        [Test]
        public void TeleportPortal_CanTargetAnotherFloorDirectly()
        {
            var host = CreateSemanticHost(
                out var workspaceService,
                out _,
                out _,
                out var semanticObjectAuthoringService,
                out _);

            var project = workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            project.floors.Add(new FloorData
            {
                floorId = "floor-2",
                name = "Floor 2",
                order = 1,
                elevation = 3f,
            });
            SandboxProjectDataUtility.EnsureIds(project);
            workspaceService.SetActiveProject(project);
            workspaceService.SetActiveFloor(project.floors[0].floorId);

            Assert.That(
                semanticObjectAuthoringService.PlaceTeleportPortal(
                    new Vector2(4f, 2f),
                    out var sourcePortalId,
                    "pair-1",
                    0),
                Is.True);
            Assert.That(semanticObjectAuthoringService.SetTeleportTargetFloor(sourcePortalId, "floor-2"), Is.True);

            var sourceFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == project.floors[0].floorId);
            var targetFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == "floor-2");
            var sourcePortal = sourceFloor.teleportPortals.Single(portal => portal.teleportPortalId == sourcePortalId);
            var targetPortal = targetFloor.teleportPortals.Single();

            Assert.That(sourcePortal.targetFloorId, Is.EqualTo("floor-2"));
            Assert.That(sourcePortal.targetTeleportPortalId, Is.EqualTo(targetPortal.teleportPortalId));
            Assert.That(targetPortal.targetFloorId, Is.EqualTo(sourceFloor.floorId));
            Assert.That(targetPortal.targetTeleportPortalId, Is.EqualTo(sourcePortalId));
            Assert.That(targetPortal.localPosition, Is.EqualTo(sourcePortal.localPosition));

            Object.DestroyImmediate(host);
        }

        private static GameObject CreateSemanticHost(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxCommandHistory commandHistory,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxSemanticObjectAuthoringService semanticObjectAuthoringService,
            out SandboxSaveLoadService saveLoadService)
        {
            var host = new GameObject("SemanticHost");
            saveLoadService = host.AddComponent<SandboxSaveLoadService>();
            commandHistory = host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxWallSnappingService>();
            host.AddComponent<SandboxColliderRebuildService>();
            host.AddComponent<SandboxValidationService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();

            workspaceService.SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            host.GetComponent<SandboxColliderRebuildService>().SendMessage("Awake");
            host.GetComponent<SandboxValidationService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");

            return host;
        }
    }
}
