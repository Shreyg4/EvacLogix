using EvacLogix.Sandbox.Data.Migrations;
using UnityEngine;

namespace EvacLogix.Sandbox.Data.Serialization
{
    public static class SandboxProjectSerializer
    {
        public static string Serialize(BuildingProjectData project, bool prettyPrint = true)
        {
            var normalizedProject = CloneWithoutFormatting(project);
            return JsonUtility.ToJson(normalizedProject, prettyPrint);
        }

        public static BuildingProjectData Deserialize(string json)
        {
            var project = string.IsNullOrWhiteSpace(json)
                ? new BuildingProjectData()
                : JsonUtility.FromJson<BuildingProjectData>(json) ?? new BuildingProjectData();

            SandboxProjectMigrator.MigrateToCurrent(project);
            SandboxProjectDataUtility.EnsureIds(project);
            return project;
        }

        public static BuildingProjectData Clone(BuildingProjectData project)
        {
            return Deserialize(Serialize(project, false));
        }

        private static BuildingProjectData CloneWithoutFormatting(BuildingProjectData project)
        {
            var normalizedProject = project == null
                ? new BuildingProjectData()
                : JsonUtility.FromJson<BuildingProjectData>(JsonUtility.ToJson(project)) ?? new BuildingProjectData();

            SandboxProjectMigrator.MigrateToCurrent(normalizedProject);
            SandboxProjectDataUtility.EnsureIds(normalizedProject);
            return normalizedProject;
        }
    }
}
