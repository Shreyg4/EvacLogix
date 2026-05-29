using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxColliderRebuildService : MonoBehaviour
    {
        [SerializeField] private int rebuildRequestCount;
        [SerializeField] private int incrementalRebuildCount;
        [SerializeField] private int fullRebuildCount;
        [SerializeField] private string colliderRootName = "ColliderRoot";
        [SerializeField] private List<SandboxGeneratedColliderData> generatedColliders = new();
        [SerializeField] private string lastRebuiltFloorId = string.Empty;
        [SerializeField] private bool lastRebuildWasFull;

        private readonly Dictionary<string, GameObject> colliderObjectsById = new(StringComparer.Ordinal);
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;

        public event Action<int> RebuildRequested;
        public event Action<IReadOnlyList<SandboxGeneratedColliderData>, bool, string> CollidersRebuilt;

        public int RebuildRequestCount => rebuildRequestCount;
        public int IncrementalRebuildCount => incrementalRebuildCount;
        public int FullRebuildCount => fullRebuildCount;
        public string LastRebuiltFloorId => lastRebuiltFloorId;
        public bool LastRebuildWasFull => lastRebuildWasFull;
        public IReadOnlyList<SandboxGeneratedColliderData> GeneratedColliders => generatedColliders;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Start()
        {
            // The installer can add this service before the workspace service exists,
            // so Awake-time GetComponent calls may resolve to null. Re-resolve once all
            // sibling services are present.
            ResolveDependencies();
        }

        private void ResolveDependencies()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            workspaceStateService ??= GetComponent<SandboxWorkspaceStateService>();
        }

        public void RequestRebuild()
        {
            ResolveDependencies();
            RequestRebuild(workspaceService?.ActiveFloorId, false);
        }

        public void RequestRebuild(string floorId, bool fullRebuild = false)
        {
            ResolveDependencies();
            rebuildRequestCount += 1;
            RebuildRequested?.Invoke(rebuildRequestCount);

            if (fullRebuild || string.IsNullOrWhiteSpace(floorId))
            {
                RebuildAll();
                return;
            }

            IncrementalRebuild(floorId);
        }

        public void RebuildAll()
        {
            ResolveDependencies();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                generatedColliders = new List<SandboxGeneratedColliderData>();
                ClearRuntimeObjects(null);
                return;
            }

            var nextColliders = new List<SandboxGeneratedColliderData>();
            for (var i = 0; i < project.floors.Count; i += 1)
            {
                nextColliders.AddRange(BuildCollidersForFloor(project, project.floors[i]));
            }

            generatedColliders = nextColliders
                .OrderBy(collider => collider.floorId, StringComparer.Ordinal)
                .ThenBy(collider => collider.sourceWallSegmentId, StringComparer.Ordinal)
                .ToList();

            fullRebuildCount += 1;
            lastRebuildWasFull = true;
            lastRebuiltFloorId = string.Empty;
            SyncRuntimeObjects();
            CollidersRebuilt?.Invoke(generatedColliders, true, string.Empty);
        }

        public void ResetCounter()
        {
            rebuildRequestCount = 0;
            incrementalRebuildCount = 0;
            fullRebuildCount = 0;
        }

        private void IncrementalRebuild(string floorId)
        {
            var floor = workspaceService?.FindFloor(floorId);
            if (floor == null)
            {
                RebuildAll();
                return;
            }

            var staleColliderIds = generatedColliders
                .Where(collider => string.Equals(collider.floorId, floorId, StringComparison.Ordinal))
                .Select(collider => collider.colliderId)
                .ToArray();

            generatedColliders.RemoveAll(collider => string.Equals(collider.floorId, floorId, StringComparison.Ordinal));
            generatedColliders.AddRange(BuildCollidersForFloor(workspaceService?.ActiveProject, floor));
            generatedColliders = generatedColliders
                .OrderBy(collider => collider.floorId, StringComparer.Ordinal)
                .ThenBy(collider => collider.sourceWallSegmentId, StringComparer.Ordinal)
                .ToList();

            incrementalRebuildCount += 1;
            lastRebuildWasFull = false;
            lastRebuiltFloorId = floorId;
            ClearRuntimeObjects(staleColliderIds);
            SyncRuntimeObjects();
            CollidersRebuilt?.Invoke(generatedColliders, false, floorId);
        }

        private IEnumerable<SandboxGeneratedColliderData> BuildCollidersForFloor(BuildingProjectData project, FloorData floor)
        {
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                var length = Vector2.Distance(wall.startPoint, wall.endPoint);
                if (length <= 0.0001f)
                {
                    continue;
                }

                var wallDirection = (wall.endPoint - wall.startPoint).normalized;
                var rotationDegrees = Mathf.Atan2(wall.endPoint.y - wall.startPoint.y, wall.endPoint.x - wall.startPoint.x) * Mathf.Rad2Deg;
                var blockedSpans = BuildBlockedWallSpans(project, floor, wall, length);

                for (var spanIndex = 0; spanIndex < blockedSpans.Count; spanIndex += 1)
                {
                    var span = blockedSpans[spanIndex];
                    var spanLength = span.end - span.start;
                    if (spanLength <= 0.01f)
                    {
                        continue;
                    }

                    var center = wall.startPoint + wallDirection * ((span.start + span.end) * 0.5f);
                    yield return new SandboxGeneratedColliderData
                    {
                        colliderId = $"wall-collider-{floor.floorId}-{i:D4}-{wall.wallSegmentId}-{spanIndex:D2}",
                        floorId = floor.floorId,
                        sourceWallSegmentId = wall.wallSegmentId,
                        center = center,
                        size = new Vector2(Mathf.Max(0.01f, spanLength), Mathf.Max(0.01f, wall.thickness)),
                        rotationDegrees = rotationDegrees
                    };
                }
            }
        }

        private List<WallSpan> BuildBlockedWallSpans(BuildingProjectData project, FloorData floor, WallSegmentData wall, float wallLength)
        {
            var openings = new List<WallSpan>();

            foreach (var door in (floor.doors ?? Enumerable.Empty<DoorData>()).Where(door =>
                         string.Equals(door.wallSegmentId, wall.wallSegmentId, StringComparison.Ordinal) &&
                         IsDoorTraversable(door)))
            {
                openings.Add(CreateOpeningSpan(floor, project, door.offsetAlongWall, door.width, wallLength));
            }

            foreach (var window in (floor.windows ?? Enumerable.Empty<WindowData>()).Where(window =>
                         string.Equals(window.wallSegmentId, wall.wallSegmentId, StringComparison.Ordinal) &&
                         window.canBeUsedForEscape))
            {
                openings.Add(CreateOpeningSpan(floor, project, window.offsetAlongWall, window.width, wallLength));
            }

            openings = MergeSpans(openings.OrderBy(span => span.start).ToList());
            var blocked = new List<WallSpan>();
            var cursor = 0f;
            foreach (var opening in openings)
            {
                if (opening.start > cursor)
                {
                    blocked.Add(new WallSpan(cursor, opening.start));
                }

                cursor = Mathf.Max(cursor, opening.end);
            }

            if (cursor < wallLength)
            {
                blocked.Add(new WallSpan(cursor, wallLength));
            }

            return blocked;
        }

        private static bool IsDoorTraversable(DoorData door)
        {
            return door.state == DoorState.Normal ||
                   door.state == DoorState.Closed ||
                   door.state == DoorState.OneWay;
        }

        private WallSpan CreateOpeningSpan(FloorData floor, BuildingProjectData project, float offsetAlongWall, float width, float wallLength)
        {
            var worldWidth = SandboxOpeningWidthUtility.ResolveWorldWidth(
                project,
                floor,
                width,
                workspaceStateService != null ? workspaceStateService.GridSize : 0.5f);
            var halfWidth = Mathf.Max(0f, worldWidth) * 0.5f;
            return new WallSpan(
                Mathf.Clamp(offsetAlongWall - halfWidth, 0f, wallLength),
                Mathf.Clamp(offsetAlongWall + halfWidth, 0f, wallLength));
        }

        private static List<WallSpan> MergeSpans(IReadOnlyList<WallSpan> spans)
        {
            var merged = new List<WallSpan>();
            foreach (var span in spans)
            {
                if (span.end <= span.start)
                {
                    continue;
                }

                var lastIndex = merged.Count - 1;
                if (merged.Count == 0 || span.start > merged[lastIndex].end)
                {
                    merged.Add(span);
                    continue;
                }

                merged[lastIndex] = new WallSpan(merged[lastIndex].start, Mathf.Max(merged[lastIndex].end, span.end));
            }

            return merged;
        }

        private void SyncRuntimeObjects()
        {
            var root = FindColliderRoot();
            if (root == null)
            {
                return;
            }

            for (var i = 0; i < generatedColliders.Count; i += 1)
            {
                var colliderData = generatedColliders[i];
                if (!colliderObjectsById.TryGetValue(colliderData.colliderId, out var colliderObject) || colliderObject == null)
                {
                    colliderObject = new GameObject(colliderData.colliderId);
                    colliderObject.transform.SetParent(root.transform, false);
                    colliderObjectsById[colliderData.colliderId] = colliderObject;
                }

                colliderObject.transform.SetPositionAndRotation(
                    new Vector3(colliderData.center.x, colliderData.center.y, 0f),
                    Quaternion.Euler(0f, 0f, colliderData.rotationDegrees));

                var boxCollider = colliderObject.GetComponent<BoxCollider2D>();
                if (boxCollider == null)
                {
                    boxCollider = colliderObject.AddComponent<BoxCollider2D>();
                }

                boxCollider.size = colliderData.size;
            }

            var activeColliderIds = new HashSet<string>(generatedColliders.Select(collider => collider.colliderId), StringComparer.Ordinal);
            var staleColliderIds = colliderObjectsById.Keys.Where(id => !activeColliderIds.Contains(id)).ToArray();
            for (var i = 0; i < staleColliderIds.Length; i += 1)
            {
                if (colliderObjectsById.TryGetValue(staleColliderIds[i], out var staleObject) && staleObject != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(staleObject);
                    }
                    else
                    {
                        DestroyImmediate(staleObject);
                    }
                }

                colliderObjectsById.Remove(staleColliderIds[i]);
            }
        }

        private void ClearRuntimeObjects(IEnumerable<string> colliderIds)
        {
            if (colliderIds == null)
            {
                var allKeys = colliderObjectsById.Keys.ToArray();
                for (var i = 0; i < allKeys.Length; i += 1)
                {
                    if (colliderObjectsById.TryGetValue(allKeys[i], out var colliderObject) && colliderObject != null)
                    {
                        if (Application.isPlaying)
                        {
                            Destroy(colliderObject);
                        }
                        else
                        {
                            DestroyImmediate(colliderObject);
                        }
                    }
                }

                colliderObjectsById.Clear();
                return;
            }

            var runtimeIds = colliderIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Where(id => colliderObjectsById.ContainsKey(id))
                .ToArray();
            for (var i = 0; i < runtimeIds.Length; i += 1)
            {
                if (colliderObjectsById.TryGetValue(runtimeIds[i], out var colliderObject) && colliderObject != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(colliderObject);
                    }
                    else
                    {
                        DestroyImmediate(colliderObject);
                    }
                }

                colliderObjectsById.Remove(runtimeIds[i]);
            }
        }

        private GameObject FindColliderRoot()
        {
            return GameObject.Find(colliderRootName);
        }

        private readonly struct WallSpan
        {
            public WallSpan(float start, float end)
            {
                this.start = start;
                this.end = end;
            }

            public readonly float start;
            public readonly float end;
        }
    }
}
