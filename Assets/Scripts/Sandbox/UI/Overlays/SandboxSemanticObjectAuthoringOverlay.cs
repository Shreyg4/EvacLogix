using System.Collections.Generic;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxSemanticObjectAuthoringOverlay : MonoBehaviour
    {
        [SerializeField] private Color doorGhostColor = new(0.18f, 0.55f, 1f, 0.75f);
        [SerializeField] private Color windowGhostColor = new(0.72f, 0.3f, 1f, 0.75f);
        [SerializeField] private Color exitGhostColor = new(0.2f, 0.9f, 0.5f, 0.75f);
        [SerializeField] private Color obstacleGhostColor = new(0.85f, 0.25f, 0.2f, 0.75f);
        [SerializeField] private Color teleportGhostColor = new(0.3f, 0.95f, 0.95f, 0.75f);
        [SerializeField] private Color fireStartGhostColor = new(0.9f, 0.2f, 0.1f, 0.75f);
        [SerializeField] private Color invalidGhostColor = new(1f, 0.2f, 0.18f, 0.65f);
        [SerializeField] private float fireStartGhostSize = 0.8f;
        [SerializeField] private Color ghostMaskColor = new(0.11f, 0.18f, 0.3f, 0.92f);
        [SerializeField] private float ghostLineWidth = 0.08f;
        [SerializeField] private float ghostMaskWidth = 0.15f;
        [SerializeField] private float ghostEdgeLength = 0.36f;

        private readonly GameObject[] ghostObjects = new GameObject[4];
        private GameObject fireGhostObject;
        private string lastOpeningGhostStatus = string.Empty;
        private SandboxToolStateService toolStateService;
        private SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
        private SandboxPreviewAuthoringService previewAuthoringService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;
        private SandboxCommandHistory commandHistory;
        private const float PlacementSnapPixelTolerance = 8f;
        private string pendingTeleportPortalId = string.Empty;
        private string pendingTeleportPairId = string.Empty;
        private int pendingTeleportColorIndex;
        private TeleportPortalKind pendingTeleportKind = TeleportPortalKind.Stair;
        private float pendingTeleportTravelCost = 1f;
        private bool pendingTeleportEnabled = true;
        private bool pendingTeleportCompletesBrokenPair;

        private void Awake()
        {
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            semanticObjectAuthoringService = FindAnyObjectByType<SandboxSemanticObjectAuthoringService>();
            previewAuthoringService = FindAnyObjectByType<SandboxPreviewAuthoringService>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            commandHistory = FindAnyObjectByType<SandboxCommandHistory>();

            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged += HandleToolModeChanged;
            }
        }

        private void OnDestroy()
        {
            if (toolStateService != null)
            {
                toolStateService.ToolModeChanged -= HandleToolModeChanged;
            }

            ClearGhost();
        }

        private void Update()
        {
            UpdatePlacementGhost();

            if (toolStateService == null || semanticObjectAuthoringService == null)
            {
                return;
            }

            if (IsPlacementTool(toolStateService.CurrentToolMode) && SandboxInputAdapter.WasRightMouseClickReleasedThisFrame())
            {
                CancelPlacementToSelect();
                return;
            }

            if (!SandboxInputAdapter.GetMouseButtonDown(0))
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
                case SandboxToolMode.Teleport:
                    HandleTeleportPlacement(worldPoint);
                    break;
                case SandboxToolMode.FireStart:
                    HandleFireStartPlacement(worldPoint);
                    break;
            }
        }

        private static bool IsPlacementTool(SandboxToolMode toolMode)
        {
            return toolMode == SandboxToolMode.Door ||
                   toolMode == SandboxToolMode.Window ||
                   toolMode == SandboxToolMode.Exit ||
                   toolMode == SandboxToolMode.Obstacle ||
                   toolMode == SandboxToolMode.Teleport ||
                   toolMode == SandboxToolMode.FireStart;
        }

        private void CancelPlacementToSelect()
        {
            var hadPendingTeleport = !string.IsNullOrWhiteSpace(pendingTeleportPortalId);
            toolStateService.RequestToolModeChange(SandboxToolMode.Select, commandHistory);
            UpdateStatus(hadPendingTeleport
                ? "Cancelled teleport placement. Back to Select."
                : "Exited placement. Back to Select.");
        }

        private void HandleToolModeChanged(SandboxToolMode toolMode)
        {
            if (toolMode != SandboxToolMode.Teleport)
            {
                ResetPendingTeleportPlacement();
            }
        }

        private void ResetPendingTeleportPlacement()
        {
            pendingTeleportPortalId = string.Empty;
            pendingTeleportPairId = string.Empty;
            pendingTeleportColorIndex = 0;
            pendingTeleportKind = TeleportPortalKind.Stair;
            pendingTeleportTravelCost = 1f;
            pendingTeleportEnabled = true;
            pendingTeleportCompletesBrokenPair = false;
        }

        public bool BeginMissingTeleportPairPlacement(string teleportPortalId)
        {
            var project = workspaceService?.ActiveProject;
            if (project == null || string.IsNullOrWhiteSpace(teleportPortalId))
            {
                return false;
            }

            foreach (var floor in project.floors)
            {
                var portal = floor.teleportPortals.Find(candidate => candidate.teleportPortalId == teleportPortalId);
                if (portal == null)
                {
                    continue;
                }

                pendingTeleportPortalId = portal.teleportPortalId;
                pendingTeleportPairId = string.IsNullOrWhiteSpace(portal.pairId) ? SandboxId.NewId() : portal.pairId;
                pendingTeleportColorIndex = portal.pairColorIndex;
                pendingTeleportKind = portal.kind;
                pendingTeleportTravelCost = Mathf.Max(0.1f, portal.travelCost);
                pendingTeleportEnabled = portal.isPairEnabled;
                pendingTeleportCompletesBrokenPair = true;
                UpdateStatus("Click to place the missing teleport endpoint. You can switch floors first.");
                return true;
            }

            return false;
        }

        private void HandleDoorPlacement(Vector2 worldPoint)
        {
            var width = semanticObjectAuthoringService.GetPlacementOpeningWidth(SandboxVisualObjectType.Door);
            if (semanticObjectAuthoringService.PlaceDoor(worldPoint, out _, width, semanticObjectAuthoringService.DefaultDoorState))
            {
                UpdateStatus("Placed door on the nearest wall.");
                return;
            }

            UpdateStatus(GetOpeningPlacementMessage(worldPoint, SandboxVisualObjectType.Door, "Doors can only be placed on existing walls."));
        }

        private void HandleWindowPlacement(Vector2 worldPoint)
        {
            var width = semanticObjectAuthoringService.GetPlacementOpeningWidth(SandboxVisualObjectType.Window);
            if (semanticObjectAuthoringService.PlaceWindow(worldPoint, out _, width, semanticObjectAuthoringService.DefaultWindowEscape))
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

        private void UpdatePlacementGhost()
        {
            if (toolStateService == null || semanticObjectAuthoringService == null)
            {
                ClearGhost();
                return;
            }

            var mode = toolStateService.CurrentToolMode;
            if (mode == SandboxToolMode.Door || mode == SandboxToolMode.Window)
            {
                UpdateOpeningGhost();
                return;
            }

            if (mode == SandboxToolMode.FireStart)
            {
                UpdateFireStartPlacementGhost();
                return;
            }

            if (mode == SandboxToolMode.Exit || mode == SandboxToolMode.Obstacle || mode == SandboxToolMode.Teleport)
            {
                UpdateRectanglePlacementGhost(mode);
                return;
            }

            lastOpeningGhostStatus = string.Empty;
            ClearGhost();
        }

        private void UpdateRectanglePlacementGhost(SandboxToolMode mode)
        {
            var inputTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (workspaceService?.ActiveFloor == null || inputTarget != SandboxInputTarget.World)
            {
                lastOpeningGhostStatus = string.Empty;
                ClearGhost();
                return;
            }

            SandboxVisualObjectType type;
            Vector2 size;
            Color color;
            switch (mode)
            {
                case SandboxToolMode.Exit:
                    type = SandboxVisualObjectType.Exit;
                    size = semanticObjectAuthoringService.DefaultExitZoneSize;
                    color = exitGhostColor;
                    break;
                case SandboxToolMode.Obstacle:
                    type = SandboxVisualObjectType.Obstacle;
                    size = semanticObjectAuthoringService.DefaultObstacleSize;
                    color = obstacleGhostColor;
                    break;
                default:
                    type = SandboxVisualObjectType.Teleport;
                    size = semanticObjectAuthoringService.DefaultTeleportPortalSize;
                    color = teleportGhostColor;
                    break;
            }

            var center = ApplyPlacementSnap(type, ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition));
            var half = size * 0.5f;
            var bottomLeft = center + new Vector2(-half.x, -half.y);
            var topLeft = center + new Vector2(-half.x, half.y);
            var topRight = center + new Vector2(half.x, half.y);
            var bottomRight = center + new Vector2(half.x, -half.y);
            RenderGhostLine(0, bottomLeft, topLeft, color, ghostLineWidth);
            RenderGhostLine(1, topLeft, topRight, color, ghostLineWidth);
            RenderGhostLine(2, topRight, bottomRight, color, ghostLineWidth);
            RenderGhostLine(3, bottomRight, bottomLeft, color, ghostLineWidth);

            var statusMessage = $"{type} ready: click to place.";
            if (!string.Equals(lastOpeningGhostStatus, statusMessage, System.StringComparison.Ordinal))
            {
                lastOpeningGhostStatus = statusMessage;
                UpdateStatus(statusMessage);
            }
        }

        private void UpdateFireStartPlacementGhost()
        {
            var inputTarget = inputRouter != null
                ? inputRouter.ResolvePointerTarget(SandboxInputAdapter.PointerScreenPosition)
                : SandboxInputTarget.World;
            if (workspaceService?.ActiveFloor == null || inputTarget != SandboxInputTarget.World)
            {
                lastOpeningGhostStatus = string.Empty;
                ClearGhost();
                return;
            }

            var center = ApplyPlacementSnap(SandboxVisualObjectType.FireStart, ScreenToWorldPoint(SandboxInputAdapter.PointerScreenPosition));
            ClearGhostObject(0);
            ClearGhostObject(1);
            ClearGhostObject(2);
            ClearGhostObject(3);
            RenderFireGhostCircle(center, fireStartGhostSize * 0.5f, fireStartGhostColor);

            const string statusMessage = "Fire start ready: click to place.";
            if (!string.Equals(lastOpeningGhostStatus, statusMessage, System.StringComparison.Ordinal))
            {
                lastOpeningGhostStatus = statusMessage;
                UpdateStatus(statusMessage);
            }
        }

        private void RenderFireGhostCircle(Vector2 center, float radius, Color color)
        {
            const int segments = 24;
            if (fireGhostObject == null)
            {
                fireGhostObject = new GameObject("FireStartGhost");
                fireGhostObject.transform.SetParent(transform, false);
                var created = fireGhostObject.AddComponent<LineRenderer>();
                created.useWorldSpace = true;
                created.loop = true;
                created.material = new Material(Shader.Find("Sprites/Default"));
                created.numCapVertices = 2;
            }

            var renderer = fireGhostObject.GetComponent<LineRenderer>();
            renderer.startWidth = ghostLineWidth;
            renderer.endWidth = ghostLineWidth;
            renderer.startColor = color;
            renderer.endColor = color;
            renderer.positionCount = segments;
            for (var i = 0; i < segments; i += 1)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                renderer.SetPosition(i, new Vector3(point.x, point.y, 0f));
            }
        }

        private void ClearFireGhost()
        {
            if (fireGhostObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(fireGhostObject);
            }
            else
            {
                DestroyImmediate(fireGhostObject);
            }

            fireGhostObject = null;
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

            ClearFireGhost();
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
            worldPoint = ApplyPlacementSnap(SandboxVisualObjectType.Exit, worldPoint);
            if (semanticObjectAuthoringService.PlaceExit(worldPoint, out _))
            {
                UpdateStatus("Placed exit zone.");
            }
        }

        private void HandleObstaclePlacement(Vector2 worldPoint)
        {
            worldPoint = ApplyPlacementSnap(SandboxVisualObjectType.Obstacle, worldPoint);
            if (semanticObjectAuthoringService.PlaceObstacle(worldPoint, out _))
            {
                UpdateStatus("Placed obstacle.");
            }
        }

        private void HandleFireStartPlacement(Vector2 worldPoint)
        {
            worldPoint = ApplyPlacementSnap(SandboxVisualObjectType.FireStart, worldPoint);
            if (previewAuthoringService != null && previewAuthoringService.PlaceFireOrigin(worldPoint, out _))
            {
                UpdateStatus("Placed fire start. It will spread during simulation.");
                return;
            }

            UpdateStatus("Could not place a fire start on the active floor.");
        }

        // Snaps the placement center to same-type peers, walls, and the grid (boxes only).
        private Vector2 ApplyPlacementSnap(SandboxVisualObjectType objectType, Vector2 point)
        {
            if (workspaceStateService != null && !workspaceStateService.SnappingEnabled)
            {
                return point;
            }

            var floor = workspaceService?.ActiveFloor;
            if (floor == null)
            {
                return point;
            }

            var gridSize = workspaceStateService != null ? workspaceStateService.GridSize : 0.5f;
            var tolerance = SandboxAlignmentGuideUtility.PixelToleranceToWorld(Camera.main, PlacementSnapPixelTolerance);
            var referenceXs = new List<float>();
            var referenceYs = new List<float>();
            SandboxAlignmentGuideUtility.CollectSameTypeAxisReferences(floor, objectType, null, referenceXs, referenceYs);
            SandboxAlignmentGuideUtility.CollectWallAxisReferences(floor, referenceXs, referenceYs);

            var result = point;
            if (SandboxAlignmentGuideUtility.TryResolveAxisSnap(new[] { point.x }, referenceXs, gridSize, tolerance, true, out var offsetX))
            {
                result.x += offsetX;
            }

            if (SandboxAlignmentGuideUtility.TryResolveAxisSnap(new[] { point.y }, referenceYs, gridSize, tolerance, false, out var offsetY))
            {
                result.y += offsetY;
            }

            return result;
        }


        private void HandleTeleportPlacement(Vector2 worldPoint)
        {
            if (workspaceService?.ActiveFloor == null)
            {
                return;
            }

            worldPoint = ApplyPlacementSnap(SandboxVisualObjectType.Teleport, worldPoint);

            if (string.IsNullOrWhiteSpace(pendingTeleportPortalId))
            {
                pendingTeleportPairId = SandboxId.NewId();
                pendingTeleportColorIndex = semanticObjectAuthoringService != null
                    ? semanticObjectAuthoringService.GetNextTeleportPairColorIndex()
                    : 0;
                pendingTeleportKind = TeleportPortalKind.Stair;
                pendingTeleportTravelCost = 1f;
                pendingTeleportEnabled = true;
                pendingTeleportCompletesBrokenPair = false;

                if (semanticObjectAuthoringService != null &&
                    semanticObjectAuthoringService.PlaceTeleportPortal(
                        worldPoint,
                        out var createdPortalId,
                        pendingTeleportPairId,
                        pendingTeleportColorIndex,
                        semanticObjectAuthoringService.DefaultTeleportPortalSize,
                        0f,
                        string.Empty,
                        pendingTeleportKind,
                        pendingTeleportTravelCost,
                        pendingTeleportEnabled))
                {
                    pendingTeleportPortalId = createdPortalId;
                    UpdateStatus("Placed teleport endpoint A. Switch floors if needed, then click to place endpoint B.");
                }

                return;
            }

            if (semanticObjectAuthoringService == null ||
                !semanticObjectAuthoringService.PlaceTeleportPortal(
                    worldPoint,
                    out var partnerPortalId,
                    pendingTeleportPairId,
                    pendingTeleportColorIndex,
                    semanticObjectAuthoringService.DefaultTeleportPortalSize,
                    0f,
                    string.Empty,
                    pendingTeleportKind,
                    pendingTeleportTravelCost,
                    pendingTeleportEnabled))
            {
                UpdateStatus("Could not place teleport endpoint.");
                return;
            }

            if (!semanticObjectAuthoringService.LinkTeleportPortals(
                    ResolveSourceFloorId(pendingTeleportPortalId),
                    pendingTeleportPortalId,
                    workspaceService.ActiveFloor.floorId,
                    partnerPortalId,
                    pendingTeleportKind,
                    pendingTeleportTravelCost,
                    pendingTeleportEnabled))
            {
                UpdateStatus("Placed teleport endpoint, but pairing failed.");
            }
            else
            {
                UpdateStatus(pendingTeleportCompletesBrokenPair
                    ? "Restored the teleporter pair."
                    : "Placed teleport pair.");
            }

            pendingTeleportPortalId = string.Empty;
            pendingTeleportPairId = string.Empty;
            pendingTeleportCompletesBrokenPair = false;
        }

        private string ResolveSourceFloorId(string teleportPortalId)
        {
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                return workspaceService?.ActiveFloor?.floorId ?? string.Empty;
            }

            foreach (var floor in project.floors)
            {
                if (floor.teleportPortals.Exists(candidate => candidate.teleportPortalId == teleportPortalId))
                {
                    return floor.floorId;
                }
            }

            return workspaceService?.ActiveFloor?.floorId ?? string.Empty;
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
