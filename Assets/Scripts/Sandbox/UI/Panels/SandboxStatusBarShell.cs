using UnityEngine;

namespace EvacLogix.Sandbox.UI.Panels
{
    public sealed class SandboxStatusBarShell : MonoBehaviour
    {
        [SerializeField] private string statusMessage = "Ready";

        public string StatusMessage
        {
            get => statusMessage;
            set => statusMessage = value;
        }
    }
}
