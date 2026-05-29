using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Validation;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxValidationService : MonoBehaviour
    {
        [SerializeField] private List<ValidationIssueData> issues = new();
        [SerializeField] private List<SandboxValidationFloorGroup> groupedIssues = new();
        [SerializeField] private bool hasBlockingIssues;
        [SerializeField] private string lastValidatedUtc = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxColliderRebuildService colliderRebuildService;

        public event Action<IReadOnlyList<ValidationIssueData>> ValidationIssuesChanged;

        public IReadOnlyList<ValidationIssueData> Issues => issues;
        public IReadOnlyList<SandboxValidationFloorGroup> GroupedIssues => groupedIssues;
        public bool HasBlockingIssues => hasBlockingIssues;
        public string LastValidatedUtc => lastValidatedUtc;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();

            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt += HandleCollidersRebuilt;
            }
        }

        private void OnDestroy()
        {
            if (colliderRebuildService != null)
            {
                colliderRebuildService.CollidersRebuilt -= HandleCollidersRebuilt;
            }
        }

        public void ReplaceIssues(IEnumerable<ValidationIssueData> nextIssues)
        {
            issues = nextIssues == null ? new List<ValidationIssueData>() : new List<ValidationIssueData>(nextIssues);
            groupedIssues = BuildGroups(issues, workspaceService?.ActiveProject);
            hasBlockingIssues = issues.Any(issue => issue.severity == ValidationIssueSeverity.BlockingError);
            lastValidatedUtc = DateTime.UtcNow.ToString("O");

            var project = workspaceService?.ActiveProject;
            if (project != null)
            {
                project.validationSnapshot ??= new ValidationSnapshotData();
                project.validationSnapshot.lastValidatedUtc = lastValidatedUtc;
                project.validationSnapshot.issues = new List<ValidationIssueData>(issues);
            }

            ValidationIssuesChanged?.Invoke(issues);
        }

        public IReadOnlyList<ValidationIssueData> ValidateActiveProject()
        {
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                Clear();
                return issues;
            }

            var nextIssues = SandboxStructuralValidationUtility.Validate(project, colliderRebuildService?.GeneratedColliders);
            ReplaceIssues(nextIssues);
            return issues;
        }

        public bool CanPreviewOrExport()
        {
            return !HasBlockingIssues;
        }

        public void Clear()
        {
            ReplaceIssues(Array.Empty<ValidationIssueData>());
        }

        private void HandleCollidersRebuilt(IReadOnlyList<SandboxGeneratedColliderData> generatedColliders, bool wasFullRebuild, string floorId)
        {
            ValidateActiveProject();
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
    }
}
