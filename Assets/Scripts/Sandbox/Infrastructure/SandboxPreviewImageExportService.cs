using System.IO;
using System;
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
        private SandboxValidationService validationService;

        private void Awake()
        {
            workspaceService = GetComponent<SandboxProjectWorkspaceService>();
            validationService = GetComponent<SandboxValidationService>();
        }

        public bool TryExportActiveBlueprintPreview(string destinationPath)
        {
            if (workspaceService?.ActiveFloor == null || string.IsNullOrWhiteSpace(destinationPath))
            {
                return false;
            }

            if (validationService != null && !validationService.CanPreviewOrExport())
            {
                return false;
            }

            var blueprintReference = workspaceService.FindBlueprintReference(workspaceService.ActiveFloor.blueprintReferenceId);
            if (blueprintReference == null)
            {
                return false;
            }

            var texture = ResolvePreviewTexture(blueprintReference);
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
        }

        private static Texture2D ResolvePreviewTexture(BlueprintReferenceData blueprintReference)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(blueprintReference.assetPath))
            {
                var editorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(blueprintReference.assetPath);
                if (editorTexture != null)
                {
                    return editorTexture;
                }
            }
#endif

            if (string.IsNullOrWhiteSpace(blueprintReference.importedPayloadBase64))
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blueprintReference.importedPayloadBase64);
            }
            catch
            {
                return null;
            }

            if (bytes.Length == 0)
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(bytes, false) ? texture : null;
        }
    }
}
