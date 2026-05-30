using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using EvacLogix.Sandbox.UI.Shortcuts;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxEditorFrameworkTests
    {
        [Test]
        public void CommandHistory_UndoAndRedoRestoresState()
        {
            var value = 1;
            var history = new GameObject("History").AddComponent<SandboxCommandHistory>();

            history.Execute(new DelegateSandboxEditorCommand(
                "Set Value",
                () => value = 2,
                () => value = 1));

            Assert.That(value, Is.EqualTo(2));
            Assert.That(history.CanUndo, Is.True);

            history.Undo();
            Assert.That(value, Is.EqualTo(1));
            Assert.That(history.CanRedo, Is.True);

            history.Redo();
            Assert.That(value, Is.EqualTo(2));

            Object.DestroyImmediate(history.gameObject);
        }

        [Test]
        public void ToolState_UsesCommandHistoryForToolSwitches()
        {
            var host = new GameObject("ToolHost");
            var history = host.AddComponent<SandboxCommandHistory>();
            var toolState = host.AddComponent<SandboxToolStateService>();

            toolState.RequestToolModeChange(SandboxToolMode.WallLine, history);
            Assert.That(toolState.CurrentToolMode, Is.EqualTo(SandboxToolMode.WallLine));

            history.Undo();
            Assert.That(toolState.CurrentToolMode, Is.EqualTo(SandboxToolMode.Select));

            history.Redo();
            Assert.That(toolState.CurrentToolMode, Is.EqualTo(SandboxToolMode.WallLine));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void SelectionService_UsesCommandHistoryForSelectionChanges()
        {
            var host = new GameObject("SelectionHost");
            var history = host.AddComponent<SandboxCommandHistory>();
            var selectionService = host.AddComponent<SandboxSelectionService>();

            selectionService.ReplaceSelection(new[] { "wall-a", "door-b" }, history);
            Assert.That(selectionService.SelectedObjectIds.Count, Is.EqualTo(2));

            history.Undo();
            Assert.That(selectionService.SelectedObjectIds.Count, Is.EqualTo(0));

            history.Redo();
            Assert.That(selectionService.SelectedObjectIds.SequenceEqual(new[] { "wall-a", "door-b" }), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void KeyboardShortcutService_SeedsExpectedDefaultBindings()
        {
            var host = new GameObject("ShortcutHost");
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxToolStateService>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxWorkspaceStateService>();
            var shortcutService = host.AddComponent<SandboxKeyboardShortcutService>();

            shortcutService.EnsureDefaultBindings();

            Assert.That(shortcutService.Bindings.Any(binding => binding.shortcutId == SandboxShortcutId.Undo), Is.True);
            Assert.That(shortcutService.Bindings.Any(binding => binding.shortcutId == SandboxShortcutId.WallLineTool), Is.True);
            Assert.That(shortcutService.Bindings.Any(binding => binding.shortcutId == SandboxShortcutId.ToggleGrid), Is.True);
            Assert.That(shortcutService.Bindings.Any(binding => binding.shortcutId == SandboxShortcutId.CutSelection), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void KeyboardShortcutService_AllowsUndoWhilePointerIsOverUi()
        {
            var host = new GameObject("ShortcutHost");
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxToolStateService>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxWorkspaceStateService>();
            var shortcutService = host.AddComponent<SandboxKeyboardShortcutService>();

            shortcutService.SendMessage("Awake");

            Assert.That(shortcutService.CanDispatchWhilePointerOverUi(SandboxShortcutId.Undo), Is.True);
            Assert.That(shortcutService.CanDispatchWhilePointerOverUi(SandboxShortcutId.Redo), Is.True);
            Assert.That(shortcutService.CanDispatchWhilePointerOverUi(SandboxShortcutId.WallLineTool), Is.False);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ToolPaletteShell_CanSwitchToolsThroughUiEntryPoint()
        {
            var servicesHost = new GameObject("ServicesHost");
            var history = servicesHost.AddComponent<SandboxCommandHistory>();
            var toolState = servicesHost.AddComponent<SandboxToolStateService>();

            var paletteObject = new GameObject("Palette");
            var palette = paletteObject.AddComponent<SandboxToolPaletteShell>();

            palette.SendMessage("Awake");
            palette.SelectTool(SandboxToolMode.Door);

            Assert.That(toolState.CurrentToolMode, Is.EqualTo(SandboxToolMode.Door));
            Assert.That(history.CanUndo, Is.True);
            Assert.That(palette.IsToolActive(SandboxToolMode.Door), Is.True);

            Object.DestroyImmediate(paletteObject);
            Object.DestroyImmediate(servicesHost);
        }

        [Test]
        public void InputRouter_ResolvesPreviewAndHandleTargetsBeforeWorld()
        {
            var host = new GameObject("InputHost");
            var router = host.AddComponent<SandboxInputRouter>();

            router.SetPointerOverHandle(true);
            Assert.That(router.ResolvePointerTarget(Vector2.zero), Is.EqualTo(SandboxInputTarget.Handle));

            router.SetPreviewOverlayCapturingInput(true);
            Assert.That(router.ResolvePointerTarget(Vector2.zero), Is.EqualTo(SandboxInputTarget.PreviewOverlay));

            router.SetPreviewOverlayCapturingInput(false);
            router.SetPointerOverHandle(false);
            Assert.That(router.ResolvePointerTarget(Vector2.zero), Is.EqualTo(SandboxInputTarget.World));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ObjectInteractionOverlay_SelectsAndErasesWallsAndOpenings()
        {
            var host = new GameObject("InteractionHost");
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxToolStateService>();
            var selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxColliderRebuildService>();
            host.AddComponent<SandboxValidationService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            host.AddComponent<SandboxClipboardService>();
            host.AddComponent<SandboxWallSnappingService>();
            var wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            var semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();
            host.AddComponent<SandboxMeasurementService>();

            host.GetComponent<SandboxProjectWorkspaceService>().SendMessage("Awake");
            host.GetComponent<SandboxColliderRebuildService>().SendMessage("Awake");
            host.GetComponent<SandboxValidationService>().SendMessage("Awake");
            host.GetComponent<SandboxVisualOrganizationService>().SendMessage("Awake");
            host.GetComponent<SandboxClipboardService>().SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            host.GetComponent<SandboxMeasurementService>().SendMessage("Awake");

            var workspaceService = host.GetComponent<SandboxProjectWorkspaceService>();
            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f)), Is.True);
            Assert.That(semanticObjectAuthoringService.PlaceDoor(new Vector2(2f, 0f), out var doorId), Is.True);

            var overlayObject = new GameObject("InteractionOverlay");
            var overlay = overlayObject.AddComponent<SandboxObjectInteractionOverlay>();
            overlay.SendMessage("Awake");

            Assert.That(overlay.SelectAtWorldPoint(new Vector2(2f, 0f)), Is.True);
            Assert.That(selectionService.SelectedObjectIds.Single(), Is.EqualTo(doorId));

            Assert.That(overlay.EraseAtWorldPoint(new Vector2(2f, 0f)), Is.True);
            Assert.That(workspaceService.ActiveFloor.doors.Count, Is.EqualTo(0));

            Assert.That(overlay.SelectAtWorldPoint(new Vector2(4.5f, 0f)), Is.True);
            Assert.That(selectionService.SelectedObjectIds.Single(), Is.EqualTo(workspaceService.ActiveFloor.wallSegments[0].wallSegmentId));

            Assert.That(overlay.EraseAtWorldPoint(new Vector2(4.5f, 0f)), Is.True);
            Assert.That(workspaceService.ActiveFloor.wallSegments.Count, Is.EqualTo(0));

            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void ObjectInteractionOverlay_CanDragMoveSelectedStairPortal()
        {
            var host = new GameObject("StairMoveHost");
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxToolStateService>();
            var selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxColliderRebuildService>();
            host.AddComponent<SandboxValidationService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            host.AddComponent<SandboxClipboardService>();
            host.AddComponent<SandboxWallSnappingService>();
            host.AddComponent<SandboxWallAuthoringService>();
            var semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();
            host.AddComponent<SandboxMeasurementService>();

            host.GetComponent<SandboxProjectWorkspaceService>().SendMessage("Awake");
            host.GetComponent<SandboxColliderRebuildService>().SendMessage("Awake");
            host.GetComponent<SandboxValidationService>().SendMessage("Awake");
            host.GetComponent<SandboxVisualOrganizationService>().SendMessage("Awake");
            host.GetComponent<SandboxClipboardService>().SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            host.GetComponent<SandboxWallAuthoringService>().SendMessage("Awake");
            semanticObjectAuthoringService.SendMessage("Awake");
            host.GetComponent<SandboxMeasurementService>().SendMessage("Awake");

            var workspaceService = host.GetComponent<SandboxProjectWorkspaceService>();
            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(semanticObjectAuthoringService.PlaceStairPortal(new Vector2(2f, 2f), out var stairId), Is.True);
            selectionService.ReplaceSelection(new[] { stairId });

            var overlayObject = new GameObject("InteractionOverlay");
            var overlay = overlayObject.AddComponent<SandboxObjectInteractionOverlay>();
            overlay.SendMessage("Awake");

            Assert.That(overlay.BeginSelectionDrag(stairId, new Vector2(2f, 2f)), Is.True);
            overlay.UpdateSelectionDragPreview(new Vector2(4f, 3f));
            Assert.That(overlay.CommitSelectionDrag(), Is.True);

            var stairPortal = workspaceService.ActiveFloor.stairPortals.Single(portal => portal.stairPortalId == stairId);
            Assert.That(stairPortal.localPosition, Is.EqualTo(new Vector2(4f, 3f)));

            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void ObjectInteractionOverlay_BrushEraseCanRemoveWallsAndClampBrushRadius()
        {
            var host = new GameObject("BrushEraseHost");
            host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxToolStateService>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxColliderRebuildService>();
            host.AddComponent<SandboxValidationService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            host.AddComponent<SandboxClipboardService>();
            host.AddComponent<SandboxWallSnappingService>();
            var wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            host.AddComponent<SandboxSemanticObjectAuthoringService>();
            host.AddComponent<SandboxMeasurementService>();

            host.GetComponent<SandboxProjectWorkspaceService>().SendMessage("Awake");
            host.GetComponent<SandboxColliderRebuildService>().SendMessage("Awake");
            host.GetComponent<SandboxValidationService>().SendMessage("Awake");
            host.GetComponent<SandboxVisualOrganizationService>().SendMessage("Awake");
            host.GetComponent<SandboxClipboardService>().SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            host.GetComponent<SandboxSemanticObjectAuthoringService>().SendMessage("Awake");
            host.GetComponent<SandboxMeasurementService>().SendMessage("Awake");

            var workspaceService = host.GetComponent<SandboxProjectWorkspaceService>();
            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f)), Is.True);

            var overlayObject = new GameObject("BrushEraseOverlay");
            var overlay = overlayObject.AddComponent<SandboxObjectInteractionOverlay>();
            overlay.SendMessage("Awake");
            overlay.SetBrushEraseEnabled(true);
            overlay.SetEraseBrushRadius(0.1f);
            Assert.That(overlay.EraseBrushRadius, Is.EqualTo(0.35f).Within(0.001f));

            overlay.SetEraseBrushRadius(0.6f);
            Assert.That(overlay.EraseWithinBrush(new Vector2(3f, 0f)), Is.EqualTo(1));
            Assert.That(workspaceService.ActiveFloor.wallSegments.Count, Is.EqualTo(0));

            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void SandboxApp_BootstrapsEditorInstallerAndSharedServices()
        {
            var systems = new GameObject("Systems");
            systems.AddComponent<SandboxEditorInstaller>();
            systems.AddComponent<SandboxApp>();

            var world = new GameObject("World");
            new GameObject("BlueprintRoot").transform.SetParent(world.transform);
            new GameObject("GridRoot").transform.SetParent(world.transform);
            new GameObject("FloorRoot").transform.SetParent(world.transform);
            new GameObject("RuntimeOverlayRoot").transform.SetParent(world.transform);

            var ui = new GameObject("UI");
            var topBar = new GameObject("TopBar");
            topBar.transform.SetParent(ui.transform);
            var leftToolPanel = new GameObject("LeftToolPanel");
            leftToolPanel.transform.SetParent(ui.transform);
            var rightInspectorPanel = new GameObject("RightInspectorPanel");
            rightInspectorPanel.transform.SetParent(ui.transform);
            var bottomStatusBar = new GameObject("BottomStatusBar");
            bottomStatusBar.transform.SetParent(ui.transform);
            var floorTabsBar = new GameObject("FloorTabsBar");
            floorTabsBar.transform.SetParent(ui.transform);
            var validationPanelRoot = new GameObject("ValidationPanelRoot");
            validationPanelRoot.transform.SetParent(ui.transform);
            new GameObject("ModalRoot").transform.SetParent(ui.transform);

            var overlayRoot = new GameObject("OverlayRoot");
            var debugRoot = new GameObject("DebugRoot");

            systems.SendMessage("Awake");

            Assert.That(systems.GetComponent<SandboxCommandHistory>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxToolStateService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxSelectionService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxInputRouter>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxWorkspaceStateService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxKeyboardShortcutService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxSaveLoadService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxProjectTransferService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxProjectMetadataService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxValidationService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxColliderRebuildService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxProjectWorkspaceService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxFloorManagementService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxVisualOrganizationService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxClipboardService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxMeasurementService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxEditorQoLService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxPreviewService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxBlueprintImportService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxScaleCalibrationService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxCalibrationWorkflowService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxPreviewImageExportService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxWallSnappingService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxWallAuthoringService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxSemanticObjectAuthoringService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxPreviewAuthoringService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxScenarioManagementService>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxOverviewNavigator>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxOnboardingOverlayShell>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxCalibrationCaptureOverlay>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxWallAuthoringOverlay>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxObjectInteractionOverlay>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxSemanticObjectAuthoringOverlay>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxMeasurementOverlay>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxPreviewInteractionOverlay>(), Is.Not.Null);
            Assert.That(topBar.GetComponent<SandboxTopBarShell>(), Is.Not.Null);
            Assert.That(leftToolPanel.GetComponent<SandboxToolPaletteShell>(), Is.Not.Null);
            Assert.That(rightInspectorPanel.GetComponent<SandboxInspectorPanelShell>(), Is.Not.Null);
            Assert.That(rightInspectorPanel.GetComponent<SandboxVisualLegendShell>(), Is.Not.Null);
            Assert.That(bottomStatusBar.GetComponent<SandboxStatusBarShell>(), Is.Not.Null);
            Assert.That(floorTabsBar.GetComponent<SandboxFloorTabsBarShell>(), Is.Not.Null);
            Assert.That(validationPanelRoot.GetComponent<SandboxValidationPanelShell>(), Is.Not.Null);
            Assert.That(ui.GetComponent<SandboxEditorHud>(), Is.Not.Null);
            Assert.That(world.transform.Find("BlueprintRoot").GetComponent<SandboxBlueprintOverlayRenderer>(), Is.Not.Null);
            Assert.That(world.transform.Find("GridRoot").GetComponent<SandboxGridOverlayRenderer>(), Is.Not.Null);
            Assert.That(world.transform.Find("WallRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("HandleRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("ColliderRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("SemanticRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("WallRoot").GetComponent<SandboxWallOverlayRenderer>(), Is.Not.Null);
            Assert.That(world.transform.Find("SemanticRoot").GetComponent<SandboxSemanticObjectRenderer>(), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("ValidationHighlightRoot"), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("ValidationHighlightRoot").GetComponent<SandboxValidationHighlightRenderer>(), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("DiagnosticsOverlayRoot"), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("DiagnosticsOverlayRoot").GetComponent<SandboxDiagnosticsOverlayRenderer>(), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("PreviewDiagnosticsRoot"), Is.Not.Null);
            Assert.That(debugRoot.transform.Find("PreviewDiagnosticsRoot").GetComponent<SandboxPreviewDiagnosticsRenderer>(), Is.Not.Null);
            Assert.That(ui.transform.Find("ModalRoot").GetComponent<SandboxNewProjectDialogShell>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<Camera>(), Is.Not.Null);

            var inspectorShell = rightInspectorPanel.GetComponent<SandboxInspectorPanelShell>();
            var topBarShell = topBar.GetComponent<SandboxTopBarShell>();
            var statusBarShell = bottomStatusBar.GetComponent<SandboxStatusBarShell>();
            var editorHud = ui.GetComponent<SandboxEditorHud>();
            var previewService = systems.GetComponent<SandboxPreviewService>();
            var workspaceService = systems.GetComponent<SandboxProjectWorkspaceService>();

            Assert.That(inspectorShell.IsFullyWired, Is.True);
            Assert.That(inspectorShell.GetMissingDependencies(), Is.Empty);
            Assert.That(editorHud.IsFullyWired, Is.True);
            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Edit Mode"));
            Assert.That(statusBarShell.ModeLabel, Is.EqualTo("Edit Mode"));

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(inspectorShell.UpdateProjectMetadata(
                "Bootstrap Tower",
                "Installer wiring smoke test",
                "Codex",
                new[] { new MetadataFieldData { key = "phase", value = "11" } }), Is.True);
            Assert.That(workspaceService.ActiveProject.metadata.buildingName, Is.EqualTo("Bootstrap Tower"));

            Assert.That(previewService.EnterPreviewMode(), Is.True);
            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Preview Mode"));
            Assert.That(statusBarShell.ModeLabel, Is.EqualTo("Preview Mode"));

            previewService.ExitPreviewMode();
            Assert.That(topBarShell.ModeLabel, Is.EqualTo("Edit Mode"));
            Assert.That(statusBarShell.ModeLabel, Is.EqualTo("Edit Mode"));

            Object.DestroyImmediate(systems);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(overlayRoot);
            Object.DestroyImmediate(debugRoot);
        }
    }
}
