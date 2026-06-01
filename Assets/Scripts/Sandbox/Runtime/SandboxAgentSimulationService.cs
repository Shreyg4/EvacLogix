using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;
using UnityEngine.AI;

namespace EvacLogix.Sandbox.Runtime
{
    [Serializable]
    public sealed class SandboxSimulationFloorOutcomeData
    {
        public string floorId = string.Empty;
        public string floorName = string.Empty;
        public int spawnedAgents;
        public int evacuatedAgents;
        public int injuredAgents;
        public int deadAgents;
        public float averageHealth;
    }

    [Serializable]
    public sealed class SandboxSimulationSummaryReportData
    {
        public bool didRun;
        public bool completedSuccessfully;
        public string completedUtc = string.Empty;
        public int totalAgents;
        public int evacuatedAgents;
        public int injuredAgents;
        public int deadAgents;
        public float averageHealth;
        public List<SandboxSimulationFloorOutcomeData> floorOutcomes = new();
    }

    [Serializable]
    public sealed class SandboxSimulationTravelDensityCellData
    {
        public string floorId = string.Empty;
        public string floorName = string.Empty;
        public Vector2 center;
        public int sampleCount;
        public float cumulativeSeconds;
        public float intensity;
    }

    [Serializable]
    public sealed class SandboxSimulationTravelDensityReportData
    {
        public bool didRun;
        public bool completedSuccessfully;
        public string completedUtc = string.Empty;
        public float cellSize = 1f;
        public List<SandboxSimulationTravelDensityCellData> cells = new();
    }

    [Serializable]
    public sealed class SandboxSimulationRunReportData
    {
        public SandboxSimulationSummaryReportData summary = new();
        public SandboxSimulationTravelDensityReportData travelDensity = new();
    }

    public sealed class SandboxAgentSimulationService : MonoBehaviour
    {
        private sealed class AgentTerminalOutcome
        {
            public string agentId = string.Empty;
            public string spawnFloorId = string.Empty;
            public string finalFloorId = string.Empty;
            public float finalHealth;
            public bool reachedExit;
        }

        private const string NavMeshPlaneRootName = "NavMeshPlaneRoot";

        [SerializeField] private SandboxAgentProfile defaultAgentProfile;
        [SerializeField] private string agentRootName = "AgentRoot";
        [SerializeField] private bool autoStartOnPreviewRun = true;
        [SerializeField] private float heatmapCellSize = 1f;
        [SerializeField] private float congestionAvoidanceRadius = 1.5f;
        [SerializeField] private float congestionRoutePenaltyWeight = 4f;
        [SerializeField] private List<SandboxEvacueeAgent> activeAgents = new();
        [SerializeField] private SandboxSimulationRunReportData lastSimulationRunReport = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxFireSimulationService fireSimulationService;
        private SandboxRoomDetectionService roomDetectionService;
        private GameObject agentRoot;
        private Texture2D agentTexture;
        private Sprite agentSprite;
        private readonly List<NavMeshDataInstance> navMeshInstances = new();
        private readonly List<NavMeshData> navMeshDatas = new();
        private readonly List<Mesh> navMeshSourceMeshes = new();
        private readonly List<GameObject> navMeshPlaneObjects = new();
        private readonly Dictionary<string, SandboxSimulationTravelDensityCellData> travelDensityCells = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> agentSpawnFloorIds = new(StringComparer.Ordinal);
        private readonly List<AgentTerminalOutcome> terminalOutcomes = new();
        private int totalSpawnedAgents;
        private bool simulationActive;
        private float lastPreviewDigestTime;

        public event Action<IReadOnlyList<SandboxEvacueeAgent>> AgentsChanged;
        public event Action<SandboxSimulationRunReportData> SimulationRunReportChanged;

        public IReadOnlyList<SandboxEvacueeAgent> ActiveAgents => activeAgents;
        public bool SimulationActive => simulationActive;
        public float AgentRadius => GetProfile().Radius;
        public SandboxSimulationRunReportData LastSimulationRunReport => lastSimulationRunReport;
        public bool HasCompletedSimulationRunReport => lastSimulationRunReport?.summary != null && lastSimulationRunReport.summary.completedSuccessfully;

        private void Awake()
        {
            ResolveDependencies();
            EnsureDefaultProfile();
        }

        private void OnEnable()
        {
            if (previewService != null)
            {
                previewService.PreviewReportChanged += HandlePreviewReportChanged;
            }
        }

        private void Start()
        {
            ResolveDependencies();
            if (previewService != null)
            {
                previewService.PreviewReportChanged -= HandlePreviewReportChanged;
                previewService.PreviewReportChanged += HandlePreviewReportChanged;
            }
        }

        private void OnDestroy()
        {
            ClearNavMesh();
        }

        private void OnDisable()
        {
            if (previewService != null)
            {
                previewService.PreviewReportChanged -= HandlePreviewReportChanged;
            }
        }

