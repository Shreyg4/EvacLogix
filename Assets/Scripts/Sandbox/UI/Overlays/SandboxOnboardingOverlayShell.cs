using System.Collections.Generic;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxOnboardingOverlayShell : MonoBehaviour
    {
        [SerializeField] private bool isVisible = true;
        [SerializeField] private List<string> onboardingSteps = new();
        [SerializeField] private string toolHelpText = string.Empty;
        [SerializeField] private string validationHelpText = string.Empty;

        private SandboxEditorQoLService editorQoLService;

        public bool IsVisible => isVisible;
        public IReadOnlyList<string> OnboardingSteps => onboardingSteps;
        public string ToolHelpText => toolHelpText;
        public string ValidationHelpText => validationHelpText;

        private void Awake()
        {
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            if (editorQoLService != null)
            {
                editorQoLService.StateChanged += HandleStateChanged;
            }

            RefreshFromState();
        }

        private void OnDestroy()
        {
            if (editorQoLService != null)
            {
                editorQoLService.StateChanged -= HandleStateChanged;
            }
        }

        public void ShowProjectCreationGuidance()
        {
            editorQoLService?.ShowFirstRunOnboarding();
            RefreshFromState();
        }

        public void Hide()
        {
            editorQoLService?.DismissFirstRunOnboarding();
            RefreshFromState();
        }

        private void HandleStateChanged()
        {
            RefreshFromState();
        }

        private void RefreshFromState()
        {
            isVisible = editorQoLService == null || editorQoLService.FirstRunOnboardingVisible;
            onboardingSteps = new List<string>
            {
                "Import a blueprint for the active floor.",
                "Calibrate scale with two reference points.",
                "Trace walls with the line or brush tool.",
                "Name exits, stairs, and important obstacles before preview.",
                "Run validation often so blocking issues never pile up."
            };

            toolHelpText = editorQoLService != null && editorQoLService.TooltipsEnabled
                ? editorQoLService.CurrentToolHelpText
                : string.Empty;
            validationHelpText = editorQoLService != null && editorQoLService.ValidationHelpEnabled
                ? editorQoLService.CurrentValidationHelpText
                : string.Empty;
        }
    }
}
