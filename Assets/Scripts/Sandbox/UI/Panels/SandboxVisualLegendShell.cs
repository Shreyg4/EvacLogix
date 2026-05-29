using System.Collections.Generic;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxVisualLegendShell : MonoBehaviour
    {
        [SerializeField] private List<SandboxVisualLegendEntry> legendEntries = new();

        private SandboxVisualOrganizationService visualOrganizationService;

        public IReadOnlyList<SandboxVisualLegendEntry> LegendEntries => legendEntries;
        public bool HasCompleteCoverage => SandboxObjectPresentationCatalog.HasCompleteLegendCoverage(legendEntries);

        private void Awake()
        {
            visualOrganizationService = FindAnyObjectByType<SandboxVisualOrganizationService>();
            if (visualOrganizationService != null)
            {
                visualOrganizationService.VisualStateChanged += HandleVisualStateChanged;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (visualOrganizationService != null)
            {
                visualOrganizationService.VisualStateChanged -= HandleVisualStateChanged;
            }
        }

        public void Refresh()
        {
            legendEntries = visualOrganizationService == null
                ? new List<SandboxVisualLegendEntry>()
                : new List<SandboxVisualLegendEntry>(visualOrganizationService.GetLegendEntries());
        }

        private void HandleVisualStateChanged()
        {
            Refresh();
        }
    }
}
