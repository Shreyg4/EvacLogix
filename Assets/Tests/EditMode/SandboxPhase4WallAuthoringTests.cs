using System.Linq;
using EvacLogix.Sandbox.Authoring;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Authoring.Selection;
using EvacLogix.Sandbox.Authoring.Snapping;
using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using EvacLogix.Sandbox.UI.Overlays;
using EvacLogix.Sandbox.UI.Panels;
using NUnit.Framework;
using UnityEngine;

namespace EvacLogix.Tests.EditMode
{
    public sealed class SandboxPhase4WallAuthoringTests
    {
        [Test]
        public void LineTool_TwoPointFlowCreatesEditableWallAndSupportsUndoRedo()
        {
            var host = CreateWallAuthoringHost(out var workspace, out var history, out var authoringService, out var rebuildService);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(authoringService.TryRegisterLinePoint(new Vector2(0f, 0f), out _), Is.False);
            Assert.That(authoringService.HasPendingLineStart, Is.True);

            Assert.That(authoringService.TryRegisterLinePoint(new Vector2(4f, 0.08f), out var wallId), Is.True);

            var floor = workspace.ActiveFloor;
            Assert.That(floor.wallSegments.Count, Is.EqualTo(1));
            Assert.That(floor.wallJunctions.Count, Is.EqualTo(2));
            Assert.That(floor.wallSegments[0].wallSegmentId, Is.EqualTo(wallId));
            Assert.That(rebuildService.RebuildRequestCount, Is.EqualTo(1));

            Assert.That(history.Undo(), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.EqualTo(0));

            Assert.That(history.Redo(), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.EqualTo(1));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void BrushTool_CleansPointsAndCreatesConnectedWallSegments()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(authoringService.BeginBrushStrokeCapture(new Vector2(0f, 0f)), Is.True);
            authoringService.AppendBrushStrokePoint(new Vector2(0.03f, 0f));
            authoringService.AppendBrushStrokePoint(new Vector2(1f, 0.1f));
            authoringService.AppendBrushStrokePoint(new Vector2(2f, 0.15f));
            authoringService.AppendBrushStrokePoint(new Vector2(2.1f, 0.2f));
            authoringService.AppendBrushStrokePoint(new Vector2(3f, 1f));
            authoringService.EndBrushStrokeCapture();

            Assert.That(authoringService.LastCleanedBrushStrokePoints.Count, Is.LessThan(authoringService.ActiveBrushStrokePoints.Count));
            Assert.That(authoringService.AcceptActiveBrushStroke(0.3f), Is.True);

            var floor = workspace.ActiveFloor;
            Assert.That(floor.wallSegments.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(floor.wallJunctions.Any(junction => junction.connectedWallSegmentIds.Count >= 2), Is.True);
            Assert.That(floor.wallSegments.All(wall => wall.thickness == 0.3f), Is.True);

            Object.DestroyImmediate(host);
        }

        [Test]
        public void SnappingService_SupportsEndpointSegmentGridAndAngleTargets()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            var snappingService = host.GetComponent<SandboxWallSnappingService>();
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(authoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(6f, 0f)), Is.True);
            var floorId = workspace.ActiveFloor.floorId;

            var endpointSnap = snappingService.SnapPoint(floorId, new Vector2(0.1f, 0.1f), null);
            Assert.That(endpointSnap.targetKind, Is.EqualTo(SandboxWallSnapTargetKind.Endpoint));
            Assert.That(endpointSnap.position, Is.EqualTo(Vector2.zero));

            var segmentSnap = snappingService.SnapPoint(floorId, new Vector2(2.5f, 0.12f), null);
            Assert.That(segmentSnap.targetKind, Is.EqualTo(SandboxWallSnapTargetKind.Segment));
            Assert.That(segmentSnap.position.y, Is.EqualTo(0f).Within(0.001f));

            var gridSnap = snappingService.SnapPoint(floorId, new Vector2(1.11f, 1.13f), null);
            Assert.That(gridSnap.targetKind, Is.EqualTo(SandboxWallSnapTargetKind.Grid));

