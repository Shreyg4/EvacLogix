namespace EvacLogix.Sandbox.Data.Migrations
{
    public static class SandboxProjectMigrator
    {
        public static void MigrateToCurrent(BuildingProjectData project)
        {
            if (project == null)
            {
                return;
            }

            if (project.schemaVersion <= 0)
            {
                project.schemaVersion = SandboxSchemaVersions.Initial;
            }

            if (project.schemaVersion > SandboxSchemaVersions.Current)
            {
                throw new SandboxMigrationException(
                    $"Unsupported sandbox schema version {project.schemaVersion}. Current supported version is {SandboxSchemaVersions.Current}.");
            }

            while (project.schemaVersion < SandboxSchemaVersions.Current)
            {
                switch (project.schemaVersion)
                {
                    default:
                        throw new SandboxMigrationException(
                            $"No migration step registered for sandbox schema version {project.schemaVersion}.");
                }
            }
        }
    }
}
