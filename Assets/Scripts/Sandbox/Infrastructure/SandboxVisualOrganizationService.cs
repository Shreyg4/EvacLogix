using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxVisualObjectType
    {
        Wall = 0,
        Door = 1,
        Window = 2,
        Exit = 3,
        Stair = 4,
        Teleport = 5,
        Obstacle = 6,
        Spawn = 7,
        Region = 8,
    }

    [Serializable]
    public sealed class SandboxVisualStyleEntry
    {
        public SandboxVisualObjectType objectType;
        public string label = string.Empty;
        public string description = string.Empty;
        public bool isVisible = true;
        public bool isLocked;
        public Color color = Color.white;
    }

    [Serializable]
    public sealed class SandboxVisualLegendEntry
    {
        public SandboxVisualObjectType objectType;
        public string label = string.Empty;
        public string description = string.Empty;
        public bool isVisible = true;
        public bool isLocked;
        public Color color = Color.white;
    }

    public sealed class SandboxVisualOrganizationService : MonoBehaviour
    {
        [SerializeField] private List<SandboxVisualStyleEntry> styleEntries = new();
        [SerializeField] private List<string> hiddenObjectIds = new();
        [SerializeField] private List<string> lockedObjectIds = new();

        private SandboxSelectionService selectionService;

        public event Action VisualStateChanged;

        public IReadOnlyList<SandboxVisualStyleEntry> StyleEntries => styleEntries;
        public IReadOnlyList<string> HiddenObjectIds => hiddenObjectIds;
        public IReadOnlyList<string> LockedObjectIds => lockedObjectIds;

        private void Awake()
        {
            selectionService = GetComponent<SandboxSelectionService>();
            EnsureDefaultStyles();
            hiddenObjectIds = NormalizeIds(hiddenObjectIds);
            lockedObjectIds = NormalizeIds(lockedObjectIds);
        }

        public IReadOnlyList<SandboxVisualLegendEntry> GetLegendEntries()
        {
            EnsureDefaultStyles();
            return styleEntries
                .OrderBy(entry => entry.objectType)
                .Select(entry => new SandboxVisualLegendEntry
                {
                    objectType = entry.objectType,
                    label = entry.label,
                    description = entry.description,
                    isVisible = entry.isVisible,
                    isLocked = entry.isLocked,
                    color = entry.color
                })
                .ToList();
        }

        public bool IsTypeVisible(SandboxVisualObjectType objectType)
        {
            return GetStyleEntry(objectType).isVisible;
        }

        public bool IsTypeLocked(SandboxVisualObjectType objectType)
        {
            return GetStyleEntry(objectType).isLocked;
        }

        public bool IsObjectHidden(string objectId)
        {
            return !string.IsNullOrWhiteSpace(objectId) && hiddenObjectIds.Contains(objectId);
        }

        public bool IsObjectLocked(string objectId)
        {
            return !string.IsNullOrWhiteSpace(objectId) && lockedObjectIds.Contains(objectId);
        }

        public Color GetColor(SandboxVisualObjectType objectType)
        {
            return GetStyleEntry(objectType).color;
        }

        public void SetTypeVisibility(SandboxVisualObjectType objectType, bool isVisible)
        {
            var entry = GetStyleEntry(objectType);
            if (entry.isVisible == isVisible)
            {
                return;
            }

            entry.isVisible = isVisible;
            RaiseVisualStateChanged();
        }

        public void SetTypeLocked(SandboxVisualObjectType objectType, bool isLocked)
        {
            var entry = GetStyleEntry(objectType);
            if (entry.isLocked == isLocked)
            {
                return;
            }

            entry.isLocked = isLocked;
            RaiseVisualStateChanged();
        }

        public void SetTypeColor(SandboxVisualObjectType objectType, Color color)
        {
            var entry = GetStyleEntry(objectType);
            if (entry.color == color)
            {
                return;
            }

            entry.color = color;
            RaiseVisualStateChanged();
        }

        public void SetObjectHidden(string objectId, bool isHidden)
        {
            UpdateObjectIdList(hiddenObjectIds, objectId, isHidden);
        }

        public void SetObjectLocked(string objectId, bool isLocked)
        {
            UpdateObjectIdList(lockedObjectIds, objectId, isLocked);
        }

        public void HideCurrentSelection(bool shouldHide)
        {
            ApplySelectionState(hiddenObjectIds, shouldHide);
        }

        public void LockCurrentSelection(bool shouldLock)
        {
            ApplySelectionState(lockedObjectIds, shouldLock);
        }

        public void ResetHiddenAndLockedSelection()
        {
            var selectedIds = selectionService == null
                ? Array.Empty<string>()
                : selectionService.SelectedObjectIds;
            var didChange = false;

            foreach (var selectedId in selectedIds)
            {
                didChange |= hiddenObjectIds.Remove(selectedId);
                didChange |= lockedObjectIds.Remove(selectedId);
            }

            if (didChange)
            {
                RaiseVisualStateChanged();
            }
        }

        private void ApplySelectionState(List<string> idList, bool enabled)
        {
            if (selectionService == null)
            {
                return;
            }

            var didChange = false;
            foreach (var selectedId in selectionService.SelectedObjectIds)
            {
                didChange |= UpdateObjectIdList(idList, selectedId, enabled, false);
            }

            if (didChange)
            {
                RaiseVisualStateChanged();
            }
        }

        private void EnsureDefaultStyles()
        {
            if (styleEntries.Count > 0)
            {
                foreach (SandboxVisualObjectType objectType in Enum.GetValues(typeof(SandboxVisualObjectType)))
                {
                    if (styleEntries.Any(entry => entry.objectType == objectType))
                    {
                        continue;
                    }

                    styleEntries.Add(CreateDefaultEntry(objectType));
                }

                styleEntries = styleEntries
                    .Select(NormalizeStyleEntry)
                    .GroupBy(entry => entry.objectType)
                    .Select(group => group.First())
                    .OrderBy(entry => entry.objectType)
                    .ToList();
                return;
            }

            styleEntries = Enum.GetValues(typeof(SandboxVisualObjectType))
                .Cast<SandboxVisualObjectType>()
                .Select(CreateDefaultEntry)
                .ToList();
        }

        private SandboxVisualStyleEntry GetStyleEntry(SandboxVisualObjectType objectType)
        {
            EnsureDefaultStyles();
            return styleEntries.First(entry => entry.objectType == objectType);
        }

        private static SandboxVisualStyleEntry CreateDefaultEntry(SandboxVisualObjectType objectType)
        {
            var definition = SandboxObjectPresentationCatalog.GetDefinition(objectType);
            return new SandboxVisualStyleEntry
            {
                objectType = objectType,
                label = definition.label,
                description = definition.description,
                color = definition.color
            };
        }

        private static SandboxVisualStyleEntry NormalizeStyleEntry(SandboxVisualStyleEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            var definition = SandboxObjectPresentationCatalog.GetDefinition(entry.objectType);
            entry.label = string.IsNullOrWhiteSpace(entry.label) ? definition.label : entry.label.Trim();
            entry.description = string.IsNullOrWhiteSpace(entry.description) ? definition.description : entry.description.Trim();
            if (entry.color.a <= 0f)
            {
                entry.color = definition.color;
            }

            return entry;
        }

        private void UpdateObjectIdList(List<string> idList, string objectId, bool enabled)
        {
            if (UpdateObjectIdList(idList, objectId, enabled, true))
            {
                RaiseVisualStateChanged();
            }
        }

        private static bool UpdateObjectIdList(List<string> idList, string objectId, bool enabled, bool normalize)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            if (enabled)
            {
                if (idList.Contains(objectId))
                {
                    return false;
                }

                idList.Add(objectId);
            }
            else if (!idList.Remove(objectId))
            {
                return false;
            }

            if (normalize)
            {
                var normalized = NormalizeIds(idList);
                idList.Clear();
                idList.AddRange(normalized);
            }

            return true;
        }

        private void RaiseVisualStateChanged()
        {
            hiddenObjectIds = NormalizeIds(hiddenObjectIds);
            lockedObjectIds = NormalizeIds(lockedObjectIds);
            VisualStateChanged?.Invoke();
        }

        private static List<string> NormalizeIds(IEnumerable<string> objectIds)
        {
            return objectIds == null
                ? new List<string>()
                : objectIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        }
    }
}
