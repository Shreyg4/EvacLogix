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
        private const float SimulationGridFallbackSize = 0.5f;
        [SerializeField] private SandboxAgentProfile profile;
        [SerializeField] private string agentRootName = "SimAgentRoot";

        private readonly List<SandboxEvacueeAgent> agents = new();
        private readonly HashSet<string> resolvedAgentIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> exitUsage = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> evacuatedByFloor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> casualtiesByFloor = new(StringComparer.Ordinal);
        private readonly SandboxBuildingRouteGraph routeGraph = new();
        private readonly Dictionary<string, SandboxBuildingRouteGraph.RouteNode> agentTargets = new(StringComparer.Ordinal);
        private readonly HashSet<string> agentsThatUsedEscapeWindow = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> failedWindowNodesByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> dynamicCostToSafeByNode = new(StringComparer.Ordinal);

        // After teleporting onto a portal endpoint, that endpoint is blocked for the agent (it won't
        // route through it again) until the agent moves beyond the portal footprint + this margin.
        // This makes re-using a portal require physically leaving its space and coming back, so two
        // linked portals can never thrash an agent back and forth in place.
        private readonly Dictionary<string, string> blockedPortalByAgent = new(StringComparer.Ordinal);
        private const float PortalClearMargin = 0.75f;
        private const float EscapeWindowExteriorClearanceBuffer = 0.8f;
        private const float ImpassableThreshold = 0.99f;
        private const float FireRoutePenaltyWeight = 10f;
        private const float FireNodePenaltyWeight = 14f;
        private const float ExitPenaltyWeight = 18f;
        private const float SevereHazardRepathThreshold = 0.92f;
        private const float CongestionAvoidanceRadius = 1.5f;
        private const float CongestionRoutePenaltyWeight = 4f;
        // An agent only abandons its current target if an alternative beats it by more than this margin,
        // so a crowd doesn't oscillate between two exits each repath (herd flip-flop).
        private const float CongestionHysteresisMargin = 6f;
        // Fixed seed makes per-agent avoidance priorities (and therefore the run) reproducible.
        private const int AvoidanceSeed = 12345;

        // Deadlock recovery ("agents realize this isn't working"). Frustration rises while an agent
        // fails to close on its goal and decays while it advances. Crossing multiples of a per-agent
        // (seeded-random) threshold escalates: yield priority -> back off and retry -> reroute away.
        private readonly Dictionary<string, float> frustrationByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> lastGoalDistanceByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> frustrationThresholdByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> basePriorityByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> retreatTimerByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> avoidedNodeByAgent = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> avoidTimerByAgent = new(StringComparer.Ordinal);
        private const float FrustrationThresholdMin = 2.5f;
        private const float FrustrationThresholdMax = 4f;
        private const float FrustrationDecayMultiplier = 2f;
        private const float ProgressEpsilon = 0.05f;
        private const float BackoffFrustrationMultiplier = 2f;
        private const float RerouteFrustrationMultiplier = 3f;
        private const float RetreatHoldMin = 0.5f;
        private const float RetreatHoldMax = 1.5f;
        private const float RetreatHopDistance = 1f;
        private const int YieldAvoidancePriority = 90;
        private const float RerouteAvoidCooldown = 5f;

        private BuildingProjectData project;
        private SandboxFloorLayoutService layoutService;
        private SandboxFireSimulationService fireSimulationService;
        private readonly Dictionary<string, FloorData> floorsById = new(StringComparer.Ordinal);
        private GameObject agentRoot;
        private Sprite agentSprite;
        private Texture2D agentTexture;
        private int cachedHazardRevision = -1;

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

            RefreshDynamicRoutingCache(force: true);

            EnsureAgentRoot();
            var cap = Mathf.Max(1, agentCap);

            // Seed the priority RNG so the spawn order -> priority assignment (and thus the run) repeats.
            UnityEngine.Random.InitState(AvoidanceSeed);

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
                TickAvoidCooldown(agent, deltaTime);

                var retreating = UpdateRecovery(agent, deltaTime);

                if (!retreating && (ShouldForceHazardRepath(agent) || agent.NeedsRepath()))
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

                if (!retreating && agentTargets.TryGetValue(agent.AgentId, out var target) && HasReachedTarget(agent, target))
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
                        if (IsNodeBlockedByHazard(target))
                        {
                            RouteAgent(agent);
                        }
                        else
                        {
                            TeleportThroughPortal(agent, target);
                        }
                    }
                }
            }
        }

        private bool HasReachedTarget(SandboxEvacueeAgent agent, SandboxBuildingRouteGraph.RouteNode target)
        {
            if (agent == null || target == null)
            {
                return false;
            }

            if (!floorsById.TryGetValue(target.floorId, out var floor) || !layoutService.TryGetPlacement(target.floorId, out var placement))
            {
                return agent.IsAtDestination();
            }

            var agentPosition = agent.CurrentWorldPosition;
            var agentRadius = Mathf.Max(0.01f, agent.Radius);
            if (target.isExit)
            {
                var exitZone = floor.exits.Find(candidate => string.Equals(candidate.exitZoneId, target.objectId, StringComparison.Ordinal));
                return exitZone != null &&
                       IsCircleTouchingRotatedRect(agentPosition, agentRadius, placement.ToWorld(exitZone.center), exitZone.size, exitZone.rotationDegrees);
            }

            if (target.isEscapeWindow)
            {
                var window = floor.windows.Find(candidate => string.Equals(candidate.windowId, target.objectId, StringComparison.Ordinal));
                if (window == null || !TryResolveOpeningSegment(floor, window.wallSegmentId, window.offsetAlongWall, window.width, out var start, out var end))
                {
                    return agent.IsAtDestination();
                }

                var worldStart = placement.ToWorld(start);
                var worldEnd = placement.ToWorld(end);
                return DistancePointToSegment(agentPosition, worldStart, worldEnd) <= agentRadius;
            }

            var stairPortal = floor.stairPortals.Find(candidate => string.Equals(candidate.stairPortalId, target.objectId, StringComparison.Ordinal));
            if (stairPortal != null)
            {
                return IsCircleTouchingRotatedRect(
                    agentPosition,
                    agentRadius,
                    placement.ToWorld(stairPortal.localPosition),
                    stairPortal.size,
                    stairPortal.rotationDegrees);
            }

            var teleportPortal = floor.teleportPortals.Find(candidate => string.Equals(candidate.teleportPortalId, target.objectId, StringComparison.Ordinal));
            if (teleportPortal != null)
            {
                return IsCircleTouchingRotatedRect(
                    agentPosition,
                    agentRadius,
                    placement.ToWorld(teleportPortal.localPosition),
                    teleportPortal.size,
                    teleportPortal.rotationDegrees);
            }

            return agent.IsAtDestination();
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
            frustrationByAgent.Clear();
            lastGoalDistanceByAgent.Clear();
            frustrationThresholdByAgent.Clear();
            basePriorityByAgent.Clear();
            retreatTimerByAgent.Clear();
            avoidedNodeByAgent.Clear();
            avoidTimerByAgent.Clear();
            blockedPortalByAgent.Clear();
            agentsThatUsedEscapeWindow.Clear();
            failedWindowNodesByAgent.Clear();
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
            var profileForPriority = GetProfile();
            var basePriority = UnityEngine.Random.Range(profileForPriority.AvoidancePriorityMin, profileForPriority.AvoidancePriorityMax + 1);
            agent.SetAvoidancePriority(basePriority);
            basePriorityByAgent[agent.AgentId] = basePriority;
            frustrationThresholdByAgent[agent.AgentId] = UnityEngine.Random.Range(FrustrationThresholdMin, FrustrationThresholdMax);
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
            avoidedNodeByAgent.TryGetValue(agent.AgentId, out var avoidedNodeId);
            agentTargets.TryGetValue(agent.AgentId, out var currentTarget);
            var currentTargetId = currentTarget?.nodeId;

            var floorNodes = routeGraph.GetFloorNodes(agent.FloorId);
            var position = agent.CurrentWorldPosition;
            // "best" respects the frustrated-reroute avoid entry; "fallback" ignores it, so a lone exit
            // that's been blacklisted is still chosen rather than stranding the agent.
            SandboxBuildingRouteGraph.RouteNode best = null;
            var bestWorld = position;
            var bestCost = float.MaxValue;
            SandboxBuildingRouteGraph.RouteNode fallback = null;
            var fallbackWorld = position;
            var fallbackCost = float.MaxValue;
            for (var i = 0; i < floorNodes.Count; i += 1)
            {
                var node = floorNodes[i];
                if (float.IsInfinity(node.costToSafe) ||
                    (blockedNodeId != null && string.Equals(node.nodeId, blockedNodeId, StringComparison.Ordinal)) ||
                    ShouldSkipNodeForAgent(agent, node) ||
                    IsNodeBlockedByHazard(node))
                {
                    continue;
                }

                var world = placement.ToWorld(node.localPosition);
                var costToSafe = GetDynamicCostToSafe(node);
                if (float.IsInfinity(costToSafe))
                {
                    continue;
                }

                var total = Vector2.Distance(position, world) +
                            costToSafe +
                            GetFireRoutePenalty(agent.FloorId, placement, position, world) +
                            GetCongestionRoutePenalty(agent, position, world);

                // Hysteresis: discount the node the agent is already committed to so it only switches
                // when an alternative is better by more than the margin (prevents herd flip-flop).
                var comparison = total;
                if (currentTargetId != null && string.Equals(node.nodeId, currentTargetId, StringComparison.Ordinal))
                {
                    comparison -= CongestionHysteresisMargin;
                }

                if (comparison < fallbackCost)
                {
                    fallbackCost = comparison;
                    fallback = node;
                    fallbackWorld = world;
                }

                var isAvoided = avoidedNodeId != null && string.Equals(node.nodeId, avoidedNodeId, StringComparison.Ordinal);
                if (!isAvoided && comparison < bestCost)
                {
                    bestCost = comparison;
                    best = node;
                    bestWorld = world;
                }
            }

            var chosen = best ?? fallback;
            if (chosen != null)
            {
                var chosenWorld = best != null ? bestWorld : fallbackWorld;
                agentTargets[agent.AgentId] = chosen;
                agent.SetDestination(chosen.objectId, chosenWorld);
                lastGoalDistanceByAgent[agent.AgentId] = Vector2.Distance(position, chosenWorld);
                return;
            }

            // Only the blocked portal can lead to safety: the agent must shuttle back through it.
            // Tell it to step clear of the footprint first; ReleasePortalBlockIfCleared then lifts the
            // block and the next repath re-targets the portal for the return trip ("leave and come
            // back"), so it can never re-teleport without physically exiting the portal space.
            if (!string.IsNullOrWhiteSpace(blockedNodeId) &&
                routeGraph.TryGetNode(blockedNodeId, out var blockedNode) &&
                !float.IsInfinity(GetDynamicCostToSafe(blockedNode)))
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
                    if (ShouldSkipNodeForAgent(agent, sinks[i]))
                    {
                        continue;
                    }

                    if (IsNodeBlockedByHazard(sinks[i]))
                    {
                        continue;
                    }

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

        // Ages out an agent's temporary node blacklist (set by a frustrated reroute) so it can return to
        // that node later if it becomes the best option again.
        private void TickAvoidCooldown(SandboxEvacueeAgent agent, float deltaTime)
        {
            if (!avoidTimerByAgent.TryGetValue(agent.AgentId, out var remaining))
            {
                return;
            }

            remaining -= deltaTime;
            if (remaining <= 0f)
            {
                avoidTimerByAgent.Remove(agent.AgentId);
                avoidedNodeByAgent.Remove(agent.AgentId);
            }
            else
            {
                avoidTimerByAgent[agent.AgentId] = remaining;
            }
        }

        // Deadlock recovery: tracks whether the agent is making headway and escalates when it isn't.
        // Returns true while the agent is in a physical back-off retreat (caller suppresses normal
        // repath/arrival handling so the retreat isn't immediately overwritten).
        private bool UpdateRecovery(SandboxEvacueeAgent agent, float deltaTime)
        {
            var id = agent.AgentId;

            // Already retreating: count down the hold, then reset and re-approach.
            if (retreatTimerByAgent.TryGetValue(id, out var retreat) && retreat > 0f)
            {
                retreat -= deltaTime;
                if (retreat <= 0f)
                {
                    retreatTimerByAgent.Remove(id);
                    frustrationByAgent[id] = 0f;
                    RestoreBasePriority(agent);
                    RouteAgent(agent);
                }
                else
                {
                    retreatTimerByAgent[id] = retreat;
                }

                return true;
            }

            // No active target (just resolved/teleporting): nothing to be frustrated about.
            if (!agentTargets.TryGetValue(id, out var target) || target == null)
            {
                frustrationByAgent[id] = 0f;
                RestoreBasePriority(agent);
                return false;
            }

            var goalDistance = Vector2.Distance(agent.CurrentWorldPosition, agent.CurrentDestination);
            var threshold = frustrationThresholdByAgent.TryGetValue(id, out var t) ? t : FrustrationThresholdMin;
            frustrationByAgent.TryGetValue(id, out var frustration);
            var lastGoalDistance = lastGoalDistanceByAgent.TryGetValue(id, out var last) ? last : goalDistance;

            if (goalDistance < lastGoalDistance - ProgressEpsilon)
            {
                // Making progress: relax and track the closer distance.
                frustration = Mathf.Max(0f, frustration - deltaTime * FrustrationDecayMultiplier);
                lastGoalDistanceByAgent[id] = goalDistance;
            }
            else
            {
                frustration += deltaTime;
                if (goalDistance < lastGoalDistance)
                {
                    lastGoalDistanceByAgent[id] = goalDistance;
                }
            }

            frustrationByAgent[id] = frustration;

            // Tier 3 — reroute: blacklist this target for a cooldown and pick another exit.
            if (frustration >= threshold * RerouteFrustrationMultiplier)
            {
                if (!string.IsNullOrWhiteSpace(target.nodeId))
                {
                    avoidedNodeByAgent[id] = target.nodeId;
                    avoidTimerByAgent[id] = RerouteAvoidCooldown;
                }

                frustrationByAgent[id] = 0f;
                RestoreBasePriority(agent);
                RouteAgent(agent);
                return false;
            }

            // Tier 2 — back off: hop away from the bottleneck briefly, then re-approach.
            if (frustration >= threshold * BackoffFrustrationMultiplier)
            {
                agent.SetDestination(string.Empty, ResolveRetreatPoint(agent));
                retreatTimerByAgent[id] = UnityEngine.Random.Range(RetreatHoldMin, RetreatHoldMax);
                return true;
            }

            // Tier 1 — yield: defer to neighbors so a symmetric standoff resolves in someone's favor.
            if (frustration >= threshold)
            {
                agent.SetAvoidancePriority(YieldAvoidancePriority);
            }
            else
            {
                RestoreBasePriority(agent);
            }

            return false;
        }

        private void RestoreBasePriority(SandboxEvacueeAgent agent)
        {
            if (basePriorityByAgent.TryGetValue(agent.AgentId, out var basePriority))
            {
                agent.SetAvoidancePriority(basePriority);
            }
        }

        // A point a short hop away from the agent's current goal, sampled onto the navmesh, used to step
        // an agent out of a jam so the bottleneck can drain before it tries again.
        private Vector2 ResolveRetreatPoint(SandboxEvacueeAgent agent)
        {
            var position = agent.CurrentWorldPosition;
            var awayDirection = position - agent.CurrentDestination;
            if (awayDirection.sqrMagnitude < 0.0001f)
            {
                awayDirection = UnityEngine.Random.insideUnitCircle;
                if (awayDirection.sqrMagnitude < 0.0001f)
                {
                    awayDirection = Vector2.right;
                }
            }

            var retreatWorld = position + (awayDirection.normalized * RetreatHopDistance);
            return TrySampleNavWorldPosition(retreatWorld, agent.Radius, out var sampled) ? sampled : retreatWorld;
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
            if (agent == null || node == null)
            {
                return;
            }

            if (!TryResolveEscapeWindowLanding(agent, node, out var landingFloorId, out var landingWorldPosition))
            {
                MarkWindowFailedForAgent(agent, node.nodeId);
                RouteAgent(agent);
                return;
            }

            agentsThatUsedEscapeWindow.Add(agent.AgentId);
            MarkWindowFailedForAgent(agent, node.nodeId);
            agent.Relocate(landingFloorId, landingWorldPosition);
            agent.ApplyInjury(node.windowInjury);
            if (agent.Health <= 0f)
            {
                ResolveCasualty(agent);
            }
            else
            {
                RouteAgent(agent);
            }
        }

        private float ComputeFireExposure(SandboxEvacueeAgent agent)
        {
            if (fireSimulationService == null || !fireSimulationService.SimulationActive ||
                !layoutService.TryGetPlacement(agent.FloorId, out var placement))
            {
                return 0f;
            }

            var radius = Mathf.Max(0.1f, GetProfile().FireDangerRadius);
            var localPosition = agent.CurrentWorldPosition - placement.OriginOffset;
            var hazard = fireSimulationService.SampleHazard(agent.FloorId, localPosition, radius);
            if (hazard <= fireSimulationService.HazardDamageThreshold)
            {
                return 0f;
            }

            return Mathf.InverseLerp(fireSimulationService.HazardDamageThreshold, 1f, hazard);
        }

        // Routing deterrent: how much fire sits near the straight path to a candidate node, so agents
        // re-route toward exits away from the fire as it spreads (recomputed each repath).
        private float GetFireRoutePenalty(string floorId, SandboxFloorPlacement placement, Vector2 from, Vector2 to)
        {
            if (fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return 0f;
            }

            var radius = Mathf.Max(0.1f, GetProfile().FireDangerRadius);
            var penalty = 0f;
            var peak = 0f;
            const int samples = 5;
            for (var i = 0; i < samples; i += 1)
            {
                var t = samples == 1 ? 0.5f : i / (float)(samples - 1);
                var worldPoint = Vector2.Lerp(from, to, t);
                var localPoint = worldPoint - placement.OriginOffset;
                var hazard = fireSimulationService.SampleHazard(floorId, localPoint, radius);
                peak = Mathf.Max(peak, hazard);
                if (hazard <= fireSimulationService.HazardCostThreshold)
                {
                    continue;
                }

                penalty += Mathf.InverseLerp(fireSimulationService.HazardCostThreshold, 1f, hazard) * FireRoutePenaltyWeight;
            }

            if (peak >= fireSimulationService.HazardImpassableThreshold)
            {
                penalty += 1000f;
            }

            return penalty;
        }

        private void RefreshDynamicRoutingCache(bool force = false)
        {
            var revision = fireSimulationService != null && fireSimulationService.SimulationActive
                ? fireSimulationService.HazardRevision
                : -1;
            if (!force && revision == cachedHazardRevision)
            {
                return;
            }

            dynamicCostToSafeByNode.Clear();
            if (project == null)
            {
                cachedHazardRevision = revision;
                return;
            }

            var dynamicCosts = routeGraph.ComputeDynamicCostToSafe(GetNodeHazardPenalty, IsNodeBlockedByHazard);
            foreach (var pair in dynamicCosts)
            {
                dynamicCostToSafeByNode[pair.Key] = pair.Value;
            }

            cachedHazardRevision = revision;
        }

        private float GetDynamicCostToSafe(SandboxBuildingRouteGraph.RouteNode node)
        {
            if (node == null)
            {
                return float.PositiveInfinity;
            }

            return dynamicCostToSafeByNode.TryGetValue(node.nodeId, out var cost) ? cost : node.costToSafe;
        }

        private float GetNodeHazardPenalty(SandboxBuildingRouteGraph.RouteNode node)
        {
            if (node == null || fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return 0f;
            }

            var hazard = GetNodeHazard(node);
            if (hazard <= fireSimulationService.HazardCostThreshold)
            {
                return 0f;
            }

            var basePenalty = Mathf.InverseLerp(fireSimulationService.HazardCostThreshold, 1f, hazard);
            return basePenalty * (node.isExit ? ExitPenaltyWeight : FireNodePenaltyWeight);
        }

        private bool IsNodeBlockedByHazard(SandboxBuildingRouteGraph.RouteNode node)
        {
            if (node == null || fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return false;
            }

            var hazard = GetNodeHazard(node);
            var threshold = node.isExit
                ? fireSimulationService.ExitUnusableThreshold
                : fireSimulationService.ConnectorUnusableThreshold;
            return hazard >= threshold;
        }

        private float GetNodeHazard(SandboxBuildingRouteGraph.RouteNode node)
        {
            if (node == null || fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return 0f;
            }

            var radius = GetNodeHazardRadius(node);
            var hazard = fireSimulationService.SampleHazard(node.floorId, node.localPosition, radius);
            if (node.isEscapeWindow && !string.IsNullOrWhiteSpace(node.landingFloorId))
            {
                hazard = Mathf.Max(hazard, fireSimulationService.SampleHazard(node.landingFloorId, node.landingLocalPosition, radius));
            }

            return hazard;
        }

        private float GetNodeHazardRadius(SandboxBuildingRouteGraph.RouteNode node)
        {
            if (node == null)
            {
                return 0.5f;
            }

            if (node.isExit && floorsById.TryGetValue(node.floorId, out var floor))
            {
                var exitZone = floor.exits.Find(candidate => string.Equals(candidate.exitZoneId, node.objectId, StringComparison.Ordinal));
                if (exitZone != null)
                {
                    return Mathf.Max(0.25f, Mathf.Max(exitZone.size.x, exitZone.size.y) * 0.5f);
                }
            }

            if (node.footprintRadius > 0.01f)
            {
                return node.footprintRadius;
            }

            return 0.5f;
        }

        private bool ShouldForceHazardRepath(SandboxEvacueeAgent agent)
        {
            if (agent == null || fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return false;
            }

            if (!agentTargets.TryGetValue(agent.AgentId, out var target) || target == null)
            {
                return false;
            }

            if (IsNodeBlockedByHazard(target))
            {
                return true;
            }

            if (!layoutService.TryGetPlacement(agent.FloorId, out var placement))
            {
                return false;
            }

            var localPosition = agent.CurrentWorldPosition - placement.OriginOffset;
            var localTarget = target.floorId == agent.FloorId ? target.localPosition : localPosition;
            var peak = GetPathHazardPeak(agent.FloorId, localPosition, localTarget);
            return peak >= SevereHazardRepathThreshold;
        }

        private float GetPathHazardPeak(string floorId, Vector2 localFrom, Vector2 localTo)
        {
            if (fireSimulationService == null || !fireSimulationService.SimulationActive)
            {
                return 0f;
            }

            const int samples = 5;
            var radius = Mathf.Max(0.1f, GetProfile().FireDangerRadius);
            var peak = 0f;
            for (var i = 0; i < samples; i += 1)
            {
                var t = samples == 1 ? 0.5f : i / (float)(samples - 1);
                var localPoint = Vector2.Lerp(localFrom, localTo, t);
                peak = Mathf.Max(peak, fireSimulationService.SampleHazard(floorId, localPoint, radius));
            }

            return peak;
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

        private static bool IsCircleTouchingRotatedRect(Vector2 circleCenter, float circleRadius, Vector2 rectCenter, Vector2 rectSize, float rotationDegrees)
        {
            var radians = -rotationDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            var delta = circleCenter - rectCenter;
            var localX = (delta.x * cos) - (delta.y * sin);
            var localY = (delta.x * sin) + (delta.y * cos);
            var half = rectSize * 0.5f;
            var clampedX = Mathf.Clamp(localX, -half.x, half.x);
            var clampedY = Mathf.Clamp(localY, -half.y, half.y);
            var dx = localX - clampedX;
            var dy = localY - clampedY;
            return (dx * dx) + (dy * dy) <= (circleRadius * circleRadius);
        }

        private bool TryResolveOpeningSegment(FloorData floor, string wallSegmentId, float offsetAlongWall, float authoredWidth, out Vector2 start, out Vector2 end)
        {
            start = Vector2.zero;
            end = Vector2.zero;
            if (floor == null)
            {
                return false;
            }

            var wall = floor.wallSegments.Find(candidate => string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return false;
            }

            var direction = wall.endPoint - wall.startPoint;
            var length = direction.magnitude;
            if (length <= 0.0001f)
            {
                return false;
            }

            var normalized = direction / length;
            var center = wall.startPoint + (normalized * offsetAlongWall);
            var worldWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(project, floor, authoredWidth, SimulationGridFallbackSize);
            var halfWidth = Mathf.Max(0.01f, worldWidth) * 0.5f;
            start = center - (normalized * halfWidth);
            end = center + (normalized * halfWidth);
            return true;
        }

        private bool ShouldSkipNodeForAgent(SandboxEvacueeAgent agent, SandboxBuildingRouteGraph.RouteNode node)
        {
            if (agent == null || node == null || !node.isEscapeWindow)
            {
                return false;
            }

            return agentsThatUsedEscapeWindow.Contains(agent.AgentId) || IsWindowFailedForAgent(agent.AgentId, node.nodeId);
        }

        private bool TryResolveEscapeWindowLanding(
            SandboxEvacueeAgent agent,
            SandboxBuildingRouteGraph.RouteNode node,
            out string landingFloorId,
            out Vector2 landingWorldPosition)
        {
            landingFloorId = string.Empty;
            landingWorldPosition = Vector2.zero;
            if (agent == null ||
                node == null ||
                string.IsNullOrWhiteSpace(node.landingFloorId) ||
                !layoutService.TryGetPlacement(node.landingFloorId, out var landingPlacement))
            {
                return false;
            }

            var landingClearance = Mathf.Max(agent.Radius + EscapeWindowExteriorClearanceBuffer, 0.5f);
            var landingLocalPoint = node.localPosition + (node.escapeOutwardNormal * landingClearance);
            var projectedWorld = landingPlacement.ToWorld(landingLocalPoint);
            if (!TrySampleNavWorldPosition(projectedWorld, agent.Radius, out var sampledWorldPosition))
            {
                return false;
            }

            if (!landingPlacement.WorldBounds.Contains(sampledWorldPosition))
            {
                return false;
            }

            landingFloorId = node.landingFloorId;
            landingWorldPosition = sampledWorldPosition;
            return true;
        }

        private static bool TrySampleNavWorldPosition(Vector2 worldPosition, float agentRadius, out Vector2 sampledWorldPosition)
        {
            sampledWorldPosition = worldPosition;
            var navPosition = new Vector3(worldPosition.x, 0f, worldPosition.y);
            var searchRadius = Mathf.Max(agentRadius + 2f, 2.5f);
            if (!UnityEngine.AI.NavMesh.SamplePosition(navPosition, out var hit, searchRadius, UnityEngine.AI.NavMesh.AllAreas))
            {
                return false;
            }

            sampledWorldPosition = new Vector2(hit.position.x, hit.position.z);
            return true;
        }

        private bool IsWindowFailedForAgent(string agentId, string nodeId)
        {
            return !string.IsNullOrWhiteSpace(agentId) &&
                   !string.IsNullOrWhiteSpace(nodeId) &&
                   failedWindowNodesByAgent.TryGetValue(agentId, out var failed) &&
                   failed.Contains(nodeId);
        }

        private void MarkWindowFailedForAgent(SandboxEvacueeAgent agent, string nodeId)
        {
            if (agent == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            if (!failedWindowNodesByAgent.TryGetValue(agent.AgentId, out var failed))
            {
                failed = new HashSet<string>(StringComparer.Ordinal);
                failedWindowNodesByAgent[agent.AgentId] = failed;
            }

            failed.Add(nodeId);
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
