using System.Runtime.InteropServices;

namespace EvacLogix.Sandbox.Infrastructure
{
    public enum SandboxRecoveryWriteResult
    {
        Failed = 0,
        StoredInLocalStorage = 1,
        Downloaded = 2,
        Unsupported = 3
    }

    // Thin, safe wrapper over the WebGL .jslib bridge for memory monitoring and crash-survival recovery.
    // On non-WebGL platforms (editor/desktop) every call is a no-op so the rest of the code can stay
    // platform-agnostic.
    public static class SandboxBrowserMemoryBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern double EvacLogixSandboxBridge_GetHeapBytes();
        [DllImport("__Internal")] private static extern int EvacLogixSandboxBridge_WriteRecovery(string key, string json);
        [DllImport("__Internal")] private static extern string EvacLogixSandboxBridge_ReadRecovery(string key);
        [DllImport("__Internal")] private static extern void EvacLogixSandboxBridge_ClearRecovery(string key);
        [DllImport("__Internal")] private static extern void EvacLogixSandboxBridge_DownloadJson(string fileName, string json);
        [DllImport("__Internal")] private static extern void EvacLogixSandboxBridge_InstallAbortHandler(string key);
#endif

        public static bool IsSupported =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        // Current wasm heap size in bytes, or 0 when unavailable (non-WebGL).
        public static long GetHeapBytes()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return (long)EvacLogixSandboxBridge_GetHeapBytes();
#else
            return 0L;
#endif
        }

        public static SandboxRecoveryWriteResult WriteRecovery(string key, string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return (SandboxRecoveryWriteResult)EvacLogixSandboxBridge_WriteRecovery(key ?? string.Empty, json ?? string.Empty);
#else
            return SandboxRecoveryWriteResult.Unsupported;
#endif
        }

        public static string ReadRecovery(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return EvacLogixSandboxBridge_ReadRecovery(key ?? string.Empty) ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        public static void ClearRecovery(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EvacLogixSandboxBridge_ClearRecovery(key ?? string.Empty);
#endif
        }

        public static void DownloadJson(string fileName, string json)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EvacLogixSandboxBridge_DownloadJson(fileName ?? "evaclogix-recovery.json", json ?? string.Empty);
#endif
        }

        public static void InstallAbortHandler(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            EvacLogixSandboxBridge_InstallAbortHandler(key ?? string.Empty);
#endif
        }
    }
}
