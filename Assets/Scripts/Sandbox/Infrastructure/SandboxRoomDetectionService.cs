using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxRoomDetectionService : MonoBehaviour
    {
        private const float MinimumRoomArea = 0.05f;
        private const float GeometryTolerance = 0.001f;

        [SerializeField] private bool showCompleteRooms;
        [SerializeField] private List<SandboxDetectedRoomData> detectedRooms = new();
        [SerializeField] private string lastStatusMessage = "Room overlay is off.";

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;

        public event Action<IReadOnlyList<SandboxDetectedRoomData>> RoomsChanged;

        public bool ShowCompleteRooms => showCompleteRooms;
        public IReadOnlyList<SandboxDetectedRoomData> DetectedRooms => detectedRooms;
        public string LastStatusMessage => lastStatusMessage;
        public int SealedRoomCount => detectedRooms.Count(room => !room.hasIntentionalOpenings);
        public int PenetratedRoomCount => detectedRooms.Count(room => room.hasIntentionalOpenings);

        private void Awake()
        {
            RefreshDependencies();
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
            }

            if (wallAuthoringService != null)
            {
                wallAuthoringService.TopologyChanged += HandleEditableGeometryChanged;
            }

            if (semanticObjectAuthoringService != null)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged += HandleEditableGeometryChanged;
            }
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            if (wallAuthoringService != null)
            {
                wallAuthoringService.TopologyChanged -= HandleEditableGeometryChanged;
            }

            if (semanticObjectAuthoringService != null)
            {
                semanticObjectAuthoringService.SemanticObjectsChanged -= HandleEditableGeometryChanged;
            }
        }

        public void SetShowCompleteRooms(bool enabled)
        {
            if (showCompleteRooms == enabled)
            {
                if (enabled)
                {
                    Recalculate();
                }
                return;
            }

            showCompleteRooms = enabled;
            if (showCompleteRooms)
            {
                Recalculate();
                return;
            }

            detectedRooms = new List<SandboxDetectedRoomData>();
            lastStatusMessage = "Room overlay is off.";
            RoomsChanged?.Invoke(detectedRooms);
        }

        public void Recalculate()
        {
            RefreshDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                detectedRooms = new List<SandboxDetectedRoomData>();
                lastStatusMessage = "Create or import a project before detecting rooms.";
                RoomsChanged?.Invoke(detectedRooms);
                return;
            }

            detectedRooms = (project.floors ?? Enumerable.Empty<FloorData>())
                .SelectMany(floor => DetectRoomsForFloor(floor))
                .OrderBy(room => room.floorId, StringComparer.Ordinal)
                .ThenByDescending(room => room.area)
                .ToList();

            lastStatusMessage = detectedRooms.Count == 0
                ? "No complete rooms detected."
                : $"Detected {detectedRooms.Count} complete rooms ({SealedRoomCount} sealed, {PenetratedRoomCount} penetrated).";
            RoomsChanged?.Invoke(detectedRooms);
        }

        public IReadOnlyList<SandboxDetectedRoomData> GetRoomsForFloor(string floorId)
        {
            if (string.IsNullOrWhiteSpace(floorId))
            {
                return Array.Empty<SandboxDetectedRoomData>();
            }

            return detectedRooms
                .Where(room => string.Equals(room.floorId, floorId, StringComparison.Ordinal))
                .ToArray();
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            if (showCompleteRooms)
            {
                Recalculate();
            }
        }

        private void HandleFloorChanged(FloorData floor)
        {
            if (showCompleteRooms)
            {
                RoomsChanged?.Invoke(detectedRooms);
            }
        }

        private void HandleEditableGeometryChanged()
        {
            if (showCompleteRooms)
            {
                Recalculate();
            }
        }

        private IEnumerable<SandboxDetectedRoomData> DetectRoomsForFloor(FloorData floor)
        {
            if (floor == null ||
                floor.wallSegments == null ||
                floor.wallSegments.Count < 3)
            {
                yield break;
            }

            var graph = BuildVirtualWallGraph(floor.wallSegments);
            if (graph.neighbors.Count < 3)
            {
                yield break;
            }

            foreach (var entry in graph.neighbors)
            {
                entry.Value.Sort((left, right) =>
                    GetAngle(graph.nodePositions[entry.Key], graph.nodePositions[left])
                        .CompareTo(GetAngle(graph.nodePositions[entry.Key], graph.nodePositions[right])));
            }

            var visitedDirectedEdges = new HashSet<string>(StringComparer.Ordinal);
            var emittedCycles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var from in graph.neighbors.Keys.OrderBy(id => id, StringComparer.Ordinal))
            {
                foreach (var to in graph.neighbors[from])
                {
                    var loop = TraceFace(from, to, graph.neighbors, graph.nodePositions, graph.wallsByEndpoint, visitedDirectedEdges);
                    if (loop == null || loop.junctionIds.Count < 3)
                    {
                        continue;
                    }

                    var area = SignedArea(loop.points);
                    if (area <= MinimumRoomArea || !IsSimplePolygon(loop.points))
                    {
                        continue;
                    }

                    var cycleKey = CreateCycleKey(loop.junctionIds);
                    if (!emittedCycles.Add(cycleKey))
                    {
                        continue;
                    }

                    var boundaryWallIds = loop.wallIds.Distinct(StringComparer.Ordinal).ToList();
                    var openings = FindOpeningsOnWalls(floor, boundaryWallIds);
                    yield return new SandboxDetectedRoomData
                    {
                        roomId = $"room-{floor.floorId}-{emittedCycles.Count:D3}",
                        floorId = floor.floorId,
                        polygonPoints = loop.points,
                        boundaryWallSegmentIds = boundaryWallIds,
                        openingObjectIds = openings.Select(opening => opening.objectId).ToList(),
                        openingPositions = openings.Select(opening => opening.position).ToList(),
                        hasIntentionalOpenings = openings.Count > 0,
                        area = area
                    };
                }
            }
        }

        private void RefreshDependencies()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            wallAuthoringService ??= GetComponent<SandboxWallAuthoringService>();
            semanticObjectAuthoringService ??= GetComponent<SandboxSemanticObjectAuthoringService>();
        }

        private static void AddNeighbor(IDictionary<string, List<string>> neighbors, string from, string to)
        {
            if (!neighbors.TryGetValue(from, out var list))
            {
                list = new List<string>();
                neighbors[from] = list;
            }

            if (!list.Contains(to))
            {
                list.Add(to);
            }
        }

        private static VirtualWallGraph BuildVirtualWallGraph(IReadOnlyList<WallSegmentData> walls)
        {
            var splitPointsByWallId = new Dictionary<string, List<Vector2>>(StringComparer.Ordinal);
            foreach (var wall in walls)
            {
                if (wall == null ||
                    string.IsNullOrWhiteSpace(wall.wallSegmentId) ||
                    (wall.endPoint - wall.startPoint).sqrMagnitude <= GeometryTolerance * GeometryTolerance)
                {
                    continue;
                }

                splitPointsByWallId[wall.wallSegmentId] = new List<Vector2> { wall.startPoint, wall.endPoint };
            }

            for (var leftIndex = 0; leftIndex < walls.Count; leftIndex += 1)
            {
                var left = walls[leftIndex];
                if (left == null || !splitPointsByWallId.ContainsKey(left.wallSegmentId))
                {
                    continue;
                }

                for (var rightIndex = leftIndex + 1; rightIndex < walls.Count; rightIndex += 1)
                {
                    var right = walls[rightIndex];
                    if (right == null || !splitPointsByWallId.ContainsKey(right.wallSegmentId))
                    {
                        continue;
                    }

                    if (TryGetSegmentIntersection(left.startPoint, left.endPoint, right.startPoint, right.endPoint, out var intersection))
                    {
                        AddDistinctPoint(splitPointsByWallId[left.wallSegmentId], intersection);
                        AddDistinctPoint(splitPointsByWallId[right.wallSegmentId], intersection);
                    }
                }
            }

            var nodePositions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            var neighbors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var wallsByEndpoint = new Dictionary<(string from, string to), WallSegmentData>();

            foreach (var wall in walls)
            {
                if (wall == null || !splitPointsByWallId.TryGetValue(wall.wallSegmentId, out var splitPoints))
                {
                    continue;
                }

                var direction = wall.endPoint - wall.startPoint;
                var lengthSquared = direction.sqrMagnitude;
                if (lengthSquared <= GeometryTolerance * GeometryTolerance)
                {
                    continue;
                }

                var orderedPoints = splitPoints
                    .OrderBy(point => Vector2.Dot(point - wall.startPoint, direction) / lengthSquared)
                    .ToList();

                for (var i = 0; i < orderedPoints.Count - 1; i += 1)
                {
                    var start = orderedPoints[i];
                    var end = orderedPoints[i + 1];
                    if ((end - start).sqrMagnitude <= GeometryTolerance * GeometryTolerance)
                    {
                        continue;
                    }

                    var startNodeId = CreateNodeKey(start);
                    var endNodeId = CreateNodeKey(end);
                    nodePositions[startNodeId] = start;
                    nodePositions[endNodeId] = end;
                    AddNeighbor(neighbors, startNodeId, endNodeId);
                    AddNeighbor(neighbors, endNodeId, startNodeId);
                    wallsByEndpoint[(startNodeId, endNodeId)] = wall;
                    wallsByEndpoint[(endNodeId, startNodeId)] = wall;
                }
            }

            return new VirtualWallGraph(nodePositions, neighbors, wallsByEndpoint);
        }

        private static void AddDistinctPoint(ICollection<Vector2> points, Vector2 point)
        {
            if (points.Any(existing => Vector2.Distance(existing, point) <= GeometryTolerance))
            {
                return;
            }

            points.Add(point);
        }

        private static string CreateNodeKey(Vector2 point)
        {
            return $"{Mathf.RoundToInt(point.x / GeometryTolerance)}:{Mathf.RoundToInt(point.y / GeometryTolerance)}";
        }

        private static float GetAngle(Vector2 origin, Vector2 target)
        {
            return Mathf.Atan2(target.y - origin.y, target.x - origin.x);
        }

        private static RoomTraceResult TraceFace(
            string startFrom,
            string startTo,
            IReadOnlyDictionary<string, List<string>> neighbors,
            IReadOnlyDictionary<string, Vector2> nodePositions,
            IReadOnlyDictionary<(string from, string to), WallSegmentData> wallsByEndpoint,
            ISet<string> visitedDirectedEdges)
        {
            var from = startFrom;
            var to = startTo;
            var junctionIds = new List<string>();
            var points = new List<Vector2>();
            var wallIds = new List<string>();

            for (var guard = 0; guard < 2048; guard += 1)
            {
                var edgeKey = CreateDirectedEdgeKey(from, to);
                if (visitedDirectedEdges.Contains(edgeKey) && !(from == startFrom && to == startTo))
                {
                    return null;
                }

                visitedDirectedEdges.Add(edgeKey);
                junctionIds.Add(from);
                points.Add(nodePositions[from]);
                if (wallsByEndpoint.TryGetValue((from, to), out var wall))
                {
                    wallIds.Add(wall.wallSegmentId);
                }

                if (!neighbors.TryGetValue(to, out var nextNeighbors) || nextNeighbors.Count == 0)
                {
                    return null;
                }

                var incomingIndex = nextNeighbors.FindIndex(candidate => string.Equals(candidate, from, StringComparison.Ordinal));
                if (incomingIndex < 0)
                {
                    return null;
                }

                var nextIndex = (incomingIndex - 1 + nextNeighbors.Count) % nextNeighbors.Count;
                var next = nextNeighbors[nextIndex];
                from = to;
                to = next;

                if (from == startFrom && to == startTo)
                {
                    return new RoomTraceResult(junctionIds, points, wallIds);
                }
            }

            return null;
        }

        private static string CreateDirectedEdgeKey(string from, string to)
        {
            return $"{from}->{to}";
        }

        private static string CreateCycleKey(IReadOnlyList<string> junctionIds)
        {
            return string.Join("|", junctionIds.OrderBy(id => id, StringComparer.Ordinal));
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            var area = 0f;
            for (var i = 0; i < points.Count; i += 1)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                area += (current.x * next.y) - (next.x * current.y);
            }

            return area * 0.5f;
        }

        private static bool IsSimplePolygon(IReadOnlyList<Vector2> points)
        {
            for (var leftIndex = 0; leftIndex < points.Count; leftIndex += 1)
            {
                var leftStart = points[leftIndex];
                var leftEnd = points[(leftIndex + 1) % points.Count];
                for (var rightIndex = leftIndex + 1; rightIndex < points.Count; rightIndex += 1)
                {
                    if (Mathf.Abs(leftIndex - rightIndex) <= 1 ||
                        (leftIndex == 0 && rightIndex == points.Count - 1))
                    {
                        continue;
                    }

                    var rightStart = points[rightIndex];
                    var rightEnd = points[(rightIndex + 1) % points.Count];
                    if (SegmentsIntersect(leftStart, leftEnd, rightStart, rightEnd))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var denominator = Cross(b - a, d - c);
            if (Mathf.Abs(denominator) < 0.0001f)
            {
                return false;
            }

            var t = Cross(c - a, d - c) / denominator;
            var u = Cross(c - a, b - a) / denominator;
            return t > 0.0001f && t < 0.9999f && u > 0.0001f && u < 0.9999f;
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return (left.x * right.y) - (left.y * right.x);
        }

        private static bool TryGetSegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection)
        {
            intersection = Vector2.zero;
            var ab = b - a;
            var cd = d - c;
            var denominator = Cross(ab, cd);
            if (Mathf.Abs(denominator) < GeometryTolerance)
            {
                return false;
            }

            var t = Cross(c - a, cd) / denominator;
            var u = Cross(c - a, ab) / denominator;
            if (t < -GeometryTolerance || t > 1f + GeometryTolerance ||
                u < -GeometryTolerance || u > 1f + GeometryTolerance)
            {
                return false;
            }

            intersection = a + ab * Mathf.Clamp01(t);
            return true;
        }

        private static List<OpeningInfo> FindOpeningsOnWalls(FloorData floor, IReadOnlyCollection<string> wallSegmentIds)
        {
            var wallSet = new HashSet<string>(wallSegmentIds, StringComparer.Ordinal);
            var wallsById = floor.wallSegments
                .GroupBy(wall => wall.wallSegmentId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var openings = new List<OpeningInfo>();

            foreach (var door in (floor.doors ?? Enumerable.Empty<DoorData>()).Where(door => wallSet.Contains(door.wallSegmentId)))
            {
                if (TryResolveOpeningPosition(wallsById, door.wallSegmentId, door.offsetAlongWall, out var position))
                {
                    openings.Add(new OpeningInfo(door.doorId, position));
                }
            }

            foreach (var window in (floor.windows ?? Enumerable.Empty<WindowData>()).Where(window => wallSet.Contains(window.wallSegmentId)))
            {
                if (TryResolveOpeningPosition(wallsById, window.wallSegmentId, window.offsetAlongWall, out var position))
                {
                    openings.Add(new OpeningInfo(window.windowId, position));
                }
            }

            return openings;
        }

        private static bool TryResolveOpeningPosition(
            IReadOnlyDictionary<string, WallSegmentData> wallsById,
            string wallSegmentId,
            float offsetAlongWall,
            out Vector2 position)
        {
            position = Vector2.zero;
            if (!wallsById.TryGetValue(wallSegmentId, out var wall))
            {
                return false;
            }

            var direction = wall.endPoint - wall.startPoint;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            position = wall.startPoint + direction.normalized * Mathf.Clamp(offsetAlongWall, 0f, direction.magnitude);
            return true;
        }

        private sealed class RoomTraceResult
        {
            public RoomTraceResult(List<string> junctionIds, List<Vector2> points, List<string> wallIds)
            {
                this.junctionIds = junctionIds;
                this.points = points;
                this.wallIds = wallIds;
            }

            public readonly List<string> junctionIds;
            public readonly List<Vector2> points;
            public readonly List<string> wallIds;
        }

        private sealed class VirtualWallGraph
        {
            public VirtualWallGraph(
                Dictionary<string, Vector2> nodePositions,
                Dictionary<string, List<string>> neighbors,
                Dictionary<(string from, string to), WallSegmentData> wallsByEndpoint)
            {
                this.nodePositions = nodePositions;
                this.neighbors = neighbors;
                this.wallsByEndpoint = wallsByEndpoint;
            }

            public readonly Dictionary<string, Vector2> nodePositions;
            public readonly Dictionary<string, List<string>> neighbors;
            public readonly Dictionary<(string from, string to), WallSegmentData> wallsByEndpoint;
        }

        private readonly struct OpeningInfo
        {
            public OpeningInfo(string objectId, Vector2 position)
            {
                this.objectId = objectId;
                this.position = position;
            }

            public readonly string objectId;
            public readonly Vector2 position;
        }
    }
}