        private void Update()
        {
            if (!simulationActive)
            {
                return;
            }

            if (previewService != null && !previewService.IsPreviewModeActive)
            {
                StopSimulation();
                return;
            }

            TickAgents(Time.deltaTime);
        }

        public void StopSimulation()
        {
            simulationActive = false;
            ClearAgents();
            ClearNavMesh();
            ClearActiveReportTracking();
            AgentsChanged?.Invoke(activeAgents);
        }

        public void HandlePreviewReportChanged(SandboxPreviewReportData report)
        {
            if (!autoStartOnPreviewRun || report == null || !report.didRun)
            {
                if (report == null || !report.didRun)
                {
                    StopSimulation();
                }

                return;
            }

            ResolveDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                StopSimulation();
                return;
            }

            var scenarioPreset = ResolveScenarioPreset(report.activeSpawnLayoutIds, report.activeFireOriginIds, project);
            var previewParameters = scenarioPreset?.previewParameters ?? new PreviewParameterData();
            StartSimulation(project, report.activeSpawnLayoutIds, report.activeFireOriginIds, previewParameters);
        }

        public void HandlePreviewReport(SandboxPreviewReportData report)
        {
            HandlePreviewReportChanged(report);
        }

        public void StartSimulation(
            BuildingProjectData project,
            IReadOnlyList<string> activeSpawnLayoutIds,
            IReadOnlyList<string> activeFireOriginIds,
            PreviewParameterData previewParameters)
        {
            ResolveDependencies();
            EnsureDefaultProfile();

            if (project == null)
            {
                StopSimulation();
                return;
            }

            var spawnLayouts = ResolveSpawnLayouts(project, activeSpawnLayoutIds);
            var fireOrigins = ResolveFireOrigins(project, activeFireOriginIds);
            var fireCells = fireSimulationService != null && fireSimulationService.SimulationActive
                ? fireSimulationService.ActiveFireCells
                : Array.Empty<SandboxFireCellData>();
            if (spawnLayouts.Count == 0)
            {
                StopSimulation();
                return;
            }

            if (!RebuildNavMesh(project, spawnLayouts))
            {
                StopSimulation();
                return;
            }

            BuildAgentRoot();
            ClearAgents();
            BeginRunReport();

            var samples = ExpandSpawnSamples(spawnLayouts);
            for (var i = 0; i < samples.Count; i += 1)
            {
                var sample = samples[i];
                if (roomDetectionService != null && !roomDetectionService.IsPointInsideCompleteRoom(sample.floorId, sample.position))
                {
                    continue;
                }

                var agentObject = new GameObject($"Agent-{sample.spawnPointId}");
                agentObject.transform.SetParent(agentRoot.transform, false);

                var spriteRenderer = agentObject.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = GetAgentSprite();

                var agent = agentObject.AddComponent<SandboxEvacueeAgent>();
                var floorElevation = ResolveFloorElevation(project, sample.floorId);
                agent.Configure(GetProfile(), sample.spawnPointId, sample.floorId, sample.position, floorElevation);
                activeAgents.Add(agent);
                agentSpawnFloorIds[agent.AgentId] = sample.floorId;
                AccumulateTravelDensity(project, agent, 0f);

                var destination = ChooseExitDestination(project, sample.floorId, sample.position, fireOrigins, fireCells, sample.spawnPointId, out var exitId);
                if (!string.IsNullOrWhiteSpace(exitId))
                {
                    agent.SetDestination(exitId, destination);
                }
            }

            totalSpawnedAgents = activeAgents.Count;
            simulationActive = activeAgents.Count > 0;
            lastPreviewDigestTime = Time.time;
            if (!simulationActive)
            {
                ClearActiveReportTracking();
            }
            else
            {
                UpdateRunReport(project, false);
            }

            AgentsChanged?.Invoke(activeAgents);
        }

        public bool ExportSimulationSummaryReport(string destinationPath)
        {
            if (!HasCompletedSimulationRunReport || string.IsNullOrWhiteSpace(destinationPath))
            {
                return false;
            }

            WriteJson(destinationPath, lastSimulationRunReport.summary);
            return true;
        }

        public bool ExportSimulationTravelDensityHeatmapReport(string destinationPath)
        {
            if (!HasCompletedSimulationRunReport || string.IsNullOrWhiteSpace(destinationPath))
            {
                return false;
            }

            WriteJson(destinationPath, lastSimulationRunReport.travelDensity);
            return true;
        }

        private void TickAgents(float deltaTime)
        {
            ResolveDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                StopSimulation();
                return;
            }

