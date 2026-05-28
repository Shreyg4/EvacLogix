using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxRoomOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private Color roomFillColor = new(0.25f, 0.95f, 0.45f, 0.18f);
        [SerializeField] private Color roomOutlineColor = new(0.35f, 1f, 0.55f, 0.9f);
        [SerializeField] private Color penetratedCueColor = new(1f, 0.9f, 0.25f, 0.95f);
        [SerializeField] private float outlineWidth = 0.045f;
        [SerializeField] private float penetratedCueRadius = 0.18f;

        private readonly List<GameObject> renderedObjects = new();
        private SandboxRoomDetectionService roomDetectionService;
        private SandboxProjectWorkspaceService workspaceService;

        private void Awake()
        {
            roomDetectionService = FindAnyObjectByType<SandboxRoomDetectionService>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();

            if (roomDetectionService != null)
            {
                roomDetectionService.RoomsChanged += HandleRoomsChanged;
            }

            if (workspaceService != null)
            {
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (roomDetectionService != null)
            {
                roomDetectionService.RoomsChanged -= HandleRoomsChanged;
            }

            if (workspaceService != null)
            {
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
            }
        }

        public void Refresh()
        {
            Clear();

            if (roomDetectionService?.ShowCompleteRooms != true)
            {
                return;
            }

            var floorId = workspaceService?.ActiveFloorId;
            if (string.IsNullOrWhiteSpace(floorId))
            {
                return;
            }

            foreach (var room in roomDetectionService.GetRoomsForFloor(floorId))
            {
                RenderRoom(room);
            }
        }

        private void HandleRoomsChanged(IReadOnlyList<SandboxDetectedRoomData> rooms)
        {
            Refresh();
        }

        private void HandleFloorChanged(FloorData floor)
        {
            Refresh();
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            Refresh();
        }

        private void RenderRoom(SandboxDetectedRoomData room)
        {
            if (room?.polygonPoints == null || room.polygonPoints.Count < 3)
            {
                return;
            }

            RenderFill($"RoomFill_{room.roomId}", room.polygonPoints, roomFillColor);
            RenderOutline($"RoomOutline_{room.roomId}", room.polygonPoints, roomOutlineColor);
            if (room.hasIntentionalOpenings)
            {
                var openingPositions = room.openingPositions.Count > 0
                    ? room.openingPositions
                    : new List<Vector2> { ComputeCentroid(room.polygonPoints) };
                for (var i = 0; i < openingPositions.Count; i += 1)
                {
                    RenderPenetratedCue($"RoomOpeningCue_{room.roomId}_{i:D2}", openingPositions[i]);
                }
            }
        }

        private void RenderFill(string name, IReadOnlyList<Vector2> points, Color color)
        {
            var triangles = Triangulate(points);
            if (triangles.Count < 3)
            {
                return;
            }

            var fillObject = new GameObject(name);
            fillObject.transform.SetParent(transform, false);
            var meshFilter = fillObject.AddComponent<MeshFilter>();
            var meshRenderer = fillObject.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = $"{name}_Mesh" };
            mesh.SetVertices(points.Select(point => new Vector3(point.x, point.y, 0.02f)).ToList());
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = CreateMaterial(color);
            renderedObjects.Add(fillObject);
        }

        private void RenderOutline(string name, IReadOnlyList<Vector2> points, Color color)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = true;
            lineRenderer.positionCount = points.Count;
            lineRenderer.material = CreateMaterial(color);
            lineRenderer.widthMultiplier = outlineWidth;
            lineRenderer.startColor = color;
            lineRenderer.endColor = color;
            for (var i = 0; i < points.Count; i += 1)
            {
                lineRenderer.SetPosition(i, new Vector3(points[i].x, points[i].y, 0.03f));
            }

            renderedObjects.Add(lineObject);
        }

        private void RenderPenetratedCue(string name, Vector2 center)
        {
            var points = new[]
            {
                center + new Vector2(0f, penetratedCueRadius),
                center + new Vector2(penetratedCueRadius, 0f),
                center + new Vector2(0f, -penetratedCueRadius),
                center + new Vector2(-penetratedCueRadius, 0f)
            };
            RenderOutline(name, points, penetratedCueColor);
        }

        private static Material CreateMaterial(Color color)
        {
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            return material;
        }

        private static Vector2 ComputeCentroid(IReadOnlyList<Vector2> points)
        {
            var sum = Vector2.zero;
            for (var i = 0; i < points.Count; i += 1)
            {
                sum += points[i];
            }

            return sum / Mathf.Max(1, points.Count);
        }

        private static List<int> Triangulate(IReadOnlyList<Vector2> points)
        {
            var indices = Enumerable.Range(0, points.Count).ToList();
            var triangles = new List<int>();
            if (SignedArea(points) < 0f)
            {
                indices.Reverse();
            }

            var guard = 0;
            while (indices.Count > 3 && guard < 2048)
            {
                guard += 1;
                var didClip = false;
                for (var i = 0; i < indices.Count; i += 1)
                {
                    var previousIndex = indices[(i - 1 + indices.Count) % indices.Count];
                    var currentIndex = indices[i];
                    var nextIndex = indices[(i + 1) % indices.Count];
                    var previous = points[previousIndex];
                    var current = points[currentIndex];
                    var next = points[nextIndex];
                    if (Cross(current - previous, next - current) <= 0f)
                    {
                        continue;
                    }

                    if (ContainsAnyPoint(points, indices, previousIndex, currentIndex, nextIndex))
                    {
                        continue;
                    }

                    triangles.Add(previousIndex);
                    triangles.Add(currentIndex);
                    triangles.Add(nextIndex);
                    indices.RemoveAt(i);
                    didClip = true;
                    break;
                }

                if (!didClip)
                {
                    break;
                }
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }

            return triangles;
        }

        private static bool ContainsAnyPoint(
            IReadOnlyList<Vector2> points,
            IEnumerable<int> indices,
            int first,
            int second,
            int third)
        {
            foreach (var index in indices)
            {
                if (index == first || index == second || index == third)
                {
                    continue;
                }

                if (PointInTriangle(points[index], points[first], points[second], points[third]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var ab = Cross(b - a, point - a);
            var bc = Cross(c - b, point - b);
            var ca = Cross(a - c, point - c);
            return ab >= -0.0001f && bc >= -0.0001f && ca >= -0.0001f;
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

        private static float Cross(Vector2 left, Vector2 right)
        {
            return (left.x * right.y) - (left.y * right.x);
        }

        private void Clear()
        {
            for (var i = 0; i < renderedObjects.Count; i += 1)
            {
                if (renderedObjects[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(renderedObjects[i]);
                }
                else
                {
                    DestroyImmediate(renderedObjects[i]);
                }
            }

            renderedObjects.Clear();
        }
    }
}
