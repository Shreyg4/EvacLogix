using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxNewProjectDialogShell : MonoBehaviour
    {
        [SerializeField] private bool isOpen = true;
        [SerializeField] private SandboxProjectTemplateKind selectedTemplate = SandboxProjectTemplateKind.DefaultTemplate;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxOnboardingOverlayShell onboardingOverlay;
        private SandboxStatusBarShell statusBar;

        public bool IsOpen => isOpen;
        public SandboxProjectTemplateKind SelectedTemplate => selectedTemplate;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            onboardingOverlay = FindAnyObjectByType<SandboxOnboardingOverlayShell>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            isOpen = workspaceService == null || workspaceService.ActiveProject == null;
        }

        public void Open()
        {
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
            workspaceService?.CreateNewProject(templateKind);
            onboardingOverlay?.ShowProjectCreationGuidance();
            if (statusBar != null)
            {
                statusBar.StatusMessage = templateKind == SandboxProjectTemplateKind.DefaultTemplate
                    ? "Created default sandbox project."
                    : "Created blank sandbox project.";
            }
            selectedTemplate = templateKind;
            isOpen = false;
        }
    }
}
