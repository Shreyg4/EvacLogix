using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Migrations;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxFloorImportConflictType
    {
        DuplicateFloorId = 0,
        DuplicateFloorName = 1,
        MissingFloorSelection = 2,
        BrokenStairLink = 3,
        ExternalStairLink = 4,
        CrossFloorReference = 5,
        ImportSourceError = 6,
        DuplicateEntityId = 7,
    }

    [Serializable]
    public sealed class SandboxFloorImportConflict
    {
        public SandboxFloorImportConflictType conflictType;
        public string floorId = string.Empty;
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class SandboxFloorImportAnalysis
    {
        public string sourcePath = string.Empty;
        public List<string> selectedFloorIds = new();
        public List<SandboxFloorImportConflict> conflicts = new();

        public bool CanImport => conflicts.Count == 0 && selectedFloorIds.Count > 0;
    }

    public sealed class SandboxProjectTransferService : MonoBehaviour
    {
        [SerializeField] private string lastExportJsonPath = string.Empty;
        [SerializeField] private string lastImportedJsonPath = string.Empty;
        [SerializeField] private string lastRuntimeExportPath = string.Empty;
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private SandboxFloorImportAnalysis lastFloorImportAnalysis = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxProjectRefreshService projectRefreshService;
        private SandboxSaveLoadService saveLoadService;
        private SandboxValidationService validationService;

        public event Action TransferStateChanged;

        public string LastExportJsonPath => lastExportJsonPath;
        public string LastImportedJsonPath => lastImportedJsonPath;
        public string LastRuntimeExportPath => lastRuntimeExportPath;
        public string LastError => lastError;
        public SandboxFloorImportAnalysis LastFloorImportAnalysis => lastFloorImportAnalysis;

        private void Awake()
        {
            RefreshDependenciesIfNeeded();
        }

        public bool ExportProjectJson(string filePath, bool prettyPrint = true)
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                SandboxProjectFileStorage.WriteProjectToPath(filePath, workspaceService.ActiveProject, prettyPrint);
                lastExportJsonPath = filePath;
                ClearError();
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public BuildingProjectData ImportProjectJson(string filePath)
        {
            RefreshDependenciesIfNeeded();
            if (saveLoadService == null)
            {
                return null;
            }

            var project = saveLoadService.LoadProjectFromPath(filePath, true);
            if (project == null)
            {
                lastError = saveLoadService.LastError;
                RaiseStateChanged();
                return null;
            }

            projectRefreshService?.RefreshDerivedProjectState();
            lastImportedJsonPath = filePath;
            ClearError();
            RaiseStateChanged();
            return project;
        }

        public BuildingProjectData ImportProjectJsonContent(string json, string sourceLabel = "")
        {
            RefreshDependenciesIfNeeded();
            if (saveLoadService == null)
            {
                return null;
            }

            var project = saveLoadService.LoadProjectFromJson(json);
            if (project == null)
            {
                lastError = saveLoadService.LastError;
                RaiseStateChanged();
                return null;
            }

            projectRefreshService?.RefreshDerivedProjectState();
            lastImportedJsonPath = sourceLabel ?? string.Empty;
            ClearError();
            RaiseStateChanged();
            return project;
        }

        public bool ExportRuntimeProjectData(string filePath, bool prettyPrint = true)
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            if (validationService != null && !validationService.CanPreviewOrExport())
            {
                lastError = "Resolve blocking validation issues before exporting runtime-ready data.";
                RaiseStateChanged();
                return false;
            }

            try
            {
                var project = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
                var timestamp = DateTime.UtcNow.ToString("O");
                project.metadata.lastRuntimeExportUtc = timestamp;
                project.metadata.updatedUtc = timestamp;
                SandboxProjectFileStorage.WriteProjectToPath(filePath, project, prettyPrint);
                workspaceService.SetActiveProject(project);
                projectRefreshService?.RefreshDerivedProjectState();
                lastRuntimeExportPath = filePath;
                ClearError();
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public SandboxExportFileData BuildProjectJsonExportPayload(bool prettyPrint = true, string fileName = "sandbox-project.json")
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveProject == null)
            {
                return null;
            }

            var json = SandboxProjectSerializer.Serialize(workspaceService.ActiveProject, prettyPrint);
            var bytes = Encoding.UTF8.GetBytes(json);
            return new SandboxExportFileData
            {
                fileName = fileName,
                mimeType = "application/json",
                sizeBytes = bytes.LongLength,
                payloadBase64 = Convert.ToBase64String(bytes)
            };
        }

        public SandboxExportFileData BuildRuntimeProjectExportPayload(bool prettyPrint = true, string fileName = "sandbox-runtime-project.json")
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveProject == null)
            {
                return null;
            }

            if (validationService != null && !validationService.CanPreviewOrExport())
            {
                lastError = "Resolve blocking validation issues before exporting runtime-ready data.";
                RaiseStateChanged();
                return null;
            }

            var project = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var timestamp = DateTime.UtcNow.ToString("O");
            project.metadata.lastRuntimeExportUtc = timestamp;
            project.metadata.updatedUtc = timestamp;
            var json = SandboxProjectSerializer.Serialize(project, prettyPrint);
            var bytes = Encoding.UTF8.GetBytes(json);
            ClearError();
            RaiseStateChanged();
            return new SandboxExportFileData
            {
                fileName = fileName,
                mimeType = "application/json",
                sizeBytes = bytes.LongLength,
                payloadBase64 = Convert.ToBase64String(bytes)
            };
        }

        public SandboxFloorImportAnalysis AnalyzeFloorImportFromPath(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            RefreshDependenciesIfNeeded();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return SetLastFloorImportAnalysis(new SandboxFloorImportAnalysis
                {
                    sourcePath = filePath ?? string.Empty,
                    conflicts =
                    {
                        new SandboxFloorImportConflict
                        {
                            conflictType = SandboxFloorImportConflictType.ImportSourceError,
                            message = "Import source file was not found."
                        }
                    }
                });
            }

            try
            {
                var sourceProject = SandboxProjectFileStorage.ReadProjectFromPath(filePath);
                var analysis = AnalyzeFloorImport(sourceProject, selectedFloorIds);
                analysis.sourcePath = filePath;
                return SetLastFloorImportAnalysis(analysis);
            }
            catch (Exception exception) when (exception is IOException || exception is SandboxMigrationException || exception is ArgumentException)
            {
                return SetLastFloorImportAnalysis(new SandboxFloorImportAnalysis
                {
                    sourcePath = filePath,
                    conflicts =
                    {
                        new SandboxFloorImportConflict
                        {
                            conflictType = SandboxFloorImportConflictType.ImportSourceError,
                            message = exception.Message
                        }
                    }
                });
            }
        }

        public bool ImportFloorsFromPath(string filePath, IEnumerable<string> selectedFloorIds = null)
        {
            RefreshDependenciesIfNeeded();
            if (workspaceService?.ActiveProject == null)
            {
                lastError = "Create or open a project before importing floors.";
                RaiseStateChanged();
                return false;
            }

            var analysis = AnalyzeFloorImportFromPath(filePath, selectedFloorIds);
            if (!analysis.CanImport)
            {
                lastError = analysis.conflicts.FirstOrDefault()?.message ?? "Floor import failed.";
                RaiseStateChanged();
                return false;
            }

            try
            {
                var sourceProject = SandboxProjectFileStorage.ReadProjectFromPath(filePath);
                var requestedIds = analysis.selectedFloorIds.ToHashSet(StringComparer.Ordinal);
                var selectedFloors = sourceProject.floors
                    .Where(floor => requestedIds.Contains(floor.floorId))
                    .Select(CloneFloor)
                    .ToList();

                var nextProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
                var blueprintIdRemap = ImportBlueprintReferences(nextProject, sourceProject, selectedFloors);
                var nextOrder = nextProject.floors.Count == 0 ? 0 : nextProject.floors.Max(floor => floor.order) + 1;

                for (var i = 0; i < selectedFloors.Count; i += 1)
                {
                    if (blueprintIdRemap.TryGetValue(selectedFloors[i].blueprintReferenceId, out var remappedBlueprintId))
                    {
                        selectedFloors[i].blueprintReferenceId = remappedBlueprintId;
                    }

                    selectedFloors[i].order = nextOrder + i;
                    nextProject.floors.Add(selectedFloors[i]);
                }

                NormalizeFloorOrders(nextProject.floors);
                workspaceService.SetActiveProject(nextProject);
                if (selectedFloors.Count > 0)
                {
                    workspaceService.SetActiveFloor(selectedFloors[0].floorId);
                }

                projectRefreshService?.RefreshDerivedProjectState();
                ClearError();
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is SandboxMigrationException || exception is ArgumentException)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        private SandboxFloorImportAnalysis AnalyzeFloorImport(BuildingProjectData sourceProject, IEnumerable<string> selectedFloorIds)
        {
            var analysis = new SandboxFloorImportAnalysis();
            var requestedIds = selectedFloorIds == null
                ? new HashSet<string>(sourceProject.floors.Select(floor => floor.floorId), StringComparer.Ordinal)
                : new HashSet<string>(selectedFloorIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);

            if (requestedIds.Count == 0)
            {
                analysis.conflicts.Add(new SandboxFloorImportConflict
                {
                    conflictType = SandboxFloorImportConflictType.MissingFloorSelection,
                    message = "No floors were selected for import."
                });
                return analysis;
            }

            var selectedFloors = sourceProject.floors.Where(floor => requestedIds.Contains(floor.floorId)).ToList();
            analysis.selectedFloorIds = selectedFloors.Select(floor => floor.floorId).ToList();
            var resolvedFloorIds = analysis.selectedFloorIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (resolvedFloorIds.Length != requestedIds.Count)
            {
                var missingFloorIds = requestedIds.Except(resolvedFloorIds, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal);
                foreach (var missingFloorId in missingFloorIds)
                {
                    analysis.conflicts.Add(new SandboxFloorImportConflict
                    {
                        conflictType = SandboxFloorImportConflictType.MissingFloorSelection,
                        floorId = missingFloorId,
                        message = $"Selected floor '{missingFloorId}' was not found in the import source."
                    });
                }
            }

            foreach (var duplicateFloorId in selectedFloors
                         .Where(floor => !string.IsNullOrWhiteSpace(floor.floorId))
                         .GroupBy(floor => floor.floorId, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                analysis.conflicts.Add(new SandboxFloorImportConflict
                {
                    conflictType = SandboxFloorImportConflictType.DuplicateFloorId,
                    floorId = duplicateFloorId.Key,
                    message = $"Duplicate floor ID '{duplicateFloorId.Key}' appears multiple times in the selected import set."
                });
            }

            foreach (var duplicateFloorName in selectedFloors
                         .Where(floor => !string.IsNullOrWhiteSpace(floor.name))
                         .GroupBy(floor => floor.name, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1))
            {
                analysis.conflicts.Add(new SandboxFloorImportConflict
                {
                    conflictType = SandboxFloorImportConflictType.DuplicateFloorName,
                    floorId = duplicateFloorName.First().floorId,
                    message = $"Duplicate floor name '{duplicateFloorName.First().name}' appears multiple times in the selected import set."
                });
            }

            var selectedFloorIdSet = resolvedFloorIds.ToHashSet(StringComparer.Ordinal);
            var currentProject = workspaceService?.ActiveProject;
            var existingFloorIds = currentProject?.floors.Select(floor => floor.floorId).ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var existingFloorNames = currentProject?.floors.Select(floor => floor.name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingEntityIds = currentProject == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(EnumerateFloorEntityIds(currentProject.floors), StringComparer.Ordinal);

            foreach (var floor in selectedFloors)
            {
                if (existingFloorIds.Contains(floor.floorId))
                {
                    analysis.conflicts.Add(new SandboxFloorImportConflict
                    {
                        conflictType = SandboxFloorImportConflictType.DuplicateFloorId,
                        floorId = floor.floorId,
                        message = $"Floor ID '{floor.floorId}' already exists in the current project."
                    });
                }

                if (!string.IsNullOrWhiteSpace(floor.name) && existingFloorNames.Contains(floor.name))
                {
                    analysis.conflicts.Add(new SandboxFloorImportConflict
                    {
                        conflictType = SandboxFloorImportConflictType.DuplicateFloorName,
                        floorId = floor.floorId,
                        message = $"Floor name '{floor.name}' already exists in the current project."
                    });
                }

                foreach (var entityId in EnumerateFloorEntityIds(new[] { floor }))
                {
                    if (existingEntityIds.Contains(entityId))
                    {
                        analysis.conflicts.Add(new SandboxFloorImportConflict
                        {
                            conflictType = SandboxFloorImportConflictType.DuplicateEntityId,
                            floorId = floor.floorId,
                            message = $"Imported floor '{floor.name}' reuses object ID '{entityId}' that already exists in the current project."
                        });
                    }

                    existingEntityIds.Add(entityId);
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    if (string.IsNullOrWhiteSpace(stairPortal.targetFloorId) || string.IsNullOrWhiteSpace(stairPortal.targetStairPortalId))
                    {
                        continue;
                    }

                    if (selectedFloorIdSet.Contains(stairPortal.targetFloorId))
                    {
                        var targetFloor = selectedFloors.FirstOrDefault(candidate =>
                            string.Equals(candidate.floorId, stairPortal.targetFloorId, StringComparison.Ordinal));
                        var targetExists = targetFloor != null && targetFloor.stairPortals.Any(candidate =>
                            string.Equals(candidate.stairPortalId, stairPortal.targetStairPortalId, StringComparison.Ordinal));
                        if (!targetExists)
                        {
                            analysis.conflicts.Add(new SandboxFloorImportConflict
                            {
                                conflictType = SandboxFloorImportConflictType.BrokenStairLink,
                                floorId = floor.floorId,
                                message = $"Stair portal '{stairPortal.stairPortalId}' references missing imported portal '{stairPortal.targetStairPortalId}'."
                            });
                        }

                        continue;
                    }

                    analysis.conflicts.Add(new SandboxFloorImportConflict
                    {
                        conflictType = SandboxFloorImportConflictType.ExternalStairLink,
                        floorId = floor.floorId,
                        message = $"Stair portal '{stairPortal.stairPortalId}' links to floor '{stairPortal.targetFloorId}', which is outside the selected floor import set."
                    });
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    if (string.IsNullOrWhiteSpace(teleportPortal.targetFloorId) || string.IsNullOrWhiteSpace(teleportPortal.targetTeleportPortalId))
                    {
                        continue;
                    }

                    if (selectedFloorIdSet.Contains(teleportPortal.targetFloorId))
                    {
                        var targetFloor = selectedFloors.FirstOrDefault(candidate =>
                            string.Equals(candidate.floorId, teleportPortal.targetFloorId, StringComparison.Ordinal));
                        var targetExists = targetFloor != null && targetFloor.teleportPortals.Any(candidate =>
                            string.Equals(candidate.teleportPortalId, teleportPortal.targetTeleportPortalId, StringComparison.Ordinal));
                        if (!targetExists)
                        {
                            analysis.conflicts.Add(new SandboxFloorImportConflict
                            {
                                conflictType = SandboxFloorImportConflictType.BrokenStairLink,
                                floorId = floor.floorId,
                                message = $"Teleporter '{teleportPortal.teleportPortalId}' references missing imported endpoint '{teleportPortal.targetTeleportPortalId}'."
                            });
                        }

                        continue;
                    }

                    analysis.conflicts.Add(new SandboxFloorImportConflict
                    {
                        conflictType = SandboxFloorImportConflictType.CrossFloorReference,
                        floorId = floor.floorId,
                        message = $"Teleporter '{teleportPortal.teleportPortalId}' links to floor '{teleportPortal.targetFloorId}', which is outside the selected floor import set."
                    });
                }
            }

            if (HasCrossFloorReferences(sourceProject, selectedFloorIdSet))
            {
                analysis.conflicts.Add(new SandboxFloorImportConflict
                {
                    conflictType = SandboxFloorImportConflictType.CrossFloorReference,
                    message = "Selected floors are referenced by spawns, fire origins, or scenarios. Floor-only import does not carry those project-level references."
                });
            }

            return analysis;
        }

        private static bool HasCrossFloorReferences(BuildingProjectData sourceProject, HashSet<string> selectedFloorIds)
        {
            if (sourceProject.spawnLayouts.Any(layout =>
                    layout.spawnPoints.Any(point => selectedFloorIds.Contains(point.floorId)) ||
                    layout.spawnBrushStrokes.Any(stroke => selectedFloorIds.Contains(stroke.floorId))))
            {
                return true;
            }

            if (sourceProject.fireOrigins.Any(origin => selectedFloorIds.Contains(origin.floorId)))
            {
                return true;
            }

            var referencedSpawnLayoutIds = sourceProject.spawnLayouts
                .Where(layout =>
                    layout.spawnPoints.Any(point => selectedFloorIds.Contains(point.floorId)) ||
                    layout.spawnBrushStrokes.Any(stroke => selectedFloorIds.Contains(stroke.floorId)))
                .Select(layout => layout.spawnLayoutId)
                .ToHashSet(StringComparer.Ordinal);

            var referencedFireOriginIds = sourceProject.fireOrigins
                .Where(origin => selectedFloorIds.Contains(origin.floorId))
                .Select(origin => origin.fireOriginId)
                .ToHashSet(StringComparer.Ordinal);

            return sourceProject.scenarioPresets.Any(preset =>
                preset.spawnLayoutIds.Any(referencedSpawnLayoutIds.Contains) ||
                preset.fireOriginIds.Any(referencedFireOriginIds.Contains));
        }

        private static Dictionary<string, string> ImportBlueprintReferences(
            BuildingProjectData targetProject,
            BuildingProjectData sourceProject,
            IEnumerable<FloorData> selectedFloors)
        {
            var remap = new Dictionary<string, string>(StringComparer.Ordinal);
            var neededBlueprintIds = selectedFloors
                .Select(floor => floor.blueprintReferenceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var blueprintId in neededBlueprintIds)
            {
                var sourceBlueprint = sourceProject.blueprintReferences.FirstOrDefault(candidate =>
                    string.Equals(candidate.blueprintReferenceId, blueprintId, StringComparison.Ordinal));
                if (sourceBlueprint == null)
                {
                    continue;
                }

                var existingBlueprint = targetProject.blueprintReferences.FirstOrDefault(candidate =>
                    string.Equals(candidate.blueprintReferenceId, blueprintId, StringComparison.Ordinal));
                if (existingBlueprint == null)
                {
                    targetProject.blueprintReferences.Add(CloneBlueprint(sourceBlueprint));
                    remap[blueprintId] = blueprintId;
                    continue;
                }

                if ((string.Equals(existingBlueprint.assetPath, sourceBlueprint.assetPath, StringComparison.Ordinal) &&
                    string.Equals(existingBlueprint.assetGuid, sourceBlueprint.assetGuid, StringComparison.Ordinal)) ||
                    string.Equals(existingBlueprint.importedPayloadBase64, sourceBlueprint.importedPayloadBase64, StringComparison.Ordinal))
                {
                    remap[blueprintId] = existingBlueprint.blueprintReferenceId;
                    continue;
                }

                var importedBlueprint = CloneBlueprint(sourceBlueprint);
                importedBlueprint.blueprintReferenceId = SandboxId.NewId();
                targetProject.blueprintReferences.Add(importedBlueprint);
                remap[blueprintId] = importedBlueprint.blueprintReferenceId;
            }

            return remap;
        }

        private static FloorData CloneFloor(FloorData floor)
        {
            return JsonUtility.FromJson<FloorData>(JsonUtility.ToJson(floor)) ?? new FloorData();
        }

        private static IEnumerable<string> EnumerateFloorEntityIds(IEnumerable<FloorData> floors)
        {
            foreach (var floor in floors)
            {
                foreach (var junction in floor.wallJunctions)
                {
                    if (!string.IsNullOrWhiteSpace(junction.wallJunctionId))
                    {
                        yield return junction.wallJunctionId;
                    }
                }

                foreach (var wall in floor.wallSegments)
                {
                    if (!string.IsNullOrWhiteSpace(wall.wallSegmentId))
                    {
                        yield return wall.wallSegmentId;
                    }
                }

                foreach (var door in floor.doors)
                {
                    if (!string.IsNullOrWhiteSpace(door.doorId))
                    {
                        yield return door.doorId;
                    }
                }

                foreach (var window in floor.windows)
                {
                    if (!string.IsNullOrWhiteSpace(window.windowId))
                    {
                        yield return window.windowId;
                    }
                }

                foreach (var exitZone in floor.exits)
                {
                    if (!string.IsNullOrWhiteSpace(exitZone.exitZoneId))
                    {
                        yield return exitZone.exitZoneId;
                    }
                }

                foreach (var obstacle in floor.obstacles)
                {
                    if (!string.IsNullOrWhiteSpace(obstacle.obstacleId))
                    {
                        yield return obstacle.obstacleId;
                    }
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    if (!string.IsNullOrWhiteSpace(stairPortal.stairPortalId))
                    {
                        yield return stairPortal.stairPortalId;
                    }
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    if (!string.IsNullOrWhiteSpace(teleportPortal.teleportPortalId))
                    {
                        yield return teleportPortal.teleportPortalId;
                    }
                }

                foreach (var region in floor.regions)
                {
                    if (!string.IsNullOrWhiteSpace(region.regionId))
                    {
                        yield return region.regionId;
                    }
                }
            }
        }

        private static BlueprintReferenceData CloneBlueprint(BlueprintReferenceData blueprint)
        {
            return JsonUtility.FromJson<BlueprintReferenceData>(JsonUtility.ToJson(blueprint)) ?? new BlueprintReferenceData();
        }

        private static void NormalizeFloorOrders(List<FloorData> floors)
        {
            var orderedFloors = floors.OrderBy(floor => floor.order).ThenBy(floor => floor.name, StringComparer.Ordinal).ToList();
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                orderedFloors[i].order = i;
            }
        }

        private SandboxFloorImportAnalysis SetLastFloorImportAnalysis(SandboxFloorImportAnalysis analysis)
        {
            lastFloorImportAnalysis = analysis ?? new SandboxFloorImportAnalysis();
            lastError = lastFloorImportAnalysis.conflicts.FirstOrDefault()?.message ?? string.Empty;
            RaiseStateChanged();
            return lastFloorImportAnalysis;
        }

        private void ClearError()
        {
            lastError = string.Empty;
        }

        private void RaiseStateChanged()
        {
            TransferStateChanged?.Invoke();
        }

        private void RefreshDependenciesIfNeeded()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            projectRefreshService ??= GetComponent<SandboxProjectRefreshService>();
            saveLoadService ??= GetComponent<SandboxSaveLoadService>();
            validationService ??= GetComponent<SandboxValidationService>();
        }
    }
}
