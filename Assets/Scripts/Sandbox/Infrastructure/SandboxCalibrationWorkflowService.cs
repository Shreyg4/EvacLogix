using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxCalibrationWorkflowService : MonoBehaviour
    {
        [SerializeField] private bool isCalibrationCaptureActive;
        [SerializeField] private string targetFloorId = string.Empty;
        [SerializeField] private bool hasPointA;
        [SerializeField] private bool hasPointB;
        [SerializeField] private Vector2 pointA;
        [SerializeField] private Vector2 pointB;
        [SerializeField] private string statusPrompt = "Ready";

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxScaleCalibrationService calibrationService;

        public bool IsCalibrationCaptureActive => isCalibrationCaptureActive;
        public string TargetFloorId => targetFloorId;
        public bool HasPointA => hasPointA;
        public bool HasPointB => hasPointB;
        public Vector2 PointA => pointA;
        public Vector2 PointB => pointB;
        public string StatusPrompt => statusPrompt;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            calibrationService = GetComponent<SandboxScaleCalibrationService>();
        }

        public bool BeginCalibrationForActiveFloor()
        {
            return workspaceService?.ActiveFloor != null
                && BeginCalibrationForFloor(workspaceService.ActiveFloor.floorId);
        }

        public bool BeginCalibrationForFloor(string floorId)
        {
            var floor = workspaceService?.FindFloor(floorId);
            if (floor == null)
            {
                statusPrompt = "Select a floor before calibration.";
                return false;
            }

            var blueprintReference = workspaceService.FindBlueprintReference(floor.blueprintReferenceId);
            if (blueprintReference == null)
            {
                statusPrompt = "Import a blueprint before calibration.";
                return false;
            }

            targetFloorId = floor.floorId;
            hasPointA = false;
            hasPointB = false;
            pointA = Vector2.zero;
            pointB = Vector2.zero;
            isCalibrationCaptureActive = true;
            statusPrompt = "Click calibration point A.";
            return true;
        }

        public bool RegisterCalibrationPoint(Vector2 worldPoint)
        {
            if (!isCalibrationCaptureActive)
            {
                return false;
            }

            if (!hasPointA)
            {
                pointA = worldPoint;
                hasPointA = true;
                statusPrompt = "Click calibration point B.";
                return true;
            }

            if (hasPointB)
            {
                return false;
            }

            pointB = worldPoint;
            hasPointB = true;
            isCalibrationCaptureActive = false;
            statusPrompt = "Enter the real-world distance to finish calibration.";
            return true;
        }

        public bool TryCompleteCalibration(float realWorldDistance)
        {
            if (!hasPointA || !hasPointB || string.IsNullOrWhiteSpace(targetFloorId) || calibrationService == null)
            {
                statusPrompt = "Capture two calibration points before finishing.";
                return false;
            }

            var didCalibrate = calibrationService.CalibrateFloorBlueprint(targetFloorId, pointA, pointB, realWorldDistance);
            statusPrompt = calibrationService.LatestMeasurementFeedback;

            if (didCalibrate)
            {
                ResetCaptureState();
            }

            return didCalibrate;
        }

        public void CancelCalibration()
        {
            ResetCaptureState();
            statusPrompt = "Calibration cancelled.";
        }

        private void ResetCaptureState()
        {
            isCalibrationCaptureActive = false;
            targetFloorId = string.Empty;
            hasPointA = false;
            hasPointB = false;
            pointA = Vector2.zero;
            pointB = Vector2.zero;
        }
    }
}
