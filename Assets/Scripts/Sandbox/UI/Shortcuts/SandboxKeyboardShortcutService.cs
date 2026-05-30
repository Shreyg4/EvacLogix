using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Shortcuts
{
    [Serializable]
    public sealed class SandboxShortcutBinding
    {
        public SandboxShortcutId shortcutId;
        public KeyCode keyCode;
        public bool requiresCommandOrControl;
        public bool requiresShift;
        public bool requiresAlt;
    }

    [Serializable]
    public sealed class SandboxShortcutCatalogEntry
    {
        public SandboxShortcutId shortcutId;
        public string category = string.Empty;
        public string label = string.Empty;
        public string description = string.Empty;
        public string bindingDisplay = string.Empty;
    }

    [Serializable]
    public sealed class SandboxShortcutConflict
    {
        public string bindingDisplay = string.Empty;
        public List<SandboxShortcutId> shortcutIds = new();
    }

    public sealed class SandboxKeyboardShortcutService : MonoBehaviour
    {
        [SerializeField] private List<SandboxShortcutBinding> bindings = new();

        private SandboxCommandHistory commandHistory;
        private SandboxToolStateService toolStateService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxClipboardService clipboardService;
        private SandboxCameraController cameraController;
        private SandboxPreviewService previewService;
        private SandboxToolMode lastNonPanToolMode = SandboxToolMode.Select;

        public IReadOnlyList<SandboxShortcutBinding> Bindings => bindings;
        public bool HasBindingConflicts => GetBindingConflicts().Count > 0;

        private void Awake()
        {
            commandHistory = GetComponent<SandboxCommandHistory>();
            toolStateService = GetComponent<SandboxToolStateService>();
            selectionService = GetComponent<SandboxSelectionService>();
            inputRouter = GetComponent<SandboxInputRouter>();
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
            clipboardService = GetComponent<SandboxClipboardService>();
            cameraController = FindAnyObjectByType<SandboxCameraController>();
            previewService = GetComponent<SandboxPreviewService>();

            EnsureDefaultBindings();
        }

        public void EnsureDefaultBindings()
        {
            if (bindings.Count == 0)
            {
                bindings = CreateDefaultBindings();
                UpgradePanBindingIfNeeded();
                return;
            }

            // Inject default bindings for any shortcut ids added after this list was
            // serialized, so new shortcuts (for example Escape -> Cancel) appear without
            // forcing users to reset their existing customized bindings.
            var boundShortcutIds = new HashSet<SandboxShortcutId>(bindings.Select(binding => binding.shortcutId));
            foreach (var defaultBinding in CreateDefaultBindings())
            {
                if (boundShortcutIds.Add(defaultBinding.shortcutId))
                {
                    bindings.Add(defaultBinding);
                }
            }

            UpgradePanBindingIfNeeded();
        }

        public IReadOnlyList<SandboxShortcutCatalogEntry> GetShortcutCatalogEntries()
        {
            EnsureDefaultBindings();

            var entries = new List<SandboxShortcutCatalogEntry>(bindings.Count);
            for (var i = 0; i < bindings.Count; i += 1)
            {
                var binding = bindings[i];
                var definition = GetDefinition(binding.shortcutId);
                entries.Add(new SandboxShortcutCatalogEntry
                {
                    shortcutId = binding.shortcutId,
                    category = definition.category,
                    label = definition.label,
                    description = definition.description,
                    bindingDisplay = GetBindingDisplay(binding)
                });
            }

            return entries;
        }

        public IReadOnlyList<SandboxShortcutConflict> GetBindingConflicts()
        {
            EnsureDefaultBindings();

            return bindings
                .GroupBy(GetBindingSignature)
                .Where(group => group.Select(binding => binding.shortcutId).Distinct().Count() > 1)
                .Select(group => new SandboxShortcutConflict
                {
                    bindingDisplay = GetBindingDisplay(group.First()),
                    shortcutIds = group.Select(binding => binding.shortcutId).Distinct().ToList()
                })
                .ToList();
        }

        private void Update()
        {
            for (var i = 0; i < bindings.Count; i += 1)
            {
                var binding = bindings[i];
                if (!SandboxInputAdapter.GetKeyDown(binding.keyCode) || !AreModifiersSatisfied(binding))
                {
                    continue;
                }

                if (inputRouter != null &&
                    inputRouter.CurrentTarget == SandboxInputTarget.UI &&
                    !CanDispatchWhilePointerOverUi(binding.shortcutId))
                {
                    continue;
                }

                Dispatch(binding.shortcutId);
                break;
            }
        }

        public bool CanDispatchWhilePointerOverUi(SandboxShortcutId shortcutId)
        {
            return shortcutId == SandboxShortcutId.Undo ||
                   shortcutId == SandboxShortcutId.Redo ||
                   shortcutId == SandboxShortcutId.CancelTool;
        }

        private void Dispatch(SandboxShortcutId shortcutId)
        {
            clipboardService ??= GetComponent<SandboxClipboardService>();
            cameraController ??= FindAnyObjectByType<SandboxCameraController>();
            previewService ??= GetComponent<SandboxPreviewService>();

            if (previewService != null &&
                previewService.IsPreviewModeActive &&
                shortcutId != SandboxShortcutId.ResetCamera &&
                !IsPreviewAuthoringShortcut(shortcutId))
            {
                return;
            }

            switch (shortcutId)
            {
                case SandboxShortcutId.SelectTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Select, commandHistory);
                    break;
                case SandboxShortcutId.PanTool:
                    TogglePanTool();
                    break;
                case SandboxShortcutId.MeasureTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Measure, commandHistory);
                    break;
                case SandboxShortcutId.WallLineTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.WallLine, commandHistory);
                    break;
                case SandboxShortcutId.WallBrushTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.WallBrush, commandHistory);
                    break;
                case SandboxShortcutId.EraseTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Erase, commandHistory);
                    break;
                case SandboxShortcutId.DoorTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Door, commandHistory);
                    break;
                case SandboxShortcutId.WindowTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Window, commandHistory);
                    break;
                case SandboxShortcutId.ExitTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Exit, commandHistory);
                    break;
                case SandboxShortcutId.ObstacleTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Obstacle, commandHistory);
                    break;
                case SandboxShortcutId.TeleportTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Teleport, commandHistory);
                    break;
                case SandboxShortcutId.SpawnPointTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.SpawnPoint, commandHistory);
                    previewService?.EnterPreviewMode();
                    previewService?.ConfigureSpawnPlacement(string.Empty, "Main Preview Layout", true);
                    previewService?.SetInteractionMode(SandboxPreviewInteractionMode.PlaceSpawnPoint);
                    break;
                case SandboxShortcutId.SpawnPointBrushTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.SpawnPointBrush, commandHistory);
                    previewService?.EnterPreviewMode();
                    previewService?.ConfigureSpawnPointBrush(1f, string.Empty, "Spawn Point Brush Layout", true);
                    previewService?.SetInteractionMode(SandboxPreviewInteractionMode.PaintSpawnPointBrush);
                    break;
                case SandboxShortcutId.RegionTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Region, commandHistory);
                    break;
                case SandboxShortcutId.Undo:
                    commandHistory?.Undo();
                    break;
                case SandboxShortcutId.Redo:
                    commandHistory?.Redo();
                    break;
                case SandboxShortcutId.DeleteSelection:
                    if (!(clipboardService?.DeleteSelection() ?? false))
                    {
                        selectionService?.ClearSelection(commandHistory);
                    }
                    break;
                case SandboxShortcutId.CopySelection:
                    clipboardService?.CopySelection();
                    break;
                case SandboxShortcutId.PasteSelection:
                    clipboardService?.PasteSelection();
                    break;
                case SandboxShortcutId.DuplicateSelection:
                    clipboardService?.DuplicateSelection();
                    break;
                case SandboxShortcutId.ToggleGrid:
                    workspaceStateService?.ToggleGridVisibility(commandHistory);
                    break;
                case SandboxShortcutId.ToggleSnapping:
                    workspaceStateService?.ToggleSnapping(commandHistory);
                    break;
                case SandboxShortcutId.ResetCamera:
                    cameraController?.ResetView();
                    break;
                case SandboxShortcutId.CancelTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Select, commandHistory);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shortcutId), shortcutId, null);
            }
        }

        private static bool AreModifiersSatisfied(SandboxShortcutBinding binding)
        {
            var commandOrControlPressed = SandboxInputAdapter.GetKey(KeyCode.LeftControl)
                || SandboxInputAdapter.GetKey(KeyCode.RightControl)
                || SandboxInputAdapter.GetKey(KeyCode.LeftCommand)
                || SandboxInputAdapter.GetKey(KeyCode.RightCommand);

            var shiftPressed = SandboxInputAdapter.GetKey(KeyCode.LeftShift) || SandboxInputAdapter.GetKey(KeyCode.RightShift);
            var altPressed = SandboxInputAdapter.GetKey(KeyCode.LeftAlt) || SandboxInputAdapter.GetKey(KeyCode.RightAlt);

            return binding.requiresCommandOrControl == commandOrControlPressed
                && binding.requiresShift == shiftPressed
                && binding.requiresAlt == altPressed;
        }

        private static bool IsPreviewAuthoringShortcut(SandboxShortcutId shortcutId)
        {
            return shortcutId == SandboxShortcutId.SpawnPointTool ||
                   shortcutId == SandboxShortcutId.SpawnPointBrushTool;
        }

        private static List<SandboxShortcutBinding> CreateDefaultBindings()
        {
            return new List<SandboxShortcutBinding>
            {
                CreateBinding(SandboxShortcutId.SelectTool, KeyCode.Q),
                CreateBinding(SandboxShortcutId.PanTool, KeyCode.P),
                CreateBinding(SandboxShortcutId.MeasureTool, KeyCode.M),
                CreateBinding(SandboxShortcutId.WallLineTool, KeyCode.L),
                CreateBinding(SandboxShortcutId.WallBrushTool, KeyCode.B),
                CreateBinding(SandboxShortcutId.EraseTool, KeyCode.E),
                CreateBinding(SandboxShortcutId.DoorTool, KeyCode.D),
                CreateBinding(SandboxShortcutId.WindowTool, KeyCode.W),
                CreateBinding(SandboxShortcutId.ExitTool, KeyCode.X),
                CreateBinding(SandboxShortcutId.ObstacleTool, KeyCode.O),
                CreateBinding(SandboxShortcutId.TeleportTool, KeyCode.Y),
                CreateBinding(SandboxShortcutId.SpawnPointTool, KeyCode.Alpha1),
                CreateBinding(SandboxShortcutId.SpawnPointBrushTool, KeyCode.Alpha2),
                CreateBinding(SandboxShortcutId.RegionTool, KeyCode.R),
                CreateBinding(SandboxShortcutId.Undo, KeyCode.Z, requiresCommandOrControl: true),
                CreateBinding(SandboxShortcutId.Redo, KeyCode.Z, requiresCommandOrControl: true, requiresShift: true),
                CreateBinding(SandboxShortcutId.DeleteSelection, KeyCode.Backspace),
                CreateBinding(SandboxShortcutId.DeleteSelection, KeyCode.Delete),
                CreateBinding(SandboxShortcutId.CopySelection, KeyCode.C, requiresCommandOrControl: true),
                CreateBinding(SandboxShortcutId.PasteSelection, KeyCode.V, requiresCommandOrControl: true),
                CreateBinding(SandboxShortcutId.DuplicateSelection, KeyCode.D, requiresCommandOrControl: true),
                CreateBinding(SandboxShortcutId.ToggleGrid, KeyCode.G),
                CreateBinding(SandboxShortcutId.ToggleSnapping, KeyCode.S),
                CreateBinding(SandboxShortcutId.ResetCamera, KeyCode.Home),
                CreateBinding(SandboxShortcutId.CancelTool, KeyCode.Escape),
            };
        }

        private static (string category, string label, string description) GetDefinition(SandboxShortcutId shortcutId)
        {
            return shortcutId switch
            {
                SandboxShortcutId.SelectTool => ("Tools", "Select Tool", "Switch to selection and inspection mode."),
                SandboxShortcutId.PanTool => ("Tools", "Pan Tool", "Switch to camera panning mode for large plans."),
                SandboxShortcutId.MeasureTool => ("Tools", "Measure Tool", "Measure distances without changing authored geometry."),
                SandboxShortcutId.WallLineTool => ("Tools", "Wall Line Tool", "Trace precise wall centerlines one segment at a time."),
                SandboxShortcutId.WallBrushTool => ("Tools", "Wall Brush Tool", "Sketch freeform wall strokes before cleanup."),
                SandboxShortcutId.EraseTool => ("Tools", "Erase Tool", "Switch to destructive cleanup mode for selected geometry."),
                SandboxShortcutId.DoorTool => ("Tools", "Door Tool", "Place door openings on existing wall segments."),
                SandboxShortcutId.WindowTool => ("Tools", "Window Tool", "Place windows and escape metadata on existing wall segments."),
                SandboxShortcutId.ExitTool => ("Tools", "Exit Tool", "Place named exit zones with width and priority inputs."),
                SandboxShortcutId.ObstacleTool => ("Tools", "Obstacle Tool", "Place blocking or slowing obstacle geometry."),
                SandboxShortcutId.TeleportTool => ("Tools", "Teleport Tool", "Place paired stair, elevator, or escalator transitions across floors."),
                SandboxShortcutId.SpawnPointTool => ("Spawn", "Spawn Point Tool", "Place individual spawn points in enclosed rooms that have exits or windows."),
                SandboxShortcutId.SpawnPointBrushTool => ("Spawn", "Spawn Point Brush Tool", "Paint spawn points in enclosed rooms that have exits or windows."),
                SandboxShortcutId.RegionTool => ("Preview", "Region Tool", "Draw named semantic regions for preview semantics."),
                SandboxShortcutId.Undo => ("Editing", "Undo", "Revert the most recent editor command."),
                SandboxShortcutId.Redo => ("Editing", "Redo", "Reapply the most recently undone editor command."),
                SandboxShortcutId.DeleteSelection => ("Editing", "Delete Selection", "Delete or clear the current selection safely."),
                SandboxShortcutId.CopySelection => ("Editing", "Copy Selection", "Copy the current safe selection to the clipboard."),
                SandboxShortcutId.PasteSelection => ("Editing", "Paste Selection", "Paste clipboard-safe objects into the active floor."),
                SandboxShortcutId.DuplicateSelection => ("Editing", "Duplicate Selection", "Duplicate the current selection with a safe offset."),
                SandboxShortcutId.ToggleGrid => ("View", "Toggle Grid", "Show or hide the drafting grid overlay."),
                SandboxShortcutId.ToggleSnapping => ("View", "Toggle Snapping", "Enable or disable wall snapping helpers."),
                SandboxShortcutId.ResetCamera => ("View", "Reset Camera", "Reset the editor camera to the default framing."),
                SandboxShortcutId.CancelTool => ("Tools", "Cancel To Select", "Cancel the active placement tool and return to Select mode."),
                _ => ("Other", shortcutId.ToString(), $"Trigger the {shortcutId} action.")
            };
        }

        private static string GetBindingSignature(SandboxShortcutBinding binding)
        {
            return $"{binding.keyCode}|{binding.requiresCommandOrControl}|{binding.requiresShift}|{binding.requiresAlt}";
        }

        private static string GetBindingDisplay(SandboxShortcutBinding binding)
        {
            var parts = new List<string>(4);
            if (binding.requiresCommandOrControl)
            {
                parts.Add("Cmd/Ctrl");
            }

            if (binding.requiresShift)
            {
                parts.Add("Shift");
            }

            if (binding.requiresAlt)
            {
                parts.Add("Alt");
            }

            parts.Add(binding.keyCode.ToString());
            return string.Join("+", parts);
        }

        private static SandboxShortcutBinding CreateBinding(
            SandboxShortcutId shortcutId,
            KeyCode keyCode,
            bool requiresCommandOrControl = false,
            bool requiresShift = false,
            bool requiresAlt = false)
        {
            return new SandboxShortcutBinding
            {
                shortcutId = shortcutId,
                keyCode = keyCode,
                requiresCommandOrControl = requiresCommandOrControl,
                requiresShift = requiresShift,
                requiresAlt = requiresAlt,
            };
        }

        private void TogglePanTool()
        {
            if (toolStateService == null)
            {
                return;
            }

            var currentToolMode = toolStateService.CurrentToolMode;
            if (currentToolMode == SandboxToolMode.Pan)
            {
                var nextToolMode = lastNonPanToolMode == SandboxToolMode.Pan
                    ? toolStateService.DefaultToolMode
                    : lastNonPanToolMode;
                toolStateService.RequestToolModeChange(nextToolMode, commandHistory);
                return;
            }

            lastNonPanToolMode = currentToolMode;
            toolStateService.RequestToolModeChange(SandboxToolMode.Pan, commandHistory);
        }

        private void UpgradePanBindingIfNeeded()
        {
            var panBinding = bindings.FirstOrDefault(binding => binding.shortcutId == SandboxShortcutId.PanTool);
            if (panBinding == null)
            {
                return;
            }

            if (panBinding.keyCode == KeyCode.H &&
                !panBinding.requiresCommandOrControl &&
                !panBinding.requiresShift &&
                !panBinding.requiresAlt)
            {
                panBinding.keyCode = KeyCode.P;
            }
        }
    }
}
