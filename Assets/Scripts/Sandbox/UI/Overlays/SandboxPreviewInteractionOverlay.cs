using System.Collections.Generic;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxPreviewInteractionOverlay : MonoBehaviour
    {
        [SerializeField] private float minimumBrushSampleDistance = 0.25f;

        private readonly List<Vector2> activeBrushPoints = new();
        private SandboxPreviewService previewService;
        private SandboxPreviewAuthoringService previewAuthoringService;
        private SandboxStatusBarShell statusBar;
        private bool regionDragActive;
        private Vector2 regionStartPoint;
        private Vector2 lastBrushPoint;

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
                case SandboxPreviewInteractionMode.PaintSpawnBrush:
                    HandleSpawnBrushPlacement(worldPoint);
                    break;
                case SandboxPreviewInteractionMode.PlaceRegion:
                    HandleRegionPlacement(worldPoint);
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
                    previewService.PendingSpawnLayoutId,
                    previewService.PendingSpawnLayoutName,
                    previewService.PendingSpawnLayoutIsPersistent))
            {
                UpdateStatus("Could not place preview spawn point.");
                return;
            }

            previewService.SetActiveSpawnLayout(resolvedLayoutId);
            previewService.ClearInteractionMode();
            UpdateStatus("Placed preview spawn point.");
        }

        private void HandleSpawnBrushPlacement(Vector2 worldPoint)
        {
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                activeBrushPoints.Clear();
                activeBrushPoints.Add(worldPoint);
                lastBrushPoint = worldPoint;
                UpdateStatus("Painting preview spawn brush.");
                return;
            }

            if (SandboxInputAdapter.GetMouseButton(0) && activeBrushPoints.Count > 0)
            {
                if (Vector2.Distance(lastBrushPoint, worldPoint) >= minimumBrushSampleDistance)
                {
                    activeBrushPoints.Add(worldPoint);
                    lastBrushPoint = worldPoint;
                }
            }

            if (SandboxInputAdapter.GetMouseButtonUp(0) && activeBrushPoints.Count >= 3)
            {
                if (!previewAuthoringService.PlaceSpawnBrush(
                        activeBrushPoints,
                        out _,
                        out var resolvedLayoutId,
                        previewService.PendingSpawnBrushDensity,
                        previewService.PendingSpawnLayoutId,
                        previewService.PendingSpawnLayoutName,
                        previewService.PendingSpawnLayoutIsPersistent))
                {
                    UpdateStatus("Could not commit preview spawn brush.");
                    activeBrushPoints.Clear();
                    return;
                }

                previewService.SetActiveSpawnLayout(resolvedLayoutId);
                previewService.ClearInteractionMode();
                activeBrushPoints.Clear();
                UpdateStatus("Committed preview spawn brush.");
                return;
            }

            if (SandboxInputAdapter.GetMouseButtonUp(0))
            {
                activeBrushPoints.Clear();
            }
        }

        private void HandleRegionPlacement(Vector2 worldPoint)
        {
            if (SandboxInputAdapter.GetMouseButtonDown(0))
            {
                regionDragActive = true;
                regionStartPoint = worldPoint;
                UpdateStatus("Dragging named preview region.");
                return;
            }

            if (!regionDragActive || !SandboxInputAdapter.GetMouseButtonUp(0))
            {
                return;
            }

            regionDragActive = false;
            var size = new Vector2(Mathf.Abs(worldPoint.x - regionStartPoint.x), Mathf.Abs(worldPoint.y - regionStartPoint.y));
            if (size.x <= 0.05f || size.y <= 0.05f)
            {
                UpdateStatus("Preview region was too small to keep.");
                return;
            }

            var center = (regionStartPoint + worldPoint) * 0.5f;
            if (!previewAuthoringService.PlaceRegion(center, size, out _, previewService.PendingRegionName, previewService.PendingRegionSemanticType))
            {
                UpdateStatus("Could not place named preview region.");
                return;
            }

            previewService.ClearInteractionMode();
            UpdateStatus("Placed named preview region.");
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
