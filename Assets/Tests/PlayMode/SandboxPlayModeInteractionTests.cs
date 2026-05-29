using System.Collections;
using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Authoring.Tools;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EvacLogix.Tests.PlayMode
{
    public sealed class SandboxPlayModeInteractionTests
    {
        [UnityTest]
        public IEnumerator WallPlacement_CreatesGeometryAcrossFrames()
        {
            var harness = CreateHarness();
            yield return null;

            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(harness.wallAuthoringService.TryRegisterLinePoint(new Vector2(0f, 0f), out _), Is.False);
            yield return null;
            Assert.That(harness.wallAuthoringService.TryRegisterLinePoint(new Vector2(4f, 0f), out var wallId), Is.True);

            Assert.That(harness.workspaceService.ActiveFloor.wallSegments.Count, Is.EqualTo(1));
            Assert.That(harness.selectionService.SelectedObjectIds.Single(), Is.EqualTo(wallId));

            harness.Destroy();
        }

        [UnityTest]
        public IEnumerator Snapping_UsesGridAndAngleStateInPlayMode()
        {
            var harness = CreateHarness();
            yield return null;

            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            harness.workspaceStateService.SetGridSize(1f);
            harness.workspaceStateService.SetAngleSnapIncrementDegrees(90f);

            Assert.That(harness.wallAuthoringService.CreateLineWall(new Vector2(0.1f, 0.1f), new Vector2(2.1f, 0.2f), 0.2f), Is.True);
            var wall = harness.workspaceService.ActiveFloor.wallSegments.Single();

            Assert.That(wall.startPoint, Is.EqualTo(Vector2.zero));
            Assert.That(wall.endPoint.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(wall.endPoint.y, Is.EqualTo(0f).Within(0.001f));

            harness.Destroy();
        }

        [UnityTest]
        public IEnumerator ObjectPlacement_CreatesSemanticAndPreviewEntities()
        {
            var harness = CreateHarness();
            yield return null;

            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(harness.wallAuthoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f), 0.2f), Is.True);

            Assert.That(harness.semanticObjectAuthoringService.PlaceDoor(new Vector2(2f, 0.05f), out _), Is.True);
            Assert.That(harness.semanticObjectAuthoringService.PlaceExit(new Vector2(7f, 0f), out _, new Vector2(2f, 1.5f), 0f, 1.5f, 30f, 1f, "Main Exit"), Is.True);
            Assert.That(harness.semanticObjectAuthoringService.PlaceObstacle(new Vector2(3f, 2f), out _, new Vector2(1f, 1f), 0f, 0.5f, 0.5f, "Chair Cluster"), Is.True);
            Assert.That(harness.previewAuthoringService.PlaceSpawnPoint(new Vector2(1f, 1f), out _, out _, null, "Play Layout", true), Is.True);
            Assert.That(harness.previewAuthoringService.PlaceFireOrigin(new Vector2(-2f, -2f), out _, 1.2f, 2f, true), Is.True);
            yield return null;

            Assert.That(harness.workspaceService.ActiveFloor.doors.Count, Is.EqualTo(1));
            Assert.That(harness.workspaceService.ActiveFloor.exits.Count, Is.EqualTo(1));
            Assert.That(harness.workspaceService.ActiveFloor.obstacles.Count, Is.EqualTo(1));
            Assert.That(harness.workspaceService.ActiveProject.spawnLayouts.SelectMany(layout => layout.spawnPoints).Count(), Is.EqualTo(1));
            Assert.That(harness.workspaceService.ActiveProject.fireOrigins.Count, Is.EqualTo(1));

            harness.Destroy();
        }

        [UnityTest]
        public IEnumerator Selection_TracksAndMovesClipboardSafeObjects()
        {
            var harness = CreateHarness();
            yield return null;

            harness.workspaceService.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(harness.semanticObjectAuthoringService.PlaceObstacle(new Vector2(2f, 2f), out var obstacleId, new Vector2(1f, 1f), 0f, 1f, 0f, "Bench"), Is.True);
            harness.selectionService.ReplaceSelection(new[] { obstacleId });
            yield return null;

            Assert.That(harness.selectionService.SelectedObjectIds.Single(), Is.EqualTo(obstacleId));
            Assert.That(harness.clipboardService.MoveSelection(new Vector2(3f, -1f)), Is.True);

            var obstacle = harness.workspaceService.ActiveFloor.obstacles.Single(candidate => candidate.obstacleId == obstacleId);
            Assert.That(obstacle.center, Is.EqualTo(new Vector2(5f, 1f)));

            harness.Destroy();
        }

        private static PlayModeHarness CreateHarness()
        {
            var host = new GameObject("PlayModeHarness");
            host.AddComponent<SandboxSaveLoadService>();
            host.AddComponent<SandboxCommandHistory>();
            var selectionService = host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxInputRouter>();
            host.AddComponent<SandboxToolStateService>();
            var workspaceStateService = host.AddComponent<SandboxWorkspaceStateService>();
            var workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxColliderRebuildService>();
            host.AddComponent<SandboxValidationService>();
            host.AddComponent<SandboxVisualOrganizationService>();
            var clipboardService = host.AddComponent<SandboxClipboardService>();
            host.AddComponent<SandboxWallSnappingService>();
            var wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();
            var semanticObjectAuthoringService = host.AddComponent<SandboxSemanticObjectAuthoringService>();
            host.AddComponent<SandboxFloorManagementService>();
            host.AddComponent<SandboxPreviewService>();
            var previewAuthoringService = host.AddComponent<SandboxPreviewAuthoringService>();

            return new PlayModeHarness
            {
                host = host,
                workspaceService = workspaceService,
                workspaceStateService = workspaceStateService,
                selectionService = selectionService,
                clipboardService = clipboardService,
                wallAuthoringService = wallAuthoringService,
                semanticObjectAuthoringService = semanticObjectAuthoringService,
                previewAuthoringService = previewAuthoringService
            };
        }

        private sealed class PlayModeHarness
        {
            public GameObject host;
            public SandboxProjectWorkspaceService workspaceService;
            public SandboxWorkspaceStateService workspaceStateService;
            public SandboxSelectionService selectionService;
            public SandboxClipboardService clipboardService;
            public SandboxWallAuthoringService wallAuthoringService;
            public SandboxSemanticObjectAuthoringService semanticObjectAuthoringService;
            public SandboxPreviewAuthoringService previewAuthoringService;

            public void Destroy()
            {
                Object.Destroy(host);
            }
        }
    }
}
