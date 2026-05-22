using System;
using System.IO;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxSaveLoadService : MonoBehaviour
    {
        [SerializeField] private string activeProjectId = string.Empty;
        [SerializeField] private string lastSavePath = string.Empty;
        [SerializeField] private BuildingProjectData activeProject;

        public event Action<string> ActiveProjectChanged;
        public event Action<string> ProjectSaved;
        public event Action<BuildingProjectData> ProjectLoaded;

        public string ActiveProjectId => activeProjectId;
        public string LastSavePath => lastSavePath;
        public BuildingProjectData ActiveProject => activeProject;

        public void SetActiveProject(BuildingProjectData project)
        {
            activeProject = project;
            SetActiveProjectId(project?.projectId ?? string.Empty);
        }

        public void SetActiveProjectId(string projectId)
        {
            if (string.Equals(activeProjectId, projectId, StringComparison.Ordinal))
            {
                return;
            }

            activeProjectId = projectId ?? string.Empty;
            ActiveProjectChanged?.Invoke(activeProjectId);
        }

        public string SerializeActiveProject(bool prettyPrint = true)
        {
            return SandboxProjectSerializer.Serialize(activeProject, prettyPrint);
        }

        public bool SaveActiveProjectToPath(string filePath, bool prettyPrint = true)
        {
            if (activeProject == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, SerializeActiveProject(prettyPrint));
            lastSavePath = filePath;
            activeProject.metadata.lastManualSaveUtc = DateTime.UtcNow.ToString("O");
            ProjectSaved?.Invoke(filePath);
            return true;
        }

        public BuildingProjectData LoadProjectFromPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            var project = SandboxProjectSerializer.Deserialize(json);
            SetActiveProject(project);
            lastSavePath = filePath;
            ProjectLoaded?.Invoke(project);
            return project;
        }
    }
}
