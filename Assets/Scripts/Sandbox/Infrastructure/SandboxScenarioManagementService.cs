using System;
using System.Collections.Generic;
using System.Linq;
using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxScenarioManagementService : MonoBehaviour
    {
        private SandboxProjectWorkspaceService workspaceService;
        private SandboxCommandHistory commandHistory;
        private SandboxValidationService validationService;
        private SandboxPreviewService previewService;

        public event Action ScenariosChanged;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            commandHistory = GetComponent<SandboxCommandHistory>();
            validationService = GetComponent<SandboxValidationService>();
            previewService = GetComponent<SandboxPreviewService>();
        }

        public IReadOnlyList<ScenarioPresetData> GetScenarioPresets()
        {
            return workspaceService?.ActiveProject == null
                ? Array.Empty<ScenarioPresetData>()
                : workspaceService.ActiveProject.scenarioPresets.ToList();
        }

        public bool RenameScenarioPreset(string scenarioPresetId, string name)
        {
            return UpdateScenarioPreset(scenarioPresetId, name, null, null, null, null);
        }

        public bool CreateScenarioPreset(
            string name,
            IEnumerable<string> spawnLayoutIds,
            IEnumerable<string> fireOriginIds,
            PreviewParameterData previewParameters,
            out string scenarioPresetId)
        {
            scenarioPresetId = string.Empty;
            if (workspaceService?.ActiveProject == null)
            {
                return false;
            }

            var createdScenarioPresetId = SandboxId.NewId();
            var beforeProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            afterProject.scenarioPresets.Add(new ScenarioPresetData
            {
                scenarioPresetId = createdScenarioPresetId,
                name = string.IsNullOrWhiteSpace(name) ? "Preview Scenario" : name.Trim(),
                spawnLayoutIds = NormalizeIds(spawnLayoutIds),
                fireOriginIds = NormalizeIds(fireOriginIds),
                previewParameters = ClonePreviewParameters(previewParameters)
            });
            SandboxProjectDataUtility.EnsureIds(afterProject);

            ExecuteScenarioMutation("Create Scenario Preset", beforeProject, afterProject);
            scenarioPresetId = createdScenarioPresetId;
            return true;
        }

        public bool UpdateScenarioPreset(
            string scenarioPresetId,
            string name,
            IEnumerable<string> spawnLayoutIds,
            IEnumerable<string> fireOriginIds,
            PreviewParameterData previewParameters,
            IEnumerable<MetadataFieldData> metadataFields)
        {
            if (workspaceService?.ActiveProject == null || string.IsNullOrWhiteSpace(scenarioPresetId))
            {
                return false;
            }

            var beforeProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var afterProject = SandboxProjectSerializer.Clone(workspaceService.ActiveProject);
            var scenarioPreset = afterProject.scenarioPresets.FirstOrDefault(candidate =>
                string.Equals(candidate.scenarioPresetId, scenarioPresetId, StringComparison.Ordinal));
            if (scenarioPreset == null)
            {
                return false;
            }

            if (name != null)
            {
                scenarioPreset.name = string.IsNullOrWhiteSpace(name) ? scenarioPreset.name : name.Trim();
            }

            if (spawnLayoutIds != null)
            {
                scenarioPreset.spawnLayoutIds = NormalizeIds(spawnLayoutIds);
            }

            if (fireOriginIds != null)
            {
                scenarioPreset.fireOriginIds = NormalizeIds(fireOriginIds);
            }

            if (previewParameters != null)
            {
                scenarioPreset.previewParameters = ClonePreviewParameters(previewParameters);
            }

            if (metadataFields != null)
            {
                scenarioPreset.metadataFields = CloneMetadata(metadataFields);
            }

            SandboxProjectDataUtility.EnsureIds(afterProject);
            ExecuteScenarioMutation("Update Scenario Preset", beforeProject, afterProject);
            return true;
        }

        public bool ApplyScenarioPreset(string scenarioPresetId)
        {
            if (string.IsNullOrWhiteSpace(scenarioPresetId))
            {
                return false;
            }

            return previewService != null && previewService.SetActiveScenarioPreset(scenarioPresetId);
        }

        private void ExecuteScenarioMutation(string description, BuildingProjectData beforeProject, BuildingProjectData afterProject)
        {
            void Apply(BuildingProjectData project)
            {
                workspaceService.SetActiveProject(project);
                validationService?.ValidateActiveProject();
                previewService?.NotifyPreviewInputsChanged();
                ScenariosChanged?.Invoke();
            }

            if (commandHistory == null)
            {
                Apply(afterProject);
                return;
            }

            commandHistory.Execute(new DelegateSandboxEditorCommand(
                description,
                () => Apply(SandboxProjectSerializer.Clone(afterProject)),
                () => Apply(SandboxProjectSerializer.Clone(beforeProject))));
        }

        private static List<string> NormalizeIds(IEnumerable<string> ids)
        {
            return ids == null
                ? new List<string>()
                : ids
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
        }

        private static PreviewParameterData ClonePreviewParameters(PreviewParameterData previewParameters)
        {
            return previewParameters == null
                ? new PreviewParameterData()
                : new PreviewParameterData
                {
                    spreadIntensity = Mathf.Max(0.1f, previewParameters.spreadIntensity),
                    startDelaySeconds = Mathf.Max(0f, previewParameters.startDelaySeconds),
                    previewAgentCap = Mathf.Max(1, previewParameters.previewAgentCap)
                };
        }

        private static List<MetadataFieldData> CloneMetadata(IEnumerable<MetadataFieldData> metadataFields)
        {
            return metadataFields == null
                ? new List<MetadataFieldData>()
                : metadataFields
                    .Where(field => field != null)
                    .Select(field => new MetadataFieldData
                    {
                        key = field.key ?? string.Empty,
                        value = field.value ?? string.Empty
                    })
                    .ToList();
        }
    }
}
