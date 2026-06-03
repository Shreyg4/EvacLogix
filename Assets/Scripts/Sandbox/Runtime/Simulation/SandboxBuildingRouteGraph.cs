using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // Building-wide evacuation routing graph (P2). Nodes are exits and stair/teleport portal
    // endpoints across every floor. Edges:
    //   - exit node -> SAFE sink (cost 0): reaching an exit means escaped.
    //   - portal node -> its paired endpoint on another floor (cost = travelCost), honoring stair
    //     ascend/descend rules and teleport pair-enabled.
    //   - any two nodes on the same floor are linked by their straight-line distance (a routing
    //     heuristic; walls are not path-traced here, only used to rank which node to head for).
    // A reverse Dijkstra from SAFE yields each node's cost-to-escape, so an agent anywhere can pick
    // the on-floor node minimizing (walk-to-node + node.CostToSafe). Topology is static in P2.
    public sealed class SandboxBuildingRouteGraph
    {
        public sealed class RouteNode
        {
            public string nodeId = string.Empty;
            public string floorId = string.Empty;
            public Vector2 localPosition;
            public bool isExit;
            public string objectId = string.Empty;
            public string linkedNodeId = string.Empty;
            public float portalTravelCost;
            // Half-extent of the portal footprint; an agent must move beyond this (plus a margin)
            // after teleporting onto the endpoint before it may use the same portal again.
            public float footprintRadius;
            // Escape-usable window: a risky one-time escape transition. Agents route to the opening,
            // relocate to an exterior landing point, take injury, then continue solving toward exits.
            public bool isEscapeWindow;
            public float escapeRouteCost;
            public float windowInjury;
            public string landingFloorId = string.Empty;
            public Vector2 landingLocalPosition;
            public Vector2 escapeOutwardNormal;
            public int storeyIndex = 1;
            public bool IsSink => isExit;
            public float costToSafe = float.PositiveInfinity;
        }

        private const string SafeNodeId = "__SAFE__";
        private const float DefaultLandingClearance = 1f;
        private const float FloorOneWindowInjury = 0.08f;
        private const float FloorTwoWindowInjury = 0.32f;
        private const float FloorThreeWindowInjury = 0.82f;
        private const float ExtraStoreyWindowInjury = 0.08f;
        private const float WindowRiskRouteWeight = 1.5f;
        private const float WindowStoreyRoutePenalty = 0.5f;

        private readonly Dictionary<string, RouteNode> nodesById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RouteNode>> nodesByFloor = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RouteEdge>> edgesByNode = new(StringComparer.Ordinal);
        private readonly List<RouteNode> sinks = new();

        // All escape sinks (exits + escape windows) across the whole project, for the project-wide
        // fallback when an agent's own floor offers no reachable escape.
        public IReadOnlyList<RouteNode> Sinks => sinks;

        public bool TryGetNode(string nodeId, out RouteNode node) => nodesById.TryGetValue(nodeId ?? string.Empty, out node);

        public IReadOnlyList<RouteNode> GetFloorNodes(string floorId)
        {
            return nodesByFloor.TryGetValue(floorId ?? string.Empty, out var list) ? list : Array.Empty<RouteNode>();
        }

        public void Build(BuildingProjectData project)
        {
            nodesById.Clear();
            nodesByFloor.Clear();
            edgesByNode.Clear();
            sinks.Clear();
            if (project?.floors == null)
            {
                return;
            }

            var elevationByFloor = new Dictionary<string, float>(StringComparer.Ordinal);
            var floorBoundsById = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var lowestOrder = int.MaxValue;
            var lowestOrderFloorId = string.Empty;
            var lowestAboveGroundOrder = int.MaxValue;
            var lowestAboveGroundFloorId = string.Empty;
            foreach (var floor in project.floors)
            {
                elevationByFloor[floor.floorId] = floor.elevation;
                floorBoundsById[floor.floorId] = SandboxFloorLayoutService.ComputeFloorLocalBounds(floor);
                if (floor.order < lowestOrder)
                {
                    lowestOrder = floor.order;
                    lowestOrderFloorId = floor.floorId;
                }

                if (floor.elevation >= 0f && floor.order < lowestAboveGroundOrder)
                {
                    lowestAboveGroundOrder = floor.order;
                    lowestAboveGroundFloorId = floor.floorId;
                }
            }

            if (string.IsNullOrWhiteSpace(lowestAboveGroundFloorId))
            {
                lowestAboveGroundOrder = lowestOrder;
                lowestAboveGroundFloorId = lowestOrderFloorId;
            }

            // Create nodes.
            foreach (var floor in project.floors)
            {
                foreach (var exit in floor.exits)
                {
                    AddNode(new RouteNode
                    {
                        nodeId = ExitId(exit.exitZoneId),
                        floorId = floor.floorId,
                        localPosition = exit.center,
                        isExit = true,
                        objectId = exit.exitZoneId
                    });
                }

                foreach (var stair in floor.stairPortals)
                {
                    AddNode(new RouteNode
                    {
                        nodeId = StairId(stair.stairPortalId),
                        floorId = floor.floorId,
                        localPosition = stair.localPosition,
                        objectId = stair.stairPortalId,
                        linkedNodeId = string.IsNullOrWhiteSpace(stair.targetStairPortalId) ? string.Empty : StairId(stair.targetStairPortalId),
                        portalTravelCost = Mathf.Max(0f, stair.travelCost),
                        footprintRadius = 0.5f * Mathf.Max(stair.size.x, stair.size.y)
                    });
                }

                foreach (var teleport in floor.teleportPortals)
                {
                    if (!teleport.isPairEnabled)
                    {
                        continue;
                    }

                    AddNode(new RouteNode
                    {
                        nodeId = TeleId(teleport.teleportPortalId),
                        floorId = floor.floorId,
                        localPosition = teleport.localPosition,
                        objectId = teleport.teleportPortalId,
                        linkedNodeId = string.IsNullOrWhiteSpace(teleport.targetTeleportPortalId) ? string.Empty : TeleId(teleport.targetTeleportPortalId),
                        portalTravelCost = Mathf.Max(0f, teleport.travelCost),
                        footprintRadius = 0.5f * Mathf.Max(teleport.size.x, teleport.size.y)
                    });
                }

                foreach (var window in floor.windows)
                {
                    if (!window.canBeUsedForEscape ||
                        !TryResolveEscapeWindowData(floor, window, floorBoundsById, out var center, out var outwardNormal))
                    {
                        continue;
                    }

                    var storeyIndex = ComputeStoreyIndex(floor.order, lowestAboveGroundOrder);
                    var risk = Mathf.Max(0.01f, window.escapeRiskMultiplier);
                    var landingFloorId = storeyIndex <= 1 ? floor.floorId : lowestAboveGroundFloorId;
                    var landingLocalPosition = center + (outwardNormal * DefaultLandingClearance);
                    var windowInjury = ComputeWindowInjury(storeyIndex, risk);
                    AddNode(new RouteNode
                    {
                        nodeId = WindowId(window.windowId),
                        floorId = floor.floorId,
                        localPosition = center,
                        objectId = window.windowId,
                        isEscapeWindow = true,
                        escapeRouteCost = Mathf.Max(0f, window.escapeCost * 0.25f) + (windowInjury * WindowRiskRouteWeight) + ((storeyIndex - 1) * WindowStoreyRoutePenalty),
                        windowInjury = windowInjury,
                        landingFloorId = landingFloorId,
                        landingLocalPosition = landingLocalPosition,
                        escapeOutwardNormal = outwardNormal,
                        storeyIndex = storeyIndex
                    });
                }
            }

            BuildPortalDirectionFilter(project, elevationByFloor);
            ComputeCostToSafe();
        }

        // Removes portal links that a stair's ascend/descend rule forbids, so they aren't used.
        private void BuildPortalDirectionFilter(BuildingProjectData project, Dictionary<string, float> elevationByFloor)
        {
            foreach (var floor in project.floors)
            {
                foreach (var stair in floor.stairPortals)
                {
                    if (stair.direction == StairTraversalDirection.Bidirectional)
                    {
                        continue;
                    }

                    if (!nodesById.TryGetValue(StairId(stair.stairPortalId), out var node) || string.IsNullOrWhiteSpace(node.linkedNodeId))
                    {
                        continue;
                    }

                    var sourceElevation = elevationByFloor.TryGetValue(stair.sourceFloorId, out var se) ? se : floor.elevation;
                    var targetElevation = elevationByFloor.TryGetValue(stair.targetFloorId, out var te) ? te : sourceElevation;
                    var ascends = targetElevation > sourceElevation;
                    var allowed = stair.direction == StairTraversalDirection.AscendOnly ? ascends : !ascends;
                    if (!allowed)
                    {
                        node.linkedNodeId = string.Empty;
                    }
                }
            }
        }

        // Reverse Dijkstra from the SAFE sink. Forward edges: exit->SAFE(0), portal->linked(tc),
        // sameFloor a<->b (distance). costToSafe[n] = least forward cost from n to SAFE.
        private void ComputeCostToSafe()
        {
            foreach (var node in nodesById.Values)
            {
                if (node.isExit)
                {
                    AddEdge(node.nodeId, SafeNodeId, 0f);
                }
                else if (!string.IsNullOrWhiteSpace(node.linkedNodeId) && nodesById.ContainsKey(node.linkedNodeId))
                {
                    AddEdge(node.nodeId, node.linkedNodeId, node.portalTravelCost);
                }
            }

            // Same-floor straight-line edges (symmetric).
            foreach (var pair in nodesByFloor)
            {
                var list = pair.Value;
                for (var i = 0; i < list.Count; i += 1)
                {
                    for (var j = i + 1; j < list.Count; j += 1)
                    {
                        var distance = Vector2.Distance(list[i].localPosition, list[j].localPosition);
                        AddEdge(list[i].nodeId, list[j].nodeId, distance);
                        AddEdge(list[j].nodeId, list[i].nodeId, distance);
                    }
                }
            }

            // Escape windows are one-way transitions to an exterior landing point on the landing
            // floor. After that landing, the agent continues using the normal routing graph from the
            // nearest nodes/exits on that landing floor.
            foreach (var node in nodesById.Values)
            {
                if (!node.isEscapeWindow ||
                    string.IsNullOrWhiteSpace(node.landingFloorId) ||
                    !nodesByFloor.TryGetValue(node.landingFloorId, out var landingNodes))
                {
                    continue;
                }

                for (var i = 0; i < landingNodes.Count; i += 1)
                {
                    var destination = landingNodes[i];
                    if (string.Equals(destination.nodeId, node.nodeId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var transitionCost = node.escapeRouteCost + Vector2.Distance(node.landingLocalPosition, destination.localPosition);
                    AddEdge(node.nodeId, destination.nodeId, transitionCost);
                }
            }

            var dist = ComputeDynamicCostToSafe(null, null);
            foreach (var node in nodesById.Values)
            {
                node.costToSafe = dist.TryGetValue(node.nodeId, out var d) ? d : float.PositiveInfinity;
            }
        }

        public Dictionary<string, float> ComputeDynamicCostToSafe(Func<RouteNode, float> nodePenaltyProvider, Func<RouteNode, bool> nodeBlockedPredicate)
        {
            var reverse = new Dictionary<string, List<(string nodeId, float weight)>>(StringComparer.Ordinal);

            void AddReverse(string from, string to, float weight)
            {
                if (!reverse.TryGetValue(to, out var list))
                {
                    list = new List<(string, float)>();
                    reverse[to] = list;
                }

                list.Add((from, weight));
            }

            foreach (var pair in edgesByNode)
            {
                if (!nodesById.TryGetValue(pair.Key, out var fromNode))
                {
                    continue;
                }

                if (nodeBlockedPredicate != null && nodeBlockedPredicate(fromNode))
                {
                    continue;
                }

                var nodePenalty = Mathf.Max(0f, nodePenaltyProvider?.Invoke(fromNode) ?? 0f);
                for (var i = 0; i < pair.Value.Count; i += 1)
                {
                    var edge = pair.Value[i];
                    if (!string.Equals(edge.toNodeId, SafeNodeId, StringComparison.Ordinal))
                    {
                        if (!nodesById.TryGetValue(edge.toNodeId, out var toNode))
                        {
                            continue;
                        }

                        if (nodeBlockedPredicate != null && nodeBlockedPredicate(toNode))
                        {
                            continue;
                        }
                    }

                    AddReverse(fromNode.nodeId, edge.toNodeId, edge.weight + nodePenalty);
                }
            }

            var dist = new Dictionary<string, float>(StringComparer.Ordinal) { [SafeNodeId] = 0f };
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (true)
            {
                var currentId = string.Empty;
                var currentDist = float.PositiveInfinity;
                foreach (var pair in dist)
                {
                    if (!visited.Contains(pair.Key) && pair.Value < currentDist)
                    {
                        currentDist = pair.Value;
                        currentId = pair.Key;
                    }
                }

                if (string.IsNullOrEmpty(currentId))
                {
                    break;
                }

                visited.Add(currentId);
                if (!reverse.TryGetValue(currentId, out var neighbors))
                {
                    continue;
                }

                for (var i = 0; i < neighbors.Count; i += 1)
                {
                    var candidate = currentDist + neighbors[i].weight;
                    if (!dist.TryGetValue(neighbors[i].nodeId, out var existing) || candidate < existing)
                    {
                        dist[neighbors[i].nodeId] = candidate;
                    }
                }
            }

            return dist;
        }

        private void AddNode(RouteNode node)
        {
            if (nodesById.ContainsKey(node.nodeId))
            {
                return;
            }

            nodesById[node.nodeId] = node;
            if (!nodesByFloor.TryGetValue(node.floorId, out var list))
            {
                list = new List<RouteNode>();
                nodesByFloor[node.floorId] = list;
            }

            list.Add(node);
            if (node.IsSink)
            {
                sinks.Add(node);
            }
        }

        private void AddEdge(string fromNodeId, string toNodeId, float weight)
        {
            if (string.IsNullOrWhiteSpace(fromNodeId) || string.IsNullOrWhiteSpace(toNodeId))
            {
                return;
            }

            if (!edgesByNode.TryGetValue(fromNodeId, out var edges))
            {
                edges = new List<RouteEdge>();
                edgesByNode[fromNodeId] = edges;
            }

            edges.Add(new RouteEdge(toNodeId, Mathf.Max(0f, weight)));
        }

        private static bool TryResolveOpeningCenter(FloorData floor, string wallSegmentId, float offsetAlongWall, out Vector2 center)
        {
            center = Vector2.zero;
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (!string.Equals(wall.wallSegmentId, wallSegmentId, StringComparison.Ordinal))
                {
                    continue;
                }

                var direction = wall.endPoint - wall.startPoint;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    return false;
                }

                center = wall.startPoint + (direction.normalized * offsetAlongWall);
                return true;
            }

            return false;
        }

        private static string ExitId(string id) => "exit:" + id;
        private static string StairId(string id) => "stair:" + id;
        private static string TeleId(string id) => "tele:" + id;
        private static string WindowId(string id) => "win:" + id;

        private static int ComputeStoreyIndex(int floorOrder, int lowestOrder)
        {
            return Mathf.Max(1, (floorOrder - lowestOrder) + 1);
        }

        private static float ComputeWindowInjury(int storeyIndex, float riskMultiplier)
        {
            var baseInjury = storeyIndex switch
            {
                <= 1 => FloorOneWindowInjury,
                2 => FloorTwoWindowInjury,
                3 => FloorThreeWindowInjury,
                _ => FloorThreeWindowInjury + ((storeyIndex - 3) * ExtraStoreyWindowInjury),
            };

            return Mathf.Clamp01(baseInjury * Mathf.Max(0.01f, riskMultiplier));
        }

        private static bool TryResolveEscapeWindowData(
            FloorData floor,
            WindowData window,
            IReadOnlyDictionary<string, Rect> floorBoundsById,
            out Vector2 center,
            out Vector2 outwardNormal)
        {
            center = Vector2.zero;
            outwardNormal = Vector2.zero;
            if (floor == null ||
                window == null ||
                !TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out center) ||
                !floorBoundsById.TryGetValue(floor.floorId, out var bounds))
            {
                return false;
            }

            var wall = floor.wallSegments.Find(candidate => string.Equals(candidate.wallSegmentId, window.wallSegmentId, StringComparison.Ordinal));
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

            var tangent = direction / length;
            var normalA = new Vector2(-tangent.y, tangent.x);
            var normalB = -normalA;
            var centerToBounds = center - bounds.center;
            var scoreA = Vector2.Dot(centerToBounds, normalA);
            var scoreB = Vector2.Dot(centerToBounds, normalB);
            if (Mathf.Abs(scoreA - scoreB) <= 0.001f)
            {
                return false;
            }

            outwardNormal = scoreA > scoreB ? normalA : normalB;
            return outwardNormal.sqrMagnitude > 0.0001f;
        }

        private readonly struct RouteEdge
        {
            public RouteEdge(string toNodeId, float weight)
            {
                this.toNodeId = toNodeId;
                this.weight = weight;
            }

            public readonly string toNodeId;
            public readonly float weight;
        }
    }
}
