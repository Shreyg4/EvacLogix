using UnityEngine;

namespace EvacLogix.Sandbox.Core
{
    public sealed class SandboxApp : MonoBehaviour
    {
        [SerializeField] private string sceneRole = "Editor";
        [SerializeField] private bool logOnAwake = true;

        public string SceneRole => sceneRole;

        private void Awake()
        {
            InstallBootstrapServices();

            if (!logOnAwake)
            {
                return;
            }

            Debug.Log($"EvacLogix sandbox initialized for {sceneRole}.");
        }

        private void InstallBootstrapServices()
        {
            var behaviours = GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i += 1)
            {
                if (behaviours[i] is not ISandboxBootstrapInstaller installer)
                {
                    continue;
                }

                installer.Install(this);
            }
        }
    }
}
