using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxFireSimulationService : MonoBehaviour
    {
        [SerializeField] private float cellSize = 0.75f;
        [SerializeField] private float spreadIntervalSeconds = 0.25f;
        [SerializeField] private float baseIgnitionChance = 0.45f;
        [SerializeField] private float diagonalSpreadPenalty = 0.2f;
        [SerializeField] private float intensityDecayPerSecond = 0.03f;
        [SerializeField] private int maxIgnitionsPerStep = 96;
        [SerializeField] private List<string> activeFireOriginSelectionIds = new();
        [SerializeField] private List<SandboxFireCellData> activeFireCells = new();

        private readonly HashSet<string> occupiedCells = new(StringComparer.Ordinal);
        private readonly List<FireSeed> pendingSeeds = new();
        private readonly Dictionary<string, List<SandboxGeneratedColliderData>> wallCollidersByFloor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Rect> floorBoundsByFloor = new(StringComparer.Ordinal);

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxColliderRebuildService colliderRebuildService;
        private bool simulationActive;
        private float simulationClock;
        private float spreadAccumulator;

        public event Action<IReadOnlyList<SandboxFireCellData>> FireStateChanged;

        public bool SimulationActive => simulationActive;
        public IReadOnlyList<SandboxFireCellData> ActiveFireCells => activeFireCells;
        public IReadOnlyList<string> ActiveFireOriginSelectionIds => activeFireOriginSelectionIds;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            ResolveDependencies();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (!simulationActive)
            {
                return;
            }

            AdvanceSimulation(Time.deltaTime);
        }

        public void SetActiveFireOriginIds(IEnumerable<string> fireOriginIds)
        {
            activeFireOriginSelectionIds = fireOriginIds == null
                ? new List<string>()
                : fireOriginIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        }

        public void ClearSimulation()
        {
            simulationActive = false;
            simulationClock = 0f;
            spreadAccumulator = 0f;
            activeFireCells.Clear();
            occupiedCells.Clear();
            pendingSeeds.Clear();
            FireStateChanged?.Invoke(activeFireCells);
        }

        public void SynchronizeFromPreviewReport(SandboxPreviewReportData report)
        {
            ResolveDependencies();
            if (report == null || !report.didRun)
            {
                ClearSimulation();
                return;
            }

            SetActiveFireOriginIds(report.activeFireOriginIds);
            RestartSimulation();
        }

        public void RestartSimulation()
        {
            ResolveDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                ClearSimulation();
                return;
            }

            BuildCaches(project);
            activeFireCells.Clear();
            occupiedCells.Clear();
            pendingSeeds.Clear();

            var fireOrigins = ResolveFireOrigins(project);
            for (var i = 0; i < fireOrigins.Count; i += 1)
            {
                var origin = fireOrigins[i];
                pendingSeeds.Add(new FireSeed(
                    origin.fireOriginId,
                    origin.floorId,
                    origin.position,
                    Mathf.Max(0.1f, origin.spreadIntensity),
                    Mathf.Max(0f, origin.startDelaySeconds)));
            }

            simulationActive = pendingSeeds.Count > 0;
            simulationClock = 0f;
            spreadAccumulator = 0f;
            ActivateReadySeeds();
            FireStateChanged?.Invoke(activeFireCells);
        }

        public void AdvanceSimulation(float deltaTime)
        {
            ResolveDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                ClearSimulation();
                return;
            }

            if (!simulationActive)
            {
                return;
            }

            simulationClock += Mathf.Max(0f, deltaTime);
            spreadAccumulator += Mathf.Max(0f, deltaTime);
            ActivateReadySeeds();

            while (spreadAccumulator >= spreadIntervalSeconds)
            {
                spreadAccumulator -= spreadIntervalSeconds;
                StepSimulation(project);
                ActivateReadySeeds();
            }

            if (pendingSeeds.Count == 0 && activeFireCells.Count == 0)
            {
                simulationActive = false;
            }
        }

        private void HandlePreviewReportChanged(SandboxPreviewReportData report)
        {
            SynchronizeFromPreviewReport(report);
        }

        private void HandleCollidersRebuilt(IReadOnlyList<SandboxGeneratedColliderData> colliders, bool wasFullRebuild, string floorId)
        {
            wallCollidersByFloor.Clear();
            if (colliders == null)
            {
                return;
            }

            for (var i = 0; i < colliders.Count; i += 1)
            {
                var collider = colliders[i];
                if (!wallCollidersByFloor.TryGetValue(collider.floorId, out var floorColliders))
                {
                    floorColliders = new List<SandboxGeneratedColliderData>();
                    wallCollidersByFloor[collider.floorId] = floorColliders;
                }

                floorColliders.Add(collider);
            }
        }

        private void ActivateReadySeeds()
        {
            for (var i = pendingSeeds.Count - 1; i >= 0; i -= 1)
            {
                if (pendingSeeds[i].activateAtSeconds > simulationClock)
                {
                    continue;
                }

                var seed = pendingSeeds[i];
                pendingSeeds.RemoveAt(i);
                IgniteCell(seed, seed.position, 1f);
            }
        }

        private void StepSimulation(BuildingProjectData project)
        {
            var ignitionAttempts = 0;
            var snapshotCount = activeFireCells.Count;
            for (var i = 0; i < snapshotCount && ignitionAttempts < maxIgnitionsPerStep; i += 1)
            {
                var sourceCell = activeFireCells[i];
                sourceCell.ageSeconds += spreadIntervalSeconds;
                sourceCell.intensity = Mathf.Clamp01(sourceCell.intensity - intensityDecayPerSecond * spreadIntervalSeconds + 0.04f);

                var neighbors = GetNeighborOffsets();
                for (var neighborIndex = 0; neighborIndex < neighbors.Length && ignitionAttempts < maxIgnitionsPerStep; neighborIndex += 1)
                {
                    var offset = neighbors[neighborIndex];
                    var candidatePosition = sourceCell.position + offset * cellSize;
                    if (!IsInsideFloorEnvelope(sourceCell.floorId, candidatePosition))
                    {
                        continue;
                    }

                    if (IsOccupied(sourceCell.floorId, candidatePosition))
                    {
                        continue;
                    }

                    if (IsBlockedByWalls(sourceCell.floorId, sourceCell.position, candidatePosition, project))
                    {
                        continue;
                    }

                    var spreadFactor = offset.x != 0f && offset.y != 0f ? diagonalSpreadPenalty : 1f;
                    var ignitionChance = Mathf.Clamp01(baseIgnitionChance * sourceCell.intensity * sourceCell.sourceSpreadIntensity * spreadFactor);
                    if (UnityEngine.Random.value > ignitionChance)
                    {
                        continue;
                    }

                    IgniteCell(
                        new FireSeed(sourceCell.sourceFireOriginId, sourceCell.floorId, candidatePosition, sourceCell.sourceSpreadIntensity, 0f),
                        candidatePosition,
                        sourceCell.intensity * 0.9f);
                    ignitionAttempts += 1;
                }
            }

            if (ignitionAttempts > 0)
            {
                FireStateChanged?.Invoke(activeFireCells);
            }
        }

        private void IgniteCell(FireSeed seed, Vector2 position, float intensityOverride)
        {
            var cellId = BuildCellId(seed.floorId, position);
            if (!occupiedCells.Add(cellId))
            {
                return;
            }

            activeFireCells.Add(new SandboxFireCellData
            {
                cellId = cellId,
                floorId = seed.floorId,
                sourceFireOriginId = seed.fireOriginId,
                position = position,
                intensity = Mathf.Clamp01(intensityOverride),
                sourceSpreadIntensity = seed.spreadIntensity,
                ageSeconds = 0f
            });
        }

        private bool IsOccupied(string floorId, Vector2 position)
        {
            return occupiedCells.Contains(BuildCellId(floorId, position));
        }

        private bool IsInsideFloorEnvelope(string floorId, Vector2 position)
        {
            if (!floorBoundsByFloor.TryGetValue(floorId, out var bounds))
            {
                return true;
            }

            return bounds.Contains(position);
        }

        private bool IsBlockedByWalls(string floorId, Vector2 start, Vector2 end, BuildingProjectData project)
        {
            if (wallCollidersByFloor.TryGetValue(floorId, out var colliders))
            {
                for (var i = 0; i < colliders.Count; i += 1)
                {
                    if (SegmentIntersectsWallCollider(start, end, colliders[i]))
                    {
                        return true;
                    }
                }

                return false;
            }

            var floor = project?.floors?.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor?.wallSegments == null)
            {
                return false;
            }

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (SegmentsIntersect(start, end, wall.startPoint, wall.endPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsWallCollider(Vector2 start, Vector2 end, SandboxGeneratedColliderData colliderData)
        {
            var rotation = Quaternion.Euler(0f, 0f, colliderData.rotationDegrees);
            var localStart = Quaternion.Inverse(rotation) * new Vector3(start.x - colliderData.center.x, start.y - colliderData.center.y, 0f);
            var localEnd = Quaternion.Inverse(rotation) * new Vector3(end.x - colliderData.center.x, end.y - colliderData.center.y, 0f);
            var halfSize = colliderData.size * 0.5f;
            return SegmentIntersectsAxisAlignedBox(
                new Vector2(localStart.x, localStart.y),
                new Vector2(localEnd.x, localEnd.y),
                halfSize);
        }

        private static bool SegmentIntersectsAxisAlignedBox(Vector2 start, Vector2 end, Vector2 halfSize)
        {
            var delta = end - start;
            var min = -halfSize;
            var max = halfSize;
            float tMin = 0f;
            float tMax = 1f;

            if (!ClipAxis(start.x, delta.x, min.x, max.x, ref tMin, ref tMax))
            {
                return false;
            }

            if (!ClipAxis(start.y, delta.y, min.y, max.y, ref tMin, ref tMax))
            {
                return false;
            }

            return tMax >= tMin;
        }

        private static bool ClipAxis(float start, float delta, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(delta) < 0.0001f)
            {
                return start >= min && start <= max;
            }

            var inverseDelta = 1f / delta;
            var t1 = (min - start) * inverseDelta;
            var t2 = (max - start) * inverseDelta;
            if (t1 > t2)
            {
                var temp = t1;
                t1 = t2;
                t2 = temp;
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMax >= tMin;
        }

        private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            return LinesIntersect(a1, a2, b1, b2) ||
                   PointOnSegment(a1, b1, b2) ||
                   PointOnSegment(a2, b1, b2) ||
                   PointOnSegment(b1, a1, a2) ||
                   PointOnSegment(b2, a1, a2);
        }

        private static bool LinesIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            var d1 = Direction(a1, a2, b1);
            var d2 = Direction(a1, a2, b2);
            var d3 = Direction(b1, b2, a1);
            var d4 = Direction(b1, b2, a2);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
                   ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Direction(Vector2 a, Vector2 b, Vector2 c)
        {
            return (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
        }

        private static bool PointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            return Mathf.Abs(Direction(segmentStart, segmentEnd, point)) < 0.0001f &&
                   point.x >= Mathf.Min(segmentStart.x, segmentEnd.x) - 0.0001f &&
                   point.x <= Mathf.Max(segmentStart.x, segmentEnd.x) + 0.0001f &&
                   point.y >= Mathf.Min(segmentStart.y, segmentEnd.y) - 0.0001f &&
                   point.y <= Mathf.Max(segmentStart.y, segmentEnd.y) + 0.0001f;
        }

        private void BuildCaches(BuildingProjectData project)
        {
            floorBoundsByFloor.Clear();
            if (project?.floors == null)
            {
                return;
            }

            for (var i = 0; i < project.floors.Count; i += 1)
            {
                var floor = project.floors[i];
                floorBoundsByFloor[floor.floorId] = CalculateFloorBounds(project, floor);
            }
        }

        private Rect CalculateFloorBounds(BuildingProjectData project, FloorData floor)
        {
            var points = new List<Vector2>();
            if (floor.wallSegments != null)
            {
                for (var i = 0; i < floor.wallSegments.Count; i += 1)
                {
                    points.Add(floor.wallSegments[i].startPoint);
                    points.Add(floor.wallSegments[i].endPoint);
                }
            }

            if (project?.fireOrigins != null)
            {
                for (var i = 0; i < project.fireOrigins.Count; i += 1)
                {
                    if (string.Equals(project.fireOrigins[i].floorId, floor.floorId, StringComparison.Ordinal))
                    {
                        points.Add(project.fireOrigins[i].position);
                    }
                }
            }

            if (points.Count == 0)
            {
                return Rect.MinMaxRect(-10f, -10f, 10f, 10f);
            }

            var min = points[0];
            var max = points[0];
            for (var i = 1; i < points.Count; i += 1)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }

            var padding = Mathf.Max(1f, cellSize * 2f);
            return Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
        }

        private List<FireOriginData> ResolveFireOrigins(BuildingProjectData project)
        {
            if (project?.fireOrigins == null)
            {
                return new List<FireOriginData>();
            }

            if (activeFireOriginSelectionIds.Count > 0)
            {
                return project.fireOrigins
                    .Where(origin => activeFireOriginSelectionIds.Contains(origin.fireOriginId, StringComparer.Ordinal))
                    .ToList();
            }

            return project.fireOrigins.ToList();
        }

        private void ResolveDependencies()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            previewService ??= GetComponent<SandboxPreviewService>();
            colliderRebuildService ??= GetComponent<SandboxColliderRebuildService>();
        }

        private void Subscribe()
        {
            if (previewService != null)
            {
                previewService.PreviewReportChanged -= HandlePreviewReportChanged;
                previewService.PreviewReportChanged += HandlePreviewReportChanged;
            }

            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt -= HandleCollidersRebuilt;
                colliderRebuildService.CollidersRebuilt += HandleCollidersRebuilt;
            }
        }

        private void Unsubscribe()
        {
            if (previewService != null)
            {
                previewService.PreviewReportChanged -= HandlePreviewReportChanged;
            }

            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt -= HandleCollidersRebuilt;
            }
        }

        private string BuildCellId(string floorId, Vector2 position)
        {
            var xIndex = Mathf.RoundToInt(position.x / Mathf.Max(0.01f, cellSize));
            var yIndex = Mathf.RoundToInt(position.y / Mathf.Max(0.01f, cellSize));
            return $"{floorId}:{xIndex}:{yIndex}";
        }

        private Vector2[] GetNeighborOffsets()
        {
            return new[]
            {
                new Vector2(1f, 0f),
                new Vector2(-1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, -1f),
                new Vector2(1f, 1f).normalized,
                new Vector2(1f, -1f).normalized,
                new Vector2(-1f, 1f).normalized,
                new Vector2(-1f, -1f).normalized
            };
        }

        private readonly struct FireSeed
        {
            public FireSeed(string fireOriginId, string floorId, Vector2 position, float spreadIntensity, float activateAtSeconds)
            {
                this.fireOriginId = fireOriginId;
                this.floorId = floorId;
                this.position = position;
                this.spreadIntensity = spreadIntensity;
                this.activateAtSeconds = activateAtSeconds;
            }

            public readonly string fireOriginId;
            public readonly string floorId;
            public readonly Vector2 position;
            public readonly float spreadIntensity;
            public readonly float activateAtSeconds;
        }
    }
}