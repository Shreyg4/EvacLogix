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
        [SerializeField] private float spreadIntervalSeconds = 0.35f;
        [SerializeField] private float lateralPropagationRate = 0.16f;
        [SerializeField] private float diagonalPropagationMultiplier = 0.45f;
        [SerializeField] private float hazardRetention = 0.86f;
        [SerializeField] private float persistentSourceInjection = 0.62f;
        [SerializeField] private float normalDoorTransmission = 0.48f;
        [SerializeField] private float closedDoorTransmission = 0.18f;
        [SerializeField] private float blockedDoorTransmission = 0.08f;
        [SerializeField] private float standardWindowTransmission = 0.18f;
        [SerializeField] private float escapeWindowTransmission = 0.34f;
        [SerializeField] private float stairTransmission = 0.55f;
        [SerializeField] private float elevatorTransmission = 0.42f;
        [SerializeField] private float escalatorTransmission = 0.50f;
        [SerializeField] private float otherConnectorTransmission = 0.28f;
        [SerializeField] private float minimumTrackedHazard = 0.025f;
        [SerializeField] private float visibleFlameThreshold = 0.48f;
        [SerializeField] private float hazardCostThreshold = 0.12f;
        [SerializeField] private float hazardDamageThreshold = 0.38f;
        [SerializeField] private float hazardImpassableThreshold = 0.82f;
        [SerializeField] private float exitUnusableThreshold = 0.78f;
        [SerializeField] private float connectorUnusableThreshold = 0.82f;
        [SerializeField] private List<string> activeFireOriginSelectionIds = new();
        [SerializeField] private List<SandboxFireCellData> activeFireCells = new();

        private readonly Dictionary<string, SandboxFireCellData> activeCellsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SandboxFireCellData>> activeCellsByFloor = new(StringComparer.Ordinal);
        // Per-floor spatial index: packed (ix,iy) cell coordinate -> cell. Lets SampleHazard look up only
        // the handful of cells inside the query radius by grid coordinate instead of scanning every fire
        // cell on the floor (which made hazard sampling — called many times per agent per frame — scale
        // with the size of the fire, the root of the "lags worse as fire spreads" problem).
        private readonly Dictionary<string, Dictionary<long, SandboxFireCellData>> cellsByFloorIndex = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FloorHazardData> floorDataById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PropagationChannel>> channelsBySourceFloor = new(StringComparer.Ordinal);
        private readonly List<FireSeed> pendingSeeds = new();
        private readonly List<FireSeed> persistentSeeds = new();
        // Reused each fire step instead of allocating a fresh dictionary (cuts GC on large multi-floor fires).
        private readonly Dictionary<string, WorkingCell> stepWorkingCells = new(StringComparer.Ordinal);

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxColliderRebuildService colliderRebuildService;
        private bool simulationActive;
        private float simulationClock;
        private float spreadAccumulator;
        private int hazardRevision;

        public event Action<IReadOnlyList<SandboxFireCellData>> FireStateChanged;

        public bool SimulationActive => simulationActive;
        public IReadOnlyList<SandboxFireCellData> ActiveFireCells => activeFireCells;
        public IReadOnlyList<string> ActiveFireOriginSelectionIds => activeFireOriginSelectionIds;
        public int HazardRevision => hazardRevision;
        public float VisibleFlameThreshold => Mathf.Clamp01(visibleFlameThreshold);
        public float HazardCostThreshold => Mathf.Clamp01(hazardCostThreshold);
        public float HazardDamageThreshold => Mathf.Clamp01(hazardDamageThreshold);
        public float HazardImpassableThreshold => Mathf.Clamp01(hazardImpassableThreshold);
        public float ExitUnusableThreshold => Mathf.Clamp01(exitUnusableThreshold);
        public float ConnectorUnusableThreshold => Mathf.Clamp01(connectorUnusableThreshold);

        private static readonly Vector2[] NeighborOffsets =
        {
            new(1f, 0f),
            new(-1f, 0f),
            new(0f, 1f),
            new(0f, -1f),
            new(1f, 1f),
            new(1f, -1f),
            new(-1f, 1f),
            new(-1f, -1f),
        };

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
            hazardRevision = 0;
            activeFireCells.Clear();
            activeCellsById.Clear();
            activeCellsByFloor.Clear();
            cellsByFloorIndex.Clear();
            floorDataById.Clear();
            channelsBySourceFloor.Clear();
            pendingSeeds.Clear();
            persistentSeeds.Clear();
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

            activeFireCells.Clear();
            activeCellsById.Clear();
            activeCellsByFloor.Clear();
            cellsByFloorIndex.Clear();
            pendingSeeds.Clear();
            persistentSeeds.Clear();
            simulationClock = 0f;
            spreadAccumulator = 0f;
            hazardRevision = 0;

            BuildCaches(project);
            var fireOrigins = ResolveFireOrigins(project);
            for (var i = 0; i < fireOrigins.Count; i += 1)
            {
                var origin = fireOrigins[i];
                pendingSeeds.Add(new FireSeed(
                    origin.fireOriginId,
                    origin.floorId,
                    SnapToCellCenter(origin.position),
                    Mathf.Max(0.1f, origin.spreadIntensity),
                    Mathf.Max(0f, origin.startDelaySeconds),
                    new Vector2(Mathf.Max(0f, origin.size.x * 0.5f), Mathf.Max(0f, origin.size.y * 0.5f)),
                    origin.isPersistent));
            }

            simulationActive = pendingSeeds.Count > 0;
            ActivateReadySeeds();
            SynchronizeCollections();
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

            // The fire field only changes when a seed activates or a spread step runs — most frames neither
            // happens. Re-synchronizing the collections and firing FireStateChanged every frame was O(fire
            // cells) per frame (rebuilding the cell lists/index AND re-running every subscriber's overlay
            // Refresh), so it scaled with the fire and made big fires unplayable. We now do that work only
            // on frames where the field actually changed.
            var changed = ActivateReadySeeds();

            while (spreadAccumulator >= spreadIntervalSeconds)
            {
                spreadAccumulator -= spreadIntervalSeconds;
                StepSimulation(project);
                ActivateReadySeeds();
                changed = true;
            }

            if (pendingSeeds.Count == 0 && activeCellsById.Count == 0)
            {
                simulationActive = false;
            }

            if (changed)
            {
                FireStateChanged?.Invoke(activeFireCells);
            }
        }

        public float SampleHazard(string floorId, Vector2 localPosition, float radius = 0f)
        {
            if (string.IsNullOrWhiteSpace(floorId) ||
                !cellsByFloorIndex.TryGetValue(floorId, out var indexed) ||
                indexed.Count == 0)
            {
                return 0f;
            }

            var sampleRadius = Mathf.Max(radius, cellSize * 0.75f);
            var safeCellSize = Mathf.Max(0.01f, cellSize);
            // Only the cells whose grid coordinate falls within the radius window can contribute, so we
            // probe that small (ix,iy) box directly instead of scanning the whole floor's fire field.
            var centerX = Mathf.RoundToInt(localPosition.x / safeCellSize);
            var centerY = Mathf.RoundToInt(localPosition.y / safeCellSize);
            var cellRange = Mathf.CeilToInt(sampleRadius / safeCellSize);
            var peak = 0f;
            for (var ix = centerX - cellRange; ix <= centerX + cellRange; ix += 1)
            {
                for (var iy = centerY - cellRange; iy <= centerY + cellRange; iy += 1)
                {
                    if (!indexed.TryGetValue(PackCellIndex(ix, iy), out var cell))
                    {
                        continue;
                    }

                    var distance = Vector2.Distance(localPosition, cell.position);
                    if (distance > sampleRadius)
                    {
                        continue;
                    }

                    var falloff = 1f - (distance / sampleRadius);
                    peak = Mathf.Max(peak, Mathf.Clamp01(cell.intensity) * falloff);
                }
            }

            return Mathf.Clamp01(peak);
        }

        public bool IsHazardImpassable(string floorId, Vector2 localPosition, float radius = 0f)
        {
            return SampleHazard(floorId, localPosition, radius) >= HazardImpassableThreshold;
        }

        public bool IsVisuallyBurning(SandboxFireCellData cell)
        {
            return cell != null && Mathf.Clamp01(cell.intensity) >= VisibleFlameThreshold;
        }

        private void HandlePreviewReportChanged(SandboxPreviewReportData report)
        {
            SynchronizeFromPreviewReport(report);
        }

        private void HandleCollidersRebuilt(IReadOnlyList<SandboxGeneratedColliderData> colliders, bool wasFullRebuild, string floorId)
        {
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                return;
            }

            BuildCaches(project);
        }

        // Activates any pending seeds whose start delay has elapsed. Returns whether anything activated, so
        // callers know if the field changed. Injecting a seed already re-synchronizes the collections (via
        // ReplaceActiveField), so we no longer pay an unconditional SynchronizeCollections every call.
        private bool ActivateReadySeeds()
        {
            var activatedAny = false;
            for (var i = pendingSeeds.Count - 1; i >= 0; i -= 1)
            {
                if (pendingSeeds[i].activateAtSeconds > simulationClock)
                {
                    continue;
                }

                var seed = pendingSeeds[i];
                pendingSeeds.RemoveAt(i);
                InjectSeedIntoActiveField(seed, Mathf.Clamp01(Mathf.Max(0.35f, persistentSourceInjection * seed.spreadIntensity)));
                if (seed.isPersistent)
                {
                    persistentSeeds.Add(seed);
                }

                activatedAny = true;
            }

            return activatedAny;
        }

        private void StepSimulation(BuildingProjectData project)
        {
            var next = stepWorkingCells;
            next.Clear();
            foreach (var activeCell in activeCellsById.Values)
            {
                var retained = Mathf.Clamp01(activeCell.intensity) * Mathf.Clamp01(hazardRetention);
                AccumulateCell(next, activeCell.floorId, activeCell.position, retained, activeCell.sourceFireOriginId, activeCell.sourceSpreadIntensity, activeCell.ageSeconds + spreadIntervalSeconds);

                for (var neighborIndex = 0; neighborIndex < NeighborOffsets.Length; neighborIndex += 1)
                {
                    var offset = NeighborOffsets[neighborIndex];
                    var candidatePosition = SnapToCellCenter(activeCell.position + offset * cellSize);
                    if (!IsInsideFloorEnvelope(activeCell.floorId, candidatePosition) ||
                        IsBlockedBySolidWall(activeCell.floorId, activeCell.position, candidatePosition, project))
                    {
                        continue;
                    }

                    var diagonal = Mathf.Abs(offset.x) > 0.01f && Mathf.Abs(offset.y) > 0.01f;
                    var transfer = Mathf.Clamp01(activeCell.intensity) * lateralPropagationRate * (diagonal ? diagonalPropagationMultiplier : 1f);
                    if (transfer < minimumTrackedHazard * 0.5f)
                    {
                        continue;
                    }

                    AccumulateCell(next, activeCell.floorId, candidatePosition, transfer, activeCell.sourceFireOriginId, activeCell.sourceSpreadIntensity, 0f);
                }
            }

            ApplyPropagationChannels(next);
            ApplyPersistentSources(next);
            ReplaceActiveField(next);
            hazardRevision += 1;
        }

        private void ApplyPropagationChannels(Dictionary<string, WorkingCell> next)
        {
            foreach (var pair in channelsBySourceFloor)
            {
                if (!activeCellsByFloor.TryGetValue(pair.Key, out var floorCells) || floorCells.Count == 0)
                {
                    continue;
                }

                var channels = pair.Value;
                for (var i = 0; i < channels.Count; i += 1)
                {
                    var channel = channels[i];
                    var sourceHazard = SampleHazard(channel.sourceFloorId, channel.sourceLocalPosition, cellSize);
                    if (sourceHazard < minimumTrackedHazard)
                    {
                        continue;
                    }

                    var transfer = sourceHazard * Mathf.Clamp01(channel.transmission);
                    if (transfer < minimumTrackedHazard * 0.5f)
                    {
                        continue;
                    }

                    AccumulateCell(next, channel.targetFloorId, channel.targetLocalPosition, transfer, channel.sourceObjectId, 1f, 0f);
                }
            }
        }

        private void ApplyPersistentSources(Dictionary<string, WorkingCell> next)
        {
            for (var i = 0; i < persistentSeeds.Count; i += 1)
            {
                InjectSeed(next, persistentSeeds[i], Mathf.Clamp01(persistentSourceInjection * persistentSeeds[i].spreadIntensity), 0f);
            }
        }

        private void InjectSeedIntoActiveField(FireSeed seed, float baseIntensity)
        {
            InjectSeed(activeCellsById, seed, baseIntensity, 0f);
        }

        private void InjectSeed(Dictionary<string, WorkingCell> cells, FireSeed seed, float baseIntensity, float ageSeconds)
        {
            var radiusX = seed.radii.x;
            var radiusY = seed.radii.y;
            if (radiusX <= cellSize * 0.5f && radiusY <= cellSize * 0.5f)
            {
                AccumulateCell(cells, seed.floorId, seed.position, baseIntensity, seed.fireOriginId, seed.spreadIntensity, ageSeconds);
                return;
            }

            var stepsX = Mathf.CeilToInt(radiusX / cellSize);
            var stepsY = Mathf.CeilToInt(radiusY / cellSize);
            var safeX = Mathf.Max(0.01f, radiusX);
            var safeY = Mathf.Max(0.01f, radiusY);
            for (var ix = -stepsX; ix <= stepsX; ix += 1)
            {
                for (var iy = -stepsY; iy <= stepsY; iy += 1)
                {
                    var delta = new Vector2(ix * cellSize, iy * cellSize);
                    var normalized = new Vector2(delta.x / safeX, delta.y / safeY);
                    var distance = normalized.sqrMagnitude;
                    if (distance > 1f)
                    {
                        continue;
                    }

                    var position = SnapToCellCenter(seed.position + delta);
                    if (!IsInsideFloorEnvelope(seed.floorId, position))
                    {
                        continue;
                    }

                    var falloff = 1f - Mathf.Sqrt(distance);
                    AccumulateCell(cells, seed.floorId, position, Mathf.Clamp01(baseIntensity * Mathf.Max(0.25f, falloff)), seed.fireOriginId, seed.spreadIntensity, ageSeconds);
                }
            }
        }

        private void InjectSeed(Dictionary<string, SandboxFireCellData> cells, FireSeed seed, float baseIntensity, float ageSeconds)
        {
            var working = new Dictionary<string, WorkingCell>(StringComparer.Ordinal);
            foreach (var pair in cells)
            {
                working[pair.Key] = new WorkingCell(pair.Value.floorId, pair.Value.position, pair.Value.sourceFireOriginId, pair.Value.sourceSpreadIntensity, pair.Value.intensity, pair.Value.ageSeconds);
            }

            InjectSeed(working, seed, baseIntensity, ageSeconds);
            ReplaceActiveField(working);
        }

        private void ReplaceActiveField(Dictionary<string, WorkingCell> next)
        {
            activeCellsById.Clear();
            foreach (var pair in next)
            {
                var intensity = Mathf.Clamp01(pair.Value.intensity);
                if (intensity < minimumTrackedHazard)
                {
                    continue;
                }

                activeCellsById[pair.Key] = new SandboxFireCellData
                {
                    cellId = pair.Key,
                    floorId = pair.Value.floorId,
                    sourceFireOriginId = pair.Value.sourceFireOriginId,
                    position = pair.Value.position,
                    intensity = intensity,
                    sourceSpreadIntensity = pair.Value.sourceSpreadIntensity,
                    ageSeconds = Mathf.Max(0f, pair.Value.ageSeconds)
                };
            }

            SynchronizeCollections();
        }

        private void SynchronizeCollections()
        {
            activeFireCells.Clear();
            activeCellsByFloor.Clear();
            foreach (var indexed in cellsByFloorIndex.Values)
            {
                indexed.Clear();
            }

            foreach (var pair in activeCellsById)
            {
                var cell = pair.Value;
                activeFireCells.Add(cell);
                if (!activeCellsByFloor.TryGetValue(cell.floorId, out var floorCells))
                {
                    floorCells = new List<SandboxFireCellData>();
                    activeCellsByFloor[cell.floorId] = floorCells;
                }

                floorCells.Add(cell);

                if (!cellsByFloorIndex.TryGetValue(cell.floorId, out var indexed))
                {
                    indexed = new Dictionary<long, SandboxFireCellData>();
                    cellsByFloorIndex[cell.floorId] = indexed;
                }

                indexed[PackCellIndex(CellIndexX(cell.position), CellIndexY(cell.position))] = cell;
            }
        }

        private int CellIndexX(Vector2 position) => Mathf.RoundToInt(position.x / Mathf.Max(0.01f, cellSize));

        private int CellIndexY(Vector2 position) => Mathf.RoundToInt(position.y / Mathf.Max(0.01f, cellSize));

        private static long PackCellIndex(int ix, int iy) => ((long)ix << 32) | (uint)iy;

        private void AccumulateCell(Dictionary<string, WorkingCell> cells, string floorId, Vector2 rawPosition, float contribution, string sourceFireOriginId, float sourceSpreadIntensity, float ageSeconds)
        {
            if (string.IsNullOrWhiteSpace(floorId) || contribution <= 0f)
            {
                return;
            }

            var position = SnapToCellCenter(rawPosition);
            if (!IsInsideFloorEnvelope(floorId, position))
            {
                return;
            }

            var cellId = BuildCellId(floorId, position);
            if (!cells.TryGetValue(cellId, out var existing))
            {
                cells[cellId] = new WorkingCell(floorId, position, sourceFireOriginId, sourceSpreadIntensity, contribution, ageSeconds);
                return;
            }

            existing.intensity = Mathf.Clamp01(existing.intensity + contribution);
            existing.ageSeconds = Mathf.Max(existing.ageSeconds, ageSeconds);
            if (contribution >= existing.strongestContribution)
            {
                existing.sourceFireOriginId = sourceFireOriginId;
                existing.sourceSpreadIntensity = sourceSpreadIntensity;
                existing.strongestContribution = contribution;
            }

            cells[cellId] = existing;
        }

        private bool IsInsideFloorEnvelope(string floorId, Vector2 position)
        {
            return floorDataById.TryGetValue(floorId, out var floorData) ? floorData.envelope.Contains(position) : true;
        }

        private bool IsBlockedBySolidWall(string floorId, Vector2 start, Vector2 end, BuildingProjectData project)
        {
            if (!floorDataById.TryGetValue(floorId, out var floorData))
            {
                return false;
            }

            for (var i = 0; i < floorData.walls.Count; i += 1)
            {
                var wall = floorData.walls[i];
                if (SegmentsIntersect(start, end, wall.startPoint, wall.endPoint))
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildCaches(BuildingProjectData project)
        {
            floorDataById.Clear();
            channelsBySourceFloor.Clear();
            if (project?.floors == null)
            {
                return;
            }

            var orderedFloors = new List<FloorData>(project.floors);
            orderedFloors.Sort((a, b) => a.order.CompareTo(b.order));
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                var floor = orderedFloors[i];
                floorDataById[floor.floorId] = new FloorHazardData(floor.floorId, floor.order, CalculateStructuralBounds(floor), CalculateHazardEnvelope(floor), floor.wallSegments.ToList());
            }

            // Cross-floor spread travels ONLY through connector channels (stairs/teleports). Fire spreads
            // laterally within a floor and moves between floors at portal points — no ambient leakage
            // through the slab, so a single origin no longer bleeds onto every stacked floor.
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                BuildOpeningChannels(project, orderedFloors[i]);
                BuildConnectorChannels(project, orderedFloors[i]);
            }
        }

        private void BuildOpeningChannels(BuildingProjectData project, FloorData floor)
        {
            for (var i = 0; i < floor.doors.Count; i += 1)
            {
                var door = floor.doors[i];
                var transmission = door.state switch
                {
                    DoorState.Normal or DoorState.OneWay => normalDoorTransmission,
                    DoorState.Closed => closedDoorTransmission,
                    DoorState.Blocked or DoorState.Locked => blockedDoorTransmission,
                    _ => normalDoorTransmission,
                };

                AddWallOpeningChannel(project, floor, door.wallSegmentId, door.offsetAlongWall, transmission, $"door:{door.doorId}");
            }

            for (var i = 0; i < floor.windows.Count; i += 1)
            {
                var window = floor.windows[i];
                var transmission = window.canBeUsedForEscape ? escapeWindowTransmission : standardWindowTransmission;
                AddWallOpeningChannel(project, floor, window.wallSegmentId, window.offsetAlongWall, transmission, $"window:{window.windowId}");
            }
        }

        private void AddWallOpeningChannel(BuildingProjectData project, FloorData floor, string wallSegmentId, float offsetAlongWall, float transmission, string sourceObjectId)
        {
            if (floor == null || string.IsNullOrWhiteSpace(wallSegmentId))
            {
                return;
            }

            var wall = floor.wallSegments.Find(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return;
            }

            var direction = wall.endPoint - wall.startPoint;
            var length = direction.magnitude;
            if (length <= 0.0001f)
            {
                return;
            }

            var tangent = direction / length;
            var center = SnapToCellCenter(wall.startPoint + tangent * offsetAlongWall);
            var normal = new Vector2(-tangent.y, tangent.x);
            var sideOffset = Mathf.Max((wall.thickness * 0.6f) + (cellSize * 0.6f), cellSize * 0.75f);
            var sideA = SnapToCellCenter(center + normal * sideOffset);
            var sideB = SnapToCellCenter(center - normal * sideOffset);

            AddChannel(floor.floorId, sideA, floor.floorId, sideB, transmission, sourceObjectId);
            AddChannel(floor.floorId, sideB, floor.floorId, sideA, transmission, sourceObjectId);
        }

        private void BuildConnectorChannels(BuildingProjectData project, FloorData floor)
        {
            for (var i = 0; i < floor.stairPortals.Count; i += 1)
            {
                var stair = floor.stairPortals[i];
                if (string.IsNullOrWhiteSpace(stair.targetFloorId) || string.IsNullOrWhiteSpace(stair.targetStairPortalId))
                {
                    continue;
                }

                var targetFloor = project.floors.Find(candidate => string.Equals(candidate.floorId, stair.targetFloorId, StringComparison.Ordinal));
                var targetPortal = targetFloor?.stairPortals.Find(candidate => string.Equals(candidate.stairPortalId, stair.targetStairPortalId, StringComparison.Ordinal));
                if (targetPortal == null)
                {
                    continue;
                }

                AddChannel(floor.floorId, SnapToCellCenter(stair.localPosition), stair.targetFloorId, SnapToCellCenter(targetPortal.localPosition), stairTransmission, $"stair:{stair.stairPortalId}");
            }

            for (var i = 0; i < floor.teleportPortals.Count; i += 1)
            {
                var portal = floor.teleportPortals[i];
                if (!portal.isPairEnabled || string.IsNullOrWhiteSpace(portal.targetFloorId) || string.IsNullOrWhiteSpace(portal.targetTeleportPortalId))
                {
                    continue;
                }

                var targetFloor = project.floors.Find(candidate => string.Equals(candidate.floorId, portal.targetFloorId, StringComparison.Ordinal));
                var targetPortal = targetFloor?.teleportPortals.Find(candidate => string.Equals(candidate.teleportPortalId, portal.targetTeleportPortalId, StringComparison.Ordinal));
                if (targetPortal == null)
                {
                    continue;
                }

                AddChannel(floor.floorId, SnapToCellCenter(portal.localPosition), portal.targetFloorId, SnapToCellCenter(targetPortal.localPosition), ResolveConnectorTransmission(portal.kind), $"tele:{portal.teleportPortalId}");
            }
        }

        private float ResolveConnectorTransmission(TeleportPortalKind kind)
        {
            return kind switch
            {
                TeleportPortalKind.Stair => stairTransmission,
                TeleportPortalKind.Elevator => elevatorTransmission,
                TeleportPortalKind.Escalator => escalatorTransmission,
                _ => otherConnectorTransmission,
            };
        }

        private void AddChannel(string sourceFloorId, Vector2 sourceLocalPosition, string targetFloorId, Vector2 targetLocalPosition, float transmission, string sourceObjectId)
        {
            if (string.IsNullOrWhiteSpace(sourceFloorId) ||
                string.IsNullOrWhiteSpace(targetFloorId) ||
                !IsInsideFloorEnvelope(sourceFloorId, sourceLocalPosition) ||
                !IsInsideFloorEnvelope(targetFloorId, targetLocalPosition))
            {
                return;
            }

            if (!channelsBySourceFloor.TryGetValue(sourceFloorId, out var channels))
            {
                channels = new List<PropagationChannel>();
                channelsBySourceFloor[sourceFloorId] = channels;
            }

            channels.Add(new PropagationChannel(sourceFloorId, sourceLocalPosition, targetFloorId, targetLocalPosition, Mathf.Clamp01(transmission), sourceObjectId));
        }

        private Rect CalculateHazardEnvelope(FloorData floor)
        {
            var structural = CalculateStructuralBounds(floor);
            var padding = Mathf.Max(cellSize * 2f, 1.5f);
            return Rect.MinMaxRect(structural.xMin - padding, structural.yMin - padding, structural.xMax + padding, structural.yMax + padding);
        }

        private static Rect CalculateStructuralBounds(FloorData floor)
        {
            var hasPoint = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            void Include(Vector2 point)
            {
                hasPoint = true;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            void IncludeRect(Vector2 center, Vector2 size)
            {
                var half = size * 0.5f;
                Include(center - half);
                Include(center + half);
            }

            if (floor != null)
            {
                foreach (var wall in floor.wallSegments)
                {
                    Include(wall.startPoint);
                    Include(wall.endPoint);
                }

                foreach (var exit in floor.exits)
                {
                    IncludeRect(exit.center, exit.size);
                }

                foreach (var obstacle in floor.obstacles)
                {
                    IncludeRect(obstacle.center, obstacle.size);
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    IncludeRect(stairPortal.localPosition, stairPortal.size);
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    IncludeRect(teleportPortal.localPosition, teleportPortal.size);
                }

                foreach (var junction in floor.wallJunctions)
                {
                    Include(junction.position);
                }
            }

            if (!hasPoint)
            {
                return new Rect(-1f, -1f, 2f, 2f);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
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

        private Vector2 SnapToCellCenter(Vector2 position)
        {
            return new Vector2(
                Mathf.Round(position.x / Mathf.Max(0.01f, cellSize)) * cellSize,
                Mathf.Round(position.y / Mathf.Max(0.01f, cellSize)) * cellSize);
        }

        private string BuildCellId(string floorId, Vector2 position)
        {
            return $"{floorId}:{Mathf.RoundToInt(position.x / Mathf.Max(0.01f, cellSize))}:{Mathf.RoundToInt(position.y / Mathf.Max(0.01f, cellSize))}";
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

        private readonly struct FireSeed
        {
            public FireSeed(
                string fireOriginId,
                string floorId,
                Vector2 position,
                float spreadIntensity,
                float activateAtSeconds,
                Vector2 radii,
                bool isPersistent)
            {
                this.fireOriginId = fireOriginId;
                this.floorId = floorId;
                this.position = position;
                this.spreadIntensity = spreadIntensity;
                this.activateAtSeconds = activateAtSeconds;
                this.radii = radii;
                this.isPersistent = isPersistent;
            }

            public readonly string fireOriginId;
            public readonly string floorId;
            public readonly Vector2 position;
            public readonly float spreadIntensity;
            public readonly float activateAtSeconds;
            public readonly Vector2 radii;
            public readonly bool isPersistent;
        }

        private readonly struct FloorHazardData
        {
            public FloorHazardData(string floorId, int order, Rect structuralBounds, Rect envelope, List<WallSegmentData> walls)
            {
                this.floorId = floorId;
                this.order = order;
                this.structuralBounds = structuralBounds;
                this.envelope = envelope;
                this.walls = walls;
            }

            public readonly string floorId;
            public readonly int order;
            public readonly Rect structuralBounds;
            public readonly Rect envelope;
            public readonly List<WallSegmentData> walls;
        }

        private readonly struct PropagationChannel
        {
            public PropagationChannel(
                string sourceFloorId,
                Vector2 sourceLocalPosition,
                string targetFloorId,
                Vector2 targetLocalPosition,
                float transmission,
                string sourceObjectId)
            {
                this.sourceFloorId = sourceFloorId;
                this.sourceLocalPosition = sourceLocalPosition;
                this.targetFloorId = targetFloorId;
                this.targetLocalPosition = targetLocalPosition;
                this.transmission = transmission;
                this.sourceObjectId = sourceObjectId;
            }

            public readonly string sourceFloorId;
            public readonly Vector2 sourceLocalPosition;
            public readonly string targetFloorId;
            public readonly Vector2 targetLocalPosition;
            public readonly float transmission;
            public readonly string sourceObjectId;
        }

        private struct WorkingCell
        {
            public WorkingCell(
                string floorId,
                Vector2 position,
                string sourceFireOriginId,
                float sourceSpreadIntensity,
                float intensity,
                float ageSeconds)
            {
                this.floorId = floorId;
                this.position = position;
                this.sourceFireOriginId = sourceFireOriginId;
                this.sourceSpreadIntensity = sourceSpreadIntensity;
                this.intensity = intensity;
                this.ageSeconds = ageSeconds;
                strongestContribution = intensity;
            }

            public string floorId;
            public Vector2 position;
            public string sourceFireOriginId;
            public float sourceSpreadIntensity;
            public float intensity;
            public float ageSeconds;
            public float strongestContribution;
        }
    }
}
