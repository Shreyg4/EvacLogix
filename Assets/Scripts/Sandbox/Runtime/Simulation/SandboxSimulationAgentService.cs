using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // Spawns and drives evacuee agents for the dedicated simulation. P1 scope: spawn points (one
    // agent each) and spawn brush strokes (density fill), routed to the nearest exit ON THEIR OWN
    // FLOOR. Cross-floor routing/teleport (P2) and fire/escape-window risk (P3) are layered on later.
    // Movement is handled by each agent's NavMeshAgent; pause/speed is driven by the controller via
    // Time.timeScale, and the controller calls UpdateAgents each frame.
    public sealed class SandboxSimulationAgentService : MonoBehaviour
    {
        [SerializeField] private SandboxAgentProfile profile;
        [SerializeField] private string agentRootName = "SimAgentRoot";

        private readonly List<SandboxEvacueeAgent> agents = new();
        private readonly HashSet<string> resolvedAgentIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> exitUsage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> evacuatedByFloor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> casualtiesByFloor = new(StringComparer.Ordinal);
        private readonly SandboxBuildingRouteGraph routeGraph = new();
        private readonly Dictionary<string, SandboxBuildingRouteGraph.RouteNode> agentTargets = new(StringComparer.Ordinal);

        // After teleporting onto a portal endpoint, that endpoint is blocked for the agent (it won't
        // route through it again) until the agent moves beyond the portal footprint + this margin.
        // This makes re-using a portal require physically leaving its space and coming back, so two
        // linked portals can never thrash an agent back and forth in place.
        private readonly Dictionary<string, string> blockedPortalByAgent = new(StringComparer.Ordinal);
        private const float PortalClearMargin = 0.75f;
        private const float ImpassableThreshold = 0.99f;
        private const float FireRoutePenaltyWeight = 10f;
        private const float CongestionAvoidanceRadius = 1.5f;
        private const float CongestionRoutePenaltyWeight = 4f;

        private BuildingProjectData project;
        private SandboxFloorLayoutService layoutService;
        private SandboxFireSimulationService fireSimulationService;
        private readonly Dictionary<string, FloorData> floorsById = new(StringComparer.Ordinal);
        private GameObject agentRoot;
        private Sprite agentSprite;
        private Texture2D agentTexture;

        public IReadOnlyList<SandboxEvacueeAgent> Agents => agents;
        public int TotalAgents { get; private set; }
        public int EvacuatedCount { get; private set; }
        public int CasualtyCount { get; private set; }
        public int ActiveCount => TotalAgents - EvacuatedCount - CasualtyCount;
        public bool AllResolved => TotalAgents > 0 && ActiveCount <= 0;
        public IReadOnlyDictionary<string, int> ExitUsage => exitUsage;
        public IReadOnlyDictionary<string, int> EvacuatedByFloor => evacuatedByFloor;
        public IReadOnlyDictionary<string, int> CasualtiesByFloor => casualtiesByFloor;

        public void StartSimulation(BuildingProjectData buildingProject, SandboxFloorLayoutService floorLayout, int agentCap)
        {
            Stop();
            project = buildingProject;
            layoutService = floorLayout;
            if (project?.spawnLayouts == null || layoutService == null)
            {
                return;
            }

            routeGraph.Build(project);
            fireSimulationService = GetComponent<SandboxFireSimulationService>();
            floorsById.Clear();
            foreach (var floor in project.floors)
            {
                floorsById[floor.floorId] = floor;
            }

            EnsureAgentRoot();
            var cap = Mathf.Max(1, agentCap);

            foreach (var layout in project.spawnLayouts)
            {
                if (layout == null)
                {
                    continue;
                }

                foreach (var spawnPoint in layout.spawnPoints)
                {
                    if (agents.Count >= cap)
                    {
                        break;
                    }

                    TrySpawnAgent(spawnPoint.floorId, spawnPoint.position);
                }

                foreach (var stroke in layout.spawnBrushStrokes)
                {
                    if (agents.Count >= cap)
                    {
                        break;
                    }

                    SpawnBrushStroke(stroke, cap);
                }
            }

            TotalAgents = agents.Count;
        }

        // Called each frame by the controller (deltaTime already scaled by Time.timeScale). Movement
        // is the NavMeshAgent's job; here we handle repath, exit arrival, and casualty resolution.
        public void UpdateAgents(float deltaTime)
        {
            for (var i = agents.Count - 1; i >= 0; i -= 1)
            {
                var agent = agents[i];
                if (agent == null || resolvedAgentIds.Contains(agent.AgentId))
                {
                    if (agent == null)
                    {
                        agents.RemoveAt(i);
                    }

                    continue;
                }

                ReleasePortalBlockIfCleared(agent);

                if (agent.NeedsRepath())
                {
                    RouteAgent(agent);
                }

                ApplySoftObstacleSpeed(agent);
                agent.Tick(deltaTime, ComputeFireExposure(agent));

                if (agent.Health <= 0f)
                {
                    ResolveCasualty(agent);
                    DespawnAgentAt(i);
                    continue;
                }

                if (agent.IsAtDestination() && agentTargets.TryGetValue(agent.AgentId, out var target))
                {
                    if (target.isExit)
                    {
                        ResolveEvacuated(agent, target.objectId);
                        DespawnAgentAt(i);
                    }
                    else if (target.isEscapeWindow)
                    {
                        ResolveWindowEscape(agent, target);
                        if (resolvedAgentIds.Contains(agent.AgentId))
                        {
                            DespawnAgentAt(i);
                        }
                    }
                    else
                    {
                        TeleportThroughPortal(agent, target);
                    }
                }
            }
        }

        public void Stop()
        {
            for (var i = 0; i < agents.Count; i += 1)
            {
                DestroyAgentObject(agents[i]);
            }

            agents.Clear();
            resolvedAgentIds.Clear();
            agentTargets.Clear();
            blockedPortalByAgent.Clear();
            exitUsage.Clear();
            evacuatedByFloor.Clear();
            casualtiesByFloor.Clear();
            TotalAgents = 0;
            EvacuatedCount = 0;
            CasualtyCount = 0;
        }

        private void TrySpawnAgent(string floorId, Vector2 localPosition)
        {
            if (string.IsNullOrWhiteSpace(floorId) || !layoutService.TryGetPlacement(floorId, out var placement))
            {
                return;
            }

            var worldPosition = placement.ToWorld(localPosition);
            var agentObject = new GameObject($"SimAgent-{agents.Count:D4}");
            agentObject.transform.SetParent(agentRoot.transform, false);

            var spriteRenderer = agentObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetAgentSprite();
            spriteRenderer.sortingOrder = 100;

            var agent = agentObject.AddComponent<SandboxEvacueeAgent>();
            agent.Configure(GetProfile(), $"sim-agent-{agents.Count:D4}", floorId, worldPosition, 0f);
            agents.Add(agent);
            RouteAgent(agent);
        }

        private void SpawnBrushStroke(SpawnBrushStrokeData stroke, int cap)
        {
            if (stroke?.polygonPoints == null || stroke.polygonPoints.Count < 3 || string.IsNullOrWhiteSpace(stroke.floorId))
            {
                return;
            }

            var density = Mathf.Max(0.05f, stroke.density);
            var spacing = Mathf.Max(0.5f, 1f / Mathf.Sqrt(density));

            var min = stroke.polygonPoints[0];
            var max = stroke.polygonPoints[0];
            for (var i = 1; i < stroke.polygonPoints.Count; i += 1)
            {
                min = Vector2.Min(min, stroke.polygonPoints[i]);
                max = Vector2.Max(max, stroke.polygonPoints[i]);
            }

            for (var y = min.y; y <= max.y; y += spacing)
            {
                for (var x = min.x; x <= max.x; x += spacing)
                {
                    if (agents.Count >= cap)
                    {
                        return;
                    }

                    var point = new Vector2(x, y);
                    if (IsPointInsidePolygon(stroke.polygonPoints, point))
                    {
                        TrySpawnAgent(stroke.floorId, point);
                    }
                }
            }
        }

        // Heads the agent toward whichever on-floor node (exit or portal) minimizes
        // walk-to-node + node.CostToSafe across the whole building. excludeNodeId skips the portal an
        // agent just teleported onto, so it walks onward rather than immediately teleporting back.
        private void RouteAgent(SandboxEvacueeAgent agent)
        {
            if (!layoutService.TryGetPlacement(agent.FloorId, out var placement))
            {
                agent.ResetRepathTimer();
                return;
            }

            blockedPortalByAgent.TryGetValue(agent.AgentId, out var blockedNodeId);

            var floorNodes = routeGraph.GetFloorNodes(agent.FloorId);
            var position = agent.CurrentWorldPosition;
            SandboxBuildingRouteGraph.RouteNode best = null;
            var bestWorld = position;
            var bestCost = float.MaxValue;
            for (var i = 0; i < floorNodes.Count; i += 1)
            {
                var node = floorNodes[i];
                if (float.IsInfinity(node.costToSafe) ||
                    (blockedNodeId != null && string.Equals(node.nodeId, blockedNodeId, StringComparison.Ordinal)))
                {
                    continue;
                }

                var world = placement.ToWorld(node.localPosition);
                var total = Vector2.Distance(position, world) +
                            node.costToSafe +
                            GetFireRoutePenalty(agent.FloorId, placement, position, world) +
                            GetCongestionRoutePenalty(agent, position, world);
                if (total < bestCost)
                {
                    bestCost = total;
                    best = node;
                    bestWorld = world;
                }
            }

            if (best != null)
            {
                agentTargets[agent.AgentId] = best;
                agent.SetDestination(best.objectId, bestWorld);
                return;
            }

            // Only the blocked portal can lead to safety: the agent must shuttle back through it.
            // Tell it to step clear of the footprint first; ReleasePortalBlockIfCleared then lifts the
            // block and the next repath re-targets the portal for the return trip ("leave and come
            // back"), so it can never re-teleport without physically exiting the portal space.
            if (!string.IsNullOrWhiteSpace(blockedNodeId) &&
                routeGraph.TryGetNode(blockedNodeId, out var blockedNode) &&
                !float.IsInfinity(blockedNode.costToSafe))
            {
                agentTargets.Remove(agent.AgentId);
                agent.SetDestination(string.Empty, ResolveStepOutPoint(placement, blockedNode));
                return;
            }

            // The agent's own floor offers no reachable escape, but escape is defined project-wide:
            // head toward the nearest exit/window anywhere in the project. (It may not be reachable
            // without a portal, but the simulation still works whenever the PROJECT has an exit
            // rather than requiring one on every floor.)
            var sinks = routeGraph.Sinks;
            SandboxBuildingRouteGraph.RouteNode bestSink = null;
            var bestSinkWorld = position;
            var bestSinkDistance = float.MaxValue;
            for (var i = 0; i < sinks.Count; i += 1)
            {
                if (!layoutService.TryGetPlacement(sinks[i].floorId, out var sinkPlacement))
                {
                    continue;
                }

                var world = sinkPlacement.ToWorld(sinks[i].localPosition);
                var distance = Vector2.Distance(position, world) + GetCongestionRoutePenalty(agent, position, world);
                if (distance < bestSinkDistance)
                {
                    bestSinkDistance = distance;
                    bestSink = sinks[i];
                    bestSinkWorld = world;
                }
            }

            if (bestSink != null)
            {
                agentTargets[agent.AgentId] = bestSink;
                agent.SetDestination(bestSink.objectId, bestSinkWorld);
                return;
            }

            agentTargets.Remove(agent.AgentId);
            agent.ResetRepathTimer();
        }

        private void TeleportThroughPortal(SandboxEvacueeAgent agent, SandboxBuildingRouteGraph.RouteNode portalNode)
        {
            if (string.IsNullOrWhiteSpace(portalNode.linkedNodeId) ||
                !routeGraph.TryGetNode(portalNode.linkedNodeId, out var linked) ||
                !layoutService.TryGetPlacement(linked.floorId, out var placement))
            {
                agent.ResetRepathTimer();
                return;
            }

            agent.Relocate(linked.floorId, placement.ToWorld(linked.localPosition));
            // Block the endpoint we just arrived on until the agent leaves its footprint.
            blockedPortalByAgent[agent.AgentId] = linked.nodeId;
            RouteAgent(agent);
        }

        // Releases an agent's portal block once it has moved beyond the portal footprint + margin, so
        // re-using that portal requires genuinely leaving and re-entering its space.
        private void ReleasePortalBlockIfCleared(SandboxEvacueeAgent agent)
        {
            if (!blockedPortalByAgent.TryGetValue(agent.AgentId, out var nodeId))
            {
                return;
            }

            if (!routeGraph.TryGetNode(nodeId, out var node) || !layoutService.TryGetPlacement(node.floorId, out var placement))
            {
                blockedPortalByAgent.Remove(agent.AgentId);
                return;
            }

            var center = placement.ToWorld(node.localPosition);
            if (Vector2.Distance(agent.CurrentWorldPosition, center) > node.footprintRadius + PortalClearMargin)
            {
                blockedPortalByAgent.Remove(agent.AgentId);
            }
        }

        // A point just outside a portal's footprint, nudged toward the floor interior, used to walk an
        // agent clear of a portal it must otherwise re-enter.
        private static Vector2 ResolveStepOutPoint(SandboxFloorPlacement placement, SandboxBuildingRouteGraph.RouteNode node)
        {
            var center = placement.ToWorld(node.localPosition);
            var direction = placement.ToWorld(placement.LocalBounds.center) - center;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector2.right;
            }

            return center + direction.normalized * (node.footprintRadius + PortalClearMargin + 0.75f);
        }

        private void ResolveEvacuated(SandboxEvacueeAgent agent, string exitId)
        {
            if (!resolvedAgentIds.Add(agent.AgentId))
            {
                return;
            }

            EvacuatedCount += 1;
            Increment(exitUsage, exitId);
            Increment(evacuatedByFloor, agent.FloorId);
            agentTargets.Remove(agent.AgentId);
            blockedPortalByAgent.Remove(agent.AgentId);
            agent.MarkExited();
        }

        private void ResolveCasualty(SandboxEvacueeAgent agent)
        {
            if (!resolvedAgentIds.Add(agent.AgentId))
            {
                return;
            }

            CasualtyCount += 1;
            Increment(casualtiesByFloor, agent.FloorId);
            agentTargets.Remove(agent.AgentId);
            blockedPortalByAgent.Remove(agent.AgentId);
            agent.MarkExited();
        }

        private void DespawnAgentAt(int agentIndex)
        {
            if (agentIndex < 0 || agentIndex >= agents.Count)
            {
                return;
            }

            var agent = agents[agentIndex];
            agents.RemoveAt(agentIndex);
            DestroyAgentObject(agent);
        }

        private static void DestroyAgentObject(SandboxEvacueeAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            agent.DespawnNow();
            if (Application.isPlaying)
            {
                Destroy(agent.gameObject);
            }
            else
            {
                DestroyImmediate(agent.gameObject);
            }
        }

        // Escaping through a window applies the fall injury; fatal heights (or injury that drops the
        // agent) are casualties, otherwise the agent evacuates (counted under the window's id).
        private void ResolveWindowEscape(SandboxEvacueeAgent agent, SandboxBuildingRouteGraph.RouteNode node)
        {
            agent.ApplyInjury(node.windowInjury);
            if (node.windowFatal || agent.Health <= 0f)
            {
                ResolveCasualty(agent);
            }
            else
            {
                ResolveEvacuated(agent, node.objectId);
            }
        }

        private float ComputeFireExposure(SandboxEvacueeAgent agent)
        {
            if (fireSimulationService == null || !fireSimulationService.SimulationActive ||
                !layoutService.TryGetPlacement(agent.FloorId, out var placement))
            {
                return 0f;
            }

            var cells = fireSimulationService.ActiveFireCells;
            var position = agent.CurrentWorldPosition;
            var radius = Mathf.Max(0.1f, GetProfile().FireDangerRadius);
            var exposure = 0f;
            for (var i = 0; i < cells.Count; i += 1)
            {
                var cell = cells[i];
                if (!string.Equals(cell.floorId, agent.FloorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Vector2.Distance(position, placement.ToWorld(cell.position));
                if (distance < radius)
                {
                    exposure += (1f - (distance / radius)) * Mathf.Clamp01(cell.intensity);
                }
            }

            return exposure;
        }

        // Routing deterrent: how much fire sits near the straight path to a candidate node, so agents
        // re-route toward exits away from the fire as it spreads (recomputed each repath).
        private float GetFireRoutePenalty(string floorId, SandboxFloorPlacement placement, Vector2 from, Vector2 to)
        {
            if (fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return 0f;
            }

            var cells = fireSimulationService.ActiveFireCells;
            var midpoint = (from + to) * 0.5f;
            var radius = Mathf.Max(0.1f, GetProfile().FireDangerRadius);
            var penalty = 0f;
            for (var i = 0; i < cells.Count; i += 1)
            {
                var cell = cells[i];
                if (!string.Equals(cell.floorId, floorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = Vector2.Distance(midpoint, placement.ToWorld(cell.position));
                if (distance < radius)
                {
                    penalty += (1f - (distance / radius)) * Mathf.Clamp01(cell.intensity) * FireRoutePenaltyWeight;
                }
            }

            return penalty;
        }

        private float GetCongestionRoutePenalty(SandboxEvacueeAgent routedAgent, Vector2 from, Vector2 to)
        {
            if (routedAgent == null)
            {
                return 0f;
            }

            var radius = Mathf.Max(0.1f, CongestionAvoidanceRadius);
            var penalty = 0f;
            for (var i = 0; i < agents.Count; i += 1)
            {
                var other = agents[i];
                if (other == null ||
                    other.HasExited ||
                    resolvedAgentIds.Contains(other.AgentId) ||
                    string.Equals(other.AgentId, routedAgent.AgentId, StringComparison.Ordinal) ||
                    !string.Equals(other.FloorId, routedAgent.FloorId, StringComparison.Ordinal))
                {
                    continue;
                }

                var distance = DistancePointToSegment(other.CurrentWorldPosition, from, to);
                if (distance < radius)
                {
                    penalty += (1f - (distance / radius)) * CongestionRoutePenaltyWeight;
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

        // Slows an agent while it stands on a soft (non-impassable) obstacle, per its speed penalty.
        private void ApplySoftObstacleSpeed(SandboxEvacueeAgent agent)
        {
            var multiplier = 1f;
            if (floorsById.TryGetValue(agent.FloorId, out var floor) && layoutService.TryGetPlacement(agent.FloorId, out var placement))
            {
                var local = agent.CurrentWorldPosition - placement.OriginOffset;
                foreach (var obstacle in floor.obstacles)
                {
                    if (obstacle.discourageWeight <= 0f || obstacle.discourageWeight >= ImpassableThreshold || obstacle.movementSpeedPenalty <= 0f)
                    {
                        continue;
                    }

                    if (IsPointInsideRotatedRect(local, obstacle.center, obstacle.size, obstacle.rotationDegrees))
                    {
                        multiplier = Mathf.Min(multiplier, 1f - Mathf.Clamp01(obstacle.movementSpeedPenalty));
                    }
                }
            }

            agent.SetSpeedMultiplier(multiplier);
        }

        private static bool IsPointInsideRotatedRect(Vector2 worldPoint, Vector2 center, Vector2 size, float rotationDegrees)
        {
            var radians = -rotationDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            var delta = worldPoint - center;
            var localX = (delta.x * cos) - (delta.y * sin);
            var localY = (delta.x * sin) + (delta.y * cos);
            var half = size * 0.5f;
            return Mathf.Abs(localX) <= half.x && Mathf.Abs(localY) <= half.y;
        }

        private void EnsureAgentRoot()
        {
            if (agentRoot != null)
            {
                return;
            }

            agentRoot = GameObject.Find(agentRootName) ?? new GameObject(agentRootName);
        }

        private SandboxAgentProfile GetProfile()
        {
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<SandboxAgentProfile>();
            }

            return profile;
        }

        private Sprite GetAgentSprite()
        {
            if (agentSprite != null)
            {
                return agentSprite;
            }

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
            agentSprite = Sprite.Create(agentTexture, new Rect(0f, 0f, agentTexture.width, agentTexture.height), new Vector2(0.5f, 0.5f), 8f);
            return agentSprite;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            counts.TryGetValue(key, out var current);
            counts[key] = current + 1;
        }

        private static bool IsPointInsidePolygon(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i, i += 1)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (((a.y > point.y) != (b.y > point.y)) &&
                    (point.x < (b.x - a.x) * (point.y - a.y) / ((b.y - a.y) + Mathf.Epsilon) + a.x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
