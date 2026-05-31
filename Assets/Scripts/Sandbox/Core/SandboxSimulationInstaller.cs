using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Runtime.Simulation;
using UnityEngine;

namespace EvacLogix.Sandbox.Core
{
    // Bootstraps the dedicated Simulation scene. Unlike the editor installer it needs no pre-authored
    // scene roots: it adds the minimal runtime services + simulation components onto its own
    // GameObject (so sibling GetComponent wiring resolves) and ensures an orthographic camera. The
    // SandboxSimulationController then consumes the launched project and runs the show.
    public sealed class SandboxSimulationInstaller : MonoBehaviour, ISandboxBootstrapInstaller
    {
        public void Install(SandboxApp app)
        {
            // Project/workspace plumbing (collider rebuild reads the active project + grid size).
            EnsureComponent<SandboxWorkspaceStateService>();
            EnsureComponent<SandboxSaveLoadService>();
            EnsureComponent<SandboxProjectWorkspaceService>();
            EnsureComponent<SandboxColliderRebuildService>();
            EnsureComponent<SandboxFireSimulationService>();

            // Simulation services.
            EnsureComponent<SandboxFloorLayoutService>();
            EnsureComponent<SandboxSimulationAgentService>();
            EnsureComponent<SandboxSimulationRenderer>();
            EnsureComponent<SandboxSimulationController>();

            EnsureCamera();
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
            {
                Camera.main.orthographic = true;
                return;
            }

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.backgroundColor = new Color(0.06f, 0.08f, 0.11f, 1f);
            cameraObject.AddComponent<AudioListener>();
        }

        private T EnsureComponent<T>() where T : Component
        {
            var existing = GetComponent<T>();
            return existing != null ? existing : gameObject.AddComponent<T>();
        }
    }
}
