using System;
using System.Collections.Generic;
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

    public sealed class SandboxKeyboardShortcutService : MonoBehaviour
    {
        [SerializeField] private List<SandboxShortcutBinding> bindings = new();

        private SandboxCommandHistory commandHistory;
        private SandboxToolStateService toolStateService;
        private SandboxSelectionService selectionService;
        private SandboxInputRouter inputRouter;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxCameraController cameraController;

        public IReadOnlyList<SandboxShortcutBinding> Bindings => bindings;

        private void Awake()
        {
            commandHistory = GetComponent<SandboxCommandHistory>();
            toolStateService = GetComponent<SandboxToolStateService>();
            selectionService = GetComponent<SandboxSelectionService>();
            inputRouter = GetComponent<SandboxInputRouter>();
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
            cameraController = FindAnyObjectByType<SandboxCameraController>();

            EnsureDefaultBindings();
        }

        public void EnsureDefaultBindings()
        {
            if (bindings.Count == 0)
            {
                bindings = CreateDefaultBindings();
            }
        }

        private void Update()
        {
            if (inputRouter != null && inputRouter.CurrentTarget == SandboxInputTarget.UI)
            {
                return;
            }

            for (var i = 0; i < bindings.Count; i += 1)
            {
                var binding = bindings[i];
                if (!Input.GetKeyDown(binding.keyCode) || !AreModifiersSatisfied(binding))
                {
                    continue;
                }

                Dispatch(binding.shortcutId);
                break;
            }
        }

        private void Dispatch(SandboxShortcutId shortcutId)
        {
            switch (shortcutId)
            {
                case SandboxShortcutId.SelectTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Select, commandHistory);
                    break;
                case SandboxShortcutId.PanTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Pan, commandHistory);
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
                case SandboxShortcutId.StairTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.Stair, commandHistory);
                    break;
                case SandboxShortcutId.SpawnPointTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.SpawnPoint, commandHistory);
                    break;
                case SandboxShortcutId.SpawnBrushTool:
                    toolStateService?.RequestToolModeChange(SandboxToolMode.SpawnBrush, commandHistory);
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
                    selectionService?.ClearSelection(commandHistory);
                    break;
                case SandboxShortcutId.CopySelection:
                case SandboxShortcutId.PasteSelection:
                case SandboxShortcutId.DuplicateSelection:
                    Debug.Log($"Shortcut {shortcutId} invoked. Object copy workflows land in a later phase.");
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(shortcutId), shortcutId, null);
            }
        }

        private static bool AreModifiersSatisfied(SandboxShortcutBinding binding)
        {
            var commandOrControlPressed = Input.GetKey(KeyCode.LeftControl)
                || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand)
                || Input.GetKey(KeyCode.RightCommand);

            var shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            var altPressed = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            return binding.requiresCommandOrControl == commandOrControlPressed
                && binding.requiresShift == shiftPressed
                && binding.requiresAlt == altPressed;
        }

        private static List<SandboxShortcutBinding> CreateDefaultBindings()
        {
            return new List<SandboxShortcutBinding>
            {
                CreateBinding(SandboxShortcutId.SelectTool, KeyCode.Q),
                CreateBinding(SandboxShortcutId.PanTool, KeyCode.H),
                CreateBinding(SandboxShortcutId.MeasureTool, KeyCode.M),
                CreateBinding(SandboxShortcutId.WallLineTool, KeyCode.L),
                CreateBinding(SandboxShortcutId.WallBrushTool, KeyCode.B),
                CreateBinding(SandboxShortcutId.EraseTool, KeyCode.E),
                CreateBinding(SandboxShortcutId.DoorTool, KeyCode.D),
                CreateBinding(SandboxShortcutId.WindowTool, KeyCode.W),
                CreateBinding(SandboxShortcutId.ExitTool, KeyCode.X),
                CreateBinding(SandboxShortcutId.ObstacleTool, KeyCode.O),
                CreateBinding(SandboxShortcutId.StairTool, KeyCode.T),
                CreateBinding(SandboxShortcutId.SpawnPointTool, KeyCode.Alpha1),
                CreateBinding(SandboxShortcutId.SpawnBrushTool, KeyCode.Alpha2),
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
            };
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
    }
}
