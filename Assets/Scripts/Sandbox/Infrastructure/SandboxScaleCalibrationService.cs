using EvacLogix.Sandbox.Data;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxScaleCalibrationService : MonoBehaviour
    {
        [SerializeField] private string latestMeasurementFeedback = string.Empty;

        private SandboxProjectWorkspaceService workspaceService;

        public string LatestMeasurementFeedback => latestMeasurementFeedback;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
        }

        public bool CalibrateFloorBlueprint(string floorId, Vector2 pointA, Vector2 pointB, float realWorldDistance)
        {
            if (workspaceService == null || realWorldDistance <= 0f)
            {
                latestMeasurementFeedback = "Calibration failed.";
                return false;
            }

            var floor = workspaceService.FindFloor(floorId);
            if (floor == null)
            {
                latestMeasurementFeedback = "Calibration failed.";
                return false;
            }

            var blueprintReference = workspaceService.FindBlueprintReference(floor.blueprintReferenceId);
            if (blueprintReference == null)
            {
                latestMeasurementFeedback = "Calibration failed.";
                return false;
            }

            var pixelDistance = Vector2.Distance(pointA, pointB);
            if (pixelDistance <= Mathf.Epsilon)
            {
                latestMeasurementFeedback = "Calibration failed.";
                return false;
            }

            blueprintReference.calibrationPointA = pointA;
            blueprintReference.calibrationPointB = pointB;
            blueprintReference.realWorldDistance = realWorldDistance;
            blueprintReference.worldUnitsPerPixel = realWorldDistance / pixelDistance;
            blueprintReference.isCalibrated = true;

            latestMeasurementFeedback =
                $"Scale set to {blueprintReference.worldUnitsPerPixel:0.####} world units per pixel using {realWorldDistance:0.##} units.";

            workspaceService.SetActiveProject(workspaceService.ActiveProject);
            return true;
        }
    }
}
