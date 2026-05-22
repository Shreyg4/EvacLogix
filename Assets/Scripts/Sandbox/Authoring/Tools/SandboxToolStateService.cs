using System;
using EvacLogix.Sandbox.Authoring.Commands;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Tools
{
    public sealed class SandboxToolStateService : MonoBehaviour
    {
        [SerializeField] private SandboxToolMode defaultToolMode = SandboxToolMode.Select;
        [SerializeField] private SandboxToolMode currentToolMode = SandboxToolMode.Select;

        public event Action<SandboxToolMode> ToolModeChanged;

        public SandboxToolMode DefaultToolMode => defaultToolMode;
        public SandboxToolMode CurrentToolMode => currentToolMode;

        public void RequestToolModeChange(SandboxToolMode nextToolMode, SandboxCommandHistory commandHistory = null)
        {
            if (nextToolMode == currentToolMode)
            {
                return;
            }

            var previousToolMode = currentToolMode;
            if (commandHistory == null)
            {
                ApplyToolMode(nextToolMode);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                $"Switch Tool To {nextToolMode}",
                () => ApplyToolMode(nextToolMode),
                () => ApplyToolMode(previousToolMode)));
        }

        public void ResetToDefaultTool(SandboxCommandHistory commandHistory = null)
        {
            RequestToolModeChange(defaultToolMode, commandHistory);
        }

        public void SetDefaultToolMode(SandboxToolMode nextDefaultToolMode)
        {
            defaultToolMode = nextDefaultToolMode;
        }

        private void ApplyToolMode(SandboxToolMode nextToolMode)
        {
            currentToolMode = nextToolMode;
            ToolModeChanged?.Invoke(currentToolMode);
        }
    }
}
