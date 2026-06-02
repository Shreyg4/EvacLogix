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
        private SandboxInputRouter inputRouter;
        private bool spawnPointBrushActive;
        private float spawnPointBrushTimer;

        private void Awake()
        {
            previewService = FindAnyObjectByType<SandboxPreviewService>();
            previewAuthoringService = FindAnyObjectByType<SandboxPreviewAuthoringService>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
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
            if (!ShouldHandlePreviewClick())
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
                ShowError("Could not place fire origin on the active floor.");
                return;
            }

            previewService.ClearInteractionMode();
            UpdateStatus("Placed fire origin for preview.");
        }

        private void HandleSpawnPointPlacement(Vector2 worldPoint)
        {
            if (!ShouldHandlePreviewClick())
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
                ShowError(string.IsNullOrWhiteSpace(failureMessage) ? "Could not place spawn point." : failureMessage);
                return;
            }

            previewService.SetActiveSpawnLayout(resolvedLayoutId);
            UpdateStatus("Placed spawn point. Click again to add another.");
        }

        private void HandleSpawnPointBrushPlacement(Vector2 worldPoint)
        {
            if (ShouldHandlePreviewClick())
            {
                spawnPointBrushActive = true;
                spawnPointBrushTimer = 0f;
                TryPlaceSpawnPointBrushStamp(worldPoint);
                UpdateStatus("Painting spawn point brush.");
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
                ShowError(string.IsNullOrWhiteSpace(failureMessage) ? "Could not place spawn point." : failureMessage);
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
            // Resolve lazily: this overlay is created before the status-bar shell exists, so the
            // Awake-time lookup can come back null.
            statusBar ??= FindAnyObjectByType<SandboxStatusBarShell>();
            if (statusBar != null)
            {
                statusBar.StatusMessage = message;
            }
        }

        // Surfaces a rejection as a prominent, auto-fading banner over the canvas (plus the status box).
        private void ShowError(string message)
        {
            statusBar ??= FindAnyObjectByType<SandboxStatusBarShell>();
            if (statusBar != null)
            {
                statusBar.ShowNotice(message, true);
            }
        }

        private bool ShouldHandlePreviewClick()
        {
            if (!SandboxInputAdapter.GetMouseButtonDown(0) || previewService == null)
            {
                return false;
            }

            if (previewService.InteractionModeChangedFrame == Time.frameCount)
            {
                return false;
            }

            inputRouter ??= FindAnyObjectByType<SandboxInputRouter>();
            var target = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            return target == SandboxInputTarget.World || target == SandboxInputTarget.PreviewOverlay;
        }
    }
}
