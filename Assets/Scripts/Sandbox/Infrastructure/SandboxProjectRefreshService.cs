using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxProjectRefreshService : MonoBehaviour
    {
        private SandboxColliderRebuildService colliderRebuildService;
        private SandboxValidationService validationService;

        private void Awake()
        {
            colliderRebuildService = GetComponent<SandboxColliderRebuildService>();
            validationService = GetComponent<SandboxValidationService>();
        }

        public void RefreshDerivedProjectState()
        {
            colliderRebuildService?.RebuildAll();
            validationService?.ValidateActiveProject();
        }

        public void RefreshValidationOnly()
        {
            validationService?.ValidateActiveProject();
        }
    }
}
