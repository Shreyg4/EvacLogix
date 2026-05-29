using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Runtime;
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
        private const string GridRootName = "GridRoot";
        private const string WallRootName = "WallRoot";
        private const string HandleRootName = "HandleRoot";
        private const string ColliderRootName = "ColliderRoot";
        private const string ValidationHighlightRootName = "ValidationHighlightRoot";
        private const string DiagnosticsOverlayRootName = "DiagnosticsOverlayRoot";
        private const string RoomOverlayRootName = "RoomOverlayRoot";

        [SerializeField] private string editorCameraName = "Main Camera";

        public void Install(SandboxApp app)
        {
            InstallServices();
            InstallUiShells();
            EnsureEditorCamera();
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log(
                "SandboxEditorInstaller completed for WebGL. " +
                $"SceneRole={app?.SceneRole ?? "Unknown"}, " +
                $"MainCameraPresent={Camera.main != null}");
#endif
        }

        private void InstallServices()
        {
            EnsureComponent<SandboxCommandHistory>(gameObject);
            EnsureComponent<SandboxToolStateService>(gameObject);
            EnsureComponent<SandboxSelectionService>(gameObject);
            EnsureComponent<SandboxInputRouter>(gameObject);
            EnsureComponent<SandboxWorkspaceStateService>(gameObject);
            EnsureComponent<SandboxDesktopFileActionBackend>(gameObject);
            EnsureComponent<SandboxWebGlFileActionBackend>(gameObject);
            EnsureComponent<SandboxWebGlBrowserFileBridgeAdapter>(gameObject);
            EnsureComponent<SandboxBrowserFileActionCoordinator>(gameObject);
            EnsureComponent<SandboxDesktopPreviewImageExportBackend>(gameObject);
            EnsureComponent<SandboxFileActionService>(gameObject);
            EnsureComponent<SandboxProjectRefreshService>(gameObject);
            EnsureComponent<SandboxSaveLoadService>(gameObject);
            EnsureComponent<SandboxProjectTransferService>(gameObject);
            EnsureComponent<SandboxProjectMetadataService>(gameObject);
            EnsureComponent<SandboxValidationService>(gameObject);
            EnsureComponent<SandboxRoomDetectionService>(gameObject);
            EnsureComponent<SandboxColliderRebuildService>(gameObject);
            EnsureComponent<SandboxFireSimulationService>(gameObject);
            EnsureComponent<SandboxProjectWorkspaceService>(gameObject);
            EnsureComponent<SandboxFloorManagementService>(gameObject);
            EnsureComponent<SandboxVisualOrganizationService>(gameObject);
            EnsureComponent<SandboxClipboardService>(gameObject);
            EnsureComponent<SandboxMeasurementService>(gameObject);
            EnsureComponent<SandboxEditorQoLService>(gameObject);
            EnsureComponent<SandboxKeyboardShortcutService>(gameObject);
            EnsureComponent<SandboxPreviewService>(gameObject);
            EnsureComponent<SandboxBlueprintImportService>(gameObject);
            EnsureComponent<SandboxScaleCalibrationService>(gameObject);
            EnsureComponent<SandboxCalibrationWorkflowService>(gameObject);
            EnsureComponent<SandboxPreviewImageExportService>(gameObject);
            EnsureComponent<SandboxWallSnappingService>(gameObject);
            EnsureComponent<SandboxWallAuthoringService>(gameObject);
            EnsureComponent<SandboxSemanticObjectAuthoringService>(gameObject);
            EnsureComponent<SandboxPreviewAuthoringService>(gameObject);
            EnsureComponent<SandboxScenarioManagementService>(gameObject);
            EnsureComponent<SandboxAgentSimulationService>(gameObject);

            var overlayRoot = FindRequiredRoot(OverlayRootName);
            var wallRoot = FindOrCreateNestedRoot(WorldRootName, WallRootName);
            var semanticRoot = FindOrCreateNestedRoot(WorldRootName, "SemanticRoot");
            FindOrCreateNestedRoot(WorldRootName, HandleRootName);
            FindOrCreateNestedRoot(WorldRootName, ColliderRootName);
            EnsureComponent<SandboxOverviewNavigator>(overlayRoot);
            EnsureComponent<SandboxOnboardingOverlayShell>(overlayRoot);
            EnsureComponent<SandboxCalibrationCaptureOverlay>(overlayRoot);
            EnsureComponent<SandboxWallAuthoringOverlay>(overlayRoot);
            EnsureComponent<SandboxObjectInteractionOverlay>(overlayRoot);
            EnsureComponent<SandboxSemanticObjectAuthoringOverlay>(overlayRoot);
            EnsureComponent<SandboxMeasurementOverlay>(overlayRoot);
            EnsureComponent<SandboxPreviewInteractionOverlay>(overlayRoot);
            EnsureComponent<SandboxBlueprintOverlayRenderer>(FindRequiredNestedRoot(WorldRootName, BlueprintRootName));
            EnsureComponent<SandboxGridOverlayRenderer>(FindRequiredNestedRoot(WorldRootName, GridRootName));
            EnsureComponent<SandboxWallOverlayRenderer>(wallRoot);
            EnsureComponent<SandboxSemanticObjectRenderer>(semanticRoot);
            EnsureComponent<SandboxValidationHighlightRenderer>(FindOrCreateNestedRoot(DebugRootName, ValidationHighlightRootName));
            EnsureComponent<SandboxDiagnosticsOverlayRenderer>(FindOrCreateNestedRoot(DebugRootName, DiagnosticsOverlayRootName));
            EnsureComponent<SandboxRoomOverlayRenderer>(FindOrCreateNestedRoot(DebugRootName, RoomOverlayRootName));
            EnsureComponent<SandboxPreviewDiagnosticsRenderer>(FindOrCreateNestedRoot(DebugRootName, "PreviewDiagnosticsRoot"));
        }

        private void InstallUiShells()
        {
            var uiRoot = FindRequiredRoot(UiRootName);
            EnsureComponent<SandboxTopBarShell>(FindRequiredNestedRoot(UiRootName, TopBarName));
            EnsureComponent<SandboxToolPaletteShell>(FindRequiredNestedRoot(UiRootName, LeftToolPanelName));
            EnsureComponent<SandboxInspectorPanelShell>(FindRequiredNestedRoot(UiRootName, RightInspectorPanelName));
            EnsureComponent<SandboxVisualLegendShell>(FindRequiredNestedRoot(UiRootName, RightInspectorPanelName));
            EnsureComponent<SandboxStatusBarShell>(FindRequiredNestedRoot(UiRootName, BottomStatusBarName));
            EnsureComponent<SandboxFloorTabsBarShell>(FindRequiredNestedRoot(UiRootName, FloorTabsBarName));
            EnsureComponent<SandboxValidationPanelShell>(FindRequiredNestedRoot(UiRootName, ValidationPanelRootName));
            EnsureComponent<SandboxNewProjectDialogShell>(FindRequiredNestedRoot(UiRootName, ModalRootName));
            EnsureComponent<SandboxEditorHud>(uiRoot);
            FindRequiredRoot(WorldRootName);
            FindRequiredRoot(DebugRootName);
        }

        private void EnsureEditorCamera()
        {
            var existingCamera = Camera.main;
            if (existingCamera != null)
            {
                EnsureCameraController(existingCamera.gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
                Debug.Log($"SandboxEditorInstaller using existing main camera: {existingCamera.name}");
#endif
                return;
            }

            var cameraObject = new GameObject(editorCameraName);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            cameraObject.AddComponent<Camera>().orthographic = true;
            cameraObject.AddComponent<AudioListener>();
            EnsureCameraController(cameraObject);
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.Log($"SandboxEditorInstaller created fallback main camera: {cameraObject.name}");
#endif
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

        private static GameObject FindOrCreateNestedRoot(string parentName, string childName)
        {
            var parent = FindRequiredRoot(parentName);
            var child = parent.transform.Find(childName);
            if (child != null)
            {
                return child.gameObject;
            }

            var childObject = new GameObject(childName);
            childObject.transform.SetParent(parent.transform, false);
            return childObject;
        }
    }
}
