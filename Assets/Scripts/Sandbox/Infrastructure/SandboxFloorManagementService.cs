using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxFloorManagementService : MonoBehaviour
    {
        [SerializeField] private bool hasPendingDeleteConfirmation;
        [SerializeField] private string pendingDeleteFloorId = string.Empty;
        [SerializeField] private string pendingDeleteMessage = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxValidationService validationService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxCommandHistory commandHistory;

        public event Action FloorsChanged;

        public bool HasPendingDeleteConfirmation => hasPendingDeleteConfirmation;
        public string PendingDeleteFloorId => pendingDeleteFloorId;
        public string PendingDeleteMessage => pendingDeleteMessage;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            validationService = GetComponent<SandboxValidationService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
        }

        public bool AddFloor(out string floorId, string name = "", float elevation = 0f, int? insertAtIndex = null)
        {
            floorId = string.Empty;
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            var nextProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            floorId = SandboxId.NewId();
            var insertionIndex = ResolveInsertionIndex(nextProject.floors, insertAtIndex);
            ShiftFloorOrders(nextProject.floors, insertionIndex, 1);

            nextProject.floors.Add(new FloorData
            {
                floorId = floorId,
                name = string.IsNullOrWhiteSpace(name) ? BuildDefaultFloorName(nextProject.floors) : name.Trim(),
                order = insertionIndex,
                elevation = elevation
            });

            NormalizeFloorOrders(nextProject.floors);
            ClearPendingDeleteConfirmation();
            ApplyProjectState(nextProject, floorId, "Add Floor");
            return true;
        }

        public bool RenameFloor(string floorId, string name)
        {
            return UpdateFloorMetadata(floorId, name, null, null);
        }

        public bool UpdateFloorMetadata(string floorId, string name, int? order, float? elevation)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(floorId))
            {
                return false;
            }

            var nextProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var floor = nextProject.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                return false;
            }

            if (name != null)
            {
                floor.name = string.IsNullOrWhiteSpace(name) ? floor.name : name.Trim();
            }

            if (elevation.HasValue)
            {
                floor.elevation = elevation.Value;
            }

            if (order.HasValue)
            {
                ReassignFloorOrder(nextProject.floors, floor.floorId, order.Value);
            }
            else
            {
                NormalizeFloorOrders(nextProject.floors);
            }

            ClearPendingDeleteConfirmation();
            ApplyProjectState(nextProject, floorId, "Update Floor Metadata");
            return true;
        }

        public bool ReorderFloor(string floorId, int newIndex)
        {
            return UpdateFloorMetadata(floorId, null, newIndex, null);
        }

        public bool DuplicateFloor(string sourceFloorId, out string duplicatedFloorId)
        {
            duplicatedFloorId = string.Empty;
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(sourceFloorId))
            {
                return false;
            }

            var nextProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var sourceFloor = nextProject.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, sourceFloorId, StringComparison.Ordinal));
            if (sourceFloor == null)
            {
                return false;
            }

            var floorCopy = CloneFloor(sourceFloor);
            var idRemap = RemapFloorEntityIds(floorCopy, sourceFloor.floorId);
            duplicatedFloorId = floorCopy.floorId;
            floorCopy.name = BuildDuplicateFloorName(nextProject.floors, sourceFloor.name);
            ReassignFloorOrder(nextProject.floors, sourceFloor.floorId, sourceFloor.order);
            ShiftFloorOrders(nextProject.floors, sourceFloor.order + 1, 1);
            floorCopy.order = sourceFloor.order + 1;
            RemapIntraFloorStairLinks(floorCopy, sourceFloor.floorId, duplicatedFloorId, idRemap);
            nextProject.floors.Add(floorCopy);
            NormalizeFloorOrders(nextProject.floors);
            ClearPendingDeleteConfirmation();
            ApplyProjectState(nextProject, duplicatedFloorId, "Duplicate Floor");
            return true;
        }

        public bool RequestDeleteFloor(string floorId)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(floorId))
            {
                return false;
            }

            var impact = AnalyzeDeleteImpact(workspaceService.ActiveProject, floorId);
            if (impact.totalImpacts == 0)
            {
                ClearPendingDeleteConfirmation();
                return DeleteFloorImmediate(floorId);
            }

            hasPendingDeleteConfirmation = true;
            pendingDeleteFloorId = floorId;
            pendingDeleteMessage =
                $"Deleting this floor may invalidate {impact.stairLinks} stair links, {impact.spawnReferences} spawn references, {impact.fireReferences} fire origins, and {impact.scenarioReferences} scenarios.";
            FloorsChanged?.Invoke();
            return false;
        }

        public bool ConfirmPendingDeleteFloor()
        {
            if (!hasPendingDeleteConfirmation || string.IsNullOrWhiteSpace(pendingDeleteFloorId))
            {
                return false;
            }

            var floorId = pendingDeleteFloorId;
            ClearPendingDeleteConfirmation();
            return DeleteFloorImmediate(floorId);
        }

        public void CancelPendingDeleteFloor()
        {
            if (!hasPendingDeleteConfirmation)
            {
                return;
            }

            ClearPendingDeleteConfirmation();
            FloorsChanged?.Invoke();
        }

        public IReadOnlyList<FloorData> GetOrderedFloors()
        {
            return workspaceService?.ActiveProject == null
                ? Array.Empty<FloorData>()
                : workspaceService.ActiveProject.floors
                    .OrderBy(floor => floor.order)
                    .ThenBy(floor => floor.name, StringComparer.Ordinal)
                    .ToList();
        }

        private bool DeleteFloorImmediate(string floorId)
        {
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            var nextProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var floor = nextProject.floors.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                return false;
            }

            nextProject.floors.Remove(floor);
            NormalizeFloorOrders(nextProject.floors);
            var nextActiveFloorId = ResolveNextActiveFloorId(nextProject.floors, floor.order);
            ApplyProjectState(nextProject, nextActiveFloorId, "Delete Floor");
            return true;
        }

        private void ApplyProjectState(BuildingProjectData project, string activeFloorId, string commandDescription)
        {
            void Apply(BuildingProjectData nextProject, string nextActiveFloorId)
            {
                workspaceService.SetActiveProject(nextProject);
                if (!string.IsNullOrWhiteSpace(nextActiveFloorId))
                {
                    workspaceService.SetActiveFloor(nextActiveFloorId);
                }

                colliderRebuildService?.RebuildAll();
                validationService?.ValidateActiveProject();
                FloorsChanged?.Invoke();
            }

            if (commandHistory == null || workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(commandDescription))
            {
                Apply(project, activeFloorId);
                return;
            }

            var beforeProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Clone(project);
            var beforeActiveFloorId = workspaceService.ActiveFloorId;

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                commandDescription,
                () => Apply(SandboxProjectSerializer.Clone(afterProject), activeFloorId),
                () => Apply(SandboxProjectSerializer.Clone(beforeProject), beforeActiveFloorId)));
        }

        private void ClearPendingDeleteConfirmation()
        {
            hasPendingDeleteConfirmation = false;
            pendingDeleteFloorId = string.Empty;
            pendingDeleteMessage = string.Empty;
        }

        private static int ResolveInsertionIndex(IReadOnlyList<FloorData> floors, int? insertAtIndex)
        {
            if (!insertAtIndex.HasValue)
            {
                return floors.Count;
            }

            return Mathf.Clamp(insertAtIndex.Value, 0, floors.Count);
        }

        private static void ShiftFloorOrders(IEnumerable<FloorData> floors, int startingOrder, int amount)
        {
            foreach (var floor in floors.Where(candidate => candidate.order >= startingOrder))
            {
                floor.order += amount;
            }
        }

        private static void ReassignFloorOrder(List<FloorData> floors, string floorId, int requestedOrder)
        {
            var orderedFloors = floors.OrderBy(floor => floor.order).ToList();
            var floor = orderedFloors.First(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            orderedFloors.Remove(floor);
            var targetIndex = Mathf.Clamp(requestedOrder, 0, orderedFloors.Count);
            orderedFloors.Insert(targetIndex, floor);

            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                orderedFloors[i].order = i;
            }
        }

        private static void NormalizeFloorOrders(List<FloorData> floors)
        {
            var orderedFloors = floors.OrderBy(floor => floor.order).ThenBy(floor => floor.name, StringComparer.Ordinal).ToList();
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                orderedFloors[i].order = i;
            }
        }

        private static string ResolveNextActiveFloorId(IReadOnlyList<FloorData> floors, int removedOrder)
        {
            if (floors.Count == 0)
            {
                return string.Empty;
            }

            var orderedFloors = floors.OrderBy(floor => floor.order).ToList();
            var targetIndex = Mathf.Clamp(removedOrder, 0, orderedFloors.Count - 1);
            return orderedFloors[targetIndex].floorId;
        }

        private static FloorData CloneFloor(FloorData sourceFloor)
        {
            return JsonUtility.FromJson<FloorData>(JsonUtility.ToJson(sourceFloor)) ?? new FloorData();
        }

        private static Dictionary<string, string> RemapFloorEntityIds(FloorData floor, string sourceFloorId)
        {
            var idRemap = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [sourceFloorId] = SandboxId.NewId()
            };

            floor.floorId = idRemap[sourceFloorId];

            for (var i = 0; i < floor.wallJunctions.Count; i += 1)
            {
                idRemap[floor.wallJunctions[i].wallJunctionId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                idRemap[floor.wallSegments[i].wallSegmentId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.doors.Count; i += 1)
            {
                idRemap[floor.doors[i].doorId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.windows.Count; i += 1)
            {
                idRemap[floor.windows[i].windowId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.exits.Count; i += 1)
            {
                idRemap[floor.exits[i].exitZoneId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.obstacles.Count; i += 1)
            {
                idRemap[floor.obstacles[i].obstacleId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.stairPortals.Count; i += 1)
            {
                idRemap[floor.stairPortals[i].stairPortalId] = SandboxId.NewId();
            }

            for (var i = 0; i < floor.regions.Count; i += 1)
            {
                idRemap[floor.regions[i].regionId] = SandboxId.NewId();
            }

            foreach (var junction in floor.wallJunctions)
            {
                junction.wallJunctionId = idRemap[junction.wallJunctionId];
                junction.connectedWallSegmentIds = junction.connectedWallSegmentIds
                    .Select(wallId => idRemap.TryGetValue(wallId, out var remappedWallId) ? remappedWallId : wallId)
                    .ToList();
            }

            foreach (var wall in floor.wallSegments)
            {
                wall.wallSegmentId = idRemap[wall.wallSegmentId];
                wall.startJunctionId = idRemap[wall.startJunctionId];
                wall.endJunctionId = idRemap[wall.endJunctionId];
            }

            foreach (var door in floor.doors)
            {
                door.doorId = idRemap[door.doorId];
                door.wallSegmentId = idRemap[door.wallSegmentId];
            }

            foreach (var window in floor.windows)
            {
                window.windowId = idRemap[window.windowId];
                window.wallSegmentId = idRemap[window.wallSegmentId];
            }

            foreach (var exitZone in floor.exits)
            {
                exitZone.exitZoneId = idRemap[exitZone.exitZoneId];
            }

            foreach (var obstacle in floor.obstacles)
            {
                obstacle.obstacleId = idRemap[obstacle.obstacleId];
            }

            foreach (var stairPortal in floor.stairPortals)
            {
                var previousPortalId = stairPortal.stairPortalId;
                stairPortal.stairPortalId = idRemap[previousPortalId];
                stairPortal.sourceFloorId = floor.floorId;
            }

            foreach (var region in floor.regions)
            {
                region.regionId = idRemap[region.regionId];
                region.floorId = floor.floorId;
            }

            return idRemap;
        }

        private static void RemapIntraFloorStairLinks(
            FloorData floor,
            string sourceFloorId,
            string duplicatedFloorId,
            IReadOnlyDictionary<string, string> idRemap)
        {
            foreach (var stairPortal in floor.stairPortals)
            {
                if (string.Equals(stairPortal.targetFloorId, sourceFloorId, StringComparison.Ordinal))
                {
                    stairPortal.targetFloorId = duplicatedFloorId;
                    if (idRemap.TryGetValue(stairPortal.targetStairPortalId, out var remappedTargetPortalId))
                    {
                        stairPortal.targetStairPortalId = remappedTargetPortalId;
                    }
                }
            }
        }

        private static string BuildDefaultFloorName(IReadOnlyList<FloorData> floors)
        {
            var nextIndex = floors.Count + 1;
            return $"Floor {nextIndex}";
        }

        private static string BuildDuplicateFloorName(IReadOnlyList<FloorData> floors, string sourceName)
        {
            var baseName = string.IsNullOrWhiteSpace(sourceName) ? "Floor Copy" : $"{sourceName} Copy";
            var candidateName = baseName;
            var copyIndex = 2;
            while (floors.Any(floor => string.Equals(floor.name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                candidateName = $"{baseName} {copyIndex}";
                copyIndex += 1;
            }

            return candidateName;
        }

        private static (int stairLinks, int spawnReferences, int fireReferences, int scenarioReferences, int totalImpacts) AnalyzeDeleteImpact(
            BuildingProjectData project,
            string floorId)
        {
            var stairLinks = project.floors.Sum(floor =>
                floor.stairPortals.Count(portal =>
                    string.Equals(portal.sourceFloorId, floorId, StringComparison.Ordinal) ||
                    string.Equals(portal.targetFloorId, floorId, StringComparison.Ordinal)));

            var spawnLayoutIds = new HashSet<string>(
                project.spawnLayouts
                    .Where(layout => layout.spawnPoints.Any(point => string.Equals(point.floorId, floorId, StringComparison.Ordinal)) ||
                                     layout.spawnBrushStrokes.Any(stroke => string.Equals(stroke.floorId, floorId, StringComparison.Ordinal)))
                    .Select(layout => layout.spawnLayoutId),
                StringComparer.Ordinal);

            var fireOriginIds = new HashSet<string>(
                project.fireOrigins
                    .Where(origin => string.Equals(origin.floorId, floorId, StringComparison.Ordinal))
                    .Select(origin => origin.fireOriginId),
                StringComparer.Ordinal);

            var scenarioReferences = project.scenarioPresets.Count(preset =>
                preset.spawnLayoutIds.Any(spawnLayoutId => spawnLayoutIds.Contains(spawnLayoutId)) ||
                preset.fireOriginIds.Any(fireOriginId => fireOriginIds.Contains(fireOriginId)));

            var spawnReferences = project.spawnLayouts.Sum(layout =>
                layout.spawnPoints.Count(point => string.Equals(point.floorId, floorId, StringComparison.Ordinal)) +
                layout.spawnBrushStrokes.Count(stroke => string.Equals(stroke.floorId, floorId, StringComparison.Ordinal)));

            var fireReferences = fireOriginIds.Count;
            return (stairLinks, spawnReferences, fireReferences, scenarioReferences, stairLinks + spawnReferences + fireReferences + scenarioReferences);
        }
    }
}