            var angleSnap = snappingService.SnapPoint(floorId, new Vector2(2.1f, 2.05f), Vector2.zero);
            Assert.That(angleSnap.targetKind, Is.EqualTo(SandboxWallSnapTargetKind.Angle));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void MovingSharedWallHandleDetachesEditedWallWithoutDraggingNeighbor()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            Assert.That(authoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f)), Is.True);
            Assert.That(authoringService.CreateLineWall(new Vector2(5f, 0f), new Vector2(5f, 4f)), Is.True);

            var floor = workspace.ActiveFloor;
            var horizontalWall = floor.wallSegments.First(wall => Mathf.Approximately(wall.endPoint.y, 0f) && wall.startPoint.y == 0f);
            var verticalWall = floor.wallSegments.First(wall => Mathf.Approximately(wall.startPoint.x, 5f) && Mathf.Approximately(wall.endPoint.x, 5f));

            Assert.That(authoringService.MoveWallEndHandle(horizontalWall.wallSegmentId, new Vector2(6f, 0f)), Is.True);

            floor = workspace.ActiveFloor;
            horizontalWall = floor.wallSegments.First(wall => wall.wallSegmentId == horizontalWall.wallSegmentId);
            verticalWall = floor.wallSegments.First(wall => wall.wallSegmentId == verticalWall.wallSegmentId);

            Assert.That(horizontalWall.endPoint, Is.EqualTo(new Vector2(6f, 0f)));
            Assert.That(verticalWall.startPoint, Is.EqualTo(new Vector2(5f, 0f)));
            Assert.That(floor.wallJunctions.Count, Is.EqualTo(4));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void NumericEditAndCleanupActionsPreserveEditableTopology()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(authoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(10f, 0f)), Is.True);

            var wallId = workspace.ActiveFloor.wallSegments[0].wallSegmentId;
            Assert.That(authoringService.SetWallThickness(wallId, 0.45f), Is.True);
            Assert.That(authoringService.SetWallEndpoints(wallId, new Vector2(1f, 0f), new Vector2(9f, 0f)), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments[0].thickness, Is.EqualTo(0.45f).Within(0.0001f));

            Assert.That(authoringService.SplitWall(wallId, new Vector2(5f, 0f)), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.EqualTo(2));
            Assert.That(workspace.ActiveFloor.wallJunctions.Count, Is.EqualTo(3));

            var currentWallIds = workspace.ActiveFloor.wallSegments.Select(wall => wall.wallSegmentId).ToArray();
            Assert.That(authoringService.MergeWalls(currentWallIds[0], currentWallIds[1]), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.EqualTo(1));

            wallId = workspace.ActiveFloor.wallSegments[0].wallSegmentId;
            Assert.That(authoringService.TrimWallEnd(wallId, new Vector2(7f, 0.2f)), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments[0].endPoint.x, Is.EqualTo(7f).Within(0.001f));

            Assert.That(authoringService.EraseWall(wallId), Is.True);
            Assert.That(workspace.ActiveFloor.wallSegments.Count, Is.EqualTo(0));
            Assert.That(workspace.ActiveFloor.wallJunctions.Count, Is.EqualTo(0));

            Object.DestroyImmediate(host);
        }

        [Test]
        public void OverlayHandleDrag_CommitsDirectEndpointEditing()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);
            Assert.That(authoringService.CreateLineWall(new Vector2(0f, 0f), new Vector2(5f, 0f)), Is.True);

            var overlayObject = new GameObject("OverlayRoot");
            var overlay = overlayObject.AddComponent<SandboxWallAuthoringOverlay>();
            overlay.SendMessage("Awake");

            var wallId = workspace.ActiveFloor.wallSegments[0].wallSegmentId;
            Assert.That(overlay.TryBeginHandleDrag(new Vector2(5f, 0f)), Is.True);
            overlay.UpdateHandleDragPreview(new Vector2(6f, 0f));
            Assert.That(overlay.IsHandleDragActive, Is.True);
            Assert.That(overlay.DraggedWallSegmentId, Is.EqualTo(wallId));
            Assert.That(overlay.CommitHandleDrag(), Is.True);

            Assert.That(workspace.ActiveFloor.wallSegments[0].endPoint, Is.EqualTo(new Vector2(6f, 0f)));

            Object.DestroyImmediate(overlayObject);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void OverlayRenderer_ShowsBrushPreviewBeforeAcceptance()
        {
            var host = CreateWallAuthoringHost(out var workspace, out _, out var authoringService, out _);
            workspace.CreateNewProject(SandboxProjectTemplateKind.DefaultTemplate);

            var world = new GameObject("World");
            var wallRoot = new GameObject("WallRoot");
            wallRoot.transform.SetParent(world.transform);
            new GameObject("HandleRoot").transform.SetParent(world.transform);
            var renderer = wallRoot.AddComponent<SandboxWallOverlayRenderer>();
            renderer.SendMessage("Awake");

            Assert.That(authoringService.BeginBrushStrokeCapture(new Vector2(0f, 0f)), Is.True);
            Assert.That(authoringService.AppendBrushStrokePoint(new Vector2(1f, 0.2f)), Is.True);
            Assert.That(authoringService.AppendBrushStrokePoint(new Vector2(2f, 0.4f)), Is.True);

            Assert.That(wallRoot.transform.childCount, Is.GreaterThan(0));

            authoringService.CancelBrushStroke();
            Assert.That(wallRoot.transform.childCount, Is.EqualTo(0));

            Object.DestroyImmediate(world);
            Object.DestroyImmediate(host);
        }

        [Test]
        public void Bootstrap_InstallsWallAuthoringServicesAndRenderers()
        {
            var systems = new GameObject("Systems");
            systems.AddComponent<SandboxEditorInstaller>();
            systems.AddComponent<SandboxApp>();

            var world = new GameObject("World");
            new GameObject("BlueprintRoot").transform.SetParent(world.transform);
            new GameObject("GridRoot").transform.SetParent(world.transform);
            new GameObject("FloorRoot").transform.SetParent(world.transform);
            new GameObject("RuntimeOverlayRoot").transform.SetParent(world.transform);

            var ui = new GameObject("UI");
            new GameObject("TopBar").transform.SetParent(ui.transform);
            new GameObject("LeftToolPanel").transform.SetParent(ui.transform);
            new GameObject("RightInspectorPanel").transform.SetParent(ui.transform);
            new GameObject("BottomStatusBar").transform.SetParent(ui.transform);
            new GameObject("FloorTabsBar").transform.SetParent(ui.transform);
            new GameObject("ValidationPanelRoot").transform.SetParent(ui.transform);
            new GameObject("ModalRoot").transform.SetParent(ui.transform);

            var overlayRoot = new GameObject("OverlayRoot");
            var debugRoot = new GameObject("DebugRoot");

            systems.SendMessage("Awake");

            Assert.That(systems.GetComponent<SandboxWallSnappingService>(), Is.Not.Null);
            Assert.That(systems.GetComponent<SandboxWallAuthoringService>(), Is.Not.Null);
            Assert.That(overlayRoot.GetComponent<SandboxWallAuthoringOverlay>(), Is.Not.Null);
            Assert.That(world.transform.Find("WallRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("HandleRoot"), Is.Not.Null);
            Assert.That(world.transform.Find("WallRoot").GetComponent<SandboxWallOverlayRenderer>(), Is.Not.Null);

            Object.DestroyImmediate(systems);
            Object.DestroyImmediate(world);
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(overlayRoot);
            Object.DestroyImmediate(debugRoot);
        }

        private static GameObject CreateWallAuthoringHost(
            out SandboxProjectWorkspaceService workspaceService,
            out SandboxCommandHistory commandHistory,
            out SandboxWallAuthoringService wallAuthoringService,
            out SandboxColliderRebuildService colliderRebuildService)
        {
            var host = new GameObject("WallAuthoringHost");
            host.AddComponent<SandboxSaveLoadService>();
            commandHistory = host.AddComponent<SandboxCommandHistory>();
            host.AddComponent<SandboxSelectionService>();
            host.AddComponent<SandboxWorkspaceStateService>();
            colliderRebuildService = host.AddComponent<SandboxColliderRebuildService>();
            workspaceService = host.AddComponent<SandboxProjectWorkspaceService>();
            host.AddComponent<SandboxWallSnappingService>();
            wallAuthoringService = host.AddComponent<SandboxWallAuthoringService>();

            workspaceService.SendMessage("Awake");
            host.GetComponent<SandboxWallSnappingService>().SendMessage("Awake");
            wallAuthoringService.SendMessage("Awake");
            return host;
        }
    }
}
