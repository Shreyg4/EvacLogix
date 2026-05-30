using System;
using System.Globalization;
using System.IO;
using System.Linq;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Migrations;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxSavedProjectInfo
    {
        public string projectId = string.Empty;
        public string displayName = string.Empty;
        public string savedUtc = string.Empty;
        public string filePath = string.Empty;
    }

    public sealed class SandboxSaveLoadService : MonoBehaviour
    {
        [SerializeField] private string activeProjectId = string.Empty;
        [SerializeField] private string lastSavePath = string.Empty;
        [SerializeField] private string lastAutosavePath = string.Empty;
        [SerializeField] private string autosaveRootDirectory = string.Empty;
        [SerializeField] private string projectLibraryRootDirectory = string.Empty;
        [SerializeField] private string recoveryAutosavePath = string.Empty;
        [SerializeField] private string recoveryPromptMessage = string.Empty;
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private BuildingProjectData activeProject;
        [SerializeField] private float autosaveIntervalSeconds = 120f;
        [SerializeField] private int autosaveEditThreshold = 8;
        [SerializeField] private float elapsedSinceAutosave;
        [SerializeField] private int pendingDirtyEditCount;
        [SerializeField] private bool autosaveEnabled = true;
        [SerializeField] private bool hasUnsavedChanges;
        [SerializeField] private bool hasRecoveryPrompt;

        private SandboxProjectWorkspaceService workspaceService;
        private bool suppressDirtyTracking;

        public event Action<string> ActiveProjectChanged;
        public event Action<string> ProjectSaved;
        public event Action<string> ProjectAutosaved;
        public event Action<BuildingProjectData> ProjectLoaded;
        public event Action PersistenceStateChanged;

        public string ActiveProjectId => activeProjectId;
        public string LastSavePath => lastSavePath;
        public string LastAutosavePath => lastAutosavePath;
        public string AutosaveRootDirectory => autosaveRootDirectory;
        public string ProjectLibraryRootDirectory => projectLibraryRootDirectory;
        public string RecoveryAutosavePath => recoveryAutosavePath;
        public string RecoveryPromptMessage => recoveryPromptMessage;
        public string LastError => lastError;
        public BuildingProjectData ActiveProject => activeProject;
        public bool AutosaveEnabled => autosaveEnabled;
        public bool HasUnsavedChanges => hasUnsavedChanges;
        public bool HasRecoveryPrompt => hasRecoveryPrompt;
        public bool UsesBrowserPersistenceMode =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor;
#endif

        private void Awake()
        {
            autosaveRootDirectory = string.IsNullOrWhiteSpace(autosaveRootDirectory)
                ? Path.Combine(Application.persistentDataPath, "SandboxAutosaves")
                : autosaveRootDirectory;
            projectLibraryRootDirectory = string.IsNullOrWhiteSpace(projectLibraryRootDirectory)
                ? Path.Combine(Application.persistentDataPath, "SandboxProjects")
                : projectLibraryRootDirectory;

            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleWorkspaceProjectChanged;
            }

            EvaluateRecoveryPrompt();
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleWorkspaceProjectChanged;
            }
        }

        private void Update()
        {
            ProcessAutosaveTick(Time.unscaledDeltaTime);
        }

        public void SetActiveProject(BuildingProjectData project)
        {
            activeProject = project;
            SetActiveProjectId(project?.projectId ?? string.Empty);
            RaiseStateChanged();
        }

        public void SetActiveProjectId(string projectId)
        {
            if (string.Equals(activeProjectId, projectId, StringComparison.Ordinal))
            {
                return;
            }

            activeProjectId = projectId ?? string.Empty;
            ActiveProjectChanged?.Invoke(activeProjectId);
            RaiseStateChanged();
        }

        public void ConfigureAutosave(string rootDirectory, float intervalSeconds, int editThreshold, bool enabled = true)
        {
            autosaveRootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "SandboxAutosaves")
                : rootDirectory;
            autosaveIntervalSeconds = Mathf.Max(1f, intervalSeconds);
            autosaveEditThreshold = Mathf.Max(1, editThreshold);
            autosaveEnabled = enabled;
            RaiseStateChanged();
        }

        public void ConfigureProjectLibrary(string rootDirectory)
        {
            projectLibraryRootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Application.persistentDataPath, "SandboxProjects")
                : rootDirectory;
            RaiseStateChanged();
        }

        public void SetAutosaveEnabled(bool enabled)
        {
            if (autosaveEnabled == enabled)
            {
                return;
            }

            autosaveEnabled = enabled;
            RaiseStateChanged();
        }

        public void NotifySignificantEdit(int editWeight = 1)
        {
            if (activeProject == null || suppressDirtyTracking)
            {
                return;
            }

            activeProject.metadata.updatedUtc = DateTime.UtcNow.ToString("O");
            hasUnsavedChanges = true;
            pendingDirtyEditCount += Mathf.Max(1, editWeight);
            if (autosaveEnabled && pendingDirtyEditCount >= autosaveEditThreshold)
            {
                ForceAutosaveNow();
                return;
            }

            RaiseStateChanged();
        }

        public void ProcessAutosaveTick(float deltaSeconds)
        {
            if (!autosaveEnabled || !hasUnsavedChanges || activeProject == null)
            {
                return;
            }

            elapsedSinceAutosave += Mathf.Max(0f, deltaSeconds);
            if (elapsedSinceAutosave < autosaveIntervalSeconds)
            {
                return;
            }

            ForceAutosaveNow();
        }

        public string SerializeActiveProject(bool prettyPrint = true)
        {
            return SandboxProjectSerializer.Serialize(activeProject, prettyPrint);
        }

        public bool SaveActiveProjectToPath(string filePath, bool prettyPrint = true)
        {
            if (activeProject == null || string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                var timestamp = DateTime.UtcNow.ToString("O");
                activeProject.metadata.lastManualSaveUtc = timestamp;
                activeProject.metadata.updatedUtc = timestamp;
                WriteProjectToPath(filePath, activeProject, prettyPrint);
                lastSavePath = filePath;
                hasUnsavedChanges = false;
                pendingDirtyEditCount = 0;
                elapsedSinceAutosave = 0f;
                ClearAutosaveSnapshotForActiveProject();
                ClearError();
                ProjectSaved?.Invoke(filePath);
                EvaluateRecoveryPrompt();
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public bool SaveActiveProjectToLibrary(string projectName = "", bool prettyPrint = true)
        {
            if (activeProject == null)
            {
                return false;
            }

            var resolvedName = string.IsNullOrWhiteSpace(projectName)
                ? activeProject.metadata?.buildingName
                : projectName;
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                lastError = "Project name is required before saving.";
                RaiseStateChanged();
                return false;
            }

            activeProject.metadata ??= new ProjectMetadataData();
            activeProject.metadata.buildingName = resolvedName.Trim();
            SandboxProjectDataUtility.EnsureIds(activeProject);
            return SaveActiveProjectToPath(BuildLibraryProjectPath(activeProject.projectId), prettyPrint);
        }

        public BuildingProjectData LoadProjectFromLibrary(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return null;
            }

            return LoadProjectFromPath(BuildLibraryProjectPath(projectId), true);
        }

        public bool DeleteProjectFromLibrary(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return false;
            }

            try
            {
                var projectDirectory = BuildLibraryProjectDirectory(projectId);
                if (!Directory.Exists(projectDirectory))
                {
                    return false;
                }

                Directory.Delete(projectDirectory, true);
                if (string.Equals(activeProjectId, projectId, StringComparison.Ordinal))
                {
                    lastSavePath = string.Empty;
                    hasUnsavedChanges = true;
                }

                ClearError();
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public SandboxSavedProjectInfo[] GetSavedProjects()
        {
            if (string.IsNullOrWhiteSpace(projectLibraryRootDirectory) || !Directory.Exists(projectLibraryRootDirectory))
            {
                return Array.Empty<SandboxSavedProjectInfo>();
            }

            return Directory.GetFiles(projectLibraryRootDirectory, "project.json", SearchOption.AllDirectories)
                .Select(TryReadSavedProjectInfo)
                .Where(info => info != null)
                .OrderByDescending(info => TryParseUtc(info.savedUtc, out var savedUtc) ? savedUtc : DateTime.MinValue)
                .ToArray();
        }

        public bool ForceAutosaveNow()
        {
            if (!autosaveEnabled || activeProject == null)
            {
                return false;
            }

            try
            {
                activeProject.metadata.updatedUtc = DateTime.UtcNow.ToString("O");
                var autosavePath = BuildAutosavePath(activeProject.projectId);
                WriteProjectToPath(autosavePath, activeProject, true);
                lastAutosavePath = autosavePath;
                pendingDirtyEditCount = 0;
                elapsedSinceAutosave = 0f;
                ClearError();
                ProjectAutosaved?.Invoke(autosavePath);
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public BuildingProjectData LoadProjectFromPath(string filePath, bool setAsWorkingSavePath = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var project = SandboxProjectFileStorage.ReadProjectFromPath(filePath);
                ApplyLoadedProject(project, setAsWorkingSavePath ? filePath : lastSavePath, false);
                return project;
            }
            catch (Exception exception) when (exception is IOException || exception is SandboxMigrationException || exception is ArgumentException)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return null;
            }
        }

        public BuildingProjectData LoadProjectFromJson(string json, string workingSavePath = "", bool setAsWorkingSavePath = false)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                var project = SandboxProjectSerializer.Deserialize(json);
                ApplyLoadedProject(project, setAsWorkingSavePath ? workingSavePath : lastSavePath, false);
                hasUnsavedChanges = true;
                RaiseStateChanged();
                return project;
            }
            catch (Exception exception) when (exception is IOException || exception is SandboxMigrationException || exception is ArgumentException)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return null;
            }
        }

        public bool TryRestoreRecovery()
        {
            if (UsesBrowserPersistenceMode)
            {
                lastError = "Recovery snapshot restore is not offered in browser mode.";
                RaiseStateChanged();
                return false;
            }

            var autosavePath = recoveryAutosavePath;
            if (string.IsNullOrWhiteSpace(autosavePath) || !File.Exists(autosavePath))
            {
                return false;
            }

            try
            {
                var project = SandboxProjectFileStorage.ReadProjectFromPath(autosavePath);
                lastAutosavePath = autosavePath;
                ApplyLoadedProject(project, lastSavePath, true);
                hasRecoveryPrompt = false;
                recoveryAutosavePath = string.Empty;
                recoveryPromptMessage = string.Empty;
                hasUnsavedChanges = true;
                RaiseStateChanged();
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is SandboxMigrationException || exception is ArgumentException)
            {
                lastError = exception.Message;
                RaiseStateChanged();
                return false;
            }
        }

        public void DismissRecoveryPrompt()
        {
            if (!hasRecoveryPrompt)
            {
                return;
            }

            hasRecoveryPrompt = false;
            recoveryAutosavePath = string.Empty;
            recoveryPromptMessage = string.Empty;
            RaiseStateChanged();
        }

        public void EvaluateRecoveryPrompt()
        {
            if (UsesBrowserPersistenceMode)
            {
                hasRecoveryPrompt = false;
                recoveryAutosavePath = string.Empty;
                recoveryPromptMessage = "Browser mode keeps autosaves in local browser storage and does not surface recovery prompts.";
                RaiseStateChanged();
                return;
            }

            if (!TryFindLatestRecoveryCandidate(out var candidatePath, out var candidateProject, out var candidateTimestamp))
            {
                hasRecoveryPrompt = false;
                recoveryAutosavePath = string.Empty;
                recoveryPromptMessage = string.Empty;
                RaiseStateChanged();
                return;
            }

            if (activeProject != null &&
                !string.IsNullOrWhiteSpace(candidateProject.projectId) &&
                !string.Equals(candidateProject.projectId, activeProject.projectId, StringComparison.Ordinal))
            {
                hasRecoveryPrompt = false;
                recoveryAutosavePath = string.Empty;
                recoveryPromptMessage = string.Empty;
                RaiseStateChanged();
                return;
            }

            hasRecoveryPrompt = true;
            recoveryAutosavePath = candidatePath;
            recoveryPromptMessage =
                $"Recovery autosave available from {candidateTimestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}. Restore it before continuing if you want the newest unsaved edits.";
            RaiseStateChanged();
        }

        private void HandleWorkspaceProjectChanged(BuildingProjectData project)
        {
            if (project == null || suppressDirtyTracking)
            {
                return;
            }

            activeProject = project;
            SetActiveProjectId(project.projectId);
            NotifySignificantEdit();
        }

        private void ApplyLoadedProject(BuildingProjectData project, string workingSavePath, bool restoredFromRecovery)
        {
            suppressDirtyTracking = true;
            try
            {
                activeProject = project;
                SetActiveProjectId(project?.projectId ?? string.Empty);
                if (!restoredFromRecovery)
                {
                    lastSavePath = workingSavePath ?? string.Empty;
                }

                hasUnsavedChanges = restoredFromRecovery;
                pendingDirtyEditCount = 0;
                elapsedSinceAutosave = 0f;
                ClearError();
                ProjectLoaded?.Invoke(project);
            }
            finally
            {
                suppressDirtyTracking = false;
            }

            EvaluateRecoveryPrompt();
            RaiseStateChanged();
        }

        private bool TryFindLatestRecoveryCandidate(out string candidatePath, out BuildingProjectData candidateProject, out DateTime candidateTimestamp)
        {
            candidatePath = string.Empty;
            candidateProject = null;
            candidateTimestamp = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(autosaveRootDirectory) || !Directory.Exists(autosaveRootDirectory))
            {
                return false;
            }

            var autosavePaths = Directory.GetFiles(autosaveRootDirectory, "*.json", SearchOption.AllDirectories);
            for (var i = 0; i < autosavePaths.Length; i += 1)
            {
                try
                {
                    var project = SandboxProjectFileStorage.ReadProjectFromPath(autosavePaths[i]);
                    if (!TryResolveRecoveryTimestamp(project, out var updatedUtc) || !IsRecoveryNewerThanManualSave(project, updatedUtc))
                    {
                        continue;
                    }

                    if (updatedUtc <= candidateTimestamp)
                    {
                        continue;
                    }

                    candidatePath = autosavePaths[i];
                    candidateProject = project;
                    candidateTimestamp = updatedUtc;
                }
                catch
                {
                    continue;
                }
            }

            return candidateProject != null;
        }

        private string BuildAutosavePath(string projectId)
        {
            var safeProjectId = string.IsNullOrWhiteSpace(projectId) ? "unsaved-project" : projectId;
            return Path.Combine(autosaveRootDirectory, safeProjectId, "autosave.json");
        }

        private string BuildLibraryProjectDirectory(string projectId)
        {
            var safeProjectId = string.IsNullOrWhiteSpace(projectId) ? "unsaved-project" : projectId;
            return Path.Combine(projectLibraryRootDirectory, safeProjectId);
        }

        private string BuildLibraryProjectPath(string projectId)
        {
            return Path.Combine(BuildLibraryProjectDirectory(projectId), "project.json");
        }

        private void ClearAutosaveSnapshotForActiveProject()
        {
            var autosavePath = BuildAutosavePath(activeProjectId);
            if (File.Exists(autosavePath))
            {
                File.Delete(autosavePath);
            }

            if (string.Equals(lastAutosavePath, autosavePath, StringComparison.Ordinal))
            {
                lastAutosavePath = string.Empty;
            }

            if (string.Equals(recoveryAutosavePath, autosavePath, StringComparison.Ordinal))
            {
                recoveryAutosavePath = string.Empty;
                recoveryPromptMessage = string.Empty;
                hasRecoveryPrompt = false;
            }
        }

        private static void WriteProjectToPath(string filePath, BuildingProjectData project, bool prettyPrint)
        {
            SandboxProjectFileStorage.WriteProjectToPath(filePath, project, prettyPrint);
        }

        private static bool TryResolveRecoveryTimestamp(BuildingProjectData project, out DateTime updatedUtc)
        {
            updatedUtc = DateTime.MinValue;
            var timestamp = project?.metadata?.updatedUtc;
            return !string.IsNullOrWhiteSpace(timestamp) &&
                   DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updatedUtc);
        }

        private static bool TryParseUtc(string timestamp, out DateTime parsed)
        {
            return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
        }

        private static SandboxSavedProjectInfo TryReadSavedProjectInfo(string path)
        {
            try
            {
                var project = SandboxProjectFileStorage.ReadProjectFromPath(path);
                return new SandboxSavedProjectInfo
                {
                    projectId = project.projectId,
                    displayName = string.IsNullOrWhiteSpace(project.metadata?.buildingName)
                        ? "Untitled Project"
                        : project.metadata.buildingName,
                    savedUtc = string.IsNullOrWhiteSpace(project.metadata?.lastManualSaveUtc)
                        ? project.metadata?.updatedUtc ?? string.Empty
                        : project.metadata.lastManualSaveUtc,
                    filePath = path,
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool IsRecoveryNewerThanManualSave(BuildingProjectData project, DateTime updatedUtc)
        {
            var lastManualSaveUtc = project?.metadata?.lastManualSaveUtc;
            if (string.IsNullOrWhiteSpace(lastManualSaveUtc))
            {
                return true;
            }

            return !DateTime.TryParse(lastManualSaveUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var manualSaveTimestamp) ||
                   updatedUtc > manualSaveTimestamp;
        }

        private void ClearError()
        {
            lastError = string.Empty;
        }

        private void RaiseStateChanged()
        {
            PersistenceStateChanged?.Invoke();
        }
    }
}
