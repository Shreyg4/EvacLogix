using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxNewProjectDialogShell : MonoBehaviour
    {
        [SerializeField] private bool isOpen = true;
        [SerializeField] private SandboxProjectTemplateKind selectedTemplate = SandboxProjectTemplateKind.DefaultTemplate;
        [SerializeField] private string projectNameDraft = string.Empty;
        [SerializeField] private string validationMessage = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxOnboardingOverlayShell onboardingOverlay;
        private SandboxStatusBarShell statusBar;

        public bool IsOpen => isOpen;
        public SandboxProjectTemplateKind SelectedTemplate => selectedTemplate;
        public string ProjectNameDraft
        {
            get => projectNameDraft;
            set => projectNameDraft = value ?? string.Empty;
        }
        public string ValidationMessage => validationMessage;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            onboardingOverlay = FindAnyObjectByType<SandboxOnboardingOverlayShell>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            isOpen = workspaceService == null || workspaceService.ActiveProject == null;
            if (isOpen)
            {
                EnsureDefaultProjectName();
            }
        }

        private void Start()
        {
            // Returning from a simulation launched in the editor: re-adopt the project the user left
            // rather than prompting for a new one (otherwise the round trip looks like it was deleted).
            // Done in Start so every service has finished Awake before the project is restored.
            if (workspaceService != null && SandboxSimulationLaunchContext.TryConsumeReturnProject(out var returnProject))
            {
                workspaceService.SetActiveProject(returnProject);
                isOpen = false;
            }
        }

        public void Open()
        {
            validationMessage = string.Empty;
            EnsureDefaultProjectName();
            isOpen = true;
        }

        public void Close()
        {
            isOpen = false;
        }

        public void SelectTemplate(SandboxProjectTemplateKind templateKind)
        {
            selectedTemplate = templateKind;
        }

        public void CreateDefaultProject()
        {
            CreateProject(SandboxProjectTemplateKind.DefaultTemplate);
        }

        public void CreateBlankProject()
        {
            CreateProject(SandboxProjectTemplateKind.BlankTemplate);
        }

        private void CreateProject(SandboxProjectTemplateKind templateKind)
        {
            EnsureDefaultProjectName();
            if (string.IsNullOrWhiteSpace(projectNameDraft))
            {
                validationMessage = "Project name is required.";
                return;
            }

            workspaceService?.CreateNewProject(templateKind, projectNameDraft.Trim());
            onboardingOverlay?.ShowProjectCreationGuidance();
            if (statusBar != null)
            {
                statusBar.StatusMessage = templateKind == SandboxProjectTemplateKind.DefaultTemplate
                    ? $"Created default sandbox project '{projectNameDraft.Trim()}'."
                    : $"Created blank sandbox project '{projectNameDraft.Trim()}'.";
            }
            selectedTemplate = templateKind;
            validationMessage = string.Empty;
            isOpen = false;
        }

        private void EnsureDefaultProjectName()
        {
            if (string.IsNullOrWhiteSpace(projectNameDraft))
            {
                projectNameDraft = "New Project";
            }
        }
    }
}
