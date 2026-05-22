using System.IO;
using EvacLogix.Sandbox.Data;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxPreviewImageExportService : MonoBehaviour
    {
        private SandboxProjectWorkspaceService workspaceService;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
        }

        public bool TryExportActiveBlueprintPreview(string destinationPath)
        {
            if (workspaceService?.ActiveFloor == null || string.IsNullOrWhiteSpace(destinationPath))
            {
                return false;
            }

            var blueprintReference = workspaceService.FindBlueprintReference(workspaceService.ActiveFloor.blueprintReferenceId);
            if (blueprintReference == null || string.IsNullOrWhiteSpace(blueprintReference.assetPath))
            {
                return false;
            }

#if UNITY_EDITOR
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(blueprintReference.assetPath);
            if (texture == null)
            {
                return false;
            }

            var bytes = texture.EncodeToPNG();
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(destinationPath, bytes);
            workspaceService.ActiveProject.metadata.lastPreviewImageExportUtc = System.DateTime.UtcNow.ToString("O");
            return true;
#else
            return false;
#endif
        }
    }
}
