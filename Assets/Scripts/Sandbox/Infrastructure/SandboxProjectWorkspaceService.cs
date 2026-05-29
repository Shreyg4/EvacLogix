using System;
using System.Linq;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxProjectTemplateKind
    {
        DefaultTemplate = 0,
        BlankTemplate = 1,
    }

    public sealed class SandboxProjectWorkspaceService : MonoBehaviour
    {
        [SerializeField] private BuildingProjectData activeProject;
        [SerializeField] private string activeFloorId = string.Empty;

        private SandboxSaveLoadService saveLoadService;

        public event Action<BuildingProjectData> ActiveProjectChanged;
        public event Action<FloorData> ActiveFloorChanged;

        public BuildingProjectData ActiveProject => activeProject;
        public string ActiveFloorId => activeFloorId;
        public FloorData ActiveFloor => FindFloor(activeFloorId);

        private void Awake()
        {
            saveLoadService = GetComponent<SandboxSaveLoadService>();
            if (saveLoadService != null)
            {
                saveLoadService.ProjectLoaded += HandleProjectLoaded;
            }
        }

        private void OnDestroy()
        {
            if (saveLoadService != null)
            {
                saveLoadService.ProjectLoaded -= HandleProjectLoaded;
            }
        }

        public BuildingProjectData CreateNewProject(SandboxProjectTemplateKind templateKind)
        {
            var project = SandboxProjectFactory.Create(templateKind);
            SetActiveProject(project);
            return project;
        }

        public void SetActiveProject(BuildingProjectData project)
        {
            activeProject = project ?? SandboxProjectFactory.Create(SandboxProjectTemplateKind.BlankTemplate);
            SandboxProjectDataUtility.EnsureIds(activeProject);

            if (activeProject.floors.Count == 0)
            {
                activeFloorId = string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(activeFloorId) || FindFloor(activeFloorId) == null)
            {
                activeFloorId = activeProject.floors[0].floorId;
            }

            saveLoadService?.SetActiveProject(activeProject);
            ActiveProjectChanged?.Invoke(activeProject);
            ActiveFloorChanged?.Invoke(ActiveFloor);
        }

        public void SetActiveFloor(string floorId)
        {
            if (string.IsNullOrWhiteSpace(floorId))
            {
                activeFloorId = string.Empty;
                ActiveFloorChanged?.Invoke(null);
                return;
            }

            var floor = FindFloor(floorId);
            if (floor == null || string.Equals(activeFloorId, floor.floorId, StringComparison.Ordinal))
            {
                return;
            }

            activeFloorId = floor.floorId;
            ActiveFloorChanged?.Invoke(floor);
        }

        public bool AssignBlueprintToFloor(string floorId, string blueprintReferenceId)
        {
            var floor = FindFloor(floorId);
            if (floor == null)
            {
                return false;
            }

            var blueprintReference = FindBlueprintReference(blueprintReferenceId);
            if (blueprintReference == null)
            {
                return false;
            }

            floor.blueprintReferenceId = blueprintReference.blueprintReferenceId;
            ActiveProjectChanged?.Invoke(activeProject);
            if (string.Equals(activeFloorId, floor.floorId, StringComparison.Ordinal))
            {
                ActiveFloorChanged?.Invoke(floor);
            }

            return true;
        }

        public bool SetBlueprintOpacity(string blueprintReferenceId, float opacity)
        {
            var blueprintReference = FindBlueprintReference(blueprintReferenceId);
            if (blueprintReference == null)
            {
                return false;
            }

            blueprintReference.opacity = Mathf.Clamp01(opacity);
            ActiveProjectChanged?.Invoke(activeProject);
            return true;
        }

        public bool SetBlueprintDisplayScale(string blueprintReferenceId, float displayScale)
        {
            var blueprintReference = FindBlueprintReference(blueprintReferenceId);
            if (blueprintReference == null)
            {
                return false;
            }

            blueprintReference.displayScale = Mathf.Clamp(displayScale, 0.1f, 4f);
            ActiveProjectChanged?.Invoke(activeProject);
            return true;
        }

        public bool SetBlueprintVisibility(string blueprintReferenceId, bool isVisible)
        {
            var blueprintReference = FindBlueprintReference(blueprintReferenceId);
            if (blueprintReference == null)
            {
                return false;
            }

            blueprintReference.isVisible = isVisible;
            ActiveProjectChanged?.Invoke(activeProject);
            return true;
        }

        public void AddBlueprintReference(BlueprintReferenceData blueprintReference)
        {
            if (activeProject == null || blueprintReference == null)
            {
                return;
            }

            SandboxProjectDataUtility.EnsureIds(activeProject);
            if (activeProject.blueprintReferences.Any(existing =>
                    string.Equals(existing.blueprintReferenceId, blueprintReference.blueprintReferenceId, StringComparison.Ordinal)))
            {
                return;
            }

            activeProject.blueprintReferences.Add(blueprintReference);
            ActiveProjectChanged?.Invoke(activeProject);
        }

        public FloorData FindFloor(string floorId)
        {
            if (activeProject == null || string.IsNullOrWhiteSpace(floorId))
            {
                return null;
            }

            return activeProject.floors.FirstOrDefault(floor => string.Equals(floor.floorId, floorId, StringComparison.Ordinal));
        }

        public BlueprintReferenceData FindBlueprintReference(string blueprintReferenceId)
        {
            if (activeProject == null || string.IsNullOrWhiteSpace(blueprintReferenceId))
            {
                return null;
            }

            return activeProject.blueprintReferences.FirstOrDefault(reference =>
                string.Equals(reference.blueprintReferenceId, blueprintReferenceId, StringComparison.Ordinal));
        }

        private void HandleProjectLoaded(BuildingProjectData project)
        {
            SetActiveProject(project);
        }
    }
}
