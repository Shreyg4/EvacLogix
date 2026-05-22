using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using EvacLogix.Sandbox.UI.Shortcuts;
using UnityEngine;

namespace EvacLogix.Sandbox.Core
{
    public sealed class SandboxEditorInstaller : MonoBehaviour, ISandboxBootstrapInstaller
    {
        private const string WorldRootName = "World";
        private const string UiRootName = "UI";
        private const string OverlayRootName = "OverlayRoot";
        private const string DebugRootName = "DebugRoot";
        private const string TopBarName = "TopBar";
        private const string LeftToolPanelName = "LeftToolPanel";
        private const string RightInspectorPanelName = "RightInspectorPanel";
        private const string BottomStatusBarName = "BottomStatusBar";
        private const string FloorTabsBarName = "FloorTabsBar";
        private const string ValidationPanelRootName = "ValidationPanelRoot";
        private const string ModalRootName = "ModalRoot";
        private const string BlueprintRootName = "BlueprintRoot";

        [SerializeField] private string editorCameraName = "Main Camera";

        public void Install(SandboxApp app)
        {
            InstallServices();
            InstallUiShells();
            EnsureEditorCamera();
        }

        private void InstallServices()
        {
            EnsureComponent<SandboxCommandHistory>(gameObject);
            EnsureComponent<SandboxToolStateService>(gameObject);
            EnsureComponent<SandboxSelectionService>(gameObject);
            EnsureComponent<SandboxInputRouter>(gameObject);
            EnsureComponent<SandboxWorkspaceStateService>(gameObject);
            EnsureComponent<SandboxKeyboardShortcutService>(gameObject);
            EnsureComponent<SandboxSaveLoadService>(gameObject);
            EnsureComponent<SandboxValidationService>(gameObject);
            EnsureComponent<SandboxColliderRebuildService>(gameObject);
            EnsureComponent<SandboxProjectWorkspaceService>(gameObject);
            EnsureComponent<SandboxBlueprintImportService>(gameObject);
            EnsureComponent<SandboxScaleCalibrationService>(gameObject);
            EnsureComponent<SandboxCalibrationWorkflowService>(gameObject);
            EnsureComponent<SandboxPreviewImageExportService>(gameObject);

            var overlayRoot = FindRequiredRoot(OverlayRootName);
            EnsureComponent<SandboxOverviewNavigator>(overlayRoot);
            EnsureComponent<SandboxOnboardingOverlayShell>(overlayRoot);
            EnsureComponent<SandboxCalibrationCaptureOverlay>(overlayRoot);
            EnsureComponent<SandboxBlueprintOverlayRenderer>(FindRequiredNestedRoot(WorldRootName, BlueprintRootName));
        }

        private void InstallUiShells()
        {
            EnsureComponent<SandboxTopBarShell>(FindRequiredNestedRoot(UiRootName, TopBarName));
            EnsureComponent<SandboxToolPaletteShell>(FindRequiredNestedRoot(UiRootName, LeftToolPanelName));
            EnsureComponent<SandboxInspectorPanelShell>(FindRequiredNestedRoot(UiRootName, RightInspectorPanelName));
            EnsureComponent<SandboxStatusBarShell>(FindRequiredNestedRoot(UiRootName, BottomStatusBarName));
            EnsureComponent<SandboxFloorTabsBarShell>(FindRequiredNestedRoot(UiRootName, FloorTabsBarName));
            EnsureComponent<SandboxValidationPanelShell>(FindRequiredNestedRoot(UiRootName, ValidationPanelRootName));
            EnsureComponent<SandboxNewProjectDialogShell>(FindRequiredNestedRoot(UiRootName, ModalRootName));
            FindRequiredRoot(WorldRootName);
            FindRequiredRoot(DebugRootName);
        }

        private void EnsureEditorCamera()
        {
            var existingCamera = Camera.main;
            if (existingCamera != null)
            {
                EnsureCameraController(existingCamera.gameObject);
                return;
            }

            var cameraObject = new GameObject(editorCameraName);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            cameraObject.AddComponent<Camera>().orthographic = true;
            cameraObject.AddComponent<AudioListener>();
            EnsureCameraController(cameraObject);
        }

        private void EnsureCameraController(GameObject cameraObject)
        {
            EnsureComponent<SandboxCameraController>(cameraObject);
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var existingComponent = target.GetComponent<T>();
            return existingComponent != null ? existingComponent : target.AddComponent<T>();
        }

        private static GameObject FindRequiredRoot(string rootName)
        {
            var root = GameObject.Find(rootName);
            if (root == null)
            {
                throw new MissingReferenceException($"Sandbox editor root '{rootName}' was not found.");
            }

            return root;
        }

        private static GameObject FindRequiredNestedRoot(string parentName, string childName)
        {
            var parent = FindRequiredRoot(parentName);
            var child = parent.transform.Find(childName);
            if (child == null)
            {
                throw new MissingReferenceException($"Sandbox editor child root '{childName}' was not found under '{parentName}'.");
            }

            return child.gameObject;
        }
    }
}
