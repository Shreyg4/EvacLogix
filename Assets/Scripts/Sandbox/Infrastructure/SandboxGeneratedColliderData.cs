using System;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxGeneratedColliderData
    {
        public string colliderId = string.Empty;
        public string floorId = string.Empty;
        public string sourceWallSegmentId = string.Empty;
        public Vector2 center;
        public Vector2 size = Vector2.one;
        public float rotationDegrees;
    }
}
