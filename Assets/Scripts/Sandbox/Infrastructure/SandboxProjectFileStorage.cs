using System.IO;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;

namespace EvacLogix.Sandbox.Infrastructure
{
    public static class SandboxProjectFileStorage
    {
        public static BuildingProjectData ReadProjectFromPath(string filePath)
        {
            return SandboxProjectSerializer.Deserialize(File.ReadAllText(filePath));
        }

        public static void WriteProjectToPath(string filePath, BuildingProjectData project, bool prettyPrint)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, SandboxProjectSerializer.Serialize(project, prettyPrint));
        }
    }
}
