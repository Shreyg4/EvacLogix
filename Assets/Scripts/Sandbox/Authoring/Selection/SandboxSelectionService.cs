using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Selection
{
    public sealed class SandboxSelectionService : MonoBehaviour
    {
        [SerializeField] private bool multiSelectEnabled = true;
        [SerializeField] private List<string> selectedObjectIds = new();

        public event Action<IReadOnlyList<string>> SelectionChanged;

        public IReadOnlyList<string> SelectedObjectIds => selectedObjectIds;
        public bool MultiSelectEnabled => multiSelectEnabled;
        public bool HasSelection => selectedObjectIds.Count > 0;

        public void SetMultiSelectEnabled(bool enabled)
        {
            multiSelectEnabled = enabled;
            if (!multiSelectEnabled && selectedObjectIds.Count > 1)
            {
                ApplySelection(selectedObjectIds.Take(1));
            }
        }

        public void ReplaceSelection(IEnumerable<string> nextSelection, SandboxCommandHistory commandHistory = null)
        {
            var normalizedSelection = Normalize(nextSelection);
            var previousSelection = new List<string>(selectedObjectIds);

            if (AreEqual(previousSelection, normalizedSelection))
            {
                return;
            }

            if (commandHistory == null)
            {
                ApplySelection(normalizedSelection);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                "Replace Selection",
                () => ApplySelection(normalizedSelection),
                () => ApplySelection(previousSelection)));
        }

        public void ClearSelection(SandboxCommandHistory commandHistory = null)
        {
            ReplaceSelection(Array.Empty<string>(), commandHistory);
        }

        public void AddToSelection(string objectId, SandboxCommandHistory commandHistory = null)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            var nextSelection = multiSelectEnabled
                ? selectedObjectIds.Concat(new[] { objectId })
                : new[] { objectId };

            ReplaceSelection(nextSelection, commandHistory);
        }

        private void ApplySelection(IEnumerable<string> nextSelection)
        {
            selectedObjectIds = Normalize(nextSelection);
            SelectionChanged?.Invoke(selectedObjectIds);
        }

        private List<string> Normalize(IEnumerable<string> selection)
        {
            if (selection == null)
            {
                return new List<string>();
            }

            var normalized = selection
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (!multiSelectEnabled && normalized.Count > 1)
            {
                normalized.RemoveRange(1, normalized.Count - 1);
            }

            return normalized;
        }

        private static bool AreEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i += 1)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
