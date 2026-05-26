using System;
using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.Data
{
    public static class SandboxSchemaVersions
    {
        public const int Initial = 1;
        public const int Current = 1;
    }

    public enum BuildingLifecycleState
    {
        Draft = 0,
        ValidationFailed = 1,
        ReadyForSimulation = 2,
        ReadyForExport = 3,
    }

    public enum ValidationIssueSeverity
    {
        Warning = 0,
        BlockingError = 1,
    }

    public enum ValidationIssueType
    {
        Unknown = 0,
        Structural = 1,
        Connectivity = 2,
        Duplicate = 3,
        Conflict = 4,
        Reference = 5,
        Preview = 6,
        Serialization = 7,
    }

    public enum DoorState
    {
        Normal = 0,
        Blocked = 1,
        Locked = 2,
        OneWay = 3,
    }

    public enum ObstacleSemanticType
    {
        HardBlocking = 0,
        SlowThrough = 1,
        HazardLinkedBlockage = 2,
    }

    public enum StairTraversalDirection
    {
        Bidirectional = 0,
        AscendOnly = 1,
        DescendOnly = 2,
    }

    public enum RegionSemanticType
    {
        SpawnZone = 0,
        RestrictedZone = 1,
        Annotation = 2,
    }

    public enum DistanceUnit
    {
        Feet = 0,
        Meters = 1,
        Inches = 2,
        Centimeters = 3,
    }

    [Serializable]
    public sealed class BuildingProjectData
    {
        public int schemaVersion = SandboxSchemaVersions.Current;
        public string projectId = string.Empty;
        public ProjectMetadataData metadata = new();
        public List<BlueprintReferenceData> blueprintReferences = new();
        public List<FloorData> floors = new();
        public List<SpawnLayoutData> spawnLayouts = new();
        public List<FireOriginData> fireOrigins = new();
        public List<ScenarioPresetData> scenarioPresets = new();
        public ValidationSnapshotData validationSnapshot = new();

        public BuildingLifecycleState LifecycleState =>
            EvacLogix.Sandbox.Data.Validation.BuildingLifecycleStateUtility.Evaluate(this);
    }

    [Serializable]
    public sealed class ProjectMetadataData
    {
        public string buildingName = string.Empty;
        public string description = string.Empty;
        public string authorName = string.Empty;
        public DistanceUnit distanceUnit = DistanceUnit.Feet;
        public string createdUtc = string.Empty;
        public string updatedUtc = string.Empty;
        public string lastManualSaveUtc = string.Empty;
        public string lastRuntimeExportUtc = string.Empty;
        public string lastPreviewImageExportUtc = string.Empty;
        public List<MetadataFieldData> customFields = new();
    }

    [Serializable]
    public sealed class MetadataFieldData
    {
        public string key = string.Empty;
        public string value = string.Empty;
    }

    [Serializable]
    public sealed class ValidationSnapshotData
    {
        public string lastValidatedUtc = string.Empty;
        public List<ValidationIssueData> issues = new();
    }

    [Serializable]
    public sealed class ValidationIssueData
    {
        public string issueId = string.Empty;
        public string floorId = string.Empty;
        public string objectId = string.Empty;
        public ValidationIssueSeverity severity = ValidationIssueSeverity.Warning;
        public ValidationIssueType issueType = ValidationIssueType.Unknown;
        public string title = string.Empty;
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class BlueprintReferenceData
    {
        public string blueprintReferenceId = string.Empty;
        public string assetGuid = string.Empty;
        public string assetPath = string.Empty;
        public string sourceFileName = string.Empty;
        public float opacity = 1f;
        public bool isVisible = true;
        public bool isCalibrated;
        public Vector2 calibrationPointA;
        public Vector2 calibrationPointB;
        public float realWorldDistance = 1f;
        public float worldUnitsPerPixel = 1f;
    }

    [Serializable]
    public sealed class FloorData
    {
        public string floorId = string.Empty;
        public string name = string.Empty;
        public int order;
        public float elevation;
        public string blueprintReferenceId = string.Empty;
        public List<MetadataFieldData> floorModifiers = new();
        public List<WallJunctionData> wallJunctions = new();
        public List<WallSegmentData> wallSegments = new();
        public List<DoorData> doors = new();
        public List<WindowData> windows = new();
        public List<ExitZoneData> exits = new();
        public List<ObstacleData> obstacles = new();
        public List<StairPortalData> stairPortals = new();
        public List<RegionData> regions = new();
    }

    [Serializable]
    public sealed class WallJunctionData
    {
        public string wallJunctionId = string.Empty;
        public Vector2 position;
        public List<string> connectedWallSegmentIds = new();
    }

    [Serializable]
    public sealed class WallSegmentData
    {
        public string wallSegmentId = string.Empty;
        public string startJunctionId = string.Empty;
        public string endJunctionId = string.Empty;
        public Vector2 startPoint;
        public Vector2 endPoint;
        public float thickness = 0.2f;
        public List<string> tags = new();
    }

    [Serializable]
    public sealed class DoorData
    {
        public string doorId = string.Empty;
        public string wallSegmentId = string.Empty;
        public float offsetAlongWall;
        public float width = 1f;
        public DoorState state = DoorState.Normal;
        public List<string> tags = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class WindowData
    {
        public string windowId = string.Empty;
        public string wallSegmentId = string.Empty;
        public float offsetAlongWall;
        public float width = 1f;
        public bool canBeUsedForEscape;
        public float escapeCost = 1f;
        public float escapeRiskMultiplier = 1f;
        public List<string> tags = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class ExitZoneData
    {
        public string exitZoneId = string.Empty;
        public string name = string.Empty;
        public Vector2 center;
        public Vector2 size = Vector2.one;
        public float rotationDegrees;
        public float width = 1f;
        public float capacity;
        public float priority = 1f;
        public List<string> tags = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class ObstacleData
    {
        public string obstacleId = string.Empty;
        public string name = string.Empty;
        public ObstacleSemanticType semanticType = ObstacleSemanticType.HardBlocking;
        public Vector2 center;
        public Vector2 size = Vector2.one;
        public float rotationDegrees;
        public float traversalCostMultiplier = 1f;
        public List<string> tags = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class StairPortalData
    {
        public string stairPortalId = string.Empty;
        public string sourceFloorId = string.Empty;
        public string name = string.Empty;
        public Vector2 localPosition;
        public Vector2 size = Vector2.one;
        public float rotationDegrees;
        public string targetFloorId = string.Empty;
        public string targetStairPortalId = string.Empty;
        public StairTraversalDirection direction = StairTraversalDirection.Bidirectional;
        public float travelCost = 1f;
        public List<string> tags = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class RegionData
    {
        public string regionId = string.Empty;
        public string floorId = string.Empty;
        public string name = string.Empty;
        public RegionSemanticType semanticType = RegionSemanticType.SpawnZone;
        public List<Vector2> polygonPoints = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class SpawnLayoutData
    {
        public string spawnLayoutId = string.Empty;
        public string name = string.Empty;
        public bool isPersistent = true;
        public List<SpawnPointData> spawnPoints = new();
        public List<SpawnBrushStrokeData> spawnBrushStrokes = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class SpawnPointData
    {
        public string spawnPointId = string.Empty;
        public string floorId = string.Empty;
        public Vector2 position;
    }

    [Serializable]
    public sealed class SpawnBrushStrokeData
    {
        public string spawnBrushStrokeId = string.Empty;
        public string floorId = string.Empty;
        public float density = 1f;
        public List<Vector2> polygonPoints = new();
    }

    [Serializable]
    public sealed class FireOriginData
    {
        public string fireOriginId = string.Empty;
        public string floorId = string.Empty;
        public Vector2 position;
        public float spreadIntensity = 1f;
        public float startDelaySeconds;
        public bool isPersistent = true;
    }

    [Serializable]
    public sealed class ScenarioPresetData
    {
        public string scenarioPresetId = string.Empty;
        public string name = string.Empty;
        public List<string> spawnLayoutIds = new();
        public List<string> fireOriginIds = new();
        public PreviewParameterData previewParameters = new();
        public List<MetadataFieldData> metadataFields = new();
    }

    [Serializable]
    public sealed class PreviewParameterData
    {
        public float spreadIntensity = 1f;
        public float startDelaySeconds;
        public int previewAgentCap = 250;
    }
}
