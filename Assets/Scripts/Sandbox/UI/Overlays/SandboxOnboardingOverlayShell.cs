using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxOnboardingOverlayShell : MonoBehaviour
    {
        [SerializeField] private bool isVisible = true;
        [SerializeField] private List<string> onboardingSteps = new();

        public bool IsVisible => isVisible;
        public IReadOnlyList<string> OnboardingSteps => onboardingSteps;

        public void ShowProjectCreationGuidance()
        {
            isVisible = true;
            onboardingSteps = new List<string>
            {
                "Import a blueprint for the active floor.",
                "Calibrate scale with two reference points.",
                "Trace walls with the line or brush tool.",
                "Link stairs after floor geometry is in place.",
            };
        }

        public void Hide()
        {
            isVisible = false;
        }
    }
}
