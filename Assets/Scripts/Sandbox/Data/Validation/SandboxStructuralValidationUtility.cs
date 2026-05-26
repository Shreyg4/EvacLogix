using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Data.Validation
{
    public static class SandboxStructuralValidationUtility
    {
        public static List<ValidationIssueData> Validate(
            BuildingProjectData project,
            IReadOnlyList<SandboxGeneratedColliderData> generatedColliders)
        {
            var issues = new List<ValidationIssueData>();
            if (project == null)
            {
                return issues;
            }

            ValidateProjectLevelDuplicates(project, issues);
            ValidateProjectLevelConflicts(project, issues);
            ValidateProjectReferenceIntegrity(project, issues);

            for (var i = 0; i < project.floors.Count; i += 1)
            {
                var floor = project.floors[i];
                var floorColliders = generatedColliders == null
                    ? Array.Empty<SandboxGeneratedColliderData>()
                    : generatedColliders.Where(collider => string.Equals(collider.floorId, floor.floorId, StringComparison.Ordinal)).ToArray();

                ValidateDisconnectedWallStructures(floor, issues);
                ValidateOpenings(floor, issues);
                ValidateStairs(project, floor, issues);
                ValidateExits(floor, issues);
                ValidateOverlappingExits(floor, issues);
                ValidateObstacleOverlaps(floor, floorColliders, issues);
                ValidateConflictingObstacles(floor, issues);
            }

            return issues
                .OrderBy(issue => issue.floorId)
                .ThenBy(issue => issue.objectId)
                .ThenBy(issue => issue.issueId)
                .ToList();
        }

        private static void ValidateProjectLevelDuplicates(BuildingProjectData project, ICollection<ValidationIssueData> issues)
        {
            AddDuplicateIdIssues(project.floors.Select(floor => (floor.floorId, floor.floorId, string.Empty, "Floor")), issues);
            AddDuplicateIdIssues(project.blueprintReferences.Select(reference => (reference.blueprintReferenceId, reference.blueprintReferenceId, string.Empty, "Blueprint")), issues);
            AddDuplicateIdIssues(project.spawnLayouts.Select(layout => (layout.spawnLayoutId, layout.spawnLayoutId, string.Empty, "Spawn layout")), issues);
            AddDuplicateIdIssues(project.fireOrigins.Select(origin => (origin.fireOriginId, origin.fireOriginId, origin.floorId, "Fire origin")), issues);
            AddDuplicateIdIssues(project.scenarioPresets.Select(preset => (preset.scenarioPresetId, preset.scenarioPresetId, string.Empty, "Scenario preset")), issues);

            for (var i = 0; i < project.floors.Count; i += 1)
            {
                var floor = project.floors[i];
                AddDuplicateIdIssues(floor.wallJunctions.Select(junction => (junction.wallJunctionId, junction.wallJunctionId, floor.floorId, "Wall junction")), issues);
                AddDuplicateIdIssues(floor.wallSegments.Select(wall => (wall.wallSegmentId, wall.wallSegmentId, floor.floorId, "Wall segment")), issues);
                AddDuplicateIdIssues(floor.doors.Select(door => (door.doorId, door.doorId, floor.floorId, "Door")), issues);
                AddDuplicateIdIssues(floor.windows.Select(window => (window.windowId, window.windowId, floor.floorId, "Window")), issues);
                AddDuplicateIdIssues(floor.exits.Select(exitZone => (exitZone.exitZoneId, exitZone.exitZoneId, floor.floorId, "Exit")), issues);
                AddDuplicateIdIssues(floor.obstacles.Select(obstacle => (obstacle.obstacleId, obstacle.obstacleId, floor.floorId, "Obstacle")), issues);
                AddDuplicateIdIssues(floor.stairPortals.Select(portal => (portal.stairPortalId, portal.stairPortalId, floor.floorId, "Stair portal")), issues);
                AddDuplicateIdIssues(floor.regions.Select(region => (region.regionId, region.regionId, floor.floorId, "Region")), issues);
            }
        }

        private static void ValidateProjectLevelConflicts(BuildingProjectData project, ICollection<ValidationIssueData> issues)
        {
            var duplicateFloorNames = project.floors
                .Where(floor => !string.IsNullOrWhiteSpace(floor.name))
                .GroupBy(floor => floor.name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var duplicateGroup in duplicateFloorNames)
            {
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.Warning,
                    ValidationIssueType.Conflict,
                    string.Empty,
                    string.Empty,
                    $"Floor name conflict: {duplicateGroup.Key}",
                    "Multiple floors share the same display name."));
            }

            var duplicateFloorOrders = project.floors
                .GroupBy(floor => floor.order)
                .Where(group => group.Count() > 1);

            foreach (var duplicateGroup in duplicateFloorOrders)
            {
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.Warning,
                    ValidationIssueType.Conflict,
                    string.Empty,
                    string.Empty,
                    $"Floor order conflict: {duplicateGroup.Key}",
                    "Multiple floors share the same floor order."));
            }
        }

        private static void ValidateProjectReferenceIntegrity(BuildingProjectData project, ICollection<ValidationIssueData> issues)
        {
            var floorIds = new HashSet<string>(project.floors.Select(floor => floor.floorId), StringComparer.Ordinal);
            var spawnLayoutIds = new HashSet<string>(project.spawnLayouts.Select(layout => layout.spawnLayoutId), StringComparer.Ordinal);
            var fireOriginIds = new HashSet<string>(project.fireOrigins.Select(origin => origin.fireOriginId), StringComparer.Ordinal);
            var invalidSpawnLayoutIds = new HashSet<string>(StringComparer.Ordinal);
            var invalidFireOriginIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var spawnLayout in project.spawnLayouts)
            {
                foreach (var spawnPoint in spawnLayout.spawnPoints)
                {
                    if (floorIds.Contains(spawnPoint.floorId))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        spawnPoint.floorId,
                        spawnPoint.spawnPointId,
                        "Invalid spawn floor reference",
                        "Spawn point references a floor that no longer exists."));
                    invalidSpawnLayoutIds.Add(spawnLayout.spawnLayoutId);
                }

                foreach (var spawnBrushStroke in spawnLayout.spawnBrushStrokes)
                {
                    if (floorIds.Contains(spawnBrushStroke.floorId))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        spawnBrushStroke.floorId,
                        spawnBrushStroke.spawnBrushStrokeId,
                        "Invalid spawn floor reference",
                        "Spawn brush stroke references a floor that no longer exists."));
                    invalidSpawnLayoutIds.Add(spawnLayout.spawnLayoutId);
                }
            }

            foreach (var fireOrigin in project.fireOrigins)
            {
                if (floorIds.Contains(fireOrigin.floorId))
                {
                    continue;
                }

                issues.Add(CreateIssue(
                    ValidationIssueSeverity.BlockingError,
                    ValidationIssueType.Reference,
                    fireOrigin.floorId,
                    fireOrigin.fireOriginId,
                    "Invalid fire origin floor reference",
                    "Fire origin references a floor that no longer exists."));
                invalidFireOriginIds.Add(fireOrigin.fireOriginId);
            }

            foreach (var scenarioPreset in project.scenarioPresets)
            {
                foreach (var spawnLayoutId in scenarioPreset.spawnLayoutIds.Where(id => !spawnLayoutIds.Contains(id)))
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        string.Empty,
                        scenarioPreset.scenarioPresetId,
                        "Invalid scenario spawn reference",
                        $"Scenario references missing spawn layout '{spawnLayoutId}'."));
                }

                foreach (var fireOriginId in scenarioPreset.fireOriginIds.Where(id => !fireOriginIds.Contains(id)))
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        string.Empty,
                        scenarioPreset.scenarioPresetId,
                        "Invalid scenario fire reference",
                        $"Scenario references missing fire origin '{fireOriginId}'."));
                }

                if (scenarioPreset.spawnLayoutIds.Any(invalidSpawnLayoutIds.Contains) ||
                    scenarioPreset.fireOriginIds.Any(invalidFireOriginIds.Contains))
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        string.Empty,
                        scenarioPreset.scenarioPresetId,
                        "Invalid scenario floor dependency",
                        "Scenario references spawn or fire data that points to a missing floor."));
                }
            }
        }

        private static void ValidateDisconnectedWallStructures(FloorData floor, ICollection<ValidationIssueData> issues)
        {
            if (floor.wallSegments.Count < 2)
            {
                return;
            }

            var junctionLookup = floor.wallJunctions.ToDictionary(junction => junction.wallJunctionId, junction => junction, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var componentCount = 0;

            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (visited.Contains(wall.wallSegmentId))
                {
                    continue;
                }

                componentCount += 1;
                var stack = new Stack<string>();
                stack.Push(wall.wallSegmentId);

                while (stack.Count > 0)
                {
                    var currentWallId = stack.Pop();
                    if (!visited.Add(currentWallId))
                    {
                        continue;
                    }

                    var currentWall = floor.wallSegments.FirstOrDefault(candidate =>
                        string.Equals(candidate.wallSegmentId, currentWallId, StringComparison.Ordinal));
                    if (currentWall == null)
                    {
                        continue;
                    }

                    VisitConnectedWall(currentWall.startJunctionId, floor, junctionLookup, visited, stack);
                    VisitConnectedWall(currentWall.endJunctionId, floor, junctionLookup, visited, stack);
                }
            }

            if (componentCount > 1)
            {
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.Warning,
                    ValidationIssueType.Connectivity,
                    floor.floorId,
                    string.Empty,
                    "Disconnected wall structures",
                    $"Found {componentCount} disconnected wall networks on this floor."));
            }
        }

        private static void ValidateOpenings(FloorData floor, ICollection<ValidationIssueData> issues)
        {
            for (var i = 0; i < floor.doors.Count; i += 1)
            {
                ValidateOpening(
                    floor.floorId,
                    floor.doors[i].doorId,
                    floor.doors[i].wallSegmentId,
                    floor.doors[i].offsetAlongWall,
                    floor.doors[i].width,
                    floor.wallSegments,
                    "Door",
                    issues);
            }

            for (var i = 0; i < floor.windows.Count; i += 1)
            {
                ValidateOpening(
                    floor.floorId,
                    floor.windows[i].windowId,
                    floor.windows[i].wallSegmentId,
                    floor.windows[i].offsetAlongWall,
                    floor.windows[i].width,
                    floor.wallSegments,
                    "Window",
                    issues);
            }
        }

        private static void ValidateOpening(
            string floorId,
            string objectId,
            string wallSegmentId,
            float offsetAlongWall,
            float width,
            IReadOnlyList<WallSegmentData> wallSegments,
            string label,
            ICollection<ValidationIssueData> issues)
        {
            var wall = wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.wallSegmentId, wallSegmentId, StringComparison.Ordinal));

            if (wall == null || width <= 0f)
            {
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.BlockingError,
                    ValidationIssueType.Reference,
                    floorId,
                    objectId,
                    $"Invalid {label.ToLowerInvariant()} placement",
                    $"{label} is missing a valid wall reference or has invalid width."));
                return;
            }

            var wallLength = Vector2.Distance(wall.startPoint, wall.endPoint);
            var halfWidth = width * 0.5f;
            if (offsetAlongWall - halfWidth < -0.01f || offsetAlongWall + halfWidth > wallLength + 0.01f)
            {
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.BlockingError,
                    ValidationIssueType.Structural,
                    floorId,
                    objectId,
                    $"Invalid {label.ToLowerInvariant()} opening",
                    $"{label} extends beyond the referenced wall segment."));
            }
        }

        private static void ValidateStairs(BuildingProjectData project, FloorData floor, ICollection<ValidationIssueData> issues)
        {
            for (var i = 0; i < floor.stairPortals.Count; i += 1)
            {
                var portal = floor.stairPortals[i];
                var targetFloor = project.floors.FirstOrDefault(candidate =>
                    string.Equals(candidate.floorId, portal.targetFloorId, StringComparison.Ordinal));
                var targetPortal = targetFloor?.stairPortals.FirstOrDefault(candidate =>
                    string.Equals(candidate.stairPortalId, portal.targetStairPortalId, StringComparison.Ordinal));

                if (targetFloor == null || targetPortal == null)
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Reference,
                        floor.floorId,
                        portal.stairPortalId,
                        "Invalid stair link",
                        "Stair portal target floor or target portal is missing."));
                    continue;
                }

                if (!string.Equals(targetPortal.targetFloorId, floor.floorId, StringComparison.Ordinal) ||
                    !string.Equals(targetPortal.targetStairPortalId, portal.stairPortalId, StringComparison.Ordinal))
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Connectivity,
                        floor.floorId,
                        portal.stairPortalId,
                        "Invalid stair link reciprocity",
                        "Linked stair portal does not point back to the source portal."));
                }
            }

            for (var leftIndex = 0; leftIndex < floor.stairPortals.Count; leftIndex += 1)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < floor.stairPortals.Count; rightIndex += 1)
                {
                    if (Vector2.Distance(floor.stairPortals[leftIndex].localPosition, floor.stairPortals[rightIndex].localPosition) > 0.1f)
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Duplicate,
                        floor.floorId,
                        floor.stairPortals[leftIndex].stairPortalId,
                        "Duplicate stair endpoints",
                        "Two stair portals occupy the same endpoint on this floor."));
                }
            }
        }

        private static void ValidateExits(FloorData floor, ICollection<ValidationIssueData> issues)
        {
            for (var i = 0; i < floor.exits.Count; i += 1)
            {
                var exitZone = floor.exits[i];
                if (exitZone.width <= 0f || exitZone.size.x <= 0f || exitZone.size.y <= 0f)
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Structural,
                        floor.floorId,
                        exitZone.exitZoneId,
                        "Invalid exit geometry",
                        "Exit zone width and size must both be positive."));
                }
            }
        }

        private static void ValidateOverlappingExits(FloorData floor, ICollection<ValidationIssueData> issues)
        {
            for (var leftIndex = 0; leftIndex < floor.exits.Count; leftIndex += 1)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < floor.exits.Count; rightIndex += 1)
                {
                    if (!RectsOverlap(floor.exits[leftIndex].center, floor.exits[leftIndex].size, floor.exits[rightIndex].center, floor.exits[rightIndex].size))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.Warning,
                        ValidationIssueType.Conflict,
                        floor.floorId,
                        floor.exits[leftIndex].exitZoneId,
                        "Overlapping exits",
                        "Two exit zones overlap on this floor."));
                }
            }
        }

        private static void ValidateObstacleOverlaps(
            FloorData floor,
            IReadOnlyList<SandboxGeneratedColliderData> generatedColliders,
            ICollection<ValidationIssueData> issues)
        {
            for (var i = 0; i < floor.obstacles.Count; i += 1)
            {
                var obstacle = floor.obstacles[i];
                if (obstacle.size.x <= 0f || obstacle.size.y <= 0f)
                {
                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Structural,
                        floor.floorId,
                        obstacle.obstacleId,
                        "Invalid obstacle geometry",
                        "Obstacle size must be positive."));
                    continue;
                }

                for (var exitIndex = 0; exitIndex < floor.exits.Count; exitIndex += 1)
                {
                    if (!RectsOverlap(obstacle.center, obstacle.size, floor.exits[exitIndex].center, floor.exits[exitIndex].size))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Conflict,
                        floor.floorId,
                        obstacle.obstacleId,
                        "Invalid obstacle overlap",
                        "Obstacle overlaps an exit zone."));
                }

                for (var stairIndex = 0; stairIndex < floor.stairPortals.Count; stairIndex += 1)
                {
                    if (!PointInsideRect(floor.stairPortals[stairIndex].localPosition, obstacle.center, obstacle.size))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Conflict,
                        floor.floorId,
                        obstacle.obstacleId,
                        "Invalid obstacle overlap",
                        "Obstacle overlaps a stair endpoint."));
                }

                for (var colliderIndex = 0; colliderIndex < generatedColliders.Count; colliderIndex += 1)
                {
                    if (!RectsOverlap(obstacle.center, obstacle.size, generatedColliders[colliderIndex].center, generatedColliders[colliderIndex].size))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.BlockingError,
                        ValidationIssueType.Structural,
                        floor.floorId,
                        obstacle.obstacleId,
                        "Invalid obstacle overlap",
                        "Obstacle overlaps generated wall collider geometry."));
                }
            }
        }

        private static void ValidateConflictingObstacles(FloorData floor, ICollection<ValidationIssueData> issues)
        {
            for (var leftIndex = 0; leftIndex < floor.obstacles.Count; leftIndex += 1)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < floor.obstacles.Count; rightIndex += 1)
                {
                    if (!RectsOverlap(
                            floor.obstacles[leftIndex].center,
                            floor.obstacles[leftIndex].size,
                            floor.obstacles[rightIndex].center,
                            floor.obstacles[rightIndex].size))
                    {
                        continue;
                    }

                    if (floor.obstacles[leftIndex].semanticType == floor.obstacles[rightIndex].semanticType &&
                        Mathf.Approximately(floor.obstacles[leftIndex].traversalCostMultiplier, floor.obstacles[rightIndex].traversalCostMultiplier))
                    {
                        continue;
                    }

                    issues.Add(CreateIssue(
                        ValidationIssueSeverity.Warning,
                        ValidationIssueType.Conflict,
                        floor.floorId,
                        floor.obstacles[leftIndex].obstacleId,
                        "Conflicting obstacles",
                        "Overlapping obstacles have conflicting semantics or traversal costs."));
                }
            }
        }

        private static void AddDuplicateIdIssues(
            IEnumerable<(string id, string objectId, string floorId, string label)> items,
            ICollection<ValidationIssueData> issues)
        {
            var duplicateGroups = items
                .Where(item => !string.IsNullOrWhiteSpace(item.id))
                .GroupBy(item => item.id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            foreach (var duplicateGroup in duplicateGroups)
            {
                var firstItem = duplicateGroup.First();
                issues.Add(CreateIssue(
                    ValidationIssueSeverity.BlockingError,
                    ValidationIssueType.Duplicate,
                    firstItem.floorId,
                    firstItem.objectId,
                    $"Duplicate {firstItem.label.ToLowerInvariant()} ID",
                    $"{firstItem.label} ID '{duplicateGroup.Key}' is used more than once."));
            }
        }

        private static void VisitConnectedWall(
            string junctionId,
            FloorData floor,
            IReadOnlyDictionary<string, WallJunctionData> junctionLookup,
            HashSet<string> visited,
            Stack<string> stack)
        {
            if (string.IsNullOrWhiteSpace(junctionId) || !junctionLookup.TryGetValue(junctionId, out var junction))
            {
                return;
            }

            for (var i = 0; i < junction.connectedWallSegmentIds.Count; i += 1)
            {
                if (!visited.Contains(junction.connectedWallSegmentIds[i]))
                {
                    stack.Push(junction.connectedWallSegmentIds[i]);
                }
            }
        }

        private static ValidationIssueData CreateIssue(
            ValidationIssueSeverity severity,
            ValidationIssueType issueType,
            string floorId,
            string objectId,
            string title,
            string message)
        {
            return new ValidationIssueData
            {
                issueId = SandboxId.NewId(),
                floorId = floorId ?? string.Empty,
                objectId = objectId ?? string.Empty,
                severity = severity,
                issueType = issueType,
                title = title ?? string.Empty,
                message = message ?? string.Empty
            };
        }

        private static bool RectsOverlap(Vector2 leftCenter, Vector2 leftSize, Vector2 rightCenter, Vector2 rightSize)
        {
            var leftHalf = leftSize * 0.5f;
            var rightHalf = rightSize * 0.5f;

            return Mathf.Abs(leftCenter.x - rightCenter.x) <= leftHalf.x + rightHalf.x &&
                   Mathf.Abs(leftCenter.y - rightCenter.y) <= leftHalf.y + rightHalf.y;
        }

        private static bool PointInsideRect(Vector2 point, Vector2 rectCenter, Vector2 rectSize)
        {
            var half = rectSize * 0.5f;
            return point.x >= rectCenter.x - half.x &&
                   point.x <= rectCenter.x + half.x &&
                   point.y >= rectCenter.y - half.y &&
                   point.y <= rectCenter.y + half.y;
        }
    }
}
