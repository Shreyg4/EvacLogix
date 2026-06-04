using EvacLogix.Sandbox.Authoring.Commands;
using EvacLogix.Sandbox.Rendering;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    // Proactive half of the memory-safety feature (WebGL). Polls the real wasm heap size and, before it
    // can hit the OOM abort, reclaims the heaviest disposable memory (undo history, then blueprint texture
    // cache) so freed space is reused instead of forcing the heap growth that aborts. Surfaces a banner so
    // the user knows to save and reload. Inert on non-WebGL (heap probe returns 0).
    public sealed class SandboxMemoryGuardService : MonoBehaviour
    {
        [SerializeField] private float maxHeapMegabytes = 2048f;
        [SerializeField] private float dangerEnterFraction = 0.85f;
        [SerializeField] private float dangerExitFraction = 0.70f;
        [SerializeField] private float escalateFraction = 0.93f;
        [SerializeField] private float pollIntervalSeconds = 1.5f;

        private SandboxCommandHistory commandHistory;
        private SandboxRecoverySnapshotService recoveryService;
        private SandboxBlueprintOverlayRenderer blueprintRenderer;

        private float nextPollTime;
        private bool inDanger;
        private bool escalated;
        private GUIStyle bannerStyle;
        private GUIStyle bannerButtonStyle;

        private void Awake()
        {
            commandHistory = FindAnyObjectByType<SandboxCommandHistory>();
            recoveryService = FindAnyObjectByType<SandboxRecoverySnapshotService>();
            blueprintRenderer = FindAnyObjectByType<SandboxBlueprintOverlayRenderer>();
        }

        private void Update()
        {
            if (!SandboxBrowserMemoryBridge.IsSupported || Time.unscaledTime < nextPollTime)
            {
                return;
            }

            nextPollTime = Time.unscaledTime + Mathf.Max(0.25f, pollIntervalSeconds);

            var cap = Mathf.Max(1f, maxHeapMegabytes) * 1024f * 1024f;
            var heapBytes = SandboxBrowserMemoryBridge.GetHeapBytes();
            if (heapBytes <= 0L)
            {
                return;
            }

            var fraction = heapBytes / cap;
            if (!inDanger)
            {
                if (fraction >= dangerEnterFraction)
                {
                    EnterDanger();
                }

                return;
            }

            if (fraction < dangerExitFraction)
            {
                inDanger = false;
                escalated = false;
                return;
            }

            if (!escalated && fraction >= escalateFraction)
            {
                Escalate();
            }
        }

        private void EnterDanger()
        {
            inDanger = true;
            escalated = false;

            // 1) Persist the latest work before touching anything.
            recoveryService?.ForceFlush();

            // 2) Reclaim the biggest disposable chunk: the undo/redo history (its snapshots are exactly
            //    what accumulates while building). Freeing live bytes lets allocations reuse the grown
            //    buffer instead of forcing the growth that aborts.
            commandHistory?.Clear();
            System.GC.Collect();
        }

        private void Escalate()
        {
            escalated = true;
            blueprintRenderer?.FlushTextureCache();
            System.GC.Collect();
        }

        private void OnGUI()
        {
            if (!inDanger)
            {
                return;
            }

            bannerStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 14, 10, 10),
                normal = { textColor = Color.white }
            };
            bannerButtonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = 13 };

            var rect = new Rect(0f, 0f, Screen.width, 46f);
            var previousColor = GUI.color;
            GUI.color = new Color(0.48f, 0.11f, 0.11f, 0.96f);
            GUILayout.BeginArea(rect, GUIContent.none, bannerStyle);
            GUI.color = previousColor;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Memory is critically low. Undo history was cleared and a recovery copy saved. Save your project and reload soon.",
                bannerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Download backup now", bannerButtonStyle, GUILayout.Width(180f), GUILayout.Height(26f)))
            {
                recoveryService?.DownloadNow();
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
