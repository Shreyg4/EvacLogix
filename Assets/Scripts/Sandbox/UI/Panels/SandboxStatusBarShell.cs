using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Validation;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxStatusBarShell : MonoBehaviour
    {
        [SerializeField] private string statusMessage = "Ready";
        [SerializeField] private string lifecycleStateLabel = "Draft";
        [SerializeField] private string modeLabel = "Edit Mode";
        [SerializeField] private string persistenceSummary = "Unsaved";
        [SerializeField] private string recoveryPromptLabel = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSaveLoadService saveLoadService;
        private SandboxValidationService validationService;
        private SandboxPreviewService previewService;

        public string StatusMessage
        {
            get => statusMessage;
            set => statusMessage = value;
        }

        public string LifecycleStateLabel => lifecycleStateLabel;
        public string ModeLabel => modeLabel;
        public string PersistenceSummary => persistenceSummary;
        public string RecoveryPromptLabel => recoveryPromptLabel;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            saveLoadService = FindAnyObjectByType<SandboxSaveLoadService>();
            validationService = FindAnyObjectByType<SandboxValidationService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                HandleProjectChanged(workspaceService.ActiveProject);
            }

            if (saveLoadService != null)
            {
                saveLoadService.PersistenceStateChanged += HandlePersistenceStateChanged;
                HandlePersistenceStateChanged();
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged += HandleValidationChanged;
            }

            if (previewService != null)
            {
                previewService.PreviewModeChanged += HandlePreviewModeChanged;
                HandlePreviewModeChanged(previewService.IsPreviewModeActive);
            }
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
            }

            if (saveLoadService != null)
            {
                saveLoadService.PersistenceStateChanged -= HandlePersistenceStateChanged;
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged -= HandleValidationChanged;
            }

            if (previewService != null)
            {
                previewService.PreviewModeChanged -= HandlePreviewModeChanged;
            }
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            lifecycleStateLabel = BuildingLifecycleStateUtility.GetDisplayLabel(
                project?.LifecycleState ?? BuildingLifecycleState.Draft);
        }

        private void HandleValidationChanged(IReadOnlyList<ValidationIssueData> issues)
        {
            HandleProjectChanged(workspaceService?.ActiveProject);
        }

        private void HandlePreviewModeChanged(bool isPreviewModeActive)
        {
            modeLabel = isPreviewModeActive ? "Preview Mode" : "Edit Mode";
        }

        private void HandlePersistenceStateChanged()
        {
            persistenceSummary = saveLoadService == null
                ? "Unsaved"
                : (saveLoadService.HasUnsavedChanges ? "Unsaved changes" : "All changes saved");
            recoveryPromptLabel = saveLoadService != null && saveLoadService.HasRecoveryPrompt
                ? saveLoadService.RecoveryPromptMessage
                : string.Empty;
        }
    }
}
