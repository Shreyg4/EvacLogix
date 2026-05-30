using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxToolPaletteShell : MonoBehaviour
    {
        [SerializeField] private List<SandboxToolMode> availableTools = new();

        private SandboxToolStateService toolStateService;
        private SandboxCommandHistory commandHistory;
        private SandboxPreviewService previewService;

        public IReadOnlyList<SandboxToolMode> AvailableTools => availableTools;

        private void Awake()
        {
            EnsureTools();
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            commandHistory = FindAnyObjectByType<SandboxCommandHistory>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
        }

        private void Reset()
        {
            EnsureTools();
        }

        public void SelectTool(SandboxToolMode toolMode)
        {
            toolStateService?.RequestToolModeChange(toolMode, commandHistory);
            ConfigurePreviewTool(toolMode);
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

        private void ConfigurePreviewTool(SandboxToolMode toolMode)
        {
            previewService ??= FindAnyObjectByType<SandboxPreviewService>();
            if (previewService == null)
            {
                return;
            }

            switch (toolMode)
            {
                case SandboxToolMode.SpawnPoint:
                    previewService.EnterPreviewMode();
                    previewService.ConfigureSpawnPlacement(string.Empty, "Main Preview Layout", true);
                    previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceSpawnPoint);
                    break;
                case SandboxToolMode.SpawnPointBrush:
                    previewService.EnterPreviewMode();
                    previewService.ConfigureSpawnPointBrush(1f, string.Empty, "Spawn Point Brush Layout", true);
                    previewService.SetInteractionMode(SandboxPreviewInteractionMode.PaintSpawnPointBrush);
                    break;
                case SandboxToolMode.Region:
                    // Region is a preview-authoring tool; leave preview state untouched.
                    break;
                default:
                    // Selecting any edit tool must leave preview mode, otherwise the editor stays
                    // gated (edit overlays and shortcuts are disabled during preview) and the user
                    // gets trapped with no way back to editing.
                    if (previewService.IsPreviewModeActive)
                    {
                        previewService.ExitPreviewMode();
                    }

                    break;
                default:
                    previewService.ClearInteractionMode();
                    previewService.ExitPreviewMode();
                    break;
            }
        }
    }
}
