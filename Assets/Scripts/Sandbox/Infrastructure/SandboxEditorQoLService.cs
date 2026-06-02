using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxAdvancedFoldoutState
    {
        public string key = string.Empty;
        public bool isExpanded;
    }

    public sealed class SandboxEditorQoLService : MonoBehaviour
    {
        [SerializeField] private bool isolateSelectedObjects;
        [SerializeField] private bool isolateObjectType;
        [SerializeField] private SandboxVisualObjectType isolatedType = SandboxVisualObjectType.Wall;
        [SerializeField] private bool showColliderOutlines;
        [SerializeField] private bool showStairLinks;
        [SerializeField] private bool showPassableBlockedRegions;
        [SerializeField] private bool showRouteInspection;
        [SerializeField] private bool tooltipsEnabled = true;
        [SerializeField] private bool validationHelpEnabled = true;
        [SerializeField] private bool firstRunOnboardingVisible = true;
        [SerializeField] private string currentToolHelpText = string.Empty;
        [SerializeField] private string currentValidationHelpText = string.Empty;
        [SerializeField] private List<SandboxAdvancedFoldoutState> advancedFoldouts = new();

        private SandboxToolStateService toolStateService;
        private SandboxValidationService validationService;
        private SandboxSelectionService selectionService;

        public event Action StateChanged;

        public bool IsolateSelectedObjects => isolateSelectedObjects;
        public bool IsolateObjectType => isolateObjectType;
        public SandboxVisualObjectType IsolatedType => isolatedType;
        public bool ShowColliderOutlines => showColliderOutlines;
        public bool ShowStairLinks => showStairLinks;
        public bool ShowPassableBlockedRegions => showPassableBlockedRegions;
        public bool ShowRouteInspection => showRouteInspection;
        public bool TooltipsEnabled => tooltipsEnabled;
        public bool ValidationHelpEnabled => validationHelpEnabled;
        public bool FirstRunOnboardingVisible => firstRunOnboardingVisible;
        public string CurrentToolHelpText => currentToolHelpText;
        public string CurrentValidationHelpText => currentValidationHelpText;
        public IReadOnlyList<SandboxAdvancedFoldoutState> AdvancedFoldouts => advancedFoldouts;

        private void Awake()
        {
            toolStateService = GetComponent<SandboxToolStateService>();
            validationService = GetComponent<SandboxValidationService>();
            selectionService = GetComponent<SandboxSelectionService>();

            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged += HandleToolModeChanged;
                HandleToolModeChanged(toolStateService.CurrentToolMode);
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged += HandleValidationIssuesChanged;
                HandleValidationIssuesChanged(validationService.Issues);
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged += HandleSelectionChanged;
            }

            EnsureDefaultAdvancedFoldouts();
        }

        private void OnDestroy()
        {
            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged -= HandleToolModeChanged;
            }

            if (validationService != null)
            {
                validationService.ValidationIssuesChanged -= HandleValidationIssuesChanged;
            }

            if (selectionService != null)
            {
                selectionService.SelectionChanged -= HandleSelectionChanged;
            }
        }

        public void SetIsolateSelectedObjects(bool enabled)
        {
            if (isolateSelectedObjects == enabled)
            {
                return;
            }

            isolateSelectedObjects = enabled;
            RaiseStateChanged();
        }

        public void SetIsolatedObjectType(SandboxVisualObjectType objectType)
        {
            isolateObjectType = true;
            isolatedType = objectType;
            RaiseStateChanged();
        }

        public void ClearObjectTypeIsolation()
        {
            if (!isolateObjectType)
            {
                return;
            }

            isolateObjectType = false;
            RaiseStateChanged();
        }

        public void SetDebugOverlayState(bool colliderOutlines, bool stairLinks, bool passableBlocked, bool routeInspection)
        {
            showColliderOutlines = colliderOutlines;
            showStairLinks = stairLinks;
            showPassableBlockedRegions = passableBlocked;
            showRouteInspection = routeInspection;
            RaiseStateChanged();
        }

        public void SetTooltipsEnabled(bool enabled)
        {
            if (tooltipsEnabled == enabled)
            {
                return;
            }

            tooltipsEnabled = enabled;
            RaiseStateChanged();
        }

        public void SetValidationHelpEnabled(bool enabled)
        {
            if (validationHelpEnabled == enabled)
            {
                return;
            }

            validationHelpEnabled = enabled;
            RaiseStateChanged();
        }

        public void DismissFirstRunOnboarding()
        {
            if (!firstRunOnboardingVisible)
            {
                return;
            }

            firstRunOnboardingVisible = false;
            RaiseStateChanged();
        }

        public void ShowFirstRunOnboarding()
        {
            if (firstRunOnboardingVisible)
            {
                return;
            }

            firstRunOnboardingVisible = true;
            RaiseStateChanged();
        }

        public void SetAdvancedFoldoutState(string key, bool isExpanded)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var entry = advancedFoldouts.Find(candidate => string.Equals(candidate.key, key, StringComparison.Ordinal));
            if (entry == null)
            {
                advancedFoldouts.Add(new SandboxAdvancedFoldoutState { key = key, isExpanded = isExpanded });
                RaiseStateChanged();
                return;
            }

            if (entry.isExpanded == isExpanded)
            {
                return;
            }

            entry.isExpanded = isExpanded;
            RaiseStateChanged();
        }

        public bool IsAdvancedFoldoutExpanded(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var entry = advancedFoldouts.Find(candidate => string.Equals(candidate.key, key, StringComparison.Ordinal));
            return entry != null && entry.isExpanded;
        }

        public void EnsureDefaultAdvancedFoldouts()
        {
            var didChange = false;
            foreach (var key in SandboxObjectPresentationCatalog.GetRequiredAdvancedFoldoutKeys())
            {
                if (advancedFoldouts.Any(candidate => string.Equals(candidate.key, key, StringComparison.Ordinal)))
                {
                    continue;
                }

                advancedFoldouts.Add(new SandboxAdvancedFoldoutState
                {
                    key = key,
                    isExpanded = false
                });
                didChange = true;
            }

            if (didChange)
            {
                advancedFoldouts = advancedFoldouts
                    .Where(candidate => !string.IsNullOrWhiteSpace(candidate.key))
                    .GroupBy(candidate => candidate.key, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderBy(candidate => candidate.key, StringComparer.Ordinal)
                    .ToList();
                RaiseStateChanged();
            }
        }

        public bool IsObjectTypeVisibleForIsolation(SandboxVisualObjectType objectType)
        {
            return !isolateObjectType || isolatedType == objectType;
        }

        public bool IsObjectVisibleForIsolation(string objectId, SandboxVisualObjectType objectType)
        {
            if (!IsObjectTypeVisibleForIsolation(objectType))
            {
                return false;
            }

            if (!isolateSelectedObjects || selectionService == null)
            {
                return true;
            }

            return selectionService.SelectedObjectIds.Count == 0 ||
                   selectionService.SelectedObjectIds.Contains(objectId);
        }

        private void HandleToolModeChanged(SandboxToolMode toolMode)
        {
            currentToolHelpText = toolMode switch
            {
                SandboxToolMode.Select => "Select objects to inspect, rename, lock, or isolate them.",
                SandboxToolMode.Pan => "Pan around large floor plates without disturbing authored geometry.",
                SandboxToolMode.Measure => "Click two points to measure distance and compare against selected geometry readouts.",
                SandboxToolMode.Erase => "Click to remove one object, or switch to Brush in the erase guide for scrub-style cleanup with a resizable radius.",
                SandboxToolMode.WallLine => "Place two points to trace precise wall centerlines with snapping. Hold Alt for fine adjustments without snapping.",
                SandboxToolMode.WallBrush => "Sketch rough wall paths, then clean them before accepting. Hold Alt to bypass snapping during fine wall edits.",
                SandboxToolMode.Door => "Place doors on existing walls only and review their state metadata in the inspector.",
                SandboxToolMode.Window => "Place windows on existing walls and capture escape-risk metadata.",
                SandboxToolMode.Exit => "Define exits as zones, not points, so capacity and orientation stay explicit.",
                SandboxToolMode.Obstacle => "Drop obstacles with limited rotation and name the important ones for review.",
                SandboxToolMode.Teleport => "Place paired stair, elevator, or escalator transitions and switch floors before placing the second endpoint when needed.",
                SandboxToolMode.SpawnPoint => "Place intentional spawn points on floors that have at least one exit.",
                SandboxToolMode.SpawnPointBrush => "Paint spawn points on floors that have at least one exit for crowd setup.",
                SandboxToolMode.FireStart => "Place a fire origin where ignition begins; the fire spreads from here during simulation.",
                _ => $"Use the {toolMode} tool and check validation feedback after each major edit."
            };

            RaiseStateChanged();
        }

        private void HandleValidationIssuesChanged(IReadOnlyList<ValidationIssueData> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                currentValidationHelpText = "Validation is clean. You can keep editing or move toward preview/export.";
                RaiseStateChanged();
                return;
            }

            currentValidationHelpText = validationService != null && validationService.HasBlockingIssues
                ? "Blocking issues stop preview/export, but you can keep editing while fixing them."
                : "Warnings flag risky layout choices without blocking continued editing.";

            RaiseStateChanged();
        }

        private void HandleSelectionChanged(IReadOnlyList<string> selection)
        {
            if (isolateSelectedObjects)
            {
                RaiseStateChanged();
            }
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
