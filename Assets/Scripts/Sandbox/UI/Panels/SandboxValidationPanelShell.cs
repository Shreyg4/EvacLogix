using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxValidationPanelShell : MonoBehaviour
    {
        [SerializeField] private bool startCollapsed;

        public bool StartCollapsed => startCollapsed;
    }
}
