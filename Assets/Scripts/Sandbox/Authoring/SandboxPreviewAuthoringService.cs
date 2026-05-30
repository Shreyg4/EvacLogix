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
        [SerializeField] private float defaultSpawnPointBrushDensity = 1f;

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

            var activeFloor = workspaceService.ActiveFloor;
            roomDetectionService?.Recalculate();
            if (!TryValidateSpawnPointPlacement(activeFloor, position, out failureMessage))
            {
                validationService?.SetPreviewPlacementValidationIssue(
                    activeFloor.floorId,
                    string.Empty,
                    "Spawn point placement failed",
                    failureMessage);
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
                validationService?.ClearPreviewPlacementValidationIssue();
                spawnPointId = createdSpawnPointId;
                resolvedSpawnLayoutId = capturedLayoutId;
            }

            return didPlace;
        }

        public bool PlaceSpawnPointBrush(
            IReadOnlyList<Vector2> polygonPoints,
            out IReadOnlyList<string> spawnPointIds,
            out string resolvedSpawnLayoutId,
            float density = -1f,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = false)
        {
            return PlaceSpawnPointBrush(
                polygonPoints,
                out spawnPointIds,
                out resolvedSpawnLayoutId,
                out _,
                density,
                spawnLayoutId,
                spawnLayoutName,
                isPersistent);
        }

        public bool PlaceSpawnPointBrush(
            IReadOnlyList<Vector2> polygonPoints,
            out IReadOnlyList<string> spawnPointIds,
            out string resolvedSpawnLayoutId,
            out string failureMessage,
            float density = -1f,
            string spawnLayoutId = null,
            string spawnLayoutName = "",
            bool isPersistent = false)
        {
            spawnPointIds = Array.Empty<string>();
            resolvedSpawnLayoutId = string.Empty;
            failureMessage = string.Empty;
            if (workspaceService?.ActiveFloor == null || polygonPoints == null || polygonPoints.Count < 3)
            {
                failureMessage = "Create or select a floor first.";
                return false;
            }

            var activeFloor = workspaceService.ActiveFloor;
            roomDetectionService?.Recalculate();
            if (!TryValidateSpawnPointBrushPlacement(activeFloor, polygonPoints, out failureMessage))
            {
                validationService?.SetPreviewPlacementValidationIssue(
                    activeFloor.floorId,
                    string.Empty,
                    "Spawn point brush placement failed",
                    failureMessage);
                return false;
            }

            var resolvedDensity = density > 0f ? density : defaultSpawnPointBrushDensity;
            var sampledPoints = GenerateSpawnPointBrushSamples(polygonPoints, resolvedDensity);
            if (sampledPoints.Count == 0)
            {
                failureMessage = "Spawn point brush did not cover a valid spawn area.";
                return false;
            }

            var activeFloorId = workspaceService.ActiveFloor.floorId;
            var createdSpawnPointIds = sampledPoints.Select(_ => SandboxId.NewId()).ToList();
            var capturedLayoutId = string.Empty;
            var didPlace = ExecuteProjectMutation(
                "Place Spawn Point Brush",
                project =>
                {
                    if (!ResolveOrCreateSpawnLayout(project, spawnLayoutId, spawnLayoutName, isPersistent, out var layout))
                    {
                        return false;
                    }

                    for (var i = 0; i < sampledPoints.Count; i += 1)
                    {
                        layout.spawnPoints.Add(new SpawnPointData
                        {
                            spawnPointId = createdSpawnPointIds[i],
                            floorId = activeFloorId,
                            position = sampledPoints[i]
                        });
                    }

                    capturedLayoutId = layout.spawnLayoutId;
                    return true;
                },
                createdSpawnPointIds);

            if (didPlace)
            {
                validationService?.ClearPreviewPlacementValidationIssue();
                spawnPointIds = createdSpawnPointIds;
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
            if (!PlaceSpawnPointBrush(
                    polygonPoints,
                    out var spawnPointIds,
                    out resolvedSpawnLayoutId,
                    out failureMessage,
                    density,
                    spawnLayoutId,
                    spawnLayoutName,
                    isPersistent))
            {
                return false;
            }

            spawnBrushStrokeId = spawnPointIds.FirstOrDefault() ?? string.Empty;
            return true;
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

        private bool TryValidateSpawnPointPlacement(FloorData floor, Vector2 position, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (!HasSpawnAccessPoints(floor))
            {
                failureMessage = "Spawn points require at least one exit or window on the floor.";
                return false;
            }

            if (roomDetectionService == null || !roomDetectionService.IsPointInsideCompleteRoom(floor.floorId, position))
            {
                failureMessage = "Spawn points must be placed inside a detected room.";
                return false;
            }

            return true;
        }

        private bool TryValidateSpawnPointBrushPlacement(FloorData floor, IReadOnlyList<Vector2> polygonPoints, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (!HasSpawnAccessPoints(floor))
            {
                failureMessage = "Spawn point brushes require at least one exit or window on the floor.";
                return false;
            }

            if (roomDetectionService == null || !roomDetectionService.ArePointsInsideCompleteRooms(floor.floorId, polygonPoints))
            {
                failureMessage = "Spawn point brushes must stay inside detected rooms.";
                return false;
            }

            return true;
        }

        private static bool HasSpawnAccessPoints(FloorData floor)
        {
            return floor != null && (floor.exits.Count > 0 || floor.windows.Count > 0);
        }

        private static List<Vector2> GenerateSpawnPointBrushSamples(IReadOnlyList<Vector2> polygonPoints, float density)
        {
            var samples = new List<Vector2>();
            if (polygonPoints == null || polygonPoints.Count < 3)
            {
                return samples;
            }

            var minX = polygonPoints.Min(point => point.x);
            var minY = polygonPoints.Min(point => point.y);
            var maxX = polygonPoints.Max(point => point.x);
            var maxY = polygonPoints.Max(point => point.y);
            var area = Mathf.Max(0.01f, PolygonArea(polygonPoints));
            var desiredCount = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(0.1f, density) * area), 1, 250);
            var spacing = Mathf.Sqrt(area / desiredCount);

            for (var y = minY; y <= maxY + spacing * 0.5f && samples.Count < desiredCount; y += spacing)
            {
                for (var x = minX; x <= maxX + spacing * 0.5f && samples.Count < desiredCount; x += spacing)
                {
                    var candidate = new Vector2(x + spacing * 0.5f, y + spacing * 0.5f);
                    if (PointInPolygon(candidate, polygonPoints))
                    {
                        samples.Add(candidate);
                    }
                }
            }

            if (samples.Count == 0)
            {
                samples.Add(CalculateCentroid(polygonPoints));
            }

            return samples;
        }

        private static float PolygonArea(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 3)
            {
                return 0f;
            }

            var area = 0f;
            for (var i = 0; i < points.Count; i += 1)
            {
                var next = (i + 1) % points.Count;
                area += points[i].x * points[next].y - points[next].x * points[i].y;
            }

            return Mathf.Abs(area) * 0.5f;
        }

        private static Vector2 CalculateCentroid(IReadOnlyList<Vector2> points)
        {
            var sum = Vector2.zero;
            for (var i = 0; i < points.Count; i += 1)
            {
                sum += points[i];
            }

            return sum / points.Count;
        }

        private static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            var isInside = false;
            for (var i = 0; i < polygon.Count; i += 1)
            {
                var j = i == 0 ? polygon.Count - 1 : i - 1;
                var left = polygon[i];
                var right = polygon[j];
                var intersects = ((left.y > point.y) != (right.y > point.y)) &&
                                 (point.x < (right.x - left.x) * (point.y - left.y) / Mathf.Max(0.0001f, right.y - left.y) + left.x);
                if (intersects)
                {
                    isInside = !isInside;
                }
            }

            return isInside;
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
