using EvacLogix.Sandbox.Infrastructure;
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
        private Vector3 defaultPosition;
        private float defaultOrthographicSize;
        private Vector3 previousMousePosition;

        private void Awake()
        {
            controlledCamera = GetComponent<Camera>();
            inputRouter = FindAnyObjectByType<SandboxInputRouter>();
            defaultPosition = transform.position;
            defaultOrthographicSize = controlledCamera.orthographic ? controlledCamera.orthographicSize : 5f;
            previousMousePosition = Input.mousePosition;
        }

        private void Update()
        {
            if (inputRouter != null && inputRouter.CurrentTarget == SandboxInputTarget.UI)
            {
                previousMousePosition = Input.mousePosition;
                return;
            }

            HandlePan();
            HandleZoom();
            HandleReset();
            previousMousePosition = Input.mousePosition;
        }

        public void ResetView()
        {
            transform.position = defaultPosition;
            if (controlledCamera.orthographic)
            {
                controlledCamera.orthographicSize = defaultOrthographicSize;
            }
        }

        private void HandlePan()
        {
            if (!Input.GetMouseButton(2))
            {
                return;
            }

            var mouseDelta = Input.mousePosition - previousMousePosition;
            var delta = new Vector3(-mouseDelta.x, -mouseDelta.y, 0f);
            transform.position += delta * panSpeed * Mathf.Max(controlledCamera.orthographicSize, 1f);
        }

        private void HandleZoom()
        {
            var scroll = Input.mouseScrollDelta.y;
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
            if (Input.GetKeyDown(resetViewKey))
            {
                ResetView();
            }
        }
    }
}
