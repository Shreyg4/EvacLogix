using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxMeasurementOverlay : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.05f, 0.08f, 0.12f, 1f);
        private static readonly Color AccentColor = new(0.33f, 0.88f, 0.59f, 0.95f);
        private static readonly Color SecondaryAccentColor = new(0.99f, 0.84f, 0.28f, 0.95f);
        private static readonly Color TextColor = new(0.94f, 0.97f, 1f, 1f);
        private static readonly Color MutedTextColor = new(0.72f, 0.82f, 0.9f, 1f);

        private Texture2D solidTexture;
        private Font overlayFont;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;
        private GUIStyle buttonStyle;
        private Rect panelRect;
        private SandboxToolStateService toolStateService;
        private SandboxMeasurementService measurementService;
        private SandboxInputRouter inputRouter;
        private SandboxStatusBarShell statusBar;

        public bool IsVisualAidVisible => toolStateService != null && toolStateService.CurrentToolMode == SandboxToolMode.Measure;
        public string VisualAidInstruction
        {
            get
            {
                if (!IsVisualAidVisible || measurementService == null)
                {
                    return string.Empty;
                }

                if (!measurementService.HasPointA)
                {
                    return "Click measurement point A.";
                }

                if (!measurementService.HasPointB)
                {
                    return "Click measurement point B.";
                }

                return "Measurement captured. Click again to update point B, or use Clear Measure to restart.";
            }
        }

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Update()
        {
            EnsureDependencies();
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

            var guiPoint = new Vector2(SandboxInputAdapter.PointerScreenPosition.x, Screen.height - SandboxInputAdapter.PointerScreenPosition.y);
            if (panelRect.Contains(guiPoint))
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

        private void OnGUI()
        {
            EnsureDependencies();
            EnsureGuiResources();
            if (!IsVisualAidVisible)
            {
                return;
            }

            DrawMeasurementPanel();
            DrawMeasurementGuide();
        }

        private void EnsureDependencies()
        {
            if (toolStateService == null)
            {
                toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            }

            if (measurementService == null)
            {
                measurementService = FindAnyObjectByType<SandboxMeasurementService>();
            }

            if (inputRouter == null)
            {
                inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            }

            if (statusBar == null)
            {
                statusBar = FindAnyObjectByType<SandboxStatusBarShell>();
            }
        }

        private void EnsureGuiResources()
        {
            if (solidTexture == null)
            {
                solidTexture = Texture2D.whiteTexture;
            }

            if (overlayFont == null)
            {
                overlayFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (panelStyle == null)
            {
                panelStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(16, 16, 14, 14),
                    alignment = TextAnchor.UpperLeft
                };
            }

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    font = overlayFont,
                    normal = { textColor = TextColor }
                };
            }

            if (bodyStyle == null)
            {
                bodyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    font = overlayFont,
                    wordWrap = true,
                    normal = { textColor = MutedTextColor }
                };
            }

            if (badgeStyle == null)
            {
                badgeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    font = overlayFont,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.06f, 0.1f, 0.16f, 1f) }
                };
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    font = overlayFont,
                    fontSize = 12
                };
            }
        }

        private void DrawMeasurementPanel()
        {
            var panelWidth = Mathf.Min(Screen.width - 32f, 360f);
            panelRect = new Rect(
                Mathf.Max(16f, (Screen.width - panelWidth) * 0.5f),
                Mathf.Max(96f, Screen.height - 196f),
                panelWidth,
                126f);
            DrawFilledRect(panelRect, BackdropColor);
            GUI.Box(panelRect, GUIContent.none, panelStyle);

            var contentRect = new Rect(panelRect.x + 16f, panelRect.y + 14f, panelRect.width - 32f, panelRect.height - 28f);
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 22f), "Measure Guide", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 40f), VisualAidInstruction, bodyStyle);

            var readout = string.IsNullOrWhiteSpace(measurementService?.LastDistanceReadout)
                ? "Distance updates in the status bar and guidance panel."
                : measurementService.LastDistanceReadout;
            GUI.Label(new Rect(contentRect.x, contentRect.y + 66f, contentRect.width - 118f, 34f), readout, bodyStyle);

            var clearButtonRect = new Rect(contentRect.x + contentRect.width - 110f, contentRect.y + 70f, 110f, 28f);
            var previousEnabled = GUI.enabled;
            GUI.enabled = measurementService != null && (measurementService.HasPointA || measurementService.HasPointB);
            if (GUI.Button(clearButtonRect, "Clear Measure", buttonStyle))
            {
                measurementService?.ClearMeasurement();
                if (statusBar != null)
                {
                    statusBar.StatusMessage = "Cleared measurement points.";
                }
            }

            GUI.enabled = previousEnabled;
        }

        private void DrawMeasurementGuide()
        {
            if (measurementService == null || !measurementService.HasPointA)
            {
                return;
            }

            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return;
            }

            var startScreenPoint = WorldToGuiPoint(cameraComponent, measurementService.PointA);
            var endScreenPoint = measurementService.HasPointB
                ? WorldToGuiPoint(cameraComponent, measurementService.PointB)
                : new Vector2(SandboxInputAdapter.PointerScreenPosition.x, Screen.height - SandboxInputAdapter.PointerScreenPosition.y);

            DrawGuideLine(startScreenPoint, endScreenPoint, measurementService.HasPointB ? AccentColor : SecondaryAccentColor, 4f);
            DrawMarker(startScreenPoint, "A", AccentColor);
            DrawMarker(endScreenPoint, measurementService.HasPointB ? "B" : "+", measurementService.HasPointB ? SecondaryAccentColor : AccentColor);
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

        private static Vector2 WorldToGuiPoint(Camera cameraComponent, Vector2 worldPoint)
        {
            var screenPoint = cameraComponent.WorldToScreenPoint(new Vector3(worldPoint.x, worldPoint.y, 0f));
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        private void DrawGuideLine(Vector2 startPoint, Vector2 endPoint, Color color, float thickness)
        {
            var delta = endPoint - startPoint;
            var length = delta.magnitude;
            if (length <= Mathf.Epsilon)
            {
                return;
            }

            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;

            GUIUtility.RotateAroundPivot(angle, startPoint);
            GUI.color = color;
            GUI.DrawTexture(new Rect(startPoint.x, startPoint.y - (thickness * 0.5f), length, thickness), solidTexture);
            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void DrawMarker(Vector2 center, string label, Color color)
        {
            var markerRect = new Rect(center.x - 14f, center.y - 14f, 28f, 28f);
            DrawFilledRect(markerRect, color);
            GUI.Label(markerRect, label, badgeStyle);
        }

        private void DrawFilledRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, solidTexture);
            GUI.color = previousColor;
        }
    }
}
