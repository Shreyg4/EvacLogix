using System;
using System.Collections.Generic;

namespace EvacLogix.Sandbox.Data.Serialization
{
    // Captures undo/redo snapshots WITHOUT the heavy blueprint image payloads. Imported blueprint images
    // can only change through a non-undoable path (import/add; there is no remove), so a snapshot never
    // needs to carry the base64 — it is re-attached from the live project on restore. This keeps every
    // undo entry small (project text minus megabytes of image), which is what lets large blueprints stay
    // within the WebGL heap.
    public static class SandboxProjectSnapshot
    {
        // Serializes the project with blueprint payloads temporarily cleared. The clear is synchronous and
        // always restored in the finally block, so the live project is unchanged once this returns.
        public static string CaptureWithoutPayloads(BuildingProjectData project)
        {
            if (project?.blueprintReferences == null || project.blueprintReferences.Count == 0)
            {
                return SandboxProjectSerializer.Serialize(project, false);
            }

            var saved = new string[project.blueprintReferences.Count];
            try
            {
                for (var i = 0; i < project.blueprintReferences.Count; i += 1)
                {
                    var reference = project.blueprintReferences[i];
                    saved[i] = reference?.importedPayloadBase64;
                    if (reference != null)
                    {
                        reference.importedPayloadBase64 = string.Empty;
                    }
                }

                return SandboxProjectSerializer.Serialize(project, false);
            }
            finally
            {
                for (var i = 0; i < project.blueprintReferences.Count; i += 1)
                {
                    var reference = project.blueprintReferences[i];
                    if (reference != null)
                    {
                        reference.importedPayloadBase64 = saved[i] ?? string.Empty;
                    }
                }
            }
        }

        // Deserializes a payload-free snapshot and re-attaches the live project's blueprint payloads by id,
        // so undo/redo never blanks an imported image.
        public static BuildingProjectData RestoreWithPayloads(string json, BuildingProjectData liveProject)
        {
            var restored = SandboxProjectSerializer.Deserialize(json);
            if (restored?.blueprintReferences == null || liveProject?.blueprintReferences == null)
            {
                return restored;
            }

            var payloadsById = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < liveProject.blueprintReferences.Count; i += 1)
            {
                var reference = liveProject.blueprintReferences[i];
                if (reference != null && !string.IsNullOrEmpty(reference.importedPayloadBase64))
                {
                    payloadsById[reference.blueprintReferenceId] = reference.importedPayloadBase64;
                }
            }

            for (var i = 0; i < restored.blueprintReferences.Count; i += 1)
            {
                var reference = restored.blueprintReferences[i];
                if (reference != null &&
                    string.IsNullOrEmpty(reference.importedPayloadBase64) &&
                    payloadsById.TryGetValue(reference.blueprintReferenceId, out var payload))
                {
                    reference.importedPayloadBase64 = payload;
                }
            }

            return restored;
        }
    }
}
