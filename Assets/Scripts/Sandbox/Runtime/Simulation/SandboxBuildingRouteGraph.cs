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
            // Escape-usable window: a sink like an exit but with a routing penalty (escapeRouteCost),
            // a fall injury applied on use, and a fatal flag above a height threshold.
            public bool isEscapeWindow;
            public float escapeRouteCost;
            public float windowInjury;
            public bool windowFatal;
            public bool IsSink => isExit || isEscapeWindow;
            public float costToSafe = float.PositiveInfinity;
        }

        private const string SafeNodeId = "__SAFE__";
        // Escape-window fall model, scaled by storeys above ground (floor.order):
        private const float StoreyRoutingPenalty = 6f; // added routing cost per storey (deters high windows)
        private const float StoreyInjury = 0.18f;       // health lost per storey on landing
        private const int FatalStoreys = 4;             // jumps from this height or higher are fatal

        private readonly Dictionary<string, RouteNode> nodesById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RouteNode>> nodesByFloor = new(StringComparer.Ordinal);
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
            sinks.Clear();
            if (project?.floors == null)
            {
                return;
            }

            var elevationByFloor = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var floor in project.floors)
            {
                elevationByFloor[floor.floorId] = floor.elevation;
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

                var storeys = Mathf.Max(0, floor.order);
                foreach (var window in floor.windows)
                {
                    if (!window.canBeUsedForEscape || !TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out var center))
                    {
                        continue;
                    }

                    var risk = Mathf.Max(0.01f, window.escapeRiskMultiplier);
                    AddNode(new RouteNode
                    {
                        nodeId = WindowId(window.windowId),
                        floorId = floor.floorId,
                        localPosition = center,
                        objectId = window.windowId,
                        isEscapeWindow = true,
                        escapeRouteCost = Mathf.Max(0f, window.escapeCost) + (storeys * StoreyRoutingPenalty * risk),
                        windowInjury = Mathf.Clamp01(storeys * StoreyInjury * risk),
                        windowFatal = storeys >= FatalStoreys
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
            var reverse = new Dictionary<string, List<(string node, float weight)>>(StringComparer.Ordinal);
            void AddReverse(string from, string to, float weight)
            {
                if (!reverse.TryGetValue(to, out var list))
                {
                    list = new List<(string, float)>();
                    reverse[to] = list;
                }

                list.Add((from, weight));
            }

            foreach (var node in nodesById.Values)
            {
                if (node.isExit)
                {
                    // forward: exit -> SAFE (0)
                    AddReverse(node.nodeId, SafeNodeId, 0f);
                }
                else if (node.isEscapeWindow)
                {
                    // forward: escape window -> SAFE (penalty cost), so agents prefer real exits.
                    AddReverse(node.nodeId, SafeNodeId, node.escapeRouteCost);
                }
                else if (!string.IsNullOrWhiteSpace(node.linkedNodeId) && nodesById.ContainsKey(node.linkedNodeId))
                {
                    // forward: portal -> linked (travelCost)
                    AddReverse(node.nodeId, node.linkedNodeId, node.portalTravelCost);
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
                        AddReverse(list[i].nodeId, list[j].nodeId, distance);
                        AddReverse(list[j].nodeId, list[i].nodeId, distance);
                    }
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
                    if (!dist.TryGetValue(neighbors[i].node, out var existing) || candidate < existing)
                    {
                        dist[neighbors[i].node] = candidate;
                    }
                }
            }

            foreach (var node in nodesById.Values)
            {
                node.costToSafe = dist.TryGetValue(node.nodeId, out var d) ? d : float.PositiveInfinity;
            }
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
    }
}
