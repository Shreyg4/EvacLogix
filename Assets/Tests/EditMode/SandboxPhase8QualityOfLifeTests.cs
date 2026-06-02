using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase8QualityOfLifeTests
    {
        [Test]
        public void FloorManagement_UsesUndoRedoForMetadataEdits()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out var commandHistory,
                out var floorManagementService,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var floorId = workspaceService.ActiveFloor.floorId;

            Assert.That(floorManagementService.UpdateFloorMetadata(floorId, "Ground Floor", 0, 2.5f), Is.True);
            Assert.That(workspaceService.ActiveFloor.name, Is.EqualTo("Ground Floor"));
            Assert.That(workspaceService.ActiveFloor.elevation, Is.EqualTo(2.5f).Within(0.001f));

            commandHistory.Undo();
            Assert.That(workspaceService.ActiveFloor.name, Is.EqualTo("Floor 1"));
            Assert.That(workspaceService.ActiveFloor.elevation, Is.EqualTo(0f).Within(0.001f));

            commandHistory.Redo();
            Assert.That(workspaceService.ActiveFloor.name, Is.EqualTo("Ground Floor"));
            Assert.That(workspaceService.ActiveFloor.elevation, Is.EqualTo(2.5f).Within(0.001f));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ClipboardService_LimitsBatchEditsToSafeObjectTypes()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out var clipboardService,
                out _,
                out _,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f), 0.25f), Is.True);
            var wallId = workspaceService.ActiveFloor.wallSegments[0].wallSegmentId;
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(2f, 2f), out var exitId, new Vector2(2f, 1f), 0f, 1.5f, 25f, 1f, "North Exit"), Is.True);

            selectionService.ReplaceSelection(new[] { wallId, exitId });
            Assert.That(clipboardService.CopySelection(), Is.True);
            Assert.That(clipboardService.PasteSelection(new Vector2(3f, 0f)), Is.True);
            Assert.That(workspaceService.ActiveFloor.wallSegments.Count, Is.EqualTo(1));
            Assert.That(workspaceService.ActiveFloor.exits.Count, Is.EqualTo(2));

            selectionService.ReplaceSelection(new[] { wallId, exitId });
            Assert.That(clipboardService.DeleteSelection(), Is.True);
            Assert.That(workspaceService.ActiveFloor.wallSegments.Count, Is.EqualTo(1));
            Assert.That(workspaceService.ActiveFloor.exits.Count, Is.EqualTo(1));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ClipboardService_CutSelectionCopiesThenDeletes()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out var clipboardService,
                out _,
                out _,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f), 0.25f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(2f, 2f), out var exitId, new Vector2(2f, 1f), 0f, 1.5f, 25f, 1f, "North Exit"), Is.True);

            selectionService.ReplaceSelection(new[] { exitId });
            Assert.That(clipboardService.CutSelection(), Is.True);
            Assert.That(clipboardService.ClipboardItems.Count, Is.EqualTo(1));
            Assert.That(workspaceService.ActiveFloor.exits.Count, Is.EqualTo(0));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ClipboardService_CutTeleportPreservesLinkedEndpointOnPaste()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out var floorManagementService,
                out _,
                out var semanticObjectAuthoringService,
                out var clipboardService,
                out _,
                out _,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var sourceFloorId = workspaceService.ActiveFloorId;
            Assert.That(floorManagementService.AddFloor(out var targetFloorId, "Floor 2", 3f), Is.True);
            workspaceService.SetActiveFloor(sourceFloorId);
            Assert.That(semanticObjectAuthoringService.PlaceTeleportPortal(new Vector2(2f, 2f), out var sourcePortalId, "pair-1", 0), Is.True);
            Assert.That(semanticObjectAuthoringService.SetTeleportTargetFloor(sourcePortalId, targetFloorId), Is.True);

            selectionService.ReplaceSelection(new[] { sourcePortalId });
            Assert.That(clipboardService.CutSelection(), Is.True);
            Assert.That(workspaceService.ActiveProject.floors.Single(floor => floor.floorId == sourceFloorId).teleportPortals.Count, Is.EqualTo(0));

            Assert.That(clipboardService.PasteSelection(Vector2.zero), Is.True);
            var sourceFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == sourceFloorId);
            var targetFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == targetFloorId);
            var pastedPortal = sourceFloor.teleportPortals.Single();
            var linkedPortal = targetFloor.teleportPortals.Single();

            Assert.That(pastedPortal.teleportPortalId, Is.Not.EqualTo(sourcePortalId));
            Assert.That(pastedPortal.targetFloorId, Is.EqualTo(targetFloorId));
            Assert.That(pastedPortal.targetTeleportPortalId, Is.EqualTo(linkedPortal.teleportPortalId));
            Assert.That(linkedPortal.targetFloorId, Is.EqualTo(sourceFloorId));
            Assert.That(linkedPortal.targetTeleportPortalId, Is.EqualTo(pastedPortal.teleportPortalId));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ClipboardService_CopyTeleportDoesNotRestoreLinkedEndpointOnPaste()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out var floorManagementService,
                out _,
                out var semanticObjectAuthoringService,
                out var clipboardService,
                out _,
                out _,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var sourceFloorId = workspaceService.ActiveFloorId;
            Assert.That(floorManagementService.AddFloor(out var targetFloorId, "Floor 2", 3f), Is.True);
            workspaceService.SetActiveFloor(sourceFloorId);
            Assert.That(semanticObjectAuthoringService.PlaceTeleportPortal(new Vector2(2f, 2f), out var sourcePortalId, "pair-1", 0), Is.True);
            Assert.That(semanticObjectAuthoringService.SetTeleportTargetFloor(sourcePortalId, targetFloorId), Is.True);

            selectionService.ReplaceSelection(new[] { sourcePortalId });
            Assert.That(clipboardService.CopySelection(), Is.True);
            Assert.That(clipboardService.PasteSelection(new Vector2(1f, 0f)), Is.True);

            var sourceFloor = workspaceService.ActiveProject.floors.Single(floor => floor.floorId == sourceFloorId);
            var copiedPortal = sourceFloor.teleportPortals.Single(portal => portal.teleportPortalId != sourcePortalId);
            Assert.That(copiedPortal.targetFloorId, Is.EqualTo(string.Empty));
            Assert.That(copiedPortal.targetTeleportPortalId, Is.EqualTo(string.Empty));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void SaveLoadService_SavesLoadsAndDeletesBrowserLibraryProjects()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
            var saveLoadService = host.GetComponent<SandboxSaveLoadService>();
            var libraryRoot = Path.Combine(Application.temporaryCachePath, "EvacLogixTests", nameof(SaveLoadService_SavesLoadsAndDeletesBrowserLibraryProjects));
            if (Directory.Exists(libraryRoot))
            {
                Directory.Delete(libraryRoot, true);
            }

            saveLoadService.ConfigureProjectLibrary(libraryRoot);
            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate, "Library Project");
            var originalProjectId = workspaceService.ActiveProject.projectId;

            Assert.That(saveLoadService.SaveActiveProjectToLibrary(), Is.True);
            var savedProjects = saveLoadService.GetSavedProjects();
            Assert.That(savedProjects.Length, Is.EqualTo(1));
            Assert.That(savedProjects[0].displayName, Is.EqualTo("Library Project"));

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.BlankTemplate, "Other Project");
            Assert.That(saveLoadService.LoadProjectFromLibrary(originalProjectId), Is.Not.Null);
            Assert.That(workspaceService.ActiveProject.projectId, Is.EqualTo(originalProjectId));
            Assert.That(workspaceService.ActiveProject.metadata.buildingName, Is.EqualTo("Library Project"));

            Assert.That(saveLoadService.DeleteProjectFromLibrary(originalProjectId), Is.True);
            Assert.That(saveLoadService.GetSavedProjects(), Is.Empty);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void SaveLoadService_ImportedJsonIsUnsavedUntilUserSaves()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _);
            var saveLoadService = host.GetComponent<SandboxSaveLoadService>();

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate, "Imported Project");
            var json = saveLoadService.SerializeActiveProject();

            Assert.That(saveLoadService.LoadProjectFromJson(json), Is.Not.Null);
            Assert.That(saveLoadService.HasUnsavedChanges, Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void MeasurementAndSnapSettings_EnablePreciseEditing()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out var wallAuthoringService,
                out _,
                out _,
                out var measurementService,
                out var workspaceStateService,
                out _,
                out _,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);

            workspaceStateService.SetGridSize(1f);
            workspaceStateService.SetAngleSnapIncrementDegrees(90f);
            var wallSnappingService = host.GetComponent<SandboxWallSnappingService>();

            var gridSnap = wallSnappingService.SnapPoint(workspaceService.ActiveFloorId, new Vector2(0.92f, 0.12f), null);
            Assert.That(gridSnap.position.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(gridSnap.position.y, Is.EqualTo(0f).Within(0.001f));

            var angleSnap = wallSnappingService.SnapPoint(workspaceService.ActiveFloorId, new Vector2(2f, 0.18f), Vector2.zero);
            Assert.That(angleSnap.position.y, Is.EqualTo(0f).Within(0.001f));

            var wallId = workspaceService.ActiveFloor.wallSegments[0].wallSegmentId;
            selectionService.ReplaceSelection(new[] { wallId });
            var selectionReadout = measurementService.RefreshSelectionReadout();
            Assert.That(selectionReadout, Does.Contain("wall length 5"));

            Assert.That(measurementService.RegisterMeasurementPoint(Vector2.zero), Does.Contain("point A"));
            Assert.That(measurementService.RegisterMeasurementPoint(new Vector2(3f, 4f)), Does.Contain("Measured 5"));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void WallSnappingService_CanTemporarilyBypassSnappingForFineAdjustments()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out var wallAuthoringService,
                out _,
                out _,
                out _,
                out var workspaceStateService,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.25f), Is.True);

            workspaceStateService.SetGridSize(1f);
            var wallSnappingService = host.GetComponent<SandboxWallSnappingService>();
            var rawPoint = new Vector2(0.92f, 0.12f);

            var snappedPoint = wallSnappingService.SnapPoint(workspaceService.ActiveFloorId, rawPoint, null);
            Assert.That(snappedPoint.position.x, Is.EqualTo(1f).Within(0.001f));

            wallSnappingService.SetTemporarySnappingBypass(true);
            var unsnappedPoint = wallSnappingService.SnapPoint(workspaceService.ActiveFloorId, rawPoint, null);
            Assert.That(unsnappedPoint.targetKind, Is.EqualTo(SandboxWallSnapTargetKind.None));
            Assert.That(unsnappedPoint.position, Is.EqualTo(rawPoint));

            wallSnappingService.SetTemporarySnappingBypass(false);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void MeasurementOverlay_ShowsVisualAidAcrossMeasureWorkflow()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out _,
                out _,
                out _,
                out _,
                out _,
                out var measurementService,
                out _,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var toolStateService = host.GetComponent<SandboxToolStateService>();

            var overlayObject = new GameObject("MeasurementOverlay");
            var overlay = overlayObject.AddComponent<SandboxMeasurementOverlay>();
            overlay.SendMessage("Awake");
            var inspectorObject = new GameObject("Inspector");
            var inspector = inspectorObject.AddComponent<SandboxInspectorPanelShell>();
            inspector.SendMessage("Awake");

            Assert.That(overlay.IsVisualAidVisible, Is.False);
            Assert.That(inspector.HasActiveMeasurement, Is.False);

            toolStateService.RequestToolModeChange(SandboxToolMode.Measure);
            Assert.That(overlay.IsVisualAidVisible, Is.True);
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Click measurement point A."));

            Assert.That(measurementService.RegisterMeasurementPoint(Vector2.zero), Does.Contain("point A"));
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Click measurement point B."));
            Assert.That(inspector.HasActiveMeasurement, Is.True);

            Assert.That(measurementService.RegisterMeasurementPoint(new Vector2(3f, 4f)), Does.Contain("Measured 5"));
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Measurement captured. Click again to update point B, or use Clear Measure to restart."));

            measurementService.ClearMeasurement();
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Click measurement point A."));
            Assert.That(inspector.HasActiveMeasurement, Is.False);

            toolStateService.RequestToolModeChange(SandboxToolMode.Select);
            Assert.That(overlay.IsVisualAidVisible, Is.False);

            Object.DestroyImmediate(inspectorObject);
            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void OverviewOnboardingDiagnosticsAndScenarioNaming_AreWiredIntoQoLFlow()
        {
            var host = CreatePhase8Host(
                out var workspaceService,
                out var commandHistory,
                out _,
                out var wallAuthoringService,
                out var semanticObjectAuthoringService,
                out _,
                out _,
                out var workspaceStateService,
                out var editorQoLService,
                out var scenarioManagementService,
                out var selectionService);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(8f, 0f), 0.25f), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceExit(new Vector2(7f, 1.5f), out var exitId, new Vector2(2f, 1f), 0f, 1.5f, 50f, 1f, "South Exit"), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceObstacle(new Vector2(2f, 2f), out var obstacleId, new Vector2(1f, 1f), 0f, 1f, 0f, "Display Kiosk"), Is.True);

            workspaceService.ActiveProject.scenarioPresets.Add(new ScenarioPresetData
            {
                scenarioPresetId = "scenario-1",
                name = "Initial Drill"
            });
            workspaceService.SetActiveProject(workspaceService.ActiveProject);

            var cameraObject = new GameObject("Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<Camera>().orthographic = true;
            var cameraController = cameraObject.AddComponent<SandboxCameraController>();
            cameraController.SendMessage("Awake");

            workspaceStateService.SetGridSize(1f);
            var gridRoot = new GameObject("GridRoot");
            var gridRenderer = gridRoot.AddComponent<SandboxGridOverlayRenderer>();
            gridRenderer.SendMessage("Awake");

            var overlayRoot = new GameObject("OverlayRoot");
            var overviewNavigator = overlayRoot.AddComponent<SandboxOverviewNavigator>();
            overviewNavigator.SendMessage("Awake");
            var onboardingOverlay = overlayRoot.AddComponent<SandboxOnboardingOverlayShell>();
            onboardingOverlay.SendMessage("Awake");

            var debugRoot = new GameObject("DiagnosticsOverlayRoot");
            var diagnosticsRenderer = debugRoot.AddComponent<SandboxDiagnosticsOverlayRenderer>();
            diagnosticsRenderer.SendMessage("Awake");

            selectionService.ReplaceSelection(new[] { exitId });
            Assert.That(overviewNavigator.FocusOnSelection(), Is.True);
            Assert.That(overviewNavigator.WorldBounds.width, Is.GreaterThan(0f));
            Assert.That(cameraObject.transform.position.x, Is.EqualTo(7f).Within(0.5f));
            Assert.That(gridRoot.transform.childCount, Is.GreaterThan(0));

            workspaceStateService.SetGridVisibility(false);
            Assert.That(gridRoot.transform.childCount, Is.EqualTo(0));

            editorQoLService.SetDebugOverlayState(true, false, true, true);
            diagnosticsRenderer.Refresh();
            Assert.That(debugRoot.transform.childCount, Is.GreaterThan(0));

            editorQoLService.SetIsolateSelectedObjects(true);
            Assert.That(editorQoLService.IsObjectVisibleForIsolation(exitId, SandboxVisualObjectType.Exit), Is.True);
            Assert.That(editorQoLService.IsObjectVisibleForIsolation(obstacleId, SandboxVisualObjectType.Obstacle), Is.False);

            var toolStateService = host.GetComponent<SandboxToolStateService>();
            toolStateService.RequestToolModeChange(SandboxToolMode.WallLine, commandHistory);
            Assert.That(onboardingOverlay.ToolHelpText, Does.Contain("wall centerlines"));
            Assert.That(onboardingOverlay.ValidationHelpText, Is.Not.Empty);

            Assert.That(scenarioManagementService.RenameScenarioPreset("scenario-1", "Evening Drill"), Is.True);
            Assert.That(workspaceService.ActiveProject.scenarioPresets.Single().name, Is.EqualTo("Evening Drill"));
            commandHistory.Undo();
            Assert.That(workspaceService.ActiveProject.scenarioPresets.Single().name, Is.EqualTo("Initial Drill"));

            Object.DestroyImmediate(gridRoot);
            Object.DestroyImmediate(debugRoot);
            Object.DestroyImmediate(overlayRoot);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(host);
        }

        private static GameObject CreatePhase8Host(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxCommandHistory commandHistory,
            out SandboxFloorManagementService floorManagementService,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxSemanticObjectAuthoringService semanticObjectAuthoringService,
            out SandboxClipboardService clipboardService,
            out SandboxMeasurementService measurementService,
            out SandboxWorkspaceStateService workspaceStateService,
            out SandboxEditorQoLService editorQoLService,
            out SandboxScenarioManagementService scenarioManagementService,
            out SandboxSelectionService selectionService)
        {
            var host = new GameObject("Phase8Host");
            host.AddComponent<SandboxSaveLoadService>();
            commandHistory = host.AddComponent<SandboxCommandHistory>();
            selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxToolStateService>();
            workspaceStateService = host.AddComponent<SandboxWorkspaceStateService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            var colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            var validationService = host.AddComponent<SandboxValidationService>();
            floorManagementService = host.AddComponent<SandboxFloorManagementService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            clipboardService = host.AddComponent<SandboxClipboardService>();
            measurementService = host.AddComponent<SandboxMeasurementService>();
            editorQoLService = host.AddComponent<SandboxEditorQoLService>();
            scenarioManagementService = host.AddComponent<SandboxScenarioManagementService>();
            var wallSnappingService = host.AddComponent<SandboxWallSnappingService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();

            workspaceService.SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            floorManagementService.SendMessage("Awake");
            host.GetComponent<SandboxVisualOrganizationService>().SendMessage("Awake");
            clipboardService.SendMessage("Awake");
            measurementService.SendMessage("Awake");
            editorQoLService.SendMessage("Awake");
            scenarioManagementService.SendMessage("Awake");
            wallSnappingService.SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            return host;
        }
    }
}
