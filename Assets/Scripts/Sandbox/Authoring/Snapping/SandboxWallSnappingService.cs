using System;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
using UnityEngine;

namespace EvacLogix.Sandbox.Authoring.Snapping
{
    public enum SandboxWallSnapTargetKind
    {
        None = 0,
        Grid = 1,
        Endpoint = 2,
        Segment = 3,
        Angle = 4,
        Intersection = 5,
    }

    public readonly struct SandboxWallSnapResult
    {
        public SandboxWallSnapResult(Vector2 position, SandboxWallSnapTargetKind targetKind, string referenceId)
        {
            this.position = position;
            this.targetKind = targetKind;
            this.referenceId = referenceId ?? string.Empty;
        }

        public readonly Vector2 position;
        public readonly SandboxWallSnapTargetKind targetKind;
        public readonly string referenceId;
    }

    public sealed class SandboxWallSnappingService : MonoBehaviour
    {
        [SerializeField] private float gridSize = 0.5f;
        [SerializeField] private float gridSnapDistance = 0.2f;
        [SerializeField] private float endpointSnapDistance = 0.35f;
        [SerializeField] private float endpointSnapPixelTolerance = 26f;
        [SerializeField] private float segmentSnapDistance = 0.25f;
        [SerializeField] private float angleIncrementDegrees = 45f;
        [SerializeField] private float angleSnapToleranceDegrees = 10f;
        [SerializeField] private float junctionReuseDistance = 0.3f;
        [SerializeField] private bool gridSnappingEnabled = true;
        [SerializeField] private bool endpointSnappingEnabled = true;
        [SerializeField] private bool segmentSnappingEnabled = true;
        [SerializeField] private bool angleSnappingEnabled = true;
        [SerializeField] private bool temporarySnappingBypass;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxWorkspaceStateService workspaceStateService;

        public float JunctionReuseDistance => junctionReuseDistance;
        public bool GridSnappingEnabled => gridSnappingEnabled;
        public bool EndpointSnappingEnabled => endpointSnappingEnabled;
        public bool SegmentSnappingEnabled => segmentSnappingEnabled;
        public bool AngleSnappingEnabled => angleSnappingEnabled;
        public bool TemporarySnappingBypass => temporarySnappingBypass;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            workspaceStateService = GetComponent<SandboxWorkspaceStateService>();
        }

        public void ConfigureSnapTargets(bool useGrid, bool useEndpoints, bool useSegments, bool useAngles)
        {
            gridSnappingEnabled = useGrid;
            endpointSnappingEnabled = useEndpoints;
            segmentSnappingEnabled = useSegments;
            angleSnappingEnabled = useAngles;
        }

        public void SetTemporarySnappingBypass(bool enabled)
        {
            temporarySnappingBypass = enabled;
        }

