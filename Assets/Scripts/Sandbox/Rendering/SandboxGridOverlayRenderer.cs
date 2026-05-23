using System.Collections.Generic;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxGridOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private Color lineColor = new(1f, 1f, 1f, 0.18f);
        [SerializeField] private float lineWidth = 0.025f;
        [SerializeField] private float paddingMultiplier = 2f;
        [SerializeField] private int maxLinesPerAxis = 48;

        private readonly List<GameObject> renderedObjects = new();
        private SandboxWorkspaceStateService workspaceStateService;
        private Camera targetCamera;
        private Vector3 lastCameraPosition;
        private float lastOrthographicSize = -1f;
        private float lastAspect = -1f;
        private float lastGridSize = -1f;
        private bool lastGridVisible;

        private void Awake()
        {
            workspaceStateService = FindAnyObjectByType<SandboxWorkspaceStateService>();
            targetCamera = Camera.main;
            if (workspaceStateService != null)
            {
                workspaceStateService.GridVisibilityChanged += HandleGridSettingsChanged;
                workspaceStateService.GridSizeChanged += HandleGridSizeChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (workspaceStateService != null)
            {
                workspaceStateService.GridVisibilityChanged -= HandleGridSettingsChanged;
                workspaceStateService.GridSizeChanged -= HandleGridSizeChanged;
            }
        }

        private void LateUpdate()
        {
            targetCamera ??= Camera.main;
            if (ShouldRefresh())
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            Clear();

            workspaceStateService ??= FindAnyObjectByType<SandboxWorkspaceStateService>();
            targetCamera ??= Camera.main;
            if (workspaceStateService == null || targetCamera == null || !targetCamera.orthographic)
            {
                RecordCurrentState();
                return;
            }

            var gridVisible = workspaceStateService.GridVisible;
            var gridSize = Mathf.Max(0.05f, workspaceStateService.GridSize);
            if (!gridVisible)
            {
                RecordCurrentState(gridVisible, gridSize);
                return;
            }

            var center = (Vector2)targetCamera.transform.position;
            var halfHeight = targetCamera.orthographicSize + gridSize * paddingMultiplier;
            var halfWidth = targetCamera.orthographicSize * targetCamera.aspect + gridSize * paddingMultiplier;
            var lineStep = ResolveLineStep(gridSize, Mathf.Max(halfWidth, halfHeight) * 2f);

            var minX = Mathf.Floor((center.x - halfWidth) / lineStep) * lineStep;
            var maxX = Mathf.Ceil((center.x + halfWidth) / lineStep) * lineStep;
            var minY = Mathf.Floor((center.y - halfHeight) / lineStep) * lineStep;
            var maxY = Mathf.Ceil((center.y + halfHeight) / lineStep) * lineStep;

            for (var x = minX; x <= maxX + lineStep * 0.5f; x += lineStep)
            {
                RenderLine(
                    $"GridVertical_{x:0.###}",
                    new Vector3(x, minY, 0f),
                    new Vector3(x, maxY, 0f));
            }

            for (var y = minY; y <= maxY + lineStep * 0.5f; y += lineStep)
            {
                RenderLine(
                    $"GridHorizontal_{y:0.###}",
                    new Vector3(minX, y, 0f),
                    new Vector3(maxX, y, 0f));
            }

            RecordCurrentState(gridVisible, gridSize);
        }

        private void HandleGridSettingsChanged(bool _)
        {
            Refresh();
        }

        private void HandleGridSizeChanged(float _)
        {
            Refresh();
        }

        private bool ShouldRefresh()
        {
            if (targetCamera == null)
            {
                return lastGridVisible || renderedObjects.Count > 0;
            }

            var gridVisible = workspaceStateService == null || workspaceStateService.GridVisible;
            var gridSize = workspaceStateService == null ? 0.5f : Mathf.Max(0.05f, workspaceStateService.GridSize);
            return lastGridVisible != gridVisible ||
                   !Mathf.Approximately(lastGridSize, gridSize) ||
                   targetCamera.transform.position != lastCameraPosition ||
                   !Mathf.Approximately(targetCamera.orthographicSize, lastOrthographicSize) ||
                   !Mathf.Approximately(targetCamera.aspect, lastAspect);
        }

        private void RenderLine(string name, Vector3 start, Vector3 end)
        {
            var lineObject = new GameObject(name);
            lineObject.transform.SetParent(transform, false);

            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;
            renderedObjects.Add(lineObject);
        }

        private float ResolveLineStep(float gridSize, float span)
        {
            var estimatedLineCount = Mathf.CeilToInt(span / gridSize);
            if (estimatedLineCount <= maxLinesPerAxis)
            {
                return gridSize;
            }

            return gridSize * Mathf.CeilToInt((float)estimatedLineCount / maxLinesPerAxis);
        }

        private void RecordCurrentState()
        {
            RecordCurrentState(
                workspaceStateService == null || workspaceStateService.GridVisible,
                workspaceStateService == null ? 0.5f : Mathf.Max(0.05f, workspaceStateService.GridSize));
        }

        private void RecordCurrentState(bool gridVisible, float gridSize)
        {
            lastGridVisible = gridVisible;
            lastGridSize = gridSize;

            if (targetCamera == null)
            {
                lastCameraPosition = Vector3.zero;
                lastOrthographicSize = -1f;
                lastAspect = -1f;
                return;
            }

            lastCameraPosition = targetCamera.transform.position;
            lastOrthographicSize = targetCamera.orthographicSize;
            lastAspect = targetCamera.aspect;
        }

        private void Clear()
        {
            for (var i = 0; i < renderedObjects.Count; i += 1)
            {
                if (renderedObjects[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(renderedObjects[i]);
                }
                else
                {
                    DestroyImmediate(renderedObjects[i]);
                }
            }

            renderedObjects.Clear();
        }
    }
}
