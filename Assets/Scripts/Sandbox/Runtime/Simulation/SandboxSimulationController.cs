using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // Orchestrates the dedicated Simulation view: consumes the launched project, lays floors into a
    // grid, builds the carved navmesh + sim visuals, spawns agents, and runs playback with an IMGUI
    // HUD (pause/speed/restart/back), camera pan-zoom, click-to-inspect, and an end-of-run results
    // panel. Pause/speed are applied via Time.timeScale so NavMeshAgent movement scales with it.
    public sealed class SandboxSimulationController : MonoBehaviour
    {
        private enum SimState
        {
            NoProject,
            Running,
            Finished,
        }

        private static readonly float[] SpeedSteps = { 1f, 2f, 4f };
        private const float MaxSimSeconds = 600f;
        // Kept small so the baked navmesh stays connected through narrow door gaps (a default
        // width-1 door on a 0.5 grid is only ~0.5 world units wide; a larger radius erodes it shut).
        private const float NavAgentRadius = 0.12f;
        private const float ZoomSpeed = 1.4f;
        private const float MinOrthoSize = 2f;
        private const float MaxOrthoSize = 400f;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxFloorLayoutService layoutService;
        private SandboxSimulationAgentService agentService;
        private SandboxSimulationRenderer simulationRenderer;
        private SandboxFireSimulationService fireSimulationService;
        private readonly SandboxSimulationNavMeshBuilder navMeshBuilder = new();

        private Camera simCamera;
        private BuildingProjectData project;
        private SimState state = SimState.NoProject;
        private float elapsedSeconds;
        private bool paused;
        private int speedIndex;
        private SandboxEvacueeAgent selectedAgent;

        private bool hasLastPanPoint;
        private Vector2 lastPanScreenPoint;

        private GUIStyle headerStyle;
        private GUIStyle bodyStyle;
        private GUIStyle floorLabelStyle;
        private bool stylesReady;

        private void Start()
        {
            ResolveDependencies();
            EnsureCamera();
            project = SandboxSimulationLaunchContext.ConsumeProject();
            if (project == null)
            {
                state = SimState.NoProject;
                return;
            }

            BuildSimulation();
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
            navMeshBuilder.Clear();
        }

        private void BuildSimulation()
        {
            workspaceService.SetActiveProject(project);
            layoutService.Rebuild(project);
            colliderRebuildService.RebuildAll();
            navMeshBuilder.Rebuild(layoutService.Placements, project, colliderRebuildService.GeneratedColliders, NavAgentRadius);
            simulationRenderer.Build(project, layoutService, colliderRebuildService.GeneratedColliders);
            if (fireSimulationService != null)
            {
                fireSimulationService.RestartSimulation();
            }

            agentService.StartSimulation(project, layoutService, ResolveAgentCap());

            elapsedSeconds = 0f;
            paused = false;
            speedIndex = 0;
            selectedAgent = null;
            state = SimState.Running;
            ApplyTimeScale();
            FitCameraToBuilding();
        }

        private void RestartSimulation()
        {
            agentService.Stop();
            simulationRenderer.Clear();
            navMeshBuilder.Clear();
            BuildSimulation();
        }

        private void Update()
        {
            if (state == SimState.NoProject)
            {
                return;
            }

            HandleCameraInput();
            HandleAgentPick();

            if (simulationRenderer != null && fireSimulationService != null)
            {
                simulationRenderer.UpdateFire(fireSimulationService.ActiveFireCells, layoutService);
            }

            if (state != SimState.Running || paused)
            {
                return;
            }

            agentService.UpdateAgents(Time.deltaTime);
            elapsedSeconds += Time.deltaTime;

            if (agentService.AllResolved || elapsedSeconds >= MaxSimSeconds)
            {
                FinishSimulation();
            }
        }

        private void FinishSimulation()
        {
            state = SimState.Finished;
            paused = true;
            Time.timeScale = 0f;
        }

        // ---- Camera ----

        private void EnsureCamera()
        {
            simCamera = Camera.main;
            if (simCamera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                simCamera = cameraObject.AddComponent<Camera>();
                simCamera.orthographic = true;
                cameraObject.AddComponent<AudioListener>();
            }

            simCamera.orthographic = true;
        }

        private void FitCameraToBuilding()
        {
            if (simCamera == null)
            {
                return;
            }

            var bounds = layoutService.OverallWorldBounds;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            var aspect = Mathf.Max(0.1f, simCamera.aspect);
            var sizeForHeight = bounds.height * 0.5f;
            var sizeForWidth = (bounds.width * 0.5f) / aspect;
            simCamera.orthographicSize = Mathf.Clamp(Mathf.Max(sizeForHeight, sizeForWidth) * 1.1f, MinOrthoSize, MaxOrthoSize);
            simCamera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -10f);
        }

        private void HandleCameraInput()
        {
            if (simCamera == null)
            {
                return;
            }

            var scroll = SandboxInputAdapter.MouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                simCamera.orthographicSize = Mathf.Clamp(simCamera.orthographicSize - scroll * ZoomSpeed, MinOrthoSize, MaxOrthoSize);
            }

            // Pan with middle or right mouse drag.
            var panning = SandboxInputAdapter.GetMouseButton(2) || SandboxInputAdapter.GetMouseButton(1);
            if (!panning)
            {
                hasLastPanPoint = false;
                return;
            }

            var pointer = SandboxInputAdapter.PointerScreenPosition;
            if (!hasLastPanPoint)
            {
                lastPanScreenPoint = pointer;
                hasLastPanPoint = true;
                return;
            }

            var worldPerPixel = (simCamera.orthographicSize * 2f) / Mathf.Max(1, Screen.height);
            var delta = (pointer - lastPanScreenPoint) * worldPerPixel;
            simCamera.transform.position -= new Vector3(delta.x, delta.y, 0f);
            lastPanScreenPoint = pointer;
        }

        private void HandleAgentPick()
        {
            if (simCamera == null || !SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var pointer = SandboxInputAdapter.PointerScreenPosition;
            // Ignore clicks over the HUD strips.
            if (pointer.y > Screen.height - 46f || pointer.y < 92f)
            {
                return;
            }

            var worldPoint = (Vector2)simCamera.ScreenToWorldPoint(new Vector3(pointer.x, pointer.y, Mathf.Abs(simCamera.transform.position.z)));
            var bestDistance = 0.8f;
            SandboxEvacueeAgent best = null;
            var agents = agentService.Agents;
            for (var i = 0; i < agents.Count; i += 1)
            {
                if (agents[i] == null)
                {
                    continue;
                }

                var distance = Vector2.Distance(agents[i].CurrentWorldPosition, worldPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = agents[i];
                }
            }

            selectedAgent = best;
        }

        private void ApplyTimeScale()
        {
            Time.timeScale = paused ? 0f : SpeedSteps[Mathf.Clamp(speedIndex, 0, SpeedSteps.Length - 1)];
        }

        private int ResolveAgentCap()
        {
            var preset = project?.scenarioPresets != null && project.scenarioPresets.Count > 0
                ? project.scenarioPresets[0].previewParameters
                : null;
            return preset != null ? Mathf.Max(1, preset.previewAgentCap) : 250;
        }

        private void ResolveDependencies()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            layoutService = GetComponent<SandboxFloorLayoutService>();
            agentService = GetComponent<SandboxSimulationAgentService>();
            simulationRenderer = GetComponent<SandboxSimulationRenderer>();
            fireSimulationService = GetComponent<SandboxFireSimulationService>();
        }

        // ---- HUD ----

        private void OnGUI()
        {
            EnsureStyles();
            DrawTopBar();
            DrawFloorLabels();
            DrawSelectedAgent();
            if (state == SimState.NoProject)
            {
                DrawNoProject();
                return;
            }

            if (state == SimState.Finished)
            {
                DrawResultsPanel();
            }
        }

        private void DrawTopBar()
        {
            GUI.Box(new Rect(0f, 0f, Screen.width, 40f), GUIContent.none);
            GUILayout.BeginArea(new Rect(8f, 6f, Screen.width - 16f, 30f));
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Back", GUILayout.Width(70f), GUILayout.Height(26f)))
            {
                LeaveSimulation();
            }

            if (state != SimState.NoProject)
            {
                if (GUILayout.Button(paused ? "Resume" : "Pause", GUILayout.Width(80f), GUILayout.Height(26f)))
                {
                    paused = !paused;
                    if (state == SimState.Finished && !paused)
                    {
                        RestartSimulation();
                    }
                    else
                    {
                        ApplyTimeScale();
                    }
                }

                if (GUILayout.Button("Restart", GUILayout.Width(80f), GUILayout.Height(26f)))
                {
                    RestartSimulation();
                }

                if (GUILayout.Button($"{SpeedSteps[speedIndex]:0}x", GUILayout.Width(50f), GUILayout.Height(26f)))
                {
                    speedIndex = (speedIndex + 1) % SpeedSteps.Length;
                    ApplyTimeScale();
                }

                if (GUILayout.Button("Fit", GUILayout.Width(50f), GUILayout.Height(26f)))
                {
                    FitCameraToBuilding();
                }

                GUILayout.Space(16f);
                GUILayout.Label(BuildStatusText(), headerStyle, GUILayout.Height(26f));
            }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrWhiteSpace(SandboxSimulationLaunchContext.SourceLabel))
            {
                GUILayout.Label(SandboxSimulationLaunchContext.SourceLabel, bodyStyle, GUILayout.Height(26f));
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private string BuildStatusText()
        {
            var stateLabel = state == SimState.Finished ? "DONE" : (paused ? "PAUSED" : "RUNNING");
            return $"{stateLabel}   t={elapsedSeconds:0.0}s   Evacuated {agentService.EvacuatedCount}/{agentService.TotalAgents}   Casualties {agentService.CasualtyCount}   Active {agentService.ActiveCount}";
        }

        private void DrawFloorLabels()
        {
            if (simCamera == null || layoutService == null)
            {
                return;
            }

            var placements = layoutService.Placements;
            for (var i = 0; i < placements.Count; i += 1)
            {
                var placement = placements[i];
                var worldBounds = placement.WorldBounds;
                var topWorld = new Vector3(worldBounds.center.x, worldBounds.yMax, 0f);
                var screen = simCamera.WorldToScreenPoint(topWorld);
                if (screen.z <= 0f)
                {
                    continue;
                }

                var name = ResolveFloorName(placement.FloorId);
                var guiY = Screen.height - screen.y - 18f;
                GUI.Label(new Rect(screen.x - 90f, guiY, 180f, 18f), name, floorLabelStyle);
            }
        }

        private string ResolveFloorName(string floorId)
        {
            if (project?.floors != null)
            {
                for (var i = 0; i < project.floors.Count; i += 1)
                {
                    if (string.Equals(project.floors[i].floorId, floorId, System.StringComparison.Ordinal))
                    {
                        return string.IsNullOrWhiteSpace(project.floors[i].name) ? floorId : project.floors[i].name;
                    }
                }
            }

            return floorId;
        }

        private void DrawSelectedAgent()
        {
            if (selectedAgent == null)
            {
                return;
            }

            var rect = new Rect(8f, Screen.height - 84f, 240f, 76f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
            GUILayout.Label($"Agent: {selectedAgent.AgentId}", bodyStyle);
            GUILayout.Label($"Floor: {ResolveFloorName(selectedAgent.FloorId)}", bodyStyle);
            GUILayout.Label($"Target exit: {(string.IsNullOrWhiteSpace(selectedAgent.TargetExitId) ? "(none)" : selectedAgent.TargetExitId)}", bodyStyle);
            GUILayout.Label($"Health: {selectedAgent.Health:0.00}   {(selectedAgent.HasExited ? "resolved" : "active")}", bodyStyle);
            GUILayout.EndArea();
        }

        private void DrawNoProject()
        {
            var rect = new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 40f, 440f, 80f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, "No project to simulate.\nLaunch from the Main Menu or the editor's 'Simulate' button.", headerStyle);
        }

        private void DrawResultsPanel()
        {
            var rect = new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f - 150f, 480f, 300f);
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 14f, rect.width - 32f, rect.height - 28f));
            GUILayout.Label("Simulation complete", headerStyle);
            GUILayout.Space(6f);
            GUILayout.Label($"Evacuated: {agentService.EvacuatedCount} / {agentService.TotalAgents}", bodyStyle);
            GUILayout.Label($"Casualties: {agentService.CasualtyCount}", bodyStyle);
            GUILayout.Label($"Evacuation time: {elapsedSeconds:0.0}s", bodyStyle);

            GUILayout.Space(6f);
            GUILayout.Label("By floor (evacuated / casualties):", bodyStyle);
            if (project?.floors != null)
            {
                for (var i = 0; i < project.floors.Count; i += 1)
                {
                    var floor = project.floors[i];
                    agentService.EvacuatedByFloor.TryGetValue(floor.floorId, out var evac);
                    agentService.CasualtiesByFloor.TryGetValue(floor.floorId, out var dead);
                    GUILayout.Label($"  {ResolveFloorName(floor.floorId)}: {evac} / {dead}", bodyStyle);
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("Exit usage:", bodyStyle);
            foreach (var pair in agentService.ExitUsage)
            {
                GUILayout.Label($"  {pair.Key}: {pair.Value}", bodyStyle);
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart", GUILayout.Height(28f)))
            {
                RestartSimulation();
            }

            if (GUILayout.Button("Back", GUILayout.Height(28f)))
            {
                LeaveSimulation();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void LeaveSimulation()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SandboxSimulationLaunchContext.ReturnSceneName, LoadSceneMode.Single);
        }

        private void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }

            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.85f, 0.9f, 0.96f, 1f) } };
            floorLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.8f, 0.88f, 1f, 1f) }
            };
            stylesReady = true;
        }
    }
}
