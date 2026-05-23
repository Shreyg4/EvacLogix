using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxCalibrationCaptureOverlay : MonoBehaviour
    {
        private SandboxCalibrationWorkflowService calibrationWorkflowService;
        private SandboxInputRouter inputRouter;

        private void Awake()
        {
            calibrationWorkflowService = FindAnyObjectByType<SandboxCalibrationWorkflowService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
        }

        private void Update()
        {
            var shouldCaptureInput = calibrationWorkflowService != null
                && calibrationWorkflowService.IsCalibrationCaptureActive
                && !calibrationWorkflowService.HasPointB;

            inputRouter?.SetPreviewOverlayCapturingInput(shouldCaptureInput);

            if (!shouldCaptureInput || !SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return;
            }

            var inputTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;

            if (inputTarget != SandboxInputTarget.World && inputTarget != SandboxInputTarget.PreviewOverlay)
            {
                return;
            }

            var screenPoint = (Vector3)SandboxInputAdapter.PointerScreenPosition;
            screenPoint.z = Mathf.Abs(cameraComponent.transform.position.z);
            var worldPoint = cameraComponent.ScreenToWorldPoint(screenPoint);
            calibrationWorkflowService.RegisterCalibrationPoint(new Vector2(worldPoint.x, worldPoint.y));
        }

        private void OnDisable()
        {
            inputRouter?.SetPreviewOverlayCapturingInput(false);
        }

        private void OnDestroy()
        {
            inputRouter?.SetPreviewOverlayCapturingInput(false);
        }
    }
}
