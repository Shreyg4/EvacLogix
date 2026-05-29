using System;
using System.Collections.Generic;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxDetectedRoomData
    {
        public string roomId = string.Empty;
        public string floorId = string.Empty;
        public List<Vector2> polygonPoints = new();
        public List<string> boundaryWallSegmentIds = new();
        public List<string> openingObjectIds = new();
        public List<Vector2> openingPositions = new();
        public bool hasIntentionalOpenings;
        public float area;
    }
}
