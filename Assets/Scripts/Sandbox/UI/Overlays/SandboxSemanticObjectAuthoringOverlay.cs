using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxSemanticObjectAuthoringOverlay : MonoBehaviour
    {
        [SerializeField] private Color doorGhostColor = new(0.18f, 0.55f, 1f, 0.75f);
        [SerializeField] private Color windowGhostColor = new(0.72f, 0.3f, 1f, 0.75f);
        [SerializeField] private Color invalidGhostColor = new(1f, 0.2f, 0.18f, 0.65f);
        [SerializeField] private Color ghostMaskColor = new(0.11f, 0.18f, 0.3f, 0.92f);
        [SerializeField] private float ghostLineWidth = 0.08f;
        [SerializeField] private float ghostMaskWidth = 0.15f;
        [SerializeField] private float ghostEdgeLength = 0.36f;

        private readonly GameObject[] ghostObjects = new GameObject[4];
        private string lastOpeningGhostStatus = string.Empty;
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

        private void OnDestroy()
        {
            ClearGhost();
        }

        private void Update()
        {
            UpdateOpeningGhost();

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
            var width = semanticObjectAuthoringService.GetPlacementOpeningWidth(SandboxVisualObjectType.Door);
            if (semanticObjectAuthoringService.PlaceDoor(worldPoint, out _, width))
            {
                UpdateStatus("Placed door on the nearest wall.");
                return;
            }

            UpdateStatus(GetOpeningPlacementMessage(worldPoint, SandboxVisualObjectType.Door, "Doors can only be placed on existing walls."));
        }

        private void HandleWindowPlacement(Vector2 worldPoint)
        {
            var width = semanticObjectAuthoringService.GetPlacementOpeningWidth(SandboxVisualObjectType.Window);
            if (semanticObjectAuthoringService.PlaceWindow(worldPoint, out _, width))
            {
                UpdateStatus("Placed window on the nearest wall.");
                return;
            }

            UpdateStatus(GetOpeningPlacementMessage(worldPoint, SandboxVisualObjectType.Window, "Windows can only be placed on existing walls."));
        }

        private string GetOpeningPlacementMessage(Vector2 worldPoint, SandboxVisualObjectType openingType, string fallback)
        {
            return semanticObjectAuthoringService.TryGetOpeningPlacementPreview(
                worldPoint,
                semanticObjectAuthoringService.GetPlacementOpeningWidth(openingType),
                openingType,
                null,
                out var preview)
                ? preview.message
                : fallback;
        }

        private void UpdateOpeningGhost()
        {
            if (toolStateService == null || semanticObjectAuthoringService == null)
            {
                ClearGhost();
                return;
            }

            var isDoorTool = toolStateService.CurrentToolMode == SandboxToolMode.Door;
            var isWindowTool = toolStateService.CurrentToolMode == SandboxToolMode.Window;
            if (!isDoorTool && !isWindowTool)
            {
                lastOpeningGhostStatus = string.Empty;
                ClearGhost();
                return;
            }

            var inputTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (inputTarget != SandboxInputTarget.World)
            {
                lastOpeningGhostStatus = string.Empty;
                ClearGhost();
                return;
            }

            var worldPoint = ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition);
            var openingType = isDoorTool ? SandboxVisualObjectType.Door : SandboxVisualObjectType.Window;
            var previewWidth = semanticObjectAuthoringService.GetPlacementOpeningWidth(openingType);
            semanticObjectAuthoringService.TryGetOpeningPlacementPreview(
                worldPoint,
                previewWidth,
                openingType,
                null,
                out var preview);

            var color = preview.isValid
                ? isDoorTool ? doorGhostColor : windowGhostColor
                : invalidGhostColor;
            var statusMessage = preview.isValid
                ? $"{(isDoorTool ? "Door" : "Window")} ready: click to place on wall."
                : preview.message;
            if (!string.Equals(lastOpeningGhostStatus, statusMessage, System.StringComparison.Ordinal))
            {
                lastOpeningGhostStatus = statusMessage;
                UpdateStatus(statusMessage);
            }

            if (preview.isValid)
            {
                RenderGhostLine(0, preview.start, preview.end, ghostMaskColor, ghostMaskWidth);

                var wallDirection = (preview.end - preview.start).normalized;
                var wallNormal = new Vector2(-wallDirection.y, wallDirection.x);
                var halfEdgeLength = ghostEdgeLength * 0.5f;
                RenderGhostLine(1, preview.start - wallNormal * halfEdgeLength, preview.start + wallNormal * halfEdgeLength, color, ghostLineWidth);
                RenderGhostLine(2, preview.end - wallNormal * halfEdgeLength, preview.end + wallNormal * halfEdgeLength, color, ghostLineWidth);
                ClearGhostObject(3);
            }
            else
            {
                RenderGhostLine(0, preview.center + new Vector2(-0.18f, 0f), preview.center + new Vector2(0.18f, 0f), invalidGhostColor, ghostLineWidth);
                RenderGhostLine(1, preview.center + new Vector2(0f, -0.18f), preview.center + new Vector2(0f, 0.18f), invalidGhostColor, ghostLineWidth);
                RenderGhostLine(2, preview.center + new Vector2(-0.25f, -0.25f), preview.center + new Vector2(0.25f, 0.25f), invalidGhostColor, ghostLineWidth);
                RenderGhostLine(3, preview.center + new Vector2(-0.25f, 0.25f), preview.center + new Vector2(0.25f, -0.25f), invalidGhostColor, ghostLineWidth);
            }
        }

        private void RenderGhostLine(int index, Vector2 start, Vector2 end, Color color, float lineWidth)
        {
            if (ghostObjects[index] == null)
            {
                ghostObjects[index] = new GameObject($"OpeningGhost_{index}");
                ghostObjects[index].transform.SetParent(transform, false);
                var lineRenderer = ghostObjects[index].AddComponent<LineRenderer>();
                lineRenderer.useWorldSpace = false;
                lineRenderer.positionCount = 2;
                lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            }

            var renderer = ghostObjects[index].GetComponent<LineRenderer>();
            renderer.widthMultiplier = lineWidth;
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.SetPosition(0, new Vector3(start.x, start.y, 0.06f));
            renderer.SetPosition(1, new Vector3(end.x, end.y, 0.06f));
        }

        private void ClearGhost()
        {
            for (var i = 0; i < ghostObjects.Length; i += 1)
            {
                ClearGhostObject(i);
            }
        }

        private void ClearGhostObject(int index)
        {
            if (ghostObjects[index] == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ghostObjects[index]);
            }
            else
            {
                DestroyImmediate(ghostObjects[index]);
            }

            ghostObjects[index] = null;
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
