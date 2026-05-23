using System;
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
using Object = UnityEngine.Object;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase7FloorManagementTests
    {
        [Test]
        public void FloorManagement_DuplicatesAndReordersFloorsWithoutReusingIds()
        {
            var host = CreatePhase7Host(
                out var workspaceService,
                out var floorManagementService,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(1.5f, 0.1f), out var sourceDoorId, 1f), Is.True);
            workspaceService.ActiveFloor.regions.Add(new RegionData
            {
                regionId = "region-source",
                floorId = workspaceService.ActiveFloor.floorId,
                name = "Assembly",
                polygonPoints = { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f) }
            });

            Assert.That(floorManagementService.AddFloor(out var secondFloorId, "Level 2", 3f), Is.True);
            Assert.That(floorManagementService.DuplicateFloor(workspaceService.ActiveProject.floors[0].floorId, out var duplicateFloorId), Is.True);

            var duplicatedFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == duplicateFloorId);
            var sourceFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId != duplicateFloorId && floor.name == "Floor 1");

            Assert.That(duplicatedFloor.floorId, Is.Not.EqualTo(sourceFloor.floorId));
            Assert.That(duplicatedFloor.wallSegments.Count, Is.EqualTo(sourceFloor.wallSegments.Count));
            Assert.That(duplicatedFloor.wallSegments[0].wallSegmentId, Is.Not.EqualTo(sourceFloor.wallSegments[0].wallSegmentId));
            Assert.That(duplicatedFloor.wallSegments[0].startPoint, Is.EqualTo(sourceFloor.wallSegments[0].startPoint));
            Assert.That(duplicatedFloor.doors[0].doorId, Is.Not.EqualTo(sourceDoorId));
            Assert.That(duplicatedFloor.doors[0].wallSegmentId, Is.EqualTo(duplicatedFloor.wallSegments[0].wallSegmentId));
            Assert.That(duplicatedFloor.regions[0].regionId, Is.Not.EqualTo(sourceFloor.regions[0].regionId));
            Assert.That(duplicatedFloor.name, Does.Contain("Copy"));

            Assert.That(floorManagementService.UpdateFloorMetadata(duplicateFloorId, "Duplicate Level", 0, 6f), Is.True);
            var orderedFloors = floorManagementService.GetOrderedFloors().ToArray();
            Assert.That(orderedFloors[0].floorId, Is.EqualTo(duplicateFloorId));
            Assert.That(orderedFloors[0].name, Is.EqualTo("Duplicate Level"));
            Assert.That(orderedFloors[0].elevation, Is.EqualTo(6f).Within(0.001f));
            Assert.That(orderedFloors.Select(floor => floor.order), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(orderedFloors.Any(floor => floor.floorId == secondFloorId), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void FloorDeletion_UsesConfirmationAndRevalidatesBrokenReferences()
        {
            var host = CreatePhase7Host(
                out var workspaceService,
                out var floorManagementService,
                out _,
                out _,
                out var semanticObjectAuthoringService,
                out _,
                out var validationService,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var firstFloorId = workspaceService.ActiveFloor.floorId;
            Assert.That(floorManagementService.AddFloor(out var secondFloorId, "Level 2", 3f), Is.True);

            workspaceService.SetActiveFloor(firstFloorId);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(1f, 1f), out var lowerPortalId), Is.True);
            workspaceService.SetActiveFloor(secondFloorId);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(1f, 1f), out var upperPortalId), Is.True);
            Assert.That(semanticObjectAuthoringService.LinkStairPortals(firstFloorId, lowerPortalId, secondFloorId, upperPortalId, StairTraversalDirection.Bidirectional, 2f), Is.True);

            var project = workspaceService.ActiveProject;
            project.spawnLayouts.Add(new SpawnLayoutData
            {
                spawnLayoutId = "spawn-layout-1",
                name = "Upper Occupants",
                spawnPoints =
                {
                    new SpawnPointData { spawnPointId = "spawn-point-upper", floorId = secondFloorId, position = new Vector2(2f, 2f) }
                }
            });
            project.fireOrigins.Add(new FireOriginData
            {
                fireOriginId = "fire-upper",
                floorId = secondFloorId,
                position = new Vector2(3f, 3f)
            });
            project.scenarioPresets.Add(new ScenarioPresetData
            {
                scenarioPresetId = "scenario-upper",
                name = "Upper Floor Drill",
                spawnLayoutIds = { "spawn-layout-1" },
                fireOriginIds = { "fire-upper" }
            });
            workspaceService.SetActiveProject(project);

            Assert.That(floorManagementService.RequestDeleteFloor(secondFloorId), Is.False);
            Assert.That(floorManagementService.HasPendingDeleteConfirmation, Is.True);
            Assert.That(floorManagementService.PendingDeleteMessage, Does.Contain("stair links"));
            Assert.That(floorManagementService.PendingDeleteMessage, Does.Contain("scenarios"));

            Assert.That(floorManagementService.ConfirmPendingDeleteFloor(), Is.True);
            Assert.That(workspaceService.ActiveProject.floors.Any(floor => floor.floorId == secondFloorId), Is.False);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid stair link"), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid spawn floor reference"), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid fire origin floor reference"), Is.True);
            Assert.That(validationService.Issues.Any(issue => issue.title == "Invalid scenario floor dependency"), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void VisualOrganization_HidesLocksAndPublishesLegendEntries()
        {
            var host = CreatePhase7Host(
                out var workspaceService,
                out _,
                out var visualOrganizationService,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(1.5f, 0.1f), out var doorId, 1f), Is.True);
            workspaceService.ActiveFloor.regions.Add(new RegionData
            {
                regionId = "region-1",
                floorId = workspaceService.ActiveFloor.floorId,
                polygonPoints = { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f) }
            });
            workspaceService.ActiveProject.spawnLayouts.Add(new SpawnLayoutData
            {
                spawnLayoutId = "spawn-layout-2",
                spawnPoints =
                {
                    new SpawnPointData
                    {
                        spawnPointId = "spawn-point-1",
                        floorId = workspaceService.ActiveFloor.floorId,
                        position = new Vector2(3f, 1f)
                    }
                }
            });
            workspaceService.SetActiveProject(workspaceService.ActiveProject);

            var world = new GameObject("World");
            var wallRoot = new GameObject("WallRoot");
            wallRoot.transform.SetParent(world.transform);
            new GameObject("HandleRoot").transform.SetParent(world.transform);
            var semanticRoot = new GameObject("SemanticRoot");
            semanticRoot.transform.SetParent(world.transform);

            var wallRenderer = wallRoot.AddComponent<SandboxWallOverlayRenderer>();
            wallRenderer.SendMessage("Awake");
            var semanticRenderer = semanticRoot.AddComponent<SandboxSemanticObjectRenderer>();
            semanticRenderer.SendMessage("Awake");

            var legendObject = new GameObject("Legend");
            var legendShell = legendObject.AddComponent<SandboxVisualLegendShell>();
            legendShell.SendMessage("Awake");

            Assert.That(wallRoot.transform.childCount, Is.GreaterThan(0));
            Assert.That(semanticRoot.transform.childCount, Is.GreaterThan(0));

            visualOrganizationService.SetTypeVisibility(SandboxVisualObjectType.Wall, false);
            Assert.That(wallRoot.transform.childCount, Is.EqualTo(0));

            selectionService.ReplaceSelection(new[] { doorId });
            visualOrganizationService.HideCurrentSelection(true);
            Assert.That(semanticRoot.transform.Cast<Transform>().Any(child => child.name.StartsWith("Door_", System.StringComparison.Ordinal)), Is.False);

            visualOrganizationService.SetTypeLocked(SandboxVisualObjectType.Wall, true);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(10f, 0f), new Vector2(14f, 0f), 0.25f), Is.False);

            Assert.That(legendShell.LegendEntries.Count, Is.EqualTo(8));
            Assert.That(legendShell.LegendEntries.Any(entry => entry.label == "Spawns"), Is.True);
            Assert.That(legendShell.LegendEntries.Any(entry => entry.label == "Regions"), Is.True);

            Object.DestroyImmediate(legendObject);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(host);
        }

        private static GameObject CreatePhase7Host(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxFloorManagementService floorManagementService,
            out SandboxVisualOrganizationService visualOrganizationService,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxSemanticObjectAuthoringService semanticObjectAuthoringService,
            out SandboxColliderRebuildService colliderRebuildService,
            out SandboxValidationService validationService,
            out SandboxSelectionService selectionService)
        {
            var host = new GameObject("Phase7Host");
            host.AddComponent<SandboxSaveLoadService>();
            host.AddComponent<SandboxCommandHistory>();
            selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            validationService = host.AddComponent<SandboxValidationService>();
            floorManagementService = host.AddComponent<SandboxFloorManagementService>();
            visualOrganizationService = host.AddComponent<SandboxVisualOrganizationService>();
            host.AddComponent<SandboxWallSnappingService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();

            workspaceService.SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            floorManagementService.SendMessage("Awake");
            visualOrganizationService.SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            return host;
        }
    }
}
