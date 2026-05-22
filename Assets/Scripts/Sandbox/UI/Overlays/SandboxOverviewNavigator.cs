using EvacLogix.Sandbox.Rendering;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Overlays
{
    public sealed class SandboxOverviewNavigator : MonoBehaviour
    {
        [SerializeField] private bool overviewEnabled = true;
        [SerializeField] private Rect worldBounds = new(-10f, -10f, 20f, 20f);

        private SandboxCameraController cameraController;

        public bool OverviewEnabled => overviewEnabled;
        public Rect WorldBounds => worldBounds;

        private void Awake()
        {
            cameraController = FindAnyObjectByType<SandboxCameraController>();
        }

        public void SetOverviewEnabled(bool enabled)
        {
            overviewEnabled = enabled;
        }

        public void SetWorldBounds(Rect bounds)
        {
            worldBounds = bounds;
        }

        public void FocusOnWorldPoint(Vector2 point)
        {
            if (!overviewEnabled || cameraController == null)
            {
                return;
            }

            var cameraTransform = cameraController.transform;
            cameraTransform.position = new Vector3(point.x, point.y, cameraTransform.position.z);
        }

        public void ResetView()
        {
            cameraController?.ResetView();
        }
    }
}
