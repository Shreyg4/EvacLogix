using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Core;
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
            Assert.That(systems.GetComponent<SandboxValidationService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxColliderRebuildService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxProjectWorkspaceService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxBlueprintImportService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxScaleCalibrationService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxCalibrationWorkflowService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxPreviewImageExportService>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxOverviewNavigator>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxOnboardingOverlayShell>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxCalibrationCaptureOverlay>(), Is.Not.Null);
            Assert.That(topBar.GetComponent<SandboxTopBarShell>(), Is.Not.Null);
            Assert.That(leftToolPanel.GetComponent<SandboxToolPaletteShell>(), Is.Not.Null);
            Assert.That(rightInspectorPanel.GetComponent<SandboxInspectorPanelShell>(), Is.Not.Null);
            Assert.That(bottomStatusBar.GetComponent<SandboxStatusBarShell>(), Is.Not.Null);
            Assert.That(floorTabsBar.GetComponent<SandboxFloorTabsBarShell>(), Is.Not.Null);
            Assert.That(validationPanelRoot.GetComponent<SandboxValidationPanelShell>(), Is.Not.Null);
            Assert.That(world.transform.Find("BlueprintRoot").GetComponent<SandboxBlueprintOverlayRenderer>(), Is.Not.Null);
            Assert.That(ui.transform.Find("ModalRoot").GetComponent<SandboxNewProjectDialogShell>(), Is.Not.Null);
            Assert.That(Object.FindAnyObjectByType<Camera>(), Is.Not.Null);

            Object.DestroyImmediate(systems);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(overlayRoot);
            Object.DestroyImmediate(debugRoot);
        }
    }
}
