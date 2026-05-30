using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxPreviewInteractionOverlay : MonoBehaviour
    {
        [SerializeField] private float spawnPointBrushPlacementIntervalSeconds = 0.75f;

        private SandboxPreviewService previewService;
        private SandboxPreviewAuthoringService previewAuthoringService;
        private SandboxStatusBarShell statusBar;
        private bool spawnPointBrushActive;
        private float spawnPointBrushTimer;

        private void Awake()
        {
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            previewAuthoringService = FindAnyObjectByType<SandboxPreviewAuthoringService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
        }

        private void Update()
        {
            if (previewService == null || previewAuthoringService == null || !previewService.IsPreviewModeActive)
            {
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            switch (previewService.InteractionMode)
            {
                case SandboxPreviewInteractionMode.PlaceFireOrigin:
                    HandleFireOriginPlacement(worldPoint);
                    break;
                case SandboxPreviewInteractionMode.PlaceSpawnPoint:
                    HandleSpawnPointPlacement(worldPoint);
                    break;
                case SandboxPreviewInteractionMode.PaintSpawnPointBrush:
                    HandleSpawnPointBrushPlacement(worldPoint);
                    break;
            }
        }

        private void HandleFireOriginPlacement(Vector2 worldPoint)
        {
            if (!SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            if (!previewAuthoringService.PlaceFireOrigin(
                    worldPoint,
                    out _,
                    previewService.ActivePreviewParameters.spreadIntensity,
                    previewService.ActivePreviewParameters.startDelaySeconds,
                    previewService.PendingFireOriginIsPersistent))
            {
                UpdateStatus("Could not place fire origin on the active floor.");
                return;
            }

            previewService.ClearInteractionMode();
            UpdateStatus("Placed fire origin for preview.");
        }

        private void HandleSpawnPointPlacement(Vector2 worldPoint)
        {
            if (!SandboxInputAdapter.GetMouseButtonDown(0))
            {
                return;
            }

            if (!previewAuthoringService.PlaceSpawnPoint(
                    worldPoint,
                    out _,
                    out var resolvedLayoutId,
                    out var failureMessage,
                    previewService.PendingSpawnLayoutId,
                    previewService.PendingSpawnLayoutName,
                    previewService.PendingSpawnLayoutIsPersistent))
            {
                UpdateStatus(string.IsNullOrWhiteSpace(failureMessage) ? "Could not place spawn point." : failureMessage);
                return;
            }

            previewService.SetActiveSpawnLayout(resolvedLayoutId);
            UpdateStatus("Placed spawn point. Click again to add another.");
        }

        private void HandleSpawnPointBrushPlacement(Vector2 worldPoint)
        {
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                spawnPointBrushActive = true;
                spawnPointBrushTimer = 0f;
                TryPlaceSpawnPointBrushStamp(worldPoint);
                UpdateStatus("Painting spawn point brush in an enclosed room.");
                return;
            }

            if (SandboxInputAdapter.GetMouseButton(0) && spawnPointBrushActive)
            {
                spawnPointBrushTimer += Time.deltaTime;
                while (spawnPointBrushTimer >= spawnPointBrushPlacementIntervalSeconds)
                {
                    spawnPointBrushTimer -= spawnPointBrushPlacementIntervalSeconds;
                    TryPlaceSpawnPointBrushStamp(worldPoint);
                }
            }

            if (SandboxInputAdapter.GetMouseButtonUp(0))
            {
                spawnPointBrushActive = false;
                spawnPointBrushTimer = 0f;
            }
        }

        private void TryPlaceSpawnPointBrushStamp(Vector2 worldPoint)
        {
            if (!previewAuthoringService.PlaceSpawnPoint(
                    worldPoint,
                    out _,
                    out var resolvedLayoutId,
                    out var failureMessage,
                    previewService.PendingSpawnLayoutId,
                    previewService.PendingSpawnLayoutName,
                    previewService.PendingSpawnLayoutIsPersistent))
            {
                UpdateStatus(string.IsNullOrWhiteSpace(failureMessage) ? "Could not place spawn point." : failureMessage);
                return;
            }

            previewService.SetActiveSpawnLayout(resolvedLayoutId);
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
