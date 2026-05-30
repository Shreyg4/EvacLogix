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
    public struct SandboxOpeningPlacementPreview
    {
        public bool isValid;
        public string wallSegmentId;
        public Vector2 center;
        public Vector2 start;
        public Vector2 end;
        public float offsetAlongWall;
        public float width;
        public string message;

        public static SandboxOpeningPlacementPreview Invalid(Vector2 center, string message)
        {
            return new SandboxOpeningPlacementPreview
            {
                isValid = false,
                wallSegmentId = string.Empty,
                center = center,
                start = center + new Vector2(-0.35f, 0f),
                end = center + new Vector2(0.35f, 0f),
                offsetAlongWall = 0f,
                width = 0f,
                message = message
            };
        }
    }

    public sealed class SandboxSemanticObjectAuthoringService : MonoBehaviour
    {
        [SerializeField] private float wallAttachDistance = 0.75f;
        [SerializeField] private float defaultOpeningWidth = 1f;
        [SerializeField] private float openingEndMargin = 0.05f;
        [SerializeField] private Vector2 defaultExitZoneSize = new(1.5f, 1.5f);
        [SerializeField] private Vector2 defaultObstacleSize = Vector2.one;
        [SerializeField] private Vector2 defaultStairPortalSize = Vector2.one;
        [SerializeField] private Vector2 defaultTeleportPortalSize = Vector2.one;
        [SerializeField] private float defaultExitWidth = 1.5f;
        [SerializeField] private float obstacleRotationStepDegrees = 15f;
        [SerializeField] private Color[] teleportPairPalette =
        {
            new(0.18f, 0.85f, 0.92f, 1f),
            new(0.96f, 0.58f, 0.22f, 1f),
            new(0.84f, 0.36f, 0.95f, 1f),
            new(0.28f, 0.88f, 0.5f, 1f),
            new(0.96f, 0.85f, 0.24f, 1f),
            new(0.95f, 0.42f, 0.56f, 1f),
        };

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxSelectionService selectionService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxValidationService validationService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;
        private SandboxWorkspaceStateService workspaceStateService;
        private float lastDoorPlacementWidth = -1f;
        private float lastWindowPlacementWidth = -1f;

        public event Action SemanticObjectsChanged;

        public float WallAttachDistance => wallAttachDistance;
        public float DefaultOpeningWidth => defaultOpeningWidth;
        public float ObstacleRotationStepDegrees => obstacleRotationStepDegrees;
        public Vector2 DefaultTeleportPortalSize => defaultTeleportPortalSize;
        public IReadOnlyList<Color> TeleportPairPalette => teleportPairPalette;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            selectionService = GetComponent<SandboxSelectionService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            validationService = GetComponent<SandboxValidationService>();
            visualOrganizationService = GetComponent<SandboxVisualOrganizationService>();
            previewService = GetComponent<SandboxPreviewService>();
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
        }

        public bool TryGetOpeningPlacementPreview(
            Vector2 worldPoint,
            float width,
            SandboxVisualObjectType openingType,
            string ignoredOpeningId,
            out SandboxOpeningPlacementPreview preview)
        {
            preview = SandboxOpeningPlacementPreview.Invalid(worldPoint, "Move closer to a wall.");
            width = ResolveOpeningWidth(width, openingType);
            var floor = workspaceService?.ActiveFloor;
            if (floor == null || width <= 0f)
            {
                preview = SandboxOpeningPlacementPreview.Invalid(worldPoint, "Create or select a floor first.");
                return false;
            }

            if (openingType != SandboxVisualObjectType.Door && openingType != SandboxVisualObjectType.Window)
            {
                preview = SandboxOpeningPlacementPreview.Invalid(worldPoint, "Unsupported opening type.");
                return false;
            }

            if (!TryFindNearestWall(floor, worldPoint, out var wall, out var projectedPoint, out var offsetAlongWall))
            {
                preview = SandboxOpeningPlacementPreview.Invalid(worldPoint, "Move closer to a wall.");
                return false;
            }

            var worldWidth = ResolveOpeningWorldWidth(floor, width);
            var wallDirection = (wall.endPoint - wall.startPoint).normalized;
            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var halfWorldWidth = worldWidth * 0.5f;
            var start = projectedPoint - wallDirection * halfWorldWidth;
            var end = projectedPoint + wallDirection * halfWorldWidth;
            var message = "Click to place opening.";
            var isValid = true;

            if (wallLength <= Mathf.Epsilon ||
                offsetAlongWall - halfWorldWidth < openingEndMargin ||
                offsetAlongWall + halfWorldWidth > wallLength - openingEndMargin)
            {
                isValid = false;
                message = "Opening too close to wall end.";
            }
            else if (DoesOpeningOverlap(floor, wall.wallSegmentId, offsetAlongWall, width, ignoredOpeningId))
            {
                isValid = false;
                message = "Opening overlaps an existing door/window.";
            }

            preview = new SandboxOpeningPlacementPreview
            {
                isValid = isValid,
                wallSegmentId = wall.wallSegmentId,
                center = projectedPoint,
                start = start,
                end = end,
                offsetAlongWall = offsetAlongWall,
                width = width,
                message = message
            };
            return true;
        }

        public bool PlaceDoor(Vector2 worldPoint, out string doorId, float width = -1f, DoorState state = DoorState.Normal)
        {
            doorId = string.Empty;
            width = ResolveOpeningWidth(width, SandboxVisualObjectType.Door);
            if (workspaceService?.ActiveFloor == null || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Door))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            if (!TryGetOpeningPlacementPreview(worldPoint, width, SandboxVisualObjectType.Door, null, out var placement) ||
                !placement.isValid)
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
                        wallSegmentId = placement.wallSegmentId,
                        offsetAlongWall = placement.offsetAlongWall,
                        width = width,
                        state = state
                    });
                    return true;
                },
                new[] { createdDoorId });

            if (didPlaceDoor)
            {
                lastDoorPlacementWidth = width;
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
            width = ResolveOpeningWidth(width, SandboxVisualObjectType.Door);
            if (string.IsNullOrWhiteSpace(doorId) || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Door, doorId))
            {
                return false;
            }

            var didUpdate = ExecuteProjectMutation(
                "Update Door",
                project =>
                {
                    if (!TryFindDoor(project, doorId, out var floor, out var door) ||
                        !TryFindWall(floor, door.wallSegmentId, out var wall))
                    {
                        return false;
                    }

                    var clampedOffset = Mathf.Clamp(offsetAlongWall, 0f, Vector2.Distance(wall.startPoint, wall.endPoint));
                    if (!IsOpeningPlacementValid(floor, wall, clampedOffset, width, doorId))
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
            if (didUpdate)
            {
                lastDoorPlacementWidth = width;
            }

            return didUpdate;
        }

        public bool PlaceWindow(
            Vector2 worldPoint,
            out string windowId,
            float width = -1f,
            bool canBeUsedForEscape = false,
            float escapeCost = 1f,
            float escapeRiskMultiplier = 1f)
        {
            windowId = string.Empty;
            width = ResolveOpeningWidth(width, SandboxVisualObjectType.Window);
            if (workspaceService?.ActiveFloor == null || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Window))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            if (!TryGetOpeningPlacementPreview(worldPoint, width, SandboxVisualObjectType.Window, null, out var placement) ||
                !placement.isValid)
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
                        wallSegmentId = placement.wallSegmentId,
                        offsetAlongWall = placement.offsetAlongWall,
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
                lastWindowPlacementWidth = width;
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
            width = ResolveOpeningWidth(width, SandboxVisualObjectType.Window);
            if (string.IsNullOrWhiteSpace(windowId) || width <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Window, windowId))
            {
                return false;
            }

            var didUpdate = ExecuteProjectMutation(
                "Update Window",
                project =>
                {
                    if (!TryFindWindow(project, windowId, out var floor, out var window) ||
                        !TryFindWall(floor, window.wallSegmentId, out var wall))
                    {
                        return false;
                    }

                    var clampedOffset = Mathf.Clamp(offsetAlongWall, 0f, Vector2.Distance(wall.startPoint, wall.endPoint));
                    if (!IsOpeningPlacementValid(floor, wall, clampedOffset, width, windowId))
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
            if (didUpdate)
            {
                lastWindowPlacementWidth = width;
            }

            return didUpdate;
        }

        public float GetPlacementOpeningWidth(SandboxVisualObjectType openingType)
        {
            return ResolveOpeningWidth(-1f, openingType);
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
            var resolvedSize = size ?? defaultExitZoneSize;
            if (workspaceService?.ActiveFloor == null || resolvedSize.x <= 0f || resolvedSize.y <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Exit))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
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
            if (string.IsNullOrWhiteSpace(exitZoneId) || size.x <= 0f || size.y <= 0f || width <= 0f)
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
            float discourageWeight = 1f,
            float movementSpeedPenalty = 0f,
            string name = "")
        {
            obstacleId = string.Empty;
            var resolvedSize = size ?? defaultObstacleSize;
            if (workspaceService?.ActiveFloor == null || resolvedSize.x <= 0f || resolvedSize.y <= 0f)
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
                        size = resolvedSize,
                        rotationDegrees = NormalizeObstacleRotation(rotationDegrees),
                        discourageWeight = Mathf.Clamp01(discourageWeight),
                        movementSpeedPenalty = Mathf.Clamp01(movementSpeedPenalty)
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
            float discourageWeight,
            float movementSpeedPenalty,
            string name,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(obstacleId) || size.x <= 0f || size.y <= 0f)
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
                    obstacle.discourageWeight = Mathf.Clamp01(discourageWeight);
                    obstacle.movementSpeedPenalty = Mathf.Clamp01(movementSpeedPenalty);
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
            Vector2? size = null,
            float rotationDegrees = 0f,
            string name = "",
            StairTraversalDirection direction = StairTraversalDirection.Bidirectional,
            float travelCost = 1f)
        {
            stairPortalId = string.Empty;
            var resolvedSize = size ?? defaultStairPortalSize;
            if (workspaceService?.ActiveFloor == null || resolvedSize.x <= 0f || resolvedSize.y <= 0f)
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
                        size = resolvedSize,
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

        public bool PlaceTeleportPortal(
            Vector2 localPosition,
            out string teleportPortalId,
            string pairId,
            int pairColorIndex,
            Vector2? size = null,
            float rotationDegrees = 0f,
            string name = "",
            TeleportPortalKind kind = TeleportPortalKind.Stair,
            float travelCost = 1f,
            bool isPairEnabled = true,
            string targetFloorId = "",
            string targetTeleportPortalId = "")
        {
            teleportPortalId = string.Empty;
            var resolvedSize = size ?? defaultTeleportPortalSize;
            if (workspaceService?.ActiveFloor == null ||
                resolvedSize.x <= 0f ||
                resolvedSize.y <= 0f ||
                string.IsNullOrWhiteSpace(pairId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Teleport))
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdTeleportPortalId = SandboxId.NewId();
            var didPlaceTeleportPortal = ExecuteProjectMutation(
                "Place Teleport Portal",
                project =>
                {
                    var floor = FindFloor(project, activeFloorId);
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.teleportPortals.Add(new TeleportPortalData
                    {
                        teleportPortalId = createdTeleportPortalId,
                        pairId = pairId,
                        pairColorIndex = Mathf.Max(0, pairColorIndex),
                        sourceFloorId = floor.floorId,
                        name = name ?? string.Empty,
                        localPosition = localPosition,
                        size = resolvedSize,
                        rotationDegrees = rotationDegrees,
                        targetFloorId = targetFloorId ?? string.Empty,
                        targetTeleportPortalId = targetTeleportPortalId ?? string.Empty,
                        kind = kind,
                        travelCost = Mathf.Max(0.1f, travelCost),
                        isPairEnabled = isPairEnabled
                    });
                    return true;
                },
                new[] { createdTeleportPortalId });

            if (didPlaceTeleportPortal)
            {
                teleportPortalId = createdTeleportPortalId;
            }

            return didPlaceTeleportPortal;
        }

        public bool UpdateTeleportPortal(
            string teleportPortalId,
            Vector2 localPosition,
            Vector2 size,
            float rotationDegrees,
            string name,
            TeleportPortalKind kind,
            float travelCost,
            bool isPairEnabled,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(teleportPortalId) || size.x <= 0f || size.y <= 0f)
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Teleport, teleportPortalId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Teleport Portal",
                project =>
                {
                    if (!TryFindTeleportPortal(project, teleportPortalId, out var floor, out var teleportPortal))
                    {
                        return false;
                    }

                    teleportPortal.sourceFloorId = floor.floorId;
                    teleportPortal.localPosition = localPosition;
                    teleportPortal.size = size;
                    teleportPortal.rotationDegrees = rotationDegrees;
                    teleportPortal.name = name ?? string.Empty;
                    teleportPortal.kind = kind;
                    teleportPortal.travelCost = Mathf.Max(0.1f, travelCost);
                    teleportPortal.tags = NormalizeTags(tags);
                    teleportPortal.metadataFields = CloneMetadataFields(metadataFields);

                    foreach (var linkedPortal in project.floors
                                 .SelectMany(candidate => candidate.teleportPortals)
                                 .Where(candidate => string.Equals(candidate.pairId, teleportPortal.pairId, StringComparison.Ordinal)))
                    {
                        linkedPortal.isPairEnabled = isPairEnabled;
                        linkedPortal.travelCost = Mathf.Max(0.1f, travelCost);
                        linkedPortal.kind = kind;
                    }

                    return true;
                },
                new[] { teleportPortalId });
        }

        public bool LinkTeleportPortals(
            string sourceFloorId,
            string sourcePortalId,
            string targetFloorId,
            string targetPortalId,
            TeleportPortalKind kind,
            float travelCost,
            bool isPairEnabled)
        {
            if (string.IsNullOrWhiteSpace(sourceFloorId) ||
                string.IsNullOrWhiteSpace(sourcePortalId) ||
                string.IsNullOrWhiteSpace(targetFloorId) ||
                string.IsNullOrWhiteSpace(targetPortalId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Teleport, sourcePortalId) ||
                IsLocked(SandboxVisualObjectType.Teleport, targetPortalId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Link Teleport Portals",
                project =>
                {
                    var sourceFloor = FindFloor(project, sourceFloorId);
                    var targetFloor = FindFloor(project, targetFloorId);
                    if (sourceFloor == null || targetFloor == null)
                    {
                        return false;
                    }

                    var sourcePortal = sourceFloor.teleportPortals.FirstOrDefault(candidate =>
                        string.Equals(candidate.teleportPortalId, sourcePortalId, StringComparison.Ordinal));
                    var targetPortal = targetFloor.teleportPortals.FirstOrDefault(candidate =>
                        string.Equals(candidate.teleportPortalId, targetPortalId, StringComparison.Ordinal));
                    if (sourcePortal == null || targetPortal == null)
                    {
                        return false;
                    }

                    var sharedPairId = string.IsNullOrWhiteSpace(sourcePortal.pairId)
                        ? string.IsNullOrWhiteSpace(targetPortal.pairId) ? SandboxId.NewId() : targetPortal.pairId
                        : sourcePortal.pairId;
                    var colorIndex = sourcePortal.pairColorIndex != 0 || targetPortal.pairColorIndex == 0
                        ? sourcePortal.pairColorIndex
                        : targetPortal.pairColorIndex;

                    sourcePortal.pairId = sharedPairId;
                    sourcePortal.sourceFloorId = sourceFloor.floorId;
                    sourcePortal.targetFloorId = targetFloor.floorId;
                    sourcePortal.targetTeleportPortalId = targetPortal.teleportPortalId;
                    sourcePortal.pairColorIndex = colorIndex;
                    sourcePortal.kind = kind;
                    sourcePortal.travelCost = Mathf.Max(0.1f, travelCost);
                    sourcePortal.isPairEnabled = isPairEnabled;

                    targetPortal.pairId = sharedPairId;
                    targetPortal.sourceFloorId = targetFloor.floorId;
                    targetPortal.targetFloorId = sourceFloor.floorId;
                    targetPortal.targetTeleportPortalId = sourcePortal.teleportPortalId;
                    targetPortal.pairColorIndex = colorIndex;
                    targetPortal.kind = kind;
                    targetPortal.travelCost = Mathf.Max(0.1f, travelCost);
                    targetPortal.isPairEnabled = isPairEnabled;
                    return true;
                },
                new[] { sourcePortalId, targetPortalId });
        }

        public bool SetTeleportTargetFloor(string sourcePortalId, string targetFloorId)
        {
            if (string.IsNullOrWhiteSpace(sourcePortalId) || string.IsNullOrWhiteSpace(targetFloorId))
            {
                return false;
            }

            if (IsLocked(SandboxVisualObjectType.Teleport, sourcePortalId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Set Teleport Target Floor",
                project =>
                {
                    if (!TryFindTeleportPortal(project, sourcePortalId, out var sourceFloor, out var sourcePortal))
                    {
                        return false;
                    }

                    var targetFloor = FindFloor(project, targetFloorId);
                    if (targetFloor == null || string.Equals(sourceFloor.floorId, targetFloor.floorId, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(sourcePortal.targetTeleportPortalId) &&
                        TryFindTeleportPortal(project, sourcePortal.targetTeleportPortalId, out _, out var previousTargetPortal) &&
                        string.Equals(previousTargetPortal.targetTeleportPortalId, sourcePortal.teleportPortalId, StringComparison.Ordinal))
                    {
                        previousTargetPortal.targetFloorId = string.Empty;
                        previousTargetPortal.targetTeleportPortalId = string.Empty;
                    }

                    var targetPortal = ResolveTeleportTargetPortal(project, sourcePortal, targetFloor);
                    if (IsLocked(SandboxVisualObjectType.Teleport, targetPortal.teleportPortalId))
                    {
                        return false;
                    }

                    var sharedPairId = string.IsNullOrWhiteSpace(sourcePortal.pairId) ? SandboxId.NewId() : sourcePortal.pairId;
                    var colorIndex = Mathf.Max(0, sourcePortal.pairColorIndex);

                    sourcePortal.pairId = sharedPairId;
                    sourcePortal.sourceFloorId = sourceFloor.floorId;
                    sourcePortal.targetFloorId = targetFloor.floorId;
                    sourcePortal.targetTeleportPortalId = targetPortal.teleportPortalId;
                    sourcePortal.pairColorIndex = colorIndex;

                    targetPortal.pairId = sharedPairId;
                    targetPortal.sourceFloorId = targetFloor.floorId;
                    targetPortal.targetFloorId = sourceFloor.floorId;
                    targetPortal.targetTeleportPortalId = sourcePortal.teleportPortalId;
                    targetPortal.pairColorIndex = colorIndex;
                    targetPortal.localPosition = sourcePortal.localPosition;
                    targetPortal.size = sourcePortal.size;
                    targetPortal.rotationDegrees = sourcePortal.rotationDegrees;
                    targetPortal.kind = sourcePortal.kind;
                    targetPortal.travelCost = Mathf.Max(0.1f, sourcePortal.travelCost);
                    targetPortal.isPairEnabled = sourcePortal.isPairEnabled;
                    return true;
                },
                new[] { sourcePortalId });
        }

        public int GetNextTeleportPairColorIndex()
        {
            var project = workspaceService?.ActiveProject;
            if (project == null || teleportPairPalette == null || teleportPairPalette.Length == 0)
            {
                return 0;
            }

            var usedIndexes = project.floors
                .SelectMany(floor => floor.teleportPortals)
                .Select(portal => Mathf.Max(0, portal.pairColorIndex))
                .Distinct()
                .ToHashSet();
            for (var index = 0; index < teleportPairPalette.Length; index += 1)
            {
                if (!usedIndexes.Contains(index))
                {
                    return index;
                }
            }

            return usedIndexes.Count % teleportPairPalette.Length;
        }

        public bool TryGetTeleportPairColor(int pairColorIndex, out Color color)
        {
            color = Color.white;
            if (teleportPairPalette == null || teleportPairPalette.Length == 0)
            {
                return false;
            }

            var index = Mathf.Clamp(pairColorIndex, 0, teleportPairPalette.Length - 1);
            color = teleportPairPalette[index];
            return true;
        }

        public bool UpdateStairPortal(
            string stairPortalId,
            Vector2 localPosition,
            Vector2 size,
            float rotationDegrees,
            string name,
            StairTraversalDirection direction,
            float travelCost,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (string.IsNullOrWhiteSpace(stairPortalId) || size.x <= 0f || size.y <= 0f)
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
                    stairPortal.size = size;
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
            colliderRebuildService?.RequestRebuild(activeFloorId);
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
                if ((wall.endPoint - wall.startPoint).sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

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

        private float ResolveOpeningWidth(float width, SandboxVisualObjectType openingType)
        {
            if (width > 0f)
            {
                return width;
            }

            return openingType switch
            {
                SandboxVisualObjectType.Door when lastDoorPlacementWidth > 0f => lastDoorPlacementWidth,
                SandboxVisualObjectType.Window when lastWindowPlacementWidth > 0f => lastWindowPlacementWidth,
                _ => defaultOpeningWidth,
            };
        }

        private bool IsOpeningPlacementValid(
            FloorData floor,
            WallSegmentData wall,
            float offsetAlongWall,
            float width,
            string ignoredOpeningId)
        {
            if (floor == null || wall == null || width <= 0f)
            {
                return false;
            }

            var worldWidth = ResolveOpeningWorldWidth(floor, width);
            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            if (wallLength <= Mathf.Epsilon ||
                offsetAlongWall - (worldWidth * 0.5f) < openingEndMargin ||
                offsetAlongWall + (worldWidth * 0.5f) > wallLength - openingEndMargin)
            {
                return false;
            }

            return !DoesOpeningOverlap(floor, wall.wallSegmentId, offsetAlongWall, width, ignoredOpeningId);
        }

        private bool DoesOpeningOverlap(
            FloorData floor,
            string wallSegmentId,
            float offsetAlongWall,
            float width,
            string ignoredOpeningId)
        {
            var worldWidth = ResolveOpeningWorldWidth(floor, width);
            var start = offsetAlongWall - (worldWidth * 0.5f);
            var end = offsetAlongWall + (worldWidth * 0.5f);

            foreach (var door in (floor.doors ?? Enumerable.Empty<DoorData>()).Where(door => string.Equals(door.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
            {
                if (string.Equals(door.doorId, ignoredOpeningId, StringComparison.Ordinal))
                {
                    continue;
                }

                var doorWorldWidth = ResolveOpeningWorldWidth(floor, door.width);
                if (IntervalsOverlap(start, end, door.offsetAlongWall - (doorWorldWidth * 0.5f), door.offsetAlongWall + (doorWorldWidth * 0.5f)))
                {
                    return true;
                }
            }

            foreach (var window in (floor.windows ?? Enumerable.Empty<WindowData>()).Where(window => string.Equals(window.wallSegmentId, wallSegmentId, StringComparison.Ordinal)))
            {
                if (string.Equals(window.windowId, ignoredOpeningId, StringComparison.Ordinal))
                {
                    continue;
                }

                var windowWorldWidth = ResolveOpeningWorldWidth(floor, window.width);
                if (IntervalsOverlap(start, end, window.offsetAlongWall - (windowWorldWidth * 0.5f), window.offsetAlongWall + (windowWorldWidth * 0.5f)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntervalsOverlap(float firstStart, float firstEnd, float secondStart, float secondEnd)
        {
            return firstStart < secondEnd && secondStart < firstEnd;
        }

        private static FloorData FindFloor(BuildingProjectData project, string floorId)
        {
            return project?.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
        }

        private static TeleportPortalData ResolveTeleportTargetPortal(
            BuildingProjectData project,
            TeleportPortalData sourcePortal,
            FloorData targetFloor)
        {
            if (!string.IsNullOrWhiteSpace(sourcePortal.targetTeleportPortalId) &&
                TryFindTeleportPortal(project, sourcePortal.targetTeleportPortalId, out var existingTargetFloor, out var existingTargetPortal) &&
                string.Equals(existingTargetFloor.floorId, targetFloor.floorId, StringComparison.Ordinal))
            {
                return existingTargetPortal;
            }

            if (!string.IsNullOrWhiteSpace(sourcePortal.pairId))
            {
                var matchingPairPortal = targetFloor.teleportPortals.FirstOrDefault(candidate =>
                    string.Equals(candidate.pairId, sourcePortal.pairId, StringComparison.Ordinal));
                if (matchingPairPortal != null)
                {
                    return matchingPairPortal;
                }
            }

            var targetPortal = new TeleportPortalData
            {
                teleportPortalId = SandboxId.NewId(),
                name = string.IsNullOrWhiteSpace(sourcePortal.name) ? "Teleport Target" : $"{sourcePortal.name} Target",
                tags = NormalizeTags(sourcePortal.tags),
                metadataFields = CloneMetadataFields(sourcePortal.metadataFields)
            };
            targetFloor.teleportPortals.Add(targetPortal);
            return targetPortal;
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

        private static bool TryFindTeleportPortal(BuildingProjectData project, string teleportPortalId, out FloorData floor, out TeleportPortalData teleportPortal)
        {
            if (project?.floors != null)
            {
                for (var i = 0; i < project.floors.Count; i += 1)
                {
                    teleportPortal = project.floors[i].teleportPortals.FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, teleportPortalId, StringComparison.Ordinal));
                    if (teleportPortal != null)
                    {
                        floor = project.floors[i];
                        return true;
                    }
                }
            }

            floor = null;
            teleportPortal = null;
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

        private float ResolveOpeningWorldWidth(FloorData floor, float authoredWidth)
        {
            return SandboxOpeningWidthUtility.ResolveWorldWidth(
                workspaceService,
                workspaceStateService,
                floor,
                authoredWidth);
        }

        private bool IsLocked(SandboxVisualObjectType objectType, string objectId = null)
        {
            return visualOrganizationService != null &&
                   (visualOrganizationService.IsTypeLocked(objectType) ||
                    (!string.IsNullOrWhiteSpace(objectId) && visualOrganizationService.IsObjectLocked(objectId)));
        }
    }
}
