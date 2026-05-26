using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxSemanticObjectAuthoringOverlay : MonoBehaviour
    {
        private SandboxToolStateService toolStateService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
        }

        private void Update()
        {
            if (toolStateService == null || semanticObjectAuthoringService == null || !SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            var inputTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (inputTarget != SandboxInputTarget.World)
            {
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            switch (toolStateService.CurrentToolMode)
            {
                case SandboxToolMode.Door:
                    HandleDoorPlacement(worldPoint);
                    break;
                case SandboxToolMode.Window:
                    HandleWindowPlacement(worldPoint);
                    break;
                case SandboxToolMode.Exit:
                    HandleExitPlacement(worldPoint);
                    break;
                case SandboxToolMode.Obstacle:
                    HandleObstaclePlacement(worldPoint);
                    break;
                case SandboxToolMode.Stair:
                    HandleStairPlacement(worldPoint);
                    break;
            }
        }

        private void HandleDoorPlacement(Vector2 worldPoint)
        {
            if (semanticObjectAuthoringService.PlaceDoor(worldPoint, out _))
            {
                UpdateStatus("Placed door on the nearest wall.");
                return;
            }

            UpdateStatus("Doors can only be placed on existing walls.");
        }

        private void HandleWindowPlacement(Vector2 worldPoint)
        {
            if (semanticObjectAuthoringService.PlaceWindow(worldPoint, out _))
            {
                UpdateStatus("Placed window on the nearest wall.");
                return;
            }

            UpdateStatus("Windows can only be placed on existing walls.");
        }

        private void HandleExitPlacement(Vector2 worldPoint)
        {
            if (semanticObjectAuthoringService.PlaceExit(worldPoint, out _))
            {
                UpdateStatus("Placed exit zone.");
            }
        }

        private void HandleObstaclePlacement(Vector2 worldPoint)
        {
            if (semanticObjectAuthoringService.PlaceObstacle(worldPoint, out _))
            {
                UpdateStatus("Placed obstacle.");
            }
        }

        private void HandleStairPlacement(Vector2 worldPoint)
        {
            if (semanticObjectAuthoringService.PlaceStairPortal(worldPoint, out _))
            {
                UpdateStatus("Placed stair endpoint. Link it to another floor from the inspector.");
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

        private void UpdateStatus(string message)
        {
            if (statusBar != null)
            {
                statusBar.StatusMessage = message;
            }
        }
    }
}
