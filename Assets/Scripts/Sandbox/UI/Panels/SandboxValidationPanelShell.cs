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

        private SandboxProjectRefreshService projectRefreshService;
        private SandboxRoomDetectionService roomDetectionService;
        private SandboxValidationService validationService;
        private SandboxStatusBarShell statusBar;

        public bool StartCollapsed => startCollapsed;
        public IReadOnlyList<SandboxValidationFloorGroup> IssueGroups => issueGroups;
        public bool HasBlockingIssues => hasBlockingIssues;
        public bool ShowCompleteRooms => roomDetectionService != null && roomDetectionService.ShowCompleteRooms;
        public string RoomDetectionStatus => roomDetectionService?.LastStatusMessage ?? "Room detector unavailable.";
        public int CompleteRoomCount => roomDetectionService?.DetectedRooms.Count ?? 0;
        public int SealedRoomCount => roomDetectionService?.SealedRoomCount ?? 0;
        public int PenetratedRoomCount => roomDetectionService?.PenetratedRoomCount ?? 0;

        private void Awake()
        {
            projectRefreshService = FindAnyObjectByType<SandboxProjectRefreshService>();
            roomDetectionService = FindAnyObjectByType<SandboxRoomDetectionService>();
            validationService = FindAnyObjectByType<SandboxValidationService>();
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

        public void SetShowCompleteRooms(bool enabled)
        {
            roomDetectionService ??= FindAnyObjectByType<SandboxRoomDetectionService>();
            roomDetectionService?.SetShowCompleteRooms(enabled);
            if (statusBar != null)
            {
                statusBar.StatusMessage = roomDetectionService?.LastStatusMessage ?? "Room detector unavailable.";
            }
        }

        public void RefreshCompleteRooms()
        {
            roomDetectionService ??= FindAnyObjectByType<SandboxRoomDetectionService>();
            roomDetectionService?.Recalculate();
            if (statusBar != null)
            {
                statusBar.StatusMessage = roomDetectionService?.LastStatusMessage ?? "Room detector unavailable.";
            }
        }

        public void RebuildAll()
        {
            projectRefreshService?.RefreshDerivedProjectState();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Rebuilt all colliders.";
            }
        }

        public void RefreshValidation()
        {
            projectRefreshService?.RefreshValidationOnly();
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
