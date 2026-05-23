using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxMeasurementOverlay : MonoBehaviour
    {
        private SandboxToolStateService toolStateService;
        private SandboxMeasurementService measurementService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            measurementService = FindAnyObjectByType<SandboxMeasurementService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
        }

        private void Update()
        {
            if (toolStateService == null ||
                measurementService == null ||
                toolStateService.CurrentToolMode != SandboxToolMode.Measure ||
                !SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var target = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (target != SandboxInputTarget.World)
            {
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            var readout = measurementService.RegisterMeasurementPoint(worldPoint);
            if (statusBar != null)
            {
                statusBar.StatusMessage = readout;
            }
        }

        private static Vector2 ScreenToWorldPoint(Vector3 screenPoint)
        {
            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return Vector2.zero;
            }

            screenPoint.z = Mathf.Abs(cameraComponent.transform.position.z);
            var worldPoint = cameraComponent.ScreenToWorldPoint(screenPoint);
            return new Vector2(worldPoint.x, worldPoint.y);
        }
    }
}
