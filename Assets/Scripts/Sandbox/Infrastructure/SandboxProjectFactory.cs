using System;
using EvacLogix.Sandbox.Data;

namespace EvacLogix.Sandbox.Infrastructure
{
    public static class SandboxProjectFactory
    {
        public static BuildingProjectData Create(SandboxProjectTemplateKind templateKind)
        {
            var timestamp = DateTime.UtcNow.ToString("O");
            var project = new BuildingProjectData
            {
                projectId = SandboxId.NewId(),
                metadata = new ProjectMetadataData
                {
                    distanceUnit = DistanceUnit.Feet,
                    createdUtc = timestamp,
                    updatedUtc = timestamp,
                }
            };

            if (templateKind == SandboxProjectTemplateKind.DefaultTemplate)
            {
                project.floors.Add(new FloorData
                {
                    floorId = SandboxId.NewId(),
                    name = "Floor 1",
                    order = 0,
                    elevation = 0f,
                });
            }

            SandboxProjectDataUtility.EnsureIds(project);
            return project;
        }
    }
}
