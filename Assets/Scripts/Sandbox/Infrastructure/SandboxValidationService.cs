using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxValidationService : MonoBehaviour
    {
        [SerializeField] private List<ValidationIssueData> issues = new();

        public event Action<IReadOnlyList<ValidationIssueData>> ValidationIssuesChanged;

        public IReadOnlyList<ValidationIssueData> Issues => issues;

        public void ReplaceIssues(IEnumerable<ValidationIssueData> nextIssues)
        {
            issues = nextIssues == null ? new List<ValidationIssueData>() : new List<ValidationIssueData>(nextIssues);
            ValidationIssuesChanged?.Invoke(issues);
        }

        public void Clear()
        {
            ReplaceIssues(Array.Empty<ValidationIssueData>());
        }
    }
}
