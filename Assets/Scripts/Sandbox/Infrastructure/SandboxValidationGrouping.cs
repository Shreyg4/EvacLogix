using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxValidationObjectGroup
    {
        public string objectId = string.Empty;
        public string label = string.Empty;
        public List<ValidationIssueData> issues = new();
    }

    [Serializable]
    public sealed class SandboxValidationFloorGroup
    {
        public string floorId = string.Empty;
        public string label = string.Empty;
        public List<SandboxValidationObjectGroup> objectGroups = new();
    }
}
