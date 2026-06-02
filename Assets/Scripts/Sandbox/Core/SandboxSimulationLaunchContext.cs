using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;

namespace EvacLogix.Sandbox.Core
{
    // Carries the project to simulate across the scene load into the Simulation scene. Scene
    // transitions discard in-memory state, so the launching scene (Main Menu or Editor) serializes
    // the project here, and the Simulation scene reads + clears it on startup.
    //
    // Two entry points populate this:
    //   - Main Menu "Simulation Mode": loads a saved project JSON from disk.
    //   - Editor "Simulate current project": hands off the in-memory (possibly unsaved) project.
    public static class SandboxSimulationLaunchContext
    {
        public const string SimulationSceneName = "SandboxSimulation";

        private static string pendingProjectJson;

        // Scene to return to when the user leaves the simulation (Main Menu by default; the editor
        // sets this to "SandboxEditor" so the round trip lands back where the user started).
        public static string ReturnSceneName { get; private set; } = "MainMenu";

        // Human-readable description of where the simulated project came from, shown in the HUD.
        public static string SourceLabel { get; private set; } = string.Empty;

        public static bool HasPendingProject => !string.IsNullOrWhiteSpace(pendingProjectJson);

        public static void SetFromJson(string projectJson, string returnSceneName, string sourceLabel)
        {
            pendingProjectJson = projectJson ?? string.Empty;
            ReturnSceneName = string.IsNullOrWhiteSpace(returnSceneName) ? "MainMenu" : returnSceneName;
            SourceLabel = sourceLabel ?? string.Empty;
        }

        public static void SetFromProject(BuildingProjectData project, string returnSceneName, string sourceLabel)
        {
            SetFromJson(project == null ? string.Empty : SandboxProjectSerializer.Serialize(project, false), returnSceneName, sourceLabel);
        }

        // Deserializes the pending project and clears the slot so a later return to the menu does not
        // accidentally re-simulate a stale project. Returns null when nothing was queued.
        public static BuildingProjectData ConsumeProject()
        {
            if (string.IsNullOrWhiteSpace(pendingProjectJson))
            {
                return null;
            }

            var json = pendingProjectJson;
            pendingProjectJson = null;
            return SandboxProjectSerializer.Deserialize(json);
        }

        public static void Clear()
        {
            pendingProjectJson = null;
            SourceLabel = string.Empty;
        }

        // The project to restore when returning to the EDITOR after a simulation launched from it.
        // Unlike the pending (sim) project, this is NOT cleared when the simulation starts — it
        // survives the whole round trip so the editor can re-adopt the project the user left, instead
        // of booting a fresh default (which would look like the project was deleted).
        private static string returnProjectJson;

        public static void SetReturnProject(BuildingProjectData project)
        {
            returnProjectJson = project == null ? null : SandboxProjectSerializer.Serialize(project, false);
        }

        public static bool TryConsumeReturnProject(out BuildingProjectData project)
        {
            project = null;
            if (string.IsNullOrWhiteSpace(returnProjectJson))
            {
                return false;
            }

            project = SandboxProjectSerializer.Deserialize(returnProjectJson);
            returnProjectJson = null;
            return true;
        }
    }
}
