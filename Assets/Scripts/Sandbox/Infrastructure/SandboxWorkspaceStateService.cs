using System;
using EvacLogix.Sandbox.Authoring.Commands;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxWorkspaceStateService : MonoBehaviour
    {
        [SerializeField] private bool gridVisible = true;
        [SerializeField] private bool snappingEnabled = true;

        public event Action<bool> GridVisibilityChanged;
        public event Action<bool> SnappingChanged;

        public bool GridVisible => gridVisible;
        public bool SnappingEnabled => snappingEnabled;

        public void ToggleGridVisibility(SandboxCommandHistory commandHistory = null)
        {
            SetGridVisibility(!gridVisible, commandHistory);
        }

        public void ToggleSnapping(SandboxCommandHistory commandHistory = null)
        {
            SetSnappingEnabled(!snappingEnabled, commandHistory);
        }

        public void SetGridVisibility(bool visible, SandboxCommandHistory commandHistory = null)
        {
            if (gridVisible == visible)
            {
                return;
            }

            var previous = gridVisible;
            if (commandHistory == null)
            {
                ApplyGridVisibility(visible);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                visible ? "Show Grid" : "Hide Grid",
                () => ApplyGridVisibility(visible),
                () => ApplyGridVisibility(previous)));
        }

        public void SetSnappingEnabled(bool enabled, SandboxCommandHistory commandHistory = null)
        {
            if (snappingEnabled == enabled)
            {
                return;
            }

            var previous = snappingEnabled;
            if (commandHistory == null)
            {
                ApplySnappingEnabled(enabled);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                enabled ? "Enable Snapping" : "Disable Snapping",
                () => ApplySnappingEnabled(enabled),
                () => ApplySnappingEnabled(previous)));
        }

        private void ApplyGridVisibility(bool visible)
        {
            gridVisible = visible;
            GridVisibilityChanged?.Invoke(gridVisible);
        }

        private void ApplySnappingEnabled(bool enabled)
        {
            snappingEnabled = enabled;
            SnappingChanged?.Invoke(snappingEnabled);
        }
    }
}
