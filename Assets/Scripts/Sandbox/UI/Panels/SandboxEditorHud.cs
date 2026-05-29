using System;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxEditorHud : MonoBehaviour
    {
        private enum SelectionEditableKind
        {
            None = 0,
            Exit = 1,
            Obstacle = 2,
            Stair = 3,
            Teleport = 4,
            Door = 5,
            Window = 6,
        }

        [SerializeField] private string blueprintImportPath = string.Empty;
        [SerializeField] private string calibrationDistanceText = "10";
        [SerializeField] private string pendingFloorName = "Floor";
        [SerializeField] private string selectionSizeXText = string.Empty;
        [SerializeField] private string selectionSizeYText = string.Empty;
        [SerializeField] private bool showLegend = true;
        [SerializeField] private bool topBarCollapsed;
        [SerializeField] private bool toolPaletteCollapsed;
        [SerializeField] private bool floorTabsCollapsed;
        [SerializeField] private bool inspectorCollapsed;
        [SerializeField] private bool validationCollapsed;
        [SerializeField] private bool statusBarCollapsed;
        [SerializeField] private Vector2 toolScrollPosition;
        [SerializeField] private Vector2 inspectorScrollPosition;
        [SerializeField] private Vector2 validationScrollPosition;

        private SandboxTopBarShell topBarShell;
        private SandboxToolPaletteShell toolPaletteShell;
        private SandboxFloorTabsBarShell floorTabsBarShell;
        private SandboxInspectorPanelShell inspectorPanelShell;
        private SandboxVisualLegendShell visualLegendShell;
        private SandboxValidationPanelShell validationPanelShell;
        private SandboxStatusBarShell statusBarShell;
        private SandboxNewProjectDialogShell newProjectDialogShell;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxSelectionService selectionService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxInputRouter inputRouter;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxSemanticObjectAuthoringOverlay semanticObjectAuthoringOverlay;

        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle bodyStyle;
        private GUIStyle activeToolButtonStyle;
        private GUIStyle collapseToggleStyle;
        private RectOffset buttonPadding;

        private const float CollapsedPanelHeight = 30f;

        private Rect topBarRect;
        private Rect toolPaletteRect;
        private Rect floorTabsRect;
        private Rect inspectorRect;
        private Rect validationRect;
        private Rect statusBarRect;
        private Rect modalRect;
        private string selectionEditorTargetId = string.Empty;
        private SelectionEditableKind selectionEditorKind = SelectionEditableKind.None;
        private string obstacleBehaviorSyncedId = string.Empty;
        private float selectionObstacleWeight = 1f;
        private float selectionObstacleSpeedPenalty;
        private bool hasLoggedGuiException;
        private string lastGuiExceptionMessage = string.Empty;
        private bool hasLoggedWebGlDependencyStatus;
        private bool hasLoggedBrowserActionMode;

        public bool IsFullyWired =>
            topBarShell != null &&
            toolPaletteShell != null &&
            floorTabsBarShell != null &&
            inspectorPanelShell != null &&
            visualLegendShell != null &&
            validationPanelShell != null &&
            statusBarShell != null &&
            newProjectDialogShell != null &&
            workspaceService != null &&
            inputRouter != null;

        private void Awake()
        {
            RefreshDependencies();
            EnsureDefaultFieldValues();
            buttonPadding ??= new RectOffset(10, 10, 6, 6);
        }

        private void Update()
        {
            RefreshDependenciesIfNeeded();
            LogWebGlDependencyStatusIfNeeded();
            LogBrowserActionModeIfNeeded();
            UpdateInputCapture();
        }

        public void RefreshDependencies()
        {
            topBarShell = FindAnyObjectByType<SandboxTopBarShell>();
            toolPaletteShell = FindAnyObjectByType<SandboxToolPaletteShell>();
            floorTabsBarShell = FindAnyObjectByType<SandboxFloorTabsBarShell>();
            inspectorPanelShell = FindAnyObjectByType<SandboxInspectorPanelShell>();
            visualLegendShell = FindAnyObjectByType<SandboxVisualLegendShell>();
            validationPanelShell = FindAnyObjectByType<SandboxValidationPanelShell>();
            statusBarShell = FindAnyObjectByType<SandboxStatusBarShell>();
            newProjectDialogShell = FindAnyObjectByType<SandboxNewProjectDialogShell>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            selectionService = FindAnyObjectByType<SandboxSelectionService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            semanticObjectAuthoringOverlay = FindAnyObjectByType<SandboxSemanticObjectAuthoringOverlay>();
        }

        private void OnDisable()
        {
            inputRouter?.SetManualOverride(SandboxInputTarget.None);
        }

        private void OnGUI()
        {
            try
            {
                RefreshDependenciesIfNeeded();
                EnsureStyles();
                EnsureDefaultFieldValues();
                RecalculateLayout();

                DrawTopBar();
                DrawToolPalette();
                DrawFloorTabs();
                DrawInspector();
                DrawValidationPanel();
                DrawStatusBar();

                if (newProjectDialogShell != null && newProjectDialogShell.IsOpen)
                {
                    DrawNewProjectModal();
                }
            }
            catch (Exception exception)
            {
                lastGuiExceptionMessage = exception.ToString();
                if (!hasLoggedGuiException)
                {
                    hasLoggedGuiException = true;
                    Debug.LogError($"SandboxEditorHud.OnGUI failed: {exception}");
                }

                DrawGuiExceptionFallback();
            }
        }

        private void DrawTopBar()
        {
            GUILayout.BeginArea(topBarRect, GUIContent.none, GUI.skin.box);
            topBarCollapsed = DrawCollapseToggle(topBarRect.width, topBarCollapsed);
            GUILayout.Label(topBarShell?.Title ?? "Sandbox Editor", headerStyle);
            if (topBarCollapsed)
            {
                GUILayout.EndArea();
                return;
            }

            var projectName = workspaceService?.ActiveProject?.metadata.buildingName;
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "Untitled Project";
            }

            GUILayout.Label($"{projectName}  |  {topBarShell?.LifecycleStateLabel ?? "Draft"}  |  {topBarShell?.ModeLabel ?? "Edit Mode"}", bodyStyle);

            GUILayout.BeginHorizontal();
            DrawActionButton("New", () => topBarShell?.OpenNewProjectDialog());

            if (topBarShell != null && topBarShell.UsesBrowserHostedFileActions)
            {
                DrawActionButton("Save", () => { }, false);
                DrawActionButton("Load", () => { }, false);
                DrawActionButton("Export JSON", () => { topBarShell.RequestBrowserProjectJsonExport(); }, workspaceService?.ActiveProject != null && !topBarShell.IsBrowserFileActionBusy);
                DrawActionButton("Import JSON", () => { topBarShell.RequestBrowserProjectJsonImport(); }, !topBarShell.IsBrowserFileActionBusy);
            }
            else
            {
                DrawActionButton("Save", () => { topBarShell?.SaveProject(GetWorkingProjectPath()); }, workspaceService?.ActiveProject != null);
                DrawActionButton("Load", () => { topBarShell?.LoadProject(GetWorkingProjectPath()); });
                DrawActionButton("Export JSON", () => { topBarShell?.ExportProjectJson(GetProjectExportPath()); }, workspaceService?.ActiveProject != null);
                DrawActionButton("Import JSON", () => { topBarShell?.ImportProjectJson(GetProjectExportPath()); });
            }

            DrawActionButton("Export Runtime", () => { topBarShell?.ExportRuntimeProjectData(GetRuntimeExportPath()); }, workspaceService?.ActiveProject != null);
            DrawActionButton("Export Preview", () => { topBarShell?.ExportPreviewImage(GetPreviewImagePath()); }, workspaceService?.ActiveProject != null);
            DrawActionButton("Rebuild All", () => topBarShell?.RebuildAll(), workspaceService?.ActiveProject != null);

            if (topBarShell != null && topBarShell.IsPreviewModeActive)
            {
                DrawActionButton("Run Preview", () => { topBarShell.RunPreview(); }, workspaceService?.ActiveProject != null);
                DrawActionButton("Exit Preview", () => topBarShell.ExitPreviewMode(), true);
            }
            else
            {
                var canPreview = topBarShell != null && topBarShell.CanOpenPreview();
                DrawActionButton("Enter Preview", () => { topBarShell?.EnterPreviewMode(); }, workspaceService?.ActiveProject != null && canPreview);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(topBarShell?.PersistenceModeSummary ?? $"Working files: {GetStorageDirectoryPath()}", bodyStyle);
            if (topBarShell != null && topBarShell.HasRecoveryPrompt)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(topBarShell.RecoveryPromptMessage, bodyStyle);
                DrawActionButton("Restore Recovery", () => { topBarShell.TryRestoreRecovery(); });
                DrawActionButton("Dismiss", () => topBarShell.DismissRecoveryPrompt());
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void DrawToolPalette()
        {
            var boxStyle = GUI.skin?.box ?? GUIStyle.none;
            GUI.BeginGroup(toolPaletteRect, GUIContent.none, boxStyle);

            GUI.Label(new Rect(12f, 10f, toolPaletteRect.width - 44f, 22f), "Tools", headerStyle);
            toolPaletteCollapsed = DrawCollapseToggle(toolPaletteRect.width, toolPaletteCollapsed);
            if (toolPaletteCollapsed)
            {
                GUI.EndGroup();
                return;
            }

            if (workspaceService?.ActiveProject == null)
            {
                GUI.Label(new Rect(12f, 38f, toolPaletteRect.width - 24f, 44f), "Create a project to enable tool actions.", bodyStyle);
                GUI.EndGroup();
                return;
            }

            var toolModes = (toolPaletteShell?.AvailableTools ?? Array.Empty<SandboxToolMode>()).ToArray();
            var contentHeight = Mathf.Max(220f, (toolModes.Length * 36f) + 120f);
            var viewportRect = new Rect(10f, 38f, toolPaletteRect.width - 20f, toolPaletteRect.height - 48f);
            var contentRect = new Rect(0f, 0f, viewportRect.width - 18f, contentHeight);

            toolScrollPosition = GUI.BeginScrollView(viewportRect, toolScrollPosition, contentRect);

            for (var index = 0; index < toolModes.Length; index++)
            {
                var toolMode = toolModes[index];
                var isActive = toolPaletteShell != null && toolPaletteShell.IsToolActive(toolMode);
                var buttonStyle = isActive
                    ? (activeToolButtonStyle ?? GUI.skin?.button ?? GUIStyle.none)
                    : (GUI.skin?.button ?? GUIStyle.none);
                var buttonRect = new Rect(0f, index * 36f, contentRect.width, 30f);
                if (GUI.Button(buttonRect, GetToolLabel(toolMode), buttonStyle))
                {
                    toolPaletteShell?.SelectTool(toolMode);
                }
            }

            var helpY = (toolModes.Length * 36f) + 12f;
            GUI.Label(new Rect(0f, helpY, contentRect.width, 20f), "World Controls", subheaderStyle);
            GUI.Label(new Rect(0f, helpY + 24f, contentRect.width, 40f), "Click in the center of the scene to place or edit using the active tool.", bodyStyle);
            GUI.Label(new Rect(0f, helpY + 66f, contentRect.width, 56f), "Use the top bar to save/load and preview. The HUD blocks world input only while the pointer is over a panel.", bodyStyle);

            GUI.EndScrollView();
            GUI.EndGroup();
        }

        private void DrawFloorTabs()
        {
            GUILayout.BeginArea(floorTabsRect, GUIContent.none, GUI.skin.box);
            floorTabsCollapsed = DrawCollapseToggle(floorTabsRect.width, floorTabsCollapsed);
            if (floorTabsCollapsed)
            {
                GUILayout.Label("Floors", subheaderStyle);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Floors", subheaderStyle, GUILayout.Width(50f));

            if (floorTabsBarShell?.FloorTabs != null)
            {
                foreach (var tab in floorTabsBarShell.FloorTabs)
                {
                    var buttonStyle = tab.isActive ? activeToolButtonStyle : GUI.skin.button;
                    if (GUILayout.Button(tab.name, buttonStyle, GUILayout.Height(28f)))
                    {
                        floorTabsBarShell.SelectFloor(tab.floorId);
                    }
                }
            }

            GUILayout.FlexibleSpace();
            pendingFloorName = GUILayout.TextField(pendingFloorName, GUILayout.Width(120f));
            DrawActionButton("Add Floor", () => { floorTabsBarShell?.AddFloor(pendingFloorName, 0f); }, workspaceService?.ActiveProject != null);

            var activeFloor = workspaceService?.ActiveFloor;
            DrawActionButton("Duplicate", () => { floorTabsBarShell?.DuplicateFloor(activeFloor.floorId); }, activeFloor != null);
            DrawActionButton("Delete", () => { floorTabsBarShell?.RequestDeleteFloor(activeFloor.floorId); }, activeFloor != null);
            if (floorTabsBarShell != null && floorTabsBarShell.HasPendingDeleteConfirmation)
            {
                DrawActionButton("Confirm Delete", () => { floorTabsBarShell.ConfirmDeleteFloor(); });
                DrawActionButton("Cancel", () => floorTabsBarShell.CancelDeleteFloor());
            }
            GUILayout.Space(30f);
            GUILayout.EndHorizontal();

            if (floorTabsBarShell != null && floorTabsBarShell.HasPendingDeleteConfirmation)
            {
                GUILayout.Label(floorTabsBarShell.PendingDeleteMessage, bodyStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawInspector()
        {
            GUILayout.BeginArea(inspectorRect, GUIContent.none, GUI.skin.box);
            inspectorCollapsed = DrawCollapseToggle(inspectorRect.width, inspectorCollapsed);
            GUILayout.Label("Inspector", headerStyle);
            if (inspectorCollapsed)
            {
                GUILayout.EndArea();
                return;
            }

            DrawBrushActionBanner();

            // Disable the horizontal scrollbar so content is constrained to the panel width
            // (rows wrap/share width instead of overflowing and forcing a horizontal bar).
            inspectorScrollPosition = GUILayout.BeginScrollView(inspectorScrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar);

            DrawInspectorSection("Workspace");
            var activeFloor = workspaceService?.ActiveFloor;
            var activeProject = workspaceService?.ActiveProject;
            GUILayout.Label($"Project: {(string.IsNullOrWhiteSpace(activeProject?.metadata.buildingName) ? "Untitled Project" : activeProject.metadata.buildingName)}", bodyStyle);
            GUILayout.Label($"Active Floor: {activeFloor?.name ?? "None"}", bodyStyle);
            GUILayout.Label($"Distance Unit: {inspectorPanelShell?.CurrentDistanceUnitLabel ?? SandboxDistanceUnitUtility.GetLabel(DistanceUnit.Feet)}", bodyStyle);
            GUILayout.Label($"Selection Count: {selectionService?.SelectedObjectIds.Count ?? 0}", bodyStyle);
            GUILayout.Label(inspectorPanelShell != null && inspectorPanelShell.IsFullyWired ? "Inspector Wiring: OK" : "Inspector Wiring: Missing Dependencies", bodyStyle);
            if (inspectorPanelShell != null && !inspectorPanelShell.IsFullyWired)
            {
                GUILayout.Label(string.Join(", ", inspectorPanelShell.GetMissingDependencies()), bodyStyle);
            }

            GUILayout.BeginHorizontal();
            DrawActionButton("Use Feet", () => inspectorPanelShell?.SetProjectDistanceUnit(DistanceUnit.Feet), activeProject != null);
            DrawActionButton("Use Meters", () => inspectorPanelShell?.SetProjectDistanceUnit(DistanceUnit.Meters), activeProject != null);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawActionButton("Use Inches", () => inspectorPanelShell?.SetProjectDistanceUnit(DistanceUnit.Inches), activeProject != null);
            DrawActionButton("Use Centimeters", () => inspectorPanelShell?.SetProjectDistanceUnit(DistanceUnit.Centimeters), activeProject != null);
            GUILayout.EndHorizontal();

            DrawSelectionEditor();

            DrawInspectorSection("Blueprint");
            if (inspectorPanelShell != null && inspectorPanelShell.UsesBrowserHostedFileActions)
            {
                DrawActionButton("Import Blueprint Image", () => { inspectorPanelShell.RequestBrowserBlueprintImport(); }, activeFloor != null);
                GUILayout.Label("Browser mode uses the website file picker for blueprint uploads.", bodyStyle);
            }
            else
            {
                blueprintImportPath = GUILayout.TextField(blueprintImportPath, GUILayout.Height(24f));
                DrawActionButton("Import Blueprint Path", () => { inspectorPanelShell?.ImportBlueprintToActiveFloor(blueprintImportPath); }, activeFloor != null && !string.IsNullOrWhiteSpace(blueprintImportPath));
            }

            var blueprintReference = activeFloor == null
                ? null
                : workspaceService?.FindBlueprintReference(activeFloor.blueprintReferenceId);

            if (blueprintReference != null)
            {
                GUILayout.Label($"Source: {blueprintReference.sourceFileName}", bodyStyle);
                var nextVisibility = GUILayout.Toggle(blueprintReference.isVisible, "Blueprint Visible");
                if (nextVisibility != blueprintReference.isVisible)
                {
                    inspectorPanelShell?.SetActiveFloorBlueprintVisibility(nextVisibility);
                }

                var nextOpacity = GUILayout.HorizontalSlider(blueprintReference.opacity, 0.05f, 1f);
                GUILayout.Label($"Opacity: {nextOpacity:0.00}", bodyStyle);
                if (!Mathf.Approximately(nextOpacity, blueprintReference.opacity))
                {
                    inspectorPanelShell?.SetActiveFloorBlueprintOpacity(nextOpacity);
                }

                var resolvedDisplayScale = blueprintReference.displayScale <= 0f ? 1f : blueprintReference.displayScale;
                var nextDisplayScale = GUILayout.HorizontalSlider(resolvedDisplayScale, 0.1f, 4f);
                GUILayout.Label($"Background Size: {nextDisplayScale:0.00}x", bodyStyle);
                if (!Mathf.Approximately(nextDisplayScale, resolvedDisplayScale))
                {
                    inspectorPanelShell?.SetActiveFloorBlueprintDisplayScale(nextDisplayScale);
                }

                GUILayout.Label($"Calibration Distance ({inspectorPanelShell?.CurrentDistanceUnitLabel ?? SandboxDistanceUnitUtility.GetLabel(DistanceUnit.Feet)})", bodyStyle);
                GUILayout.BeginHorizontal();
                DrawActionButton("Start Calibration", () => { inspectorPanelShell?.BeginActiveFloorCalibrationCapture(); }, true);
                calibrationDistanceText = GUILayout.TextField(calibrationDistanceText, GUILayout.Width(60f));
                var parsedDistance = float.TryParse(calibrationDistanceText, out var calibrationDistance);
                DrawActionButton("Complete", () => { inspectorPanelShell?.CompleteActiveFloorCalibration(calibrationDistance); }, parsedDistance);
                DrawActionButton("Cancel", () => inspectorPanelShell?.CancelActiveFloorCalibration());
                GUILayout.EndHorizontal();

                if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.LatestCalibrationFeedback))
                {
                    GUILayout.Label(inspectorPanelShell.LatestCalibrationFeedback, bodyStyle);
                }
            }
            else
            {
                GUILayout.Label("No blueprint assigned to the active floor yet.", bodyStyle);
            }

            DrawInspectorSection("Preview");
            if (previewService != null && topBarShell != null)
            {
                GUILayout.Label($"Preview Mode: {(previewService.IsPreviewModeActive ? "Active" : "Inactive")}", bodyStyle);
                if (previewService.IsPreviewModeActive)
                {
                    GUILayout.BeginHorizontal();
                    DrawActionButton("Fire Tool", ActivateFirePlacement);
                    DrawActionButton("Agents", ActivateSpawnPlacement);
                    DrawActionButton("Agents Brush", ActivateSpawnBrushPlacement);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    DrawActionButton("Region Tool", ActivateRegionPlacement);
                    DrawActionButton("Clear Mode", () => previewService.ClearInteractionMode());
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"Interaction: {GetPreviewInteractionLabel(previewService.InteractionMode)}", bodyStyle);
                    if (!string.IsNullOrWhiteSpace(topBarShell.PreviewSummary))
                    {
                        GUILayout.Label(topBarShell.PreviewSummary, bodyStyle);
                    }
                }
                else
                {
                    GUILayout.Label("Enter preview mode from the top bar to place fire origins, agents, regions, and run diagnostics.", bodyStyle);
                }
            }

            DrawInspectorSection("Wall Brush");
            if (wallAuthoringService != null)
            {
                if (wallAuthoringService.IsBrushCaptureActive)
                {
                    GUILayout.Label($"Recording stroke: {wallAuthoringService.ActiveBrushStrokePoints.Count} points captured.", bodyStyle);
                }
                else if (wallAuthoringService.LastCleanedBrushStrokePoints.Count >= 2 && wallAuthoringService.ActiveBrushStrokePoints.Count >= 2)
                {
                    GUILayout.Label($"Brush stroke ready: {wallAuthoringService.LastCleanedBrushStrokePoints.Count} cleaned points.", bodyStyle);
                }
                else
                {
                    GUILayout.Label("Use Wall Brush to sketch a stroke, then accept or discard it from the banner above.", bodyStyle);
                }
            }

            DrawInspectorSection("Legend");
            showLegend = GUILayout.Toggle(showLegend, "Show Visual Legend");
            if (showLegend && visualLegendShell != null && visualOrganizationService != null)
            {
                foreach (var entry in visualLegendShell.LegendEntries)
                {
                    GUILayout.BeginHorizontal();
                    var nextVisible = GUILayout.Toggle(entry.isVisible, entry.label, GUILayout.Width(160f));
                    if (nextVisible != entry.isVisible)
                    {
                        visualOrganizationService.SetTypeVisibility(entry.objectType, nextVisible);
                    }

                    var nextLocked = GUILayout.Toggle(entry.isLocked, "Lock", GUILayout.Width(55f));
                    if (nextLocked != entry.isLocked)
                    {
                        visualOrganizationService.SetTypeLocked(entry.objectType, nextLocked);
                    }

                    var swatchRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
                    var previousColor = GUI.color;
                    GUI.color = entry.color;
                    GUI.Box(swatchRect, GUIContent.none);
                    GUI.color = previousColor;
                    GUILayout.EndHorizontal();
                }
            }

            DrawInspectorSection("Guidance");
            if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentToolHelpText))
            {
                GUILayout.Label(inspectorPanelShell.CurrentToolHelpText, bodyStyle);
            }

            if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentValidationHelpText))
            {
                GUILayout.Label(inspectorPanelShell.CurrentValidationHelpText, bodyStyle);
            }

            if (inspectorPanelShell?.HasActiveMeasurement == true || !string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentMeasurementReadout))
            {
                if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentMeasurementReadout))
                {
                    GUILayout.Label(inspectorPanelShell.CurrentMeasurementReadout, bodyStyle);
                }
                DrawActionButton("Clear Measure", () => inspectorPanelShell?.ClearMeasurement());
            }

            if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentSelectionMeasurementReadout))
            {
                GUILayout.Label(inspectorPanelShell.CurrentSelectionMeasurementReadout, bodyStyle);
            }

            if (inspectorPanelShell != null && inspectorPanelShell.HasShortcutConflicts)
            {
                GUILayout.Label("Shortcut conflicts detected. Review the hardening panel/tests before release.", bodyStyle);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawBrushActionBanner()
        {
            if (wallAuthoringService == null)
            {
                return;
            }

            if (!wallAuthoringService.IsBrushCaptureActive &&
                !(wallAuthoringService.LastCleanedBrushStrokePoints.Count >= 2 && wallAuthoringService.ActiveBrushStrokePoints.Count >= 2))
            {
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Wall Brush", subheaderStyle);

            if (wallAuthoringService.IsBrushCaptureActive)
            {
                GUILayout.Label($"Recording stroke: {wallAuthoringService.ActiveBrushStrokePoints.Count} points captured.", bodyStyle);
                DrawActionButton("Cancel Brush Capture", () => inspectorPanelShell?.CancelBrushWallStroke());
            }
            else
            {
                GUILayout.Label($"Brush stroke ready: {wallAuthoringService.LastCleanedBrushStrokePoints.Count} cleaned points.", bodyStyle);
                GUILayout.BeginHorizontal();
                DrawActionButton("Accept Brush Walls", () => inspectorPanelShell?.AcceptBrushWallStroke());
                DrawActionButton("Discard Brush", () => inspectorPanelShell?.CancelBrushWallStroke());
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private void DrawValidationPanel()
        {
            GUILayout.BeginArea(validationRect, GUIContent.none, GUI.skin.box);
            validationCollapsed = DrawCollapseToggle(validationRect.width, validationCollapsed);
            GUILayout.Label("Validation", headerStyle);

            if (validationCollapsed)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            DrawActionButton("Refresh", () => validationPanelShell?.RefreshValidation(), workspaceService?.ActiveProject != null);
            DrawActionButton("Rebuild All", () => validationPanelShell?.RebuildAll(), workspaceService?.ActiveProject != null);
            GUILayout.EndHorizontal();

            GUILayout.Label(validationPanelShell != null && validationPanelShell.HasBlockingIssues
                ? "Blocking issues present."
                : "No blocking issues currently reported.", bodyStyle);

            if (validationPanelShell != null)
            {
                var showCompleteRooms = GUILayout.Toggle(validationPanelShell.ShowCompleteRooms, "Show All Complete Rooms");
                if (showCompleteRooms != validationPanelShell.ShowCompleteRooms)
                {
                    validationPanelShell.SetShowCompleteRooms(showCompleteRooms);
                }

                GUILayout.BeginHorizontal();
                DrawActionButton("Refresh Rooms", () => validationPanelShell.RefreshCompleteRooms(), validationPanelShell.ShowCompleteRooms);
                GUILayout.Label(validationPanelShell.RoomDetectionStatus, bodyStyle);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    $"Room overlay: sealed {validationPanelShell.SealedRoomCount}, penetrated {validationPanelShell.PenetratedRoomCount}.",
                    bodyStyle);
            }

            validationScrollPosition = GUILayout.BeginScrollView(validationScrollPosition);
            foreach (var floorGroup in validationPanelShell?.IssueGroups ?? Enumerable.Empty<SandboxValidationFloorGroup>())
            {
                GUILayout.Label(floorGroup.label, subheaderStyle);
                foreach (var objectGroup in floorGroup.objectGroups)
                {
                    GUILayout.Label(objectGroup.label, bodyStyle);
                    foreach (var issue in objectGroup.issues)
                    {
                        GUILayout.Label($"- {issue.severity}: {issue.title} | {issue.message}", bodyStyle);
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSelectionEditor()
        {
            DrawInspectorSection("Selection");
            if (selectionService == null || selectionService.SelectedObjectIds.Count != 1)
            {
                ResetSelectionEditorState();
                GUILayout.Label("Select a single editable object to update its size or traversal behavior.", bodyStyle);
                return;
            }

            var selectedId = selectionService.SelectedObjectIds[0];
            if (TryFindSelectedExit(selectedId, out var exitZone))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Exit, exitZone.size);
                DrawEditableSelectionSizeFields(
                    "Exit Zone",
                    string.IsNullOrWhiteSpace(exitZone.name) ? selectedId : exitZone.name,
                    () => TryApplyExitSize(exitZone));
                return;
            }

            if (TryFindSelectedObstacle(selectedId, out var obstacle))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Obstacle, obstacle.size);
                if (!string.Equals(obstacleBehaviorSyncedId, selectedId, StringComparison.Ordinal))
                {
                    obstacleBehaviorSyncedId = selectedId;
                    selectionObstacleWeight = obstacle.discourageWeight;
                    selectionObstacleSpeedPenalty = obstacle.movementSpeedPenalty;
                }

                DrawEditableSelectionSizeFields(
                    "Obstacle",
                    string.IsNullOrWhiteSpace(obstacle.name) ? selectedId : obstacle.name,
                    () => TryApplyObstacleSize(obstacle));
                DrawObstacleBehaviorFields(obstacle);
                return;
            }

            if (TryFindSelectedStair(selectedId, out var stairPortal))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Stair, stairPortal.size);
                DrawEditableSelectionSizeFields(
                    "Stair Portal",
                    string.IsNullOrWhiteSpace(stairPortal.name) ? selectedId : stairPortal.name,
                    () => TryApplyStairSize(stairPortal));
                return;
            }

            if (TryFindSelectedTeleport(selectedId, out var teleportPortal))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Teleport, teleportPortal.size);
                DrawTeleportFields(teleportPortal);
                return;
            }

            if (TryFindSelectedDoor(selectedId, out var door))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Door, new Vector2(door.width, 0f));
                DrawDoorFields(door);
                return;
            }

            if (TryFindSelectedWindow(selectedId, out var window))
            {
                SyncSelectionEditorState(selectedId, SelectionEditableKind.Window, new Vector2(window.width, 0f));
                DrawWindowFields(window);
                return;
            }

            ResetSelectionEditorState();
            GUILayout.Label("The current selection does not expose editable size controls yet.", bodyStyle);
        }

        private void DrawDoorFields(DoorData door)
        {
            GUILayout.Label($"Door: {door.doorId}", bodyStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Width", bodyStyle, GUILayout.Width(42f));
            selectionSizeXText = GUILayout.TextField(selectionSizeXText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            GUILayout.Label("Door State", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawActionButton("Normal", () => TryApplyDoor(door, DoorState.Normal), door.state != DoorState.Normal);
            DrawActionButton("Closed", () => TryApplyDoor(door, DoorState.Closed), door.state != DoorState.Closed);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawActionButton("Blocked", () => TryApplyDoor(door, DoorState.Blocked), door.state != DoorState.Blocked);
            DrawActionButton("Locked", () => TryApplyDoor(door, DoorState.Locked), door.state != DoorState.Locked);
            GUILayout.EndHorizontal();

            var canApplyWidth = inspectorPanelShell != null && TryParseSelectionWidth(out _);
            DrawActionButton("Apply Door Width", () => TryApplyDoor(door, door.state), canApplyWidth);
            GUILayout.Label("Normal/Closed doors create passable collider gaps. Blocked/Locked doors remain blocked.", bodyStyle);
        }

        private void DrawWindowFields(WindowData window)
        {
            GUILayout.Label($"Window: {window.windowId}", bodyStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Width", bodyStyle, GUILayout.Width(42f));
            selectionSizeXText = GUILayout.TextField(selectionSizeXText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            var escapeUsable = GUILayout.Toggle(window.canBeUsedForEscape, "Escape Usable");
            if (escapeUsable != window.canBeUsedForEscape)
            {
                TryApplyWindow(window, escapeUsable);
            }

            var canApplyWidth = inspectorPanelShell != null && TryParseSelectionWidth(out _);
            DrawActionButton("Apply Window Width", () => TryApplyWindow(window, window.canBeUsedForEscape), canApplyWidth);
            GUILayout.Label("Only escape-usable windows create passable collider gaps.", bodyStyle);
        }

        private void DrawTeleportFields(TeleportPortalData teleportPortal)
        {
            GUILayout.Label($"Teleport: {teleportPortal.teleportPortalId}", bodyStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Width", bodyStyle, GUILayout.Width(42f));
            selectionSizeXText = GUILayout.TextField(selectionSizeXText, GUILayout.Width(64f));
            GUILayout.Label("Height", bodyStyle, GUILayout.Width(48f));
            selectionSizeYText = GUILayout.TextField(selectionSizeYText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            var pairStateLabel = IsBrokenTeleport(teleportPortal) ? "Broken Pair" : (teleportPortal.isPairEnabled ? "On" : "Off");
            GUILayout.Label($"Pair State: {pairStateLabel}", bodyStyle);

            GUILayout.Label("Type", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawActionButton("Stair", () => TryApplyTeleport(teleportPortal, TeleportPortalKind.Stair, teleportPortal.isPairEnabled), teleportPortal.kind != TeleportPortalKind.Stair);
            DrawActionButton("Elevator", () => TryApplyTeleport(teleportPortal, TeleportPortalKind.Elevator, teleportPortal.isPairEnabled), teleportPortal.kind != TeleportPortalKind.Elevator);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawActionButton("Escalator", () => TryApplyTeleport(teleportPortal, TeleportPortalKind.Escalator, teleportPortal.isPairEnabled), teleportPortal.kind != TeleportPortalKind.Escalator);
            DrawActionButton("Other", () => TryApplyTeleport(teleportPortal, TeleportPortalKind.Other, teleportPortal.isPairEnabled), teleportPortal.kind != TeleportPortalKind.Other);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawActionButton("On", () => TryApplyTeleport(teleportPortal, teleportPortal.kind, true), !teleportPortal.isPairEnabled);
            DrawActionButton("Off", () => TryApplyTeleport(teleportPortal, teleportPortal.kind, false), teleportPortal.isPairEnabled);
            GUILayout.EndHorizontal();

            var canApplySize = inspectorPanelShell != null && TryParseSelectionSize(out _);
            DrawActionButton("Apply Teleport Size", () => TryApplyTeleport(teleportPortal, teleportPortal.kind, teleportPortal.isPairEnabled), canApplySize);

            if (IsBrokenTeleport(teleportPortal))
            {
                DrawActionButton(
                    "Place Missing Pair Endpoint",
                    () =>
                    {
                        var didBegin = semanticObjectAuthoringOverlay != null &&
                                       semanticObjectAuthoringOverlay.BeginMissingTeleportPairPlacement(teleportPortal.teleportPortalId);
                        if (didBegin)
                        {
                            toolPaletteShell?.SelectTool(SandboxToolMode.Teleport);
                        }
                    },
                    semanticObjectAuthoringOverlay != null);
                GUILayout.Label("Broken pairs stay in the project until you place the missing endpoint.", bodyStyle);
            }
            else
            {
                GUILayout.Label($"Linked floor: {teleportPortal.targetFloorId}", bodyStyle);
            }
        }

        private void DrawEditableSelectionSizeFields(string objectTypeLabel, string objectLabel, Action applyAction)
        {
            GUILayout.Label($"{objectTypeLabel}: {objectLabel}", bodyStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Width", bodyStyle, GUILayout.Width(42f));
            selectionSizeXText = GUILayout.TextField(selectionSizeXText, GUILayout.Width(64f));
            GUILayout.Label("Height", bodyStyle, GUILayout.Width(48f));
            selectionSizeYText = GUILayout.TextField(selectionSizeYText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            var canApply = inspectorPanelShell != null && TryParseSelectionSize(out _);
            DrawActionButton("Apply Size", applyAction, canApply);
        }

        private bool TryApplyExitSize(ExitZoneData exitZone)
        {
            if (exitZone == null || !TryParseSelectionSize(out var nextSize))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateExit(
                exitZone.exitZoneId,
                exitZone.center,
                nextSize,
                exitZone.rotationDegrees,
                exitZone.width,
                exitZone.capacity,
                exitZone.priority,
                exitZone.name,
                exitZone.tags,
                exitZone.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(exitZone.exitZoneId, SelectionEditableKind.Exit, nextSize, true);
            }

            return didUpdate;
        }

        private bool TryApplyObstacleSize(ObstacleData obstacle)
        {
            if (obstacle == null || !TryParseSelectionSize(out var nextSize))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateObstacle(
                obstacle.obstacleId,
                obstacle.center,
                nextSize,
                obstacle.rotationDegrees,
                obstacle.discourageWeight,
                obstacle.movementSpeedPenalty,
                obstacle.name,
                obstacle.tags,
                obstacle.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(obstacle.obstacleId, SelectionEditableKind.Obstacle, nextSize, true);
            }

            return didUpdate;
        }

        private void DrawObstacleBehaviorFields(ObstacleData obstacle)
        {
            GUILayout.Label($"Discourage Weight: {selectionObstacleWeight:0.00}  (0 = open floor, 1 = impassable)", bodyStyle);
            selectionObstacleWeight = Mathf.Clamp01(GUILayout.HorizontalSlider(selectionObstacleWeight, 0f, 1f));

            var impassable = selectionObstacleWeight >= 1f;
            var previousEnabled = GUI.enabled;
            GUI.enabled = !impassable;
            GUILayout.Label(impassable
                ? "Movement Speed Penalty: n/a (impassable)"
                : $"Movement Speed Penalty: {selectionObstacleSpeedPenalty:0.00}  (0 = none, 1 = full stop)", bodyStyle);
            selectionObstacleSpeedPenalty = Mathf.Clamp01(GUILayout.HorizontalSlider(selectionObstacleSpeedPenalty, 0f, 1f));
            GUI.enabled = previousEnabled;

            DrawActionButton("Apply Behavior", () => TryApplyObstacleBehavior(obstacle), inspectorPanelShell != null);
        }

        private bool TryApplyObstacleBehavior(ObstacleData obstacle)
        {
            if (obstacle == null)
            {
                return false;
            }

            return inspectorPanelShell != null && inspectorPanelShell.UpdateObstacle(
                obstacle.obstacleId,
                obstacle.center,
                obstacle.size,
                obstacle.rotationDegrees,
                selectionObstacleWeight,
                selectionObstacleSpeedPenalty,
                obstacle.name,
                obstacle.tags,
                obstacle.metadataFields);
        }

        private bool TryApplyStairSize(StairPortalData stairPortal)
        {
            if (stairPortal == null || !TryParseSelectionSize(out var nextSize))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateStairPortal(
                stairPortal.stairPortalId,
                stairPortal.localPosition,
                nextSize,
                stairPortal.rotationDegrees,
                stairPortal.name,
                stairPortal.direction,
                stairPortal.travelCost,
                stairPortal.tags,
                stairPortal.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(stairPortal.stairPortalId, SelectionEditableKind.Stair, nextSize, true);
            }

            return didUpdate;
        }

        private bool TryApplyTeleport(TeleportPortalData teleportPortal, TeleportPortalKind kind, bool isPairEnabled)
        {
            if (teleportPortal == null || !TryParseSelectionSize(out var nextSize))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateTeleportPortal(
                teleportPortal.teleportPortalId,
                teleportPortal.localPosition,
                nextSize,
                teleportPortal.rotationDegrees,
                teleportPortal.name,
                kind,
                teleportPortal.travelCost,
                isPairEnabled,
                teleportPortal.tags,
                teleportPortal.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(teleportPortal.teleportPortalId, SelectionEditableKind.Teleport, nextSize, true);
            }

            return didUpdate;
        }

        private bool TryApplyDoor(DoorData door, DoorState state)
        {
            if (door == null || !TryParseSelectionWidth(out var width))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateDoor(
                door.doorId,
                width,
                door.offsetAlongWall,
                state,
                door.tags,
                door.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(door.doorId, SelectionEditableKind.Door, new Vector2(width, 0f), true);
            }

            return didUpdate;
        }

        private bool TryApplyWindow(WindowData window, bool canBeUsedForEscape)
        {
            if (window == null || !TryParseSelectionWidth(out var width))
            {
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateWindow(
                window.windowId,
                width,
                window.offsetAlongWall,
                canBeUsedForEscape,
                window.escapeCost,
                window.escapeRiskMultiplier,
                window.tags,
                window.metadataFields);
            if (didUpdate)
            {
                SyncSelectionEditorState(window.windowId, SelectionEditableKind.Window, new Vector2(width, 0f), true);
            }

            return didUpdate;
        }

        private void SyncSelectionEditorState(string targetId, SelectionEditableKind editableKind, Vector2 size, bool force = false)
        {
            if (!force &&
                string.Equals(selectionEditorTargetId, targetId, StringComparison.Ordinal) &&
                selectionEditorKind == editableKind)
            {
                return;
            }

            selectionEditorTargetId = targetId ?? string.Empty;
            selectionEditorKind = editableKind;
            selectionSizeXText = size.x.ToString("0.###");
            selectionSizeYText = size.y.ToString("0.###");
        }

        private void ResetSelectionEditorState()
        {
            selectionEditorTargetId = string.Empty;
            selectionEditorKind = SelectionEditableKind.None;
            selectionSizeXText = string.Empty;
            selectionSizeYText = string.Empty;
        }

        private bool TryParseSelectionSize(out Vector2 size)
        {
            size = Vector2.zero;
            if (!float.TryParse(selectionSizeXText, out var width) ||
                !float.TryParse(selectionSizeYText, out var height) ||
                width <= 0f ||
                height <= 0f)
            {
                return false;
            }

            size = new Vector2(width, height);
            return true;
        }

        private bool TryParseSelectionWidth(out float width)
        {
            return float.TryParse(selectionSizeXText, out width) && width > 0f;
        }

        private bool TryFindSelectedExit(string selectedId, out ExitZoneData exitZone)
        {
            exitZone = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            exitZone = floors
                .SelectMany(floor => floor.exits)
                .FirstOrDefault(candidate => string.Equals(candidate.exitZoneId, selectedId, StringComparison.Ordinal));
            return exitZone != null;
        }

        private bool TryFindSelectedObstacle(string selectedId, out ObstacleData obstacle)
        {
            obstacle = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            obstacle = floors
                .SelectMany(floor => floor.obstacles)
                .FirstOrDefault(candidate => string.Equals(candidate.obstacleId, selectedId, StringComparison.Ordinal));
            return obstacle != null;
        }

        private bool TryFindSelectedStair(string selectedId, out StairPortalData stairPortal)
        {
            stairPortal = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            stairPortal = floors
                .SelectMany(floor => floor.stairPortals)
                .FirstOrDefault(candidate => string.Equals(candidate.stairPortalId, selectedId, StringComparison.Ordinal));
            return stairPortal != null;
        }

        private bool TryFindSelectedDoor(string selectedId, out DoorData door)
        {
            door = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            door = floors
                .SelectMany(floor => floor.doors)
                .FirstOrDefault(candidate => string.Equals(candidate.doorId, selectedId, StringComparison.Ordinal));
            return door != null;
        }

        private bool TryFindSelectedTeleport(string selectedId, out TeleportPortalData teleportPortal)
        {
            teleportPortal = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            teleportPortal = floors
                .SelectMany(floor => floor.teleportPortals)
                .FirstOrDefault(candidate => string.Equals(candidate.teleportPortalId, selectedId, StringComparison.Ordinal));
            return teleportPortal != null;
        }

        private bool IsBrokenTeleport(TeleportPortalData teleportPortal)
        {
            if (teleportPortal == null ||
                string.IsNullOrWhiteSpace(teleportPortal.targetFloorId) ||
                string.IsNullOrWhiteSpace(teleportPortal.targetTeleportPortalId))
            {
                return true;
            }

            var targetFloor = workspaceService?.ActiveProject?.floors?.FirstOrDefault(candidate =>
                string.Equals(candidate.floorId, teleportPortal.targetFloorId, StringComparison.Ordinal));
            return targetFloor == null || !targetFloor.teleportPortals.Any(candidate =>
                string.Equals(candidate.teleportPortalId, teleportPortal.targetTeleportPortalId, StringComparison.Ordinal));
        }

        private bool TryFindSelectedWindow(string selectedId, out WindowData window)
        {
            window = null;
            var floors = workspaceService?.ActiveProject?.floors;
            if (floors == null)
            {
                return false;
            }

            window = floors
                .SelectMany(floor => floor.windows)
                .FirstOrDefault(candidate => string.Equals(candidate.windowId, selectedId, StringComparison.Ordinal));
            return window != null;
        }

        private void DrawStatusBar()
        {
            GUILayout.BeginArea(statusBarRect, GUIContent.none, GUI.skin.box);
            statusBarCollapsed = DrawCollapseToggle(statusBarRect.width, statusBarCollapsed);
            if (statusBarCollapsed)
            {
                GUILayout.Label(statusBarShell?.StatusMessage ?? "Ready", bodyStyle);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(statusBarShell?.StatusMessage ?? "Ready", bodyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(statusBarShell?.PersistenceSummary ?? "Unsaved", bodyStyle);
            GUILayout.Space(16f);
            GUILayout.Label(statusBarShell?.LifecycleStateLabel ?? "Draft", bodyStyle);
            GUILayout.Space(16f);
            GUILayout.Label(statusBarShell?.ModeLabel ?? "Edit Mode", bodyStyle);
            GUILayout.Space(30f);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(statusBarShell?.RecoveryPromptLabel))
            {
                GUILayout.Label(statusBarShell.RecoveryPromptLabel, bodyStyle);
            }

            GUILayout.EndArea();
        }

        private void DrawNewProjectModal()
        {
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty);

            GUILayout.BeginArea(modalRect, GUIContent.none, GUI.skin.window);
            GUILayout.Label("Start a Sandbox Project", headerStyle);
            GUILayout.Label("Choose a starting template to unlock the tool palette and authoring overlays.", bodyStyle);
            GUILayout.Space(8f);

            DrawActionButton("Create Default Project", () => newProjectDialogShell?.CreateDefaultProject());
            DrawActionButton("Create Blank Project", () => newProjectDialogShell?.CreateBlankProject());

            if (workspaceService?.ActiveProject != null)
            {
                DrawActionButton("Close", () => newProjectDialogShell?.Close());
            }

            GUILayout.Space(10f);
            GUILayout.Label("Tip: after creating a project, import a blueprint path in the Inspector and then pick a wall tool on the left.", bodyStyle);
            GUILayout.EndArea();
        }

        private void DrawInspectorSection(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, subheaderStyle);
        }

        // Draws a small triangle toggle in the panel's top-right corner. The triangle points
        // down (open) when expanded and right (closed) when collapsed. Returns the new state.
        private bool DrawCollapseToggle(float panelWidth, bool collapsed)
        {
            var toggleRect = new Rect(panelWidth - 26f, 6f, 20f, 18f);
            var glyph = collapsed ? "▶" : "▼";
            var tooltip = collapsed ? "Expand panel" : "Collapse panel";
            if (GUI.Button(toggleRect, new GUIContent(glyph, tooltip), collapseToggleStyle ?? GUI.skin.button))
            {
                return !collapsed;
            }

            return collapsed;
        }

        private void DrawActionButton(string label, Action action, bool enabled = true)
        {
            var previousState = GUI.enabled;
            GUI.enabled = enabled;
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                Debug.Log($"SandboxEditorHud: button clicked '{label}', enabled={enabled}");
                try
                {
                    EnsureStorageDirectory();
                    Debug.Log($"SandboxEditorHud: storage directory ensured for '{label}'.");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"SandboxEditorHud: EnsureStorageDirectory failed for '{label}': {exception}");
                }

                try
                {
                    action?.Invoke();
                    Debug.Log($"SandboxEditorHud: action invoked for '{label}'.");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"SandboxEditorHud: action threw for '{label}': {exception}");
                }
            }
            GUI.enabled = previousState;
        }

        private void ActivateFirePlacement()
        {
            if (previewService == null)
            {
                return;
            }

            previewService.ConfigureFirePlacement(true);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceFireOrigin);
        }

        private void ActivateSpawnPlacement()
        {
            if (previewService == null)
            {
                return;
            }

            previewService.ConfigureSpawnPlacement(string.Empty, "Main Preview Layout", true);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceSpawnPoint);
        }

        private void ActivateSpawnBrushPlacement()
        {
            if (previewService == null)
            {
                return;
            }

            previewService.ConfigureSpawnBrush(1f, string.Empty, "Density Brush Layout", true);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PaintSpawnBrush);
        }

        private void ActivateRegionPlacement()
        {
            if (previewService == null)
            {
                return;
            }

            previewService.ConfigureRegionPlacement("Preview Region", RegionSemanticType.SpawnZone);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceRegion);
        }

        private void RefreshDependenciesIfNeeded()
        {
            if (!IsFullyWired)
            {
                RefreshDependencies();
            }
        }

        private void LogWebGlDependencyStatusIfNeeded()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (hasLoggedWebGlDependencyStatus)
            {
                return;
            }

            hasLoggedWebGlDependencyStatus = true;
            Debug.Log(
                "SandboxEditorHud WebGL wiring status: " +
                $"IsFullyWired={IsFullyWired}, " +
                $"TopBar={topBarShell != null}, " +
                $"ToolPalette={toolPaletteShell != null}, " +
                $"FloorTabs={floorTabsBarShell != null}, " +
                $"Inspector={inspectorPanelShell != null}, " +
                $"Legend={visualLegendShell != null}, " +
                $"Validation={validationPanelShell != null}, " +
                $"StatusBar={statusBarShell != null}, " +
                $"NewProjectDialog={newProjectDialogShell != null}, " +
                $"Workspace={workspaceService != null}, " +
                $"InputRouter={inputRouter != null}, " +
                $"Screen={Screen.width}x{Screen.height}");
#endif
        }

        private void LogBrowserActionModeIfNeeded()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (hasLoggedBrowserActionMode || topBarShell == null || inspectorPanelShell == null)
            {
                return;
            }

            hasLoggedBrowserActionMode = true;
            Debug.Log(
                "SandboxEditorHud browser action mode: " +
                $"TopBarUsesBrowserHostedFileActions={topBarShell.UsesBrowserHostedFileActions}, " +
                $"TopBarIsBrowserFileActionBusy={topBarShell.IsBrowserFileActionBusy}, " +
                $"TopBarUsesBrowserPersistenceMode={topBarShell.UsesBrowserPersistenceMode}, " +
                $"InspectorUsesBrowserHostedFileActions={inspectorPanelShell.UsesBrowserHostedFileActions}");
#endif
        }

        private void DrawGuiExceptionFallback()
        {
            var fallbackRect = new Rect(16f, 16f, Mathf.Min(Screen.width - 32f, 720f), Mathf.Min(Screen.height - 32f, 220f));
            GUI.Box(fallbackRect, string.Empty);
            GUILayout.BeginArea(fallbackRect);
            GUILayout.Label("Sandbox HUD runtime error", headerStyle ?? GUI.skin.label);
            GUILayout.Label(
                string.IsNullOrWhiteSpace(lastGuiExceptionMessage)
                    ? "An unknown IMGUI error occurred."
                    : lastGuiExceptionMessage,
                bodyStyle ?? GUI.skin.label);

            if (!IsFullyWired)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Missing dependencies:", subheaderStyle ?? GUI.skin.label);
                foreach (var dependency in BuildMissingDependencyList())
                {
                    GUILayout.Label($"- {dependency}", bodyStyle ?? GUI.skin.label);
                }
            }

            GUILayout.EndArea();
        }

        private string[] BuildMissingDependencyList()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (topBarShell == null) missing.Add(nameof(topBarShell));
            if (toolPaletteShell == null) missing.Add(nameof(toolPaletteShell));
            if (floorTabsBarShell == null) missing.Add(nameof(floorTabsBarShell));
            if (inspectorPanelShell == null) missing.Add(nameof(inspectorPanelShell));
            if (visualLegendShell == null) missing.Add(nameof(visualLegendShell));
            if (validationPanelShell == null) missing.Add(nameof(validationPanelShell));
            if (statusBarShell == null) missing.Add(nameof(statusBarShell));
            if (newProjectDialogShell == null) missing.Add(nameof(newProjectDialogShell));
            if (workspaceService == null) missing.Add(nameof(workspaceService));
            if (inputRouter == null) missing.Add(nameof(inputRouter));
            return missing.ToArray();
        }

        private void EnsureDefaultFieldValues()
        {
            if (string.IsNullOrWhiteSpace(pendingFloorName))
            {
                var floorCount = workspaceService?.ActiveProject?.floors.Count ?? 1;
                pendingFloorName = $"Floor {floorCount + 1}";
            }

            if (string.IsNullOrWhiteSpace(calibrationDistanceText))
            {
                calibrationDistanceText = "10";
            }
        }

        private void EnsureStorageDirectory()
        {
            Directory.CreateDirectory(GetStorageDirectoryPath());
        }

        private void EnsureStyles()
        {
            var labelStyle = GUI.skin?.label ?? GUIStyle.none;
            var buttonStyle = GUI.skin?.button ?? GUIStyle.none;
            buttonPadding ??= new RectOffset(10, 10, 6, 6);

            headerStyle ??= new GUIStyle(labelStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            subheaderStyle ??= new GUIStyle(labelStyle)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            bodyStyle ??= new GUIStyle(labelStyle)
            {
                fontSize = 11,
                wordWrap = true
            };

            activeToolButtonStyle ??= new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
            };

            collapseToggleStyle ??= new GUIStyle(buttonStyle)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            if (activeToolButtonStyle.padding == null)
            {
                activeToolButtonStyle.padding = new RectOffset(buttonPadding.left, buttonPadding.right, buttonPadding.top, buttonPadding.bottom);
            }
            else
            {
                activeToolButtonStyle.padding.left = buttonPadding.left;
                activeToolButtonStyle.padding.right = buttonPadding.right;
                activeToolButtonStyle.padding.top = buttonPadding.top;
                activeToolButtonStyle.padding.bottom = buttonPadding.bottom;
            }
        }

        private void RecalculateLayout()
        {
            var margin = 12f;
            var topBarHeight = topBarCollapsed ? CollapsedPanelHeight : 110f;
            var floorTabsHeight = floorTabsCollapsed ? CollapsedPanelHeight : 60f;
            var statusBarHeight = statusBarCollapsed ? CollapsedPanelHeight : 58f;
            var toolWidth = 180f;
            var inspectorWidth = 340f;
            var toolHeight = toolPaletteCollapsed ? CollapsedPanelHeight : Screen.height - topBarHeight - statusBarHeight - (margin * 4f);
            var validationHeight = validationCollapsed ? CollapsedPanelHeight : 220f;
            var inspectorHeight = inspectorCollapsed
                ? CollapsedPanelHeight
                : Screen.height - topBarHeight - statusBarHeight - validationHeight - (margin * 5f);

            topBarRect = new Rect(margin, margin, Screen.width - (margin * 2f), topBarHeight);
            toolPaletteRect = new Rect(margin, topBarRect.yMax + margin, toolWidth, toolHeight);
            floorTabsRect = new Rect(toolPaletteRect.xMax + margin, topBarRect.yMax + margin, Screen.width - toolWidth - inspectorWidth - (margin * 4f), floorTabsHeight);
            inspectorRect = new Rect(Screen.width - inspectorWidth - margin, topBarRect.yMax + margin, inspectorWidth, inspectorHeight);
            validationRect = new Rect(Screen.width - inspectorWidth - margin, inspectorRect.yMax + margin, inspectorWidth, validationHeight);
            statusBarRect = new Rect(margin, Screen.height - statusBarHeight - margin, Screen.width - (margin * 2f), statusBarHeight);

            var modalWidth = Mathf.Min(480f, Screen.width - 40f);
            var modalHeight = 210f;
            modalRect = new Rect(
                (Screen.width - modalWidth) * 0.5f,
                (Screen.height - modalHeight) * 0.5f,
                modalWidth,
                modalHeight);
        }

        private void UpdateInputCapture()
        {
            if (inputRouter == null)
            {
                return;
            }

            RecalculateLayout();
            var pointer = SandboxInputAdapter.PointerScreenPosition;
            var guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
            var isOverHud = topBarRect.Contains(guiPoint)
                || toolPaletteRect.Contains(guiPoint)
                || floorTabsRect.Contains(guiPoint)
                || inspectorRect.Contains(guiPoint)
                || validationRect.Contains(guiPoint)
                || statusBarRect.Contains(guiPoint)
                || (newProjectDialogShell != null && newProjectDialogShell.IsOpen);

            inputRouter.SetManualOverride(isOverHud ? SandboxInputTarget.UI : SandboxInputTarget.None);
        }

        private string GetStorageDirectoryPath()
        {
            return Path.Combine(Application.persistentDataPath, "SandboxEditor");
        }

        private string GetWorkingProjectPath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "sandbox-working.json");
        }

        private string GetProjectExportPath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "sandbox-export.json");
        }

        private string GetRuntimeExportPath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "sandbox-runtime.json");
        }

        private string GetPreviewImagePath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "sandbox-preview.png");
        }

        private static string GetToolLabel(SandboxToolMode toolMode)
        {
            return toolMode switch
            {
                SandboxToolMode.WallLine => "Wall Line",
                SandboxToolMode.WallBrush => "Wall Brush",
                SandboxToolMode.Teleport => "Teleport",
                SandboxToolMode.SpawnPoint => "Agents",
                SandboxToolMode.SpawnBrush => "Agents Brush",
                _ => toolMode.ToString()
            };
        }

        private static string GetPreviewInteractionLabel(SandboxPreviewInteractionMode interactionMode)
        {
            return interactionMode switch
            {
                SandboxPreviewInteractionMode.PlaceFireOrigin => "Fire",
                SandboxPreviewInteractionMode.PlaceSpawnPoint => "Agents",
                SandboxPreviewInteractionMode.PaintSpawnBrush => "Agents Brush",
                SandboxPreviewInteractionMode.PlaceRegion => "Region",
                _ => "None"
            };
        }
    }
}
