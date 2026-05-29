using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Tools;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxToolPaletteShell : MonoBehaviour
    {
        [SerializeField] private List<SandboxToolMode> availableTools = new();

        private SandboxToolStateService toolStateService;
        private SandboxCommandHistory commandHistory;

        public IReadOnlyList<SandboxToolMode> AvailableTools => availableTools;

        private void Awake()
        {
            EnsureTools();
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            commandHistory = FindAnyObjectByType<SandboxCommandHistory>();
        }

        private void Reset()
        {
            EnsureTools();
        }

        public void SelectTool(SandboxToolMode toolMode)
        {
            toolStateService?.RequestToolModeChange(toolMode, commandHistory);
        }

        public bool IsToolActive(SandboxToolMode toolMode)
        {
            return toolStateService != null && toolStateService.CurrentToolMode == toolMode;
        }

        private void EnsureTools()
        {
            if (availableTools.Count > 0)
            {
                return;
            }

            availableTools = new List<SandboxToolMode>((SandboxToolMode[])Enum.GetValues(typeof(SandboxToolMode)));
        }
    }
}
