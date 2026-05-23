using System;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxEditorHud : MonoBehaviour
    {
        [SerializeField] private string blueprintImportPath = string.Empty;
        [SerializeField] private string calibrationDistanceText = "10";
        [SerializeField] private string pendingFloorName = "Floor";
        [SerializeField] private bool showLegend = true;
        [SerializeField] private bool showValidation = true;
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

        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle bodyStyle;
        private GUIStyle activeToolButtonStyle;
        private RectOffset buttonPadding;

        private Rect topBarRect;
        private Rect toolPaletteRect;
        private Rect floorTabsRect;
        private Rect inspectorRect;
        private Rect validationRect;
        private Rect statusBarRect;
        private Rect modalRect;

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
        }

        private void OnDisable()
        {
            inputRouter?.SetManualOverride(SandboxInputTarget.None);
        }

        private void OnGUI()
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

        private void DrawTopBar()
        {
            GUILayout.BeginArea(topBarRect, GUIContent.none, GUI.skin.box);
            GUILayout.Label(topBarShell?.Title ?? "Sandbox Editor", headerStyle);

            var projectName = workspaceService?.ActiveProject?.metadata.buildingName;
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "Untitled Project";
            }

            GUILayout.Label($"{projectName}  |  {topBarShell?.LifecycleStateLabel ?? "Draft"}  |  {topBarShell?.ModeLabel ?? "Edit Mode"}", bodyStyle);

            GUILayout.BeginHorizontal();
            DrawActionButton("New", () => topBarShell?.OpenNewProjectDialog());
            DrawActionButton("Save", () => { topBarShell?.SaveProject(GetWorkingProjectPath()); }, workspaceService?.ActiveProject != null);
            DrawActionButton("Load", () => { topBarShell?.LoadProject(GetWorkingProjectPath()); });
            DrawActionButton("Export JSON", () => { topBarShell?.ExportProjectJson(GetProjectExportPath()); }, workspaceService?.ActiveProject != null);
            DrawActionButton("Import JSON", () => { topBarShell?.ImportProjectJson(GetProjectExportPath()); });
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

            GUILayout.Label($"Working files: {GetStorageDirectoryPath()}", bodyStyle);
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

            GUI.Label(new Rect(12f, 10f, toolPaletteRect.width - 24f, 22f), "Tools", headerStyle);

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
            GUILayout.Label("Inspector", headerStyle);
            inspectorScrollPosition = GUILayout.BeginScrollView(inspectorScrollPosition);

            DrawInspectorSection("Workspace");
            var activeFloor = workspaceService?.ActiveFloor;
            var activeProject = workspaceService?.ActiveProject;
            GUILayout.Label($"Project: {(string.IsNullOrWhiteSpace(activeProject?.metadata.buildingName) ? "Untitled Project" : activeProject.metadata.buildingName)}", bodyStyle);
            GUILayout.Label($"Active Floor: {activeFloor?.name ?? "None"}", bodyStyle);
            GUILayout.Label($"Selection Count: {selectionService?.SelectedObjectIds.Count ?? 0}", bodyStyle);
            GUILayout.Label(inspectorPanelShell != null && inspectorPanelShell.IsFullyWired ? "Inspector Wiring: OK" : "Inspector Wiring: Missing Dependencies", bodyStyle);
            if (inspectorPanelShell != null && !inspectorPanelShell.IsFullyWired)
            {
                GUILayout.Label(string.Join(", ", inspectorPanelShell.GetMissingDependencies()), bodyStyle);
            }

            DrawInspectorSection("Blueprint");
            blueprintImportPath = GUILayout.TextField(blueprintImportPath, GUILayout.Height(24f));
            DrawActionButton("Import Blueprint Path", () => { inspectorPanelShell?.ImportBlueprintToActiveFloor(blueprintImportPath); }, activeFloor != null && !string.IsNullOrWhiteSpace(blueprintImportPath));

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
                    DrawActionButton("Spawn Tool", ActivateSpawnPlacement);
                    DrawActionButton("Spawn Brush", ActivateSpawnBrushPlacement);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    DrawActionButton("Region Tool", ActivateRegionPlacement);
                    DrawActionButton("Clear Mode", () => previewService.ClearInteractionMode());
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"Interaction: {previewService.InteractionMode}", bodyStyle);
                    if (!string.IsNullOrWhiteSpace(topBarShell.PreviewSummary))
                    {
                        GUILayout.Label(topBarShell.PreviewSummary, bodyStyle);
                    }
                }
                else
                {
                    GUILayout.Label("Enter preview mode from the top bar to place fire origins, spawn points, regions, and run diagnostics.", bodyStyle);
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

                    var previousColor = GUI.contentColor;
                    GUI.contentColor = entry.color;
                    GUILayout.Label("■", GUILayout.Width(18f));
                    GUI.contentColor = previousColor;
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

            if (!string.IsNullOrWhiteSpace(inspectorPanelShell?.CurrentMeasurementReadout))
            {
                GUILayout.Label(inspectorPanelShell.CurrentMeasurementReadout, bodyStyle);
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

        private void DrawValidationPanel()
        {
            GUILayout.BeginArea(validationRect, GUIContent.none, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Validation", headerStyle);
            GUILayout.FlexibleSpace();
            showValidation = GUILayout.Toggle(showValidation, "Expanded");
            GUILayout.EndHorizontal();

            if (!showValidation)
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

        private void DrawStatusBar()
        {
            GUILayout.BeginArea(statusBarRect, GUIContent.none, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(statusBarShell?.StatusMessage ?? "Ready", bodyStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(statusBarShell?.PersistenceSummary ?? "Unsaved", bodyStyle);
            GUILayout.Space(16f);
            GUILayout.Label(statusBarShell?.LifecycleStateLabel ?? "Draft", bodyStyle);
            GUILayout.Space(16f);
            GUILayout.Label(statusBarShell?.ModeLabel ?? "Edit Mode", bodyStyle);
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

        private void DrawActionButton(string label, Action action, bool enabled = true)
        {
            var previousState = GUI.enabled;
            GUI.enabled = enabled;
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                EnsureStorageDirectory();
                action?.Invoke();
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
                wordWrap = true
            };

            activeToolButtonStyle ??= new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
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
            var topBarHeight = 110f;
            var floorTabsHeight = 60f;
            var statusBarHeight = 58f;
            var toolWidth = 180f;
            var inspectorWidth = 340f;
            var validationHeight = 220f;

            topBarRect = new Rect(margin, margin, Screen.width - (margin * 2f), topBarHeight);
            toolPaletteRect = new Rect(margin, topBarRect.yMax + margin, toolWidth, Screen.height - topBarHeight - statusBarHeight - (margin * 4f));
            floorTabsRect = new Rect(toolPaletteRect.xMax + margin, topBarRect.yMax + margin, Screen.width - toolWidth - inspectorWidth - (margin * 4f), floorTabsHeight);
            inspectorRect = new Rect(Screen.width - inspectorWidth - margin, topBarRect.yMax + margin, inspectorWidth, Screen.height - topBarHeight - statusBarHeight - validationHeight - (margin * 5f));
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
                SandboxToolMode.SpawnPoint => "Spawn Point",
                SandboxToolMode.SpawnBrush => "Spawn Brush",
                _ => toolMode.ToString()
            };
        }
    }
}
