using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring
{
    public sealed class SandboxSemanticObjectAuthoringService : MonoBehaviour
    {
        [SerializeField] private float wallAttachDistance = 0.6f;
        [SerializeField] private Vector2 defaultExitZoneSize = new(1.5f, 1.5f);
        [SerializeField] private Vector2 defaultObstacleSize = Vector2.one;
        [SerializeField] private float defaultExitWidth = 1.5f;
        [SerializeField] private float obstacleRotationStepDegrees = 15f;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxSelectionService selectionService;
        private SandboxValidationService validationService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;

        public event Action SemanticObjectsChanged;

        public float WallAttachDistance => wallAttachDistance;
        public float ObstacleRotationStepDegrees => obstacleRotationStepDegrees;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            selectionService = GetComponent<SandboxSelectionService>();
            validationService = GetComponent<SandboxValidationService>();
            visualOrganizationService = GetComponent<SandboxVisualOrganizationService>();
            previewService = GetComponent<SandboxPreviewService>();
        }

        public bool PlaceDoor(Vector2 worldPoint, out string doorId, float width = 1f, DoorState state = DoorState.Normal)
        {
            doorId = string.Empty;
            if (workspaceService?.ActiveFloor == null || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Door))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            if (!TryFindNearestWall(workspaceService.ActiveFloor, worldPoint, out var wall, out _, out var offsetAlongWall))
            {
                return false;
            }

            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var halfWidth = width * 0.5f;
            if (wallLength <= Mathf.Epsilon || offsetAlongWall - halfWidth < -0.01f || offsetAlongWall + halfWidth > wallLength + 0.01f)
            {
                return false;
            }

            var createdDoorId = SandboxId.NewId();
            var didPlaceDoor = ExecuteProjectMutation(
                "Place Door",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.doors.Add(new DoorData
                    {
                        doorId = createdDoorId,
                        wallSegmentId = wall.wallSegmentId,
                        offsetAlongWall = offsetAlongWall,
                        width = width,
                        state = state
                    });
                    return true;
                },
                new[] { createdDoorId });

            if (didPlaceDoor)
            {
                doorId = createdDoorId;
            }

            return didPlaceDoor;
        }

        public bool UpdateDoor(
            string doorId,
            float width,
            float offsetAlongWall,
            DoorState state,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(doorId) || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Door, doorId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Door",
                project =>
                {
                    if (!TryFindDoor(project, doorId, out var floor, out var door) ||
                        !TryFindWall(floor, door.wallSegmentId, out var wall))
                    {
                        return false;
                    }

                    var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
                    var clampedOffset = Mathf.Clamp(offsetAlongWall, 0f, wallLength);
                    var halfWidth = width * 0.5f;
                    if (clampedOffset - halfWidth < -0.01f || clampedOffset + halfWidth > wallLength + 0.01f)
                    {
                        return false;
                    }

                    door.width = width;
                    door.offsetAlongWall = clampedOffset;
                    door.state = state;
                    door.tags = NormalizeTags(tags);
                    door.metadataFields = CloneMetadataFields(metadataFields);
                    return true;
                },
                new[] { doorId });
        }

        public bool PlaceWindow(
            Vector2 worldPoint,
            out string windowId,
            float width = 1f,
            bool canBeUsedForEscape = false,
            float escapeCost = 1f,
            float escapeRiskMultiplier = 1f)
        {
            windowId = string.Empty;
            if (workspaceService?.ActiveFloor == null || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Window))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            if (!TryFindNearestWall(workspaceService.ActiveFloor, worldPoint, out var wall, out _, out var offsetAlongWall))
            {
                return false;
            }

            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var halfWidth = width * 0.5f;
            if (wallLength <= Mathf.Epsilon || offsetAlongWall - halfWidth < -0.01f || offsetAlongWall + halfWidth > wallLength + 0.01f)
            {
                return false;
            }

            var createdWindowId = SandboxId.NewId();
            var didPlaceWindow = ExecuteProjectMutation(
                "Place Window",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.windows.Add(new WindowData
                    {
                        windowId = createdWindowId,
                        wallSegmentId = wall.wallSegmentId,
                        offsetAlongWall = offsetAlongWall,
                        width = width,
                        canBeUsedForEscape = canBeUsedForEscape,
                        escapeCost = escapeCost,
                        escapeRiskMultiplier = escapeRiskMultiplier
                    });
                    return true;
                },
                new[] { createdWindowId });

            if (didPlaceWindow)
            {
                windowId = createdWindowId;
            }

            return didPlaceWindow;
        }

        public bool UpdateWindow(
            string windowId,
            float width,
            float offsetAlongWall,
            bool canBeUsedForEscape,
            float escapeCost,
            float escapeRiskMultiplier,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(windowId) || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Window, windowId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Window",
                project =>
                {
                    if (!TryFindWindow(project, windowId, out var floor, out var window) ||
                        !TryFindWall(floor, window.wallSegmentId, out var wall))
                    {
                        return false;
                    }

                    var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
                    var clampedOffset = Mathf.Clamp(offsetAlongWall, 0f, wallLength);
                    var halfWidth = width * 0.5f;
                    if (clampedOffset - halfWidth < -0.01f || clampedOffset + halfWidth > wallLength + 0.01f)
                    {
                        return false;
                    }

                    window.width = width;
                    window.offsetAlongWall = clampedOffset;
                    window.canBeUsedForEscape = canBeUsedForEscape;
                    window.escapeCost = Mathf.Max(0f, escapeCost);
                    window.escapeRiskMultiplier = Mathf.Max(0f, escapeRiskMultiplier);
                    window.tags = NormalizeTags(tags);
                    window.metadataFields = CloneMetadataFields(metadataFields);
                    return true;
                },
                new[] { windowId });
        }

        public bool PlaceExit(
            Vector2 center,
            out string exitZoneId,
            Vector2? size = null,
            float rotationDegrees = 0f,
            float width = -1f,
            float capacity = 0f,
            float priority = 1f,
            string name = "")
        {
            exitZoneId = string.Empty;
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Exit))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var resolvedSize = size ?? defaultExitZoneSize;
            var createdExitZoneId = SandboxId.NewId();
            var didPlaceExit = ExecuteProjectMutation(
                "Place Exit",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.exits.Add(new ExitZoneData
                    {
                        exitZoneId = createdExitZoneId,
                        name = name ?? string.Empty,
                        center = center,
                        size = resolvedSize,
                        rotationDegrees = rotationDegrees,
                        width = width > 0f ? width : defaultExitWidth,
                        capacity = capacity,
                        priority = priority
                    });
                    return true;
                },
                new[] { createdExitZoneId });

            if (didPlaceExit)
            {
                exitZoneId = createdExitZoneId;
            }

            return didPlaceExit;
        }

        public bool UpdateExit(
            string exitZoneId,
            Vector2 center,
            Vector2 size,
            float rotationDegrees,
            float width,
            float capacity,
            float priority,
            string name,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(exitZoneId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Exit, exitZoneId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Exit",
                project =>
                {
                    if (!TryFindExit(project, exitZoneId, out _, out var exitZone))
                    {
                        return false;
                    }

                    exitZone.center = center;
                    exitZone.size = size;
                    exitZone.rotationDegrees = rotationDegrees;
                    exitZone.width = width;
                    exitZone.capacity = capacity;
                    exitZone.priority = priority;
                    exitZone.name = name ?? string.Empty;
                    exitZone.tags = NormalizeTags(tags);
                    exitZone.metadataFields = CloneMetadataFields(metadataFields);
                    return true;
                },
                new[] { exitZoneId });
        }

        public bool PlaceObstacle(
            Vector2 center,
            out string obstacleId,
            Vector2? size = null,
            float rotationDegrees = 0f,
            ObstacleSemanticType semanticType = ObstacleSemanticType.HardBlocking,
            float traversalCostMultiplier = 1f,
            string name = "")
        {
            obstacleId = string.Empty;
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Obstacle))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdObstacleId = SandboxId.NewId();
            var didPlaceObstacle = ExecuteProjectMutation(
                "Place Obstacle",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.obstacles.Add(new ObstacleData
                    {
                        obstacleId = createdObstacleId,
                        name = name ?? string.Empty,
                        center = center,
                        size = size ?? defaultObstacleSize,
                        rotationDegrees = NormalizeObstacleRotation(rotationDegrees),
                        semanticType = semanticType,
                        traversalCostMultiplier = traversalCostMultiplier
                    });
                    return true;
                },
                new[] { createdObstacleId });

            if (didPlaceObstacle)
            {
                obstacleId = createdObstacleId;
            }

            return didPlaceObstacle;
        }

        public bool UpdateObstacle(
            string obstacleId,
            Vector2 center,
            Vector2 size,
            float rotationDegrees,
            ObstacleSemanticType semanticType,
            float traversalCostMultiplier,
            string name,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(obstacleId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Obstacle, obstacleId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Obstacle",
                project =>
                {
                    if (!TryFindObstacle(project, obstacleId, out _, out var obstacle))
                    {
                        return false;
                    }

                    obstacle.center = center;
                    obstacle.size = size;
                    obstacle.rotationDegrees = NormalizeObstacleRotation(rotationDegrees);
                    obstacle.semanticType = semanticType;
                    obstacle.traversalCostMultiplier = Mathf.Max(0f, traversalCostMultiplier);
                    obstacle.name = name ?? string.Empty;
                    obstacle.tags = NormalizeTags(tags);
                    obstacle.metadataFields = CloneMetadataFields(metadataFields);
                    return true;
                },
                new[] { obstacleId });
        }

        public bool PlaceStairPortal(
            Vector2 localPosition,
            out string stairPortalId,
            float rotationDegrees = 0f,
            string name = "",
            StairTraversalDirection direction = StairTraversalDirection.Bidirectional,
            float travelCost = 1f)
        {
            stairPortalId = string.Empty;
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Stair))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdStairPortalId = SandboxId.NewId();
            var didPlaceStairPortal = ExecuteProjectMutation(
                "Place Stair Portal",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.stairPortals.Add(new StairPortalData
                    {
                        stairPortalId = createdStairPortalId,
                        sourceFloorId = floor.floorId,
                        name = name ?? string.Empty,
                        localPosition = localPosition,
                        rotationDegrees = rotationDegrees,
                        direction = direction,
                        travelCost = travelCost
                    });
                    return true;
                },
                new[] { createdStairPortalId });

            if (didPlaceStairPortal)
            {
                stairPortalId = createdStairPortalId;
            }

            return didPlaceStairPortal;
        }

        public bool UpdateStairPortal(
            string stairPortalId,
            Vector2 localPosition,
            float rotationDegrees,
            string name,
            StairTraversalDirection direction,
            float travelCost,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(stairPortalId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Stair, stairPortalId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Stair Portal",
                project =>
                {
                    if (!TryFindStairPortal(project, stairPortalId, out var floor, out var stairPortal))
                    {
                        return false;
                    }

                    stairPortal.sourceFloorId = floor.floorId;
                    stairPortal.localPosition = localPosition;
                    stairPortal.rotationDegrees = rotationDegrees;
                    stairPortal.name = name ?? string.Empty;
                    stairPortal.direction = direction;
                    stairPortal.travelCost = Mathf.Max(0f, travelCost);
                    stairPortal.tags = NormalizeTags(tags);
                    stairPortal.metadataFields = CloneMetadataFields(metadataFields);
                    return true;
                },
                new[] { stairPortalId });
        }

        public bool LinkStairPortals(
            string sourceFloorId,
            string sourcePortalId,
            string targetFloorId,
            string targetPortalId,
            StairTraversalDirection direction,
            float travelCost)
        {
            if (string.IsNullOrWhiteSpace(sourceFloorId) ||
                string.IsNullOrWhiteSpace(sourcePortalId) ||
                string.IsNullOrWhiteSpace(targetFloorId) ||
                string.IsNullOrWhiteSpace(targetPortalId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Stair, sourcePortalId) ||
                IsLocked(SandboxVisualObjectType.Stair, targetPortalId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Link Stair Portals",
                project =>
                {
                    var sourceFloor = FindFloor(project, sourceFloorId);
                    var targetFloor = FindFloor(project, targetFloorId);
                    if (sourceFloor == null || targetFloor == null)
                    {
                        return false;
                    }

                    var sourcePortal = sourceFloor.stairPortals.FirstOrDefault(candidate =>
                        string.Equals(candidate.stairPortalId, sourcePortalId, StringComparison.Ordinal));
                    var targetPortal = targetFloor.stairPortals.FirstOrDefault(candidate =>
                        string.Equals(candidate.stairPortalId, targetPortalId, StringComparison.Ordinal));
                    if (sourcePortal == null || targetPortal == null)
                    {
                        return false;
                    }

                    sourcePortal.sourceFloorId = sourceFloor.floorId;
                    sourcePortal.targetFloorId = targetFloor.floorId;
                    sourcePortal.targetStairPortalId = targetPortal.stairPortalId;
                    sourcePortal.direction = direction;
                    sourcePortal.travelCost = Mathf.Max(0f, travelCost);

                    targetPortal.sourceFloorId = targetFloor.floorId;
                    targetPortal.targetFloorId = sourceFloor.floorId;
                    targetPortal.targetStairPortalId = sourcePortal.stairPortalId;
                    targetPortal.direction = GetReciprocalDirection(direction);
                    targetPortal.travelCost = Mathf.Max(0f, travelCost);
                    return true;
                },
                new[] { sourcePortalId, targetPortalId });
        }

        private bool ExecuteProjectMutation(
            string description,
            Func<BuildingProjectData, bool> mutation,
            IReadOnlyList<string> nextSelection)
        {
            if (workspaceService?.ActiveProject == null ||
                mutation == null ||
                (previewService != null && previewService.IsPreviewModeActive))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloorId;
            var beforeProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var beforeSelection = selectionService != null
                ? new List<string>(selectionService.SelectedObjectIds)
                : new List<string>();

            if (!mutation(afterProject))
            {
                return false;
            }

            SandboxProjectDataUtility.EnsureIds(afterProject);

            void ApplyAfter()
            {
                ApplyProjectState(SandboxProjectSerializer.Clone(afterProject), activeFloorId, nextSelection);
            }

            void ApplyBefore()
            {
                ApplyProjectState(SandboxProjectSerializer.Clone(beforeProject), activeFloorId, beforeSelection);
            }

            if (commandHistory == null)
            {
                ApplyAfter();
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(description, ApplyAfter, ApplyBefore));
            return true;
        }

        private void ApplyProjectState(BuildingProjectData project, string activeFloorId, IReadOnlyList<string> selection)
        {
            workspaceService.SetActiveProject(project);
            if (!string.IsNullOrWhiteSpace(activeFloorId))
            {
                workspaceService.SetActiveFloor(activeFloorId);
            }

            if (selectionService != null && selection != null)
            {
                selectionService.ReplaceSelection(selection);
            }

            validationService?.ValidateActiveProject();
            SemanticObjectsChanged?.Invoke();
        }

        private bool TryFindNearestWall(
            FloorData floor,
            Vector2 worldPoint,
            out WallSegmentData nearestWall,
            out Vector2 projectedPoint,
            out float offsetAlongWall)
        {
            nearestWall = null;
            projectedPoint = Vector2.zero;
            offsetAlongWall = 0f;

            if (floor == null)
            {
                return false;
            }

            var bestDistance = float.MaxValue;
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                var projection = ProjectPointOntoSegment(worldPoint, wall.startPoint, wall.endPoint);
                var distance = Vector2.Distance(worldPoint, projection);
                if (distance > wallAttachDistance || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                nearestWall = wall;
                projectedPoint = projection;
                var wallDirection = (wall.endPoint - wall.startPoint).normalized;
                offsetAlongWall = Vector2.Dot(projection - wall.startPoint, wallDirection);
            }

            return nearestWall != null;
        }

        private static FloorData FindFloor(BuildingProjectData project, string floorId)
        {
            return project?.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
        }

        private static bool TryFindDoor(BuildingProjectData project, string doorId, out FloorData floor, out DoorData door)
        {
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                door = project.floors[i].doors.FirstOrDefault(candidate => string.Equals(candidate.doorId, doorId, StringComparison.Ordinal));
                if (door != null)
                {
                    floor = project.floors[i];
                    return true;
                }
            }

            floor = null;
            door = null;
            return false;
        }

        private static bool TryFindWindow(BuildingProjectData project, string windowId, out FloorData floor, out WindowData window)
        {
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                window = project.floors[i].windows.FirstOrDefault(candidate => string.Equals(candidate.windowId, windowId, StringComparison.Ordinal));
                if (window != null)
                {
                    floor = project.floors[i];
                    return true;
                }
            }

            floor = null;
            window = null;
            return false;
        }

        private static bool TryFindWall(FloorData floor, string wallSegmentId, out WallSegmentData wall)
        {
            wall = floor?.wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            return wall != null;
        }

        private static bool TryFindExit(BuildingProjectData project, string exitZoneId, out FloorData floor, out ExitZoneData exitZone)
        {
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                exitZone = project.floors[i].exits.FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, exitZoneId, StringComparison.Ordinal));
                if (exitZone != null)
                {
                    floor = project.floors[i];
                    return true;
                }
            }

            floor = null;
            exitZone = null;
            return false;
        }

        private static bool TryFindObstacle(BuildingProjectData project, string obstacleId, out FloorData floor, out ObstacleData obstacle)
        {
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                obstacle = project.floors[i].obstacles.FirstOrDefault(candidate => string.Equals(candidate.obstacleId, obstacleId, StringComparison.Ordinal));
                if (obstacle != null)
                {
                    floor = project.floors[i];
                    return true;
                }
            }

            floor = null;
            obstacle = null;
            return false;
        }

        private static bool TryFindStairPortal(BuildingProjectData project, string stairPortalId, out FloorData floor, out StairPortalData stairPortal)
        {
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                stairPortal = project.floors[i].stairPortals.FirstOrDefault(candidate => string.Equals(candidate.stairPortalId, stairPortalId, StringComparison.Ordinal));
                if (stairPortal != null)
                {
                    floor = project.floors[i];
                    return true;
                }
            }

            floor = null;
            stairPortal = null;
            return false;
        }

        private static List<string> NormalizeTags(IEnumerable<string> tags)
        {
            return tags == null
                ? new List<string>()
                : tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.Ordinal).ToList();
        }

        private static List<MetadataFieldData> CloneMetadataFields(IEnumerable<MetadataFieldData> metadataFields)
        {
            if (metadataFields == null)
            {
                return new List<MetadataFieldData>();
            }

            var cloned = new List<MetadataFieldData>();
            foreach (var field in metadataFields)
            {
                if (field == null)
                {
                    continue;
                }

                cloned.Add(new MetadataFieldData
                {
                    key = field.key ?? string.Empty,
                    value = field.value ?? string.Empty
                });
            }

            return cloned;
        }

        private float NormalizeObstacleRotation(float rotationDegrees)
        {
            if (Mathf.Abs(obstacleRotationStepDegrees) <= Mathf.Epsilon)
            {
                return rotationDegrees;
            }

            return Mathf.Round(rotationDegrees / obstacleRotationStepDegrees) * obstacleRotationStepDegrees;
        }

        private static StairTraversalDirection GetReciprocalDirection(StairTraversalDirection direction)
        {
            return direction switch
            {
                StairTraversalDirection.AscendOnly => StairTraversalDirection.DescendOnly,
                StairTraversalDirection.DescendOnly => StairTraversalDirection.AscendOnly,
                _ => StairTraversalDirection.Bidirectional,
            };
        }

        private static Vector2 ProjectPointOntoSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return segmentStart;
            }

            var t = Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Mathf.Clamp01(t);
            return segmentStart + segment * t;
        }

        private bool IsLocked(SandboxVisualObjectType objectType, string objectId = null)
        {
            return visualOrganizationService != null &&
                   (visualOrganizationService.IsTypeLocked(objectType) ||
                    (!string.IsNullOrWhiteSpace(objectId) && visualOrganizationService.IsObjectLocked(objectId)));
        }
    }
}
