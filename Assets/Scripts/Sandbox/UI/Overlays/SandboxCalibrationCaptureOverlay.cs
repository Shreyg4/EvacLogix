using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxCalibrationCaptureOverlay : MonoBehaviour
    {
        private static readonly Color BackdropColor = new(0.05f, 0.08f, 0.12f, 1f);
        private static readonly Color AccentColor = new(0.21f, 0.78f, 0.96f, 0.95f);
        private static readonly Color SecondaryAccentColor = new(1f, 0.76f, 0.25f, 0.95f);
        private static readonly Color TextColor = new(0.94f, 0.97f, 1f, 1f);
        private static readonly Color MutedTextColor = new(0.72f, 0.82f, 0.9f, 1f);

        private Texture2D solidTexture;
        private Font overlayFont;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;
        private SandboxCalibrationWorkflowService calibrationWorkflowService;
        private SandboxInputRouter inputRouter;

        public bool IsVisualAidVisible => calibrationWorkflowService != null && calibrationWorkflowService.HasPendingCalibration;
        public string VisualAidInstruction => calibrationWorkflowService?.StatusPrompt ?? string.Empty;

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Update()
        {
            EnsureDependencies();
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

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
            {
                return;
            }

            EnsureDependencies();
            EnsureGuiResources();
            if (!IsVisualAidVisible)
            {
                return;
            }

            DrawCalibrationPanel();
            DrawCalibrationGuide();
        }

        private void OnDisable()
        {
            inputRouter?.SetPreviewOverlayCapturingInput(false);
        }

        private void OnDestroy()
        {
            inputRouter?.SetPreviewOverlayCapturingInput(false);
        }

        private void EnsureDependencies()
        {
            if (calibrationWorkflowService == null)
            {
                calibrationWorkflowService = FindAnyObjectByType<SandboxCalibrationWorkflowService>();
            }

            if (inputRouter == null)
            {
                inputRouter = FindAnyObjectByType<SandboxInputRouter>();
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
        }

        private void DrawCalibrationPanel()
        {
            var panelRect = new Rect(16f, 16f, Mathf.Min(Screen.width - 32f, 360f), 112f);
            DrawFilledRect(panelRect, BackdropColor);
            GUI.Box(panelRect, GUIContent.none, panelStyle);

            var contentRect = new Rect(panelRect.x + 16f, panelRect.y + 14f, panelRect.width - 32f, panelRect.height - 28f);
            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 22f), "Calibration Guide", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 40f), VisualAidInstruction, bodyStyle);
            GUI.Label(
                new Rect(contentRect.x, contentRect.y + 66f, contentRect.width, 22f),
                calibrationWorkflowService != null && calibrationWorkflowService.HasPointB
                    ? "Use the inspector distance field to finish."
                    : "Pick two known reference points on the blueprint.",
                bodyStyle);
        }

        private void DrawCalibrationGuide()
        {
            if (calibrationWorkflowService == null || !calibrationWorkflowService.HasPointA)
            {
                return;
            }

            var cameraComponent = Camera.main;
            if (cameraComponent == null)
            {
                return;
            }

            var startScreenPoint = WorldToGuiPoint(cameraComponent, calibrationWorkflowService.PointA);
            var endScreenPoint = calibrationWorkflowService.HasPointB
                ? WorldToGuiPoint(cameraComponent, calibrationWorkflowService.PointB)
                : new Vector2(SandboxInputAdapter.PointerScreenPosition.x, Screen.height - SandboxInputAdapter.PointerScreenPosition.y);

            DrawGuideLine(startScreenPoint, endScreenPoint, calibrationWorkflowService.HasPointB ? AccentColor : SecondaryAccentColor, 4f);
            DrawMarker(startScreenPoint, "A", AccentColor);
            DrawMarker(endScreenPoint, calibrationWorkflowService.HasPointB ? "B" : "+", calibrationWorkflowService.HasPointB ? SecondaryAccentColor : AccentColor);
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
