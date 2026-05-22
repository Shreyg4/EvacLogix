using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxFloorTabsBarShell : MonoBehaviour
    {
        [SerializeField] private List<string> placeholderFloorNames = new() { "Floor 1" };

        private SandboxProjectWorkspaceService workspaceService;

        public IReadOnlyList<string> PlaceholderFloorNames => placeholderFloorNames;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                HandleProjectChanged(workspaceService.ActiveProject);
            }
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
            }
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            placeholderFloorNames = project == null
                ? new List<string>()
                : project.floors
                    .OrderBy(floor => floor.order)
                    .Select(floor => floor.name)
                    .ToList();
        }
    }
}
