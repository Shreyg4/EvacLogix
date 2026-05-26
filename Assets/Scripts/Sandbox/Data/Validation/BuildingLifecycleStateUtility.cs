namespace EvacLogix.Sandbox.Data.Validation
{
    public static class BuildingLifecycleStateUtility
    {
        public static string GetDisplayLabel(BuildingLifecycleState lifecycleState)
        {
            return lifecycleState switch
            {
                BuildingLifecycleState.Draft => "Draft",
                BuildingLifecycleState.ValidationFailed => "Validation Failed",
                BuildingLifecycleState.ReadyForSimulation => "Ready for Simulation",
                BuildingLifecycleState.ReadyForExport => "Ready for Export",
                _ => lifecycleState.ToString()
            };
        }

        public static BuildingLifecycleState Evaluate(BuildingProjectData project)
        {
            if (project == null)
            {
                return BuildingLifecycleState.Draft;
            }

            var hasFloors = project.floors != null && project.floors.Count > 0;
            var issues = project.validationSnapshot?.issues;
            var hasBlockingIssues = false;

            if (issues != null)
            {
                for (var i = 0; i < issues.Count; i += 1)
                {
                    if (issues[i].severity == ValidationIssueSeverity.BlockingError)
                    {
                        hasBlockingIssues = true;
                        break;
                    }
                }
            }

            if (hasBlockingIssues)
            {
                return BuildingLifecycleState.ValidationFailed;
            }

            if (!hasFloors)
            {
                return BuildingLifecycleState.Draft;
            }

            if (!string.IsNullOrWhiteSpace(project.metadata?.lastRuntimeExportUtc))
            {
                return BuildingLifecycleState.ReadyForExport;
            }

            return BuildingLifecycleState.ReadyForSimulation;
        }
    }
}
