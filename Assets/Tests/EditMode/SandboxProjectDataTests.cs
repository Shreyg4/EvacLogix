using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Migrations;
using EvacLogix.Sandbox.Data.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxProjectDataTests
    {
        [Test]
        public void SerializeDeserialize_RoundTripsTwoFloorProjectLosslessly()
        {
            var originalProject = CreateSampleProject();

            var serialized = SandboxProjectSerializer.Serialize(originalProject, false);
            var roundTrippedProject = SandboxProjectSerializer.Deserialize(serialized);
            var serializedAgain = SandboxProjectSerializer.Serialize(roundTrippedProject, false);

            Assert.That(serializedAgain, Is.EqualTo(serialized));
        }

        [Test]
        public void EnsureIds_FillsMissingIdsAndCarriesFloorReferences()
        {
            var project = new BuildingProjectData
            {
                floors =
                {
                    new FloorData
                    {
                        name = "Floor A",
                        regions =
                        {
                            new RegionData
                            {
                                name = "Spawn Zone"
                            }
                        },
                        wallSegments =
                        {
                            new WallSegmentData()
                        }
                    }
                },
                spawnLayouts =
                {
                    new SpawnLayoutData
                    {
                        spawnPoints =
                        {
                            new SpawnPointData()
                        }
                    }
                },
                fireOrigins =
                {
                    new FireOriginData()
                },
                scenarioPresets =
                {
                    new ScenarioPresetData()
                }
            };

            SandboxProjectDataUtility.EnsureIds(project);

            Assert.That(project.projectId, Is.Not.Empty);
            Assert.That(project.floors[0].floorId, Is.Not.Empty);
            Assert.That(project.floors[0].regions[0].regionId, Is.Not.Empty);
            Assert.That(project.floors[0].regions[0].floorId, Is.EqualTo(project.floors[0].floorId));
            Assert.That(project.floors[0].wallSegments[0].wallSegmentId, Is.Not.Empty);
            Assert.That(project.spawnLayouts[0].spawnLayoutId, Is.Not.Empty);
            Assert.That(project.spawnLayouts[0].spawnPoints[0].spawnPointId, Is.Not.Empty);
            Assert.That(project.fireOrigins[0].fireOriginId, Is.Not.Empty);
            Assert.That(project.scenarioPresets[0].scenarioPresetId, Is.Not.Empty);
        }

        [Test]
        public void LifecycleState_IsDerivedFromValidationAndExportState()
        {
            var draftProject = new BuildingProjectData();
            Assert.That(draftProject.LifecycleState, Is.EqualTo(BuildingLifecycleState.Draft));

            var validProject = CreateSampleProject();
            validProject.validationSnapshot.issues.Clear();
            validProject.metadata.lastRuntimeExportUtc = string.Empty;
            Assert.That(validProject.LifecycleState, Is.EqualTo(BuildingLifecycleState.ReadyForSimulation));

            validProject.validationSnapshot.issues.Add(new ValidationIssueData
            {
                issueId = "issue-blocking",
                severity = ValidationIssueSeverity.BlockingError
            });
            Assert.That(validProject.LifecycleState, Is.EqualTo(BuildingLifecycleState.ValidationFailed));

            validProject.validationSnapshot.issues.Clear();
            validProject.metadata.lastRuntimeExportUtc = "2026-05-22T00:00:00Z";
            Assert.That(validProject.LifecycleState, Is.EqualTo(BuildingLifecycleState.ReadyForExport));
        }

        [Test]
        public void Deserialize_MigratesSchemaVersionOneProjectsToCurrent()
        {
            var originalProject = CreateSampleProject();
            originalProject.schemaVersion = SandboxSchemaVersions.Initial;

            var serialized = SandboxProjectSerializer.Serialize(originalProject, false);
            var deserializedProject = SandboxProjectSerializer.Deserialize(serialized);

            Assert.That(deserializedProject.schemaVersion, Is.EqualTo(SandboxSchemaVersions.Current));
            Assert.That(deserializedProject.projectId, Is.EqualTo(originalProject.projectId));
        }

        [Test]
        public void Deserialize_ThrowsForUnsupportedFutureSchemaVersion()
        {
            var json = "{\"schemaVersion\":999,\"projectId\":\"future-project\"}";

            Assert.Throws<SandboxMigrationException>(() => SandboxProjectSerializer.Deserialize(json));
        }

        private static BuildingProjectData CreateSampleProject()
        {
            return new BuildingProjectData
            {
                schemaVersion = SandboxSchemaVersions.Current,
                projectId = "project-main",
                metadata = new ProjectMetadataData
                {
                    buildingName = "Sieg Hall",
                    description = "Two-floor authoring sample",
                    authorName = "EvacLogix",
                    createdUtc = "2026-05-22T00:00:00Z",
                    updatedUtc = "2026-05-22T01:00:00Z",
                    lastManualSaveUtc = "2026-05-22T01:00:00Z",
                    customFields =
                    {
                        new MetadataFieldData { key = "campus", value = "UW" }
                    }
                },
                blueprintReferences =
                {
                    new BlueprintReferenceData
                    {
                        blueprintReferenceId = "bp-floor-1",
                        assetGuid = "guid-floor-1",
                        assetPath = "Assets/Blueprints/floor1.png",
                        sourceFileName = "floor1.png",
                        opacity = 0.75f,
                        isCalibrated = true,
                        calibrationPointA = new Vector2(0f, 0f),
                        calibrationPointB = new Vector2(400f, 0f),
                        realWorldDistance = 20f,
                        worldUnitsPerPixel = 0.05f
                    },
                    new BlueprintReferenceData
                    {
                        blueprintReferenceId = "bp-floor-2",
                        assetGuid = "guid-floor-2",
                        assetPath = "Assets/Blueprints/floor2.png",
                        sourceFileName = "floor2.png",
                        opacity = 0.8f,
                        isCalibrated = true,
                        calibrationPointA = new Vector2(10f, 10f),
                        calibrationPointB = new Vector2(510f, 10f),
                        realWorldDistance = 25f,
                        worldUnitsPerPixel = 0.05f
                    }
                },
                floors =
                {
                    CreateFirstFloor(),
                    CreateSecondFloor()
                },
                spawnLayouts =
                {
                    new SpawnLayoutData
                    {
                        spawnLayoutId = "spawn-layout-main",
                        name = "Main Entry Crowds",
                        isPersistent = true,
                        spawnPoints =
                        {
                            new SpawnPointData
                            {
                                spawnPointId = "spawn-point-1",
                                floorId = "floor-1",
                                position = new Vector2(4f, 3f)
                            }
                        },
                        spawnBrushStrokes =
                        {
                            new SpawnBrushStrokeData
                            {
                                spawnBrushStrokeId = "spawn-brush-1",
                                floorId = "floor-2",
                                density = 0.4f,
                                polygonPoints =
                                {
                                    new Vector2(2f, 2f),
                                    new Vector2(4f, 2f),
                                    new Vector2(4f, 5f)
                                }
                            }
                        }
                    }
                },
                fireOrigins =
                {
                    new FireOriginData
                    {
                        fireOriginId = "fire-1",
                        floorId = "floor-1",
                        position = new Vector2(8f, 6f),
                        spreadIntensity = 1.5f,
                        startDelaySeconds = 3f,
                        isPersistent = true
                    }
                },
                scenarioPresets =
                {
                    new ScenarioPresetData
                    {
                        scenarioPresetId = "scenario-1",
                        name = "Lobby Fire",
                        spawnLayoutIds =
                        {
                            "spawn-layout-main"
                        },
                        fireOriginIds =
                        {
                            "fire-1"
                        },
                        previewParameters = new PreviewParameterData
                        {
                            spreadIntensity = 1.5f,
                            startDelaySeconds = 3f,
                            previewAgentCap = 180
                        }
                    }
                },
                validationSnapshot = new ValidationSnapshotData
                {
                    lastValidatedUtc = "2026-05-22T01:30:00Z",
                    issues =
                    {
                        new ValidationIssueData
                        {
                            issueId = "warn-1",
                            floorId = "floor-2",
                            objectId = "obstacle-2",
                            severity = ValidationIssueSeverity.Warning,
                            issueType = ValidationIssueType.Conflict,
                            title = "Dense obstacle grouping",
                            message = "Obstacle placement may create a local choke point."
                        }
                    }
                }
            };
        }

        private static FloorData CreateFirstFloor()
        {
            return new FloorData
            {
                floorId = "floor-1",
                name = "Ground Floor",
                order = 0,
                elevation = 0f,
                blueprintReferenceId = "bp-floor-1",
                floorModifiers =
                {
                    new MetadataFieldData { key = "windowRisk", value = "1.0" }
                },
                wallJunctions =
                {
                    new WallJunctionData
                    {
                        wallJunctionId = "junction-1a",
                        position = new Vector2(0f, 0f),
                        connectedWallSegmentIds = { "wall-1a" }
                    },
                    new WallJunctionData
                    {
                        wallJunctionId = "junction-1b",
                        position = new Vector2(10f, 0f),
                        connectedWallSegmentIds = { "wall-1a" }
                    }
                },
                wallSegments =
                {
                    new WallSegmentData
                    {
                        wallSegmentId = "wall-1a",
                        startJunctionId = "junction-1a",
                        endJunctionId = "junction-1b",
                        startPoint = new Vector2(0f, 0f),
                        endPoint = new Vector2(10f, 0f),
                        thickness = 0.25f,
                        tags = { "perimeter" }
                    }
                },
                doors =
                {
                    new DoorData
                    {
                        doorId = "door-1",
                        wallSegmentId = "wall-1a",
                        offsetAlongWall = 2f,
                        width = 1.25f,
                        state = DoorState.Normal
                    }
                },
                windows =
                {
                    new WindowData
                    {
                        windowId = "window-1",
                        wallSegmentId = "wall-1a",
                        offsetAlongWall = 7f,
                        width = 1.4f,
                        canBeUsedForEscape = true,
                        escapeCost = 2f,
                        escapeRiskMultiplier = 1.1f
                    }
                },
                exits =
                {
                    new ExitZoneData
                    {
                        exitZoneId = "exit-1",
                        name = "Main Exit",
                        center = new Vector2(10f, 1f),
                        size = new Vector2(2f, 1f),
                        rotationDegrees = 0f,
                        width = 2f,
                        capacity = 40f,
                        priority = 1f
                    }
                },
                obstacles =
                {
                    new ObstacleData
                    {
                        obstacleId = "obstacle-1",
                        name = "Front Desk",
                        semanticType = ObstacleSemanticType.HardBlocking,
                        center = new Vector2(5f, 3f),
                        size = new Vector2(2f, 1f),
                        rotationDegrees = 15f
                    }
                },
                stairPortals =
                {
                    new StairPortalData
                    {
                        stairPortalId = "stair-1-down",
                        name = "Stair A Lower",
                        localPosition = new Vector2(9f, 6f),
                        targetFloorId = "floor-2",
                        targetStairPortalId = "stair-2-up",
                        direction = StairTraversalDirection.Bidirectional,
                        travelCost = 1.2f
                    }
                },
                regions =
                {
                    new RegionData
                    {
                        regionId = "region-1",
                        floorId = "floor-1",
                        name = "Lobby Spawn Zone",
                        semanticType = RegionSemanticType.SpawnZone,
                        polygonPoints =
                        {
                            new Vector2(1f, 1f),
                            new Vector2(4f, 1f),
                            new Vector2(4f, 4f),
                            new Vector2(1f, 4f)
                        }
                    }
                }
            };
        }

        private static FloorData CreateSecondFloor()
        {
            return new FloorData
            {
                floorId = "floor-2",
                name = "Upper Floor",
                order = 1,
                elevation = 4f,
                blueprintReferenceId = "bp-floor-2",
                floorModifiers =
                {
                    new MetadataFieldData { key = "windowRisk", value = "1.8" }
                },
                wallJunctions =
                {
                    new WallJunctionData
                    {
                        wallJunctionId = "junction-2a",
                        position = new Vector2(0f, 0f),
                        connectedWallSegmentIds = { "wall-2a" }
                    },
                    new WallJunctionData
                    {
                        wallJunctionId = "junction-2b",
                        position = new Vector2(0f, 10f),
                        connectedWallSegmentIds = { "wall-2a" }
                    }
                },
                wallSegments =
                {
                    new WallSegmentData
                    {
                        wallSegmentId = "wall-2a",
                        startJunctionId = "junction-2a",
                        endJunctionId = "junction-2b",
                        startPoint = new Vector2(0f, 0f),
                        endPoint = new Vector2(0f, 10f),
                        thickness = 0.25f,
                        tags = { "interior" }
                    }
                },
                exits =
                {
                    new ExitZoneData
                    {
                        exitZoneId = "exit-2",
                        name = "Balcony Exit",
                        center = new Vector2(2f, 9f),
                        size = new Vector2(2.5f, 1f),
                        rotationDegrees = 90f,
                        width = 2.5f,
                        capacity = 18f,
                        priority = 0.6f
                    }
                },
                obstacles =
                {
                    new ObstacleData
                    {
                        obstacleId = "obstacle-2",
                        name = "Display Cases",
                        semanticType = ObstacleSemanticType.SlowThrough,
                        center = new Vector2(3f, 5f),
                        size = new Vector2(3f, 1f),
                        rotationDegrees = 0f,
                        traversalCostMultiplier = 1.6f
                    }
                },
                stairPortals =
                {
                    new StairPortalData
                    {
                        stairPortalId = "stair-2-up",
                        name = "Stair A Upper",
                        localPosition = new Vector2(1f, 6f),
                        targetFloorId = "floor-1",
                        targetStairPortalId = "stair-1-down",
                        direction = StairTraversalDirection.Bidirectional,
                        travelCost = 1.2f
                    }
                },
                regions =
                {
                    new RegionData
                    {
                        regionId = "region-2",
                        floorId = "floor-2",
                        name = "Upper Restriction",
                        semanticType = RegionSemanticType.RestrictedZone,
                        polygonPoints =
                        {
                            new Vector2(6f, 6f),
                            new Vector2(8f, 6f),
                            new Vector2(8f, 8f),
                            new Vector2(6f, 8f)
                        }
                    }
                }
            };
        }
    }
}