            var fireOrigins = ResolveActiveFireOrigins(project);
            var fireCells = fireSimulationService != null && fireSimulationService.SimulationActive
                ? fireSimulationService.ActiveFireCells
                : Array.Empty<SandboxFireCellData>();
            var terminalStateChanged = false;
            for (var i = activeAgents.Count - 1; i >= 0; i -= 1)
            {
                var agent = activeAgents[i];
                if (agent == null)
                {
                    activeAgents.RemoveAt(i);
                    terminalStateChanged = true;
                    continue;
                }

                if (agent.HasExited)
                {
                    RecordAndDespawnAgent(project, i, agent.Health > 0f);
                    terminalStateChanged = true;
                    continue;
                }

                var position = agent.CurrentWorldPosition;
                if (agent.NeedsRepath() || agent.IsAtDestination())
                {
                    var destination = ChooseExitDestination(project, agent.FloorId, position, fireOrigins, fireCells, agent.AgentId, out var exitId);
                    if (!string.IsNullOrWhiteSpace(exitId))
                    {
                        agent.SetDestination(exitId, destination);
                    }
                    else
                    {
                        agent.ResetRepathTimer();
                    }
                }

                var exposure = ComputeFireExposure(position, fireOrigins, fireCells, agent.FloorId);
                agent.Tick(deltaTime, exposure);
                AccumulateTravelDensity(project, agent, deltaTime);

                if (agent.Health <= 0f)
                {
                    RecordAndDespawnAgent(project, i, false);
                    terminalStateChanged = true;
                }
                else if (agent.IsAtDestination())
                {
                    agent.MarkExited();
                    RecordAndDespawnAgent(project, i, true);
                    terminalStateChanged = true;
                }
            }

            lastPreviewDigestTime += deltaTime;
            if (terminalStateChanged)
            {
                UpdateRunReport(project, activeAgents.Count == 0);
            }

            AgentsChanged?.Invoke(activeAgents);

