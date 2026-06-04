using System;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Data.Serialization;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    // Crash-survival half of the memory-safety feature. Mirrors the active project (payload-stripped) to
    // browser localStorage on a debounce, installs a JS onAbort hook that downloads the snapshot if the
    // wasm heap aborts anyway, and offers a one-time recovery prompt on the next load. The snapshot omits
    // the blueprint image bytes (re-importable), so it stays small enough to write often and store.
    public sealed class SandboxRecoverySnapshotService : MonoBehaviour
    {
        private const string RecoveryKey = "EvacLogix.Recovery.latest";
        private const string RecoveryFileName = "evaclogix-recovery.json";
        private const float DebounceSeconds = 3f;

        private SandboxProjectWorkspaceService workspaceService;
        private SandboxSaveLoadService saveLoadService;

        private bool dirty;
        private float dirtySince;
        private bool subscribed;
        private bool saveSubscribed;

        private RecoveryEnvelope pendingRecovery;
        private bool recoveryPromptResolved;
        private GUIStyle promptStyle;

        public bool HasPendingRecovery => pendingRecovery != null && !recoveryPromptResolved;

        [Serializable]
        private sealed class RecoveryEnvelope
        {
            public int version = 1;
            public string projectId = string.Empty;
            public long savedAtUnixSeconds;
            public string projectJson = string.Empty;
        }

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleActiveProjectChanged;
                subscribed = true;
            }
        }

        private void Start()
        {
            // Clear the recovery snapshot on a clean save so the prompt only appears for work that is
            // genuinely newer than the last save (or a crash), not after every session. Resolved in Start
            // so the save/load service is guaranteed to exist.
            saveLoadService = FindAnyObjectByType<SandboxSaveLoadService>();
            if (saveLoadService != null)
            {
                saveLoadService.ProjectSaved += HandleProjectSaved;
                saveSubscribed = true;
            }

            if (!SandboxBrowserMemoryBridge.IsSupported)
            {
                return;
            }

            // Capture any snapshot left from a previous (possibly crashed) session BEFORE editing
            // overwrites it, then arm the abort hook for this session.
            pendingRecovery = TryReadStoredRecovery();
            SandboxBrowserMemoryBridge.InstallAbortHandler(RecoveryKey);
        }

        private void HandleProjectSaved(string filePath)
        {
            dirty = false;
            SandboxBrowserMemoryBridge.ClearRecovery(RecoveryKey);
        }

        private void OnDestroy()
        {
            if (subscribed && workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleActiveProjectChanged;
            }

            if (saveSubscribed && saveLoadService != null)
            {
                saveLoadService.ProjectSaved -= HandleProjectSaved;
            }
        }

        private void Update()
        {
            if (dirty && Time.unscaledTime - dirtySince >= DebounceSeconds)
            {
                WriteSnapshot();
            }
        }

        // Writes the snapshot right now (used by the memory guard when entering danger mode, so the latest
        // state is persisted before any reclaim).
        public void ForceFlush()
        {
            if (dirty)
            {
                WriteSnapshot();
            }
        }

        // Always downloads the current snapshot as a file (the "Download backup now" action).
        public void DownloadNow()
        {
            var json = BuildEnvelopeJson();
            if (!string.IsNullOrEmpty(json))
            {
                SandboxBrowserMemoryBridge.DownloadJson(RecoveryFileName, json);
            }
        }

        // Loads the pending recovery snapshot into the workspace. Geometry is restored intact; the
        // blueprint image must be re-imported (its bytes are not stored in the snapshot).
        public bool AcceptPendingRecovery()
        {
            recoveryPromptResolved = true;
            var envelope = pendingRecovery;
            pendingRecovery = null;
            if (envelope == null || string.IsNullOrWhiteSpace(envelope.projectJson) || workspaceService == null)
            {
                return false;
            }

            BuildingProjectData project;
            try
            {
                project = SandboxProjectSerializer.Deserialize(envelope.projectJson);
            }
            catch
            {
                return false;
            }

            if (project == null)
            {
                return false;
            }

            workspaceService.SetActiveProject(project);
            SandboxBrowserMemoryBridge.ClearRecovery(RecoveryKey);
            return true;
        }

        public void DiscardPendingRecovery()
        {
            recoveryPromptResolved = true;
            pendingRecovery = null;
            SandboxBrowserMemoryBridge.ClearRecovery(RecoveryKey);
        }

        private void HandleActiveProjectChanged(BuildingProjectData project)
        {
            dirty = true;
            dirtySince = Time.unscaledTime;
        }

        private void WriteSnapshot()
        {
            dirty = false;
            var json = BuildEnvelopeJson();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            SandboxBrowserMemoryBridge.WriteRecovery(RecoveryKey, json);
        }

        private string BuildEnvelopeJson()
        {
            var project = workspaceService?.ActiveProject;
            if (project == null)
            {
                return string.Empty;
            }

            var envelope = new RecoveryEnvelope
            {
                projectId = project.projectId ?? string.Empty,
                savedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                projectJson = SandboxProjectSnapshot.CaptureWithoutPayloads(project)
            };
            return JsonUtility.ToJson(envelope);
        }

        private static RecoveryEnvelope TryReadStoredRecovery()
        {
            var raw = SandboxBrowserMemoryBridge.ReadRecovery(RecoveryKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                var envelope = JsonUtility.FromJson<RecoveryEnvelope>(raw);
                return envelope != null && !string.IsNullOrWhiteSpace(envelope.projectJson) ? envelope : null;
            }
            catch
            {
                return null;
            }
        }

        // Minimal self-contained recovery prompt. (Can later be folded into the unified recovery UI used
        // by the desktop file backend.)
        private void OnGUI()
        {
            if (!HasPendingRecovery)
            {
                return;
            }

            promptStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(12, 12, 10, 10)
            };

            const float width = 360f;
            const float height = 150f;
            var rect = new Rect((Screen.width - width) * 0.5f, 24f, width, height);
            GUILayout.BeginArea(rect, GUIContent.none, promptStyle);
            var when = DateTimeOffset.FromUnixTimeSeconds(pendingRecovery.savedAtUnixSeconds).LocalDateTime;
            GUILayout.Label($"Recover unsaved work from {when:g}? The blueprint image will need to be re-imported.");
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recover", GUILayout.Height(28f)))
            {
                AcceptPendingRecovery();
            }

            if (GUILayout.Button("Discard", GUILayout.Height(28f)))
            {
                DiscardPendingRecovery();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
