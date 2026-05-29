using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    internal readonly struct SandboxAxisGuide
    {
        public SandboxAxisGuide(bool isValid, bool isVertical, float coordinate, float start, float end, bool isGrid, float distance)
        {
            IsValid = isValid;
            IsVertical = isVertical;
            Coordinate = coordinate;
            Start = start;
            End = end;
            IsGrid = isGrid;
            Distance = distance;
        }

        public bool IsValid { get; }
        public bool IsVertical { get; }
        public float Coordinate { get; }
        public float Start { get; }
        public float End { get; }
        public bool IsGrid { get; }
        public float Distance { get; }
    }

    internal static class SandboxAlignmentGuideUtility
    {
        private const float GuideMargin = 0.35f;

        public static void CollectFloorAxisReferences(FloorData floor, ICollection<float> xReferences, ICollection<float> yReferences, string ignoredObjectId = null)
        {
            if (floor == null || xReferences == null || yReferences == null)
            {
                return;
            }

            foreach (var junction in floor.wallJunctions)
            {
                xReferences.Add(junction.position.x);
                yReferences.Add(junction.position.y);
            }

            foreach (var exitZone in floor.exits)
            {
                if (string.Equals(exitZone.exitZoneId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(exitZone.center, exitZone.size, exitZone.rotationDegrees, xReferences, yReferences);
            }

            foreach (var obstacle in floor.obstacles)
            {
                if (string.Equals(obstacle.obstacleId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(obstacle.center, obstacle.size, obstacle.rotationDegrees, xReferences, yReferences);
            }

            foreach (var teleportPortal in floor.teleportPortals)
            {
                if (string.Equals(teleportPortal.teleportPortalId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees, xReferences, yReferences);
            }

            foreach (var stairPortal in floor.stairPortals)
            {
                if (string.Equals(stairPortal.stairPortalId, ignoredObjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                AddRectangleReferences(stairPortal.localPosition, stairPortal.size, stairPortal.rotationDegrees, xReferences, yReferences);
            }
        }

        public static SandboxAxisGuide ResolveBestVerticalGuide(float targetX, float minY, float maxY, IEnumerable<float> referenceXs, float gridSize, float tolerance)
        {
            return ResolveBestGuide(targetX, minY, maxY, true, referenceXs, gridSize, tolerance);
        }

        public static SandboxAxisGuide ResolveBestHorizontalGuide(float targetY, float minX, float maxX, IEnumerable<float> referenceYs, float gridSize, float tolerance)
        {
            return ResolveBestGuide(targetY, minX, maxX, false, referenceYs, gridSize, tolerance);
        }

        // Finds the strongest axis alignment across a set of candidate coordinates and returns the
        // offset needed to move the matched candidate exactly onto its reference. Used to snap a
        // dragged/placed object so it lands where its alignment guide shows.
        public static bool TryResolveAxisSnap(
            IReadOnlyList<float> candidates,
            IEnumerable<float> references,
            float gridSize,
            float tolerance,
            bool isVertical,
            out float snapOffset)
        {
            snapOffset = 0f;
            if (candidates == null)
            {
                return false;
            }

            var referenceList = references as IList<float> ?? references?.ToList();
            var resolved = false;
            var bestDistance = float.PositiveInfinity;
            for (var i = 0; i < candidates.Count; i += 1)
            {
                var guide = isVertical
                    ? ResolveBestVerticalGuide(candidates[i], 0f, 0f, referenceList, gridSize, tolerance)
                    : ResolveBestHorizontalGuide(candidates[i], 0f, 0f, referenceList, gridSize, tolerance);
                if (guide.IsValid && guide.Distance < bestDistance)
                {
                    bestDistance = guide.Distance;
                    snapOffset = guide.Coordinate - candidates[i];
                    resolved = true;
                }
            }

            return resolved;
        }

        public static string FormatSquaresAndDistance(float worldDistance, float gridSize, DistanceUnit distanceUnit, string worldFormat = "0.##")
        {
            var safeGridSize = Mathf.Max(0.05f, gridSize);
            var squares = worldDistance / safeGridSize;
            var formattedSquares = squares.ToString("0.##", CultureInfo.InvariantCulture);
            return $"{formattedSquares} sq ({SandboxDistanceUnitUtility.FormatDistance(worldDistance, distanceUnit, worldFormat)})";
        }

        public static string FormatAngle(float angleDegrees)
        {
            return $"{angleDegrees.ToString("0.#", CultureInfo.InvariantCulture)} deg";
        }

        public static Vector2 ToGuiPoint(Camera cameraComponent, Vector2 worldPoint)
        {
            if (cameraComponent == null)
            {
                return Vector2.zero;
            }

            var screenPoint = cameraComponent.WorldToScreenPoint(new Vector3(worldPoint.x, worldPoint.y, 0f));
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        // Gathers alignment reference coordinates from OTHER entities of the SAME type as the
        // object being manipulated, so guides only fire against "similar" entities.
        public static void CollectSameTypeAxisReferences(
            FloorData floor,
            SandboxVisualObjectType type,
            string ignoredObjectId,
            ICollection<float> xReferences,
            ICollection<float> yReferences)
        {
            if (floor == null || xReferences == null || yReferences == null)
            {
                return;
            }

            switch (type)
            {
                case SandboxVisualObjectType.Wall:
                    CollectWallAxisReferences(floor, xReferences, yReferences);
                    break;
                case SandboxVisualObjectType.Exit:
                    foreach (var exitZone in floor.exits)
                    {
                        if (string.Equals(exitZone.exitZoneId, ignoredObjectId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        AddRectangleReferences(exitZone.center, exitZone.size, exitZone.rotationDegrees, xReferences, yReferences);
                    }

                    break;
                case SandboxVisualObjectType.Obstacle:
                    foreach (var obstacle in floor.obstacles)
                    {
                        if (string.Equals(obstacle.obstacleId, ignoredObjectId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        AddRectangleReferences(obstacle.center, obstacle.size, obstacle.rotationDegrees, xReferences, yReferences);
                    }

                    break;
                case SandboxVisualObjectType.Teleport:
                    foreach (var teleportPortal in floor.teleportPortals)
                    {
                        if (string.Equals(teleportPortal.teleportPortalId, ignoredObjectId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        AddRectangleReferences(teleportPortal.localPosition, teleportPortal.size, teleportPortal.rotationDegrees, xReferences, yReferences);
                    }

                    break;
                case SandboxVisualObjectType.Door:
                    foreach (var door in floor.doors)
                    {
                        if (string.Equals(door.doorId, ignoredObjectId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (TryResolveOpeningCenter(floor, door.wallSegmentId, door.offsetAlongWall, out var center))
                        {
                            xReferences.Add(center.x);
                            yReferences.Add(center.y);
                        }
                    }

                    break;
                case SandboxVisualObjectType.Window:
                    foreach (var window in floor.windows)
                    {
                        if (string.Equals(window.windowId, ignoredObjectId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out var center))
                        {
                            xReferences.Add(center.x);
                            yReferences.Add(center.y);
                        }
                    }

                    break;
            }
        }

        // Gathers wall alignment references usable by ANY entity type: intersections/endpoints
        // (always), plus the centerline and both faces (centerline +/- thickness/2) for
        // axis-aligned walls. Diagonal walls have no single axis coordinate, so only their
        // endpoints contribute.
        public static void CollectWallAxisReferences(FloorData floor, ICollection<float> xReferences, ICollection<float> yReferences)
        {
            if (floor == null || xReferences == null || yReferences == null)
            {
                return;
            }

            foreach (var junction in floor.wallJunctions)
            {
                xReferences.Add(junction.position.x);
                yReferences.Add(junction.position.y);
            }

            foreach (var wall in floor.wallSegments)
            {
                xReferences.Add(wall.startPoint.x);
                xReferences.Add(wall.endPoint.x);
                yReferences.Add(wall.startPoint.y);
                yReferences.Add(wall.endPoint.y);

                var direction = wall.endPoint - wall.startPoint;
                var halfThickness = Mathf.Max(0f, wall.thickness * 0.5f);
                if (Mathf.Abs(direction.x) <= 0.01f && Mathf.Abs(direction.y) > 0.01f)
                {
                    // Vertical wall: its centerline and faces are constant-X lines.
                    var x = wall.startPoint.x;
                    xReferences.Add(x);
                    xReferences.Add(x - halfThickness);
                    xReferences.Add(x + halfThickness);
                }
                else if (Mathf.Abs(direction.y) <= 0.01f && Mathf.Abs(direction.x) > 0.01f)
                {
                    // Horizontal wall: its centerline and faces are constant-Y lines.
                    var y = wall.startPoint.y;
                    yReferences.Add(y);
                    yReferences.Add(y - halfThickness);
                    yReferences.Add(y + halfThickness);
                }
            }
        }

        // When an axis guide is active, finds the wall intersection (junction) lying on that guide
        // line nearest the point being worked, so the caller can draw the perpendicular guide
        // through it (a cross that pinpoints the intersection). Returns the perpendicular
        // coordinate of that junction.
        public static bool TryFindNearestJunctionPerpendicular(
            FloorData floor,
            bool guideIsVertical,
            float guideCoordinate,
            Vector2 nearPoint,
            float tolerance,
            out float perpendicularCoordinate)
        {
            perpendicularCoordinate = 0f;
            if (floor == null)
            {
                return false;
            }

            var found = false;
            var bestDistance = float.PositiveInfinity;
            foreach (var junction in floor.wallJunctions)
            {
                var axisCoordinate = guideIsVertical ? junction.position.x : junction.position.y;
                if (Mathf.Abs(axisCoordinate - guideCoordinate) > tolerance)
                {
                    continue;
                }

                var perpendicular = guideIsVertical ? junction.position.y : junction.position.x;
                var nearPerpendicular = guideIsVertical ? nearPoint.y : nearPoint.x;
                var distance = Mathf.Abs(perpendicular - nearPerpendicular);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    perpendicularCoordinate = perpendicular;
                    found = true;
                }
            }

            return found;
        }

        // Appends a rectangle's alignment candidate coordinates (min edge, center, max edge per
        // axis) so guides catch flush-edge and shared-centerline alignment, not just centers.
        public static void AppendRectangleCandidates(Vector2 center, Vector2 size, float rotationDegrees, ICollection<float> xCandidates, ICollection<float> yCandidates)
        {
            var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var corner in corners)
            {
                minX = Mathf.Min(minX, corner.x);
                maxX = Mathf.Max(maxX, corner.x);
                minY = Mathf.Min(minY, corner.y);
                maxY = Mathf.Max(maxY, corner.y);
            }

            xCandidates.Add(minX);
            xCandidates.Add(center.x);
            xCandidates.Add(maxX);
            yCandidates.Add(minY);
            yCandidates.Add(center.y);
            yCandidates.Add(maxY);
        }

        // Converts a screen-space pixel tolerance into world units for the current orthographic
        // zoom so the guide "magnetism" feels consistent regardless of zoom level.
        public static float PixelToleranceToWorld(Camera cameraComponent, float pixels)
        {
            if (cameraComponent == null || !cameraComponent.orthographic || Screen.height <= 0)
            {
                return 0.1f;
            }

            return pixels * (2f * cameraComponent.orthographicSize / Screen.height);
        }

        // Collects true intersection points: wall junctions PLUS points where two wall segments
        // cross mid-span. These are the "intersections" users align/snap to, distinct from grid.
        public static void CollectWallIntersectionPoints(FloorData floor, ICollection<Vector2> points)
        {
            if (floor == null || points == null)
            {
                return;
            }

            foreach (var junction in floor.wallJunctions)
            {
                points.Add(junction.position);
            }

            var walls = floor.wallSegments;
            for (var i = 0; i < walls.Count; i += 1)
            {
                for (var j = i + 1; j < walls.Count; j += 1)
                {
                    if (TrySegmentIntersection(walls[i].startPoint, walls[i].endPoint, walls[j].startPoint, walls[j].endPoint, out var crossing))
                    {
                        points.Add(crossing);
                    }
                }
            }
        }

        public static bool TryFindNearestIntersectionPoint(FloorData floor, Vector2 nearPoint, float maxDistance, out Vector2 point)
        {
            point = Vector2.zero;
            if (floor == null)
            {
                return false;
            }

            var found = false;
            var bestDistance = float.PositiveInfinity;

            foreach (var junction in floor.wallJunctions)
            {
                var distance = Vector2.Distance(junction.position, nearPoint);
                if (distance <= maxDistance && distance < bestDistance)
                {
                    bestDistance = distance;
                    point = junction.position;
                    found = true;
                }
            }

            var walls = floor.wallSegments;
            for (var i = 0; i < walls.Count; i += 1)
            {
                for (var j = i + 1; j < walls.Count; j += 1)
                {
                    if (!TrySegmentIntersection(walls[i].startPoint, walls[i].endPoint, walls[j].startPoint, walls[j].endPoint, out var crossing))
                    {
                        continue;
                    }

                    var distance = Vector2.Distance(crossing, nearPoint);
                    if (distance <= maxDistance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        point = crossing;
                        found = true;
                    }
                }
            }

            return found;
        }

        // Finds the nearest wall intersection (junction or segment crossing) whose X or Y lines up
        // with the cursor within tolerance — so an intersection you're aligned to shows from any
        // distance along the aligned axis, while non-aligned intersections are ignored.
        public static bool TryFindAxisAlignedIntersection(FloorData floor, Vector2 cursor, float tolerance, out Vector2 point)
        {
            point = Vector2.zero;
            if (floor == null)
            {
                return false;
            }

            var candidates = new List<Vector2>();
            CollectWallIntersectionPoints(floor, candidates);

            var found = false;
            var bestDistance = float.PositiveInfinity;
            foreach (var candidate in candidates)
            {
                var alignedOnAxis = Mathf.Abs(candidate.x - cursor.x) <= tolerance || Mathf.Abs(candidate.y - cursor.y) <= tolerance;
                if (!alignedOnAxis)
                {
                    continue;
                }

                var distance = Vector2.Distance(candidate, cursor);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    point = candidate;
                    found = true;
                }
            }

            return found;
        }

        private static bool TrySegmentIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 point)
        {
            point = Vector2.zero;
            var d1 = a2 - a1;
            var d2 = b2 - b1;
            var denominator = (d1.x * d2.y) - (d1.y * d2.x);
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return false;
            }

            var t = (((b1.x - a1.x) * d2.y) - ((b1.y - a1.y) * d2.x)) / denominator;
            var u = (((b1.x - a1.x) * d1.y) - ((b1.y - a1.y) * d1.x)) / denominator;
            if (t < 0f || t > 1f || u < 0f || u > 1f)
            {
                return false;
            }

            point = a1 + (t * d1);
            return true;
        }

        public static bool TryResolveOpeningCenter(FloorData floor, string wallSegmentId, float offsetAlongWall, out Vector2 center)
        {
            center = Vector2.zero;
            var wall = floor?.wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));
            if (wall == null)
            {
                return false;
            }

            var direction = wall.endPoint - wall.startPoint;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            center = wall.startPoint + (direction.normalized * offsetAlongWall);
            return true;
        }

        public static Vector2[] BuildRotatedRectCorners(Vector2 center, Vector2 size, float rotationDegrees)
        {
            var half = size * 0.5f;
            var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            return new[]
            {
                center + (Vector2)(rotation * new Vector3(-half.x, -half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(-half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, half.y, 0f)),
                center + (Vector2)(rotation * new Vector3(half.x, -half.y, 0f))
            };
        }

        private static SandboxAxisGuide ResolveBestGuide(
            float targetCoordinate,
            float minSpan,
            float maxSpan,
            bool isVertical,
            IEnumerable<float> references,
            float gridSize,
            float tolerance)
        {
            var bestDistance = float.PositiveInfinity;
            var bestCoordinate = 0f;
            var bestIsGrid = false;

            var safeTolerance = Mathf.Max(0.01f, tolerance);
            var safeGridSize = Mathf.Max(0.05f, gridSize);
            var gridCoordinate = Mathf.Round(targetCoordinate / safeGridSize) * safeGridSize;
            var gridDistance = Mathf.Abs(targetCoordinate - gridCoordinate);
            if (gridDistance <= safeTolerance)
            {
                bestDistance = gridDistance;
                bestCoordinate = gridCoordinate;
                bestIsGrid = true;
            }

            if (references != null)
            {
                foreach (var reference in references)
                {
                    var distance = Mathf.Abs(targetCoordinate - reference);
                    if (distance > safeTolerance || distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    bestCoordinate = reference;
                    bestIsGrid = false;
                }
            }

            if (float.IsPositiveInfinity(bestDistance))
            {
                return new SandboxAxisGuide(false, isVertical, 0f, 0f, 0f, false, 0f);
            }

            return new SandboxAxisGuide(
                true,
                isVertical,
                bestCoordinate,
                Mathf.Min(minSpan, maxSpan) - GuideMargin,
                Mathf.Max(minSpan, maxSpan) + GuideMargin,
                bestIsGrid,
                bestDistance);
        }

        private static void AddRectangleReferences(Vector2 center, Vector2 size, float rotationDegrees, ICollection<float> xReferences, ICollection<float> yReferences)
        {
            xReferences.Add(center.x);
            yReferences.Add(center.y);

            var corners = BuildRotatedRectCorners(center, size, rotationDegrees);
            foreach (var corner in corners)
            {
                xReferences.Add(corner.x);
                yReferences.Add(corner.y);
            }
        }
    }
}
