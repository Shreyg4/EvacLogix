using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxProjectMetadataService : MonoBehaviour
    {
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxValidationService validationService;
        private SandboxPreviewService previewService;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            validationService = GetComponent<SandboxValidationService>();
            previewService = GetComponent<SandboxPreviewService>();
        }

        public bool UpdateProjectMetadata(
            string buildingName,
            string description,
            string authorName,
            IEnumerable<MetadataFieldData> customFields,
            DistanceUnit? distanceUnit = null)
        {
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            var beforeProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            afterProject.metadata ??= new ProjectMetadataData();
            afterProject.metadata.buildingName = Sanitize(buildingName);
            afterProject.metadata.description = Sanitize(description);
            afterProject.metadata.authorName = Sanitize(authorName);
            afterProject.metadata.distanceUnit = SandboxDistanceUnitUtility.Normalize(
                distanceUnit ?? afterProject.metadata.distanceUnit);
            afterProject.metadata.customFields = CloneMetadataFields(customFields);
            SandboxProjectDataUtility.EnsureIds(afterProject);

            void Apply(BuildingProjectData project)
            {
                workspaceService.SetActiveProject(project);
                validationService?.ValidateActiveProject();
                previewService?.NotifyPreviewInputsChanged();
            }

            if (commandHistory == null)
            {
                Apply(afterProject);
                return true;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                "Update Project Metadata",
                () => Apply(SandboxProjectSerializer.Clone(afterProject)),
                () => Apply(SandboxProjectSerializer.Clone(beforeProject))));
            return true;
        }

        public bool SetDistanceUnit(DistanceUnit distanceUnit)
        {
            if (workspaceService?.ActiveProject?.metadata == null)
            {
                return false;
            }

            return UpdateProjectMetadata(
                workspaceService.ActiveProject.metadata.buildingName,
                workspaceService.ActiveProject.metadata.description,
                workspaceService.ActiveProject.metadata.authorName,
                workspaceService.ActiveProject.metadata.customFields,
                distanceUnit);
        }

        private static string Sanitize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static List<MetadataFieldData> CloneMetadataFields(IEnumerable<MetadataFieldData> metadataFields)
        {
            return metadataFields == null
                ? new List<MetadataFieldData>()
                : metadataFields
                    .Where(field => field != null)
                    .Select(field => new MetadataFieldData
                    {
                        key = Sanitize(field.key),
                        value = Sanitize(field.value)
                    })
                    .ToList();
        }
    }
}
