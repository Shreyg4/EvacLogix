using System;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase9PersistenceTests
    {
        [Test]
        public void SaveLoadService_KeepsManualSaveAndAutosaveDistinctAndRestoresRecovery()
        {
            var autosaveRoot = CreateTempDirectory("phase9-autosave");
            var manualSavePath = Path.Combine(CreateTempDirectory("phase9-manual"), "project.json");

            var host = CreatePhase9Host(
                autosaveRoot,
                out var workspaceService,
                out var saveLoadService,
                out _,
                out _,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            workspaceService.ActiveProject.metadata.buildingName = "Manual Save";
            Assert.That(saveLoadService.SaveActiveProjectToPath(manualSavePath), Is.True);

            var savedProject = SandboxProjectSerializer.Deserialize(File.ReadAllText(manualSavePath));
            Assert.That(savedProject.metadata.lastManualSaveUtc, Is.Not.Empty);

            var recoveredProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            recoveredProject.metadata.buildingName = "Recovered Draft";
            workspaceService.SetActiveProject(recoveredProject);
            Assert.That(saveLoadService.ForceAutosaveNow(), Is.True);
            Assert.That(saveLoadService.LastAutosavePath, Is.Not.EqualTo(manualSavePath));
            Assert.That(File.Exists(saveLoadService.LastAutosavePath), Is.True);

            var recoveryHost = CreatePhase9Host(
                autosaveRoot,
                out var recoveryWorkspaceService,
                out var recoverySaveLoadService,
                out _,
                out _,
                out _,
                out _,
                out _);

            Assert.That(recoverySaveLoadService.HasRecoveryPrompt, Is.True);
            Assert.That(recoverySaveLoadService.RecoveryPromptMessage, Does.Contain("Recovery autosave available"));
            Assert.That(recoverySaveLoadService.TryRestoreRecovery(), Is.True);
            Assert.That(recoveryWorkspaceService.ActiveProject.metadata.buildingName, Is.EqualTo("Recovered Draft"));
            Assert.That(recoverySaveLoadService.HasUnsavedChanges, Is.True);

            Object.DestroyImmediate(recoveryHost);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void ProjectTransferService_RoundTripsFullJsonAndExportsRuntimeData()
        {
            var autosaveRoot = CreateTempDirectory("phase9-roundtrip");
            var exportPath = Path.Combine(CreateTempDirectory("phase9-export"), "portable-project.json");
            var runtimeExportPath = Path.Combine(CreateTempDirectory("phase9-runtime"), "runtime-project.json");

            var host = CreatePhase9Host(
                autosaveRoot,
                out var workspaceService,
                out _,
                out var transferService,
                out var validationService,
                out _,
                out _,
                out _);

            var statusBarObject = new GameObject("StatusBar");
            var statusBar = statusBarObject.AddComponent<SandboxStatusBarShell>();
            statusBar.SendMessage("Awake");

            var topBarObject = new GameObject("TopBar");
            var topBar = topBarObject.AddComponent<SandboxTopBarShell>();
            topBar.SendMessage("Awake");

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            workspaceService.ActiveProject.metadata.buildingName = "Portable Export";
            validationService.ReplaceIssues(Array.Empty<ValidationIssueData>());

            Assert.That(transferService.ExportProjectJson(exportPath), Is.True);
            Assert.That(File.Exists(exportPath), Is.True);

            workspaceService.SetActiveProject(SandboxProjectFactory.Create(SandboxProjectTemplateKind.BlankTemplate));
            Assert.That(transferService.ImportProjectJson(exportPath), Is.Not.Null);
            Assert.That(workspaceService.ActiveProject.metadata.buildingName, Is.EqualTo("Portable Export"));

            Assert.That(topBar.ExportRuntimeProjectData(runtimeExportPath), Is.True);
            Assert.That(File.Exists(runtimeExportPath), Is.True);
            Assert.That(workspaceService.ActiveProject.metadata.lastRuntimeExportUtc, Is.Not.Empty);
            Assert.That(topBar.LifecycleStateLabel, Is.EqualTo("Ready for Export"));
            Assert.That(statusBar.LifecycleStateLabel, Is.EqualTo("Ready for Export"));

            var unsupportedPath = Path.Combine(CreateTempDirectory("phase9-future"), "future.json");
            File.WriteAllText(unsupportedPath, "{\"schemaVersion\":999,\"projectId\":\"future-project\"}");
            Assert.That(transferService.ImportProjectJson(unsupportedPath), Is.Null);
            Assert.That(transferService.LastError, Does.Contain("Unsupported sandbox schema version"));

            Object.DestroyImmediate(topBarObject);
            Object.DestroyImmediate(statusBarObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void FloorImport_RejectsConflictsAndAcceptsSafeImports()
        {
            var autosaveRoot = CreateTempDirectory("phase9-floor-import");
            var conflictPath = Path.Combine(CreateTempDirectory("phase9-conflict"), "conflict.json");
            var safePath = Path.Combine(CreateTempDirectory("phase9-safe"), "safe.json");

            var host = CreatePhase9Host(
                autosaveRoot,
                out var workspaceService,
                out _,
                out var transferService,
                out var validationService,
                out _,
                out _,
                out _);

            workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            validationService.ReplaceIssues(Array.Empty<ValidationIssueData>());
            workspaceService.SetActiveProject(workspaceService.ActiveProject);

            var conflictingSource = new BuildingProjectData
            {
                metadata = new ProjectMetadataData(),
                floors =
                {
                    new FloorData
                    {
                        floorId = "import-floor-duplicate-name",
                        name = "Floor 1",
                        order = 0,
                        stairPortals =
                        {
                            new StairPortalData
                            {
                                stairPortalId = "stair-a",
                                sourceFloorId = "import-floor-duplicate-name",
                                targetFloorId = "external-floor",
                                targetStairPortalId = "external-stair"
                            }
                        }
                    },
                    new FloorData
                    {
                        floorId = "import-floor-duplicate-name-2",
                        name = "Imported Twin",
                        order = 1
                    },
                    new FloorData
                    {
                        floorId = "import-floor-duplicate-name-3",
                        name = "Imported Twin",
                        order = 2
                    }
                },
                spawnLayouts =
                {
                    new SpawnLayoutData
                    {
                        spawnLayoutId = "spawn-layout-import",
                        spawnPoints =
                        {
                            new SpawnPointData
                            {
                                spawnPointId = "spawn-a",
                                floorId = "import-floor-duplicate-name",
                                position = new Vector2(1f, 1f)
                            }
                        }
                    }
                }
            };
            SandboxProjectDataUtility.EnsureIds(conflictingSource);
            File.WriteAllText(conflictPath, SandboxProjectSerializer.Serialize(conflictingSource));

            var conflictAnalysis = transferService.AnalyzeFloorImportFromPath(conflictPath);
            Assert.That(conflictAnalysis.CanImport, Is.False);
            Assert.That(conflictAnalysis.conflicts.Any(conflict => conflict.conflictType == SandboxFloorImportConflictType.DuplicateFloorName), Is.True);
            Assert.That(conflictAnalysis.conflicts.Any(conflict => conflict.message.Contains("Imported Twin")), Is.True);
            Assert.That(conflictAnalysis.conflicts.Any(conflict => conflict.conflictType == SandboxFloorImportConflictType.DuplicateEntityId), Is.True);
            Assert.That(conflictAnalysis.conflicts.Any(conflict => conflict.conflictType == SandboxFloorImportConflictType.ExternalStairLink), Is.True);
            Assert.That(conflictAnalysis.conflicts.Any(conflict => conflict.conflictType == SandboxFloorImportConflictType.CrossFloorReference), Is.True);
            Assert.That(transferService.ImportFloorsFromPath(conflictPath), Is.False);

            var safeSource = new BuildingProjectData
            {
                metadata = new ProjectMetadataData(),
                blueprintReferences =
                {
                    new BlueprintReferenceData
                    {
                        blueprintReferenceId = "bp-import-1",
                        assetGuid = "guid-import-1",
                        assetPath = "Assets/Art/Blueprints/Sandbox/imported-floor.png",
                        sourceFileName = "imported-floor.png"
                    }
                },
                floors =
                {
                    new FloorData
                    {
                        floorId = "import-floor-safe",
                        name = "Imported Level",
                        order = 0,
                        blueprintReferenceId = "bp-import-1"
                    }
                }
            };
            SandboxProjectDataUtility.EnsureIds(safeSource);
            File.WriteAllText(safePath, SandboxProjectSerializer.Serialize(safeSource));

            var safeAnalysis = transferService.AnalyzeFloorImportFromPath(safePath);
            Assert.That(safeAnalysis.CanImport, Is.True);
            Assert.That(transferService.ImportFloorsFromPath(safePath), Is.True);
            Assert.That(workspaceService.ActiveProject.floors.Any(floor => floor.name == "Imported Level"), Is.True);
            Assert.That(workspaceService.ActiveProject.blueprintReferences.Any(reference => reference.sourceFileName == "imported-floor.png"), Is.True);

            Object.DestroyImmediate(host);
        }

        private static GameObject CreatePhase9Host(
            string autosaveRoot,
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxSaveLoadService saveLoadService,
            out SandboxProjectTransferService transferService,
            out SandboxValidationService validationService,
            out SandboxColliderRebuildService colliderRebuildService,
            out SandboxPreviewImageExportService previewImageExportService,
            out SandboxStatusBarShell unusedStatusBar)
        {
            var host = new GameObject("Phase9Host");
            saveLoadService = host.AddComponent<SandboxSaveLoadService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            transferService = host.AddComponent<SandboxProjectTransferService>();
            colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            validationService = host.AddComponent<SandboxValidationService>();
            previewImageExportService = host.AddComponent<SandboxPreviewImageExportService>();
            unusedStatusBar = null;

            saveLoadService.ConfigureAutosave(autosaveRoot, 10f, 1, true);

            saveLoadService.SendMessage("Awake");
            workspaceService.SendMessage("Awake");
            colliderRebuildService.SendMessage("Awake");
            validationService.SendMessage("Awake");
            transferService.SendMessage("Awake");
            previewImageExportService.SendMessage("Awake");
            return host;
        }

        private static string CreateTempDirectory(string prefix)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
