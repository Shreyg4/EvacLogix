using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxClipboardItemKind
    {
        Door = 0,
        Window = 1,
        Exit = 2,
        Obstacle = 3,
        Stair = 4,
        Region = 5,
        SpawnPoint = 6,
        SpawnBrush = 7,
    }

    [Serializable]
    public sealed class SandboxClipboardItem
    {
        public SandboxClipboardItemKind kind;
        public string sourceFloorId = string.Empty;
        public string serializedPayload = string.Empty;
    }

    public sealed class SandboxClipboardService : MonoBehaviour
    {
        [SerializeField] private Vector2 defaultPasteOffset = new(1f, 1f);
        [SerializeField] private List<SandboxClipboardItem> clipboardItems = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSelectionService selectionService;
        private SandboxCommandHistory commandHistory;
        private SandboxValidationService validationService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxPreviewService previewService;
        private SandboxWorkspaceStateService workspaceStateService;

        public IReadOnlyList<SandboxClipboardItem> ClipboardItems => clipboardItems;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            selectionService = GetComponent<SandboxSelectionService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            validationService = GetComponent<SandboxValidationService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            visualOrganizationService = GetComponent<SandboxVisualOrganizationService>();
            previewService = GetComponent<SandboxPreviewService>();
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
        }

        public bool CopySelection()
        {
            var floor = workspaceService?.ActiveFloor;
            if (floor == null || selectionService == null)
            {
                return false;
            }

            var items = new List<SandboxClipboardItem>();
            foreach (var selectedId in selectionService.SelectedObjectIds)
            {
                var didCopy = TryCopyFloorItem(floor, selectedId, items) || TryCopySpawnItem(workspaceService.ActiveProject, floor.floorId, selectedId, items);
                if (!didCopy)
                {
                    continue;
                }
            }

            if (items.Count == 0)
            {
                return false;
            }

            clipboardItems = items;
            return true;
        }

        public bool PasteSelection(Vector2? offset = null)
        {
            if (workspaceService?.ActiveProject == null || workspaceService.ActiveFloor == null || clipboardItems.Count == 0)
            {
                return false;
            }

            var requestedOffset = offset ?? defaultPasteOffset;
            return ExecuteMutation(
                "Paste Selection",
                project =>
                {
                    var floor = project.floors.FirstOrDefault(candidate => candidate.floorId == workspaceService.ActiveFloor.floorId);
                    if (floor == null)
                    {
                        return new List<string>();
                    }

                    var newSelection = new List<string>();
                    foreach (var item in clipboardItems)
                    {
                        if (TryPasteItem(project, floor, item, requestedOffset, newSelection))
                        {
                            continue;
                        }
                    }

                    return newSelection;
                });
        }

        public bool DuplicateSelection(Vector2? offset = null)
        {
            return CopySelection() && PasteSelection(offset);
        }

        public bool DeleteSelection()
        {
            if (workspaceService?.ActiveProject == null || selectionService == null || selectionService.SelectedObjectIds.Count == 0)
            {
                return false;
            }

            return ExecuteMutation(
                "Delete Selection",
                project =>
                {
                    var floor = project.floors.FirstOrDefault(candidate => candidate.floorId == workspaceService.ActiveFloorId);
                    if (floor == null)
                    {
                        return Array.Empty<string>();
                    }

                    var selectedIds = selectionService.SelectedObjectIds.ToHashSet(StringComparer.Ordinal);
                    var didChange = false;
                    didChange |= RemoveAll(floor.doors, candidate => selectedIds.Contains(candidate.doorId) && !IsLocked(SandboxVisualObjectType.Door, candidate.doorId));
                    didChange |= RemoveAll(floor.windows, candidate => selectedIds.Contains(candidate.windowId) && !IsLocked(SandboxVisualObjectType.Window, candidate.windowId));
                    didChange |= RemoveAll(floor.exits, candidate => selectedIds.Contains(candidate.exitZoneId) && !IsLocked(SandboxVisualObjectType.Exit, candidate.exitZoneId));
                    didChange |= RemoveAll(floor.obstacles, candidate => selectedIds.Contains(candidate.obstacleId) && !IsLocked(SandboxVisualObjectType.Obstacle, candidate.obstacleId));
                    didChange |= RemoveAll(floor.stairPortals, candidate => selectedIds.Contains(candidate.stairPortalId) && !IsLocked(SandboxVisualObjectType.Stair, candidate.stairPortalId));
                    didChange |= RemoveAll(floor.regions, candidate => selectedIds.Contains(candidate.regionId) && !IsLocked(SandboxVisualObjectType.Region, candidate.regionId));

                    foreach (var layout in project.spawnLayouts)
                    {
                        didChange |= RemoveAll(layout.spawnPoints, candidate =>
                            candidate.floorId == floor.floorId &&
                            selectedIds.Contains(candidate.spawnPointId) &&
                            !IsLocked(SandboxVisualObjectType.Spawn, candidate.spawnPointId));
                        didChange |= RemoveAll(layout.spawnBrushStrokes, candidate =>
                            candidate.floorId == floor.floorId &&
                            selectedIds.Contains(candidate.spawnBrushStrokeId) &&
                            !IsLocked(SandboxVisualObjectType.Spawn, candidate.spawnBrushStrokeId));
                    }

                    return didChange ? Array.Empty<string>() : null;
                });
        }

        public bool MoveSelection(Vector2 delta)
        {
            if (workspaceService?.ActiveProject == null || selectionService == null || selectionService.SelectedObjectIds.Count == 0)
            {
                return false;
            }

            return ExecuteMutation(
                "Move Selection",
                project =>
                {
                    var floor = project.floors.FirstOrDefault(candidate => candidate.floorId == workspaceService.ActiveFloorId);
                    if (floor == null)
                    {
                        return null;
                    }

                    var selectedIds = selectionService.SelectedObjectIds.ToHashSet(StringComparer.Ordinal);
                    var movedIds = new List<string>();

                    foreach (var exitZone in floor.exits.Where(candidate => selectedIds.Contains(candidate.exitZoneId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Exit, exitZone.exitZoneId))
                        {
                            continue;
                        }

                        exitZone.center += delta;
                        movedIds.Add(exitZone.exitZoneId);
                    }

                    foreach (var obstacle in floor.obstacles.Where(candidate => selectedIds.Contains(candidate.obstacleId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Obstacle, obstacle.obstacleId))
                        {
                            continue;
                        }

                        obstacle.center += delta;
                        movedIds.Add(obstacle.obstacleId);
                    }

                    foreach (var stairPortal in floor.stairPortals.Where(candidate => selectedIds.Contains(candidate.stairPortalId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Stair, stairPortal.stairPortalId))
                        {
                            continue;
                        }

                        stairPortal.localPosition += delta;
                        movedIds.Add(stairPortal.stairPortalId);
                    }

                    foreach (var region in floor.regions.Where(candidate => selectedIds.Contains(candidate.regionId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Region, region.regionId))
                        {
                            continue;
                        }

                        for (var i = 0; i < region.polygonPoints.Count; i += 1)
                        {
                            region.polygonPoints[i] += delta;
                        }
                        movedIds.Add(region.regionId);
                    }

                    foreach (var door in floor.doors.Where(candidate => selectedIds.Contains(candidate.doorId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Door, door.doorId))
                        {
                            continue;
                        }

                        if (TryMoveOpening(floor, door.wallSegmentId, ref door.offsetAlongWall, door.width, delta))
                        {
                            movedIds.Add(door.doorId);
                        }
                    }

                    foreach (var window in floor.windows.Where(candidate => selectedIds.Contains(candidate.windowId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Window, window.windowId))
                        {
                            continue;
                        }

                        if (TryMoveOpening(floor, window.wallSegmentId, ref window.offsetAlongWall, window.width, delta))
                        {
                            movedIds.Add(window.windowId);
                        }
                    }

                    foreach (var layout in project.spawnLayouts)
                    {
                        foreach (var spawnPoint in layout.spawnPoints.Where(candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.spawnPointId)))
                        {
                            if (IsLocked(SandboxVisualObjectType.Spawn, spawnPoint.spawnPointId))
                            {
                                continue;
                            }

                            spawnPoint.position += delta;
                            movedIds.Add(spawnPoint.spawnPointId);
                        }

                        foreach (var stroke in layout.spawnBrushStrokes.Where(candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.spawnBrushStrokeId)))
                        {
                            if (IsLocked(SandboxVisualObjectType.Spawn, stroke.spawnBrushStrokeId))
                            {
                                continue;
                            }

                            for (var i = 0; i < stroke.polygonPoints.Count; i += 1)
                            {
                                stroke.polygonPoints[i] += delta;
                            }

                            movedIds.Add(stroke.spawnBrushStrokeId);
                        }
                    }

                    return movedIds.Count > 0 ? movedIds : null;
                });
        }

        private bool ExecuteMutation(string description, Func<BuildingProjectData, IReadOnlyList<string>> mutation)
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

            var nextSelection = mutation(afterProject);
            if (nextSelection == null)
            {
                return false;
            }

            SandboxProjectDataUtility.EnsureIds(afterProject);

            void ApplyProject(BuildingProjectData project, IReadOnlyList<string> selection)
            {
                workspaceService.SetActiveProject(project);
                if (!string.IsNullOrWhiteSpace(activeFloorId))
                {
                    workspaceService.SetActiveFloor(activeFloorId);
                }

                if (selectionService != null)
                {
                    selectionService.ReplaceSelection(selection ?? Array.Empty<string>());
                }

                colliderRebuildService?.RebuildAll();
                validationService?.ValidateActiveProject();
            }

            void ApplyAfter() => ApplyProject(SandboxProjectSerializer.Clone(afterProject), nextSelection);
            void ApplyBefore() => ApplyProject(SandboxProjectSerializer.Clone(beforeProject), beforeSelection);

            if (commandHistory == null)
            {
                ApplyAfter();
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(description, ApplyAfter, ApplyBefore));
            return true;
        }

        private static bool TryCopyFloorItem(FloorData floor, string selectedId, ICollection<SandboxClipboardItem> items)
        {
            var door = floor.doors.FirstOrDefault(candidate => candidate.doorId == selectedId);
            if (door != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Door, floor.floorId, door));
                return true;
            }

            var window = floor.windows.FirstOrDefault(candidate => candidate.windowId == selectedId);
            if (window != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Window, floor.floorId, window));
                return true;
            }

            var exitZone = floor.exits.FirstOrDefault(candidate => candidate.exitZoneId == selectedId);
            if (exitZone != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Exit, floor.floorId, exitZone));
                return true;
            }

            var obstacle = floor.obstacles.FirstOrDefault(candidate => candidate.obstacleId == selectedId);
            if (obstacle != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Obstacle, floor.floorId, obstacle));
                return true;
            }

            var stairPortal = floor.stairPortals.FirstOrDefault(candidate => candidate.stairPortalId == selectedId);
            if (stairPortal != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Stair, floor.floorId, stairPortal));
                return true;
            }

            var region = floor.regions.FirstOrDefault(candidate => candidate.regionId == selectedId);
            if (region != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Region, floor.floorId, region));
                return true;
            }

            return false;
        }

        private static bool TryCopySpawnItem(BuildingProjectData project, string activeFloorId, string selectedId, ICollection<SandboxClipboardItem> items)
        {
            foreach (var layout in project.spawnLayouts)
            {
                var spawnPoint = layout.spawnPoints.FirstOrDefault(candidate =>
                    candidate.floorId == activeFloorId && candidate.spawnPointId == selectedId);
                if (spawnPoint != null)
                {
                    items.Add(CreateItem(SandboxClipboardItemKind.SpawnPoint, activeFloorId, spawnPoint));
                    return true;
                }

                var stroke = layout.spawnBrushStrokes.FirstOrDefault(candidate =>
                    candidate.floorId == activeFloorId && candidate.spawnBrushStrokeId == selectedId);
                if (stroke != null)
                {
                    items.Add(CreateItem(SandboxClipboardItemKind.SpawnBrush, activeFloorId, stroke));
                    return true;
                }
            }

            return false;
        }

        private static SandboxClipboardItem CreateItem<T>(SandboxClipboardItemKind kind, string floorId, T payload)
        {
            return new SandboxClipboardItem
            {
                kind = kind,
                sourceFloorId = floorId ?? string.Empty,
                serializedPayload = JsonUtility.ToJson(payload)
            };
        }

        private bool TryPasteItem(
            BuildingProjectData project,
            FloorData targetFloor,
            SandboxClipboardItem item,
            Vector2 offset,
            ICollection<string> newSelection)
        {
            switch (item.kind)
            {
                case SandboxClipboardItemKind.Door:
                    if (item.sourceFloorId != targetFloor.floorId)
                    {
                        return false;
                    }

                    var door = JsonUtility.FromJson<DoorData>(item.serializedPayload);
                    if (door == null)
                    {
                        return false;
                    }

                    door.doorId = SandboxId.NewId();
                    if (!TryMoveOpening(targetFloor, door.wallSegmentId, ref door.offsetAlongWall, door.width, offset))
                    {
                        return false;
                    }

                    targetFloor.doors.Add(door);
                    newSelection.Add(door.doorId);
                    return true;
                case SandboxClipboardItemKind.Window:
                    if (item.sourceFloorId != targetFloor.floorId)
                    {
                        return false;
                    }

                    var window = JsonUtility.FromJson<WindowData>(item.serializedPayload);
                    if (window == null)
                    {
                        return false;
                    }

                    window.windowId = SandboxId.NewId();
                    if (!TryMoveOpening(targetFloor, window.wallSegmentId, ref window.offsetAlongWall, window.width, offset))
                    {
                        return false;
                    }

                    targetFloor.windows.Add(window);
                    newSelection.Add(window.windowId);
                    return true;
                case SandboxClipboardItemKind.Exit:
                    var exitZone = JsonUtility.FromJson<ExitZoneData>(item.serializedPayload);
                    if (exitZone == null)
                    {
                        return false;
                    }

                    exitZone.exitZoneId = SandboxId.NewId();
                    exitZone.center += offset;
                    targetFloor.exits.Add(exitZone);
                    newSelection.Add(exitZone.exitZoneId);
                    return true;
                case SandboxClipboardItemKind.Obstacle:
                    var obstacle = JsonUtility.FromJson<ObstacleData>(item.serializedPayload);
                    if (obstacle == null)
                    {
                        return false;
                    }

                    obstacle.obstacleId = SandboxId.NewId();
                    obstacle.center += offset;
                    targetFloor.obstacles.Add(obstacle);
                    newSelection.Add(obstacle.obstacleId);
                    return true;
                case SandboxClipboardItemKind.Stair:
                    var stairPortal = JsonUtility.FromJson<StairPortalData>(item.serializedPayload);
                    if (stairPortal == null)
                    {
                        return false;
                    }

                    stairPortal.stairPortalId = SandboxId.NewId();
                    stairPortal.sourceFloorId = targetFloor.floorId;
                    stairPortal.targetFloorId = string.Empty;
                    stairPortal.targetStairPortalId = string.Empty;
                    stairPortal.localPosition += offset;
                    targetFloor.stairPortals.Add(stairPortal);
                    newSelection.Add(stairPortal.stairPortalId);
                    return true;
                case SandboxClipboardItemKind.Region:
                    var region = JsonUtility.FromJson<RegionData>(item.serializedPayload);
                    if (region == null)
                    {
                        return false;
                    }

                    region.regionId = SandboxId.NewId();
                    region.floorId = targetFloor.floorId;
                    for (var i = 0; i < region.polygonPoints.Count; i += 1)
                    {
                        region.polygonPoints[i] += offset;
                    }
                    targetFloor.regions.Add(region);
                    newSelection.Add(region.regionId);
                    return true;
                case SandboxClipboardItemKind.SpawnPoint:
                    var spawnPoint = JsonUtility.FromJson<SpawnPointData>(item.serializedPayload);
                    if (spawnPoint == null)
                    {
                        return false;
                    }

                    spawnPoint.spawnPointId = SandboxId.NewId();
                    spawnPoint.floorId = targetFloor.floorId;
                    spawnPoint.position += offset;
                    EnsurePasteSpawnLayout(project).spawnPoints.Add(spawnPoint);
                    newSelection.Add(spawnPoint.spawnPointId);
                    return true;
                case SandboxClipboardItemKind.SpawnBrush:
                    var stroke = JsonUtility.FromJson<SpawnBrushStrokeData>(item.serializedPayload);
                    if (stroke == null)
                    {
                        return false;
                    }

                    stroke.spawnBrushStrokeId = SandboxId.NewId();
                    stroke.floorId = targetFloor.floorId;
                    for (var i = 0; i < stroke.polygonPoints.Count; i += 1)
                    {
                        stroke.polygonPoints[i] += offset;
                    }
                    EnsurePasteSpawnLayout(project).spawnBrushStrokes.Add(stroke);
                    newSelection.Add(stroke.spawnBrushStrokeId);
                    return true;
                default:
                    return false;
            }
        }

        private static SpawnLayoutData EnsurePasteSpawnLayout(BuildingProjectData project)
        {
            var pasteLayout = project.spawnLayouts.FirstOrDefault(layout => string.Equals(layout.name, "Clipboard Paste", StringComparison.Ordinal));
            if (pasteLayout != null)
            {
                return pasteLayout;
            }

            pasteLayout = new SpawnLayoutData
            {
                spawnLayoutId = SandboxId.NewId(),
                name = "Clipboard Paste",
                isPersistent = false
            };
            project.spawnLayouts.Add(pasteLayout);
            return pasteLayout;
        }

        private bool TryMoveOpening(FloorData floor, string wallSegmentId, ref float offsetAlongWall, float width, Vector2 delta)
        {
            var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == wallSegmentId);
            if (wall == null)
            {
                return false;
            }

            var wallDirection = (wall.endPoint - wall.startPoint).normalized;
            var nextOffset = offsetAlongWall + Vector2.Dot(delta, wallDirection);
            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var halfWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(
                workspaceService,
                workspaceStateService,
                floor,
                width) * 0.5f;
            if (nextOffset - halfWidth < -0.01f || nextOffset + halfWidth > wallLength + 0.01f)
            {
                return false;
            }

            offsetAlongWall = nextOffset;
            return true;
        }

        private static bool RemoveAll<T>(List<T> items, Predicate<T> predicate)
        {
            var countBefore = items.Count;
            items.RemoveAll(predicate);
            return items.Count != countBefore;
        }

        private bool IsLocked(SandboxVisualObjectType objectType, string objectId)
        {
            return visualOrganizationService != null &&
                   (visualOrganizationService.IsTypeLocked(objectType) ||
                    visualOrganizationService.IsObjectLocked(objectId));
        }
    }
}
