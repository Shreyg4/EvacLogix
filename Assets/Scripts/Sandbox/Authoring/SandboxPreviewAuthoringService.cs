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
    public sealed class SandboxPreviewAuthoringService : MonoBehaviour
    {
        [SerializeField] private float defaultSpawnBrushDensity = 1f;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxSelectionService selectionService;
        private SandboxValidationService validationService;
        private SandboxPreviewService previewService;
        private SandboxRoomDetectionService roomDetectionService;

        public event Action PreviewAuthoringChanged;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            selectionService = GetComponent<SandboxSelectionService>();
            validationService = GetComponent<SandboxValidationService>();
            previewService = GetComponent<SandboxPreviewService>();
            roomDetectionService = GetComponent<SandboxRoomDetectionService>();
        }

        public IReadOnlyList<SpawnLayoutData> GetSpawnLayouts()
        {
            return workspaceService?.ActiveProject == null
                ? Array.Empty<SpawnLayoutData>()
                : workspaceService.ActiveProject.spawnLayouts.ToList();
        }

        public IReadOnlyList<FireOriginData> GetFireOrigins()
        {
            return workspaceService?.ActiveProject == null
                ? Array.Empty<FireOriginData>()
                : workspaceService.ActiveProject.fireOrigins.ToList();
        }

        public bool CreateSpawnLayout(string name, bool isPersistent, out string spawnLayoutId)
        {
            spawnLayoutId = string.Empty;
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            var createdLayoutId = SandboxId.NewId();
            var didCreate = ExecuteProjectMutation(
                "Create Spawn Layout",
                project =>
                {
                    if (project.spawnLayouts.Any(layout =>
                            !string.IsNullOrWhiteSpace(layout.name) &&
                            string.Equals(layout.name, name, StringComparison.OrdinalIgnoreCase) &&
                            layout.isPersistent == isPersistent))
                    {
                        return false;
                    }

                    project.spawnLayouts.Add(new SpawnLayoutData
                    {
                        spawnLayoutId = createdLayoutId,
                        name = string.IsNullOrWhiteSpace(name)
                            ? (isPersistent ? "Main Preview Layout" : "Preview Temporary Layout")
                            : name.Trim(),
                        isPersistent = isPersistent
                    });
                    return true;
                },
                new[] { createdLayoutId });

            if (didCreate)
            {
                spawnLayoutId = createdLayoutId;
            }

            return didCreate;
        }

        public bool UpdateSpawnLayout(
            string spawnLayoutId,
            string name,
            bool? isPersistent,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(spawnLayoutId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Spawn Layout",
                project =>
                {
                    var layout = project.spawnLayouts.FirstOrDefault(candidate =>
                        string.Equals(candidate.spawnLayoutId, spawnLayoutId, StringComparison.Ordinal));
                    if (layout == null)
                    {
                        return false;
                    }

                    if (name != null)
                    {
                        layout.name = string.IsNullOrWhiteSpace(name) ? layout.name : name.Trim();
                    }

                    if (isPersistent.HasValue)
                    {
                        layout.isPersistent = isPersistent.Value;
                    }

                    if (metadataFields != null)
                    {
                        layout.metadataFields = CloneMetadataFields(metadataFields);
                    }

                    return true;
                },
                new[] { spawnLayoutId });
        }

        public bool PlaceSpawnPoint(
            Vector2 position,
            out string spawnPointId,
            out string resolvedSpawnLayoutId,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = true)
        {
            return PlaceSpawnPoint(position, out spawnPointId, out resolvedSpawnLayoutId, out _, spawnLayoutId, spawnLayoutName, isPersistent);
        }

        public bool PlaceSpawnPoint(
            Vector2 position,
            out string spawnPointId,
            out string resolvedSpawnLayoutId,
            out string failureMessage,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = true)
        {
            spawnPointId = string.Empty;
            resolvedSpawnLayoutId = string.Empty;
            failureMessage = string.Empty;
            if (workspaceService?.ActiveFloor == null)
            {
                failureMessage = "Create or select a floor first.";
                return false;
            }

            if (!IsValidAgentPlacement(workspaceService.ActiveFloor.floorId, position))
            {
                failureMessage = "Agents can only be placed inside enclosed rooms.";
                return false;
            }

            var createdSpawnPointId = SandboxId.NewId();
            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var capturedLayoutId = string.Empty;
            var didPlace = ExecuteProjectMutation(
                "Place Spawn Point",
                project =>
                {
                    if (!ResolveOrCreateSpawnLayout(project, spawnLayoutId, spawnLayoutName, isPersistent, out var layout))
                    {
                        return false;
                    }

                    layout.spawnPoints.Add(new SpawnPointData
                    {
                        spawnPointId = createdSpawnPointId,
                        floorId = activeFloorId,
                        position = position
                    });
                    capturedLayoutId = layout.spawnLayoutId;
                    return true;
                },
                new[] { createdSpawnPointId });

            if (didPlace)
            {
                spawnPointId = createdSpawnPointId;
                resolvedSpawnLayoutId = capturedLayoutId;
            }

            return didPlace;
        }

        public bool PlaceSpawnBrush(
            IReadOnlyList<Vector2> polygonPoints,
            out string spawnBrushStrokeId,
            out string resolvedSpawnLayoutId,
            float density = -1f,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = false)
        {
            return PlaceSpawnBrush(
                polygonPoints,
                out spawnBrushStrokeId,
                out resolvedSpawnLayoutId,
                out _,
                density,
                spawnLayoutId,
                spawnLayoutName,
                isPersistent);
        }

        public bool PlaceSpawnBrush(
            IReadOnlyList<Vector2> polygonPoints,
            out string spawnBrushStrokeId,
            out string resolvedSpawnLayoutId,
            out string failureMessage,
            float density = -1f,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = false)
        {
            spawnBrushStrokeId = string.Empty;
            resolvedSpawnLayoutId = string.Empty;
            failureMessage = string.Empty;
            if (workspaceService?.ActiveFloor == null || polygonPoints == null || polygonPoints.Count < 3)
            {
                failureMessage = "Create or select a floor first.";
                return false;
            }

            if (!IsValidAgentBrushPlacement(workspaceService.ActiveFloor.floorId, polygonPoints))
            {
                failureMessage = "Agent brushes must stay inside enclosed rooms.";
                return false;
            }

            var createdBrushId = SandboxId.NewId();
            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var resolvedDensity = density > 0f ? density : defaultSpawnBrushDensity;
            var capturedLayoutId = string.Empty;
            var didPlace = ExecuteProjectMutation(
                "Place Spawn Brush",
                project =>
                {
                    if (!ResolveOrCreateSpawnLayout(project, spawnLayoutId, spawnLayoutName, isPersistent, out var layout))
                    {
                        return false;
                    }

                    layout.spawnBrushStrokes.Add(new SpawnBrushStrokeData
                    {
                        spawnBrushStrokeId = createdBrushId,
                        floorId = activeFloorId,
                        density = Mathf.Max(0.1f, resolvedDensity),
                        polygonPoints = polygonPoints.ToList()
                    });
                    capturedLayoutId = layout.spawnLayoutId;
                    return true;
                },
                new[] { createdBrushId });

            if (didPlace)
            {
                spawnBrushStrokeId = createdBrushId;
                resolvedSpawnLayoutId = capturedLayoutId;
            }

            return didPlace;
        }

        public bool PlaceFireOrigin(
            Vector2 position,
            out string fireOriginId,
            float spreadIntensity = 1f,
            float startDelaySeconds = 0f,
            bool isPersistent = true)
        {
            fireOriginId = string.Empty;
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdFireOriginId = SandboxId.NewId();
            var didPlace = ExecuteProjectMutation(
                "Place Fire Origin",
                project =>
                {
                    project.fireOrigins.Add(new FireOriginData
                    {
                        fireOriginId = createdFireOriginId,
                        floorId = activeFloorId,
                        position = position,
                        spreadIntensity = Mathf.Max(0.1f, spreadIntensity),
                        startDelaySeconds = Mathf.Max(0f, startDelaySeconds),
                        isPersistent = isPersistent
                    });
                    return true;
                },
                new[] { createdFireOriginId });

            if (didPlace)
            {
                fireOriginId = createdFireOriginId;
            }

            return didPlace;
        }

        public bool UpdateFireOrigin(
            string fireOriginId,
            Vector2 position,
            float spreadIntensity,
            float startDelaySeconds,
            bool isPersistent)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(fireOriginId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Fire Origin",
                project =>
                {
                    var fireOrigin = project.fireOrigins.FirstOrDefault(candidate =>
                        string.Equals(candidate.fireOriginId, fireOriginId, StringComparison.Ordinal));
                    if (fireOrigin == null)
                    {
                        return false;
                    }

                    fireOrigin.position = position;
                    fireOrigin.spreadIntensity = Mathf.Max(0.1f, spreadIntensity);
                    fireOrigin.startDelaySeconds = Mathf.Max(0f, startDelaySeconds);
                    fireOrigin.isPersistent = isPersistent;
                    return true;
                },
                new[] { fireOriginId });
        }

        public bool PlaceRegion(
            Vector2 center,
            Vector2 size,
            out string regionId,
            string name = "",
            RegionSemanticType semanticType = RegionSemanticType.SpawnZone)
        {
            regionId = string.Empty;
            if (workspaceService?.ActiveFloor == null || size.x <= 0f || size.y <= 0f)
            {
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdRegionId = SandboxId.NewId();
            var half = size * 0.5f;
            var polygonPoints = new List<Vector2>
            {
                center + new Vector2(-half.x, -half.y),
                center + new Vector2(-half.x, half.y),
                center + new Vector2(half.x, half.y),
                center + new Vector2(half.x, -half.y)
            };

            var didPlace = ExecuteProjectMutation(
                "Place Region",
                project =>
                {
                    var floor = project.floors.FirstOrDefault(candidate =>
                        string.Equals(candidate.floorId, activeFloorId, StringComparison.Ordinal));
                    if (floor == null)
                    {
                        return false;
                    }

                    floor.regions.Add(new RegionData
                    {
                        regionId = createdRegionId,
                        floorId = activeFloorId,
                        name = string.IsNullOrWhiteSpace(name)
                            ? BuildDefaultRegionName(semanticType, floor.regions.Count + 1)
                            : name.Trim(),
                        semanticType = semanticType,
                        polygonPoints = polygonPoints
                    });
                    return true;
                },
                new[] { createdRegionId });

            if (didPlace)
            {
                regionId = createdRegionId;
            }

            return didPlace;
        }

        public bool UpdateRegion(
            string regionId,
            string name,
            RegionSemanticType semanticType,
            IReadOnlyList<Vector2> polygonPoints,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(regionId))
            {
                return false;
            }

            return ExecuteProjectMutation(
                "Update Region",
                project =>
                {
                    foreach (var floor in project.floors)
                    {
                        var region = floor.regions.FirstOrDefault(candidate =>
                            string.Equals(candidate.regionId, regionId, StringComparison.Ordinal));
                        if (region == null)
                        {
                            continue;
                        }

                        if (name != null)
                        {
                            region.name = string.IsNullOrWhiteSpace(name) ? region.name : name.Trim();
                        }

                        region.semanticType = semanticType;
                        if (polygonPoints != null && polygonPoints.Count >= 3)
                        {
                            region.polygonPoints = polygonPoints.ToList();
                        }

                        if (metadataFields != null)
                        {
                            region.metadataFields = CloneMetadataFields(metadataFields);
                        }

                        return true;
                    }

                    return false;
                },
                new[] { regionId });
        }

        private bool ExecuteProjectMutation(
            string description,
            Func<BuildingProjectData, bool> mutation,
            IReadOnlyList<string> nextSelection)
        {
            if (workspaceService?.ActiveProject == null || mutation == null)
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

            void Apply(BuildingProjectData project, IReadOnlyList<string> selection)
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
                previewService?.NotifyPreviewInputsChanged();
                PreviewAuthoringChanged?.Invoke();
            }

            void ApplyAfter()
            {
                Apply(SandboxProjectSerializer.Clone(afterProject), nextSelection);
            }

            void ApplyBefore()
            {
                Apply(SandboxProjectSerializer.Clone(beforeProject), beforeSelection);
            }

            if (commandHistory == null)
            {
                ApplyAfter();
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(description, ApplyAfter, ApplyBefore));
            return true;
        }

        private bool IsValidAgentPlacement(string floorId, Vector2 position)
        {
            return roomDetectionService != null && roomDetectionService.IsPointInsideCompleteRoom(floorId, position);
        }

        private bool IsValidAgentBrushPlacement(string floorId, IReadOnlyList<Vector2> polygonPoints)
        {
            return roomDetectionService != null && roomDetectionService.ArePointsInsideCompleteRooms(floorId, polygonPoints);
        }

        private static bool ResolveOrCreateSpawnLayout(
            BuildingProjectData project,
            string spawnLayoutId,
            string spawnLayoutName,
            bool isPersistent,
            out SpawnLayoutData layout)
        {
            layout = null;
            if (project == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(spawnLayoutId))
            {
                layout = project.spawnLayouts.FirstOrDefault(candidate =>
                    string.Equals(candidate.spawnLayoutId, spawnLayoutId, StringComparison.Ordinal));
                return layout != null;
            }

            var resolvedName = string.IsNullOrWhiteSpace(spawnLayoutName)
                ? (isPersistent ? "Main Preview Layout" : "Preview Temporary Layout")
                : spawnLayoutName.Trim();
            layout = project.spawnLayouts.FirstOrDefault(candidate =>
                string.Equals(candidate.name, resolvedName, StringComparison.OrdinalIgnoreCase) &&
                candidate.isPersistent == isPersistent);
            if (layout != null)
            {
                return true;
            }

            layout = new SpawnLayoutData
            {
                spawnLayoutId = SandboxId.NewId(),
                name = resolvedName,
                isPersistent = isPersistent
            };
            project.spawnLayouts.Add(layout);
            return true;
        }

        private static string BuildDefaultRegionName(RegionSemanticType semanticType, int index)
        {
            return semanticType switch
            {
                RegionSemanticType.SpawnZone => $"Spawn Zone {index}",
                RegionSemanticType.RestrictedZone => $"Restricted Zone {index}",
                RegionSemanticType.Annotation => $"Annotation {index}",
                _ => $"Region {index}"
            };
        }

        private static List<MetadataFieldData> CloneMetadataFields(IEnumerable<MetadataFieldData> metadataFields)
        {
            return metadataFields == null
                ? new List<MetadataFieldData>()
                : metadataFields
                    .Where(field => field != null)
                    .Select(field => new MetadataFieldData
                    {
                        key = string.IsNullOrWhiteSpace(field.key) ? string.Empty : field.key.Trim(),
                        value = string.IsNullOrWhiteSpace(field.value) ? string.Empty : field.value.Trim()
                    })
                    .ToList();
        }
    }
}
