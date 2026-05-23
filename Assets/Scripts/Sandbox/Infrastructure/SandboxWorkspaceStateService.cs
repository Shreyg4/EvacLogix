using System;
using EvacLogix.Sandbox.Authoring.Commands;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxWorkspaceStateService : MonoBehaviour
    {
        [SerializeField] private bool gridVisible = true;
        [SerializeField] private bool snappingEnabled = true;
        [SerializeField] private float gridSize = 0.5f;
        [SerializeField] private float angleSnapIncrementDegrees = 45f;

        public event Action<bool> GridVisibilityChanged;
        public event Action<bool> SnappingChanged;
        public event Action<float> GridSizeChanged;
        public event Action<float> AngleSnapIncrementChanged;

        public bool GridVisible => gridVisible;
        public bool SnappingEnabled => snappingEnabled;
        public float GridSize => gridSize;
        public float AngleSnapIncrementDegrees => angleSnapIncrementDegrees;

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

        public void SetGridSize(float nextGridSize, SandboxCommandHistory commandHistory = null)
        {
            nextGridSize = Mathf.Max(0.05f, nextGridSize);
            if (Mathf.Approximately(gridSize, nextGridSize))
            {
                return;
            }

            var previous = gridSize;
            if (commandHistory == null)
            {
                ApplyGridSize(nextGridSize);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                "Set Grid Size",
                () => ApplyGridSize(nextGridSize),
                () => ApplyGridSize(previous)));
        }

        public void SetAngleSnapIncrementDegrees(float nextAngleSnapIncrementDegrees, SandboxCommandHistory commandHistory = null)
        {
            nextAngleSnapIncrementDegrees = Mathf.Clamp(nextAngleSnapIncrementDegrees, 1f, 180f);
            if (Mathf.Approximately(angleSnapIncrementDegrees, nextAngleSnapIncrementDegrees))
            {
                return;
            }

            var previous = angleSnapIncrementDegrees;
            if (commandHistory == null)
            {
                ApplyAngleSnapIncrementDegrees(nextAngleSnapIncrementDegrees);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                "Set Angle Snap Increment",
                () => ApplyAngleSnapIncrementDegrees(nextAngleSnapIncrementDegrees),
                () => ApplyAngleSnapIncrementDegrees(previous)));
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

        private void ApplyGridSize(float nextGridSize)
        {
            gridSize = nextGridSize;
            GridSizeChanged?.Invoke(gridSize);
        }

        private void ApplyAngleSnapIncrementDegrees(float nextAngleSnapIncrementDegrees)
        {
            angleSnapIncrementDegrees = nextAngleSnapIncrementDegrees;
            AngleSnapIncrementChanged?.Invoke(angleSnapIncrementDegrees);
        }
    }
}
