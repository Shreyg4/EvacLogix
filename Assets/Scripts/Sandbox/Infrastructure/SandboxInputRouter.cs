using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxInputTarget
    {
        None = 0,
        UI = 1,
        World = 2,
        Handle = 3,
        PreviewOverlay = 4,
    }

    public sealed class SandboxInputRouter : MonoBehaviour
    {
        [SerializeField] private bool prioritizeUiPointerTargets = true;
        [SerializeField] private SandboxInputTarget manualOverride = SandboxInputTarget.None;
        [SerializeField] private bool pointerOverHandle;
        [SerializeField] private bool previewOverlayCapturingInput;
        [SerializeField] private SandboxInputTarget currentTarget = SandboxInputTarget.World;
        [SerializeField] private Vector2 pointerScreenPosition;

        public event Action<SandboxInputTarget> InputTargetChanged;

        public SandboxInputTarget CurrentTarget => currentTarget;
        public Vector2 PointerScreenPosition => pointerScreenPosition;

        private void Update()
        {
            UpdatePointerTarget(Input.mousePosition);
        }

        public void SetManualOverride(SandboxInputTarget inputTarget)
        {
            manualOverride = inputTarget;
            UpdatePointerTarget(pointerScreenPosition);
        }

        public void SetPointerOverHandle(bool isOverHandle)
        {
            pointerOverHandle = isOverHandle;
            UpdatePointerTarget(pointerScreenPosition);
        }

        public void SetPreviewOverlayCapturingInput(bool isCapturingInput)
        {
            previewOverlayCapturingInput = isCapturingInput;
            UpdatePointerTarget(pointerScreenPosition);
        }

        public SandboxInputTarget ResolvePointerTarget(Vector2 screenPosition)
        {
            if (manualOverride != SandboxInputTarget.None)
            {
                return manualOverride;
            }

            if (previewOverlayCapturingInput)
            {
                return SandboxInputTarget.PreviewOverlay;
            }

            if (pointerOverHandle)
            {
                return SandboxInputTarget.Handle;
            }

            if (prioritizeUiPointerTargets && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return SandboxInputTarget.UI;
            }

            return SandboxInputTarget.World;
        }

        private void UpdatePointerTarget(Vector2 screenPosition)
        {
            pointerScreenPosition = screenPosition;
            var nextTarget = ResolvePointerTarget(screenPosition);
            if (nextTarget == currentTarget)
            {
                return;
            }

            currentTarget = nextTarget;
            InputTargetChanged?.Invoke(currentTarget);
        }
    }
}