            if (activeAgents.Count == 0)
            {
                simulationActive = false;
                if (terminalStateChanged)
                {
                    ClearActiveReportTracking();
                }
            }
        }

        private Vector2 ChooseExitDestination(
            BuildingProjectData project,
            string floorId,
            Vector2 origin,
            IReadOnlyList<FireOriginData> fireOrigins,
            IReadOnlyList<SandboxFireCellData> fireCells,
            string ignoredAgentId,
            out string exitId)
        {
            exitId = string.Empty;
            if (project == null)
            {
                return origin;
            }

            var floor = project.floors?.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null || floor.exits == null || floor.exits.Count == 0)
            {
                return origin;
            }

            var bestExit = floor.exits[0];
            var bestScore = float.MaxValue;
            for (var i = 0; i < floor.exits.Count; i += 1)
            {
                var exitZone = floor.exits[i];
                var distance = Vector2.Distance(origin, exitZone.center);
                var firePenalty = GetFirePenalty(origin, exitZone.center, floorId, fireOrigins, fireCells);
                var congestionPenalty = GetCongestionRoutePenalty(floorId, origin, exitZone.center, ignoredAgentId);
                var score = distance + firePenalty + congestionPenalty - Mathf.Max(0f, exitZone.priority * 0.25f);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestExit = exitZone;
                }
            }

            exitId = bestExit.exitZoneId;
            return bestExit.center;
        }

        private float GetFirePenalty(Vector2 origin, Vector2 exitCenter, string floorId, IReadOnlyList<FireOriginData> fireOrigins, IReadOnlyList<SandboxFireCellData> fireCells)
        {
            var midpoint = (origin + exitCenter) * 0.5f;
            var dangerRadius = GetProfile().FireDangerRadius;
            var penalty = 0f;
            if (fireCells != null && fireCells.Count > 0)
            {
                for (var i = 0; i < fireCells.Count; i += 1)
                {
                    var fireCell = fireCells[i];
                    if (!string.Equals(fireCell.floorId, floorId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var distance = Vector2.Distance(midpoint, fireCell.position);
                    if (distance >= dangerRadius)
                    {
                        continue;
                    }

                    penalty += 1f - Mathf.Clamp01(distance / dangerRadius);
                }

                return penalty;
            }

            for (var i = 0; i < fireOrigins.Count; i += 1)
            {
                var fireOrigin = fireOrigins[i];
                if (!string.Equals(fireOrigin.floorId, floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Vector2.Distance(midpoint, fireOrigin.position);
                if (distance >= dangerRadius)
                {
                    continue;
                }

                penalty += 1f - Mathf.Clamp01(distance / dangerRadius);
            }

            return penalty;
        }

        private float GetCongestionRoutePenalty(string floorId, Vector2 origin, Vector2 destination, string ignoredAgentId)
        {
            var radius = Mathf.Max(0.1f, congestionAvoidanceRadius);
            var penalty = 0f;
            for (var i = 0; i < activeAgents.Count; i += 1)
            {
                var agent = activeAgents[i];
                if (agent == null ||
                    agent.HasExited ||
                    string.Equals(agent.AgentId, ignoredAgentId, StringComparison.Ordinal) ||
                    !string.Equals(agent.FloorId, floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = DistancePointToSegment(agent.CurrentWorldPosition, origin, destination);
                if (distance < radius)
                {
                    penalty += (1f - (distance / radius)) * Mathf.Max(0f, congestionRoutePenaltyWeight);
                }
            }

            return penalty;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                return Vector2.Distance(point, start);
            }

            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private float ComputeFireExposure(Vector2 position, IReadOnlyList<FireOriginData> fireOrigins, IReadOnlyList<SandboxFireCellData> fireCells, string floorId)
        {
            var exposure = 0f;
            var avoidance = Vector2.zero;

            if (fireCells != null && fireCells.Count > 0)
            {
                for (var i = 0; i < fireCells.Count; i += 1)
                {
                    var fireCell = fireCells[i];
                    if (!string.Equals(fireCell.floorId, floorId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ApplyFireInfluence(position, fireCell.position, fireCell.intensity, fireCell.sourceSpreadIntensity, ref exposure, ref avoidance);
                }

                return exposure;
            }

            for (var i = 0; i < fireOrigins.Count; i += 1)
            {
                var fireOrigin = fireOrigins[i];
                if (!string.Equals(fireOrigin.floorId, floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                ApplyFireInfluence(position, fireOrigin.position, 1f, fireOrigin.spreadIntensity, ref exposure, ref avoidance);
            }

            return exposure;
        }

        private List<SpawnSample> ExpandSpawnSamples(IReadOnlyList<SpawnLayoutData> spawnLayouts)
        {
            var samples = new List<SpawnSample>();
            for (var layoutIndex = 0; layoutIndex < spawnLayouts.Count; layoutIndex += 1)
            {
                var layout = spawnLayouts[layoutIndex];
                if (layout == null)
                {
                    continue;
                }

                for (var i = 0; i < layout.spawnPoints.Count; i += 1)
                {
                    samples.Add(new SpawnSample(layout.spawnPoints[i].spawnPointId, layout.spawnPoints[i].floorId, layout.spawnPoints[i].position));
                }
            }

            return samples;
        }

        private void ClearAgents()
        {
            for (var i = 0; i < activeAgents.Count; i += 1)
            {
                DestroyAgentObject(activeAgents[i]);
            }

            activeAgents.Clear();
        }

        private void RecordAndDespawnAgent(BuildingProjectData project, int agentIndex, bool reachedExit)
        {
            if (agentIndex < 0 || agentIndex >= activeAgents.Count)
            {
                return;
            }

            var agent = activeAgents[agentIndex];
            if (agent == null)
            {
                activeAgents.RemoveAt(agentIndex);
                return;
            }

            terminalOutcomes.Add(new AgentTerminalOutcome
            {
                agentId = agent.AgentId,
                spawnFloorId = agentSpawnFloorIds.TryGetValue(agent.AgentId, out var spawnFloorId) ? spawnFloorId : agent.FloorId,
                finalFloorId = agent.FloorId,
                finalHealth = Mathf.Clamp01(agent.Health),
                reachedExit = reachedExit && agent.Health > 0f
            });

            AccumulateTravelDensity(project, agent, 0f);
            activeAgents.RemoveAt(agentIndex);
            DestroyAgentObject(agent);
        }

        private static void DestroyAgentObject(SandboxEvacueeAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                agent.DespawnNow();
                UnityEngine.Object.Destroy(agent.gameObject);
            }
            else
            {
                agent.DespawnNow();
                UnityEngine.Object.DestroyImmediate(agent.gameObject);
            }
        }

        private void BeginRunReport()
        {
            ClearActiveReportTracking();
            lastSimulationRunReport = new SandboxSimulationRunReportData();
        }

        private void ClearActiveReportTracking()
        {
            travelDensityCells.Clear();
            agentSpawnFloorIds.Clear();
            terminalOutcomes.Clear();
            totalSpawnedAgents = 0;
        }

        private void AccumulateTravelDensity(BuildingProjectData project, SandboxEvacueeAgent agent, float deltaTime)
        {
            if (project == null || agent == null || agent.HasExited)
            {
                return;
            }

            var cellSize = Mathf.Max(0.25f, heatmapCellSize);
            var position = agent.CurrentWorldPosition;
            var cellX = Mathf.FloorToInt(position.x / cellSize);
            var cellY = Mathf.FloorToInt(position.y / cellSize);
            var key = $"{agent.FloorId}:{cellX}:{cellY}";
            if (!travelDensityCells.TryGetValue(key, out var cell))
            {
                cell = new SandboxSimulationTravelDensityCellData
                {
                    floorId = agent.FloorId,
                    floorName = ResolveFloorName(project, agent.FloorId),
                    center = new Vector2((cellX + 0.5f) * cellSize, (cellY + 0.5f) * cellSize)
                };
                travelDensityCells[key] = cell;
            }

            cell.sampleCount += 1;
            cell.cumulativeSeconds += Mathf.Max(0f, deltaTime);
            cell.intensity = cell.sampleCount;
        }

        private void UpdateRunReport(BuildingProjectData project, bool completedSuccessfully)
        {
            var completedUtc = completedSuccessfully ? DateTime.UtcNow.ToString("O") : string.Empty;
            var summary = BuildSummaryReport(project, completedSuccessfully, completedUtc);
            var heatmap = new SandboxSimulationTravelDensityReportData
            {
                didRun = true,
                completedSuccessfully = completedSuccessfully,
                completedUtc = completedUtc,
                cellSize = Mathf.Max(0.25f, heatmapCellSize),
                cells = travelDensityCells.Values
                    .OrderBy(cell => cell.floorName, StringComparer.Ordinal)
                    .ThenByDescending(cell => cell.intensity)
                    .ThenBy(cell => cell.center.x)
                    .ThenBy(cell => cell.center.y)
                    .ToList()
            };

            lastSimulationRunReport = new SandboxSimulationRunReportData
            {
                summary = summary,
                travelDensity = heatmap
            };
            SimulationRunReportChanged?.Invoke(lastSimulationRunReport);
        }

        private SandboxSimulationSummaryReportData BuildSummaryReport(BuildingProjectData project, bool completedSuccessfully, string completedUtc)
        {
            var report = new SandboxSimulationSummaryReportData
            {
                didRun = true,
                completedSuccessfully = completedSuccessfully,
                completedUtc = completedUtc,
                totalAgents = totalSpawnedAgents,
                evacuatedAgents = terminalOutcomes.Count(outcome => outcome.reachedExit && outcome.finalHealth > 0f),
                injuredAgents = terminalOutcomes.Count(outcome => outcome.finalHealth > 0f && outcome.finalHealth < 0.999f),
                deadAgents = terminalOutcomes.Count(outcome => outcome.finalHealth <= 0f),
            };
            report.averageHealth = terminalOutcomes.Count == 0 ? 0f : terminalOutcomes.Average(outcome => outcome.finalHealth);

            var floorIds = new HashSet<string>(project?.floors?.Select(floor => floor.floorId) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var outcome in terminalOutcomes)
            {
                if (!string.IsNullOrWhiteSpace(outcome.finalFloorId))
                {
                    floorIds.Add(outcome.finalFloorId);
                }

                if (!string.IsNullOrWhiteSpace(outcome.spawnFloorId))
                {
                    floorIds.Add(outcome.spawnFloorId);
                }
            }

            report.floorOutcomes = floorIds
                .OrderBy(floorId => ResolveFloorOrder(project, floorId))
                .ThenBy(floorId => ResolveFloorName(project, floorId), StringComparer.Ordinal)
                .Select(floorId =>
                {
                    var finalOutcomes = terminalOutcomes
                        .Where(outcome => string.Equals(outcome.finalFloorId, floorId, StringComparison.Ordinal))
                        .ToList();
                    var spawnedAgents = terminalOutcomes.Count(outcome => string.Equals(outcome.spawnFloorId, floorId, StringComparison.Ordinal)) +
                                        activeAgents.Count(agent =>
                                            agent != null &&
                                            agentSpawnFloorIds.TryGetValue(agent.AgentId, out var spawnFloorId) &&
                                            string.Equals(spawnFloorId, floorId, StringComparison.Ordinal));

                    return new SandboxSimulationFloorOutcomeData
                    {
                        floorId = floorId,
                        floorName = ResolveFloorName(project, floorId),
                        spawnedAgents = spawnedAgents,
                        evacuatedAgents = finalOutcomes.Count(outcome => outcome.reachedExit && outcome.finalHealth > 0f),
                        injuredAgents = finalOutcomes.Count(outcome => outcome.finalHealth > 0f && outcome.finalHealth < 0.999f),
                        deadAgents = finalOutcomes.Count(outcome => outcome.finalHealth <= 0f),
                        averageHealth = finalOutcomes.Count == 0 ? 0f : finalOutcomes.Average(outcome => outcome.finalHealth)
                    };
                })
                .ToList();

            return report;
        }

        private static void WriteJson(string destinationPath, object payload)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(destinationPath, JsonUtility.ToJson(payload, true));
        }

        private void ClearNavMesh()
        {
            for (var i = 0; i < navMeshInstances.Count; i += 1)
            {
                navMeshInstances[i].Remove();
            }

            navMeshInstances.Clear();

            for (var i = 0; i < navMeshDatas.Count; i += 1)
            {
                if (navMeshDatas[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(navMeshDatas[i]);
                    }
                    else
                    {
                        DestroyImmediate(navMeshDatas[i]);
                    }
                }
            }

            navMeshDatas.Clear();

            for (var i = 0; i < navMeshSourceMeshes.Count; i += 1)
            {
                if (navMeshSourceMeshes[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(navMeshSourceMeshes[i]);
                    }
                    else
                    {
                        DestroyImmediate(navMeshSourceMeshes[i]);
                    }
                }
            }

            navMeshSourceMeshes.Clear();

            for (var i = 0; i < navMeshPlaneObjects.Count; i += 1)
            {
                if (navMeshPlaneObjects[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(navMeshPlaneObjects[i]);
                    }
                    else
                    {
                        DestroyImmediate(navMeshPlaneObjects[i]);
                    }
                }
            }

            navMeshPlaneObjects.Clear();
        }

        private void BuildAgentRoot()
        {
            if (agentRoot != null)
            {
                return;
            }

            agentRoot = GameObject.Find(agentRootName);
            if (agentRoot != null)
            {
                return;
            }

            agentRoot = new GameObject(agentRootName);
        }

        private void ResolveDependencies()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            previewService ??= GetComponent<SandboxPreviewService>();
            fireSimulationService ??= GetComponent<SandboxFireSimulationService>();
            roomDetectionService ??= GetComponent<SandboxRoomDetectionService>();
        }

        private bool RebuildNavMesh(BuildingProjectData project, IReadOnlyList<SpawnLayoutData> spawnLayouts)
        {
            ClearNavMesh();
            if (project == null || roomDetectionService == null || spawnLayouts == null)
            {
                return false;
            }

            var floorIds = new HashSet<string>(StringComparer.Ordinal);
            for (var layoutIndex = 0; layoutIndex < spawnLayouts.Count; layoutIndex += 1)
            {
                var layout = spawnLayouts[layoutIndex];
                if (layout?.spawnPoints == null)
                {
                    continue;
                }

                for (var pointIndex = 0; pointIndex < layout.spawnPoints.Count; pointIndex += 1)
                {
                    if (!string.IsNullOrWhiteSpace(layout.spawnPoints[pointIndex].floorId))
                    {
                        floorIds.Add(layout.spawnPoints[pointIndex].floorId);
                    }
                }
            }

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(Vector3.zero, Vector3.one);
            var initializedBounds = false;
            foreach (var floorId in floorIds)
            {
                var floor = project.floors.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
                if (floor == null)
                {
                    continue;
                }

                var rooms = roomDetectionService.GetCompleteRoomsForFloor(floorId);
                for (var roomIndex = 0; roomIndex < rooms.Count; roomIndex += 1)
                {
                    var room = rooms[roomIndex];
                    if (room?.polygonPoints == null || room.polygonPoints.Count < 3)
                    {
                        continue;
                    }

                    if (!TryBuildRoomNavMeshMesh(room.polygonPoints, out var mesh))
                    {
                        continue;
                    }

                    CreateRoomNavMeshPlane(room, floor.elevation, mesh);
                    navMeshSourceMeshes.Add(mesh);
                    var source = new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mesh,
                        transform = Matrix4x4.TRS(new Vector3(0f, floor.elevation, 0f), Quaternion.identity, Vector3.one),
                        area = 0
                    };
                    sources.Add(source);

                    var roomBounds = CalculateRoomBounds(room.polygonPoints, floor.elevation);
                    if (!initializedBounds)
                    {
                        bounds = roomBounds;
                        initializedBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(roomBounds.min);
                        bounds.Encapsulate(roomBounds.max);
                    }
                }
            }

            if (sources.Count == 0)
            {
                return false;
            }

            bounds.Expand(4f);
            var settings = NavMesh.GetSettingsByIndex(0);
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                return false;
            }

            navMeshDatas.Add(data);
            navMeshInstances.Add(NavMesh.AddNavMeshData(data));
            return true;
        }

        private void CreateRoomNavMeshPlane(SandboxDetectedRoomData room, float floorElevation, Mesh mesh)
        {
            if (room == null || mesh == null)
            {
                return;
            }

            var root = GetNavMeshPlaneRoot();
            if (root == null)
            {
                return;
            }

            var planeObject = new GameObject($"RoomNavMeshPlane_{room.roomId}");
            planeObject.transform.SetParent(root.transform, false);
            planeObject.transform.SetPositionAndRotation(
                new Vector3(0f, floorElevation, 0f),
                Quaternion.identity);
            planeObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            var meshFilter = planeObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshCollider = planeObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            meshCollider.convex = false;

            navMeshPlaneObjects.Add(planeObject);
        }

        private static Bounds CalculateRoomBounds(IReadOnlyList<Vector2> points, float floorElevation)
        {
            var min = new Vector3(float.MaxValue, floorElevation, float.MaxValue);
            var max = new Vector3(float.MinValue, floorElevation, float.MinValue);
            for (var i = 0; i < points.Count; i += 1)
            {
                var point = points[i];
                min.x = Mathf.Min(min.x, point.x);
                min.y = Mathf.Min(min.y, floorElevation);
                min.z = Mathf.Min(min.z, point.y);
                max.x = Mathf.Max(max.x, point.x);
                max.y = Mathf.Max(max.y, floorElevation);
                max.z = Mathf.Max(max.z, point.y);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        private bool TryBuildRoomNavMeshMesh(IReadOnlyList<Vector2> polygonPoints, out Mesh mesh)
        {
            mesh = null;
            if (polygonPoints == null || polygonPoints.Count < 3)
            {
                return false;
            }

            var vertices = polygonPoints.Select(point => new Vector3(point.x, 0f, point.y)).ToArray();
            var triangleIndices = TriangulatePolygon(polygonPoints);
            if (triangleIndices.Count < 3)
            {
                return false;
            }

            mesh = new Mesh
            {
                name = "SandboxRoomNavMeshSource"
            };
            mesh.vertices = vertices;
            mesh.triangles = triangleIndices.ToArray();
            mesh.RecalculateBounds();
            return true;
        }

        private GameObject GetNavMeshPlaneRoot()
        {
            var root = GameObject.Find(NavMeshPlaneRootName);
            if (root != null)
            {
                return root;
            }

            root = new GameObject(NavMeshPlaneRootName);
            return root;
        }

        private static List<int> TriangulatePolygon(IReadOnlyList<Vector2> polygonPoints)
        {
            var triangles = new List<int>();
            if (polygonPoints == null || polygonPoints.Count < 3)
            {
                return triangles;
            }

            var indices = Enumerable.Range(0, polygonPoints.Count).ToList();
            if (SignedPolygonArea(polygonPoints) < 0f)
            {
                indices.Reverse();
            }

            var guard = 0;
            while (indices.Count > 2 && guard < 10_000)
            {
                var earClipped = false;
                for (var i = 0; i < indices.Count; i += 1)
                {
                    var prev = indices[(i - 1 + indices.Count) % indices.Count];
                    var current = indices[i];
                    var next = indices[(i + 1) % indices.Count];

                    if (!IsConvex(polygonPoints[prev], polygonPoints[current], polygonPoints[next]))
                    {
                        continue;
                    }

                    if (ContainsPointInsideTriangle(polygonPoints, indices, prev, current, next))
                    {
                        continue;
                    }

                    triangles.Add(prev);
                    triangles.Add(current);
                    triangles.Add(next);
                    indices.RemoveAt(i);
                    earClipped = true;
                    break;
                }

                if (!earClipped)
                {
                    break;
                }

                guard += 1;
            }

            return triangles;
        }

        private static bool ContainsPointInsideTriangle(IReadOnlyList<Vector2> points, IReadOnlyList<int> indices, int prev, int current, int next)
        {
            var a = points[prev];
            var b = points[current];
            var c = points[next];
            for (var i = 0; i < indices.Count; i += 1)
            {
                var index = indices[i];
                if (index == prev || index == current || index == next)
                {
                    continue;
                }

                if (PointInTriangle(points[index], a, b, c))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsConvex(Vector2 previous, Vector2 current, Vector2 next)
        {
            var cross = (current.x - previous.x) * (next.y - current.y) -
                        (current.y - previous.y) * (next.x - current.x);
            return cross > 0f;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var v0 = c - a;
            var v1 = b - a;
            var v2 = point - a;

            var dot00 = Vector2.Dot(v0, v0);
            var dot01 = Vector2.Dot(v0, v1);
            var dot02 = Vector2.Dot(v0, v2);
            var dot11 = Vector2.Dot(v1, v1);
            var dot12 = Vector2.Dot(v1, v2);

            var invDenominator = 1f / Mathf.Max(0.0001f, dot00 * dot11 - dot01 * dot01);
            var u = (dot11 * dot02 - dot01 * dot12) * invDenominator;
            var v = (dot00 * dot12 - dot01 * dot02) * invDenominator;
            return u >= 0f && v >= 0f && u + v <= 1f;
        }

        private static float SignedPolygonArea(IReadOnlyList<Vector2> points)
        {
            var area = 0f;
            for (var i = 0; i < points.Count; i += 1)
            {
                var next = (i + 1) % points.Count;
                area += points[i].x * points[next].y - points[next].x * points[i].y;
            }

            return area * 0.5f;
        }

        private float ResolveFloorElevation(BuildingProjectData project, string floorId)
        {
            var floor = project?.floors?.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            return floor != null ? floor.elevation : 0f;
        }

        private static string ResolveFloorName(BuildingProjectData project, string floorId)
        {
            var floor = project?.floors?.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            if (floor == null)
            {
                return string.IsNullOrWhiteSpace(floorId) ? "Unknown Floor" : floorId;
            }

            return string.IsNullOrWhiteSpace(floor.name) ? floor.floorId : floor.name;
        }

        private static int ResolveFloorOrder(BuildingProjectData project, string floorId)
        {
            var floor = project?.floors?.FirstOrDefault(candidate => string.Equals(candidate.floorId, floorId, StringComparison.Ordinal));
            return floor?.order ?? int.MaxValue;
        }

        private void ApplyFireInfluence(Vector2 position, Vector2 firePosition, float intensity, float spreadIntensity, ref float exposure, ref Vector2 avoidance)
        {
            var offset = position - firePosition;
            var distance = offset.magnitude;
            var dangerRadius = GetProfile().FireDangerRadius;
            if (distance <= 0.0001f)
            {
                exposure += Mathf.Clamp01(intensity);
                avoidance += UnityEngine.Random.insideUnitCircle * 0.01f;
                return;
            }

            if (distance > dangerRadius)
            {
                return;
            }

            var normalizedThreat = 1f - Mathf.Clamp01(distance / dangerRadius);
            exposure += normalizedThreat * Mathf.Clamp01(intensity * spreadIntensity);
            avoidance += (offset / distance) * (normalizedThreat * GetProfile().FireAvoidanceWeight);
        }

        private List<FireOriginData> ResolveActiveFireOrigins(BuildingProjectData project)
        {
            if (project?.fireOrigins == null)
            {
                return new List<FireOriginData>();
            }

            if (fireSimulationService != null && fireSimulationService.ActiveFireOriginSelectionIds.Count > 0)
            {
                return project.fireOrigins
                    .Where(origin => fireSimulationService.ActiveFireOriginSelectionIds.Contains(origin.fireOriginId, StringComparer.Ordinal))
                    .ToList();
            }

            return project.fireOrigins.ToList();
        }

        private SandboxAgentProfile GetProfile()
        {
            return defaultAgentProfile != null ? defaultAgentProfile : EnsureDefaultProfile();
        }

        private SandboxAgentProfile EnsureDefaultProfile()
        {
            if (defaultAgentProfile != null)
            {
                return defaultAgentProfile;
            }

            defaultAgentProfile = ScriptableObject.CreateInstance<SandboxAgentProfile>();
            return defaultAgentProfile;
        }

        private static List<SpawnLayoutData> ResolveSpawnLayouts(BuildingProjectData project, IReadOnlyList<string> activeSpawnLayoutIds)
        {
            if (project?.spawnLayouts == null || project.spawnLayouts.Count == 0)
            {
                return new List<SpawnLayoutData>();
            }

            if (activeSpawnLayoutIds == null || activeSpawnLayoutIds.Count == 0)
            {
                return project.spawnLayouts.ToList();
            }

            return project.spawnLayouts
                .Where(layout => activeSpawnLayoutIds.Contains(layout.spawnLayoutId, StringComparer.Ordinal))
                .ToList();
        }

        private static List<FireOriginData> ResolveFireOrigins(BuildingProjectData project, IReadOnlyList<string> activeFireOriginIds)
        {
            if (project?.fireOrigins == null || project.fireOrigins.Count == 0)
            {
                return new List<FireOriginData>();
            }

            if (activeFireOriginIds == null || activeFireOriginIds.Count == 0)
            {
                return project.fireOrigins.ToList();
            }

            return project.fireOrigins
                .Where(origin => activeFireOriginIds.Contains(origin.fireOriginId, StringComparer.Ordinal))
                .ToList();
        }

        private ScenarioPresetData ResolveScenarioPreset(IReadOnlyList<string> activeSpawnLayoutIds, IReadOnlyList<string> activeFireOriginIds, BuildingProjectData project)
        {
            if (project?.scenarioPresets == null || project.scenarioPresets.Count == 0)
            {
                return null;
            }

            return project.scenarioPresets.FirstOrDefault(scenario =>
                scenario.spawnLayoutIds != null &&
                scenario.fireOriginIds != null &&
                scenario.spawnLayoutIds.Count == activeSpawnLayoutIds.Count &&
                scenario.fireOriginIds.Count == activeFireOriginIds.Count &&
                !scenario.spawnLayoutIds.Except(activeSpawnLayoutIds, StringComparer.Ordinal).Any() &&
                !scenario.fireOriginIds.Except(activeFireOriginIds, StringComparer.Ordinal).Any());
        }

        private Sprite GetAgentSprite()
        {
            if (agentSprite != null)
            {
                return agentSprite;
            }

            if (agentTexture == null)
            {
                agentTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                for (var y = 0; y < agentTexture.height; y += 1)
                {
                    for (var x = 0; x < agentTexture.width; x += 1)
                    {
                        agentTexture.SetPixel(x, y, Color.white);
                    }
                }

                agentTexture.Apply();
            }

            agentSprite = Sprite.Create(agentTexture, new Rect(0f, 0f, agentTexture.width, agentTexture.height), new Vector2(0.5f, 0.5f), 8f);
            return agentSprite;
        }

        private readonly struct SpawnSample
        {
            public SpawnSample(string spawnPointId, string floorId, Vector2 position)
            {
                this.spawnPointId = spawnPointId;
                this.floorId = floorId;
                this.position = position;
            }

            public readonly string spawnPointId;
            public readonly string floorId;
            public readonly Vector2 position;
        }
    }
}
