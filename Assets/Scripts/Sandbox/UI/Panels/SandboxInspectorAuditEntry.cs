using System;

namespace EvacLogix.Sandbox.UI.Panels
{
    [Serializable]
    public sealed class SandboxInspectorAuditEntry
    {
        public string key = string.Empty;
        public string displayName = string.Empty;
        public int namingFieldCount;
        public int numericFieldCount;
        public bool supportsMetadataFields;
        public string advancedFoldoutKey = string.Empty;
    }
}
