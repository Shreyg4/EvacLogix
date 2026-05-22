using UnityEngine;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxInspectorPanelShell : MonoBehaviour
    {
        [SerializeField] private bool showSelectionSummary = true;
        [SerializeField] private string latestCalibrationFeedback = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxBlueprintImportService blueprintImportService;
        private SandboxScaleCalibrationService calibrationService;
        private SandboxCalibrationWorkflowService calibrationWorkflowService;
        private SandboxPreviewImageExportService previewImageExportService;
        private SandboxStatusBarShell statusBar;

        public bool ShowSelectionSummary => showSelectionSummary;
        public string LatestCalibrationFeedback => latestCalibrationFeedback;

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            blueprintImportService = FindAnyObjectByType<SandboxBlueprintImportService>();
            calibrationService = FindAnyObjectByType<SandboxScaleCalibrationService>();
            calibrationWorkflowService = FindAnyObjectByType<SandboxCalibrationWorkflowService>();
            previewImageExportService = FindAnyObjectByType<SandboxPreviewImageExportService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
        }

        public BlueprintReferenceData ImportBlueprintToActiveFloor(string sourceFilePath)
        {
            if (workspaceService?.ActiveFloor == null || blueprintImportService == null)
            {
                return null;
            }

            var blueprintReference = blueprintImportService.ImportBlueprint(sourceFilePath);
            workspaceService.AddBlueprintReference(blueprintReference);
            workspaceService.AssignBlueprintToFloor(workspaceService.ActiveFloor.floorId, blueprintReference.blueprintReferenceId);
            if (statusBar != null)
            {
                statusBar.StatusMessage = $"Imported blueprint {blueprintReference.sourceFileName}.";
            }
            return blueprintReference;
        }

        public bool SetActiveFloorBlueprintOpacity(float opacity)
        {
            if (workspaceService?.ActiveFloor == null)
            {
                return false;
            }

            return workspaceService.SetBlueprintOpacity(workspaceService.ActiveFloor.blueprintReferenceId, opacity);
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
    }
}
