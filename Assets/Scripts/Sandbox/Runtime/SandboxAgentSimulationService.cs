using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Runtime
{
    public sealed class SandboxAgentSimulationService : MonoBehaviour
    {
        [SerializeField] private SandboxAgentProfile defaultAgentProfile;
        [SerializeField] private string agentRootName = "AgentRoot";
        [SerializeField] private bool autoStartOnPreviewRun = true;
        [SerializeField] private List<SandboxEvacueeAgent> activeAgents = new();

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxFireSimulationService fireSimulationService;
        private SandboxRoomDetectionService roomDetectionService;
        private GameObject agentRoot;
        private Texture2D agentTexture;
        private Sprite agentSprite;
        private bool simulationActive;
        private float lastPreviewDigestTime;

        public event Action<IReadOnlyList<SandboxEvacueeAgent>> AgentsChanged;

        public IReadOnlyList<SandboxEvacueeAgent> ActiveAgents => activeAgents;
        public bool SimulationActive => simulationActive;

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

            BuildAgentRoot();
            ClearAgents();

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
                agent.Configure(GetProfile(), sample.spawnPointId, sample.floorId, sample.position);
                activeAgents.Add(agent);

                var destination = ChooseExitDestination(project, sample.floorId, sample.position, fireOrigins, fireCells, out var exitId);
                if (!string.IsNullOrWhiteSpace(exitId))
                {
                    agent.SetDestination(exitId, destination);
                }
            }

            simulationActive = activeAgents.Count > 0;
            lastPreviewDigestTime = Time.time;
            AgentsChanged?.Invoke(activeAgents);
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
            for (var i = 0; i < activeAgents.Count; i += 1)
            {
                var agent = activeAgents[i];
                if (agent == null || agent.HasExited)
                {
                    continue;
                }

                var position = (Vector2)agent.transform.position;
                var destination = agent.CurrentDestination;
                if (agent.NeedsRepath() || agent.IsAtDestination())
                {
                    destination = ChooseExitDestination(project, agent.FloorId, position, fireOrigins, fireCells, out var exitId);
                    if (!string.IsNullOrWhiteSpace(exitId))
                    {
                        agent.SetDestination(exitId, destination);
                    }
                    else
                    {
                        agent.ResetRepathTimer();
                    }
                }

                var movement = ComputeMovementVector(position, destination, fireOrigins, fireCells, agent.FloorId, out var exposure);
                agent.Tick(deltaTime, movement, exposure);

                if (agent.IsAtDestination())
                {
                    agent.MarkExited();
                }
            }

            lastPreviewDigestTime += deltaTime;
            AgentsChanged?.Invoke(activeAgents);
        }

        private Vector2 ComputeMovementVector(Vector2 position, Vector2 destination, IReadOnlyList<FireOriginData> fireOrigins, IReadOnlyList<SandboxFireCellData> fireCells, string floorId, out float exposure)
        {
            var toDestination = destination - position;
            var desiredDirection = toDestination.sqrMagnitude > 0.0001f ? toDestination.normalized : Vector2.zero;
            var avoidance = Vector2.zero;
            exposure = 0f;

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

                var blendedFire = desiredDirection + avoidance;
                return blendedFire.sqrMagnitude <= 0.0001f ? desiredDirection : blendedFire.normalized;
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

            var blended = desiredDirection + avoidance;
            if (blended.sqrMagnitude <= 0.0001f)
            {
                return desiredDirection;
            }

            return blended.normalized;
        }

        private Vector2 ChooseExitDestination(
            BuildingProjectData project,
            string floorId,
            Vector2 origin,
            IReadOnlyList<FireOriginData> fireOrigins,
            IReadOnlyList<SandboxFireCellData> fireCells,
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
                var score = distance + firePenalty - Mathf.Max(0f, exitZone.priority * 0.25f);
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
                if (activeAgents[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(activeAgents[i].gameObject);
                }
                else
                {
                    DestroyImmediate(activeAgents[i].gameObject);
                }
            }

            activeAgents.Clear();
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
