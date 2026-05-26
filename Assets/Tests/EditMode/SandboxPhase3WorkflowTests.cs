using System.IO;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase3WorkflowTests
    {
        [Test]
        public void ProjectWorkspaceService_CreatesDefaultProjectWithOneFloorAndActiveFloor()
        {
            var host = new GameObject("WorkspaceHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();

            workspace.SendMessage("Awake");
            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(project.floors.Count, Is.EqualTo(1));
            Assert.That(workspace.ActiveFloor, Is.Not.Null);
            Assert.That(workspace.ActiveFloor.name, Is.EqualTo("Floor 1"));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void ProjectWorkspaceService_CreatesBlankProjectWithoutFloors()
        {
            var host = new GameObject("WorkspaceHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();

            workspace.SendMessage("Awake");
            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.BlankTemplate);

            Assert.That(project.floors.Count, Is.EqualTo(0));
            Assert.That(workspace.ActiveFloor, Is.Null);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void CalibrationService_UpdatesBlueprintScaleAndFeedback()
        {
            var host = new GameObject("WorkspaceHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();
            var calibrationService = host.AddComponent<SandboxScaleCalibrationService>();

            workspace.SendMessage("Awake");
            calibrationService.SendMessage("Awake");

            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var blueprintReference = new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetPath = "Assets/Art/Blueprints/Sandbox/example.png",
                sourceFileName = "example.png",
            };

            workspace.AddBlueprintReference(blueprintReference);
            workspace.AssignBlueprintToFloor(project.floors[0].floorId, blueprintReference.blueprintReferenceId);

            var didCalibrate = calibrationService.CalibrateFloorBlueprint(
                project.floors[0].floorId,
                new Vector2(0f, 0f),
                new Vector2(200f, 0f),
                20f);

            Assert.That(didCalibrate, Is.True);
            Assert.That(blueprintReference.isCalibrated, Is.True);
            Assert.That(blueprintReference.worldUnitsPerPixel, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(calibrationService.LatestMeasurementFeedback, Does.Contain("0.1"));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void CalibrationWorkflow_CapturesTwoPointsAndCompletesCalibration()
        {
            var host = new GameObject("CalibrationWorkflowHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();
            var calibrationService = host.AddComponent<SandboxScaleCalibrationService>();
            var workflowService = host.AddComponent<SandboxCalibrationWorkflowService>();

            workspace.SendMessage("Awake");
            calibrationService.SendMessage("Awake");
            workflowService.SendMessage("Awake");

            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var blueprintReference = new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetPath = "Assets/Art/Blueprints/Sandbox/example.png",
                sourceFileName = "example.png",
            };

            workspace.AddBlueprintReference(blueprintReference);
            workspace.AssignBlueprintToFloor(project.floors[0].floorId, blueprintReference.blueprintReferenceId);

            Assert.That(workflowService.BeginCalibrationForActiveFloor(), Is.True);
            Assert.That(workflowService.StatusPrompt, Is.EqualTo("Click calibration point A."));

            Assert.That(workflowService.RegisterCalibrationPoint(new Vector2(10f, 20f)), Is.True);
            Assert.That(workflowService.StatusPrompt, Is.EqualTo("Click calibration point B."));
            Assert.That(workflowService.HasPointA, Is.True);

            Assert.That(workflowService.RegisterCalibrationPoint(new Vector2(110f, 20f)), Is.True);
            Assert.That(workflowService.StatusPrompt, Is.EqualTo("Enter the real-world distance to finish calibration."));
            Assert.That(workflowService.HasPointB, Is.True);

            Assert.That(workflowService.TryCompleteCalibration(25f), Is.True);
            Assert.That(blueprintReference.isCalibrated, Is.True);
            Assert.That(blueprintReference.worldUnitsPerPixel, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(workflowService.StatusPrompt, Does.Contain("0.25"));
            Assert.That(workflowService.IsCalibrationCaptureActive, Is.False);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void CalibrationOverlay_KeepsVisualAidVisibleUntilCalibrationFinishes()
        {
            var host = new GameObject("CalibrationOverlayHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();
            var calibrationService = host.AddComponent<SandboxScaleCalibrationService>();
            var workflowService = host.AddComponent<SandboxCalibrationWorkflowService>();
            host.AddComponent<SandboxInputRouter>();

            workspace.SendMessage("Awake");
            calibrationService.SendMessage("Awake");
            workflowService.SendMessage("Awake");

            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var blueprintReference = new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetPath = "Assets/Art/Blueprints/Sandbox/example.png",
                sourceFileName = "example.png",
            };

            workspace.AddBlueprintReference(blueprintReference);
            workspace.AssignBlueprintToFloor(project.floors[0].floorId, blueprintReference.blueprintReferenceId);

            var overlayObject = new GameObject("CalibrationOverlay");
            var overlay = overlayObject.AddComponent<SandboxCalibrationCaptureOverlay>();
            overlay.SendMessage("Awake");

            Assert.That(workflowService.BeginCalibrationForActiveFloor(), Is.True);
            Assert.That(overlay.IsVisualAidVisible, Is.True);
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Click calibration point A."));

            Assert.That(workflowService.RegisterCalibrationPoint(new Vector2(5f, 10f)), Is.True);
            Assert.That(overlay.IsVisualAidVisible, Is.True);
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Click calibration point B."));

            Assert.That(workflowService.RegisterCalibrationPoint(new Vector2(25f, 10f)), Is.True);
            Assert.That(workflowService.IsCalibrationCaptureActive, Is.False);
            Assert.That(overlay.IsVisualAidVisible, Is.True);
            Assert.That(overlay.VisualAidInstruction, Is.EqualTo("Enter the real-world distance to finish calibration."));

            Assert.That(workflowService.TryCompleteCalibration(10f), Is.True);
            Assert.That(overlay.IsVisualAidVisible, Is.False);

            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void SaveLoadService_SavesAndReloadsBlueprintAssignment()
        {
            var host = new GameObject("SaveHost");
            var saveLoad = host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();

            workspace.SendMessage("Awake");
            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            project.metadata.buildingName = "Blueprint Save Test";

            var blueprintReference = new BlueprintReferenceData
            {
                blueprintReferenceId = "bp-test",
                sourceFileName = "test.png",
                assetPath = "Assets/Art/Blueprints/Sandbox/test.png",
                opacity = 0.6f,
                isVisible = false,
                isCalibrated = true,
                realWorldDistance = 30f,
                worldUnitsPerPixel = 0.2f
            };

            workspace.AddBlueprintReference(blueprintReference);
            workspace.AssignBlueprintToFloor(project.floors[0].floorId, blueprintReference.blueprintReferenceId);

            var savePath = Path.Combine(Path.GetTempPath(), $"evaclogix_phase3_{SandboxId.NewId()}.json");
            try
            {
                var didSave = saveLoad.SaveActiveProjectToPath(savePath);
                Assert.That(didSave, Is.True);

                var loadedProject = saveLoad.LoadProjectFromPath(savePath);
                Assert.That(loadedProject, Is.Not.Null);
                Assert.That(loadedProject.metadata.buildingName, Is.EqualTo("Blueprint Save Test"));
                Assert.That(loadedProject.floors[0].blueprintReferenceId, Is.EqualTo("bp-test"));
                Assert.That(loadedProject.blueprintReferences[0].opacity, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(loadedProject.blueprintReferences[0].isVisible, Is.False);
                Assert.That(loadedProject.blueprintReferences[0].isCalibrated, Is.True);
                Assert.That(workspace.ActiveProject.metadata.buildingName, Is.EqualTo("Blueprint Save Test"));
            }
            finally
            {
                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }
            }

            Object.DestroyImmediate(host);
        }

        [Test]
        public void NewProjectDialog_CreatesDefaultProjectAndShowsOnboarding()
        {
            var host = new GameObject("WorkspaceHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();
            workspace.SendMessage("Awake");

            var overlayObject = new GameObject("Overlay");
            var onboarding = overlayObject.AddComponent<SandboxOnboardingOverlayShell>();

            var statusBarObject = new GameObject("StatusBar");
            var statusBar = statusBarObject.AddComponent<SandboxStatusBarShell>();

            var dialogObject = new GameObject("Dialog");
            var dialog = dialogObject.AddComponent<SandboxNewProjectDialogShell>();
            dialog.SendMessage("Awake");
            dialog.CreateDefaultProject();

            Assert.That(workspace.ActiveProject, Is.Not.Null);
            Assert.That(workspace.ActiveProject.floors.Count, Is.EqualTo(1));
            Assert.That(onboarding.IsVisible, Is.True);
            Assert.That(onboarding.OnboardingSteps.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(statusBar.StatusMessage, Is.EqualTo("Created default sandbox project."));
            Assert.That(dialog.IsOpen, Is.False);

            Object.DestroyImmediate(dialogObject);
            Object.DestroyImmediate(statusBarObject);
            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void InspectorPanelShell_UsesExplicitCalibrationWorkflow()
        {
            var servicesHost = new GameObject("ServicesHost");
            servicesHost.AddComponent<SandboxSaveLoadService>();
            var workspace = servicesHost.AddComponent<SandboxProjectWorkspaceService>();
            var calibrationService = servicesHost.AddComponent<SandboxScaleCalibrationService>();
            servicesHost.AddComponent<SandboxCalibrationWorkflowService>();

            workspace.SendMessage("Awake");
            calibrationService.SendMessage("Awake");
            servicesHost.GetComponent<SandboxCalibrationWorkflowService>().SendMessage("Awake");

            var project = workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            var blueprintReference = new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetPath = "Assets/Art/Blueprints/Sandbox/example.png",
                sourceFileName = "example.png",
            };

            workspace.AddBlueprintReference(blueprintReference);
            workspace.AssignBlueprintToFloor(project.floors[0].floorId, blueprintReference.blueprintReferenceId);

            var statusBarObject = new GameObject("StatusBar");
            var statusBar = statusBarObject.AddComponent<SandboxStatusBarShell>();

            var inspectorObject = new GameObject("Inspector");
            var inspector = inspectorObject.AddComponent<SandboxInspectorPanelShell>();
            inspector.SendMessage("Awake");

            Assert.That(inspector.BeginActiveFloorCalibrationCapture(), Is.True);
            Assert.That(statusBar.StatusMessage, Is.EqualTo("Click calibration point A."));

            Assert.That(inspector.RegisterCalibrationPoint(new Vector2(0f, 0f)), Is.True);
            Assert.That(statusBar.StatusMessage, Is.EqualTo("Click calibration point B."));

            Assert.That(inspector.RegisterCalibrationPoint(new Vector2(50f, 0f)), Is.True);
            Assert.That(statusBar.StatusMessage, Is.EqualTo("Enter the real-world distance to finish calibration."));

            Assert.That(inspector.CompleteActiveFloorCalibration(10f), Is.True);
            Assert.That(inspector.LatestCalibrationFeedback, Does.Contain("0.2"));
            Assert.That(statusBar.StatusMessage, Does.Contain("0.2"));

            Object.DestroyImmediate(inspectorObject);
            Object.DestroyImmediate(statusBarObject);
            Object.DestroyImmediate(servicesHost);
        }

        [Test]
        public void FloorTabsBarShell_ReflectsCreatedProjectFloorNames()
        {
            var host = new GameObject("WorkspaceHost");
            host.AddComponent<SandboxSaveLoadService>();
            var workspace = host.AddComponent<SandboxProjectWorkspaceService>();
            workspace.SendMessage("Awake");

            var floorTabsObject = new GameObject("FloorTabs");
            var floorTabs = floorTabsObject.AddComponent<SandboxFloorTabsBarShell>();
            floorTabs.SendMessage("Awake");

            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(floorTabs.PlaceholderFloorNames.Count, Is.EqualTo(1));
            Assert.That(floorTabs.PlaceholderFloorNames[0], Is.EqualTo("Floor 1"));

            Object.DestroyImmediate(floorTabsObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void BlueprintImportService_ValidatesSupportedFileExtensions()
        {
            var host = new GameObject("ImportHost");
            var service = host.AddComponent<SandboxBlueprintImportService>();

            var pngPath = Path.Combine(Path.GetTempPath(), $"blueprint_{SandboxId.NewId()}.png");
            var txtPath = Path.Combine(Path.GetTempPath(), $"blueprint_{SandboxId.NewId()}.txt");

            try
            {
                File.WriteAllBytes(pngPath, new byte[] { 137, 80, 78, 71 });
                File.WriteAllText(txtPath, "not an image");

                Assert.That(service.CanImport(pngPath), Is.True);
                Assert.That(service.CanImport(txtPath), Is.False);
            }
            finally
            {
                if (File.Exists(pngPath))
                {
                    File.Delete(pngPath);
                }

                if (File.Exists(txtPath))
                {
                    File.Delete(txtPath);
                }
            }

            Object.DestroyImmediate(host);
        }
    }
}