        public SandboxWallSnapResult SnapPoint(string floorId, Vector2 rawPoint, Vector2? anchorPoint)
        {
            if ((workspaceStateService != null && !workspaceStateService.SnappingEnabled) || temporarySnappingBypass)
            {
                return new SandboxWallSnapResult(rawPoint, SandboxWallSnapTargetKind.None, string.Empty);
            }

            var floor = workspaceService?.FindFloor(floorId);
            if (floor == null)
            {
                return new SandboxWallSnapResult(rawPoint, SandboxWallSnapTargetKind.None, string.Empty);
            }

            if (endpointSnappingEnabled)
            {
                // Use a zoom-aware (screen-space) reach so off-grid intersections snap from a
                // consistent on-screen distance, not just the small fixed world radius.
                var effectiveEndpointDistance = Mathf.Max(
                    endpointSnapDistance,
                    SandboxAlignmentGuideUtility.PixelToleranceToWorld(Camera.main, endpointSnapPixelTolerance));
                var snappedEndpoint = floor.wallJunctions
                    .Where(junction => Vector2.Distance(junction.position, rawPoint) <= effectiveEndpointDistance)
                    .OrderBy(junction => Vector2.Distance(junction.position, rawPoint))
                    .FirstOrDefault();

                if (snappedEndpoint != null)
                {
                    return new SandboxWallSnapResult(
                        snappedEndpoint.position,
                        SandboxWallSnapTargetKind.Endpoint,
                        snappedEndpoint.wallJunctionId);
                }

                // Snap to where two wall segments cross (not just shared junctions), with priority
                // over grid/angle so real intersections win.
                if (SandboxAlignmentGuideUtility.TryFindNearestIntersectionPoint(floor, rawPoint, effectiveEndpointDistance, out var crossing))
                {
                    return new SandboxWallSnapResult(crossing, SandboxWallSnapTargetKind.Intersection, string.Empty);
                }
            }

            var bestResult = new SandboxWallSnapResult(rawPoint, SandboxWallSnapTargetKind.None, string.Empty);
            var bestDistance = float.MaxValue;

            if (angleSnappingEnabled && anchorPoint.HasValue)
            {
                    var delta = rawPoint - anchorPoint.Value;
                if (delta.sqrMagnitude > Mathf.Epsilon)
                {
                    var rawAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    var activeAngleIncrement = workspaceStateService != null
                        ? workspaceStateService.AngleSnapIncrementDegrees
                        : angleIncrementDegrees;
                    var snappedAngle = Mathf.Round(rawAngle / activeAngleIncrement) * activeAngleIncrement;
                    var angleDelta = Mathf.Abs(Mathf.DeltaAngle(rawAngle, snappedAngle));
                    if (angleDelta <= angleSnapToleranceDegrees)
                    {
                        var distance = delta.magnitude;
                        var snappedDirection = new Vector2(
                            Mathf.Cos(snappedAngle * Mathf.Deg2Rad),
                            Mathf.Sin(snappedAngle * Mathf.Deg2Rad));
                        var anglePoint = anchorPoint.Value + snappedDirection * distance;
                        var angleDistance = Vector2.Distance(rawPoint, anglePoint);
                        if (angleDistance < bestDistance)
                        {
                            bestDistance = angleDistance;
                            bestResult = new SandboxWallSnapResult(anglePoint, SandboxWallSnapTargetKind.Angle, string.Empty);
                        }
                    }
                }
            }

            if (segmentSnappingEnabled)
            {
                foreach (var wall in floor.wallSegments)
                {
                    var projectedPoint = ProjectPointOntoSegment(rawPoint, wall.startPoint, wall.endPoint);
                    var segmentDistance = Vector2.Distance(rawPoint, projectedPoint);
                    if (segmentDistance <= segmentSnapDistance && segmentDistance < bestDistance)
                    {
                        bestDistance = segmentDistance;
                        bestResult = new SandboxWallSnapResult(
                            projectedPoint,
                            SandboxWallSnapTargetKind.Segment,
                            wall.wallSegmentId);
                    }
                }
            }

            if (gridSnappingEnabled)
            {
                var activeGridSize = workspaceStateService != null
                    ? workspaceStateService.GridSize
                    : gridSize;
                var gridPoint = new Vector2(
                    Mathf.Round(rawPoint.x / activeGridSize) * activeGridSize,
                    Mathf.Round(rawPoint.y / activeGridSize) * activeGridSize);
                var gridDistance = Vector2.Distance(rawPoint, gridPoint);
                if (gridDistance <= gridSnapDistance && gridDistance < bestDistance)
                {
                    bestDistance = gridDistance;
                    bestResult = new SandboxWallSnapResult(gridPoint, SandboxWallSnapTargetKind.Grid, string.Empty);
                }
            }

            return bestResult;
        }

        private static Vector2 ProjectPointOntoSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var segment = segmentEnd - segmentStart;
            var segmentLengthSquared = segment.sqrMagnitude;
            if (segmentLengthSquared <= Mathf.Epsilon)
            {
                return segmentStart;
            }

            var t = Vector2.Dot(point - segmentStart, segment) / segmentLengthSquared;
            t = Mathf.Clamp01(t);
            return segmentStart + segment * t;
        }
    }
}
