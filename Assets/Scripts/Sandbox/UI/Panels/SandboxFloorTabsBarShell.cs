using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    [System.Serializable]
    public sealed class SandboxFloorTabEntry
    {
        public string floorId = string.Empty;
        public string name = string.Empty;
        public int order;
        public float elevation;
        public bool isActive;
    }

    public sealed class SandboxFloorTabsBarShell : MonoBehaviour
    {
        [SerializeField] private List<SandboxFloorTabEntry> floorTabs = new();
        [SerializeField] private List<string> placeholderFloorNames = new() { "Floor 1" };
        [SerializeField] private bool hasPendingDeleteConfirmation;
        [SerializeField] private string pendingDeleteMessage = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxFloorManagementService floorManagementService;
        private SandboxStatusBarShell statusBar;

        public IReadOnlyList<SandboxFloorTabEntry> FloorTabs => floorTabs;
        public IReadOnlyList<string> PlaceholderFloorNames => placeholderFloorNames;
        public bool HasPendingDeleteConfirmation => hasPendingDeleteConfirmation;
        public string PendingDeleteMessage => pendingDeleteMessage;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            floorManagementService = FindAnyObjectByType<SandboxFloorManagementService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();

            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleActiveFloorChanged;
            }

            if (floorManagementService != null)
            {
                floorManagementService.FloorsChanged += HandleFloorsChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleActiveFloorChanged;
            }

            if (floorManagementService != null)
            {
                floorManagementService.FloorsChanged -= HandleFloorsChanged;
            }
        }

        public void SelectFloor(string floorId)
        {
            workspaceService?.SetActiveFloor(floorId);
        }

        public bool AddFloor(string name = "", float elevation = 0f)
        {
            if (floorManagementService == null || !floorManagementService.AddFloor(out _, name, elevation))
            {
                return false;
            }

            UpdateStatus("Added floor.");
            return true;
        }

        public bool AddSurfaceFloor(string name = "")
        {
            if (floorManagementService == null || !floorManagementService.AddSurfaceFloor(out _, name))
            {
                return false;
            }

            UpdateStatus("Added surface floor.");
            return true;
        }

        public bool AddBasementFloor(string name = "")
        {
            if (floorManagementService == null || !floorManagementService.AddBasementFloor(out _, name))
            {
                return false;
            }

            UpdateStatus("Added basement floor.");
            return true;
        }

        public bool RenameFloor(string floorId, string name)
        {
            if (floorManagementService == null || !floorManagementService.RenameFloor(floorId, name))
            {
                return false;
            }

            UpdateStatus("Renamed floor.");
            return true;
        }

        public bool UpdateFloorMetadata(string floorId, string name, int order, float elevation)
        {
            if (floorManagementService == null || !floorManagementService.UpdateFloorMetadata(floorId, name, order, elevation))
            {
                return false;
            }

            UpdateStatus("Updated floor metadata.");
            return true;
        }

        public bool ReorderFloor(string floorId, int newIndex)
        {
            if (floorManagementService == null || !floorManagementService.ReorderFloor(floorId, newIndex))
            {
                return false;
            }

            UpdateStatus("Reordered floor tabs.");
            return true;
        }

        public bool DuplicateFloor(string floorId)
        {
            if (floorManagementService == null || !floorManagementService.DuplicateFloor(floorId, out _))
            {
                return false;
            }

            UpdateStatus("Duplicated floor.");
            return true;
        }

        public bool RequestDeleteFloor(string floorId)
        {
            if (floorManagementService == null)
            {
                return false;
            }

            var didDeleteImmediately = floorManagementService.RequestDeleteFloor(floorId);
            RefreshPendingDeleteState();
            if (didDeleteImmediately)
            {
                UpdateStatus("Deleted floor.");
                return true;
            }

            if (hasPendingDeleteConfirmation)
            {
                UpdateStatus(pendingDeleteMessage);
            }

            return false;
        }

        public bool ConfirmDeleteFloor()
        {
            if (floorManagementService == null || !floorManagementService.ConfirmPendingDeleteFloor())
            {
                return false;
            }

            RefreshPendingDeleteState();
            UpdateStatus("Deleted floor after confirmation.");
            return true;
        }

        public void CancelDeleteFloor()
        {
            floorManagementService?.CancelPendingDeleteFloor();
            RefreshPendingDeleteState();
        }

        public void Refresh()
        {
            var orderedFloors = floorManagementService?.GetOrderedFloors()
                ?? workspaceService?.ActiveProject?.floors.OrderBy(floor => floor.order).ToList()
                ?? new List<FloorData>();

            floorTabs = orderedFloors
                .Select(floor => new SandboxFloorTabEntry
                {
                    floorId = floor.floorId,
                    name = floor.name,
                    order = floor.order,
                    elevation = floor.elevation,
                    isActive = workspaceService != null && floor.floorId == workspaceService.ActiveFloorId
                })
                .ToList();

            placeholderFloorNames = floorTabs.Select(tab => tab.name).ToList();
            RefreshPendingDeleteState();
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            Refresh();
        }

        private void HandleActiveFloorChanged(FloorData floor)
        {
            Refresh();
        }

        private void HandleFloorsChanged()
        {
            Refresh();
        }

        private void RefreshPendingDeleteState()
        {
            hasPendingDeleteConfirmation = floorManagementService != null && floorManagementService.HasPendingDeleteConfirmation;
            pendingDeleteMessage = floorManagementService?.PendingDeleteMessage ?? string.Empty;
        }

        private void UpdateStatus(string message)
        {
            if (statusBar != null)
            {
                statusBar.StatusMessage = message;
            }
        }
    }
}
