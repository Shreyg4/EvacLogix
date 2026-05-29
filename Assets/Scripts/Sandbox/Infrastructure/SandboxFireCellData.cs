using System;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxFireCellData
    {
        public string cellId = string.Empty;
        public string floorId = string.Empty;
        public string sourceFireOriginId = string.Empty;
        public Vector2 position;
        public float intensity;
        public float sourceSpreadIntensity;
        public float ageSeconds;
    }
}