using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // One floor's placement in the simulation grid. OriginOffset is added to any floor-local point
    // to get its world position in the laid-out building, so floors never overlap:
    //   worldPoint = localPoint + OriginOffset
    public readonly struct SandboxFloorPlacement
    {
        public SandboxFloorPlacement(string floorId, int order, Vector2 originOffset, Rect localBounds)
        {
            FloorId = floorId;
            Order = order;
            OriginOffset = originOffset;
            LocalBounds = localBounds;
        }

        public string FloorId { get; }
        public int Order { get; }
        public Vector2 OriginOffset { get; }
        public Rect LocalBounds { get; }

        public Vector2 ToWorld(Vector2 localPoint) => localPoint + OriginOffset;
        public Rect WorldBounds => new(LocalBounds.position + OriginOffset, LocalBounds.size);
    }

    // Lays floors out left-to-right in a row-major grid (wrapping after a column count), reusing a
    // single uniform cell size so the grid is regular. Zoom/pan never reflows this — the layout is
    // computed once from the project. Pure, deterministic, and unit-testable; the MonoBehaviour just
    // caches the most recent computation for the simulation services to query.
    public sealed class SandboxFloorLayoutService : MonoBehaviour
    {
        [SerializeField] private int columnsOverride;
        [SerializeField] private float floorGap = 3f;

        private readonly Dictionary<string, SandboxFloorPlacement> placementsById = new(StringComparer.Ordinal);
        private readonly List<SandboxFloorPlacement> placements = new();

        public IReadOnlyList<SandboxFloorPlacement> Placements => placements;
        public int Columns { get; private set; }
        public Rect OverallWorldBounds { get; private set; }

        public int ColumnsOverride
        {
            get => columnsOverride;
            set => columnsOverride = Mathf.Max(0, value);
        }

        public float FloorGap
        {
            get => floorGap;
            set => floorGap = Mathf.Max(0f, value);
        }

        public bool TryGetPlacement(string floorId, out SandboxFloorPlacement placement)
        {
            placement = default;
            return !string.IsNullOrWhiteSpace(floorId) && placementsById.TryGetValue(floorId, out placement);
        }

        public Vector2 ToWorld(string floorId, Vector2 localPoint)
        {
            return TryGetPlacement(floorId, out var placement) ? placement.ToWorld(localPoint) : localPoint;
        }

        public void Rebuild(BuildingProjectData project)
        {
            placements.Clear();
            placementsById.Clear();
            Columns = 0;
            OverallWorldBounds = new Rect(0f, 0f, 0f, 0f);
            if (project?.floors == null || project.floors.Count == 0)
            {
                return;
            }

            var orderedFloors = new List<FloorData>(project.floors);
            orderedFloors.Sort((a, b) => a.order.CompareTo(b.order));

            // Uniform cell size = the largest floor bounds plus a gap, so every cell is identical and
            // the grid stays aligned regardless of differing floor footprints.
            var bounds = new List<Rect>(orderedFloors.Count);
            var maxWidth = 0f;
            var maxHeight = 0f;
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                var floorBounds = ComputeFloorLocalBounds(orderedFloors[i]);
                bounds.Add(floorBounds);
                maxWidth = Mathf.Max(maxWidth, floorBounds.width);
                maxHeight = Mathf.Max(maxHeight, floorBounds.height);
            }

            var cellWidth = maxWidth + floorGap;
            var cellHeight = maxHeight + floorGap;

            Columns = columnsOverride > 0
                ? columnsOverride
                : Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(orderedFloors.Count)));

            var hasOverall = false;
            var overall = new Rect();
            for (var i = 0; i < orderedFloors.Count; i += 1)
            {
                var column = i % Columns;
                var row = i / Columns;
                // Rows descend (negative Y) so floor "order 0" sits top-left and later floors flow down.
                var cellCenter = new Vector2(column * cellWidth, -row * cellHeight);
                var localBounds = bounds[i];
                var localCenter = localBounds.center;
                var originOffset = cellCenter - localCenter;

                var placement = new SandboxFloorPlacement(orderedFloors[i].floorId, orderedFloors[i].order, originOffset, localBounds);
                placements.Add(placement);
                if (!string.IsNullOrWhiteSpace(placement.FloorId))
                {
                    placementsById[placement.FloorId] = placement;
                }

                var worldBounds = placement.WorldBounds;
                if (!hasOverall)
                {
                    overall = worldBounds;
                    hasOverall = true;
                }
                else
                {
                    overall = Encapsulate(overall, worldBounds);
                }
            }

            OverallWorldBounds = overall;
        }

        // AABB over a floor's geometry in its own local coordinates. Falls back to a unit cell for an
        // empty floor so it still gets a slot in the grid.
        public static Rect ComputeFloorLocalBounds(FloorData floor)
        {
            var hasPoint = false;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            void Include(Vector2 point)
            {
                hasPoint = true;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            void IncludeRect(Vector2 center, Vector2 size)
            {
                var half = size * 0.5f;
                Include(center - half);
                Include(center + half);
            }

            if (floor != null)
            {
                foreach (var junction in floor.wallJunctions)
                {
                    Include(junction.position);
                }

                foreach (var wall in floor.wallSegments)
                {
                    Include(wall.startPoint);
                    Include(wall.endPoint);
                }

                foreach (var exit in floor.exits)
                {
                    IncludeRect(exit.center, exit.size);
                }

                foreach (var obstacle in floor.obstacles)
                {
                    IncludeRect(obstacle.center, obstacle.size);
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    IncludeRect(stairPortal.localPosition, stairPortal.size);
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    IncludeRect(teleportPortal.localPosition, teleportPortal.size);
                }
            }

            if (!hasPoint)
            {
                return new Rect(-0.5f, -0.5f, 1f, 1f);
            }

            // Pad slightly so walls on the boundary are not flush against the grid gap.
            min -= new Vector2(0.5f, 0.5f);
            max += new Vector2(0.5f, 0.5f);
            return new Rect(min, max - min);
        }

        private static Rect Encapsulate(Rect a, Rect b)
        {
            var minX = Mathf.Min(a.xMin, b.xMin);
            var minY = Mathf.Min(a.yMin, b.yMin);
            var maxX = Mathf.Max(a.xMax, b.xMax);
            var maxY = Mathf.Max(a.yMax, b.yMax);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
}
