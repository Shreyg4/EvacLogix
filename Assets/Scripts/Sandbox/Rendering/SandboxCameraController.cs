using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Authoring.Tools;
using UnityEngine;

namespace EvacLogix.Sandbox.Rendering
{
    [RequireComponent(typeof(Camera))]
    public sealed class SandboxCameraController : MonoBehaviour
    {
        [SerializeField] private float panSpeed = 0.01f;
        [SerializeField] private float zoomSpeed = 2f;
        [SerializeField] private float minOrthographicSize = 2f;
        [SerializeField] private float maxOrthographicSize = 40f;
        [SerializeField] private KeyCode resetViewKey = KeyCode.Home;

        private Camera controlledCamera;
        private SandboxInputRouter inputRouter;
        private SandboxToolStateService toolStateService;
        private Vector3 defaultPosition;
        private float defaultOrthographicSize;
        private Vector3 previousMousePosition;
        private bool wasPanningLastFrame;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            toolStateService = FindAnyObjectByType<SandboxToolStateService>();
            defaultPosition = transform.position;
            defaultOrthographicSize = controlledCamera.orthographic ? controlledCamera.orthographicSize : 5f;
            previousMousePosition = SandboxInputAdapter.PointerScreenPosition;
        }

        private void Update()
        {
            if (inputRouter != null && inputRouter.CurrentTarget == SandboxInputTarget.UI)
            {
                // Panning can't continue while the pointer is over UI; force a re-anchor when it returns
                // so dragging across a panel and back doesn't produce a jump.
                wasPanningLastFrame = false;
                UpdatePreviousPointer();
                return;
            }

            HandlePan();
            HandleZoom();
            HandleReset();
            UpdatePreviousPointer();
        }

        // Tracks the pointer for next frame's pan delta, but ignores frames with no valid pointer device
        // (their reported position is a bogus Vector2.zero that would corrupt the delta).
        private void UpdatePreviousPointer()
        {
            if (SandboxInputAdapter.HasPointer)
            {
                previousMousePosition = SandboxInputAdapter.PointerScreenPosition;
            }
        }

        public void ResetView()
        {
            transform.position = defaultPosition;
            if (controlledCamera.orthographic)
            {
                controlledCamera.orthographicSize = defaultOrthographicSize;
            }
        }

        public void FocusOnPoint(Vector2 point)
        {
            transform.position = new Vector3(point.x, point.y, transform.position.z);
        }

        // Recenters on world origin and restores the default zoom. Unlike ResetView (which targets the
        // camera's scene-start position), this always lands on literal (0,0) so it honors the
        // "Move to Origin" contract regardless of scene setup.
        public void MoveToOrigin()
        {
            FocusOnPoint(Vector2.zero);
            if (controlledCamera.orthographic)
            {
                controlledCamera.orthographicSize = defaultOrthographicSize;
            }
        }

        public void FrameBounds(Rect bounds, float padding = 1f)
        {
            if (!controlledCamera.orthographic)
            {
                FocusOnPoint(bounds.center);
                return;
            }

            var safeWidth = Mathf.Max(0.1f, bounds.width);
            var safeHeight = Mathf.Max(0.1f, bounds.height);
            var halfHeight = safeHeight * 0.5f + padding;
            var halfWidth = (safeWidth * 0.5f + padding) / Mathf.Max(0.01f, controlledCamera.aspect);

            FocusOnPoint(bounds.center);
            controlledCamera.orthographicSize = Mathf.Clamp(
                Mathf.Max(halfHeight, halfWidth),
                minOrthographicSize,
                maxOrthographicSize);
        }

        private void HandlePan()
        {
            var usingMiddleMousePan = SandboxInputAdapter.GetMouseButton(2);
            var usingRightMousePan = SandboxInputAdapter.IsRightMousePanActive;
            var usingPanToolPrimaryDrag =
                toolStateService != null &&
                toolStateService.CurrentToolMode == SandboxToolMode.Pan &&
                SandboxInputAdapter.GetMouseButton(0);

            if (!usingMiddleMousePan && !usingRightMousePan && !usingPanToolPrimaryDrag)
            {
                wasPanningLastFrame = false;
                return;
            }

            // No valid pointer this frame: don't pan from a bogus reading; resume cleanly next frame.
            if (!SandboxInputAdapter.HasPointer)
            {
                wasPanningLastFrame = false;
                return;
            }

            var current = (Vector3)SandboxInputAdapter.PointerScreenPosition;

            // First frame of a pan (or resuming after UI/lost pointer): re-anchor and don't move, so a
            // stale baseline can't jolt the camera.
            if (!wasPanningLastFrame)
            {
                previousMousePosition = current;
                wasPanningLastFrame = true;
                return;
            }

            var mouseDelta = current - previousMousePosition;

            // Clamp the per-frame delta so a single bad pointer read or frame hitch can't fling the view
            // across the scene (which left you unsure where you were).
            var maxDelta = Mathf.Max(Screen.width, Screen.height) * 0.25f;
            if (mouseDelta.sqrMagnitude > maxDelta * maxDelta)
            {
                previousMousePosition = current;
                return;
            }

            var delta = new Vector3(-mouseDelta.x, -mouseDelta.y, 0f);
            transform.position += delta * panSpeed * Mathf.Max(controlledCamera.orthographicSize, 1f);
        }

        private void HandleZoom()
        {
            var scroll = SandboxInputAdapter.MouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f) || !controlledCamera.orthographic)
            {
                return;
            }

            controlledCamera.orthographicSize = Mathf.Clamp(
                controlledCamera.orthographicSize - scroll * zoomSpeed,
                minOrthographicSize,
                maxOrthographicSize);
        }

        private void HandleReset()
        {
            if (SandboxInputAdapter.GetKeyDown(resetViewKey))
            {
                ResetView();
            }
        }
    }
}
