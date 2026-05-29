using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxValidationPanelShell : MonoBehaviour
    {
        [SerializeField] private bool startCollapsed;
        [SerializeField] private List<SandboxValidationFloorGroup> issueGroups = new();
        [SerializeField] private bool hasBlockingIssues;

        private SandboxValidationService validationService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxStatusBarShell statusBar;

        public bool StartCollapsed => startCollapsed;
        public IReadOnlyList<SandboxValidationFloorGroup> IssueGroups => issueGroups;
        public bool HasBlockingIssues => hasBlockingIssues;

        private void Awake()
        {
            validationService = FindAnyObjectByType<SandboxValidationService>();
            colliderRebuildService = FindAnyObjectByType<SandboxColliderRebuildService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged += HandleIssuesChanged;
                HandleIssuesChanged(validationService.Issues);
            }
        }

        private void OnDestroy()
        {
            if (validationService != null)
            {
                validationService.ValidationIssuesChanged -= HandleIssuesChanged;
            }
        }

        public void RebuildAll()
        {
            colliderRebuildService?.RebuildAll();
            validationService?.ValidateActiveProject();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Rebuilt all colliders.";
            }
        }

        public void RefreshValidation()
        {
            validationService?.ValidateActiveProject();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Validation refreshed.";
            }
        }

        private void HandleIssuesChanged(IReadOnlyList<ValidationIssueData> nextIssues)
        {
            issueGroups = validationService == null
                ? new List<SandboxValidationFloorGroup>()
                : new List<SandboxValidationFloorGroup>(validationService.GroupedIssues);
            hasBlockingIssues = validationService != null && validationService.HasBlockingIssues;
        }
    }
}
