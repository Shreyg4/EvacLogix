using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Infrastructure;
using System.Collections.Generic;
using EvacLogix.Sandbox.UI.Overlays;
using UnityEngine;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.UI.Shortcuts;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxInspectorPanelShell : MonoBehaviour
    {
        [SerializeField] private bool showSelectionSummary = true;
        [SerializeField] private string latestCalibrationFeedback = string.Empty;

        private ISandboxFileActionService fileActionService;
        private SandboxBrowserFileActionCoordinator browserFileActionCoordinator;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxProjectMetadataService projectMetadataService;
        private SandboxScaleCalibrationService calibrationService;
        private SandboxCalibrationWorkflowService calibrationWorkflowService;
        private SandboxPreviewImageExportService previewImageExportService;
        private SandboxWallAuthoringService wallAuthoringService;
        private SandboxWallSnappingService wallSnappingService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxFloorManagementService floorManagementService;
        private SandboxVisualOrganizationService visualOrganizationService;
        private SandboxClipboardService clipboardService;
        private SandboxMeasurementService measurementService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxOverviewNavigator overviewNavigator;
        private SandboxEditorQoLService editorQoLService;
        private SandboxScenarioManagementService scenarioManagementService;
        private SandboxPreviewAuthoringService previewAuthoringService;
        private SandboxPreviewService previewService;
        private SandboxKeyboardShortcutService keyboardShortcutService;
        private SandboxStatusBarShell statusBar;

        public bool ShowSelectionSummary => showSelectionSummary;
        public string LatestCalibrationFeedback => latestCalibrationFeedback;
        public DistanceUnit CurrentDistanceUnit => workspaceService?.ActiveProject?.metadata?.distanceUnit ?? DistanceUnit.Feet;
        public string CurrentDistanceUnitLabel => SandboxDistanceUnitUtility.GetLabel(CurrentDistanceUnit);
        public bool HasActiveMeasurement => measurementService != null && (measurementService.HasPointA || measurementService.HasPointB);
        public string CurrentMeasurementReadout => measurementService?.LastDistanceReadout ?? string.Empty;
        public string CurrentSelectionMeasurementReadout => measurementService?.LastSelectionReadout ?? string.Empty;
        public string CurrentToolHelpText => editorQoLService?.CurrentToolHelpText ?? string.Empty;
        public string CurrentValidationHelpText => editorQoLService?.CurrentValidationHelpText ?? string.Empty;
        public bool HasShortcutConflicts => keyboardShortcutService != null && keyboardShortcutService.HasBindingConflicts;
        public bool IsFullyWired => GetMissingDependencies().Count == 0;
        public bool UsesBrowserHostedFileActions => browserFileActionCoordinator != null && browserFileActionCoordinator.SupportsBrowserFileActions;

        private void Awake()
        {
            RefreshDependencies();
            editorQoLService?.EnsureDefaultAdvancedFoldouts();
        }

        public void RefreshDependencies()
        {
            fileActionService = FindAnyObjectByType<SandboxFileActionService>();
            browserFileActionCoordinator = FindAnyObjectByType<SandboxBrowserFileActionCoordinator>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            projectMetadataService = FindAnyObjectByType<SandboxProjectMetadataService>();
            calibrationService = FindAnyObjectByType<SandboxScaleCalibrationService>();
            calibrationWorkflowService = FindAnyObjectByType<SandboxCalibrationWorkflowService>();
            previewImageExportService = FindAnyObjectByType<SandboxPreviewImageExportService>();
            wallAuthoringService = FindAnyObjectByType<SandboxWallAuthoringService>();
            wallSnappingService = FindAnyObjectByType<SandboxWallSnappingService>();
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            floorManagementService = FindAnyObjectByType<SandboxFloorManagementService>();
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            clipboardService = FindAnyObjectByType<SandboxClipboardService>();
            measurementService = FindAnyObjectByType<SandboxMeasurementService>();
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            overviewNavigator = FindAnyObjectByType<SandboxOverviewNavigator>();
            editorQoLService = FindAnyObjectByType<SandboxEditorQoLService>();
            scenarioManagementService = FindAnyObjectByType<SandboxScenarioManagementService>();
            previewAuthoringService = FindAnyObjectByType<SandboxPreviewAuthoringService>();
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            keyboardShortcutService = FindAnyObjectByType<SandboxKeyboardShortcutService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
        }

        public bool SnappingEnabled => workspaceStateService != null && workspaceStateService.SnappingEnabled;

        public IReadOnlyList<string> GetMissingDependencies()
        {
            RefreshDependencies();
            var missingDependencies = new List<string>();
            AddMissingDependency(missingDependencies, fileActionService, nameof(fileActionService));
            AddMissingDependency(missingDependencies, browserFileActionCoordinator, nameof(browserFileActionCoordinator));
            AddMissingDependency(missingDependencies, workspaceService, nameof(workspaceService));
            AddMissingDependency(missingDependencies, projectMetadataService, nameof(projectMetadataService));
            AddMissingDependency(missingDependencies, calibrationService, nameof(calibrationService));
            AddMissingDependency(missingDependencies, calibrationWorkflowService, nameof(calibrationWorkflowService));
            AddMissingDependency(missingDependencies, previewImageExportService, nameof(previewImageExportService));
            AddMissingDependency(missingDependencies, wallAuthoringService, nameof(wallAuthoringService));
            AddMissingDependency(missingDependencies, wallSnappingService, nameof(wallSnappingService));
            AddMissingDependency(missingDependencies, semanticObjectAuthoringService, nameof(semanticObjectAuthoringService));
            AddMissingDependency(missingDependencies, floorManagementService, nameof(floorManagementService));
            AddMissingDependency(missingDependencies, visualOrganizationService, nameof(visualOrganizationService));
            AddMissingDependency(missingDependencies, clipboardService, nameof(clipboardService));
            AddMissingDependency(missingDependencies, measurementService, nameof(measurementService));
            AddMissingDependency(missingDependencies, workspaceStateService, nameof(workspaceStateService));
            AddMissingDependency(missingDependencies, overviewNavigator, nameof(overviewNavigator));
            AddMissingDependency(missingDependencies, editorQoLService, nameof(editorQoLService));
            AddMissingDependency(missingDependencies, scenarioManagementService, nameof(scenarioManagementService));
            AddMissingDependency(missingDependencies, previewAuthoringService, nameof(previewAuthoringService));
            AddMissingDependency(missingDependencies, previewService, nameof(previewService));
            AddMissingDependency(missingDependencies, keyboardShortcutService, nameof(keyboardShortcutService));
            AddMissingDependency(missingDependencies, statusBar, nameof(statusBar));
            return missingDependencies;
        }

        public IReadOnlyList<SandboxInspectorAuditEntry> GetInspectorAuditEntries()
        {
            return new List<SandboxInspectorAuditEntry>
            {
                CreateAuditEntry("project", "Project Metadata", 3, 0, true, "project.advanced"),
                CreateAuditEntry("floor", "Floor", 1, 2, true, "floor.advanced"),
                CreateAuditEntry("wall", "Wall", 0, 3, false, "wall.advanced"),
                CreateAuditEntry("door", "Door", 0, 2, true, "door.advanced"),
                CreateAuditEntry("window", "Window", 0, 4, true, "window.advanced"),
                CreateAuditEntry("exit", "Exit Zone", 1, 6, true, "exit.advanced"),
                CreateAuditEntry("obstacle", "Obstacle", 1, 4, true, "obstacle.advanced"),
                CreateAuditEntry("stair", "Stair Portal", 1, 4, true, "stair.advanced"),
                CreateAuditEntry("spawn", "Spawn Layout", 1, 0, true, "spawn.advanced"),
                CreateAuditEntry("preview", "Fire Origin", 0, 2, false, "preview.advanced"),
                CreateAuditEntry("scenario", "Scenario Preset", 1, 2, true, "scenario.advanced"),
            };
        }

        public IReadOnlyList<SandboxShortcutCatalogEntry> GetShortcutCatalogEntries()
        {
            return keyboardShortcutService == null
                ? new List<SandboxShortcutCatalogEntry>()
                : new List<SandboxShortcutCatalogEntry>(keyboardShortcutService.GetShortcutCatalogEntries());
        }

        public IReadOnlyList<SandboxShortcutConflict> GetShortcutConflicts()
        {
            return keyboardShortcutService == null
                ? new List<SandboxShortcutConflict>()
                : new List<SandboxShortcutConflict>(keyboardShortcutService.GetBindingConflicts());
        }

        public bool UpdateProjectMetadata(
            string buildingName,
            string description,
            string authorName,
            IEnumerable<MetadataFieldData> customFields,
            DistanceUnit? distanceUnit = null)
        {
            return UpdateVisualActionStatus(
                projectMetadataService != null && projectMetadataService.UpdateProjectMetadata(buildingName, description, authorName, customFields, distanceUnit),
                "Updated project metadata.");
        }

        public bool SetProjectDistanceUnit(DistanceUnit distanceUnit)
        {
            return UpdateVisualActionStatus(
                projectMetadataService != null && projectMetadataService.SetDistanceUnit(distanceUnit),
                $"Set project distance unit to {SandboxDistanceUnitUtility.GetLabel(distanceUnit)}.");
        }

        public BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath)
        {
            if (workspaceService?.ActiveFloor == null || fileActionService == null)
            {
                return null;
            }

            var blueprintReference = fileActionService.ImportBlueprintToActiveFloor(sourceFilePath);
            if (blueprintReference == null)
            {
                return null;
            }

            if (statusBar != null)
            {
                statusBar.StatusMessage = $"Imported blueprint {blueprintReference.sourceFileName}.";
            }
            return blueprintReference;
        }

        public bool RequestBrowserBlueprintImport()
        {
            var didRequest = browserFileActionCoordinator != null && browserFileActionCoordinator.RequestBlueprintImageImport();
            Debug.Log($"SandboxInspectorPanelShell: RequestBrowserBlueprintImport result={didRequest}");
            if (!didRequest && statusBar != null)
            {
                statusBar.StatusMessage = "Browser blueprint import request did not start.";
            }

            return didRequest;
        }

        public bool SetActiveFloorBlueprintOpacity(float opacity)
        {
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            return workspaceService.SetBlueprintOpacity(workspaceService.ActiveFloor.blueprintReferenceId, opacity);
        }

        public bool SetActiveFloorBlueprintDisplayScale(float displayScale)
        {
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            return workspaceService.SetBlueprintDisplayScale(workspaceService.ActiveFloor.blueprintReferenceId, displayScale);
        }

        public bool SetActiveFloorBlueprintVisibility(bool isVisible)
        {
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            return workspaceService.SetBlueprintVisibility(workspaceService.ActiveFloor.blueprintReferenceId, isVisible);
        }

        public bool CalibrateActiveFloorBlueprint(Vector2 pointA, Vector2 pointB, float realWorldDistance)
        {
            if (workspaceService?.ActiveFloor == null || calibrationService == null)
            {
                return false;
            }

            var didCalibrate = calibrationService.CalibrateFloorBlueprint(
                workspaceService.ActiveFloor.floorId,
                pointA,
                pointB,
                realWorldDistance);

            latestCalibrationFeedback = calibrationService.LatestMeasurementFeedback;
            if (didCalibrate && statusBar != null)
            {
                statusBar.StatusMessage = latestCalibrationFeedback;
            }
            return didCalibrate;
        }

        public bool BeginActiveFloorCalibrationCapture()
        {
            if (calibrationWorkflowService == null)
            {
                return false;
            }

            var didBegin = calibrationWorkflowService.BeginCalibrationForActiveFloor();
            latestCalibrationFeedback = calibrationWorkflowService.StatusPrompt;
            if (statusBar != null)
            {
                statusBar.StatusMessage = latestCalibrationFeedback;
            }

            return didBegin;
        }

        public bool RegisterCalibrationPoint(Vector2 worldPoint)
        {
            if (calibrationWorkflowService == null)
            {
                return false;
            }

            var didRegister = calibrationWorkflowService.RegisterCalibrationPoint(worldPoint);
            latestCalibrationFeedback = calibrationWorkflowService.StatusPrompt;
            if (didRegister && statusBar != null)
            {
                statusBar.StatusMessage = latestCalibrationFeedback;
            }

            return didRegister;
        }

        public bool CompleteActiveFloorCalibration(float realWorldDistance)
        {
            if (calibrationWorkflowService == null)
            {
                return false;
            }

            var didComplete = calibrationWorkflowService.TryCompleteCalibration(realWorldDistance);
            latestCalibrationFeedback = calibrationWorkflowService.StatusPrompt;
            if (statusBar != null)
            {
                statusBar.StatusMessage = latestCalibrationFeedback;
            }

            return didComplete;
        }

        public void CancelActiveFloorCalibration()
        {
            calibrationWorkflowService?.CancelCalibration();
            latestCalibrationFeedback = calibrationWorkflowService != null
                ? calibrationWorkflowService.StatusPrompt
                : string.Empty;
            if (statusBar != null && !string.IsNullOrWhiteSpace(latestCalibrationFeedback))
            {
                statusBar.StatusMessage = latestCalibrationFeedback;
            }
        }

        public bool ExportActiveBlueprintPreview(string destinationPath)
        {
            return previewImageExportService != null
                && previewImageExportService.TryExportActiveBlueprintPreview(destinationPath);
        }

        public void ConfigureBrushCleanup(int smoothingWindow, float pointReductionDistance, float nearJoinCleanupDistance)
        {
            wallAuthoringService?.SetBrushCleanupSettings(smoothingWindow, pointReductionDistance, nearJoinCleanupDistance);
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Updated wall brush cleanup settings.";
            }
        }

        public void ConfigureWallSnapping(bool useGrid, bool useEndpoints, bool useSegments, bool useAngles)
        {
            wallSnappingService?.ConfigureSnapTargets(useGrid, useEndpoints, useSegments, useAngles);
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Updated wall snapping targets.";
            }
        }

        public bool RegisterLineWallPoint(Vector2 worldPoint)
        {
            if (wallAuthoringService == null)
            {
                return false;
            }

            var didCreate = wallAuthoringService.TryRegisterLinePoint(worldPoint, out _);
            if (statusBar != null)
            {
                statusBar.StatusMessage = didCreate
                    ? "Created line wall segment."
                    : "Stored line wall start point.";
            }

            return didCreate;
        }

        public bool BeginBrushWallStroke(Vector2 worldPoint)
        {
            var didBegin = wallAuthoringService != null && wallAuthoringService.BeginBrushStrokeCapture(worldPoint);
            if (didBegin && statusBar != null)
            {
                statusBar.StatusMessage = "Recording wall brush stroke.";
            }

            return didBegin;
        }

        public bool AppendBrushWallStrokePoint(Vector2 worldPoint)
        {
            return wallAuthoringService != null && wallAuthoringService.AppendBrushStrokePoint(worldPoint);
        }

        public void EndBrushWallStroke()
        {
            wallAuthoringService?.EndBrushStrokeCapture();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Captured brush wall polyline.";
            }
        }

        public bool AcceptBrushWallStroke(float thickness = -1f)
        {
            var didAccept = wallAuthoringService != null && wallAuthoringService.AcceptActiveBrushStroke(thickness);
            if (didAccept && statusBar != null)
            {
                statusBar.StatusMessage = "Converted brush stroke into editable wall segments.";
            }

            return didAccept;
        }

        public void CancelBrushWallStroke()
        {
            wallAuthoringService?.CancelBrushStroke();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Cancelled brush wall stroke.";
            }
        }

        public void CancelLineWallPlacement()
        {
            wallAuthoringService?.CancelLinePlacement();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Cancelled line wall placement.";
            }
        }

        public bool MoveWallStartHandle(string wallSegmentId, Vector2 newStartPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.MoveWallStartHandle(wallSegmentId, newStartPoint),
                "Moved wall start handle.");
        }

        public bool MoveWallEndHandle(string wallSegmentId, Vector2 newEndPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.MoveWallEndHandle(wallSegmentId, newEndPoint),
                "Moved wall end handle.");
        }

        public bool SetWallThickness(string wallSegmentId, float thickness)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.SetWallThickness(wallSegmentId, thickness),
                "Updated wall thickness.");
        }

        public bool SetWallEndpoints(string wallSegmentId, Vector2 startPoint, Vector2 endPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.SetWallEndpoints(wallSegmentId, startPoint, endPoint),
                "Updated wall endpoints.");
        }

        public bool SplitWall(string wallSegmentId, Vector2 splitPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.SplitWall(wallSegmentId, splitPoint),
                "Split wall segment.");
        }

        public bool MergeWalls(string firstWallSegmentId, string secondWallSegmentId)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.MergeWalls(firstWallSegmentId, secondWallSegmentId),
                "Merged wall segments.");
        }

        public bool TrimWallStart(string wallSegmentId, Vector2 trimPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.TrimWallStart(wallSegmentId, trimPoint),
                "Trimmed wall start.");
        }

        public bool TrimWallEnd(string wallSegmentId, Vector2 trimPoint)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.TrimWallEnd(wallSegmentId, trimPoint),
                "Trimmed wall end.");
        }

        public bool EraseWall(string wallSegmentId)
        {
            return UpdateWallActionStatus(
                wallAuthoringService != null && wallAuthoringService.EraseWall(wallSegmentId),
                "Erased wall segment.");
        }

        public bool PlaceDoor(Vector2 worldPoint, float width = 1f, DoorState state = DoorState.Normal)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceDoor(worldPoint, out _, width, state),
                "Placed door.");
        }

        public bool UpdateDoor(
            string doorId,
            float width,
            string wallSegmentId,
            float offsetAlongWall,
            DoorState state,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateDoor(doorId, width, wallSegmentId, offsetAlongWall, state, tags, metadataFields),
                "Updated door metadata.");
        }

        public bool UpdateDoor(
            string doorId,
            float width,
            float offsetAlongWall,
            DoorState state,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateDoor(doorId, width, string.Empty, offsetAlongWall, state, tags, metadataFields);
        }

        public bool PlaceWindow(
            Vector2 worldPoint,
            float width = 1f,
            bool canBeUsedForEscape = false,
            float escapeCost = 1f,
            float escapeRiskMultiplier = 1f)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceWindow(worldPoint, out _, width, canBeUsedForEscape, escapeCost, escapeRiskMultiplier),
                "Placed window.");
        }

        public bool UpdateWindow(
            string windowId,
            float width,
            string wallSegmentId,
            float offsetAlongWall,
            bool canBeUsedForEscape,
            float escapeCost,
            float escapeRiskMultiplier,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateWindow(windowId, width, wallSegmentId, offsetAlongWall, canBeUsedForEscape, escapeCost, escapeRiskMultiplier, tags, metadataFields),
                "Updated window metadata.");
        }

        public bool UpdateWindow(
            string windowId,
            float width,
            float offsetAlongWall,
            bool canBeUsedForEscape,
            float escapeCost,
            float escapeRiskMultiplier,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateWindow(windowId, width, string.Empty, offsetAlongWall, canBeUsedForEscape, escapeCost, escapeRiskMultiplier, tags, metadataFields);
        }

        public bool PlaceExit(
            Vector2 center,
            Vector2 size,
            float rotationDegrees = 0f,
            float width = 1.5f,
            float capacity = 0f,
            float priority = 1f,
            string name = "")
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceExit(center, out _, size, rotationDegrees, width, capacity, priority, name),
                "Placed exit zone.");
        }

        public bool UpdateExit(
            string exitZoneId,
            Vector2 center,
            Vector2 size,
            float rotationDegrees,
            float width,
            float capacity,
            float priority,
            string name,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateExit(exitZoneId, center, size, rotationDegrees, width, capacity, priority, name, tags, metadataFields),
                "Updated exit metadata.");
        }

        public bool PlaceObstacle(
            Vector2 center,
            Vector2 size,
            float rotationDegrees = 0f,
            float discourageWeight = 1f,
            float movementSpeedPenalty = 0f,
            string name = "")
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceObstacle(center, out _, size, rotationDegrees, discourageWeight, movementSpeedPenalty, name),
                "Placed obstacle.");
        }

        public bool UpdateObstacle(
            string obstacleId,
            Vector2 center,
            Vector2 size,
            float rotationDegrees,
            float discourageWeight,
            float movementSpeedPenalty,
            string name,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateObstacle(obstacleId, center, size, rotationDegrees, discourageWeight, movementSpeedPenalty, name, tags, metadataFields),
                "Updated obstacle metadata.");
        }

        public bool PlaceStairPortal(
            Vector2 localPosition,
            Vector2? size = null,
            float rotationDegrees = 0f,
            string name = "",
            StairTraversalDirection direction = StairTraversalDirection.Bidirectional,
            float travelCost = 1f)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceStairPortal(localPosition, out _, size, rotationDegrees, name, direction, travelCost),
                "Placed stair endpoint.");
        }

        public bool PlaceTeleportPortal(
            Vector2 localPosition,
            string pairId,
            int pairColorIndex,
            Vector2? size = null,
            float rotationDegrees = 0f,
            string name = "",
            TeleportPortalKind kind = TeleportPortalKind.Stair,
            float travelCost = 1f,
            bool isPairEnabled = true,
            string targetFloorId = "",
            string targetTeleportPortalId = "")
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.PlaceTeleportPortal(
                    localPosition,
                    out _,
                    pairId,
                    pairColorIndex,
                    size,
                    rotationDegrees,
                    name,
                    kind,
                    travelCost,
                    isPairEnabled,
                    targetFloorId,
                    targetTeleportPortalId),
                "Placed teleport endpoint.");
        }

        public bool UpdateStairPortal(
            string stairPortalId,
            Vector2 localPosition,
            Vector2 size,
            float rotationDegrees,
            string name,
            StairTraversalDirection direction,
            float travelCost,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateStairPortal(stairPortalId, localPosition, size, rotationDegrees, name, direction, travelCost, tags, metadataFields),
                "Updated stair metadata.");
        }

        public bool LinkStairPortals(
            string sourceFloorId,
            string sourcePortalId,
            string targetFloorId,
            string targetPortalId,
            StairTraversalDirection direction,
            float travelCost)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.LinkStairPortals(sourceFloorId, sourcePortalId, targetFloorId, targetPortalId, direction, travelCost),
                "Linked stair endpoints.");
        }

        public bool UpdateTeleportPortal(
            string teleportPortalId,
            Vector2 localPosition,
            Vector2 size,
            float rotationDegrees,
            string name,
            TeleportPortalKind kind,
            float travelCost,
            bool isPairEnabled,
            IEnumerable<string> tags,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.UpdateTeleportPortal(
                    teleportPortalId,
                    localPosition,
                    size,
                    rotationDegrees,
                    name,
                    kind,
                    travelCost,
                    isPairEnabled,
                    tags,
                    metadataFields),
                "Updated teleporter metadata.");
        }

        public bool LinkTeleportPortals(
            string sourceFloorId,
            string sourcePortalId,
            string targetFloorId,
            string targetPortalId,
            TeleportPortalKind kind,
            float travelCost,
            bool isPairEnabled)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.LinkTeleportPortals(
                    sourceFloorId,
                    sourcePortalId,
                    targetFloorId,
                    targetPortalId,
                    kind,
                    travelCost,
                    isPairEnabled),
                "Linked teleport endpoints.");
        }

        public bool SetTeleportTargetFloor(string sourcePortalId, string targetFloorId)
        {
            return UpdateSemanticActionStatus(
                semanticObjectAuthoringService != null && semanticObjectAuthoringService.SetTeleportTargetFloor(sourcePortalId, targetFloorId),
                "Updated teleport target floor.");
        }

        public bool AddFloor(string name = "", float elevation = 0f)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.AddFloor(out _, name, elevation),
                "Added floor.");
        }

        public bool RenameFloor(string floorId, string name)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.RenameFloor(floorId, name),
                "Renamed floor.");
        }

        public bool ReorderFloor(string floorId, int newIndex)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.ReorderFloor(floorId, newIndex),
                "Reordered floor.");
        }

        public bool DuplicateFloor(string floorId)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.DuplicateFloor(floorId, out _),
                "Duplicated floor.");
        }

        public bool UpdateFloorMetadata(string floorId, string name, int order, float elevation)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.UpdateFloorMetadata(floorId, name, order, elevation),
                "Updated floor metadata.");
        }

        public bool UpdateSpawnLayout(
            string spawnLayoutId,
            string name,
            bool? isPersistent,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateVisualActionStatus(
                previewAuthoringService != null && previewAuthoringService.UpdateSpawnLayout(spawnLayoutId, name, isPersistent, metadataFields),
                "Updated spawn layout metadata.");
        }

        public bool UpdateFireOrigin(
            string fireOriginId,
            Vector2 position,
            float spreadIntensity,
            float startDelaySeconds,
            bool isPersistent)
        {
            return UpdateVisualActionStatus(
                previewAuthoringService != null && previewAuthoringService.UpdateFireOrigin(fireOriginId, position, spreadIntensity, startDelaySeconds, isPersistent),
                "Updated fire origin parameters.");
        }

        public bool RequestDeleteFloor(string floorId)
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.RequestDeleteFloor(floorId),
                "Deleted floor.");
        }

        public bool ConfirmDeleteFloor()
        {
            return UpdateFloorActionStatus(
                floorManagementService != null && floorManagementService.ConfirmPendingDeleteFloor(),
                "Deleted floor after confirmation.");
        }

        public void CancelDeleteFloor()
        {
            floorManagementService?.CancelPendingDeleteFloor();
        }

        public bool SetObjectTypeVisibility(SandboxVisualObjectType objectType, bool isVisible)
        {
            if (visualOrganizationService == null)
            {
                return false;
            }

            visualOrganizationService.SetTypeVisibility(objectType, isVisible);
            return UpdateVisualActionStatus(true, $"Updated {objectType} visibility.");
        }

        public bool SetObjectTypeLocked(SandboxVisualObjectType objectType, bool isLocked)
        {
            if (visualOrganizationService == null)
            {
                return false;
            }

            visualOrganizationService.SetTypeLocked(objectType, isLocked);
            return UpdateVisualActionStatus(true, $"Updated {objectType} lock state.");
        }

        public bool HideCurrentSelection(bool shouldHide)
        {
            if (visualOrganizationService == null)
            {
                return false;
            }

            visualOrganizationService.HideCurrentSelection(shouldHide);
            return UpdateVisualActionStatus(true, shouldHide ? "Hid current selection." : "Unhid current selection.");
        }

        public bool LockCurrentSelection(bool shouldLock)
        {
            if (visualOrganizationService == null)
            {
                return false;
            }

            visualOrganizationService.LockCurrentSelection(shouldLock);
            return UpdateVisualActionStatus(true, shouldLock ? "Locked current selection." : "Unlocked current selection.");
        }

        public bool ResetCurrentSelectionVisibilityAndLock()
        {
            if (visualOrganizationService == null)
            {
                return false;
            }

            visualOrganizationService.ResetHiddenAndLockedSelection();
            return UpdateVisualActionStatus(true, "Reset visibility and lock for current selection.");
        }

        public bool CopyCurrentSelection()
        {
            return UpdateVisualActionStatus(
                clipboardService != null && clipboardService.CopySelection(),
                "Copied safe selection objects.");
        }

        public bool PasteClipboard(Vector2? offset = null)
        {
            return UpdateVisualActionStatus(
                clipboardService != null && clipboardService.PasteSelection(offset),
                "Pasted clipboard objects.");
        }

        public bool DuplicateCurrentSelection(Vector2? offset = null)
        {
            return UpdateVisualActionStatus(
                clipboardService != null && clipboardService.DuplicateSelection(offset),
                "Duplicated safe selection objects.");
        }

        public bool DeleteCurrentSelection()
        {
            return UpdateVisualActionStatus(
                clipboardService != null && clipboardService.DeleteSelection(),
                "Deleted safe selection objects.");
        }

        public bool MoveCurrentSelection(Vector2 delta)
        {
            return UpdateVisualActionStatus(
                clipboardService != null && clipboardService.MoveSelection(delta),
                "Moved safe selection objects.");
        }

        public string RegisterMeasurementPoint(Vector2 worldPoint)
        {
            if (measurementService == null)
            {
                return string.Empty;
            }

            var readout = measurementService.RegisterMeasurementPoint(worldPoint);
            if (statusBar != null)
            {
                statusBar.StatusMessage = readout;
            }

            return readout;
        }

        public void ClearMeasurement()
        {
            measurementService?.ClearMeasurement();
            if (statusBar != null)
            {
                statusBar.StatusMessage = "Cleared measurement points.";
            }
        }

        public bool RefreshSelectionMeasurementReadout()
        {
            if (measurementService == null)
            {
                return false;
            }

            var readout = measurementService.RefreshSelectionReadout();
            if (statusBar != null)
            {
                statusBar.StatusMessage = readout;
            }

            return true;
        }

        public bool SetGridVisibility(bool isVisible)
        {
            if (workspaceStateService == null)
            {
                return false;
            }

            workspaceStateService.SetGridVisibility(isVisible);
            return UpdateVisualActionStatus(true, isVisible ? "Enabled grid visibility." : "Disabled grid visibility.");
        }

        public bool SetSnappingEnabled(bool enabled)
        {
            if (workspaceStateService == null)
            {
                return false;
            }

            workspaceStateService.SetSnappingEnabled(enabled);
            return UpdateVisualActionStatus(true, enabled ? "Enabled snapping." : "Disabled snapping.");
        }

        public bool SetGridSize(float gridSize)
        {
            if (workspaceStateService == null)
            {
                return false;
            }

            workspaceStateService.SetGridSize(gridSize);
            return UpdateVisualActionStatus(true, $"Updated grid size to {workspaceStateService.GridSize:0.##}.");
        }

        public bool SetAngleSnapIncrementDegrees(float angleIncrementDegrees)
        {
            if (workspaceStateService == null)
            {
                return false;
            }

            workspaceStateService.SetAngleSnapIncrementDegrees(angleIncrementDegrees);
            return UpdateVisualActionStatus(true, $"Updated angle snap increment to {workspaceStateService.AngleSnapIncrementDegrees:0.##} degrees.");
        }

        public bool FocusOverviewOnActiveFloor()
        {
            if (overviewNavigator == null)
            {
                return false;
            }

            overviewNavigator.FocusOnActiveFloor();
            return UpdateVisualActionStatus(true, "Focused overview on the active floor.");
        }

        public bool FocusOverviewOnSelection()
        {
            return UpdateVisualActionStatus(
                overviewNavigator != null && overviewNavigator.FocusOnSelection(),
                "Focused overview on the current selection.");
        }

        public bool SetOverviewEnabled(bool enabled)
        {
            if (overviewNavigator == null)
            {
                return false;
            }

            overviewNavigator.SetOverviewEnabled(enabled);
            return UpdateVisualActionStatus(true, enabled ? "Enabled overview navigator." : "Disabled overview navigator.");
        }

        public bool RenameScenarioPreset(string scenarioPresetId, string name)
        {
            return UpdateVisualActionStatus(
                scenarioManagementService != null && scenarioManagementService.RenameScenarioPreset(scenarioPresetId, name),
                "Renamed scenario preset.");
        }

        public bool UpdateScenarioPreset(
            string scenarioPresetId,
            string name,
            IEnumerable<string> spawnLayoutIds,
            IEnumerable<string> fireOriginIds,
            float spreadIntensity,
            float startDelaySeconds,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            return UpdateVisualActionStatus(
                scenarioManagementService != null && scenarioManagementService.UpdateScenarioPreset(
                    scenarioPresetId,
                    name,
                    spawnLayoutIds,
                    fireOriginIds,
                    new PreviewParameterData
                    {
                        spreadIntensity = spreadIntensity,
                        startDelaySeconds = startDelaySeconds
                    },
                    metadataFields),
                "Updated scenario preset.");
        }

        public bool CreateScenarioPreset(
            string name,
            IEnumerable<string> spawnLayoutIds,
            IEnumerable<string> fireOriginIds,
            float spreadIntensity,
            float startDelaySeconds,
            out string scenarioPresetId)
        {
            scenarioPresetId = string.Empty;
            if (scenarioManagementService == null)
            {
                return false;
            }

            var didCreate = scenarioManagementService.CreateScenarioPreset(
                name,
                spawnLayoutIds,
                fireOriginIds,
                new PreviewParameterData
                {
                    spreadIntensity = spreadIntensity,
                    startDelaySeconds = startDelaySeconds
                },
                out scenarioPresetId);
            return UpdateVisualActionStatus(didCreate, "Created scenario preset.");
        }

        public bool ApplyScenarioPreset(string scenarioPresetId)
        {
            return UpdateVisualActionStatus(
                scenarioManagementService != null && scenarioManagementService.ApplyScenarioPreset(scenarioPresetId),
                "Applied scenario preset to preview.");
        }

        public bool EnterPreviewMode()
        {
            return UpdateVisualActionStatus(
                previewService != null && previewService.EnterPreviewMode(),
                "Entered preview mode.");
        }

        public bool ExitPreviewMode()
        {
            if (previewService == null)
            {
                return false;
            }

            previewService.ExitPreviewMode();
            return UpdateVisualActionStatus(true, "Exited preview mode.");
        }

        public bool RunPreview()
        {
            var didRun = previewService != null && previewService.RunPreview();
            return UpdateVisualActionStatus(
                didRun,
                previewService?.LastPreviewReport?.summary ?? "Ran preview.");
        }

        public bool SetPreviewParameters(float spreadIntensity, float startDelaySeconds)
        {
            if (previewService == null)
            {
                return false;
            }

            previewService.SetPreviewParameters(spreadIntensity, startDelaySeconds);
            return UpdateVisualActionStatus(true, "Updated preview fire parameters.");
        }

        public bool BeginFireOriginPlacement(bool isPersistent = true)
        {
            if (previewService == null)
            {
                return false;
            }

            previewService.EnterPreviewMode();
            previewService.ConfigureFirePlacement(isPersistent);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceFireOrigin);
            return UpdateVisualActionStatus(true, "Click to place a preview fire origin.");
        }

        public bool BeginSpawnPointPlacement(string spawnLayoutName = "", bool isPersistent = true, string spawnLayoutId = null)
        {
            if (previewService == null)
            {
                return false;
            }

            previewService.EnterPreviewMode();
            previewService.ConfigureSpawnPlacement(spawnLayoutId, spawnLayoutName, isPersistent);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PlaceSpawnPoint);
            return UpdateVisualActionStatus(true, "Click to place a preview spawn point.");
        }

        public bool BeginSpawnPointBrushPlacement(float density = 1f, string spawnLayoutName = "", bool isPersistent = false, string spawnLayoutId = null)
        {
            if (previewService == null)
            {
                return false;
            }

            previewService.EnterPreviewMode();
            previewService.ConfigureSpawnPointBrush(density, spawnLayoutId, spawnLayoutName, isPersistent);
            previewService.SetInteractionMode(SandboxPreviewInteractionMode.PaintSpawnPointBrush);
            return UpdateVisualActionStatus(true, "Drag to paint preview spawn points.");
        }

        public bool BeginSpawnBrushPlacement(float density = 1f, string spawnLayoutName = "", bool isPersistent = false, string spawnLayoutId = null)
        {
            return BeginSpawnPointBrushPlacement(density, spawnLayoutName, isPersistent, spawnLayoutId);
        }

        public bool SetIsolateSelectedObjects(bool enabled)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetIsolateSelectedObjects(enabled);
            return UpdateVisualActionStatus(true, enabled ? "Enabled selected-object isolation." : "Disabled selected-object isolation.");
        }

        public bool SetIsolatedObjectType(SandboxVisualObjectType objectType)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetIsolatedObjectType(objectType);
            return UpdateVisualActionStatus(true, $"Isolated {objectType} objects.");
        }

        public bool ClearObjectTypeIsolation()
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.ClearObjectTypeIsolation();
            return UpdateVisualActionStatus(true, "Cleared object-type isolation.");
        }

        public bool SetDebugOverlayState(bool showColliderOutlines, bool showStairLinks, bool showPassableBlockedRegions, bool showRouteInspection)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetDebugOverlayState(showColliderOutlines, showStairLinks, showPassableBlockedRegions, showRouteInspection);
            return UpdateVisualActionStatus(true, "Updated debug overlay visibility.");
        }

        public bool SetTooltipsEnabled(bool enabled)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetTooltipsEnabled(enabled);
            return UpdateVisualActionStatus(true, enabled ? "Enabled tool help text." : "Disabled tool help text.");
        }

        public bool SetValidationHelpEnabled(bool enabled)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetValidationHelpEnabled(enabled);
            return UpdateVisualActionStatus(true, enabled ? "Enabled validation help text." : "Disabled validation help text.");
        }

        public bool DismissFirstRunOnboarding()
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.DismissFirstRunOnboarding();
            return UpdateVisualActionStatus(true, "Dismissed onboarding guidance.");
        }

        public bool ShowFirstRunOnboarding()
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.ShowFirstRunOnboarding();
            return UpdateVisualActionStatus(true, "Displayed onboarding guidance.");
        }

        public bool SetAdvancedPropertiesFoldout(string key, bool isExpanded)
        {
            if (editorQoLService == null)
            {
                return false;
            }

            editorQoLService.SetAdvancedFoldoutState(key, isExpanded);
            return UpdateVisualActionStatus(true, isExpanded ? $"Expanded {key} advanced properties." : $"Collapsed {key} advanced properties.");
        }

        private static SandboxInspectorAuditEntry CreateAuditEntry(
            string key,
            string displayName,
            int namingFieldCount,
            int numericFieldCount,
            bool supportsMetadataFields,
            string advancedFoldoutKey)
        {
            return new SandboxInspectorAuditEntry
            {
                key = key,
                displayName = displayName,
                namingFieldCount = namingFieldCount,
                numericFieldCount = numericFieldCount,
                supportsMetadataFields = supportsMetadataFields,
                advancedFoldoutKey = advancedFoldoutKey
            };
        }

        private static void AddMissingDependency<TDependency>(ICollection<string> missingDependencies, TDependency dependency, string dependencyName)
            where TDependency : class
        {
            if (dependency == null)
            {
                missingDependencies.Add(dependencyName);
            }
        }

        private bool UpdateWallActionStatus(bool didSucceed, string successMessage)
        {
            if (didSucceed && statusBar != null)
            {
                statusBar.StatusMessage = successMessage;
            }

            return didSucceed;
        }

        private bool UpdateSemanticActionStatus(bool didSucceed, string successMessage)
        {
            if (didSucceed && statusBar != null)
            {
                statusBar.StatusMessage = successMessage;
            }

            return didSucceed;
        }

        private bool UpdateFloorActionStatus(bool didSucceed, string successMessage)
        {
            if (didSucceed && statusBar != null)
            {
                statusBar.StatusMessage = successMessage;
            }

            return didSucceed;
        }

        private bool UpdateVisualActionStatus(bool didSucceed, string successMessage)
        {
            if (didSucceed && statusBar != null)
            {
                statusBar.StatusMessage = successMessage;
            }

            return didSucceed;
        }
    }
}
