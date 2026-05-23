using System.Collections.Generic;

namespace EvacLogix.Sandbox.Data
{
    public static class SandboxProjectDataUtility
    {
        public static void EnsureIds(BuildingProjectData project)
        {
            if (project == null)
            {
                return;
            }

            project.projectId = EnsureId(project.projectId);
            project.schemaVersion = project.schemaVersion <= 0 ? SandboxSchemaVersions.Initial : project.schemaVersion;
            project.metadata ??= new ProjectMetadataData();
            project.metadata.customFields ??= new List<MetadataFieldData>();
            project.blueprintReferences ??= new List<BlueprintReferenceData>();
            project.floors ??= new List<FloorData>();
            project.spawnLayouts ??= new List<SpawnLayoutData>();
            project.fireOrigins ??= new List<FireOriginData>();
            project.scenarioPresets ??= new List<ScenarioPresetData>();
            project.validationSnapshot ??= new ValidationSnapshotData();
            project.validationSnapshot.issues ??= new List<ValidationIssueData>();

            foreach (var issue in project.validationSnapshot.issues)
            {
                issue.issueId = EnsureId(issue.issueId);
            }

            foreach (var blueprint in project.blueprintReferences)
            {
                blueprint.blueprintReferenceId = EnsureId(blueprint.blueprintReferenceId);
            }

            foreach (var floor in project.floors)
            {
                floor.floorId = EnsureId(floor.floorId);
                floor.floorModifiers ??= new List<MetadataFieldData>();
                floor.wallJunctions ??= new List<WallJunctionData>();
                floor.wallSegments ??= new List<WallSegmentData>();
                floor.doors ??= new List<DoorData>();
                floor.windows ??= new List<WindowData>();
                floor.exits ??= new List<ExitZoneData>();
                floor.obstacles ??= new List<ObstacleData>();
                floor.stairPortals ??= new List<StairPortalData>();
                floor.regions ??= new List<RegionData>();

                foreach (var junction in floor.wallJunctions)
                {
                    junction.wallJunctionId = EnsureId(junction.wallJunctionId);
                    junction.connectedWallSegmentIds ??= new List<string>();
                }

                foreach (var wall in floor.wallSegments)
                {
                    wall.wallSegmentId = EnsureId(wall.wallSegmentId);
                    wall.tags ??= new List<string>();
                }

                foreach (var door in floor.doors)
                {
                    door.doorId = EnsureId(door.doorId);
                    door.tags ??= new List<string>();
                    door.metadataFields ??= new List<MetadataFieldData>();
                }

                foreach (var window in floor.windows)
                {
                    window.windowId = EnsureId(window.windowId);
                    window.tags ??= new List<string>();
                    window.metadataFields ??= new List<MetadataFieldData>();
                }

                foreach (var exit in floor.exits)
                {
                    exit.exitZoneId = EnsureId(exit.exitZoneId);
                    exit.tags ??= new List<string>();
                    exit.metadataFields ??= new List<MetadataFieldData>();
                }

                foreach (var obstacle in floor.obstacles)
                {
                    obstacle.obstacleId = EnsureId(obstacle.obstacleId);
                    obstacle.tags ??= new List<string>();
                    obstacle.metadataFields ??= new List<MetadataFieldData>();
                }

                foreach (var stair in floor.stairPortals)
                {
                    stair.stairPortalId = EnsureId(stair.stairPortalId);
                    stair.sourceFloorId = string.IsNullOrWhiteSpace(stair.sourceFloorId) ? floor.floorId : stair.sourceFloorId;
                    stair.tags ??= new List<string>();
                    stair.metadataFields ??= new List<MetadataFieldData>();
                }

                foreach (var region in floor.regions)
                {
                    region.regionId = EnsureId(region.regionId);
                    region.floorId = string.IsNullOrWhiteSpace(region.floorId) ? floor.floorId : region.floorId;
                    region.polygonPoints ??= new List<UnityEngine.Vector2>();
                    region.metadataFields ??= new List<MetadataFieldData>();
                }
            }

            foreach (var layout in project.spawnLayouts)
            {
                layout.spawnLayoutId = EnsureId(layout.spawnLayoutId);
                layout.spawnPoints ??= new List<SpawnPointData>();
                layout.spawnBrushStrokes ??= new List<SpawnBrushStrokeData>();
                layout.metadataFields ??= new List<MetadataFieldData>();

                foreach (var point in layout.spawnPoints)
                {
                    point.spawnPointId = EnsureId(point.spawnPointId);
                }

                foreach (var stroke in layout.spawnBrushStrokes)
                {
                    stroke.spawnBrushStrokeId = EnsureId(stroke.spawnBrushStrokeId);
                    stroke.polygonPoints ??= new List<UnityEngine.Vector2>();
                }
            }

            foreach (var fireOrigin in project.fireOrigins)
            {
                fireOrigin.fireOriginId = EnsureId(fireOrigin.fireOriginId);
            }

            foreach (var scenario in project.scenarioPresets)
            {
                scenario.scenarioPresetId = EnsureId(scenario.scenarioPresetId);
                scenario.spawnLayoutIds ??= new List<string>();
                scenario.fireOriginIds ??= new List<string>();
                scenario.previewParameters ??= new PreviewParameterData();
                scenario.metadataFields ??= new List<MetadataFieldData>();
            }
        }

        private static string EnsureId(string currentId)
        {
            return string.IsNullOrWhiteSpace(currentId) ? SandboxId.NewId() : currentId;
        }
    }
}
