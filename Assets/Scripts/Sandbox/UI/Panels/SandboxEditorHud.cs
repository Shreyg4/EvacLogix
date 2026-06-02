using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            FireStart = 7,
            Wall = 8,
        }

        private enum FloorLevelView
        {
            All = 0,
            Surface = 1,
            Basement = 2,
        }

        private enum ProjectLibraryModalMode
        {
            None = 0,
            Load = 1,
            SaveName = 2,
            ConfirmUnsaved = 3,
            ConfirmDelete = 4,
        }

        private enum FloorCategory
        {
            All = 0,
            Surface = 1,
            Basement = 2,
        }

        [SerializeField] private string blueprintImportPath = string.Empty;
        [SerializeField] private string calibrationDistanceText = "10";
        [SerializeField] private string pendingFloorName = string.Empty;
        [SerializeField] private string selectionSizeXText = string.Empty;
        [SerializeField] private string selectionSizeYText = string.Empty;
        [SerializeField] private string windowEscapeCostText = "1";
        [SerializeField] private string windowEscapeRiskText = "1";
        [SerializeField] private bool showLegend = true;
        [SerializeField] private bool topBarCollapsed;
        [SerializeField] private bool toolPaletteCollapsed;
        [SerializeField] private bool floorTabsCollapsed;
        [SerializeField] private bool inspectorCollapsed;
        [SerializeField] private bool validationCollapsed;
        [SerializeField] private bool statusBarCollapsed;
        [SerializeField] private FloorLevelView selectedFloorLevelView = FloorLevelView.All;
        [SerializeField] private Vector2 toolScrollPosition;
        [SerializeField] private Vector2 floorTabsScrollPosition;
        [SerializeField] private Vector2 inspectorScrollPosition;
        [SerializeField] private Vector2 validationScrollPosition;
        [SerializeField] private Vector2 newProjectScrollPosition;
        [SerializeField] private Vector2 projectLibraryScrollPosition;
        [SerializeField] private ProjectLibraryModalMode projectLibraryModalMode = ProjectLibraryModalMode.None;
        [SerializeField] private string saveProjectNameDraft = string.Empty;
        [SerializeField] private string pendingDeleteProjectId = string.Empty;
        [SerializeField] private string pendingDeleteProjectName = string.Empty;
        [SerializeField] private string projectLibraryMessage = string.Empty;
        [SerializeField] private FloorCategory selectedFloorCategory = FloorCategory.All;

        private SandboxTopBarShell topBarShell;
        private SandboxToolPaletteShell toolPaletteShell;
        private SandboxFloorTabsBarShell floorTabsBarShell;
        private SandboxInspectorPanelShell inspectorPanelShell;
        private SandboxVisualLegendShell visualLegendShell;
        private SandboxValidationPanelShell validationPanelShell;
        private SandboxCameraController cameraController;
        private bool showReturnToMenuConfirm;
        private SandboxStatusBarShell statusBarShell;
        private SandboxNewProjectDialogShell newProjectDialogShell;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxPreviewService previewService;
        private SandboxSelectionService selectionService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxInputRouter inputRouter;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxSemanticObjectAuthoringOverlay semanticObjectAuthoringOverlay;

        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle bodyStyle;
        private GUIStyle activeToolButtonStyle;
        private GUIStyle collapseToggleStyle;
        private GUIStyle panelBoxStyle;
        private GUIStyle insetPanelBoxStyle;
        private GUIStyle modalWindowStyle;
        private GUIStyle noticeStyle;
        private RectOffset buttonPadding;
        private Texture2D solidTexture;
        private Texture2D hudPanelTexture;
        private Texture2D hudInsetPanelTexture;
        private Texture2D modalWindowTexture;

        private const float CollapsedPanelHeight = 30f;
        // Floors panel: fixed chrome (header + category row + add/duplicate/delete row) around the tab
        // viewport, and the min/max the viewport itself is allowed to size to its content.
        private const float FloorTabsChrome = 128f;
        private const float FloorTabsMinViewport = 36f;
        private const float FloorTabsMaxViewport = 132f;
        // Measured during the OnGUI draw pass; RecalculateLayout (which runs from Update, where GUI calls
        // are illegal) reads this cached value to size the floors panel to its content.
        private float lastFloorTabsContentHeight = FloorTabsMinViewport;
        private static readonly Color HudPanelColor = new(0.06f, 0.09f, 0.14f, 1f);
        private static readonly Color HudInsetPanelColor = new(0.08f, 0.12f, 0.18f, 1f);
        private static readonly Color ModalBackdropColor = new(0.02f, 0.03f, 0.05f, 0.38f);
        private static readonly Color ModalWindowColor = new(0.07f, 0.1f, 0.15f, 1f);

        private Rect topBarRect;
        private Rect toolPaletteRect;
        private Rect floorTabsRect;
        private Rect inspectorRect;
        private Rect validationRect;
        private Rect statusBarRect;

        private enum MovablePanel { TopBar, Floors, Tools, Inspector, Validation, StatusBar }

        private const float PanelDragThresholdPixels = 5f;
        // v2: the validation panel moved to a bottom band and the status bar shrank to a bottom-left
        // box; bumping the prefix discards pre-v2 saved positions so everyone gets the new defaults.
        private const string PanelLayoutPrefPrefix = "Sandbox.PanelLayout.v2.";
        private readonly Dictionary<MovablePanel, Vector2> customPanelPositions = new();
        private bool panelLayoutLoaded;
        private bool hasPendingPanelPress;
        private bool isPanelDragActive;
        private MovablePanel pendingPanel;
        private Vector2 panelDragGrabOffset;
        private Vector2 pendingPanelPressPoint;
        private Vector2 panelDragGhostPosition;

        private const float FloorTabDragThresholdPixels = 6f;
        private readonly Dictionary<string, Rect> floorTabRects = new();
        private bool hasPendingFloorTabPress;
        private bool isFloorTabDragActive;
        private string draggedFloorTabId = string.Empty;
        private Vector2 floorTabPressPoint;
        private Vector2 floorTabGhostPosition;
        private string floorTabGhostLabel = string.Empty;
        private Rect modalRect;
        private string selectionEditorTargetId = string.Empty;
        private SelectionEditableKind selectionEditorKind = SelectionEditableKind.None;
        private string openingValidationMessage = string.Empty;
        private string openingValidationTargetId = string.Empty;
        private string obstacleBehaviorSyncedId = string.Empty;
        private float selectionObstacleWeight = 1f;
        private float selectionObstacleSpeedPenalty;
        private string fireBehaviorSyncedId = string.Empty;
        private float selectionFireIntensity = 1f;
        private float selectionFireStartDelay;
        private string selectionFireWidthText = "1";
        private string selectionFireLengthText = "1";
        private bool dimensionsInGridUnits = true;
        private string wallSyncedId = string.Empty;
        private string selectionWallLengthText = "1";
        private bool selectionWallAnchorAtStart = true;
        private string wallLengthError = string.Empty;
        private bool hasLoggedGuiException;
        private string lastGuiExceptionMessage = string.Empty;
        private bool hasLoggedWebGlDependencyStatus;
        private bool hasLoggedBrowserActionMode;
        private float uiScale = 1f;
        private Action pendingUnsavedContinuation;

        private const float MinUiScale = 1f;
        private const float MaxUiScale = 2f;

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
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            semanticObjectAuthoringOverlay = FindAnyObjectByType<SandboxSemanticObjectAuthoringOverlay>();
        }

        private void OnDisable()
        {
            inputRouter?.SetManualOverride(SandboxInputTarget.None);
        }

        private void OnDestroy()
        {
            DestroyGeneratedTexture(hudPanelTexture);
            DestroyGeneratedTexture(hudInsetPanelTexture);
            DestroyGeneratedTexture(modalWindowTexture);
        }

        private void OnGUI()
        {
            var previousMatrix = GUI.matrix;
            try
            {
                RefreshDependenciesIfNeeded();
                EnsureStyles();
                EnsureDefaultFieldValues();
                EnsurePanelLayoutLoaded();
                RecalculateLayout();

                // Panel rects (RecalculateLayout) are in logical/pre-scale coordinates. Event mouse
                // positions are only reported in that same space once GUI.matrix is applied, so drag
                // handling must run after the matrix is set — otherwise it hit-tests physical mouse
                // coords against logical rects and breaks whenever the web UI-size buttons push
                // uiScale above 1.
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));
                ProcessPanelDragEvents();
                DrawTopBar();
                DrawToolPalette();
                DrawFloorTabs();
                DrawInspector();
                DrawValidationPanel();
                DrawStatusBar();
                DrawTransientNotice();
                DrawPanelDragGhost();

                if (newProjectDialogShell != null && newProjectDialogShell.IsOpen)
                {
                    DrawNewProjectModal();
                }

                DrawProjectLibraryModal();
                if (showReturnToMenuConfirm)
                {
                    DrawReturnToMenuModal();
                }
            }
            catch (Exception exception)
            {
                GUI.matrix = previousMatrix;
                lastGuiExceptionMessage = exception.ToString();
                if (!hasLoggedGuiException)
                {
                    hasLoggedGuiException = true;
                    Debug.LogError($"SandboxEditorHud.OnGUI failed: {exception}");
                }

                DrawGuiExceptionFallback();
            }
            finally
            {
                GUI.matrix = previousMatrix;
            }
        }

        public void SetUiScale(string scaleText)
        {
            if (!float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var nextScale))
            {
                return;
            }

            uiScale = Mathf.Clamp(nextScale, MinUiScale, MaxUiScale);
        }

        public void SelectDefaultTool()
        {
            RefreshDependenciesIfNeeded();
            toolPaletteShell?.SelectTool(SandboxToolMode.Select);
        }

        private void DrawTopBar()
        {
            GUILayout.BeginArea(topBarRect, GUIContent.none, panelBoxStyle ?? GUI.skin.box);
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
            DrawActionButton("New", RequestNewProject);

            if (topBarShell != null && topBarShell.UsesBrowserHostedFileActions)
            {
                DrawActionButton("Save", () => { SaveCurrentBrowserProject(); }, workspaceService?.ActiveProject != null);
                DrawActionButton("Load", RequestLoadBrowserProject, topBarShell.HasSavedBrowserProjects);
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
            DrawActionButton("Reset Layout", ResetPanelLayout);

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

            GUILayout.FlexibleSpace();
            DrawActionButton("Simulate", LaunchSimulation, workspaceService?.ActiveProject != null);
            DrawActionButton("Main Menu", () => showReturnToMenuConfirm = true);
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
            var boxStyle = panelBoxStyle ?? GUI.skin?.box ?? GUIStyle.none;
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
            GUILayout.BeginArea(floorTabsRect, GUIContent.none, panelBoxStyle ?? GUI.skin.box);
            floorTabsCollapsed = DrawCollapseToggle(floorTabsRect.width, floorTabsCollapsed);
            if (floorTabsCollapsed)
            {
                GUILayout.Label("Floors", subheaderStyle);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Floors", subheaderStyle, GUILayout.Width(50f));
            DrawFloorCategoryButton(FloorCategory.All, "All");
            DrawFloorCategoryButton(FloorCategory.Surface, "Surface");
            DrawFloorCategoryButton(FloorCategory.Basement, "Basement");
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            var visibleTabs = GetVisibleFloorTabs();
            var tabsViewportHeight = Mathf.Max(FloorTabsMinViewport, floorTabsRect.height - FloorTabsChrome);
            var tabsViewportRect = GUILayoutUtility.GetRect(floorTabsRect.width - 24f, tabsViewportHeight);
            var tabsContentWidth = Mathf.Max(0f, tabsViewportRect.width - 18f);
            var tabsContentHeight = CalculateFloorTabsContentHeight(visibleTabs, tabsContentWidth);
            lastFloorTabsContentHeight = tabsContentHeight;
            var tabsContentRect = new Rect(0f, 0f, tabsContentWidth, tabsContentHeight);

            // Only show the vertical scrollbar when the list actually overflows the viewport.
            floorTabsScrollPosition = GUI.BeginScrollView(tabsViewportRect, floorTabsScrollPosition, tabsContentRect, false, false);
            DrawWrappedFloorTabs(visibleTabs, tabsContentWidth);
            GUI.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            pendingFloorName = GUILayout.TextField(pendingFloorName, GUILayout.Width(120f));
            DrawActionButton("Add Floor", () => { floorTabsBarShell?.AddFloor(pendingFloorName, ResolveNewFloorElevation()); }, workspaceService?.ActiveProject != null);

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

        private void DrawFloorCategoryButton(FloorCategory category, string label)
        {
            var isSelected = selectedFloorCategory == category;
            var style = isSelected ? activeToolButtonStyle : GUI.skin.button;
            var labelContent = new GUIContent(label);
            var buttonWidth = Mathf.Max(70f, style.CalcSize(labelContent).x + 12f);
            if (GUILayout.Button(labelContent, style, GUILayout.Width(buttonWidth), GUILayout.Height(28f)))
            {
                selectedFloorCategory = category;
                floorTabsScrollPosition = Vector2.zero;
            }
        }

        private float ResolveNewFloorElevation()
        {
            const float floorHeight = 3f;

            if (selectedFloorCategory != FloorCategory.Basement)
            {
                return 0f;
            }

            var lowestBasementElevation = floorTabsBarShell?.FloorTabs?
                .Where(tab => tab.elevation < 0f)
                .Select(tab => tab.elevation)
                .DefaultIfEmpty(0f)
                .Min() ?? 0f;

            return lowestBasementElevation - floorHeight;
        }

        private IReadOnlyList<SandboxFloorTabEntry> GetVisibleFloorTabs()
        {
            if (floorTabsBarShell?.FloorTabs == null)
            {
                return Array.Empty<SandboxFloorTabEntry>();
            }

            return floorTabsBarShell.FloorTabs
                .Where(tab => IsFloorVisibleInCategory(tab))
                .ToList();
        }

        private bool IsFloorVisibleInCategory(SandboxFloorTabEntry tab)
        {
            return selectedFloorCategory switch
            {
                FloorCategory.Surface => tab.elevation >= 0f || Mathf.Approximately(tab.elevation, 0f),
                FloorCategory.Basement => tab.elevation < 0f,
                _ => true,
            };
        }

        private void DrawWrappedFloorTabs(IReadOnlyList<SandboxFloorTabEntry> visibleTabs, float availableWidth)
        {
            var buttonStyle = GUI.skin?.button ?? GUIStyle.none;
            var activeButtonStyle = activeToolButtonStyle ?? buttonStyle;
            var x = 0f;
            var y = 0f;
            var rowHeight = 32f;
            var spacing = 6f;

            if (visibleTabs.Count == 0)
            {
                GUI.Label(new Rect(0f, 0f, Mathf.Max(140f, availableWidth), 24f), "No floors in this category.", bodyStyle);
                return;
            }

            for (var index = 0; index < visibleTabs.Count; index += 1)
            {
                var tab = visibleTabs[index];
                var style = tab.isActive ? activeButtonStyle : buttonStyle;
                var label = new GUIContent(tab.name);
                var size = style.CalcSize(label);
                var buttonWidth = Mathf.Clamp(size.x + 26f, 72f, Mathf.Max(72f, availableWidth));

                if (x > 0f && x + buttonWidth > availableWidth)
                {
                    x = 0f;
                    y += rowHeight + spacing;
                }

                var buttonRect = new Rect(x, y, buttonWidth, rowHeight);
                if (GUI.Button(buttonRect, label, style))
                {
                    floorTabsBarShell?.SelectFloor(tab.floorId);
                }

                x += buttonWidth + spacing;
            }
        }

        private float CalculateFloorTabsContentHeight(IReadOnlyList<SandboxFloorTabEntry> visibleTabs, float availableWidth)
        {
            if (visibleTabs.Count == 0)
            {
                return 32f;
            }

            var buttonStyle = GUI.skin?.button ?? GUIStyle.none;
            var rowHeight = 32f;
            var spacing = 6f;
            var x = 0f;
            var rows = 1;

            for (var index = 0; index < visibleTabs.Count; index += 1)
            {
                var tab = visibleTabs[index];
                var size = buttonStyle.CalcSize(new GUIContent(tab.name));
                var buttonWidth = Mathf.Clamp(size.x + 26f, 72f, Mathf.Max(72f, availableWidth));
                if (x > 0f && x + buttonWidth > availableWidth)
                {
                    rows += 1;
                    x = 0f;
                }

                x += buttonWidth + spacing;
            }

            return (rows * rowHeight) + ((rows - 1) * spacing);
        }

        private void DrawInspector()
        {
            GUILayout.BeginArea(inspectorRect, GUIContent.none, panelBoxStyle ?? GUI.skin.box);
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

            var snappingRelevant = toolPaletteShell != null && (
                toolPaletteShell.IsToolActive(SandboxToolMode.WallLine) ||
                toolPaletteShell.IsToolActive(SandboxToolMode.WallBrush) ||
                toolPaletteShell.IsToolActive(SandboxToolMode.Exit) ||
                toolPaletteShell.IsToolActive(SandboxToolMode.Obstacle) ||
                toolPaletteShell.IsToolActive(SandboxToolMode.Teleport) ||
                (selectionService != null && selectionService.SelectedObjectIds.Count == 1));
            if (inspectorPanelShell != null && snappingRelevant)
            {
                DrawInspectorSection("Snapping");
                var snappingEnabled = GUILayout.Toggle(inspectorPanelShell.SnappingEnabled, "Snapping");
                if (snappingEnabled != inspectorPanelShell.SnappingEnabled)
                {
                    inspectorPanelShell.SetSnappingEnabled(snappingEnabled);
                }
            }

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
                    DrawActionButton("Spawn Point", ActivateSpawnPlacement);
                    DrawActionButton("Spawn Point Brush", ActivateSpawnPointBrushPlacement);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    DrawActionButton("Clear Mode", () => previewService.ClearInteractionMode());
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"Interaction: {GetPreviewInteractionLabel(previewService.InteractionMode)}", bodyStyle);
                    if (!string.IsNullOrWhiteSpace(topBarShell.PreviewSummary))
                    {
                        GUILayout.Label(topBarShell.PreviewSummary, bodyStyle);
                    }

                    DrawSimulationRunReports();
                }
                else
                {
                    GUILayout.Label("Enter preview mode from the top bar to place spawn points and run diagnostics.", bodyStyle);
                    DrawSimulationRunReports();
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

            GUILayout.BeginVertical(insetPanelBoxStyle ?? GUI.skin.box);
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
            GUILayout.BeginArea(validationRect, GUIContent.none, panelBoxStyle ?? GUI.skin.box);
            validationCollapsed = DrawCollapseToggle(validationRect.width, validationCollapsed);
            GUILayout.Label("Validation", headerStyle);

            if (validationCollapsed)
            {
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();

            // Left control column: actions + room overlay controls.
            GUILayout.BeginVertical(GUILayout.Width(196f));
            GUILayout.BeginHorizontal();
            DrawActionButton("Refresh", () => validationPanelShell?.RefreshValidation(), workspaceService?.ActiveProject != null);
            DrawActionButton("Rebuild All", () => validationPanelShell?.RebuildAll(), workspaceService?.ActiveProject != null);
            GUILayout.EndHorizontal();

            // View-recovery: snap the camera back to world origin at default zoom. Always available
            // since it only affects the view, not project state.
            DrawActionButton("Move to Origin", () => (cameraController ??= FindAnyObjectByType<SandboxCameraController>())?.MoveToOrigin(), true);

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

                if (!string.IsNullOrWhiteSpace(validationPanelShell.PreviewPlacementMessage))
                {
                    GUILayout.Label(validationPanelShell.PreviewPlacementMessage, bodyStyle);
                }

                GUILayout.BeginHorizontal();
                DrawActionButton("Refresh Rooms", () => validationPanelShell.RefreshCompleteRooms(), validationPanelShell.ShowCompleteRooms);
                GUILayout.Label(validationPanelShell.RoomDetectionStatus, bodyStyle);
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    $"Rooms: sealed {validationPanelShell.SealedRoomCount}, penetrated {validationPanelShell.PenetratedRoomCount}.",
                    bodyStyle);
            }

            GUILayout.EndVertical();

            GUILayout.Space(10f);

            // Right side: the issue list fills the remaining (wide) area at full band height.
            GUILayout.BeginVertical();
            validationScrollPosition = GUILayout.BeginScrollView(validationScrollPosition);
            var hasIssues = false;
            foreach (var floorGroup in validationPanelShell?.IssueGroups ?? Enumerable.Empty<SandboxValidationFloorGroup>())
            {
                hasIssues = true;
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

            if (!hasIssues)
            {
                GUILayout.Label("No issues to display. Run Refresh to validate the project.", bodyStyle);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
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
            DrawObjectLockToggle(selectedId);
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
                SyncWindowEditorState(selectedId, window);
                DrawWindowFields(window);
                return;
            }

            if (TryFindSelectedFireOrigin(selectedId, out var fireOrigin))
            {
                selectionEditorKind = SelectionEditableKind.FireStart;
                if (!string.Equals(fireBehaviorSyncedId, selectedId, StringComparison.Ordinal))
                {
                    fireBehaviorSyncedId = selectedId;
                    selectionFireIntensity = fireOrigin.spreadIntensity;
                    selectionFireStartDelay = fireOrigin.startDelaySeconds;
                    selectionFireWidthText = FormatDimension(WorldToDisplayDimension(fireOrigin.size.x));
                    selectionFireLengthText = FormatDimension(WorldToDisplayDimension(fireOrigin.size.y));
                }

                DrawFireStartFields(fireOrigin);
                return;
            }

            if (TryFindSelectedWall(selectedId, out var wall))
            {
                selectionEditorKind = SelectionEditableKind.Wall;
                if (!string.Equals(wallSyncedId, selectedId, StringComparison.Ordinal))
                {
                    wallSyncedId = selectedId;
                    var worldLength = Vector2.Distance(wall.startPoint, wall.endPoint);
                    selectionWallLengthText = FormatDimension(WorldToDisplayDimension(worldLength));
                    selectionWallAnchorAtStart = ResolveDefaultWallAnchor(wall);
                    wallLengthError = string.Empty;
                }

                DrawWallFields(wall);
                return;
            }

            ResetSelectionEditorState();
            GUILayout.Label("The current selection does not expose editable size controls yet.", bodyStyle);
        }

        // Per-object editor edit-lock toggle shown at the top of every selected entity's inspector.
        // This is the EDITOR lock (prevents moving/resizing/editing/deleting), distinct from any
        // in-simulation state such as a door's DoorState.Locked. Fields stay active; edit attempts
        // are rejected by the authoring services with a "locked" status message.
        private void DrawObjectLockToggle(string objectId)
        {
            if (visualOrganizationService == null || string.IsNullOrWhiteSpace(objectId))
            {
                return;
            }

            var isLocked = visualOrganizationService.IsObjectLocked(objectId);
            var nextLocked = GUILayout.Toggle(isLocked, "Lock editing");
            if (nextLocked != isLocked)
            {
                visualOrganizationService.SetObjectLocked(objectId, nextLocked);
            }

            if (nextLocked)
            {
                GUILayout.Label("Locked: move, resize, edit, and delete are blocked. Untick to edit.", bodyStyle);
            }
        }

        private bool TryFindSelectedWall(string selectedId, out WallSegmentData wall)
        {
            wall = null;
            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return false;
            }

            wall = floor.wallSegments.FirstOrDefault(candidate =>
                string.Equals(candidate.wallSegmentId, selectedId, StringComparison.Ordinal));
            return wall != null;
        }

        private bool ResolveDefaultWallAnchor(WallSegmentData wall)
        {
            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return true;
            }

            var startShared = IsJunctionShared(floor, wall.startJunctionId, wall.wallSegmentId);
            var endShared = IsJunctionShared(floor, wall.endJunctionId, wall.wallSegmentId);
            if (endShared && !startShared)
            {
                return false; // anchor the shared end, move the free start
            }

            return true; // default: anchor start, move end
        }

        private static bool IsJunctionShared(FloorData floor, string junctionId, string wallSegmentId)
        {
            var junction = floor.wallJunctions.FirstOrDefault(candidate =>
                string.Equals(candidate.wallJunctionId, junctionId, StringComparison.Ordinal));
            return junction != null && junction.connectedWallSegmentIds.Any(id =>
                !string.Equals(id, wallSegmentId, StringComparison.Ordinal));
        }

        private void DrawSimulationRunReports()
        {
            if (topBarShell == null)
            {
                return;
            }

            var report = topBarShell.LastSimulationRunReport;
            if (report?.summary == null || !report.summary.completedSuccessfully)
            {
                GUILayout.Label("Simulation Reports: Run a successful preview simulation to generate reports.", bodyStyle);
                return;
            }

            GUILayout.Label(
                $"Simulation Summary: {report.summary.evacuatedAgents} evacuated, {report.summary.injuredAgents} injured, {report.summary.deadAgents} dead.",
                bodyStyle);

            foreach (var floorOutcome in report.summary.floorOutcomes)
            {
                if (floorOutcome.spawnedAgents == 0 && floorOutcome.evacuatedAgents == 0 && floorOutcome.injuredAgents == 0 && floorOutcome.deadAgents == 0)
                {
                    continue;
                }

                GUILayout.Label(
                    $"{floorOutcome.floorName}: {floorOutcome.evacuatedAgents} evacuated, {floorOutcome.injuredAgents} injured, {floorOutcome.deadAgents} dead.",
                    bodyStyle);
            }

            GUILayout.Label($"Travel Density Heatmap: {report.travelDensity?.cells.Count ?? 0} occupied cells across floors.", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawActionButton("Export Summary", () => topBarShell.ExportSimulationSummaryReport(GetSimulationSummaryReportPath()), topBarShell.HasCompletedSimulationRunReport);
            DrawActionButton("Export Heatmap", () => topBarShell.ExportSimulationTravelDensityHeatmapReport(GetSimulationHeatmapReportPath()), topBarShell.HasCompletedSimulationRunReport);
            GUILayout.EndHorizontal();
        }

        private void DrawWallFields(WallSegmentData wall)
        {
            GUILayout.Label($"Wall: {wall.wallSegmentId}", bodyStyle);
            DrawDimensionUnitToggle();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Length ({DimensionUnitLabel})", bodyStyle, GUILayout.Width(96f));
            selectionWallLengthText = GUILayout.TextField(selectionWallLengthText, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            var anchorDesc = selectionWallAnchorAtStart ? "Start (green square)" : "End (blue diamond)";
            var movingDesc = selectionWallAnchorAtStart ? "End (blue diamond)" : "Start (green square)";
            var anchorPoint = selectionWallAnchorAtStart ? wall.startPoint : wall.endPoint;
            var movingPoint = selectionWallAnchorAtStart ? wall.endPoint : wall.startPoint;
            GUILayout.Label($"Anchored (fixed): {anchorDesc}  ({anchorPoint.x:0.0}, {anchorPoint.y:0.0})", bodyStyle);
            GUILayout.Label($"Adjusted (moves): {movingDesc}  ({movingPoint.x:0.0}, {movingPoint.y:0.0})", bodyStyle);

            GUILayout.BeginHorizontal();
            DrawActionButton("Apply Length", () => TryApplyWallLength(wall), inspectorPanelShell != null);
            DrawActionButton("Flip Ends", () => selectionWallAnchorAtStart = !selectionWallAnchorAtStart);
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(wallLengthError))
            {
                GUILayout.Label(wallLengthError, bodyStyle);
            }
        }

        private void TryApplyWallLength(WallSegmentData wall)
        {
            if (wall == null || inspectorPanelShell == null)
            {
                return;
            }

            if (!float.TryParse(selectionWallLengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var displayLength))
            {
                wallLengthError = "Enter a valid number for length.";
                return;
            }

            var worldLength = DisplayToWorldDimension(displayLength);
            if (inspectorPanelShell.TrySetWallLength(wall.wallSegmentId, worldLength, selectionWallAnchorAtStart, out var error, out var minWorldLength, out var offenderLabel))
            {
                wallLengthError = string.Empty;
                return;
            }

            if (!string.IsNullOrEmpty(offenderLabel))
            {
                // Express the rejection in the same unit shown in the Length field so the numbers
                // line up with what the user typed (e.g. grid vs feet).
                var unit = DimensionUnitLabel;
                wallLengthError =
                    $"Can't set length to {FormatDimension(displayLength)} {unit}: {offenderLabel} on this wall would fall off. " +
                    $"Minimum length {FormatDimension(WorldToDisplayDimension(minWorldLength))} {unit}.";
            }
            else
            {
                wallLengthError = error;
            }
        }

        private bool TryFindSelectedFireOrigin(string selectedId, out FireOriginData fireOrigin)
        {
            fireOrigin = null;
            var floor = workspaceService?.ActiveFloor;
            var project = workspaceService?.ActiveProject;
            if (floor == null || project == null)
            {
                return false;
            }

            fireOrigin = project.fireOrigins.FirstOrDefault(origin =>
                origin.floorId == floor.floorId && origin.fireOriginId == selectedId);
            return fireOrigin != null;
        }

        private void DrawFireStartFields(FireOriginData fireOrigin)
        {
            GUILayout.Label($"Fire Start: {fireOrigin.fireOriginId}", bodyStyle);
            DrawDimensionUnitToggle();
            var unit = DimensionUnitLabel;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Width ({unit})", bodyStyle, GUILayout.Width(78f));
            selectionFireWidthText = GUILayout.TextField(selectionFireWidthText, GUILayout.Width(64f));
            GUILayout.Label($"Length ({unit})", bodyStyle, GUILayout.Width(86f));
            selectionFireLengthText = GUILayout.TextField(selectionFireLengthText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();
            GUILayout.Label($"Spread Intensity: {selectionFireIntensity:0.00}  (higher = faster, farther spread)", bodyStyle);
            selectionFireIntensity = Mathf.Clamp(GUILayout.HorizontalSlider(selectionFireIntensity, 0.1f, 5f), 0.1f, 5f);
            GUILayout.Label($"Start Delay: {selectionFireStartDelay:0.0}s  (seconds before ignition)", bodyStyle);
            selectionFireStartDelay = Mathf.Max(0f, GUILayout.HorizontalSlider(selectionFireStartDelay, 0f, 30f));
            DrawActionButton("Apply Fire Settings", () => TryApplyFireStart(fireOrigin), inspectorPanelShell != null);
        }

        private bool TryApplyFireStart(FireOriginData fireOrigin)
        {
            if (fireOrigin == null || inspectorPanelShell == null)
            {
                return false;
            }

            // Parse the typed Width/Length (in the active unit). If both parse, set the ellipse size;
            // otherwise preserve the current size so an intensity/delay-only edit keeps the shape.
            Vector2? size = null;
            if (float.TryParse(selectionFireWidthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var widthDisplay) &&
                float.TryParse(selectionFireLengthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var lengthDisplay))
            {
                size = new Vector2(
                    Mathf.Max(0.2f, DisplayToWorldDimension(widthDisplay)),
                    Mathf.Max(0.2f, DisplayToWorldDimension(lengthDisplay)));
            }

            return inspectorPanelShell.UpdateFireOrigin(
                fireOrigin.fireOriginId,
                fireOrigin.position,
                selectionFireIntensity,
                selectionFireStartDelay,
                fireOrigin.isPersistent,
                size);
        }

        // ---- Shared dimension unit (Grid <-> distance) used by fire size and wall length ----

        private float DimensionGridSize => inspectorPanelShell != null ? inspectorPanelShell.CurrentGridSize : 0.5f;

        private DistanceUnit DimensionDistanceUnit =>
            workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;

        private string DimensionUnitLabel =>
            dimensionsInGridUnits ? "grid" : SandboxDistanceUnitUtility.GetAbbreviation(DimensionDistanceUnit);

        private float WorldToDisplayDimension(float world) =>
            dimensionsInGridUnits ? world / Mathf.Max(0.05f, DimensionGridSize) : world;

        private float DisplayToWorldDimension(float display) =>
            dimensionsInGridUnits ? display * Mathf.Max(0.05f, DimensionGridSize) : display;

        private static string FormatDimension(float value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);

        private void DrawDimensionUnitToggle()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Units: {(dimensionsInGridUnits ? "Grid" : SandboxDistanceUnitUtility.GetLabel(DimensionDistanceUnit))}", bodyStyle);
            var swapLabel = dimensionsInGridUnits
                ? $"Use {SandboxDistanceUnitUtility.GetLabel(DimensionDistanceUnit)}"
                : "Use Grid";
            if (GUILayout.Button(swapLabel, GUILayout.Height(22f)))
            {
                dimensionsInGridUnits = !dimensionsInGridUnits;
                // Force the dimensioned fields to re-sync their text in the new unit.
                fireBehaviorSyncedId = string.Empty;
                wallSyncedId = string.Empty;
            }

            GUILayout.EndHorizontal();
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
            DrawActionButton("Locked (door)", () => TryApplyDoor(door, DoorState.Locked), door.state != DoorState.Locked);
            GUILayout.EndHorizontal();

            var canApplyWidth = inspectorPanelShell != null && TryParseSelectionWidth(out _);
            DrawActionButton("Apply Door Width", () => TryApplyDoor(door, door.state), canApplyWidth);
            GUILayout.Label("Normal/Closed doors create passable collider gaps. Blocked/Locked doors remain blocked.", bodyStyle);
            DrawOpeningValidationMessage(door.doorId);
        }

        private void DrawWindowFields(WindowData window)
        {
            GUILayout.Label($"Window: {window.windowId}", bodyStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Width", bodyStyle, GUILayout.Width(42f));
            selectionSizeXText = GUILayout.TextField(selectionSizeXText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawActionButton("Escape Off", () => TryApplyWindow(window, false), window.canBeUsedForEscape);
            DrawActionButton("Escape On", () => TryApplyWindow(window, true), !window.canBeUsedForEscape);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Cost", bodyStyle, GUILayout.Width(42f));
            windowEscapeCostText = GUILayout.TextField(windowEscapeCostText, GUILayout.Width(64f));
            GUILayout.Label("Risk", bodyStyle, GUILayout.Width(42f));
            windowEscapeRiskText = GUILayout.TextField(windowEscapeRiskText, GUILayout.Width(64f));
            GUILayout.EndHorizontal();

            var escapeUsable = GUILayout.Toggle(window.canBeUsedForEscape, "Escape Usable");
            if (escapeUsable != window.canBeUsedForEscape)
            {
                TryApplyWindow(window, escapeUsable);
            }

            var canApplyWindowSettings = inspectorPanelShell != null && TryParseWindowSettings(out _, out _, out _);
            DrawActionButton("Apply Window Settings", () => TryApplyWindow(window, window.canBeUsedForEscape), canApplyWindowSettings);
            GUILayout.Label("Only escape-usable windows create passable collider gaps. Cost and risk tune escape-window traversal.", bodyStyle);
            DrawOpeningValidationMessage(window.windowId);
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
            DrawTeleportTargetFloorControls(teleportPortal);

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
        }

        private void DrawTeleportTargetFloorControls(TeleportPortalData teleportPortal)
        {
            var floors = workspaceService?.ActiveProject?.floors?
                .OrderBy(floor => floor.order)
                .ThenBy(floor => floor.name, StringComparer.Ordinal)
                .ToList();
            if (floors == null || floors.Count <= 1)
            {
                GUILayout.Label("Add another floor before setting a teleport target.", bodyStyle);
                return;
            }

            var sourceFloorId = ResolveTeleportSourceFloorId(teleportPortal);
            var linkedFloorName = ResolveFloorName(teleportPortal.targetFloorId);
            GUILayout.Label(
                string.IsNullOrWhiteSpace(linkedFloorName)
                    ? "Target Floor: Not set"
                    : $"Target Floor: {linkedFloorName}",
                bodyStyle);

            GUILayout.BeginHorizontal();
            foreach (var floor in floors)
            {
                if (string.Equals(floor.floorId, sourceFloorId, StringComparison.Ordinal))
                {
                    continue;
                }

                DrawActionButton(
                    floor.name,
                    () => TrySetTeleportTargetFloor(teleportPortal, floor.floorId),
                    !string.Equals(teleportPortal.targetFloorId, floor.floorId, StringComparison.Ordinal));
            }
            GUILayout.EndHorizontal();
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

        private bool TrySetTeleportTargetFloor(TeleportPortalData teleportPortal, string targetFloorId)
        {
            if (teleportPortal == null || string.IsNullOrWhiteSpace(targetFloorId))
            {
                return false;
            }

            return inspectorPanelShell != null && inspectorPanelShell.SetTeleportTargetFloor(teleportPortal.teleportPortalId, targetFloorId);
        }

        private bool TryApplyDoor(DoorData door, DoorState state)
        {
            if (door == null)
            {
                return false;
            }

            if (!TryParseSelectionWidth(out var width))
            {
                SetOpeningValidationMessage(door.doorId, "Invalid door/window size: width must be a positive number.");
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateDoor(
                door.doorId,
                width,
                door.wallSegmentId,
                door.offsetAlongWall,
                state,
                door.tags,
                door.metadataFields);
            if (didUpdate)
            {
                ClearOpeningValidationMessage(door.doorId);
                SyncSelectionEditorState(door.doorId, SelectionEditableKind.Door, new Vector2(width, 0f), true);
            }
            else if (semanticObjectAuthoringService != null &&
                     semanticObjectAuthoringService.TryGetDoorValidationError(door.doorId, width, door.offsetAlongWall, out var errorMessage) &&
                     !string.IsNullOrWhiteSpace(errorMessage))
            {
                SetOpeningValidationMessage(door.doorId, errorMessage);
            }

            return didUpdate;
        }

        private bool TryApplyWindow(WindowData window, bool canBeUsedForEscape)
        {
            if (window == null)
            {
                return false;
            }

            if (!TryParseWindowSettings(out var width, out var escapeCost, out var escapeRiskMultiplier))
            {
                SetOpeningValidationMessage(window.windowId, "Invalid door/window size: width, cost, and risk must be valid non-negative numbers.");
                return false;
            }

            var didUpdate = inspectorPanelShell != null && inspectorPanelShell.UpdateWindow(
                window.windowId,
                width,
                window.wallSegmentId,
                window.offsetAlongWall,
                canBeUsedForEscape,
                escapeCost,
                escapeRiskMultiplier,
                window.tags,
                window.metadataFields);
            if (didUpdate)
            {
                window.canBeUsedForEscape = canBeUsedForEscape;
                window.escapeCost = escapeCost;
                window.escapeRiskMultiplier = escapeRiskMultiplier;
                ClearOpeningValidationMessage(window.windowId);
                SyncWindowEditorState(window.windowId, window, true);
            }
            else if (semanticObjectAuthoringService != null &&
                     semanticObjectAuthoringService.TryGetWindowValidationError(window.windowId, width, window.offsetAlongWall, out var errorMessage) &&
                     !string.IsNullOrWhiteSpace(errorMessage))
            {
                SetOpeningValidationMessage(window.windowId, errorMessage);
            }

            return didUpdate;
        }

        private void SyncWindowEditorState(string targetId, WindowData window, bool force = false)
        {
            if (window == null)
            {
                return;
            }

            SyncSelectionEditorState(targetId, SelectionEditableKind.Window, new Vector2(window.width, 0f), force);
            windowEscapeCostText = window.escapeCost.ToString("0.###");
            windowEscapeRiskText = window.escapeRiskMultiplier.ToString("0.###");
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
            windowEscapeCostText = "1";
            windowEscapeRiskText = "1";
            openingValidationTargetId = string.Empty;
            openingValidationMessage = string.Empty;
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

        private bool TryParseWindowSettings(out float width, out float escapeCost, out float escapeRiskMultiplier)
        {
            width = 0f;
            escapeCost = 0f;
            escapeRiskMultiplier = 0f;
            return TryParseSelectionWidth(out width) &&
                float.TryParse(windowEscapeCostText, out escapeCost) &&
                float.TryParse(windowEscapeRiskText, out escapeRiskMultiplier) &&
                escapeCost >= 0f &&
                escapeRiskMultiplier >= 0f;
        }

        private void DrawOpeningValidationMessage(string openingId)
        {
            if (string.IsNullOrWhiteSpace(openingId) ||
                !string.Equals(openingValidationTargetId, openingId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(openingValidationMessage))
            {
                return;
            }

            var previousColor = GUI.color;
            GUI.color = new Color(1f, 0.72f, 0.3f, 1f);
            GUILayout.Label(openingValidationMessage, bodyStyle);
            GUI.color = previousColor;
        }

        private void SetOpeningValidationMessage(string openingId, string message)
        {
            openingValidationTargetId = openingId ?? string.Empty;
            openingValidationMessage = message ?? string.Empty;
            if (statusBarShell != null && !string.IsNullOrWhiteSpace(openingValidationMessage))
            {
                statusBarShell.StatusMessage = openingValidationMessage;
            }
        }

        private void ClearOpeningValidationMessage(string openingId)
        {
            if (!string.Equals(openingValidationTargetId, openingId, StringComparison.Ordinal))
            {
                return;
            }

            openingValidationTargetId = string.Empty;
            openingValidationMessage = string.Empty;
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

        private string ResolveTeleportSourceFloorId(TeleportPortalData teleportPortal)
        {
            if (teleportPortal == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(teleportPortal.sourceFloorId))
            {
                return teleportPortal.sourceFloorId;
            }

            var sourceFloor = workspaceService?.ActiveProject?.floors?.FirstOrDefault(floor =>
                floor.teleportPortals.Any(candidate =>
                    string.Equals(candidate.teleportPortalId, teleportPortal.teleportPortalId, StringComparison.Ordinal)));
            return sourceFloor?.floorId ?? string.Empty;
        }

        private string ResolveFloorName(string floorId)
        {
            if (string.IsNullOrWhiteSpace(floorId))
            {
                return string.Empty;
            }

            return workspaceService?.ActiveProject?.floors?
                .FirstOrDefault(floor => string.Equals(floor.floorId, floorId, StringComparison.Ordinal))
                ?.name ?? string.Empty;
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
            GUILayout.BeginArea(statusBarRect, GUIContent.none, panelBoxStyle ?? GUI.skin.box);
            statusBarCollapsed = DrawCollapseToggle(statusBarRect.width, statusBarCollapsed);
            if (statusBarCollapsed)
            {
                GUILayout.Label(statusBarShell?.StatusMessage ?? "Ready", bodyStyle);
                GUILayout.EndArea();
                return;
            }

            // Slim bottom-left box: transient message on top, a compact state line beneath.
            var labelWidth = Mathf.Max(40f, statusBarRect.width - 12f);
            GUILayout.Label(statusBarShell?.StatusMessage ?? "Ready", bodyStyle, GUILayout.Width(labelWidth));
            var persistence = statusBarShell?.PersistenceSummary ?? "Unsaved";
            var lifecycle = statusBarShell?.LifecycleStateLabel ?? "Draft";
            var mode = statusBarShell?.ModeLabel ?? "Edit Mode";
            GUILayout.Label($"{persistence} · {lifecycle} · {mode}", bodyStyle, GUILayout.Width(labelWidth));

            if (!string.IsNullOrWhiteSpace(statusBarShell?.RecoveryPromptLabel))
            {
                GUILayout.Label(statusBarShell.RecoveryPromptLabel, bodyStyle, GUILayout.Width(labelWidth));
            }

            GUILayout.EndArea();
        }

        // A prominent, auto-fading banner over the top-center of the canvas for important transient
        // messages (e.g. "Add at least one exit before placing spawns") so rejections are impossible
        // to miss, unlike the slim bottom-left status box.
        private void DrawTransientNotice()
        {
            if (statusBarShell == null)
            {
                return;
            }

            var age = statusBarShell.NoticeAgeSeconds;
            const float holdSeconds = 2.5f;
            const float fadeSeconds = 0.6f;
            if (string.IsNullOrWhiteSpace(statusBarShell.NoticeMessage) || age > holdSeconds + fadeSeconds)
            {
                return;
            }

            var alpha = age <= holdSeconds ? 1f : Mathf.Clamp01(1f - ((age - holdSeconds) / fadeSeconds));

            var content = new GUIContent(statusBarShell.NoticeMessage);
            var style = noticeStyle ?? bodyStyle;
            var logicalWidth = Screen.width / uiScale;
            var width = Mathf.Min(560f, logicalWidth - 40f);
            var height = Mathf.Max(40f, style.CalcHeight(content, width - 28f) + 20f);
            var rect = new Rect((logicalWidth - width) * 0.5f, topBarRect.yMax + 16f, width, height);

            var previousColor = GUI.color;
            var background = statusBarShell.NoticeIsError ? new Color(0.6f, 0.12f, 0.12f, 0.96f) : new Color(0.12f, 0.18f, 0.28f, 0.96f);
            background.a *= alpha;
            GUI.color = background;
            GUI.DrawTexture(rect, solidTexture ?? Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, statusBarShell.NoticeMessage, style);
            GUI.color = previousColor;
        }

        private void DrawNewProjectModal()
        {
            var previousColor = GUI.color;
            GUI.color = ModalBackdropColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), solidTexture ?? Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.BeginArea(modalRect, GUIContent.none, modalWindowStyle ?? GUI.skin.window);
            newProjectScrollPosition = GUILayout.BeginScrollView(
                newProjectScrollPosition,
                false,
                true,
                GUILayout.Height(Mathf.Max(120f, modalRect.height - 24f)));
            GUILayout.Label("Start a Sandbox Project", headerStyle);
            GUILayout.Label("Choose a saved project or name a new project to unlock the editor.", bodyStyle);
            GUILayout.Space(8f);

            if (topBarShell != null && topBarShell.UsesBrowserHostedFileActions && topBarShell.HasSavedBrowserProjects)
            {
                DrawActionButton("Load Saved Project", () =>
                {
                    newProjectDialogShell?.Close();
                    projectLibraryModalMode = ProjectLibraryModalMode.Load;
                    projectLibraryMessage = string.Empty;
                });
                GUILayout.Space(8f);
            }

            GUILayout.Label("Project Name", subheaderStyle);
            newProjectDialogShell.ProjectNameDraft = GUILayout.TextField(newProjectDialogShell.ProjectNameDraft ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(newProjectDialogShell.ValidationMessage))
            {
                GUILayout.Label(newProjectDialogShell.ValidationMessage, bodyStyle);
            }

            DrawActionButton("Create Default Project", () => newProjectDialogShell?.CreateDefaultProject());
            DrawActionButton("Create Blank Project", () => newProjectDialogShell?.CreateBlankProject());

            if (topBarShell != null && topBarShell.UsesBrowserHostedFileActions)
            {
                DrawActionButton("Import JSON", () =>
                {
                    newProjectDialogShell?.Close();
                    topBarShell.RequestBrowserProjectJsonImport();
                }, !topBarShell.IsBrowserFileActionBusy);
            }

            if (workspaceService?.ActiveProject != null)
            {
                DrawActionButton("Close", () => newProjectDialogShell?.Close());
            }

            GUILayout.Space(10f);
            GUILayout.Label("Tip: after creating a project, import a blueprint path in the Inspector and then pick a wall tool on the left.", bodyStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawProjectLibraryModal()
        {
            if (projectLibraryModalMode == ProjectLibraryModalMode.None)
            {
                return;
            }
            var previousColor = GUI.color;
            GUI.color = ModalBackdropColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), solidTexture ?? Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.BeginArea(modalRect, GUIContent.none, modalWindowStyle ?? GUI.skin.window);
            switch (projectLibraryModalMode)
            {
                case ProjectLibraryModalMode.Load:
                    DrawLoadProjectModalContents();
                    break;
                case ProjectLibraryModalMode.SaveName:
                    DrawSaveNameModalContents();
                    break;
                case ProjectLibraryModalMode.ConfirmUnsaved:
                    DrawUnsavedChangesModalContents();
                    break;
                case ProjectLibraryModalMode.ConfirmDelete:
                    DrawDeleteProjectModalContents();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawLoadProjectModalContents()
        {
            GUILayout.Label("Load Saved Project", headerStyle);
            GUILayout.Label("Saved projects live in this browser/device. Use Import JSON for files from elsewhere.", bodyStyle);
            GUILayout.Space(8f);

            var projects = topBarShell?.GetSavedBrowserProjects() ?? Array.Empty<SandboxSavedProjectInfo>();
            if (projects.Length == 0)
            {
                GUILayout.Label("No saved projects yet.", bodyStyle);
            }
            else
            {
                var scrollHeight = Mathf.Clamp(projects.Length * 84f, 84f, modalRect.height - 130f);
                projectLibraryScrollPosition = GUILayout.BeginScrollView(
                    projectLibraryScrollPosition,
                    false,
                    true,
                    GUILayout.Height(scrollHeight));
                for (var i = 0; i < projects.Length; i += 1)
                {
                    var project = projects[i];
                    GUILayout.BeginVertical(insetPanelBoxStyle ?? GUI.skin.box);
                    GUILayout.Label(project.displayName, subheaderStyle);
                    GUILayout.Label($"Last saved: {FormatProjectTimestamp(project.savedUtc)}", bodyStyle);
                    GUILayout.BeginHorizontal();
                    DrawActionButton("Open", () =>
                    {
                        topBarShell?.LoadBrowserProject(project.projectId);
                        projectLibraryModalMode = ProjectLibraryModalMode.None;
                    });
                    DrawActionButton("Delete", () =>
                    {
                        pendingDeleteProjectId = project.projectId;
                        pendingDeleteProjectName = project.displayName;
                        projectLibraryModalMode = ProjectLibraryModalMode.ConfirmDelete;
                    });
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                }

                GUILayout.EndScrollView();
            }

            if (!string.IsNullOrWhiteSpace(projectLibraryMessage))
            {
                GUILayout.Label(projectLibraryMessage, bodyStyle);
            }

            DrawActionButton("Cancel", () => projectLibraryModalMode = ProjectLibraryModalMode.None);
        }

        private void DrawSaveNameModalContents()
        {
            GUILayout.Label("Name Project", headerStyle);
            GUILayout.Label("A project name is required before saving to browser storage.", bodyStyle);
            saveProjectNameDraft = GUILayout.TextField(saveProjectNameDraft ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(projectLibraryMessage))
            {
                GUILayout.Label(projectLibraryMessage, bodyStyle);
            }

            GUILayout.BeginHorizontal();
            DrawActionButton("Save", () =>
            {
                if (string.IsNullOrWhiteSpace(saveProjectNameDraft))
                {
                    projectLibraryMessage = "Project name is required.";
                    return;
                }

                if (topBarShell?.SaveProjectToBrowserLibrary(saveProjectNameDraft.Trim()) == true)
                {
                    projectLibraryMessage = string.Empty;
                    ContinueAfterSave();
                }
            });
            DrawActionButton("Cancel", () => projectLibraryModalMode = ProjectLibraryModalMode.None);
            GUILayout.EndHorizontal();
        }

        private void DrawUnsavedChangesModalContents()
        {
            GUILayout.Label("Unsaved Changes", headerStyle);
            GUILayout.Label("Save this project before continuing, discard changes, or cancel.", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawActionButton("Save and Continue", () =>
            {
                if (!SaveCurrentBrowserProject())
                {
                    return;
                }

                ContinueAfterSave();
            });
            DrawActionButton("Discard Changes", () =>
            {
                var continuation = pendingUnsavedContinuation;
                pendingUnsavedContinuation = null;
                projectLibraryModalMode = ProjectLibraryModalMode.None;
                continuation?.Invoke();
            });
            DrawActionButton("Cancel", () =>
            {
                pendingUnsavedContinuation = null;
                projectLibraryModalMode = ProjectLibraryModalMode.None;
            });
            GUILayout.EndHorizontal();
        }

        private void DrawDeleteProjectModalContents()
        {
            GUILayout.Label("Delete Project", headerStyle);
            GUILayout.Label($"Delete '{pendingDeleteProjectName}' from this browser?", bodyStyle);
            GUILayout.BeginHorizontal();
            DrawActionButton("Delete Project", () =>
            {
                topBarShell?.DeleteBrowserProject(pendingDeleteProjectId);
                pendingDeleteProjectId = string.Empty;
                pendingDeleteProjectName = string.Empty;
                projectLibraryModalMode = ProjectLibraryModalMode.Load;
            });
            DrawActionButton("Cancel", () => projectLibraryModalMode = ProjectLibraryModalMode.Load);
            GUILayout.EndHorizontal();
        }

        private void RequestNewProject()
        {
            RunAfterUnsavedCheck(OpenNewProjectDialog);
        }

        private void OpenNewProjectDialog()
        {
            pendingUnsavedContinuation = null;
            projectLibraryModalMode = ProjectLibraryModalMode.None;
            projectLibraryMessage = string.Empty;

            if (newProjectDialogShell != null)
            {
                newProjectDialogShell.Open();
                return;
            }

            topBarShell?.OpenNewProjectDialog();
        }

        private void RequestLoadBrowserProject()
        {
            RunAfterUnsavedCheck(() =>
            {
                projectLibraryModalMode = ProjectLibraryModalMode.Load;
                projectLibraryMessage = string.Empty;
            });
        }

        private bool SaveCurrentBrowserProject()
        {
            var currentName = workspaceService?.ActiveProject?.metadata?.buildingName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentName))
            {
                saveProjectNameDraft = "New Project";
                projectLibraryMessage = string.Empty;
                projectLibraryModalMode = ProjectLibraryModalMode.SaveName;
                return false;
            }

            return topBarShell?.SaveProjectToBrowserLibrary(currentName) == true;
        }

        private void RunAfterUnsavedCheck(Action continuation)
        {
            if (topBarShell != null && topBarShell.HasUnsavedChanges)
            {
                pendingUnsavedContinuation = continuation;
                projectLibraryModalMode = ProjectLibraryModalMode.ConfirmUnsaved;
                return;
            }

            continuation?.Invoke();
        }

        private void ContinueAfterSave()
        {
            var continuation = pendingUnsavedContinuation;
            pendingUnsavedContinuation = null;
            projectLibraryModalMode = ProjectLibraryModalMode.None;
            continuation?.Invoke();
        }

        private static string FormatProjectTimestamp(string timestamp)
        {
            if (DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }

            return "Unknown";
        }

        private void DrawReturnToMenuModal()
        {
            var previousColor = GUI.color;
            GUI.color = ModalBackdropColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), solidTexture ?? Texture2D.whiteTexture);
            GUI.color = previousColor;

            GUILayout.BeginArea(modalRect, GUIContent.none, modalWindowStyle ?? GUI.skin.window);
            GUILayout.Label("Return to Main Menu?", headerStyle);
            GUILayout.Label("Any unsaved changes will be lost.", bodyStyle);
            GUILayout.Space(10f);

            DrawActionButton("Return to Main Menu", () =>
            {
                showReturnToMenuConfirm = false;
                SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            });
            DrawActionButton("Cancel", () => showReturnToMenuConfirm = false);
            GUILayout.EndArea();
        }
        private void DrawInspectorSection(string title)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, subheaderStyle);
        }

        // Hands the current in-memory project (including unsaved edits) to the simulation scene and
        // returns here when the user leaves the simulation.
        private void LaunchSimulation()
        {
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                return;
            }

            SandboxSimulationLaunchContext.SetFromProject(project, "SandboxEditor", $"Project: {project.metadata?.buildingName}");
            // Preserve the editor's project so returning from the simulation restores it instead of
            // booting a fresh default project.
            SandboxSimulationLaunchContext.SetReturnProject(project);
            SceneManager.LoadScene(SandboxSimulationLaunchContext.SimulationSceneName, LoadSceneMode.Single);
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

        private void ActivateSpawnPointBrushPlacement()
        {
            if (previewService == null)
            {
                return;
            }

            previewService.ConfigureSpawnPointBrush(1f, string.Empty, "Spawn Point Brush Layout", true);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PaintSpawnPointBrush);
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
            solidTexture ??= Texture2D.whiteTexture;
            hudPanelTexture ??= CreateSolidTexture(HudPanelColor);
            hudInsetPanelTexture ??= CreateSolidTexture(HudInsetPanelColor);
            modalWindowTexture ??= CreateSolidTexture(ModalWindowColor);
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

            noticeStyle ??= new GUIStyle(labelStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(14, 14, 10, 10),
                normal = { textColor = Color.white }
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

            panelBoxStyle ??= new GUIStyle(GUI.skin?.box ?? GUIStyle.none);
            insetPanelBoxStyle ??= new GUIStyle(GUI.skin?.box ?? GUIStyle.none)
            {
                padding = new RectOffset(8, 8, 8, 8),
                margin = new RectOffset(0, 0, 4, 4)
            };
            modalWindowStyle ??= new GUIStyle(GUI.skin?.window ?? GUIStyle.none)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };

            ApplySolidBackground(panelBoxStyle, hudPanelTexture);
            ApplySolidBackground(insetPanelBoxStyle, hudInsetPanelTexture);
            ApplySolidBackground(modalWindowStyle, modalWindowTexture);

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

        private void ApplySolidBackground(GUIStyle style, Texture2D backgroundTexture)
        {
            if (style == null || backgroundTexture == null)
            {
                return;
            }

            style.normal.background = backgroundTexture;
            style.hover.background = backgroundTexture;
            style.active.background = backgroundTexture;
            style.focused.background = backgroundTexture;
            style.onNormal.background = backgroundTexture;
            style.onHover.background = backgroundTexture;
            style.onActive.background = backgroundTexture;
            style.onFocused.background = backgroundTexture;

            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            style.onNormal.textColor = Color.white;
            style.onHover.textColor = Color.white;
            style.onActive.textColor = Color.white;
            style.onFocused.textColor = Color.white;
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static void DestroyGeneratedTexture(Texture2D texture)
        {
            if (texture == null || texture == Texture2D.whiteTexture)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        private void RecalculateLayout()
        {
            var margin = 12f;
            var logicalScreenWidth = Screen.width / uiScale;
            var logicalScreenHeight = Screen.height / uiScale;
            var topBarHeight = topBarCollapsed ? CollapsedPanelHeight : 110f;
            var statusBarHeight = statusBarCollapsed ? CollapsedPanelHeight : 58f;
            var toolWidth = 180f;
            var inspectorWidth = 340f;

            var contentTop = topBarHeight + (margin * 2f);
            var centerX = (margin * 2f) + toolWidth;
            var centerWidth = logicalScreenWidth - toolWidth - inspectorWidth - (margin * 4f);

            // Floors sizes to its actual content so a project with only a couple of floors doesn't
            // reserve a tall panel with an empty scroll area; it only grows (and scrolls) once the tab
            // list exceeds the viewport cap.
            var floorTabsAvailableHeight = Mathf.Max(CollapsedPanelHeight, logicalScreenHeight - topBarHeight - (margin * 4f) - 58f);
            float floorTabsHeight;
            if (floorTabsCollapsed)
            {
                floorTabsHeight = CollapsedPanelHeight;
            }
            else
            {
                // Use the content height measured during the last OnGUI pass (GUI calls are illegal here).
                var floorViewport = Mathf.Clamp(lastFloorTabsContentHeight, FloorTabsMinViewport, FloorTabsMaxViewport);
                floorTabsHeight = Mathf.Min(floorTabsAvailableHeight, FloorTabsChrome + floorViewport);
            }

            // Validation is a wide band across the center-bottom. Its height is independent of the floors
            // panel (it reserves only the collapsed floors height), so expanding floors no longer steals
            // room from validation and clips its content — the scene canvas absorbs the difference.
            const float minCanvasHeight = 220f;
            var maxValidationHeight = Mathf.Max(
                CollapsedPanelHeight,
                logicalScreenHeight - contentTop - CollapsedPanelHeight - minCanvasHeight - (margin * 3f));
            var validationHeight = validationCollapsed ? CollapsedPanelHeight : Mathf.Min(220f, maxValidationHeight);

            var statusY = logicalScreenHeight - statusBarHeight - margin;
            var validationY = logicalScreenHeight - validationHeight - margin;

            topBarRect = new Rect(margin, margin, logicalScreenWidth - (margin * 2f), topBarHeight);
            // Inspector now owns the full right column height.
            var inspectorHeight = inspectorCollapsed ? CollapsedPanelHeight : logicalScreenHeight - contentTop - margin;
            inspectorRect = new Rect(logicalScreenWidth - inspectorWidth - margin, contentTop, inspectorWidth, inspectorHeight);
            // Tools runs down the left column to just above the slim status box.
            var toolHeight = toolPaletteCollapsed ? CollapsedPanelHeight : Mathf.Max(CollapsedPanelHeight, statusY - margin - contentTop);
            toolPaletteRect = new Rect(margin, contentTop, toolWidth, toolHeight);
            floorTabsRect = new Rect(centerX, contentTop, centerWidth, floorTabsHeight);
            // Validation: wide center-bottom band (controls left, error list right).
            validationRect = new Rect(centerX, validationY, centerWidth, validationHeight);
            // Status: small box, bottom-left under the tools column.
            statusBarRect = new Rect(margin, statusY, toolWidth, statusBarHeight);

            ApplyCustomPanelPositions();

            var modalWidth = Mathf.Min(480f, logicalScreenWidth - 40f);
            float modalHeight;
            if (projectLibraryModalMode == ProjectLibraryModalMode.Load)
            {
                var projectCount = topBarShell?.GetSavedBrowserProjects().Length ?? 0;
                var scrollHeight = Mathf.Clamp(projectCount * 84f, 84f, 300f);
                modalHeight = Mathf.Min(150f + scrollHeight, logicalScreenHeight - 40f);
            }
            else
            {
                modalHeight = 210f;
            }

            modalRect = new Rect(
                (logicalScreenWidth - modalWidth) * 0.5f,
                (logicalScreenHeight - modalHeight) * 0.5f,
                modalWidth,
                modalHeight);
        }

        private void EnsurePanelLayoutLoaded()
        {
            if (panelLayoutLoaded)
            {
                return;
            }

            panelLayoutLoaded = true;
            foreach (MovablePanel panel in Enum.GetValues(typeof(MovablePanel)))
            {
                var key = PanelLayoutPrefPrefix + panel;
                if (PlayerPrefs.GetInt(key + ".set", 0) == 1)
                {
                    customPanelPositions[panel] = new Vector2(
                        PlayerPrefs.GetFloat(key + ".x", 0f),
                        PlayerPrefs.GetFloat(key + ".y", 0f));
                }
            }
        }

        // After the default layout is computed, override the position (not size) of any panel the
        // user has dragged, clamped so its header stays on-screen.
        private void ApplyCustomPanelPositions()
        {
            if (customPanelPositions.Count == 0)
            {
                return;
            }

            ApplyStoredPosition(MovablePanel.TopBar, ref topBarRect);
            ApplyStoredPosition(MovablePanel.Tools, ref toolPaletteRect);
            ApplyStoredPosition(MovablePanel.Floors, ref floorTabsRect);
            ApplyStoredPosition(MovablePanel.Inspector, ref inspectorRect);
            ApplyStoredPosition(MovablePanel.Validation, ref validationRect);
            ApplyStoredPosition(MovablePanel.StatusBar, ref statusBarRect);
        }

        private void ApplyStoredPosition(MovablePanel panel, ref Rect rect)
        {
            if (customPanelPositions.TryGetValue(panel, out var position))
            {
                rect = new Rect(ClampPanelPosition(position, rect.size), rect.size);
            }
        }

        private Vector2 ClampPanelPosition(Vector2 position, Vector2 size)
        {
            // Positions are stored/used in logical (pre-scale) space, so clamp against the logical
            // screen bounds rather than the physical pixel dimensions.
            var scale = Mathf.Approximately(uiScale, 0f) ? 1f : uiScale;
            var maxX = Mathf.Max(0f, (Screen.width / scale) - 60f);
            var maxY = Mathf.Max(0f, (Screen.height / scale) - 30f);
            return new Vector2(Mathf.Clamp(position.x, 0f, maxX), Mathf.Clamp(position.y, 0f, maxY));
        }

        private Rect GetPanelRect(MovablePanel panel)
        {
            return panel switch
            {
                MovablePanel.TopBar => topBarRect,
                MovablePanel.Tools => toolPaletteRect,
                MovablePanel.Floors => floorTabsRect,
                MovablePanel.Inspector => inspectorRect,
                MovablePanel.Validation => validationRect,
                MovablePanel.StatusBar => statusBarRect,
                _ => topBarRect
            };
        }

        private string GetPanelTitle(MovablePanel panel)
        {
            return panel switch
            {
                MovablePanel.TopBar => topBarShell?.Title ?? "Sandbox Editor",
                MovablePanel.Tools => "Tools",
                MovablePanel.Floors => "Floors",
                MovablePanel.Inspector => "Inspector",
                MovablePanel.Validation => "Validation",
                MovablePanel.StatusBar => "Status",
                _ => string.Empty
            };
        }

        // The grab zone is the panel's title strip, excluding the collapse triangle and (for Floors)
        // the tab/button row to its right.
        private Rect GetPanelHeaderGrabRect(MovablePanel panel)
        {
            var rect = GetPanelRect(panel);
            if (panel == MovablePanel.Floors)
            {
                return new Rect(rect.x, rect.y, Mathf.Min(60f, rect.width), 28f);
            }

            return new Rect(rect.x, rect.y, Mathf.Max(0f, rect.width - 30f), 28f);
        }

        private void ProcessPanelDragEvents()
        {
            var e = Event.current;
            if (e == null)
            {
                return;
            }

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && !hasPendingPanelPress:
                    foreach (MovablePanel panel in Enum.GetValues(typeof(MovablePanel)))
                    {
                        if (!GetPanelHeaderGrabRect(panel).Contains(e.mousePosition))
                        {
                            continue;
                        }

                        hasPendingPanelPress = true;
                        isPanelDragActive = false;
                        pendingPanel = panel;
                        pendingPanelPressPoint = e.mousePosition;
                        panelDragGrabOffset = e.mousePosition - GetPanelRect(panel).position;
                        e.Use();
                        break;
                    }

                    break;
                case EventType.MouseDrag when hasPendingPanelPress:
                    if (!isPanelDragActive && Vector2.Distance(e.mousePosition, pendingPanelPressPoint) >= PanelDragThresholdPixels)
                    {
                        isPanelDragActive = true;
                    }

                    if (isPanelDragActive)
                    {
                        panelDragGhostPosition = ClampPanelPosition(e.mousePosition - panelDragGrabOffset, GetPanelRect(pendingPanel).size);
                    }

                    e.Use();
                    break;
                case EventType.MouseUp when hasPendingPanelPress && e.button == 0:
                    if (isPanelDragActive)
                    {
                        CommitPanelDrag();
                    }

                    ClearPanelDragState();
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 1 && hasPendingPanelPress:
                    ClearPanelDragState();
                    e.Use();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && hasPendingPanelPress:
                    ClearPanelDragState();
                    e.Use();
                    break;
            }
        }

        private void CommitPanelDrag()
        {
            var clamped = ClampPanelPosition(panelDragGhostPosition, GetPanelRect(pendingPanel).size);
            customPanelPositions[pendingPanel] = clamped;
            var key = PanelLayoutPrefPrefix + pendingPanel;
            PlayerPrefs.SetInt(key + ".set", 1);
            PlayerPrefs.SetFloat(key + ".x", clamped.x);
            PlayerPrefs.SetFloat(key + ".y", clamped.y);
            PlayerPrefs.Save();
        }

        private void ClearPanelDragState()
        {
            hasPendingPanelPress = false;
            isPanelDragActive = false;
        }

        private void ResetPanelLayout()
        {
            foreach (MovablePanel panel in Enum.GetValues(typeof(MovablePanel)))
            {
                var key = PanelLayoutPrefPrefix + panel;
                PlayerPrefs.DeleteKey(key + ".set");
                PlayerPrefs.DeleteKey(key + ".x");
                PlayerPrefs.DeleteKey(key + ".y");
            }

            PlayerPrefs.Save();
            customPanelPositions.Clear();
            ClearPanelDragState();
        }

        private void DrawPanelDragGhost()
        {
            if (!isPanelDragActive)
            {
                return;
            }

            var ghostRect = new Rect(panelDragGhostPosition, GetPanelRect(pendingPanel).size);
            var previousColor = GUI.color;
            GUI.color = new Color(0.4f, 0.62f, 0.95f, 0.35f);
            GUI.DrawTexture(ghostRect, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(
                new Rect(ghostRect.x + 12f, ghostRect.y + 6f, Mathf.Max(40f, ghostRect.width - 24f), 22f),
                GetPanelTitle(pendingPanel),
                headerStyle ?? GUI.skin.label);
        }

        private void UpdateInputCapture()
        {
            if (inputRouter == null)
            {
                return;
            }

            RecalculateLayout();
            var pointer = SandboxInputAdapter.PointerScreenPosition;
            var guiPoint = new Vector2(pointer.x / uiScale, (Screen.height - pointer.y) / uiScale);
            var isOverHud = topBarRect.Contains(guiPoint)
                || toolPaletteRect.Contains(guiPoint)
                || floorTabsRect.Contains(guiPoint)
                || inspectorRect.Contains(guiPoint)
                || validationRect.Contains(guiPoint)
                || statusBarRect.Contains(guiPoint)
                || (newProjectDialogShell != null && newProjectDialogShell.IsOpen)
                || projectLibraryModalMode != ProjectLibraryModalMode.None;

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

        private string GetSimulationSummaryReportPath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "simulation-summary-report.json");
        }

        private string GetSimulationHeatmapReportPath()
        {
            return Path.Combine(GetStorageDirectoryPath(), "simulation-travel-density-heatmap.json");
        }

        private static string GetToolLabel(SandboxToolMode toolMode)
        {
            return toolMode switch
            {
                SandboxToolMode.WallLine => "Wall Line",
                SandboxToolMode.WallBrush => "Wall Brush",
                SandboxToolMode.Teleport => "Teleport",
                SandboxToolMode.SpawnPoint => "Spawn Point",
                SandboxToolMode.SpawnPointBrush => "Spawn Point Brush",
                SandboxToolMode.FireStart => "Fire Start",
                _ => toolMode.ToString()
            };
        }

        private static string GetPreviewInteractionLabel(SandboxPreviewInteractionMode interactionMode)
        {
            return interactionMode switch
            {
                SandboxPreviewInteractionMode.PlaceFireOrigin => "Fire",
                SandboxPreviewInteractionMode.PlaceSpawnPoint => "Spawn Point",
                SandboxPreviewInteractionMode.PaintSpawnPointBrush => "Spawn Point Brush",
                _ => "None"
            };
        }
    }
}
