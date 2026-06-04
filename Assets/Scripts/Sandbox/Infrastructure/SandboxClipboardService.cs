using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
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
        Teleport = 5,
        FireStart = 6,
        SpawnPoint = 7,
        SpawnBrush = 8,
        Wall = 9,
    }

    [Serializable]
    public sealed class SandboxClipboardItem
    {
        public SandboxClipboardItemKind kind;
        public string sourceFloorId = string.Empty;
        public string serializedPayload = string.Empty;
        public bool restoreTeleportLinkOnPaste;
        public string linkedFloorId = string.Empty;
        public string serializedLinkedPayload = string.Empty;
    }

    public sealed class SandboxClipboardService : MonoBehaviour
    {
        // Only endpoints that are effectively coincident (walls copied together) re-share a junction;
        // pasted walls never weld to unrelated existing geometry.
        private const float PasteWallJunctionReuseDistance = 0.001f;

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
            return CopySelection(false);
        }

        private bool CopySelection(bool restoreTeleportLinksOnPaste)
        {
            var floor = workspaceService?.ActiveFloor;
            if (floor == null || selectionService == null)
            {
                return false;
            }

            var items = new List<SandboxClipboardItem>();
            foreach (var selectedId in selectionService.SelectedObjectIds)
            {
                var didCopy = TryCopyFloorItem(workspaceService.ActiveProject, floor, selectedId, restoreTeleportLinksOnPaste, items) ||
                              TryCopySpawnItem(workspaceService.ActiveProject, floor.floorId, selectedId, items);
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

                    // Walls must paste before openings: the opening re-parent map and the host walls
                    // they may attach to have to exist first.
                    var wallIdRemap = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var item in clipboardItems.Where(candidate => candidate.kind == SandboxClipboardItemKind.Wall))
                    {
                        TryPasteItem(project, floor, item, requestedOffset, newSelection, wallIdRemap);
                    }

                    foreach (var item in clipboardItems.Where(candidate => candidate.kind != SandboxClipboardItemKind.Wall))
                    {
                        TryPasteItem(project, floor, item, requestedOffset, newSelection, wallIdRemap);
                    }

                    return newSelection;
                });
        }

        public bool DuplicateSelection(Vector2? offset = null)
        {
            return CopySelection() && PasteSelection(offset);
        }

        public bool CutSelection()
        {
            return CopySelection(true) && DeleteSelection();
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
                    didChange |= DeleteSelectedWalls(floor, selectedIds);
                    didChange |= RemoveAll(floor.doors, candidate => selectedIds.Contains(candidate.doorId) && !IsLocked(SandboxVisualObjectType.Door, candidate.doorId));
                    didChange |= RemoveAll(floor.windows, candidate => selectedIds.Contains(candidate.windowId) && !IsLocked(SandboxVisualObjectType.Window, candidate.windowId));
                    didChange |= RemoveAll(floor.exits, candidate => selectedIds.Contains(candidate.exitZoneId) && !IsLocked(SandboxVisualObjectType.Exit, candidate.exitZoneId));
                    didChange |= RemoveAll(floor.obstacles, candidate => selectedIds.Contains(candidate.obstacleId) && !IsLocked(SandboxVisualObjectType.Obstacle, candidate.obstacleId));
                    didChange |= RemoveAll(floor.stairPortals, candidate => selectedIds.Contains(candidate.stairPortalId) && !IsLocked(SandboxVisualObjectType.Stair, candidate.stairPortalId));
                    didChange |= RemoveAll(floor.teleportPortals, candidate => selectedIds.Contains(candidate.teleportPortalId) && !IsLocked(SandboxVisualObjectType.Teleport, candidate.teleportPortalId));
                    didChange |= RemoveAll(project.fireOrigins, candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.fireOriginId) && !IsLocked(SandboxVisualObjectType.FireStart, candidate.fireOriginId));

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

                    MoveSelectedWalls(floor, selectedIds, delta, movedIds);

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

                    foreach (var teleportPortal in floor.teleportPortals.Where(candidate => selectedIds.Contains(candidate.teleportPortalId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.Teleport, teleportPortal.teleportPortalId))
                        {
                            continue;
                        }

                        teleportPortal.localPosition += delta;
                        movedIds.Add(teleportPortal.teleportPortalId);
                    }

                    foreach (var fireOrigin in project.fireOrigins.Where(candidate => candidate.floorId == floor.floorId && selectedIds.Contains(candidate.fireOriginId)))
                    {
                        if (IsLocked(SandboxVisualObjectType.FireStart, fireOrigin.fireOriginId))
                        {
                            continue;
                        }

                        fireOrigin.position += delta;
                        movedIds.Add(fireOrigin.fireOriginId);
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
            // Snapshot as serialized strings WITHOUT the blueprint image payloads (re-attached from the
            // live project on restore). Paste/cut can span entity types and floors, so a whole-project
            // snapshot is the safe scope; stripping the base64 keeps each undo entry compact.
            var beforeJson = SandboxProjectSnapshot.CaptureWithoutPayloads(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Deserialize(beforeJson);
            var beforeSelection = selectionService != null
                ? new List<string>(selectionService.SelectedObjectIds)
                : new List<string>();

            var nextSelection = mutation(afterProject);
            if (nextSelection == null)
            {
                return false;
            }

            SandboxProjectDataUtility.EnsureIds(afterProject);
            var afterJson = SandboxProjectSnapshot.CaptureWithoutPayloads(afterProject);

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

            void ApplyAfter() => ApplyProject(SandboxProjectSnapshot.RestoreWithPayloads(afterJson, workspaceService.ActiveProject), nextSelection);
            void ApplyBefore() => ApplyProject(SandboxProjectSnapshot.RestoreWithPayloads(beforeJson, workspaceService.ActiveProject), beforeSelection);

            if (commandHistory == null)
            {
                ApplyAfter();
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                description,
                ApplyAfter,
                ApplyBefore,
                (long)(beforeJson.Length + afterJson.Length) * sizeof(char)));
            return true;
        }

        private static bool TryCopyFloorItem(
            BuildingProjectData project,
            FloorData floor,
            string selectedId,
            bool restoreTeleportLinksOnPaste,
            ICollection<SandboxClipboardItem> items)
        {
            var wall = floor.wallSegments.FirstOrDefault(candidate => candidate.wallSegmentId == selectedId);
            if (wall != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.Wall, floor.floorId, wall));
                return true;
            }

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

            var teleportPortal = floor.teleportPortals.FirstOrDefault(candidate => candidate.teleportPortalId == selectedId);
            if (teleportPortal != null)
            {
                var item = CreateItem(SandboxClipboardItemKind.Teleport, floor.floorId, teleportPortal);
                if (restoreTeleportLinksOnPaste &&
                    TryFindTeleportPortal(project, teleportPortal.targetTeleportPortalId, out var linkedFloor, out var linkedPortal))
                {
                    item.restoreTeleportLinkOnPaste = true;
                    item.linkedFloorId = linkedFloor.floorId;
                    item.serializedLinkedPayload = JsonUtility.ToJson(linkedPortal);
                }

                items.Add(item);
                return true;
            }

            var fireOrigin = project.fireOrigins.FirstOrDefault(candidate =>
                candidate.floorId == floor.floorId && candidate.fireOriginId == selectedId);
            if (fireOrigin != null)
            {
                items.Add(CreateItem(SandboxClipboardItemKind.FireStart, floor.floorId, fireOrigin));
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
            ICollection<string> newSelection,
            IDictionary<string, string> wallIdRemap)
        {
            switch (item.kind)
            {
                case SandboxClipboardItemKind.Wall:
                    var wallSegment = JsonUtility.FromJson<WallSegmentData>(item.serializedPayload);
                    if (wallSegment == null)
                    {
                        return false;
                    }

                    var pastedWallId = SandboxId.NewId();
                    // Near-zero reuse distance: endpoints coincident with another wall pasted in the
                    // same batch re-share a junction (preserving the copied chain's connectivity),
                    // but the paste never welds to unrelated existing geometry.
                    if (!SandboxWallAuthoringService.AddWallSegment(
                            targetFloor,
                            pastedWallId,
                            SandboxId.NewId(),
                            SandboxId.NewId(),
                            wallSegment.startPoint + offset,
                            wallSegment.endPoint + offset,
                            wallSegment.thickness,
                            PasteWallJunctionReuseDistance))
                    {
                        return false;
                    }

                    if (wallIdRemap != null && !string.IsNullOrWhiteSpace(wallSegment.wallSegmentId))
                    {
                        wallIdRemap[wallSegment.wallSegmentId] = pastedWallId;
                    }

                    newSelection.Add(pastedWallId);
                    return true;
                case SandboxClipboardItemKind.Door:
                    var door = JsonUtility.FromJson<DoorData>(item.serializedPayload);
                    if (door == null)
                    {
                        return false;
                    }

                    door.doorId = SandboxId.NewId();
                    if (!TryResolveOpeningHostWall(item, ref door.wallSegmentId, targetFloor, wallIdRemap, out var doorFollowedWall))
                    {
                        return false;
                    }

                    // A re-parented opening rides a wall that was already shifted by the paste offset,
                    // so its position along that wall is unchanged; a standalone opening shifts along
                    // its original wall by the offset.
                    if (!TryMoveOpening(targetFloor, door.wallSegmentId, ref door.offsetAlongWall, door.width, doorFollowedWall ? Vector2.zero : offset))
                    {
                        return false;
                    }

                    targetFloor.doors.Add(door);
                    newSelection.Add(door.doorId);
                    return true;
                case SandboxClipboardItemKind.Window:
                    var window = JsonUtility.FromJson<WindowData>(item.serializedPayload);
                    if (window == null)
                    {
                        return false;
                    }

                    window.windowId = SandboxId.NewId();
                    if (!TryResolveOpeningHostWall(item, ref window.wallSegmentId, targetFloor, wallIdRemap, out var windowFollowedWall))
                    {
                        return false;
                    }

                    if (!TryMoveOpening(targetFloor, window.wallSegmentId, ref window.offsetAlongWall, window.width, windowFollowedWall ? Vector2.zero : offset))
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
                case SandboxClipboardItemKind.Teleport:
                    var teleportPortal = JsonUtility.FromJson<TeleportPortalData>(item.serializedPayload);
                    if (teleportPortal == null)
                    {
                        return false;
                    }

                    var pastedTeleportPortalId = SandboxId.NewId();
                    var pairId = item.restoreTeleportLinkOnPaste && !string.IsNullOrWhiteSpace(teleportPortal.pairId)
                        ? teleportPortal.pairId
                        : SandboxId.NewId();

                    teleportPortal.teleportPortalId = pastedTeleportPortalId;
                    teleportPortal.pairId = pairId;
                    teleportPortal.sourceFloorId = targetFloor.floorId;
                    teleportPortal.localPosition += offset;
                    if (item.restoreTeleportLinkOnPaste &&
                        TryResolveLinkedTeleportForPaste(project, item, out var linkedFloor, out var linkedPortal))
                    {
                        teleportPortal.targetFloorId = linkedFloor.floorId;
                        teleportPortal.targetTeleportPortalId = linkedPortal.teleportPortalId;
                        linkedPortal.pairId = pairId;
                        linkedPortal.pairColorIndex = teleportPortal.pairColorIndex;
                        linkedPortal.targetFloorId = targetFloor.floorId;
                        linkedPortal.targetTeleportPortalId = pastedTeleportPortalId;
                    }
                    else
                    {
                        teleportPortal.targetFloorId = string.Empty;
                        teleportPortal.targetTeleportPortalId = string.Empty;
                    }

                    targetFloor.teleportPortals.Add(teleportPortal);
                    newSelection.Add(teleportPortal.teleportPortalId);
                    return true;
                case SandboxClipboardItemKind.FireStart:
                    var fireOrigin = JsonUtility.FromJson<FireOriginData>(item.serializedPayload);
                    if (fireOrigin == null)
                    {
                        return false;
                    }

                    fireOrigin.fireOriginId = SandboxId.NewId();
                    fireOrigin.floorId = targetFloor.floorId;
                    fireOrigin.position += offset;
                    project.fireOrigins.Add(fireOrigin);
                    newSelection.Add(fireOrigin.fireOriginId);
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

        private static bool TryResolveLinkedTeleportForPaste(
            BuildingProjectData project,
            SandboxClipboardItem item,
            out FloorData linkedFloor,
            out TeleportPortalData linkedPortal)
        {
            linkedFloor = null;
            linkedPortal = null;
            if (project?.floors == null || item == null || string.IsNullOrWhiteSpace(item.serializedLinkedPayload))
            {
                return false;
            }

            var savedLinkedPortal = JsonUtility.FromJson<TeleportPortalData>(item.serializedLinkedPayload);
            if (savedLinkedPortal == null || string.IsNullOrWhiteSpace(savedLinkedPortal.teleportPortalId))
            {
                return false;
            }

            if (TryFindTeleportPortal(project, savedLinkedPortal.teleportPortalId, out linkedFloor, out linkedPortal))
            {
                return true;
            }

            var floorId = !string.IsNullOrWhiteSpace(item.linkedFloorId)
                ? item.linkedFloorId
                : savedLinkedPortal.sourceFloorId;
            linkedFloor = project.floors.FirstOrDefault(floor => string.Equals(floor.floorId, floorId, StringComparison.Ordinal));
            if (linkedFloor == null)
            {
                return false;
            }

            savedLinkedPortal.sourceFloorId = linkedFloor.floorId;
            linkedFloor.teleportPortals.Add(savedLinkedPortal);
            linkedPortal = savedLinkedPortal;
            return true;
        }

        private static bool TryFindTeleportPortal(
            BuildingProjectData project,
            string teleportPortalId,
            out FloorData floor,
            out TeleportPortalData teleportPortal)
        {
            floor = null;
            teleportPortal = null;
            if (project?.floors == null || string.IsNullOrWhiteSpace(teleportPortalId))
            {
                return false;
            }

            for (var i = 0; i < project.floors.Count; i += 1)
            {
                teleportPortal = project.floors[i].teleportPortals.FirstOrDefault(candidate =>
                    string.Equals(candidate.teleportPortalId, teleportPortalId, StringComparison.Ordinal));
                if (teleportPortal == null)
                {
                    continue;
                }

                floor = project.floors[i];
                return true;
            }

            return false;
        }

        // Resolves which wall a pasted opening attaches to. If its original host wall was copied in
        // the same batch, the opening follows the pasted wall (and may cross floors with it).
        // Otherwise it stays on its original wall, which only exists on the source floor.
        private static bool TryResolveOpeningHostWall(
            SandboxClipboardItem item,
            ref string wallSegmentId,
            FloorData targetFloor,
            IDictionary<string, string> wallIdRemap,
            out bool followedCopiedWall)
        {
            followedCopiedWall = false;
            if (wallIdRemap != null &&
                !string.IsNullOrWhiteSpace(wallSegmentId) &&
                wallIdRemap.TryGetValue(wallSegmentId, out var remappedWallId))
            {
                wallSegmentId = remappedWallId;
                followedCopiedWall = true;
                return true;
            }

            return item.sourceFloorId == targetFloor.floorId;
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

        private bool DeleteSelectedWalls(FloorData floor, HashSet<string> selectedIds)
        {
            if (floor == null || selectedIds == null || selectedIds.Count == 0)
            {
                return false;
            }

            var wallIds = floor.wallSegments
                .Where(candidate => selectedIds.Contains(candidate.wallSegmentId) && !IsLocked(SandboxVisualObjectType.Wall, candidate.wallSegmentId))
                .Select(candidate => candidate.wallSegmentId)
                .ToList();
            if (wallIds.Count == 0)
            {
                return false;
            }

            floor.doors.RemoveAll(candidate => wallIds.Contains(candidate.wallSegmentId));
            floor.windows.RemoveAll(candidate => wallIds.Contains(candidate.wallSegmentId));

            for (var i = 0; i < wallIds.Count; i += 1)
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, wallIds[i], StringComparison.Ordinal));
                if (wall == null)
                {
                    continue;
                }

                var startJunctionId = wall.startJunctionId;
                var endJunctionId = wall.endJunctionId;
                var startJunction = FindJunction(floor, startJunctionId);
                var endJunction = FindJunction(floor, endJunctionId);
                if (startJunction != null)
                {
                    RemoveConnection(startJunction, wall.wallSegmentId);
                }

                if (endJunction != null)
                {
                    RemoveConnection(endJunction, wall.wallSegmentId);
                }

                floor.wallSegments.Remove(wall);
                PruneJunctionIfOrphan(floor, startJunctionId);
                PruneJunctionIfOrphan(floor, endJunctionId);
            }

            return true;
        }

        private void MoveSelectedWalls(FloorData floor, HashSet<string> selectedIds, Vector2 delta, List<string> movedIds)
        {
            if (floor == null || selectedIds == null || movedIds == null || delta.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var selectedWallIds = floor.wallSegments
                .Where(candidate => selectedIds.Contains(candidate.wallSegmentId) && !IsLocked(SandboxVisualObjectType.Wall, candidate.wallSegmentId))
                .Select(candidate => candidate.wallSegmentId)
                .ToHashSet(StringComparer.Ordinal);
            if (selectedWallIds.Count == 0)
            {
                return;
            }

            foreach (var wallId in selectedWallIds.ToList())
            {
                var wall = floor.wallSegments.FirstOrDefault(candidate =>
                    string.Equals(candidate.wallSegmentId, wallId, StringComparison.Ordinal));
                if (wall == null)
                {
                    continue;
                }

                DetachSharedWallEndpointIfNeeded(floor, wall, true, selectedWallIds);
                DetachSharedWallEndpointIfNeeded(floor, wall, false, selectedWallIds);
            }

            var junctionIdsToMove = floor.wallSegments
                .Where(candidate => selectedWallIds.Contains(candidate.wallSegmentId))
                .SelectMany(candidate => new[] { candidate.startJunctionId, candidate.endJunctionId })
                .Distinct(StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < junctionIdsToMove.Count; i += 1)
            {
                var junction = FindJunction(floor, junctionIdsToMove[i]);
                if (junction != null)
                {
                    junction.position += delta;
                }
            }

            SyncWallPointsFromJunctions(floor);
            movedIds.AddRange(selectedWallIds);
        }

        private static void DetachSharedWallEndpointIfNeeded(FloorData floor, WallSegmentData wall, bool isStartPoint, HashSet<string> selectedWallIds)
        {
            if (floor == null || wall == null || selectedWallIds == null)
            {
                return;
            }

            var junctionId = isStartPoint ? wall.startJunctionId : wall.endJunctionId;
            var junction = FindJunction(floor, junctionId);
            if (junction == null)
            {
                return;
            }

            var hasUnselectedNeighbors = junction.connectedWallSegmentIds.Any(id => !selectedWallIds.Contains(id));
            if (!hasUnselectedNeighbors)
            {
                return;
            }

            var detachedJunction = new WallJunctionData
            {
                wallJunctionId = SandboxId.NewId(),
                position = junction.position
            };
            detachedJunction.connectedWallSegmentIds.Add(wall.wallSegmentId);
            floor.wallJunctions.Add(detachedJunction);
            RemoveConnection(junction, wall.wallSegmentId);

            if (isStartPoint)
            {
                wall.startJunctionId = detachedJunction.wallJunctionId;
                wall.startPoint = detachedJunction.position;
            }
            else
            {
                wall.endJunctionId = detachedJunction.wallJunctionId;
                wall.endPoint = detachedJunction.position;
            }
        }

        private static void SyncWallPointsFromJunctions(FloorData floor)
        {
            if (floor == null)
            {
                return;
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                var startJunction = FindJunction(floor, wall.startJunctionId);
                var endJunction = FindJunction(floor, wall.endJunctionId);
                if (startJunction != null)
                {
                    wall.startPoint = startJunction.position;
                }

                if (endJunction != null)
                {
                    wall.endPoint = endJunction.position;
                }
            }
        }

        private static WallJunctionData FindJunction(FloorData floor, string wallJunctionId)
        {
            return floor?.wallJunctions.FirstOrDefault(junction =>
                string.Equals(junction.wallJunctionId, wallJunctionId, StringComparison.Ordinal));
        }

        private static void RemoveConnection(WallJunctionData junction, string wallSegmentId)
        {
            junction?.connectedWallSegmentIds.RemoveAll(id => string.Equals(id, wallSegmentId, StringComparison.Ordinal));
        }

        private static void PruneJunctionIfOrphan(FloorData floor, string wallJunctionId)
        {
            var junction = FindJunction(floor, wallJunctionId);
            if (junction != null && junction.connectedWallSegmentIds.Count == 0)
            {
                floor.wallJunctions.Remove(junction);
            }
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
