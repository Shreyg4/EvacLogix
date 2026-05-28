using System;
using System.IO;
using EvacLogix.Sandbox.Data;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxBlueprintImportService : MonoBehaviour
    {
        private const string ManagedBlueprintDirectory = "Assets/Art/Blueprints/Sandbox";

        public bool CanImport(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                return false;
            }

            var extension = Path.GetExtension(sourceFilePath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase);
        }

        public BlueprintReferenceData ImportBlueprint(string sourceFilePath)
        {
            if (!CanImport(sourceFilePath))
            {
                throw new InvalidOperationException($"Unsupported blueprint import path '{sourceFilePath}'.");
            }

            return ImportBlueprint(Path.GetFileName(sourceFilePath), File.ReadAllBytes(sourceFilePath));
        }

        public BlueprintReferenceData ImportBlueprint(string sourceFileName, byte[] fileBytes)
        {
            if (string.IsNullOrWhiteSpace(sourceFileName))
            {
                throw new InvalidOperationException("Blueprint import requires a file name.");
            }

            if (fileBytes == null || fileBytes.Length == 0)
            {
                throw new InvalidOperationException($"Blueprint import payload for '{sourceFileName}' was empty.");
            }

            var extension = Path.GetExtension(sourceFileName);
            if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported blueprint import file '{sourceFileName}'.");
            }

            var fileName = Path.GetFileName(sourceFileName);
            var assetGuid = string.Empty;
            var managedAssetPath = string.Empty;
#if UNITY_EDITOR
            var managedBlueprintDiskDirectory = Path.Combine(Application.dataPath, "Art/Blueprints/Sandbox");
            Directory.CreateDirectory(managedBlueprintDiskDirectory);

            managedAssetPath = Path.Combine(ManagedBlueprintDirectory, fileName).Replace("\\", "/");
            var managedDiskPath = Path.Combine(managedBlueprintDiskDirectory, fileName);

            if (File.Exists(managedDiskPath))
            {
                var timestampPrefix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                managedAssetPath = Path.Combine(ManagedBlueprintDirectory, $"{timestampPrefix}_{fileName}").Replace("\\", "/");
                managedDiskPath = Path.Combine(managedBlueprintDiskDirectory, $"{timestampPrefix}_{fileName}");
            }

            File.WriteAllBytes(managedDiskPath, fileBytes);
            AssetDatabase.ImportAsset(managedAssetPath, ImportAssetOptions.ForceUpdate);
            assetGuid = AssetDatabase.AssetPathToGUID(managedAssetPath);
#endif

            return new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetGuid = assetGuid,
                assetPath = managedAssetPath,
                sourceFileName = fileName,
                sourceMimeType = ResolveMimeTypeFromExtension(extension),
                importedPayloadBase64 = Convert.ToBase64String(fileBytes),
                opacity = 1f,
                displayScale = 1f,
                isVisible = true,
            };
        }

        private static string ResolveMimeTypeFromExtension(string extension)
        {
            if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                return "image/png";
            }

            if (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return "image/jpeg";
            }

            if (string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase))
            {
                return "image/webp";
            }

            if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase))
            {
                return "image/bmp";
            }

            if (string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                return "image/gif";
            }

            if (string.Equals(extension, ".tif", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".tiff", StringComparison.OrdinalIgnoreCase))
            {
                return "image/tiff";
            }

            return "application/octet-stream";
        }
    }
}
