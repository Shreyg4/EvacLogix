using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Validation;
using EvacLogix.Sandbox.Runtime;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxValidationService : MonoBehaviour
    {
        [SerializeField] private List<ValidationIssueData> issues = new();
        [SerializeField] private List<ValidationIssueData> structuralIssues = new();
        [SerializeField] private List<ValidationIssueData> previewPlacementIssues = new();
        [SerializeField] private List<SandboxValidationFloorGroup> groupedIssues = new();
        [SerializeField] private bool hasBlockingIssues;
        [SerializeField] private string lastValidatedUtc = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxAgentSimulationService agentSimulationService;
        private SandboxWorkspaceStateService workspaceStateService;

        public event Action<IReadOnlyList<ValidationIssueData>> ValidationIssuesChanged;

        public IReadOnlyList<ValidationIssueData> Issues => issues;
        public IReadOnlyList<SandboxValidationFloorGroup> GroupedIssues => groupedIssues;
        public bool HasBlockingIssues => hasBlockingIssues;
        public string LastValidatedUtc => lastValidatedUtc;
        public string PreviewPlacementMessage => previewPlacementIssues.FirstOrDefault()?.message ?? string.Empty;

        private bool subscribedToColliders;
        private bool subscribedToProjectChanges;

        private void Awake()
        {
            ResolveDependenciesAndSubscribe();
        }

        private void Start()
        {
            // The installer adds this service before the workspace/collider services exist,
            // so Awake-time GetComponent calls can resolve to null. Re-resolve here once all
            // sibling services are present, then surface validation for the current project.
            ResolveDependenciesAndSubscribe();
            ValidateActiveProject();
        }

        private void OnDestroy()
        {
            if (subscribedToColliders && colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt -= HandleCollidersRebuilt;
            }

            if (subscribedToProjectChanges && workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleActiveProjectChanged;
            }
        }

        private void ResolveDependenciesAndSubscribe()
        {
            workspaceService ??= GetComponent<SandboxProjectWorkspaceService>();
            colliderRebuildService ??= GetComponent<SandboxColliderRebuildService>();
            agentSimulationService ??= GetComponent<SandboxAgentSimulationService>();
            workspaceStateService ??= GetComponent<SandboxWorkspaceStateService>();

            if (!subscribedToColliders && colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt += HandleCollidersRebuilt;
                subscribedToColliders = true;
            }

            if (!subscribedToProjectChanges && workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleActiveProjectChanged;
                subscribedToProjectChanges = true;
            }
        }

        private void HandleActiveProjectChanged(BuildingProjectData project)
        {
            ValidateActiveProject();
        }

        public void ReplaceIssues(IEnumerable<ValidationIssueData> nextIssues)
        {
            structuralIssues = nextIssues == null ? new List<ValidationIssueData>() : new List<ValidationIssueData>(nextIssues);
            PublishIssues();
        }

        public void SetPreviewPlacementValidationIssue(
            string floorId,
            string objectId,
            string title,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                ClearPreviewPlacementValidationIssue();
                return;
            }

            previewPlacementIssues = new List<ValidationIssueData>
            {
                new()
                {
                    issueId = "preview-spawn-placement",
                    floorId = floorId ?? string.Empty,
                    objectId = objectId ?? string.Empty,
                    severity = ValidationIssueSeverity.BlockingError,
                    issueType = ValidationIssueType.Preview,
                    title = string.IsNullOrWhiteSpace(title) ? "Preview placement error" : title,
                    message = message
                }
            };

            PublishIssues();
        }

        public void ClearPreviewPlacementValidationIssue()
        {
            if (previewPlacementIssues.Count == 0)
            {
                return;
            }

            previewPlacementIssues = new List<ValidationIssueData>();
            PublishIssues();
        }

        public IReadOnlyList<ValidationIssueData> ValidateActiveProject()
        {
            ResolveDependenciesAndSubscribe();
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                Clear();
                return issues;
            }

            var nextIssues = SandboxStructuralValidationUtility.Validate(
                project,
                colliderRebuildService?.GeneratedColliders,
                workspaceStateService != null ? workspaceStateService.GridSize : 0.5f);
            AddAgentSpacingIssues(project, nextIssues);
            ReplaceIssues(nextIssues);
            return issues;
        }

        public bool CanPreviewOrExport()
        {
            return !HasBlockingIssues;
        }

        public void Clear()
        {
            structuralIssues = new List<ValidationIssueData>();
            previewPlacementIssues = new List<ValidationIssueData>();
            ReplaceIssues(Array.Empty<ValidationIssueData>());
        }

        private void HandleCollidersRebuilt(IReadOnlyList<SandboxGeneratedColliderData> generatedColliders, bool wasFullRebuild, string floorId)
        {
            ValidateActiveProject();
        }

        private void AddAgentSpacingIssues(BuildingProjectData project, ICollection<ValidationIssueData> issues)
        {
            if (project == null || project.spawnLayouts == null || project.spawnLayouts.Count == 0)
            {
                return;
            }

            var agentRadius = agentSimulationService != null ? agentSimulationService.AgentRadius : 0.25f;
            var minimumSpacing = Mathf.Max(0.1f, agentRadius * 2f);
            var floorSpawnPoints = project.spawnLayouts
                .Where(layout => layout?.spawnPoints != null)
                .SelectMany(layout => layout.spawnPoints.Select(point => new SpawnPointPlacementContext(point)))
                .Where(context => context.point != null && !string.IsNullOrWhiteSpace(context.point.floorId))
                .GroupBy(context => context.point.floorId, StringComparer.Ordinal);

            foreach (var floorGroup in floorSpawnPoints)
            {
                var points = floorGroup.ToList();
                for (var leftIndex = 0; leftIndex < points.Count; leftIndex += 1)
                {
                    for (var rightIndex = leftIndex + 1; rightIndex < points.Count; rightIndex += 1)
                    {
                        var left = points[leftIndex];
                        var right = points[rightIndex];
                        var distance = Vector2.Distance(left.point.position, right.point.position);
                        if (distance >= minimumSpacing)
                        {
                            continue;
                        }

                        var pairId = string.CompareOrdinal(left.point.spawnPointId, right.point.spawnPointId) <= 0
                            ? $"{left.point.spawnPointId}:{right.point.spawnPointId}"
                            : $"{right.point.spawnPointId}:{left.point.spawnPointId}";
                        issues.Add(new ValidationIssueData
                        {
                            issueId = $"agent-spacing-{floorGroup.Key}-{pairId}",
                            floorId = floorGroup.Key,
                            objectId = pairId,
                            severity = ValidationIssueSeverity.BlockingError,
                            issueType = ValidationIssueType.Conflict,
                            title = "Spawn point agents overlap",
                            message = $"Spawn points '{left.point.spawnPointId}' and '{right.point.spawnPointId}' are only {distance:0.00} units apart, but agent circles need at least {minimumSpacing:0.00} units of spacing."
                        });
                    }
                }
            }
        }

        private static List<SandboxValidationFloorGroup> BuildGroups(
            IReadOnlyList<ValidationIssueData> nextIssues,
            BuildingProjectData project)
        {
            var floorLabels = project?.floors
                                 .GroupBy(floor => floor.floorId, StringComparer.Ordinal)
                                 .ToDictionary(group => group.Key, group => group.First().name, StringComparer.Ordinal)
                             ?? new Dictionary<string, string>(StringComparer.Ordinal);

            return nextIssues
                .GroupBy(issue => string.IsNullOrWhiteSpace(issue.floorId) ? "__project__" : issue.floorId, StringComparer.Ordinal)
                .Select(floorGroup => new SandboxValidationFloorGroup
                {
                    floorId = floorGroup.Key == "__project__" ? string.Empty : floorGroup.Key,
                    label = floorGroup.Key == "__project__"
                        ? "Project"
                        : (floorLabels.TryGetValue(floorGroup.Key, out var floorName) && !string.IsNullOrWhiteSpace(floorName)
                            ? floorName
                            : floorGroup.Key),
                    objectGroups = floorGroup
                        .GroupBy(issue => string.IsNullOrWhiteSpace(issue.objectId) ? "__floor__" : issue.objectId, StringComparer.Ordinal)
                        .Select(objectGroup => new SandboxValidationObjectGroup
                        {
                            objectId = objectGroup.Key == "__floor__" ? string.Empty : objectGroup.Key,
                            label = objectGroup.Key == "__floor__" ? "Floor" : objectGroup.Key,
                            issues = objectGroup.ToList()
                        })
                        .OrderBy(group => group.label, StringComparer.Ordinal)
                        .ToList()
                })
                .OrderBy(group => group.label, StringComparer.Ordinal)
                .ToList();
        }

        private readonly struct SpawnPointPlacementContext
        {
            public SpawnPointPlacementContext(SpawnPointData point)
            {
                this.point = point;
            }

            public SpawnPointData point { get; }
        }

        private void PublishIssues()
        {
            issues = structuralIssues
                .Concat(previewPlacementIssues)
                .ToList();
            groupedIssues = BuildGroups(issues, workspaceService?.ActiveProject);
            hasBlockingIssues = issues.Any(issue => issue.severity == ValidationIssueSeverity.BlockingError);
            lastValidatedUtc = DateTime.UtcNow.ToString("O");

            var project = workspaceService?.ActiveProject;
            if (project != null)
            {
                project.validationSnapshot ??= new ValidationSnapshotData();
                project.validationSnapshot.lastValidatedUtc = lastValidatedUtc;
                project.validationSnapshot.issues = new List<ValidationIssueData>(structuralIssues);
            }

            ValidationIssuesChanged?.Invoke(issues);
        }
    }
}
